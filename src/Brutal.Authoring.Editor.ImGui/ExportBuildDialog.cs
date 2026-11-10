using Brutal.Authoring.ExportIO;
using Brutal.ImGuiApi;
using Im = Brutal.ImGuiApi.ImGui;
namespace Brutal.Authoring.Editor.ImGui;
/// <summary>One workspace-owned export attempt; only the App's actual smoke result enables copy.</summary>
public sealed class ExportBuildDialog:IDisposable
{
    private readonly IGameExportHost host;private readonly GameBuildSpec spec;private readonly GameExportDeclaration declaration;
    private readonly string root;private readonly Func<PlaySnapshot> capture;private readonly Func<bool> dirty;
    private readonly ImInputString destination=new(4096,""u8);private IGameBuildOperation? operation;
    private Task<(bool Success,string Reason,string Folder)>? copy;
    private GameExportPlan? plan;private Task<GameExportSmokeResult>? smoke;private CancellationTokenSource cancellation=new();
    private GameContentManifest? pendingOutputs;
    private (int Files,long Bytes)? verifiedSummary;
    private string? copiedFolder,logPath;
    internal Func<string,long?> AvailableSpace {get;set;}=ExportDiskSpace.AvailableBytes;
    private string? spaceFolder;private long spaceChecked;private long? availableBytes;
    private long? Space(string folder,bool fresh=false)
    {
        var now=Environment.TickCount64;
        if(fresh||spaceFolder!=folder||now-spaceChecked>=1000)
        {spaceFolder=folder;spaceChecked=now;availableBytes=AvailableSpace(folder);}
        return availableBytes;
    }
    private string? SpaceRefusal(string folder,bool fresh=false)=>verifiedSummary is {} summary&&Space(folder,fresh) is {} available&&available<summary.Bytes
        ?ExportDiskSpace.Refusal(summary.Bytes,available):null;
    private IVerifiedGameBuild? verified;private bool canceled;private bool disposed;private readonly Queue<string> logs=new();
    private enum Focus { None, Build, Cancel, Destination, Close }
    private Focus focus;
    private bool open, focusWindow;
    private ExportFolderPreference? folderPreference;
    internal void BindDestinationPreference(string project, string preferencesFolder)
    {
        folderPreference = new(project, preferencesFolder);
        if(folderPreference.Read() is { } folder) destination.SetValue(folder.AsSpan());
    }
    /// <summary>Owns one bounded workspace export workflow and Saved capture.</summary>
    public ExportBuildDialog(IGameExportHost host,GameBuildSpec spec,GameExportDeclaration declaration,string root,Func<PlaySnapshot> capture,Func<bool> dirty)
    {this.host=host;this.spec=spec;this.declaration=declaration;this.root=root;this.capture=capture;this.dirty=dirty;}
    /// <summary>Whether the export window is shown.</summary>
    public bool Open
    {
        get => open;
        set
        {
            if (value && !open) { focusWindow = true; focus = Busy ? Focus.Cancel : CanCopy ? Focus.Destination : Focus.Build; }
            open = value;
        }
    }
    /// <summary>Current user-facing export result or progress.</summary>
    public string Status {get;private set;}="Build the saved files, then check the unpacked build before copying.";
    /// <summary>Whether build or smoke completion is pending.</summary>
    public bool Busy=>operation is not null||smoke is not null||copy is not null;
    /// <summary>Whether build or smoke completion is pending.</summary>
    public bool CanCopy=>verified is not null&&!canceled&&!Busy;
    /// <summary>Captures admitted Saved input and starts one build; dirty edits refuse.</summary>
    public bool Start()
    {
        if(Busy)return false;
        verified=null;pendingOutputs=null;verifiedSummary=null;copiedFolder=null;spaceFolder=null;canceled=false;logPath=null;logs.Clear();cancellation.Dispose();cancellation=new();
        try
        {
            if(dirty())throw new InvalidOperationException("Save changes before building.");
            var saved=capture();plan=declaration.Prepare(saved);operation=host.Start(spec,root,false,plan);logPath=operation.LogPath;Status="Building saved files...";focus=Focus.Cancel;return true;
        }
        catch(Exception error)when(error is not OutOfMemoryException){Status=error.Message;focus=Focus.Build;return false;}
    }
    /// <summary>Cancels owned build/smoke and clears copy authority.</summary>
    public void Cancel(){canceled=true;verified=null;pendingOutputs=null;verifiedSummary=null;copiedFolder=null;cancellation.Cancel();operation?.Cancel();Status="Export canceled. Your edits are unchanged.";focus=Focus.Close;}
    /// <summary>Observes completion on the UI thread without blocking it.</summary>
    public void Pump()
    {
        try
        {
            if(operation is { } running)
            {
                foreach(var line in running.DrainLogs()){if(logs.Count==256)logs.Dequeue();logs.Enqueue(line.Text);}
                if(running.Completion.IsCompleted)
                {
                    var receipt=running.Completion.GetAwaiter().GetResult();var outputs=running.OutputManifest;running.Dispose();operation=null;
                    if(canceled)return;
                    if(!receipt.Succeeded||outputs is null){Status=string.IsNullOrWhiteSpace(receipt.Reason)?"The build failed. Check the build output below.":receipt.Reason;focus=Focus.Build;return;}
                    pendingOutputs=outputs;
                    smoke=host.Smoke(plan!,receipt,outputs,declaration.Smoke(receipt.OutputDirectory,outputs),cancellation.Token);Status="Checking the unpacked build...";
                }
            }
            if(smoke is {IsCompleted:true} done)
            {
                var result=done.GetAwaiter().GetResult();smoke=null;
                if(!canceled)
                {
                    verified=result.Build;
                    if(verified is not null && pendingOutputs is {} outputs)
                        verifiedSummary=(outputs.Nodes.Length,outputs.Nodes.Sum(n=>(long)n.Length));
                    Status=result.Reason;focus=CanCopy?Focus.Destination:Focus.Build;
                }
            }
            if(copy is {IsCompleted:true} copied)
            {
                var result=copied.GetAwaiter().GetResult();copy=null;
                if(!canceled)FinishCopy(result);
            }
        }
        catch(Exception error)when(error is not OutOfMemoryException){operation?.Dispose();operation=null;smoke=null;copy=null;verified=null;if(!canceled)Status=error.Message;focus=Focus.Build;}
    }
    /// <summary>Uses only the host-issued copy authority and reports its result for synchronous host callers.</summary>
    public bool Copy(string folder)
    {
        if(!CopyDestination(folder))return false;
        var result=CopyVerified(verified!,folder,cancellation.Token);FinishCopy(result);return result.Success;
    }
    private bool CopyDestination(string folder)
    {
        if(!CanCopy){Status="A successful unpacked check is required before copying.";return false;}
        if(string.IsNullOrWhiteSpace(folder)){Status="Choose a destination folder before copying.";focus=Focus.Destination;return false;}
        if(SpaceRefusal(folder,true) is {} refusal){Status=refusal;focus=Focus.Destination;return false;}
        return true;
    }
    private void BeginCopy(string folder)
    {
        if(!CopyDestination(folder))return;
        var build=verified!;var token=cancellation.Token;
        copy=Task.Run(()=>CopyVerified(build,folder,token));
        Status="Copying verified build...";focus=Focus.Cancel;
    }
    private static (bool Success,string Reason,string Folder) CopyVerified(IVerifiedGameBuild build,string folder,CancellationToken token)
    {
        try
        {
            var ok=build.CopyTo(folder,out var why,token);
            return(ok,string.IsNullOrWhiteSpace(why)?ok?"Build copied.":"The build could not be copied. Choose a new or empty destination folder.":why,folder);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {return(false,"The build could not be copied: "+error.Message,folder);}
    }
    private void FinishCopy((bool Success,string Reason,string Folder) result)
    {
        Status=result.Reason;
        if(result.Success){copiedFolder=Path.GetFullPath(result.Folder);folderPreference?.Remember(result.Folder);}
        focus=result.Success?Focus.Close:Focus.Destination;
    }
    /// <summary>Draws bounded progress, cancel and smoke-gated copy controls.</summary>
    public void Draw()
    {
        Pump();if(!Open)return;
        Im.SetNextWindowSize(new Brutal.Numerics.float2(580,420),ImGuiCond.FirstUseEver);
        if(focusWindow){Im.SetNextWindowFocus();focusWindow=false;}
        if(Ui.Begin("Export build###export.build"u8))
        {
            if(Im.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&Im.IsKeyPressed(ImGuiKey.Escape,false))
            {if(Busy&&!canceled)Cancel();else Open=false;}
            Im.PushTextWrapPos(0);Ui.Status($"Export status: {Status}","export.status");Im.PopTextWrapPos();
            if(verifiedSummary is {} summary)Ui.TextWrapped($"Verified build: {summary.Files} files, {summary.Bytes} bytes.");
            if(copiedFolder is not null)Ui.TextWrapped("Copied to: "+copiedFolder);
            Ui.BeginDisabled(Busy,Busy?"An export is already running.":null);
            FocusNext(Focus.Build);if(Ui.Button("Build saved files"u8))Start();Ui.EndDisabled();
            Ui.BeginDisabled(!Busy||canceled,canceled?"The export has already been canceled.":"No export is running.");
            FocusNext(Focus.Cancel);if(Ui.Button("Cancel export"u8))Cancel();Ui.EndDisabled();
            foreach(var line in logs)Ui.TextWrapped(line);
            FocusNext(Focus.Destination);Ui.InputText("Destination folder"u8,destination);
            var folder=destination.ToString()??"";
            var spaceRefusal=CanCopy?SpaceRefusal(folder):null;
            if(verifiedSummary is {Bytes:>500_000_000})Ui.TextWrapped("Warning: this verified build is over 500 MB. Copying may take a while."u8);
            if(spaceRefusal is not null)Ui.TextWrapped(spaceRefusal);
            else if(CanCopy&&Path.IsPathFullyQualified(folder)&&Space(folder) is null)
                Ui.TextWrapped("The destination drive's free space could not be checked. Copying may fail if it runs out of room."u8);
            Ui.BeginDisabled(!CanCopy||spaceRefusal is not null,spaceRefusal??"Build and verify the unpacked result before copying.");
            if(Ui.Button("Copy build to folder..."u8))BeginCopy(folder);Ui.EndDisabled();
            FocusNext(Focus.Close);if(Ui.Button("Close"u8)){if(Busy&&!canceled)Cancel();Open=false;}
            Ui.BeginDisabled(logPath is null||!File.Exists(logPath),"Build an export to create its log.");
            if(Ui.Button("Open log folder"u8)&&logPath is {} path&&!host.OpenLogFolder(path,out var why))Status=why;
            Ui.EndDisabled();
            if(logPath is not null)Ui.TextWrapped("Export log: "+logPath);
            Ui.TextDisabled("The last 3 export runs are kept for this project."u8);
            Ui.TextDisabled("Tab moves between controls. Escape cancels a running export or closes this window."u8);
        }
        Ui.End();
    }
    private void FocusNext(Focus target)
    {if(focus==target){Im.SetKeyboardFocusHere();focus=Focus.None;}}
    /// <summary>Cancels owned build/smoke and clears copy authority.</summary>
    public void Dispose(){if(disposed)return;Cancel();disposed=true;operation?.Dispose();operation=null;cancellation.Dispose();if(copy is {} pending)_=pending.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);}
}
