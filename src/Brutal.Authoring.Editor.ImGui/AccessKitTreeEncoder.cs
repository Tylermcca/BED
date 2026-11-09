namespace Brutal.Authoring.Editor.ImGui;

/// <summary>An owned C tree update, freed on its creating thread or explicitly transferred to AccessKit. No finalizer can call the thread-affine API.</summary>
public sealed class AccessKitEncodedUpdate : IDisposable
{
    private readonly IAccessKitTreeApi api;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private nint pointer;
    internal AccessKitEncodedUpdate(IAccessKitTreeApi api, nint pointer) { this.api = api; this.pointer = pointer; }
    /// <summary>Whether this object still owns an update. Read only on the creating thread, before entering a native callback.</summary>
    public bool IsOwned { get { CheckThread(); return pointer != 0; } }
    /// <summary>Transfers ownership once to the native update callback; after this the caller must let AccessKit free it.</summary>
    public nint Transfer()
    {
        CheckThread();
        if (pointer == 0) throw new ObjectDisposedException(nameof(AccessKitEncodedUpdate));
        var result = pointer; pointer = 0; return result;
    }
    /// <summary>Frees an untransferred update exactly once.</summary>
    public void Dispose()
    {
        CheckThread();
        if (pointer == 0) return;
        api.FreeUpdate(pointer); pointer = 0;
    }
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("AccessKit update ownership belongs to its creating thread.");
    }
}

/// <summary>Encodes the managed projection and implemented action metadata using AccessKit C 0.23.2 ownership rules. Window lifetime and request dispatch belong to the host.</summary>
public static class AccessKitTreeEncoder
{
    /// <summary>The byte ordinal in the pinned C header, deliberately independent of managed enum order.</summary>
    public static byte NativeRole(AccessKitProjectedRole role) => role switch
    {
        AccessKitProjectedRole.Window => 133, AccessKitProjectedRole.MenuBar => 93,
        AccessKitProjectedRole.Menu => 30, AccessKitProjectedRole.MenuItem => 11,
        AccessKitProjectedRole.Button => 18, AccessKitProjectedRole.Link => 5,
        AccessKitProjectedRole.Label => 3, AccessKitProjectedRole.TextInput => 17,
        AccessKitProjectedRole.CheckBox => 15, AccessKitProjectedRole.ComboBox => 56,
        AccessKitProjectedRole.ListItem => 7, AccessKitProjectedRole.Table => 25,
        AccessKitProjectedRole.Row => 6, AccessKitProjectedRole.Cell => 2,
        AccessKitProjectedRole.TreeItem => 9, AccessKitProjectedRole.Tab => 120,
        AccessKitProjectedRole.Dialog => 66, AccessKitProjectedRole.Status => 116,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>Encodes an AccessKitProjection result. Any failure frees allocations not already transferred. Removed nodes use changed parent child lists; incremental graph validity remains the projection's responsibility.</summary>
    public static AccessKitEncodedUpdate Encode(AccessKitProjectedUpdate update, IAccessKitTreeApi api)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(api);
        // Validate before crossing the C boundary; AccessKit's Rust release panics abort the process.
        var ids = new HashSet<ulong>();
        foreach (var node in update.ChangedNodes)
        {
            _ = NativeRole(node.Role);
            if (node.AccessKey is { } key && (key.Length != 1 || key[0] < 'A' || key[0] > 'Z'))
                throw new ArgumentException("An access key must be one uppercase A-Z character.", nameof(update));
            if (!ids.Add(node.Id)) throw new ArgumentException("An update cannot replace the same node twice.", nameof(update));
            if (node.Children.Contains(node.Id) || node.Children.Distinct().Count() != node.Children.Count)
                throw new ArgumentException("A node cannot contain itself or duplicate children.", nameof(update));
            var b = node.Bounds;
            if (!float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Width) || !float.IsFinite(b.Height) || b.Width < 0 || b.Height < 0)
                throw new ArgumentException("Node bounds must be finite and non-negative in size.", nameof(update));
        }
        if (update.Initial)
        {
            if (!ids.Contains(update.Root) || !ids.Contains(update.Focus))
                throw new ArgumentException("The initial update must contain its root and focus.", nameof(update));
            var nodes = update.ChangedNodes.ToDictionary(n => n.Id);
            var visited = new HashSet<ulong>();
            var pending = new Stack<ulong>(); pending.Push(update.Root);
            while (pending.TryPop(out var id))
            {
                if (!nodes.TryGetValue(id, out var node) || !visited.Add(id))
                    throw new ArgumentException("The initial tree must be connected, without missing or repeated children.", nameof(update));
                foreach (var child in node.Children) pending.Push(child);
            }
            if (visited.Count != nodes.Count) throw new ArgumentException("The initial tree cannot contain orphan nodes.", nameof(update));
        }
        var pointer = api.NewUpdate((nuint)update.ChangedNodes.Count, update.Focus);
        if (pointer == 0) throw new OutOfMemoryException();
        var owned = new AccessKitEncodedUpdate(api, pointer);
        try
        {
            if (update.Initial) api.SetTree(pointer, update.Root);
            foreach (var node in update.ChangedNodes)
            {
                var native = api.NewNode(NativeRole(node.Role));
                if (native == 0) throw new OutOfMemoryException();
                try
                {
                    if (node.Label is { } label) api.Label(native, label);
                    if (node.Value is { } value) api.Value(native, value);
                    if (node.Description is { } description) api.Description(native, description);
                    if (node.AccessKey is { } accessKey) api.AccessKey(native, accessKey);
                    var b = node.Bounds;
                    api.Bounds(native, new(b.X, b.Y, (double)b.X + b.Width, (double)b.Y + b.Height));
                    api.Children(native, node.Children);
                    if (node.Disabled) api.Disabled(native);
                    api.Selected(native, node.Selected);
                    if (node.Expanded is { } expanded) api.Expanded(native, expanded);
                    if (node.Checked is { } toggled) api.Toggled(native, toggled ? (byte)1 : (byte)0);
                    var actions = node.Disabled || !node.StableIdentity ? UiActions.None : node.Actions;
                    if ((actions & UiActions.Focus) != 0) api.AddAction(native, 1);
                    if ((actions & (UiActions.Invoke | UiActions.Select | UiActions.Toggle)) != 0) api.AddAction(native, 0);
                    if ((actions & UiActions.SetValue) != 0) api.AddAction(native, 20);
                    api.PushNode(pointer, node.Id, native);
                    native = 0; // ownership was transferred
                }
                finally { if (native != 0) api.FreeNode(native); }
            }
            return owned;
        }
        catch { owned.Dispose(); throw; }
    }
}
