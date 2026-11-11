using System.Collections.Concurrent;
using Brutal.GlfwApi;
using Bed.Editor.ImGui;

namespace Bed.Editor.App;

/// <summary>An owned drop captured in window-content coordinates before the next frame.</summary>
/// <param name="Paths">Read-only detached path strings; no file content is read.</param>
/// <param name="X">Horizontal cursor position at the callback.</param>
/// <param name="Y">Vertical cursor position at the callback.</param>
public sealed record WindowDrop(IReadOnlyList<string> Paths, double X, double Y)
{
    /// <summary>Finds the last drawn containing window in a completed frame, never a child item's extent.</summary>
    public UiNode? TargetWindow(UiSnapshot frame)
    {
        for (var i = frame.Nodes.Count - 1; i >= 0; i--)
        {
            var node = frame.Nodes[i]; var b = node.Bounds;
            if (node.Role is not (UiRole.Window or UiRole.Dialog) || b.IsEmpty) continue;
            if (X >= b.X && Y >= b.Y && X < b.X + b.Width && Y < b.Y + b.Height) return node;
        }
        return null;
    }
}

/// <summary>Copies GLFW's managed drop paths during the callback and queues owned batches for the host's next UI-thread drain.
/// It never inspects, opens, copies, moves or deletes a file. Register/unregister <see cref="Callback"/> in the window owner;
/// consume batches only through the editor's approved operations. Construct, post, drain and dispose on that host thread.</summary>
public sealed class WindowDropQueue : IDisposable
{
    private readonly ConcurrentQueue<WindowDrop> pending = new();
    private bool disposed;

    /// <summary>Creates a rooted callback without initializing GLFW or opening a window.</summary>
    public WindowDropQueue()
    {
        Callback = (window, count, paths) =>
        {
            if (disposed) return;
            if (count <= 0 || paths == null || count != paths.Length) { Problem = "The drop did not contain a readable file or folder."; return; }
            try
            {
                var owned = new string[count];
                for (var i = 0; i < count; i++) owned[i] = paths[i] ?? "";
                Post(owned, window is null ? 0 : window.CursorX, window is null ? 0 : window.CursorY);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // No editor work or exception is allowed to cross the unmanaged callback boundary.
                Problem = "The dropped paths could not be read.";
            }
        };
    }

    /// <summary>The actual GLFW callback. Keep this queue alive while it is registered; the window owner must detach it before disposal.</summary>
    public GlfwDropCallback Callback { get; }

    /// <summary>The last capture refusal, or null after a valid batch; this is transport status, not a file-validation result.</summary>
    public string? Problem { get; private set; }

    /// <summary>Posts a synthetic or captured batch through the same owned-copy path. Returns false for an empty/unreadable batch or a closed queue.</summary>
    public bool Post(IReadOnlyList<string> paths, double x = 0, double y = 0)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (disposed) { Problem = "This window is closed; the drop was not used."; return false; }
        if (paths.Count == 0 || paths.Any(string.IsNullOrWhiteSpace))
        { Problem = "The drop did not contain a readable file or folder."; return false; }
        pending.Enqueue(new WindowDrop(Array.AsReadOnly(paths.ToArray()), x, y));
        Problem = null;
        return true;
    }

    /// <summary>Removes one detached batch in arrival order. No receiving action or filesystem operation is invoked here.</summary>
    public bool TryTake(out IReadOnlyList<string> paths)
    {
        if (!disposed && pending.TryDequeue(out var found)) { paths = found.Paths; return true; }
        paths = Array.Empty<string>(); return false;
    }

    /// <summary>Removes one owned event including its captured cursor before the next frame.</summary>
    public bool TryTakeDrop(out WindowDrop? drop)
    {
        if (!disposed && pending.TryDequeue(out var found)) { drop = found; return true; }
        drop = null; return false;
    }

    /// <summary>Discards unconsumed batches and refuses late posts. Detaching the native callback remains the window owner's job.</summary>
    public void Dispose()
    {
        disposed = true;
        pending.Clear();
    }
}
