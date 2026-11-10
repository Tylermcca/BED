using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
namespace Brutal.Authoring.Editor.App;

/// <summary>Thin app implementation over the existing supervised build/inventory/staging host; no new process protocol.</summary>
public sealed class ExportBuildHost:IGameExportHost
{
    private readonly ExportRunStore runs;
    private readonly ConcurrentDictionary<string,ExportRunLog> logs=new(StringComparer.OrdinalIgnoreCase);
    private readonly int maxLine,maxLogs;private readonly TimeSpan timeout;
    /// <summary>Declares the existing host's bounded logs/deadline without starting a process.</summary>
    public ExportBuildHost(int maxLineCharacters,int maxPendingLines,TimeSpan timeout)
        :this(maxLineCharacters,maxPendingLines,timeout,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BED")){}
    internal ExportBuildHost(int maxLineCharacters,int maxPendingLines,TimeSpan timeout,string localFolder)
    {
        runs=new(localFolder);
        if(maxLineCharacters<=0||maxPendingLines<=0||timeout<=TimeSpan.Zero||timeout>TimeSpan.FromDays(1))throw new ArgumentOutOfRangeException(nameof(timeout));
        maxLine=maxLineCharacters;maxLogs=maxPendingLines;this.timeout=timeout;
    }
    /// <summary>Creates one actual host from a Saved-admitted plan and forwards its concrete completion/lifetime.</summary>
    public IGameBuildOperation Start(GameBuildSpec spec,string projectRoot,bool hasUnsavedChanges,GameExportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(spec);
        if(hasUnsavedChanges)throw new InvalidOperationException("Save changes before building.");
        if(plan.Outputs.Source.Hash!=plan.Admission.Source.Hash||plan.Outputs.Source.InventoryRevision!=plan.Admission.SnapshotHash)
            throw new InvalidOperationException("The output plan is stale for Saved input.");
        var log=runs.Begin(projectRoot);var run=Path.GetDirectoryName(log.Path)!;
        GameBuildHost? host=null;var output=Path.Combine(run,"output");
        try
        {
            log.Phase("Project: "+Path.GetFullPath(projectRoot));log.Phase("Build command: "+spec.Executable+" "+System.Text.Json.JsonSerializer.Serialize(spec.Arguments));
            host=new GameBuildHost(spec,projectRoot,hasUnsavedChanges,plan,maxLine,maxLogs,timeout){OwnedExportFolder=run,PersistentLog=log.Output};
            logs[output]=log;host.Start();return new Operation(host,log);
        }
        catch(Exception e)
        {
            host?.Dispose();logs.TryRemove(output,out _);
            try{log.Phase("Build failed: "+e.Message);}catch(Exception failed)when(failed is IOException or UnauthorizedAccessException){}
            finally{log.Dispose();}
            throw;
        }
    }
    /// <summary>Forwards actual smoke verification and the guarded copy capability.</summary>
    public async Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec smoke,CancellationToken cancellation)
    {
        logs.TryGetValue(receipt.OutputDirectory,out var log);
        using var lease=log?.Acquire();
        log?.Phase("Checking the unpacked build...");
        try
        {
            var result=await VerifiedGameBuild.SmokeAsync(plan,receipt,outputs,smoke,timeout,maxLine,maxLogs,cancellation).ConfigureAwait(false);
            foreach(var line in result.Logs)log?.Output(line);log?.Phase(result.Reason);
            if(result.Build is {} build)build.RunLog=log;
            return new(result.Build,result.Reason);
        }
        finally{logs.TryRemove(receipt.OutputDirectory,out _);}
    }
    /// <inheritdoc/>
    public bool OpenLogFolder(string path,out string reason)
    {
        if(!runs.Owns(path)){reason="The export log folder is unavailable.";return false;}
        try{Process.Start(new ProcessStartInfo(Path.GetDirectoryName(Path.GetFullPath(path))!){UseShellExecute=true});reason="";return true;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {reason="The export log folder could not be opened: "+e.Message;return false;}
    }
    private sealed class Operation:IGameBuildOperation
    {
        private readonly GameBuildHost host;private readonly ExportRunLog log;private int disposed;
        public Operation(GameBuildHost host,ExportRunLog log){this.host=host;this.log=log;Completion=Observe();}
        public string? LogPath=>log.Path;
        public Task<GameBuildReceipt> Completion {get;}
        private async Task<GameBuildReceipt> Observe()
        {var result=await host.Completion!.ConfigureAwait(false);log.Phase(result.Reason);return result;}
        public GameContentManifest? OutputManifest=>host.OutputManifest;
        public ImmutableArray<GameBuildLog> DrainLogs()=>host.DrainLogs();
        public void Cancel(){log.Phase("Export cancellation requested.");host.Cancel();}
        public void Dispose(){if(Interlocked.Exchange(ref disposed,1)==0){host.Dispose();log.Dispose();}}
    }
}
