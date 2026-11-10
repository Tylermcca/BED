namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Managed names matching AccessKit C 0.23.2 roles; these values are not native ABI ordinals.</summary>
public enum AccessKitProjectedRole
{
    /// <summary>A window.</summary>
    Window,
    /// <summary>A menu bar.</summary>
    MenuBar,
    /// <summary>A menu.</summary>
    Menu,
    /// <summary>A menu item.</summary>
    MenuItem,
    /// <summary>A button.</summary>
    Button,
    /// <summary>A link.</summary>
    Link,
    /// <summary>Static text, supplied through the node value.</summary>
    Label,
    /// <summary>A text input.</summary>
    TextInput,
    /// <summary>A check box.</summary>
    CheckBox,
    /// <summary>A combo box.</summary>
    ComboBox,
    /// <summary>A list item.</summary>
    ListItem,
    /// <summary>A table.</summary>
    Table,
    /// <summary>A row.</summary>
    Row,
    /// <summary>A cell.</summary>
    Cell,
    /// <summary>A tree item.</summary>
    TreeItem,
    /// <summary>A tab.</summary>
    Tab,
    /// <summary>A dialog.</summary>
    Dialog,
    /// <summary>A status region, encoded as polite and atomic by the native adapter.</summary>
    Status,
    /// <summary>A custom-drawn canvas, retaining its value and selectable children.</summary>
    Canvas,
}

/// <summary>One managed node for the pinned native encoder. Children and bounds are fully specified on replacement.</summary>
public sealed record AccessKitProjectedNode(ulong Id, AccessKitProjectedRole Role, string? Label, string? Value,
    string? Description, UiBounds Bounds, bool Disabled, bool Selected, bool? Expanded, bool? Checked,
    bool StableIdentity, IReadOnlyList<ulong> Children)
{
    /// <summary>Optional uppercase mnemonic, present independently of whether Alt is held. Copied from UiNode without depending on Alt state.</summary>
    public string? AccessKey { get; init; }
    /// <summary>The source UiNode ID; null for the synthetic window root.</summary>
    public ulong? SourceId { get; init; }
    /// <summary>Only enabled, stable actions implemented by the existing wrapper.</summary>
    public UiActions Actions { get; init; }
}

/// <summary>An atomic managed update. Removed IDs are bookkeeping; the native tree removes nodes through parent child lists.</summary>
public sealed record AccessKitProjectedUpdate(bool Initial, ulong Root, ulong Focus,
    IReadOnlyList<AccessKitProjectedNode> ChangedNodes, IReadOnlyList<ulong> RemovedIds);

/// <summary>A managed submission boundary for a native adapter or a headless fake; it must accept the tree atomically, decline it, or throw.</summary>
public interface IAccessKitUpdateSink
{
    /// <summary>Returns true only after accepting the tree; false (inactive) or an exception retains the previous baseline. Raise native events later, outside this call.</summary>
    bool TryApply(AccessKitProjectedUpdate update);
}

/// <summary>
/// Projects one host window's completed UiTree frames into AccessKit-shaped managed updates, without native calls.
/// Create on and use from the UI thread, one instance per native window lifetime. The host must supply its actual
/// viewport origin, logical size and physical-pixel scale; an ImGui panel is not an OS-window origin.
/// This class does not attach an HWND, encode the C ABI or dispatch actions.
/// </summary>
public sealed class AccessKitProjection
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private Dictionary<ulong, ulong> identities = [];
    private Dictionary<ulong, AccessKitProjectedNode> previous = [];
    private ulong nextIdentity = 1;
    private ulong previousFocus;
    private long previousFrame = -1;

    /// <summary>Maps each existing UiTree role to its AccessKit counterpart; unknown roles are refused.</summary>
    public static AccessKitProjectedRole RoleOf(UiRole role) => role switch
    {
        UiRole.Window => AccessKitProjectedRole.Window,
        UiRole.MenuBar => AccessKitProjectedRole.MenuBar,
        UiRole.Menu => AccessKitProjectedRole.Menu,
        UiRole.MenuItem => AccessKitProjectedRole.MenuItem,
        UiRole.Button => AccessKitProjectedRole.Button,
        UiRole.Link => AccessKitProjectedRole.Link,
        UiRole.Text => AccessKitProjectedRole.Label,
        UiRole.TextField => AccessKitProjectedRole.TextInput,
        UiRole.Checkbox => AccessKitProjectedRole.CheckBox,
        UiRole.Combo => AccessKitProjectedRole.ComboBox,
        UiRole.ListItem => AccessKitProjectedRole.ListItem,
        UiRole.Table => AccessKitProjectedRole.Table,
        UiRole.Row => AccessKitProjectedRole.Row,
        UiRole.Cell => AccessKitProjectedRole.Cell,
        UiRole.TreeItem => AccessKitProjectedRole.TreeItem,
        UiRole.Tab => AccessKitProjectedRole.Tab,
        UiRole.Dialog => AccessKitProjectedRole.Dialog,
        UiRole.Status => AccessKitProjectedRole.Status,
        UiRole.Canvas => AccessKitProjectedRole.Canvas,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>
    /// Submits changed/new nodes with complete properties and child lists, or returns false for an unchanged frame.
    /// The synthetic native-window root is ID zero; recorded IDs, including zero and ulong.MaxValue, are remapped.
    /// Input bounds are in desktop logical coordinates; output bounds are relative physical pixels, top-down.
    /// </summary>
    public bool Submit(UiSnapshot snapshot, string windowTitle, UiBounds viewport, float scaleX, float scaleY, IAccessKitUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(windowTitle);
        ArgumentNullException.ThrowIfNull(sink);
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Submit on the window's UI thread.");
        if (snapshot.Frame < previousFrame) throw new InvalidOperationException("A completed UI frame cannot replace a newer frame.");
        Validate(viewport);
        if (viewport.IsEmpty || !float.IsFinite(scaleX) || !float.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewport), "A visible viewport and positive finite physical scale are required.");

        var next = nextIdentity;
        var mappedIds = new Dictionary<ulong, ulong>();
        var nativeIds = new ulong[snapshot.Nodes.Count];
        var childLists = new List<ulong>[snapshot.Nodes.Count];
        var rootChildren = new List<ulong>();
        for (var i = 0; i < snapshot.Nodes.Count; i++)
        {
            var node = snapshot.Nodes[i];
            Validate(node.Bounds);
            if (node.Parent < -1 || node.Parent >= i) throw new ArgumentException("Parents must precede their children.", nameof(snapshot));
            if (mappedIds.ContainsKey(node.Id)) throw new ArgumentException("UI node IDs must be unique within the host window.", nameof(snapshot));
            if (!identities.TryGetValue(node.Id, out var id))
            {
                if (next == ulong.MaxValue) throw new InvalidOperationException("The window's accessibility identity space is exhausted.");
                id = next++;
            }
            mappedIds.Add(node.Id, id);
            nativeIds[i] = id;
            childLists[i] = [];
            if (node.Parent == -1) rootChildren.Add(id);
            else childLists[node.Parent].Add(id);
        }

        var nodes = new Dictionary<ulong, AccessKitProjectedNode>();
        var rootBounds = Physical(new UiBounds(viewport.X, viewport.Y, viewport.Width, viewport.Height), viewport, scaleX, scaleY);
        nodes.Add(0, new(0, AccessKitProjectedRole.Window, windowTitle, null, null, rootBounds,
            false, false, null, null, true, rootChildren.AsReadOnly()));
        ulong focus = 0;
        var focusedDepth = -1;
        for (var i = 0; i < snapshot.Nodes.Count; i++)
        {
            var node = snapshot.Nodes[i];
            var role = RoleOf(node.Role);
            nodes.Add(nativeIds[i], new(nativeIds[i], role, role == AccessKitProjectedRole.Label ? null : node.Label,
                role == AccessKitProjectedRole.Label ? node.Label : node.Value,
                Description(node), Physical(node.Bounds, viewport, scaleX, scaleY),
                !node.Enabled, node.Selected, node.Expanded, node.Checked, node.IdKind != UiIdKind.Positional,
                childLists[i].AsReadOnly()) { AccessKey = node.AccessKey, SourceId = node.Id, Actions = node.Enabled && node.IdKind != UiIdKind.Positional ? node.Actions & Supported(node.Role) : UiActions.None });
            if (!node.Focused) continue;
            var depth = 0;
            for (var parent = node.Parent; parent >= 0; parent = snapshot.Nodes[parent].Parent) depth++;
            // A focused containing scope must not displace the actual focused descendant.
            if (depth > focusedDepth) { focus = nativeIds[i]; focusedDepth = depth; }
        }

        var changed = nodes.Values.Where(n => !previous.TryGetValue(n.Id, out var old) || !Same(n, old)).ToArray();
        var removed = previous.Keys.Where(id => !nodes.ContainsKey(id)).ToArray();
        var initial = previous.Count == 0;
        var submit = initial || changed.Length > 0 || removed.Length > 0 || focus != previousFocus;
        if (submit && !sink.TryApply(new(initial, 0, focus, Array.AsReadOnly(changed), Array.AsReadOnly(removed))))
            return false;
        // Commit only after the sink returns. A rejected update cannot poison the next diff.
        identities = mappedIds;
        previous = nodes;
        nextIdentity = next;
        previousFocus = focus;
        previousFrame = snapshot.Frame;
        return submit;
    }


    private static void Validate(UiBounds bounds)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height))
            throw new ArgumentOutOfRangeException(nameof(bounds), "Accessibility bounds must be finite.");
    }

    private static UiBounds Physical(UiBounds bounds, UiBounds viewport, float scaleX, float scaleY)
    {
        if (bounds.IsEmpty) return new(0, 0, 0, 0);
        var physical = new UiBounds((bounds.X - viewport.X) * scaleX, (bounds.Y - viewport.Y) * scaleY,
            bounds.Width * scaleX, bounds.Height * scaleY);
        Validate(physical);
        return physical;
    }

    private static UiActions Supported(UiRole role) => role switch
    {
        UiRole.Button or UiRole.Link or UiRole.MenuItem => UiActions.Focus | UiActions.Invoke,
        UiRole.ListItem => UiActions.Focus | UiActions.Select,
        UiRole.Checkbox => UiActions.Focus | UiActions.Toggle,
        UiRole.TextField => UiActions.Focus | UiActions.SetValue,
        _ => UiActions.None,
    };
    private static string? Description(UiNode node)
    {
        var parts = new[] { node.Tooltip, node.Enabled ? null : node.DisabledReason ?? "disabled", node.TextEvidence?.Truncated == true ? node.Label : null }
            .Where(p => !string.IsNullOrWhiteSpace(p)).SelectMany(p => p!.Split('\n')).Distinct().ToArray();
        return parts.Length == 0 ? null : string.Join("\n", parts);
    }
    private static bool Same(AccessKitProjectedNode a, AccessKitProjectedNode b) =>
        a.Id == b.Id && a.Role == b.Role && a.Label == b.Label && a.Value == b.Value && a.Description == b.Description && a.AccessKey == b.AccessKey && a.SourceId == b.SourceId && a.Actions == b.Actions
        && a.Bounds == b.Bounds && a.Disabled == b.Disabled && a.Selected == b.Selected && a.Expanded == b.Expanded
        && a.Checked == b.Checked && a.StableIdentity == b.StableIdentity && a.Children.SequenceEqual(b.Children);
}
