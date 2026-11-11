using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bed.Editor.ImGui;

/// <summary>The source and stability of a node identity.</summary>
public enum UiIdKind
{
    /// <summary>ImGui semantic value.</summary>
    ImGui,
    /// <summary>Semantic semantic value.</summary>
    Semantic,
    /// <summary>Positional semantic value.</summary>
    Positional,
}
/// <summary>Semantic roles for a later accessibility adapter.</summary>
public enum UiRole
{
    /// <summary>Window semantic value.</summary>
    Window,
    /// <summary>MenuBar semantic value.</summary>
    MenuBar,
    /// <summary>Menu semantic value.</summary>
    Menu,
    /// <summary>MenuItem semantic value.</summary>
    MenuItem,
    /// <summary>Button semantic value.</summary>
    Button,
    /// <summary>Link semantic value.</summary>
    Link,
    /// <summary>Text semantic value.</summary>
    Text,
    /// <summary>TextField semantic value.</summary>
    TextField,
    /// <summary>Checkbox semantic value.</summary>
    Checkbox,
    /// <summary>Combo semantic value.</summary>
    Combo,
    /// <summary>ListItem semantic value.</summary>
    ListItem,
    /// <summary>Table semantic value.</summary>
    Table,
    /// <summary>Row semantic value.</summary>
    Row,
    /// <summary>Cell semantic value.</summary>
    Cell,
    /// <summary>TreeItem semantic value.</summary>
    TreeItem,
    /// <summary>Tab semantic value.</summary>
    Tab,
    /// <summary>Dialog semantic value.</summary>
    Dialog,
    /// <summary>Status semantic value.</summary>
    Status,
    /// <summary>A custom-drawn map or viewport.</summary>
    Canvas,
}
/// <summary>Descriptive supported actions; these flags never dispatch input.</summary>
[Flags]
public enum UiActions
{
    /// <summary>None semantic value.</summary>
    None = 0,
    /// <summary>Focus semantic value.</summary>
    Focus = 1,
    /// <summary>Invoke semantic value.</summary>
    Invoke = 2,
    /// <summary>SetValue semantic value.</summary>
    SetValue = 4,
    /// <summary>Toggle semantic value.</summary>
    Toggle = 8,
    /// <summary>Expand semantic value.</summary>
    Expand = 16,
    /// <summary>Select semantic value.</summary>
    Select = 32,
}
/// <summary>A screen-space rectangle; nonpositive extent is explicitly empty.</summary>
/// <param name="X">Left screen coordinate.</param>
/// <param name="Y">Top screen coordinate.</param>
/// <param name="Width">Horizontal extent.</param>
/// <param name="Height">Vertical extent.</param>
public readonly record struct UiBounds(float X, float Y, float Width, float Height)
{
    /// <summary>Whether this rectangle supplies no hit area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
    /// <summary>The union of nonempty child extents.</summary>
    public UiBounds Union(UiBounds other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        var x = Math.Min(X, other.X); var y = Math.Min(Y, other.Y);
        return new(x, y, Math.Max(X + Width, other.X + other.Width) - x, Math.Max(Y + Height, other.Y + other.Height) - y);
    }
}
/// <summary>Native text layout evidence, not a claim about occlusion by other windows or glyph rasterization.</summary>
/// <param name="Clip">Actual draw-list clip, intersected with a control's text clip when applicable.</param>
/// <param name="FullBounds">Screen-space extent calculated from the complete submitted label at the active font and wrap width.</param>
/// <param name="WrapWidth">Effective native wrap width, or minus one for unwrapped text.</param>
/// <param name="Truncated">Whether any of the full extent falls outside its effective clip.</param>
public readonly record struct UiTextEvidence(UiBounds Clip, UiBounds FullBounds, float WrapWidth, bool Truncated);

/// <summary>One immutable semantic item in a completed frame.</summary>
/// <param name="Id">Full 64-bit identity.</param>
/// <param name="Role">Semantic role.</param>
/// <param name="Label">Visible label without hidden control suffix.</param>
/// <param name="Bounds">Actual item rectangle or explicit group extent.</param>
/// <param name="Enabled">Whether interaction is enabled.</param>
/// <param name="DisabledReason">Supplied explanation or disabled fallback.</param>
/// <param name="Focused">Native keyboard focus.</param>
/// <param name="Hovered">Native item hover.</param>
/// <param name="Selected">Selection state.</param>
/// <param name="Expanded">Expansion state when applicable.</param>
/// <param name="Checked">Checkmark state when applicable.</param>
/// <param name="Value">Current field value, redacted for passwords.</param>
/// <param name="Parent">Preceding parent node index, or minus one.</param>
/// <param name="Actions">Actions supported by this wrapper, offered only while enabled.</param>
/// <param name="IdKind">Identity stability classification.</param>
/// <param name="TextEvidence">Native full-label layout evidence when the wrapper can establish it; otherwise null.</param>
/// <param name="AccessKey">Optional single uppercase access letter; present independently of Alt visibility.</param>
/// <param name="Tooltip">Full descriptive text, independent of hover.</param>
/// <param name="ViewportId">Owning native viewport ID; zero denotes the main application viewport.</param>
public readonly record struct UiNode(ulong Id, UiRole Role, string Label, UiBounds Bounds, bool Enabled,
    string? DisabledReason, bool Focused, bool Hovered, bool Selected, bool? Expanded, bool? Checked,
    string? Value, int Parent, UiActions Actions, UiIdKind IdKind = UiIdKind.ImGui, UiTextEvidence? TextEvidence = null, string? AccessKey = null, string? Tooltip = null, uint ViewportId = 0);

/// <summary>Owned copy. A snapshot stays valid across later frames; it never exposes pooled storage.</summary>
public sealed class UiSnapshot
{
    private readonly UiNode[] nodes;
    internal UiSnapshot(long frame, UiNode[] nodes) { Frame = frame; this.nodes = nodes; Nodes = Array.AsReadOnly(nodes); }
    /// <summary>Completed frame sequence number.</summary>
    public long Frame { get; }
    /// <summary>Immutable snapshot nodes in parent-before-child order.</summary>
    public IReadOnlyList<UiNode> Nodes { get; }
    /// <summary>Copy one viewport's nodes, preserving IDs/order and remapping parents within that viewport.
    /// A node whose direct parent belongs to another viewport becomes a root. An absent viewport yields an empty snapshot.</summary>
    public UiSnapshot ForViewport(uint viewportId)
    {
        var selected = new List<UiNode>();
        var indexes = new int[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node.ViewportId != viewportId) continue;
            var parent = node.Parent >= 0 && nodes[node.Parent].ViewportId == viewportId ? indexes[node.Parent] : -1;
            indexes[i] = selected.Count;
            selected.Add(node with { Parent = parent });
        }
        return new UiSnapshot(Frame, selected.ToArray());
    }
    /// <summary>Owned snapshots for the viewports present in this completed frame, in first-appearance order.</summary>
    public IReadOnlyDictionary<uint, UiSnapshot> PartitionByViewport()
    {
        var parts = new Dictionary<uint, List<UiNode>>();
        var indexes = new int[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (!parts.TryGetValue(node.ViewportId, out var part)) parts.Add(node.ViewportId, part = new());
            var parent = node.Parent >= 0 && nodes[node.Parent].ViewportId == node.ViewportId ? indexes[node.Parent] : -1;
            indexes[i] = part.Count;
            part.Add(node with { Parent = parent });
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<uint, UiSnapshot>(
            parts.ToDictionary(p => p.Key, p => new UiSnapshot(Frame, p.Value.ToArray())));
    }
    /// <summary>Indented text with escaped labels and state.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder();
            for (var i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i]; var depth = 0;
                for (var parent = n.Parent; parent >= 0; parent = nodes[parent].Parent) depth++;
                text.Append(' ', depth * 2).Append(n.Role.ToString().ToLowerInvariant()).Append(' ')
                    .Append(JsonSerializer.Serialize(n.Label));
                if (!n.Enabled) text.Append(" (disabled: ").Append((n.DisabledReason ?? "disabled").Replace("\r", "\\r").Replace("\n", "\\n")).Append(')');
                if (n.IdKind == UiIdKind.Positional) text.Append(" [idKind: positional]");
                if (n.AccessKey is not null) text.Append(" [accessKey: ").Append(n.AccessKey).Append(']');
                if (n.Focused) text.Append(" [focused]");
                if (n.Selected) text.Append(" [selected]");
                if (n.Expanded is bool expanded) text.Append(expanded ? " [expanded]" : " [collapsed]");
                if (n.Checked is bool check) text.Append(check ? " [checked]" : " [unchecked]");
                if (n.Value is not null) text.Append(" = ").Append(JsonSerializer.Serialize(n.Value));
                text.AppendLine();
            }
            return text.ToString();
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    /// <summary>Camel-case JSON snapshot, preserving full integer IDs.</summary>
    public string Json => JsonSerializer.Serialize(new { Frame, Nodes }, JsonOptions);
}

/// <summary>
/// MIXED: ImGui frame lifecycle and AccessKit-shaped semantics, with BED-owned double buffers.
/// Begin discards an unfinished frame; only Complete publishes. UI-thread writer; snapshot copies under a lock.
/// Two reusable capacity-growing buffers and reusable stacks avoid one object per recorded node per frame.
/// Snapshot allocation is explicit and separate from recording. No timing guarantee is claimed without measurement.
/// </summary>
public sealed class UiTree
{
    private readonly object gate = new();
    private UiNode[] writing = new UiNode[2048], complete = new UiNode[2048];
    private bool[] aggregates = new bool[2048];
    private int count, completeCount;
    private long frame;
    private bool active;
    internal int Count => count;
    internal bool Active => active;
    /// <summary>Common successful-render hook for every ImGui host.</summary>
    public static void EndFrame() => Ui.CompleteFrame();
    /// <summary>Discard unfinished work and begin recording a new frame.</summary>
    public void Begin()
    {
        // Release references left in this reusable buffer, without touching the published buffer.
        Array.Clear(writing, 0, count); Array.Clear(aggregates, 0, count);
        count = 0; active = true;
    }
    /// <summary>Append a node whose parent precedes it; optionally aggregate child bounds.</summary>
    public int Record(UiNode node, bool aggregateChildren = false)
    {
        if (!active) throw new InvalidOperationException("Begin the UI frame before recording.");
        if (node.Parent < -1 || node.Parent >= count) throw new ArgumentOutOfRangeException(nameof(node), "Parent must precede its child.");
        if (count == writing.Length) { Array.Resize(ref writing, count * 2); Array.Resize(ref aggregates, count * 2); }
        writing[count] = node; aggregates[count] = aggregateChildren;
        return count++;
    }
    internal UiNode At(int index) => writing[index];
    internal void Update(int index, UiNode node) => writing[index] = node;
    internal string ReuseText(ReadOnlySpan<char> text, bool value = false)
    {
        // Same traversal slots reuse strings without global interning or retaining obsolete arbitrary user text.
        if (count < completeCount)
        {
            var old = value ? complete[count].Value : complete[count].Label;
            if (old is not null && text.SequenceEqual(old.AsSpan())) return old;
        }
        return text.ToString();
    }
    /// <summary>Publish the completed frame atomically; retain it until the next completion.</summary>
    public void Complete()
    {
        if (!active) return;
        for (var i = count - 1; i >= 0; i--)
        {
            var parent = writing[i].Parent;
            if (parent >= 0 && aggregates[parent] && writing[parent].ViewportId == writing[i].ViewportId)
                writing[parent] = writing[parent] with { Bounds = writing[parent].Bounds.Union(writing[i].Bounds) };
        }
        lock (gate)
        {
            (writing, complete) = (complete, writing);
            (count, completeCount) = (completeCount, count);
            frame++; active = false;
        }
    }
    /// <summary>Copy the most recently completed frame under the publication lock.</summary>
    public UiSnapshot Snapshot()
    {
        lock (gate) return new UiSnapshot(frame, complete.AsSpan(0, completeCount).ToArray());
    }
}
