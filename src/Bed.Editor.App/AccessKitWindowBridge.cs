using System.Collections.Concurrent;
using Bed.Editor.ImGui;

namespace Bed.Editor.App;

/// <summary>One HWND's accessibility tree and bounded managed request queue. Caches completed frames even while UIA is inactive, so later activation receives a full current tree with stable identities.</summary>
public sealed class AccessKitWindowBridge : IAccessKitUpdateSink, IDisposable
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly IAccessKitWindowApi windows;
    private readonly IAccessKitTreeApi trees;
    private readonly AccessKitProjection projection = new();
    private Dictionary<ulong, AccessKitProjectedNode> current;
    private ulong root, focus;
    private nint adapter, pendingEvents;
    private volatile bool closed;
    private bool activated;
    private readonly ConcurrentQueue<AccessKitNativeActionRequest> incoming = new();
    private readonly List<UiActionTicket> tickets = [];
    private int incomingCount;
    private string? lastActionOutcome;
    /// <summary>The most recent payload-free request outcome, including expiry as not available.</summary>
    public string? LastActionOutcome => Volatile.Read(ref lastActionOutcome);
    /// <summary>The last managed activation failure, if any; no exception is propagated through a native callback.</summary>
    public string? ActivationFailure { get; private set; }

    /// <summary>Attaches to a hidden, never-shown HWND on its owning thread. The injected APIs enable headless lifetime tests.</summary>
    public AccessKitWindowBridge(nint hiddenHwnd, string title, IAccessKitWindowApi windows, IAccessKitTreeApi trees)
    {
        ArgumentNullException.ThrowIfNull(windows); ArgumentNullException.ThrowIfNull(trees); ArgumentNullException.ThrowIfNull(title);
        if (hiddenHwnd == 0) throw new ArgumentException("A hidden HWND is required.", nameof(hiddenHwnd));
        this.windows = windows; this.trees = trees;
        current = new() { [0] = new(0, AccessKitProjectedRole.Window, title, null, null, new(0, 0, 0, 0), false, false, null, null, true, Array.Empty<ulong>()) };
        adapter = windows.NewSubclass(hiddenHwnd, Activate, QueueRequest);
        if (adapter == 0) throw new InvalidOperationException("AccessKit did not attach to the hidden window.");
    }
    /// <summary>Uses the vendored Windows x64 adapter. Native construction happens here, never in a headless host.</summary>
    public static AccessKitWindowBridge Attach(nint hiddenHwnd, string title) =>
        new(hiddenHwnd, title, new AccessKitNativeWindowApi(), new AccessKitNativeTreeApi());

    /// <summary>Publishes a completed frame, then raises its events after projection mutations have committed. Bounds are relative to the ImGui viewport; scales convert to client physical pixels.</summary>
    public bool Publish(UiSnapshot snapshot, string title, UiBounds viewport, float scaleX, float scaleY)
    {
        Check();
        var changed = projection.Submit(snapshot, title, viewport, scaleX, scaleY, this);
        RaisePendingEvents();
        return changed;
    }
    /// <inheritdoc/>
    public bool TryApply(AccessKitProjectedUpdate update)
    {
        Check();
        if (pendingEvents != 0) throw new InvalidOperationException("Raise pending AccessKit events before another update.");
        var next = update.Initial ? new Dictionary<ulong, AccessKitProjectedNode>() : new(current);
        foreach (var removed in update.RemovedIds) next.Remove(removed);
        foreach (var node in update.ChangedNodes) next[node.Id] = node;
        nint events = 0;
        if (activated)
        {
            using var encoded = AccessKitTreeEncoder.Encode(update, trees);
            events = windows.UpdateIfActive(adapter, encoded);
        }
        // Even an inactive adapter must retain the complete latest managed state for later activation.
        current = next; root = update.Root; focus = update.Focus;
        pendingEvents = events;
        return true;
    }
    private void QueueRequest(AccessKitNativeActionRequest request)
    {
        if (closed) return;
        if (Interlocked.Increment(ref incomingCount) > 128)
        { Interlocked.Decrement(ref incomingCount); Volatile.Write(ref lastActionOutcome, "not available"); return; }
        incoming.Enqueue(request);
        if (closed) while (incoming.TryDequeue(out _)) Interlocked.Decrement(ref incomingCount);
    }
    /// <summary>Drains bounded native requests on the UI thread before the next ImGui frame; wrappers retain final availability and validation.</summary>
    public void DrainRequests()
    {
        Check();
        foreach (var ticket in tickets)
            if (ticket.Outcome != UiActionOutcome.Pending) Volatile.Write(ref lastActionOutcome, ticket.Message);
        tickets.RemoveAll(t => t.Outcome != UiActionOutcome.Pending);
        while (incoming.TryDequeue(out var request))
        {
            Interlocked.Decrement(ref incomingCount);
            if (tickets.Count >= 128 || !current.TryGetValue(request.Target, out var node) || node.SourceId is not { } source || !node.StableIdentity || node.Disabled)
            { Volatile.Write(ref lastActionOutcome, "not available"); continue; }
            var action = request.Action switch
            {
                AccessKitNativeActionKind.Focus => UiActions.Focus,
                AccessKitNativeActionKind.SetValue => UiActions.SetValue,
                AccessKitNativeActionKind.Click when node.Role == AccessKitProjectedRole.CheckBox => UiActions.Toggle,
                AccessKitNativeActionKind.Click when node.Role == AccessKitProjectedRole.ListItem => UiActions.Select,
                AccessKitNativeActionKind.Click => UiActions.Invoke,
                _ => UiActions.None,
            };
            if (action == 0 || (node.Actions & action) == 0) { Volatile.Write(ref lastActionOutcome, "not available"); continue; }
            var queued = Ui.RequestAction(source, action, request.Value);
            Volatile.Write(ref lastActionOutcome, queued.Message);
            if (queued.Outcome == UiActionOutcome.Pending) tickets.Add(queued);
        }
    }
    /// <summary>Raises and consumes one pending event batch without holding a tree mutation or native update lock.</summary>
    public void RaisePendingEvents()
    {
        Check();
        var events = pendingEvents; pendingEvents = 0;
        if (events != 0) windows.RaiseEvents(events);
    }
    private nint Activate()
    {
        if (closed || Environment.CurrentManagedThreadId != owner) return 0;
        try
        {
            using var encoded = AccessKitTreeEncoder.Encode(new(true, root, focus, current.Values.ToArray(), Array.Empty<ulong>()), trees);
            var pointer = encoded.Transfer();
            activated = true;
            ActivationFailure = null;
            return pointer;
        }
        catch (Exception error) { ActivationFailure = error.Message; return 0; }
    }
    private void Check()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("AccessKit HWND lifetime belongs to the window thread.");
        if (closed) throw new ObjectDisposedException(nameof(AccessKitWindowBridge));
    }
    /// <summary>Detaches before HWND destruction, consumes pending events and releases callback roots. Must be called on the creating thread.</summary>
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("AccessKit HWND lifetime belongs to the window thread.");
        if (closed) return;
        closed = true;
        while (incoming.TryDequeue(out _)) Interlocked.Decrement(ref incomingCount);
        foreach (var ticket in tickets) Ui.CancelAction(ticket);
        tickets.Clear();
        // Reentrant activation while events are raised now safely defers.
        var events = pendingEvents; pendingEvents = 0;
        try { if (events != 0) windows.RaiseEvents(events); }
        finally { windows.FreeSubclass(adapter); adapter = 0; current.Clear(); }
    }
}
