using Bed.Editor.ImGui;
using System.Runtime.InteropServices;
using Xunit;

namespace Bed.Tests;

public sealed class AccessKitNativeTreeA457Tests
{
    private static AccessKitProjectedNode Node(ulong id = 0) => new(id, AccessKitProjectedRole.TreeItem,
        "naÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¯ve\0ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¨ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¨", "value", "description", new(1, 2, 30, 40), true, true, false, true, true, []) { AccessKey = "F" };
    private static AccessKitProjectedUpdate Update() => new(true, 0, 0, [Node()], []);

    [Fact]
    public void Encoding_preserves_all_fields_and_transfer_frees_nothing_twice()
    {
        var api = new Fake();
        using var owned = AccessKitTreeEncoder.Encode(Update(), api);
        Assert.Equal((nuint)1, api.Capacity);
        Assert.Equal(0UL, api.Focus);
        Assert.Equal(0UL, api.Root);
        Assert.Equal((byte)9, api.Role);
        Assert.Equal("naÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¯ve\0ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¨ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¨", api.LabelText);
        Assert.Equal("value", api.ValueText);
        Assert.Equal("description", api.DescriptionText);
        Assert.Equal("F", api.AccessKeyText);
        Assert.Equal(new AccessKitNativeRect(1, 2, 31, 42), api.Rect);
        Assert.True(api.IsDisabled);
        Assert.True(api.IsSelected);
        Assert.False(api.IsExpanded);
        Assert.Equal((byte)1, api.Checked);
        Assert.Equal(new ulong[] { 0 }, api.Ids);
        var raw = owned.Transfer();
        Assert.NotEqual(0, raw);
        Assert.Throws<ObjectDisposedException>(() => owned.Transfer());
        owned.Dispose();
        Assert.Equal(0, api.UpdateFrees);
        Assert.Equal(0, api.NodeFrees);
        api.FreeUpdate(raw); // model the C callback's ownership after transfer
        Assert.Equal(1, api.UpdateFrees);
        Assert.Empty(api.LiveNodes);
    }

    [Fact]
    public void Dispose_frees_update_once_and_all_its_transferred_nodes()
    {
        var api = new Fake();
        var owned = AccessKitTreeEncoder.Encode(Update(), api);
        owned.Dispose(); owned.Dispose();
        Assert.Equal(1, api.UpdateFrees);
        Assert.Equal(0, api.NodeFrees);
        Assert.Empty(api.LiveNodes);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("new-node")]
    [InlineData("label")]
    [InlineData("value")]
    [InlineData("description")]
    [InlineData("access-key")]
    [InlineData("bounds")]
    [InlineData("children")]
    [InlineData("disabled")]
    [InlineData("selected")]
    [InlineData("expanded")]
    [InlineData("toggled")]
    [InlineData("push")]
    public void Failure_before_transfer_frees_all_owned_allocations(string fail)
    {
        var api = new Fake { Fail = fail };
        Assert.Throws<InvalidOperationException>(() => AccessKitTreeEncoder.Encode(Update(), api));
        Assert.Equal(1, api.UpdateFrees);
        Assert.Empty(api.LiveNodes);
        Assert.Empty(api.Owned);
        Assert.Equal(fail is "tree" or "new-node" ? 0 : 1, api.NodeFrees);
    }

    [Fact]
    public void Second_node_failure_releases_previously_transferred_node_with_update()
    {
        var api = new Fake { FailSecondNode = true };
        var update = new AccessKitProjectedUpdate(true, 0, 1, [Node() with { Children = new ulong[] { 1 } }, Node(1)], []);
        Assert.Throws<InvalidOperationException>(() => AccessKitTreeEncoder.Encode(update, api));
        Assert.Equal(1, api.UpdateFrees);
        Assert.Equal(0, api.NodeFrees);
        Assert.Empty(api.LiveNodes);
    }

    [Fact]
    public void Zero_allocation_result_is_refused_without_an_invalid_free()
    {
        var api = new Fake { ZeroUpdate = true };
        Assert.Throws<OutOfMemoryException>(() => AccessKitTreeEncoder.Encode(Update(), api));
        Assert.Equal(0, api.UpdateFrees);
        api = new Fake { ZeroNode = true };
        Assert.Throws<OutOfMemoryException>(() => AccessKitTreeEncoder.Encode(Update(), api));
        Assert.Equal(1, api.UpdateFrees);
        Assert.Equal(0, api.NodeFrees);
    }

    [Fact]
    public void Changed_parent_children_preserve_order_and_remove_without_rebuilding_tree_info()
    {
        var api = new Fake();
        var node = Node(7) with { Children = new ulong[] { 19, 12 }, Disabled = false, Selected = false, Expanded = null, Checked = false };
        using var owned = AccessKitTreeEncoder.Encode(new(false, 0, 12, [node], [20]), api);
        Assert.Null(api.Root);
        Assert.Equal(12UL, api.Focus);
        Assert.Equal(new ulong[] { 19, 12 }, api.ChildIds);
        Assert.Equal(new ulong[] { 7 }, api.Ids);
        Assert.False(api.IsDisabled);
        Assert.False(api.IsSelected);
        Assert.Null(api.IsExpanded);
        Assert.Equal((byte)0, api.Checked);
    }

    [Fact]
    public void Focus_only_update_has_no_nodes_or_tree_reset()
    {
        var api = new Fake();
        using var owned = AccessKitTreeEncoder.Encode(new(false, 0, 7, [], []), api);
        Assert.Empty(api.Ids); Assert.Null(api.Root); Assert.Equal(7UL, api.Focus);
    }

    [Theory]
    [InlineData(AccessKitProjectedRole.Window, 133)]
    [InlineData(AccessKitProjectedRole.MenuBar, 93)]
    [InlineData(AccessKitProjectedRole.Menu, 30)]
    [InlineData(AccessKitProjectedRole.MenuItem, 11)]
    [InlineData(AccessKitProjectedRole.Button, 18)]
    [InlineData(AccessKitProjectedRole.Link, 5)]
    [InlineData(AccessKitProjectedRole.Label, 3)]
    [InlineData(AccessKitProjectedRole.TextInput, 17)]
    [InlineData(AccessKitProjectedRole.CheckBox, 15)]
    [InlineData(AccessKitProjectedRole.ComboBox, 56)]
    [InlineData(AccessKitProjectedRole.ListItem, 7)]
    [InlineData(AccessKitProjectedRole.Table, 25)]
    [InlineData(AccessKitProjectedRole.Row, 6)]
    [InlineData(AccessKitProjectedRole.Cell, 2)]
    [InlineData(AccessKitProjectedRole.TreeItem, 9)]
    [InlineData(AccessKitProjectedRole.Tab, 120)]
    [InlineData(AccessKitProjectedRole.Dialog, 66)]
    [InlineData(AccessKitProjectedRole.Status, 116)]
    [InlineData(AccessKitProjectedRole.Canvas, 51)]
    public void Role_ordinals_match_the_pinned_header(AccessKitProjectedRole role, byte ordinal) =>
        Assert.Equal(ordinal, AccessKitTreeEncoder.NativeRole(role));

    [Fact]
    public void Bad_projection_is_refused_before_any_native_allocation()
    {
        var api = new Fake();
        Assert.Throws<ArgumentOutOfRangeException>(() => AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [Node() with { Role = (AccessKitProjectedRole)999 }] }, api));
        Assert.Throws<ArgumentException>(() => AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [Node() with { Bounds = new(float.NaN, 0, 1, 1) }] }, api));
        Assert.Throws<ArgumentException>(() => AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [Node() with { Bounds = new(0, 0, -1, 1) }] }, api));
        Assert.Throws<ArgumentException>(() => AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [] }, api));
        Assert.Throws<ArgumentException>(() => AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [Node(), Node()] }, api));
        Assert.Equal(0, api.NewUpdates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("f")]
    [InlineData("FF")]
    [InlineData("Alt+F")]
    [InlineData("1")]
    public void Invalid_access_key_metadata_is_refused_before_allocation(string key)
    {
        var api = new Fake();
        Assert.Throws<ArgumentException>(() => AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [Node() with { AccessKey = key }] }, api));
        Assert.Equal(0, api.NewUpdates);
    }

    [Fact]
    public void Absent_access_key_is_not_invented_from_label_or_value()
    {
        var api = new Fake();
        using var owned = AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [Node() with { AccessKey = null }] }, api);
        Assert.Null(api.AccessKeyText);
    }

    [Fact]
    public void Malformed_initial_graphs_are_refused_before_the_native_boundary()
    {
        var api = new Fake();
        void Refused(AccessKitProjectedUpdate update) => Assert.Throws<ArgumentException>(() => AccessKitTreeEncoder.Encode(update, api));
        Refused(Update() with { Focus = 9 });
        Refused(Update() with { ChangedNodes = [Node(), Node(1)] }); // orphan
        Refused(Update() with { ChangedNodes = [Node() with { Children = new ulong[] { 9 } }] });
        Refused(Update() with { ChangedNodes = [Node() with { Children = new ulong[] { 0 } }] });
        Refused(Update() with { ChangedNodes = [Node() with { Children = new ulong[] { 1, 1 } }, Node(1)] });
        Refused(Update() with { ChangedNodes = [Node() with { Children = new ulong[] { 1, 2 } }, Node(1) with { Children = new ulong[] { 2 } }, Node(2)] });
        Assert.Equal(0, api.NewUpdates);
    }

    [Fact]
    public void Cross_thread_ownership_is_refused_without_losing_the_original_handle()
    {
        var api = new Fake();
        using var owned = AccessKitTreeEncoder.Encode(Update(), api);
        Exception? failure = null;
        var other = new Thread(() => failure = Record.Exception(owned.Dispose));
        other.Start(); other.Join();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, api.UpdateFrees);
        owned.Dispose();
        Assert.Equal(1, api.UpdateFrees);
    }

    [Fact]
    public void Only_supported_enabled_stable_actions_become_native_ordinals()
    {
        var node = Node() with { Disabled = false, Actions = UiActions.Focus | UiActions.Invoke | UiActions.SetValue };
        var api = new Fake();
        using (AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [node] }, api))
            Assert.Equal(new byte[] { 1, 0, 20 }, api.Actions);
        foreach (var unavailable in new[] { node with { Disabled = true }, node with { StableIdentity = false } })
        {
            api = new Fake();
            using var owned = AccessKitTreeEncoder.Encode(Update() with { ChangedNodes = [unavailable] }, api);
            Assert.Empty(api.Actions);
        }
    }

    [Fact]
    public void Rect_is_four_sequential_doubles_not_float_coordinates() => Assert.Equal(32, Marshal.SizeOf<AccessKitNativeRect>());

    // A fake C ownership boundary. No DLL loading, native window, UIA or AccessKit call is performed by this class.
    internal sealed class Fake : IAccessKitTreeApi
    {
        public string? Fail;
        public bool ZeroUpdate, ZeroNode, FailSecondNode;
        public int NewUpdates, UpdateFrees, NodeFrees;
        public nuint Capacity;
        public ulong Focus;
        public ulong? Root;
        public byte Role, Checked;
        public string? LabelText, ValueText, DescriptionText, AccessKeyText;
        public AccessKitNativeRect Rect;
        public bool IsDisabled, IsSelected;
        public int SelectionCalls;
        public bool? IsExpanded;
        public List<ulong> Ids = [], ChildIds = [];
        public HashSet<nint> LiveNodes = [], Owned = [];
        private nint next = 20;
        private void Check(string op) { if (op == Fail) throw new InvalidOperationException(op); }
        public nint NewUpdate(nuint cap, ulong focus) { NewUpdates++; Capacity = cap; Focus = focus; return ZeroUpdate ? 0 : 1; }
        public void FreeUpdate(nint p) { Assert.Equal((nint)1, p); UpdateFrees++; foreach (var n in Owned) Assert.True(LiveNodes.Remove(n)); Owned.Clear(); }
        public void SetTree(nint p, ulong root) { Check("tree"); Root = root; }
        public nint NewNode(byte role) { Check("new-node"); if (FailSecondNode && next == 21) throw new InvalidOperationException(); Role = role; if (ZeroNode) return 0; var p = next++; LiveNodes.Add(p); return p; }
        public void FreeNode(nint p) { Assert.DoesNotContain(p, Owned); Assert.True(LiveNodes.Remove(p)); NodeFrees++; }
        public void Label(nint p, string v) { Check("label"); LabelText = v; }
        public void Value(nint p, string v) { Check("value"); ValueText = v; }
        public void Description(nint p, string v) { Check("description"); DescriptionText = v; }
        public void AccessKey(nint p, string value) { Check("access-key"); AccessKeyText = value; }
        public void Bounds(nint p, AccessKitNativeRect b) { Check("bounds"); Rect = b; }
        public void Children(nint p, IReadOnlyList<ulong> c) { Check("children"); ChildIds.AddRange(c); }
        public void Disabled(nint p) { Check("disabled"); IsDisabled = true; }
        public void Selected(nint p, bool v) { Check("selected"); SelectionCalls++; IsSelected = v; }
        public void Expanded(nint p, bool v) { Check("expanded"); IsExpanded = v; }
        public void Toggled(nint p, byte v) { Check("toggled"); Checked = v; }
        public List<byte> Actions = [];
        public void AddAction(nint p, byte action) { Check("action"); Actions.Add(action); }
        public void PushNode(nint p, ulong id, nint n) { Check("push"); Assert.Contains(n, LiveNodes); Assert.True(Owned.Add(n)); Ids.Add(id); }
    }
}
