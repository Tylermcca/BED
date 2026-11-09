using Brutal.Authoring.Editor.ImGui;

namespace Brutal.Authoring.Editor.App;

/// <summary>UI-thread ownership of one accessibility bridge per secondary native-window lifetime.
/// The platform must call Create after hidden creation, Show before first visibility, and Destroy before HWND disposal.</summary>
public sealed class AccessKitViewportBridges(Func<nint, string, AccessKitWindowBridge> attach) : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<uint, (nint Hwnd, AccessKitWindowBridge Bridge)> windows = new();
    private bool closed;

    /// <summary>Attaches to a newly created, still-hidden secondary HWND. Main viewport zero is owned separately.</summary>
    public void Create(uint viewport, nint hiddenHwnd, string title)
    {
        Check();
        if (viewport == 0 || hiddenHwnd == 0) throw new ArgumentException("A secondary viewport and hidden HWND are required.");
        if (windows.ContainsKey(viewport) || windows.Values.Any(w => w.Hwnd == hiddenHwnd))
            throw new InvalidOperationException("A viewport or HWND already has an accessibility lifetime.");
        windows.Add(viewport, (hiddenHwnd, attach(hiddenHwnd, title)));
    }

    /// <summary>Runs the platform's show operation only after this exact native lifetime has attached.</summary>
    public void Show(uint viewport, nint hwnd, Action show)
    {
        Check(); _ = Find(viewport, hwnd); show();
    }

    /// <summary>Publishes only this viewport's complete nodes, including an empty frame when its last node disappears.</summary>
    public bool Publish(uint viewport, nint hwnd, UiSnapshot snapshot, string title, UiBounds bounds, float scaleX, float scaleY)
    {
        Check(); return Find(viewport, hwnd).Publish(snapshot.ForViewport(viewport), title, bounds, scaleX, scaleY);
    }

    /// <summary>Transfers queued native requests to the existing next-frame wrapper dispatcher.</summary>
    public void DrainRequests()
    {
        Check(); foreach (var window in windows.Values) window.Bridge.DrainRequests();
    }

    /// <summary>Detaches and cancels this lifetime's requests before the platform destroys its HWND.</summary>
    public void Destroy(uint viewport, nint hwnd, Action destroy)
    {
        Check(); var bridge = Find(viewport, hwnd); windows.Remove(viewport);
        try { bridge.Dispose(); } finally { destroy(); }
    }

    private AccessKitWindowBridge Find(uint viewport, nint hwnd) =>
        windows.TryGetValue(viewport, out var window) && window.Hwnd == hwnd ? window.Bridge
            : throw new InvalidOperationException("The secondary native window is not attached or its lifetime changed.");

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Viewport bridges belong to the UI thread.");
        ObjectDisposedException.ThrowIf(closed, this);
    }

    /// <summary>Detaches any remaining bridges; platform-window destruction remains the host's responsibility.</summary>
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Viewport bridges belong to the UI thread.");
        if (closed) return;
        closed = true;
        Exception? failure = null;
        foreach (var window in windows.Values)
        {
            try { window.Bridge.Dispose(); }
            catch (Exception error) { failure ??= error; }
        }
        windows.Clear();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
