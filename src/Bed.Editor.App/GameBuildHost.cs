using System.Collections.Immutable;
using System.Runtime.InteropServices;
namespace Bed.Editor.App;

/// <summary>Single-use supervised build with explicit saved-state input and caller-declared staging allowlist.
/// Caller supplies the captured saved revision; this host does not save drafts or infer live freshness.</summary>
public sealed class GameBuildHost : IDisposable
{
    internal string? OwnedExportFolder {get;init;}
    internal Action<GameBuildLog>? PersistentLog {get;init;}
    private readonly object gate=new();
    private readonly GameBuildSpec spec;
    private readonly string root,revision;
    private ExportStaging? staging;
    private readonly BuildOutputDeclaration? declaration;
    private readonly Guid attempt;
    private readonly CancellationTokenSource inventoryCancellation=new();
    private readonly string? priorGood;
    private readonly GameExportPlan? exportPlan;
    /// <summary>Pinned post-build output manifest, available only after a successful inventory.</summary>
    public GameContentManifest? OutputManifest {get;private set;}
    private readonly int maxLine,maxLogs;
    private readonly TimeSpan timeout;
    private readonly Queue<GameBuildLog> logs=[];
    private SupervisedProcess? child;
    private bool started,canceled,finished;
    private string? fault;
    /// <summary>Validates declarations without reading project files or starting a child. Unsaved input is refused.</summary>
    public GameBuildHost(GameBuildSpec spec,string projectRoot,string savedRevision,bool hasUnsavedChanges,ExportStaging staging,
        int maxLineCharacters,int maxPendingLines,TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(spec);ArgumentNullException.ThrowIfNull(staging);
        if(hasUnsavedChanges)throw new InvalidOperationException("Save changes before building.");
        if(!Path.IsPathFullyQualified(projectRoot)||string.IsNullOrWhiteSpace(savedRevision)||savedRevision.Length>256)throw new ArgumentException("Declare an absolute project root and saved revision.");
        if(maxLineCharacters<=0||maxPendingLines<=0||timeout<=TimeSpan.Zero||timeout>TimeSpan.FromDays(1))throw new ArgumentOutOfRangeException(nameof(timeout));
        this.spec=spec;root=Path.GetFullPath(projectRoot);revision=savedRevision;this.staging=staging;attempt=staging.Id;maxLine=maxLineCharacters;maxLogs=maxPendingLines;this.timeout=timeout;
    }
    /// <summary>Admits source receipts and a byte-independent output declaration; inventory is pinned only after zero exit.</summary>
    public GameBuildHost(GameBuildSpec spec,string projectRoot,string savedRevision,bool hasUnsavedChanges,BuildOutputDeclaration declaration,
        int maxLineCharacters,int maxPendingLines,TimeSpan timeout,string? priorGoodManifestHash=null)
    {
        ArgumentNullException.ThrowIfNull(spec);ArgumentNullException.ThrowIfNull(declaration);
        if(hasUnsavedChanges)throw new InvalidOperationException("Save changes before building.");
        if(!Path.IsPathFullyQualified(projectRoot)||string.IsNullOrWhiteSpace(savedRevision)||savedRevision.Length>256)throw new ArgumentException("Declare an absolute project root and saved revision.");
        if(savedRevision!=declaration.Source.InventoryRevision)throw new ArgumentException("Saved revision differs from the admitted source inventory.");
        if(maxLineCharacters<=0||maxPendingLines<=0||timeout<=TimeSpan.Zero||timeout>TimeSpan.FromDays(1))throw new ArgumentOutOfRangeException(nameof(timeout));
        if(priorGoodManifestHash is not null && (priorGoodManifestHash.Length!=64||!priorGoodManifestHash.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f')))throw new ArgumentException("Invalid prior-good manifest hash.");
        this.spec=spec;root=Path.GetFullPath(projectRoot);revision=savedRevision;this.declaration=declaration;
        attempt=Guid.NewGuid();priorGood=priorGoodManifestHash;maxLine=maxLineCharacters;maxLogs=maxPendingLines;this.timeout=timeout;
    }
    /// <summary>Export-only route: requires every captured source file at the same declared output path and an explicit captured-input command.</summary>
    public GameBuildHost(GameBuildSpec spec,string projectRoot,bool hasUnsavedChanges,GameExportPlan plan,
        int maxLineCharacters,int maxPendingLines,TimeSpan timeout)
        :this(spec,projectRoot,plan.Admission.SnapshotHash,hasUnsavedChanges,plan.Outputs,maxLineCharacters,maxPendingLines,timeout)
    {
        if(!spec.AcceptsSavedInput)throw new InvalidOperationException("This game's build does not take BED's saved level.");
        if(!plan.Admission.Source.Nodes.All(n=>plan.Outputs.Paths.Contains(n.Output.Destination,StringComparer.Ordinal)))throw new InvalidOperationException("Every captured Saved file must be declared at the same output path.");
        exportPlan=plan;
    }
    /// <summary>Observed completion after Start; no publication or prior-good replacement occurs.</summary>
    public Task<GameBuildReceipt>? Completion {get;private set;}
    /// <summary>Starts one declared build in a fresh owned output folder, with no stdin protocol.</summary>
    public void Start()
    {
        lock(gate){if(started||canceled)throw new InvalidOperationException("Build host is single-use.");started=true;}
        Completion=Run();
    }
    /// <summary>Drains bounded plain logs; this does not drive process lifetime.</summary>
    public ImmutableArray<GameBuildLog> DrainLogs(){lock(gate){var result=logs.ToImmutableArray();logs.Clear();return result;}}
    private void Log(string text,bool error)
    {
        try{PersistentLog?.Invoke(new(error,text));}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Fault("The export log could not be written: "+e.Message);child?.AbortOwned();return;}
        lock(gate){if(logs.Count>=maxLogs){fault??="Build output exceeded its pending-line budget.";child?.AbortOwned();return;}logs.Enqueue(new(error,text));}}
    private void Fault(string why){lock(gate){if(!canceled)fault??=why;}}
    /// <summary>Cancels only this attempt and owned Job. No directory or prior export is deleted.</summary>
    public void Cancel(){lock(gate){if(finished)return;canceled=true;inventoryCancellation.Cancel();child?.AbortOwned();staging?.Cancel(attempt);}}
    private async Task<GameBuildReceipt> Run()
    {
        string output="";int? code=null;
        try
        {
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Build supervision needs Windows for now");
            output=PrepareOutput();
            string? input=null;
            if(exportPlan is not null){input=BuildInputStaging.Write(output,exportPlan.Admission);output=Path.Combine(output,"output");Directory.CreateDirectory(output);}
            lock(gate){if(canceled)throw new OperationCanceledException();child=new(spec.BindOutput(output,input),4096,maxLine,Log,Fault);child.CloseInput();}
            var timer=Task.Delay(timeout);var done=await Task.WhenAny(child.Completion,timer).ConfigureAwait(false);
            if(done!=child.Completion){Fault("Build exceeded its time limit.");child.AbortOwned();}
            code=await child.Completion.ConfigureAwait(false);
            lock(gate)
            {
                if(canceled)throw new OperationCanceledException();
                if(fault is not null)throw new InvalidOperationException(fault);
                if(code!=0)throw new InvalidOperationException("Build exited with code "+code+".");
            }
            if(exportPlan is not null)BuildInputStaging.Verify(input!,exportPlan.Admission);
            var outputs=Inspect(output);
            lock(gate)
            {
                if(canceled)throw new OperationCanceledException();
                if(staging?.State!=ExportStagingState.Ready)throw new InvalidOperationException("Build did not produce every allowlisted output.");
                return new(revision,staging?.ManifestHash??"",output,code,true,"Build outputs verified; not published.",outputs);
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or OperationCanceledException or PlatformNotSupportedException)
        {
            lock(gate){if(canceled)staging?.Cancel(attempt);else staging?.Fail(attempt,e.Message);}
            return new(revision,staging?.ManifestHash??"",output,code,false,canceled?"Build canceled; prior export unchanged.":e.Message,[]);
        }
        finally{lock(gate)finished=true;child?.Dispose();}
    }
    private string PrepareOutput()
    {
        // Reject pre-existing links along the complete path, not merely the last directory.
        var full=Path.GetFullPath(root);var current=Path.GetPathRoot(full)!;
        foreach(var part in full[current.Length..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries))
        {current=Path.Combine(current,part);RequireDirectory(current,false);}
        if(OwnedExportFolder is {} owned){RequireDirectory(owned,false);return owned;}
        foreach(var part in new[]{".bed","cache","export"}){current=Path.Combine(current,part);RequireDirectory(current,true);}
        var output=Path.Combine(current,attempt.ToString("N"));
        if(Path.Exists(output))throw new IOException("Build output admission already exists.");
        if(!CreateDirectoryW(output,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Could not exclusively create build output admission.");
        RequireDirectory(output,false);return output;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CreateDirectoryW(string path,IntPtr security);
    private static void RequireDirectory(string path,bool create)
    {
        if(!Directory.Exists(path)){if(!create)throw new DirectoryNotFoundException("Build project directory is missing.");Directory.CreateDirectory(path);}
        var attributes=File.GetAttributes(path);
        if((attributes&FileAttributes.ReparsePoint)!=0||(attributes&FileAttributes.Directory)==0)throw new IOException("Build output path contains a link or non-directory.");
    }
    private ImmutableArray<ExportStagedOutput> Inspect(string output)
    {
        if(declaration is not null)
        {
            var pinned=BuildOutputInventory.Capture(output,declaration,attempt,priorGood,inventoryCancellation.Token,
                exportPlan is null?null:BuildInputStaging.ExpectedOutputs(exportPlan.Admission));
            lock(gate){if(canceled)throw new OperationCanceledException();OutputManifest=pinned.Manifest;staging=pinned.Staging;}
            return pinned.Staging.Outputs;
        }
        var expected=staging!.Outputs.ToDictionary(o=>o.Path,StringComparer.Ordinal);var result=ImmutableArray.CreateBuilder<ExportStagedOutput>();
        var pending=new Stack<string>();pending.Push(output);var entries=0;long bytes=0;
        while(pending.TryPop(out var directory))
        {
            RequireDirectory(directory,false);
            foreach(var path in Directory.EnumerateFileSystemEntries(directory))
            {
                lock(gate){if(canceled)throw new OperationCanceledException();}
                if(++entries>4096)throw new IOException("Build output inventory exceeded its entry budget.");
                var attr=File.GetAttributes(path);if((attr&FileAttributes.ReparsePoint)!=0)throw new IOException("Build output contains a link.");
                var relative=Path.GetRelativePath(output,path).Replace('\\','/');
                if((attr&FileAttributes.Directory)!=0){if(!expected.Keys.Any(k=>k.StartsWith(relative+"/",StringComparison.Ordinal)))throw new IOException("Build directory is outside the allowlist: "+relative);pending.Push(path);continue;}
                if(!expected.TryGetValue(relative,out var item))throw new IOException("Build output is outside the allowlist: "+relative);
                using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(file.Length!=item.Length||file.Length>64L*1024*1024||(bytes+=file.Length)>320L*1024*1024)throw new IOException("Build output length/budget differs: "+relative);
                var payload=new byte[checked((int)file.Length)];file.ReadExactly(payload);
                if(!staging.Record(staging.Id,relative,payload,out var why))throw new IOException(why);
                result.Add(item);
            }
        }
        return result.OrderBy(r=>r.Path,StringComparer.Ordinal).ToImmutableArray();
    }
    /// <summary>Stops the owned build; leaves all output and prior export paths untouched.</summary>
    public void Dispose()=>Cancel();
}
