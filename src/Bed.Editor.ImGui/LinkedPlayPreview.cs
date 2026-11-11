namespace Bed.Editor.ImGui;

/// <summary>The concrete saved-input linked bridge through the existing IPlaySession extension point.
/// The host supplies the declared session, owner-thread pump and actual owned-completion observation;
/// this class invents no launch producer or converter and never saves an editor document.</summary>
public sealed class LinkedPlayPreview : IPlaySession
{
    private readonly LinkedPlayPanel panel;
    private readonly Action<LinkedPlaySession> pump;
    private readonly Func<bool> completed;
    private bool disposed;
    /// <summary>Starts an explicitly supplied saved-input session. The host constructs it with its agreed five-second
    /// stop deadline; workspace teardown uses Dispose immediately. Start failure follows the existing PlayMode
    /// exception/refusal path, preserving ordinary in-process behavior.</summary>
    public LinkedPlayPreview(LinkedPlaySession session,Action<LinkedPlaySession> pump,Func<bool> completed,ConsolePanel? console=null)
    {
        ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(pump);ArgumentNullException.ThrowIfNull(completed);
        if(session.Snapshot.Choice!=PlayInputChoice.Saved)
            throw new InvalidOperationException("Play runs your saved files. Choose the available saved version first.");
        this.pump=pump;this.completed=completed;panel=new(session,pump,console);
        if(!panel.Start())
        {
            var why=session.Error?.Message ?? "The linked Play session could not start.";
            panel.Dispose();throw new InvalidOperationException(why);
        }
    }
    /// <summary>The immutable-input session, never a live editor document.</summary>
    public LinkedPlaySession Session=>panel.Session;
    /// <summary>Terminal protocol state plus actual owned-resource completion, or forced workspace disposal.</summary>
    public bool HasCompleted=>disposed || (Session.State is LinkedPlayState.Stopped or LinkedPlayState.Faulted) && completed();
    /// <summary>Current full linked status.</summary>
    public string Status=>panel.Status;
    /// <summary>Cooperative user Stop; disposal remains reserved for owner loss/forced teardown.</summary>
    public bool RequestStop()=>!disposed && panel.RequestStop();
    /// <summary>Updates even when the game window is collapsed. Terminal sessions still drain their owned host
    /// until actual Completion, so a Stopped message alone cannot release the editing owner.</summary>
    public void Update()
    {
        if(disposed) return;
        panel.Update();
        if(Session.State is LinkedPlayState.Stopped or LinkedPlayState.Faulted && !completed()) pump(Session);
    }
    /// <summary>Ordinary IPlaySession draw with owner-frame progress and shared-tree controls.</summary>
    public void Draw() {Update();panel.DrawContents();}
    internal void DrawAfterUpdate()=>panel.DrawContents();
    /// <summary>Workspace/file/project loss immediately aborts owned resources, once, without a save or draft edit.</summary>
    public void Dispose()
    {
        if(disposed) return;
        disposed=true;panel.Dispose();
    }
}
