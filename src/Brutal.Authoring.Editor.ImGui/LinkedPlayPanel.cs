namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Standalone linked Play presentation over an already supplied session and its owner-thread pump.
/// It creates no producer, process, snapshot, editor session or save callback. The host must call Update while
/// Starting/Running/Stopping even when this panel is hidden, and Dispose when its workspace owner is lost.</summary>
public sealed class LinkedPlayPanel : IDisposable
{
    private readonly Action<LinkedPlaySession> pump;
    private readonly ConsolePanel? console;
    private bool disposed;
    /// <summary>Uses existing declared session/control surfaces. For an actual process, pass GameProcessHost.Pump
    /// from the App host; this UI assembly has no dependency on the process implementation.</summary>
    public LinkedPlayPanel(LinkedPlaySession session,Action<LinkedPlaySession> pump,ConsolePanel? console=null)
    {
        ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(pump);
        Session=session;this.pump=pump;this.console=console;
    }
    /// <summary>The supplied single-use owner, never silently replaced with a newer draft.</summary>
    public LinkedPlaySession Session { get; }
    /// <summary>Whether this already captured input may start once.</summary>
    public bool CanStart=>!disposed && !Session.IsDisposed && Session.State==LinkedPlayState.Created;
    /// <summary>Whether cooperative stop can be requested once. Stopping is still active until ack/exit/deadline.</summary>
    public bool CanStop=>!disposed && !Session.IsDisposed && Session.State is LinkedPlayState.Starting or LinkedPlayState.Running;
    /// <summary>Plain full lifecycle status, separate from in-process preview.</summary>
    public string Status=>Session.State switch
    {
        LinkedPlayState.Created=>"Ready to start.",
        LinkedPlayState.Starting=>"Starting: waiting for the game to load the captured input.",
        LinkedPlayState.Running=>"Running.",
        LinkedPlayState.Stopping=>"Stopping: waiting for the game to finish.",
        LinkedPlayState.Stopped=>"Stopped.",
        _=>"Play failed: "+(Session.Error?.Message ?? "The linked session failed."),
    };
    /// <summary>Explicitly starts the supplied input only. Failure remains visible without mutating authored data.</summary>
    public bool Start()=>CanStart && Session.Start();
    /// <summary>Requests cooperative stop, never disposal or a silent restart/save.</summary>
    public bool RequestStop()=>CanStop && Session.RequestStop();
    /// <summary>Owner-frame work; no hidden timer or background callback touches the UI/editor.
    /// A pump fault becomes a bounded correlated failure of this active linked session.</summary>
    public void Update()
    {
        if(disposed || Session.IsDisposed || Session.State is not (LinkedPlayState.Starting or LinkedPlayState.Running or LinkedPlayState.Stopping)) return;
        try {pump(Session);}
        catch(Exception error) when(error is not OutOfMemoryException)
        {
            var detail=new string(("Could not update linked Play: "+error.Message)
                .Select(c=>char.IsControl(c) && c is not ('\n' or '\r' or '\t') ? ' ' : c)
                .Take(PlayProtocol.MaxErrorLength).ToArray());
            Session.Receive(new PlayFailure(PlayProtocol.Version,Session.Correlation,new(PlayErrorCode.RuntimeFailed,detail)));
        }
        Session.Tick();
    }
    /// <summary>Initially closed standalone panel. Registration and existing Play toolbar hooks require host wiring.</summary>
    public Panel AsPanel(string id="editor.linked-play")=>new(id,"Linked Play",Draw,OpenAtStart:false,Width:720,Height:500);
    /// <summary>Shows the explicit captured Saved/Draft choice, full status, start/stop availability and optional Console.</summary>
    public void Draw()
    {
        Update();DrawContents();
    }
    internal void DrawContents()
    {
        Ui.TextWrapped($"Input: {Session.Snapshot.Choice}, captured revision {Session.Snapshot.Revision}.",semanticId:"linked.play.input");
        Ui.TextWrapped(Status,semanticId:"linked.play.status");
        if(Session.CleanupError is { } cleanup) Ui.TextWrapped(cleanup.Message,semanticId:"linked.play.cleanup");
        Ui.BeginDisabled(!CanStart,disposed || Session.IsDisposed ? "This Play owner is closed." : "Choose Play again for a new session.");
        if(Ui.Button("Start linked Play"u8)) Start();
        Ui.EndDisabled();
        Ui.BeginDisabled(!CanStop,Session.State==LinkedPlayState.Stopping ? "Stop has already been requested." : "No linked game is starting or running.");
        if(Ui.Button("Stop linked Play"u8)) RequestStop();
        Ui.EndDisabled();
        console?.Draw();
    }
    /// <summary>Workspace loss/forced teardown ends only this supplied owner, once; toolbar cooperative stop uses RequestStop.</summary>
    public void Dispose()
    {
        if(disposed) return;
        disposed=true;Session.Dispose();
    }
}
