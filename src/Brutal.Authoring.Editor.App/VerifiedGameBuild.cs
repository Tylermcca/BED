using Brutal.Authoring.ExportIO;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Brutal.Authoring.Editor;
namespace Brutal.Authoring.Editor.App;

/// <summary>Actual bounded smoke outcome. Only a successful run can issue the copy capability.</summary>
public sealed record ExportSmokeResult(VerifiedGameBuild? Build,string Reason,ImmutableArray<GameBuildLog> Logs);

/// <summary>Copy capability issued only after verified build inventory and an actual owned smoke process.
/// Copy rechecks every pinned byte; a changed or nonempty destination never overwrites an existing build.</summary>
public sealed class VerifiedGameBuild:Brutal.Authoring.Editor.ImGui.IVerifiedGameBuild
{
    private readonly GameExportPlan plan;private readonly GameBuildReceipt receipt;
    private VerifiedGameBuild(GameExportPlan plan,GameBuildReceipt receipt){this.plan=plan;this.receipt=receipt;}
    /// <summary>The owned build directory, not a published user destination.</summary>
    public string OutputDirectory=>receipt.OutputDirectory;
    /// <summary>Runs only the game's declared, inventoried executable from the actual owned output directory.</summary>
    public static async Task<ExportSmokeResult> SmokeAsync(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,
        GameLaunchSpec smoke,TimeSpan timeout,int maxLineCharacters=8192,int maxPendingLines=256,CancellationToken cancellation=default)
    {
        if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromDays(1)||maxLineCharacters<=0||maxPendingLines<=0)throw new ArgumentOutOfRangeException(nameof(timeout));
        var logs=new List<GameBuildLog>();var gate=new object();string? fault=null;SupervisedProcess? child=null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if(!receipt.Succeeded||receipt.SavedRevision!=plan.Admission.SnapshotHash||outputs.SourceManifestHash!=plan.Admission.Source.Hash||outputs.Hash!=receipt.ManifestHash)
                throw new InvalidOperationException("Smoke requires this exact successful Saved build and output manifest.");
            Recheck(plan,receipt,cancellation);
            var root=Path.GetFullPath(receipt.OutputDirectory);
            if(!Path.GetFullPath(smoke.WorkingDirectory).Equals(root,StringComparison.OrdinalIgnoreCase)||!plan.Outputs.Paths.Contains(Path.GetRelativePath(root,smoke.Executable).Replace('\\','/'),StringComparer.Ordinal))
                throw new InvalidOperationException("Smoke must run the declared executable from the owned build output.");
            void Fault(string why){lock(gate){fault??=why;child?.AbortOwned();}}
            child=new(smoke,4096,maxLineCharacters,(text,error)=>{lock(gate){if(logs.Count>=maxPendingLines){Fault("Smoke log budget exceeded.");return;}logs.Add(new(error,text));}},Fault);
            child.CloseInput();using var registration=cancellation.Register(child.AbortOwned);
            lock(gate)if(fault is not null)child.AbortOwned();
            var deadline=Task.Delay(timeout);var finished=await Task.WhenAny(child.Completion,deadline).ConfigureAwait(false);
            if(finished!=child.Completion){Fault("Smoke exceeded its time limit.");child.AbortOwned();}
            var code=await child.Completion.ConfigureAwait(false);cancellation.ThrowIfCancellationRequested();
            lock(gate)if(fault is not null)throw new InvalidOperationException(fault);
            if(code!=0)
            {
                string? framework;lock(gate)framework=logs.Where(line=>line.Error).Select(line=>DotNetLaunchFailure.Framework(line.Text)).FirstOrDefault(line=>line is not null);
                throw new InvalidOperationException(DotNetLaunchFailure.Describe(code,framework)??"Smoke exited with code "+code+".");
            }
            Recheck(plan,receipt,cancellation);
            return new(new(plan,receipt),"Build and unpacked smoke verified; ready to copy.",logs.ToImmutableArray());
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {lock(gate)return new(null,error is OperationCanceledException?"Smoke canceled; copy remains unavailable.":error.Message,logs.ToImmutableArray());}
        finally{child?.Dispose();}
    }
    private static void Recheck(GameExportPlan plan,GameBuildReceipt receipt,CancellationToken cancellation=default)
    {
        var expected=BuildInputStaging.ExpectedOutputs(plan.Admission);
        var current=BuildOutputInventory.Capture(receipt.OutputDirectory,plan.Outputs,Guid.NewGuid(),cancellationToken:cancellation,expectedHashes:expected);
        if(current.Manifest.Hash!=receipt.ManifestHash||!current.Staging.Outputs.SequenceEqual(receipt.Outputs))throw new IOException("Build output changed since verification.");
    }
    /// <summary>Copies only a fully verified folder into a new/empty destination. A canceled or failed attempt leaves no partial destination.</summary>
    public bool CopyTo(string destination,out string reason,CancellationToken cancellation=default)=>CopyToCore(destination,out reason,cancellation);

    // Per-instance progress observation, used by the owning UI/test host without changing the copy authority.
    internal ExportRunLog? RunLog {get;set;}
    internal Action<long>? CopyProgress { get; set; }
    internal Func<string,long?> AvailableSpace {get;set;}=ExportDiskSpace.AvailableBytes;
    private void RequireSpace(string target,long bytes)
    {
        if(AvailableSpace(target) is {} available&&available<bytes)throw new IOException(ExportDiskSpace.Refusal(bytes,available));
    }
    private bool CopyToCore(string destination,out string reason,CancellationToken cancellation)
    {
        string? stage=null;var committed=false;var removedEmpty=false;string? target=null;IDisposable? logLease=null;
        try
        {
            logLease=RunLog?.Acquire();RunLog?.Phase("Copy started: "+destination);
            cancellation.ThrowIfCancellationRequested();
            if(!Path.IsPathFullyQualified(destination))throw new ArgumentException("Choose an absolute new or empty folder.");
            target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));var source=Path.TrimEndingDirectorySeparator(Path.GetFullPath(OutputDirectory));
            if(target.Equals(source,StringComparison.OrdinalIgnoreCase)||target.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a folder outside the owned build output.");
            RequireDestination(target);
            RequireSpace(target,receipt.Outputs.Sum(item=>(long)item.Length));
            Recheck(plan,receipt,cancellation);
            // Pin every declared artifact before allocating a sibling staging directory.
            var bytes=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
            foreach(var item in receipt.Outputs)
            {
                cancellation.ThrowIfCancellationRequested();
                var path=Path.Combine(source,item.Path);using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(file.Length!=item.Length||file.Length>64L*1024*1024||(total+=file.Length)>320L*1024*1024)throw new IOException("Build copy byte budget differs.");
                var data=new byte[checked((int)file.Length)];
                for(var at=0;at<data.Length;)
                {cancellation.ThrowIfCancellationRequested();var read=file.Read(data,at,Math.Min(65536,data.Length-at));if(read==0)throw new EndOfStreamException();at+=read;}
                if(Convert.ToHexStringLower(SHA256.HashData(data))!=item.Sha256)throw new IOException("Build changed before copy: "+item.Path);
                bytes.Add(item.Path,data);
            }
            var parent=Path.GetDirectoryName(target)??throw new IOException("Choose a destination below a drive root.");
            RequireSpace(target,total);
            Directory.CreateDirectory(parent);RequireDestination(target);
            var proposed=Path.Combine(parent,".bed-export-copy-"+Guid.NewGuid().ToString("N"));
            if(!CreateDirectoryW(proposed,IntPtr.Zero))throw new IOException("Could not reserve an owned copy staging folder.");
            stage=proposed;long copied=0;
            foreach(var item in bytes)
            {
                cancellation.ThrowIfCancellationRequested();
                RunLog?.Phase("Copy file: "+item.Key+" ("+item.Value.Length+" bytes)");
                var path=Path.Combine(stage,item.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                for(var at=0;at<item.Value.Length;)
                {
                    cancellation.ThrowIfCancellationRequested();var count=Math.Min(65536,item.Value.Length-at);
                    file.Write(item.Value,at,count);at+=count;copied+=count;CopyProgress?.Invoke(copied);
                }
            }
            cancellation.ThrowIfCancellationRequested();RequireDestination(target,true);
            if(Directory.Exists(target)){Directory.Delete(target);removedEmpty=true;}
            cancellation.ThrowIfCancellationRequested();Directory.Move(stage,target);committed=true;
            reason="Build copied to "+target;
            try{RunLog?.Phase(reason);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){reason+="; export log incomplete: "+e.Message;}
            return true;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {reason=error is OperationCanceledException?"Copy canceled; destination unchanged.":error.Message;RunLog?.Phase(reason);return false;}
        finally
        {
            try
            {
                if(!committed&&stage is not null)CleanOwnedStage(stage);
                if(!committed&&removedEmpty&&target is not null&&!Path.Exists(target))Directory.CreateDirectory(target);
            }
            finally{logLease?.Dispose();}
        }
    }
    private static void RequireDestination(string target,bool afterWriting=false)
    {
        var current=Path.GetPathRoot(target)!;
        foreach(var part in target[current.Length..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries))
        {current=Path.Combine(current,part);if(Path.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Copy destination contains a link.");}
        if(File.Exists(target)||Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any())throw new IOException(afterWriting?"Copy destination is no longer empty.":"Choose a new or empty folder; existing files will not be replaced.");
    }
    private static void CleanOwnedStage(string stage)
    {
        // The native create above exclusively allocated this exact sibling; never recursively delete a user target.
        if(!Path.GetFileName(stage).StartsWith(".bed-export-copy-",StringComparison.Ordinal)||(File.GetAttributes(stage)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Owned copy staging changed before cleanup.");
        Directory.Delete(stage,true);
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool CreateDirectoryW(string path,IntPtr security);
}
