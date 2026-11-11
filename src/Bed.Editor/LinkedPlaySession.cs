namespace Bed.Editor;

/// <summary>Owned linked-game control seam. Implementations report messages/exits back on the editor owner thread.
/// This contract supplies no process or transport and must never operate on unrelated resources.</summary>
public interface ILinkedPlayControl
{
    /// <summary>Starts once with the exact immutable input and editor hello. May report a synchronous reply.</summary>
    void Start(PlayHello hello,PlaySnapshot snapshot);
    /// <summary>Requests cooperative stop for the exact correlated start, including while waiting for Ready.</summary>
    void RequestStop(PlayStop stop);
    /// <summary>Releases/aborts only this control's owned resources. Called at most once by the session,
    /// after completion, fault or disposal; it must tolerate a partially failed Start.</summary>
    void AbortOwned();
}

/// <summary>States of one linked Play start; a restart uses a new owner and a fresh session identity.</summary>
public enum LinkedPlayState
{
    /// <summary>Constructed but no control call has occurred.</summary>
    Created,
    /// <summary>Start was attempted; waiting for exact loaded-input readiness.</summary>
    Starting,
    /// <summary>The exact snapshot was acknowledged and required capabilities negotiated.</summary>
    Running,
    /// <summary>Cooperative stop was requested; waiting for completion or exit.</summary>
    Stopping,
    /// <summary>Ended normally or disposed before/while active.</summary>
    Stopped,
    /// <summary>Start, protocol, runtime, exit or deadline failed.</summary>
    Faulted,
}

/// <summary>In-process owner-thread state machine over a supplied control and clock. It retains only an immutable
/// PlaySnapshot, never an editor session or save callback. Call Tick from the owner loop; there are no timers,
/// threads, process launches or hidden timeout defaults. Start/stop durations are required caller policy.</summary>
public sealed class LinkedPlaySession : IDisposable
{
    private readonly ILinkedPlayControl control;
    private readonly TimeProvider clock;
    private readonly TimeSpan startTimeout,stopTimeout;
    private long stateAt;
    private bool attempted,aborted;
    /// <summary>Creates a single-use session without starting it or changing any editor data.</summary>
    public LinkedPlaySession(ILinkedPlayControl control,PlaySnapshot snapshot,Guid sessionId,TimeProvider clock,
        TimeSpan startTimeout,TimeSpan stopTimeout)
    {
        ArgumentNullException.ThrowIfNull(control); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(clock);
        if(startTimeout<=TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(startTimeout));
        if(stopTimeout<=TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stopTimeout));
        this.control=control; this.clock=clock; this.startTimeout=startTimeout; this.stopTimeout=stopTimeout;
        Snapshot=snapshot; Hello=PlayProtocol.Hello(snapshot.Correlate(sessionId));
    }

    /// <summary>The immutable input held for this entire start.</summary>
    public PlaySnapshot Snapshot { get; }
    /// <summary>The exact editor offer; never rebound to later editor edits.</summary>
    public PlayHello Hello { get; }
    /// <summary>Captured session/revision/hash, shared by all control calls and accepted replies.</summary>
    public PlayCorrelation Correlation=>Hello.Correlation;
    /// <summary>Current lifecycle state.</summary>
    public LinkedPlayState State { get; private set; }
    /// <summary>Negotiated capabilities while Running; cleared when ending.</summary>
    public PlayCapability Capabilities { get; private set; }
    /// <summary>The primary fault, retained after cleanup/disposal.</summary>
    public PlayProtocolError? Error { get; private set; }
    /// <summary>A separate bounded owned-cleanup fault; never replaces the primary diagnostic.</summary>
    public PlayProtocolError? CleanupError { get; private set; }
    /// <summary>Saturating count of stale, duplicate or terminal replies that had no effect.</summary>
    public long IgnoredMessages { get; private set; }
    /// <summary>Whether Dispose has been called. Dispose is idempotent and prevents a later Start.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Attempts Start exactly once. State/deadline are installed before invoking the control, so synchronous
    /// callbacks are legal. A Start fault releases any partially created owned resources.</summary>
    public bool Start()
    {
        if(IsDisposed || State!=LinkedPlayState.Created) return false;
        State=LinkedPlayState.Starting; stateAt=clock.GetTimestamp(); attempted=true;
        try { control.Start(Hello,Snapshot); }
        catch(Exception error) when(error is not OutOfMemoryException)
        { Fault(new(PlayErrorCode.LoadFailed,Plain("Could not start linked Play: "+error.Message))); }
        return State!=LinkedPlayState.Faulted;
    }

    /// <summary>Requests cooperative stop at most once, including during startup. Created can stop without any
    /// control call. The stop deadline starts here, independently of the original Ready deadline.</summary>
    public bool RequestStop()
    {
        if(IsDisposed || State is LinkedPlayState.Stopped or LinkedPlayState.Faulted or LinkedPlayState.Stopping) return false;
        if(State==LinkedPlayState.Created) { State=LinkedPlayState.Stopped; return true; }
        State=LinkedPlayState.Stopping; Capabilities=PlayCapability.None; stateAt=clock.GetTimestamp();
        try { control.RequestStop(new(PlayProtocol.Version,Correlation)); }
        catch(Exception error) when(error is not OutOfMemoryException)
        { Fault(new(PlayErrorCode.RuntimeFailed,Plain("Could not request linked Play stop: "+error.Message))); }
        return true;
    }

    /// <summary>Processes a decoded peer message. A stale identity is ignored before checking its version or errors.
    /// Exact-identity malformed/unexpected messages fault the active session. Duplicate Ready and Ready after stop
    /// cannot restart it. Completion is accepted while Running/Stopping; ending before Ready is a load failure.</summary>
    public bool Receive(PlayMessage message)
    {
        if(IsDisposed || State is LinkedPlayState.Created or LinkedPlayState.Stopped or LinkedPlayState.Faulted)
            return Ignore();
        Tick();
        if(State==LinkedPlayState.Faulted) return Ignore();
        if(message is not null && message.Correlation!=Correlation) return Ignore();
        if(PlayProtocol.Validate(message!,Correlation) is { } invalid) { Fault(invalid); return true; }
        switch(message)
        {
            case PlayReady ready when State==LinkedPlayState.Starting:
                var negotiated=PlayProtocol.Negotiate(Hello,ready);
                if(!negotiated.Accepted) Fault(negotiated.Error!);
                else { State=LinkedPlayState.Running; Capabilities=negotiated.Capabilities; }
                return true;
            case PlayReady: return Ignore();
            case PlayFailure failure: Fault(failure.Error); return true;
            case PlayStopped when State is LinkedPlayState.Running or LinkedPlayState.Stopping:
                Complete(); return true;
            case PlayStopped:
                Fault(new(PlayErrorCode.LoadFailed,"Linked Play ended before confirming the loaded input.")); return true;
            default:
                Fault(new(PlayErrorCode.InvalidMessage,"The game sent an unexpected linked Play message.")); return true;
        }
    }

    /// <summary>Reports the owned control's actual EOF/exit. During cooperative stop it completes; an unannounced
    /// exit while Starting/Running faults. Terminal/never-started notifications are ignored, never resurrecting it.</summary>
    public bool Exited(string? reason)
    {
        if(IsDisposed || State is LinkedPlayState.Created or LinkedPlayState.Stopped or LinkedPlayState.Faulted) return false;
        Tick();
        if(State==LinkedPlayState.Faulted) return false;
        if(State==LinkedPlayState.Stopping) Complete();
        else Fault(new(State==LinkedPlayState.Starting ? PlayErrorCode.LoadFailed : PlayErrorCode.RuntimeFailed,
            Plain("Linked Play exited unexpectedly."+(string.IsNullOrWhiteSpace(reason) ? "" : " "+reason))));
        return true;
    }

    /// <summary>Checks monotonic elapsed time. A reply exactly at its deadline is late; Running has no hidden
    /// maximum duration. Fault/cleanup happens once, even if Tick is called repeatedly.</summary>
    public void Tick()
    {
        if(IsDisposed) return;
        if(State==LinkedPlayState.Starting && clock.GetElapsedTime(stateAt)>=startTimeout)
            Fault(new(PlayErrorCode.LoadFailed,"Linked Play timed out waiting for loaded-input readiness."));
        else if(State==LinkedPlayState.Stopping && clock.GetElapsedTime(stateAt)>=stopTimeout)
            Fault(new(PlayErrorCode.RuntimeFailed,"Linked Play timed out waiting for stop completion."));
    }

    private bool Ignore() { if(IgnoredMessages<long.MaxValue) IgnoredMessages++; return false; }
    private void Complete() { State=LinkedPlayState.Stopped; Capabilities=PlayCapability.None; AbortOnce(); }
    private void Fault(PlayProtocolError error)
    {
        State=LinkedPlayState.Faulted; Capabilities=PlayCapability.None; Error=error; AbortOnce();
    }
    private void AbortOnce()
    {
        if(!attempted || aborted) return;
        aborted=true;
        try { control.AbortOwned(); }
        catch(Exception error) when(error is not OutOfMemoryException)
        { CleanupError=new(PlayErrorCode.RuntimeFailed,Plain("Could not release owned linked Play resources: "+error.Message)); }
    }
    private static string Plain(string text)=>new(text.Select(c=>char.IsControl(c) && c is not ('\n' or '\r' or '\t') ? ' ' : c)
        .Take(PlayProtocol.MaxErrorLength).ToArray());

    /// <summary>Ends this owner only. Active/partially started owned resources are aborted once; a never-started
    /// session has nothing to abort. No editor draft, file, history or unrelated resource is reachable here.</summary>
    public void Dispose()
    {
        if(IsDisposed) return;
        IsDisposed=true;
        if(State!=LinkedPlayState.Faulted) State=LinkedPlayState.Stopped;
        Capabilities=PlayCapability.None; AbortOnce();
    }
}
