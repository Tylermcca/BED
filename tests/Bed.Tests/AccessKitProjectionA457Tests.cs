using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

public sealed class AccessKitProjectionA457Tests
{
    private sealed class FakeAdapter : IAccessKitUpdateSink
    {
        public List<AccessKitProjectedUpdate> Updates { get; } = [];
        public bool Reject { get; set; }
        public bool Active { get; set; } = true;
        public bool TryApply(AccessKitProjectedUpdate update)
        {
            if (Reject) throw new InvalidOperationException("Synthetic adapter refusal.");
            if (!Active) return false;
            Updates.Add(update);
            return true;
        }
    }

    private static UiNode Node(ulong id, UiRole role = UiRole.Button, int parent = -1) =>
        new(id, role, "Visible label", new UiBounds(110, 220, 40, 10), true, null, false, false,
            false, null, null, null, parent, UiActions.Focus | UiActions.Invoke | UiActions.SetValue);

    private static UiSnapshot Frame(UiTree tree, params UiNode[] nodes)
    {
        tree.Begin();
        foreach (var node in nodes) tree.Record(node);
        tree.Complete();
        return tree.Snapshot();
    }

    private static bool Submit(AccessKitProjection projection, UiSnapshot snapshot, FakeAdapter adapter) =>
        projection.Submit(snapshot, "Synthetic editor", new UiBounds(100, 200, 800, 600), 1.5f, 2, adapter);

    [Fact]
    public void Access_key_metadata_is_unconditional_and_key_only_changes_replace_the_node()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        var node = Node(1) with { AccessKey = "F", Focused = false, Hovered = false };
        Assert.True(Submit(projection, Frame(tree, node), adapter));
        var first = adapter.Updates.Single().ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Button);
        Assert.Equal("F", first.AccessKey);
        Assert.Equal(UiActions.Focus | UiActions.Invoke, first.Actions); // only this Button wrapper's implemented actions, not the supplied SetValue flag.
        Assert.True(Submit(projection, Frame(tree, node with { AccessKey = "G" }), adapter));
        var changed = Assert.Single(adapter.Updates[1].ChangedNodes);
        Assert.Equal(first.Id, changed.Id);
        Assert.Equal("G", changed.AccessKey);
        Assert.True(Submit(projection, Frame(tree, node with { AccessKey = null }), adapter));
        Assert.Null(Assert.Single(adapter.Updates[2].ChangedNodes).AccessKey);
    }

    [Fact]
    public void Full_tooltip_disabled_reason_and_proven_truncation_are_described_without_duplicate_lines()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        var node = Node(1) with { Enabled = false, DisabledReason = "No selection", Tooltip = "Hint\nNo selection",
            TextEvidence = new(new(0, 0, 1, 1), new(0, 0, 100, 20), -1, true) };
        Assert.True(Submit(projection, Frame(tree, node), adapter));
        var target = adapter.Updates.Single().ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Button);
        Assert.Equal("Hint\nNo selection\nVisible label", target.Description);
        Assert.Equal(UiActions.None, target.Actions);
    }

    [Theory]
    [InlineData(UiRole.Window, AccessKitProjectedRole.Window)]
    [InlineData(UiRole.MenuBar, AccessKitProjectedRole.MenuBar)]
    [InlineData(UiRole.Menu, AccessKitProjectedRole.Menu)]
    [InlineData(UiRole.MenuItem, AccessKitProjectedRole.MenuItem)]
    [InlineData(UiRole.Button, AccessKitProjectedRole.Button)]
    [InlineData(UiRole.Link, AccessKitProjectedRole.Link)]
    [InlineData(UiRole.Text, AccessKitProjectedRole.Label)]
    [InlineData(UiRole.TextField, AccessKitProjectedRole.TextInput)]
    [InlineData(UiRole.Checkbox, AccessKitProjectedRole.CheckBox)]
    [InlineData(UiRole.Combo, AccessKitProjectedRole.ComboBox)]
    [InlineData(UiRole.ListItem, AccessKitProjectedRole.ListItem)]
    [InlineData(UiRole.Table, AccessKitProjectedRole.Table)]
    [InlineData(UiRole.Row, AccessKitProjectedRole.Row)]
    [InlineData(UiRole.Cell, AccessKitProjectedRole.Cell)]
    [InlineData(UiRole.TreeItem, AccessKitProjectedRole.TreeItem)]
    [InlineData(UiRole.Tab, AccessKitProjectedRole.Tab)]
    [InlineData(UiRole.Dialog, AccessKitProjectedRole.Dialog)]
    [InlineData(UiRole.Status, AccessKitProjectedRole.Status)]
    [InlineData(UiRole.Canvas, AccessKitProjectedRole.Canvas)]
    public void Every_existing_role_has_its_AccessKit_name(UiRole source, AccessKitProjectedRole expected)
        => Assert.Equal(expected, AccessKitProjection.RoleOf(source));

    [Fact]
    public void Initial_update_preserves_hierarchy_visible_values_and_physical_geometry()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        var snapshot = Frame(tree, Node(ulong.MaxValue, UiRole.Dialog),
            Node(0, UiRole.Text, 0) with { Label = "Ordinary visible text" },
            Node(12, UiRole.TextField, 0) with { Value = "***", Focused = true });
        Assert.True(Submit(projection, snapshot, adapter));
        var update = Assert.Single(adapter.Updates);
        Assert.True(update.Initial);
        Assert.Equal(0UL, update.Root);
        Assert.Empty(update.RemovedIds);
        var root = update.ChangedNodes.Single(n => n.Id == update.Root);
        Assert.Equal(new UiBounds(0, 0, 1200, 1200), root.Bounds);
        var dialog = update.ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Dialog);
        Assert.Equal(new[] { dialog.Id }, root.Children);
        Assert.Equal(new UiBounds(15, 40, 60, 20), dialog.Bounds);
        var label = update.ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Label);
        Assert.Null(label.Label);
        Assert.Equal("Ordinary visible text", label.Value);
        var field = update.ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.TextInput);
        Assert.Equal("Visible label", field.Label);
        Assert.Equal("***", field.Value);
        Assert.Equal(field.Id, update.Focus);
        Assert.Equal(new[] { label.Id, field.Id }, dialog.Children);
        Assert.NotEqual(label.Id, root.Id); // source zero is not the synthetic root.
    }

    [Fact]
    public void Disabled_selected_checked_expanded_and_identity_states_are_preserved_without_advertising_actions()
    {
        var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        var snapshot = Frame(new UiTree(), Node(1, UiRole.Checkbox) with
        { Enabled = false, DisabledReason = "No selection", Selected = true, Expanded = false, Checked = true, IdKind = UiIdKind.Positional });
        Submit(projection, snapshot, adapter);
        var node = Assert.Single(Assert.Single(adapter.Updates).ChangedNodes, n => n.Role == AccessKitProjectedRole.CheckBox);
        Assert.True(node.Disabled); Assert.Equal("No selection", node.Description);
        Assert.True(node.Selected); Assert.False(node.Expanded); Assert.True(node.Checked);
        Assert.False(node.StableIdentity); Assert.Equal(UiActions.None, node.Actions);
    }

    [Fact]
    public void Equal_frames_and_hover_only_changes_do_not_submit_native_work()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Submit(projection, Frame(tree, Node(1)), adapter);
        Assert.False(Submit(projection, Frame(tree, Node(1)), adapter));
        Assert.False(Submit(projection, Frame(tree, Node(1) with { Hovered = true }), adapter));
        Assert.Single(adapter.Updates);
    }

    [Fact]
    public void One_changed_value_replaces_only_that_complete_node()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Submit(projection, Frame(tree, Node(1, UiRole.TextField) with { Value = "before" }), adapter);
        var original = adapter.Updates[0].ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.TextInput);
        Submit(projection, Frame(tree, Node(1, UiRole.TextField) with { Value = "after" }), adapter);
        var update = adapter.Updates[1];
        Assert.False(update.Initial); Assert.Empty(update.RemovedIds);
        var changed = Assert.Single(update.ChangedNodes);
        Assert.Equal(original.Id, changed.Id); Assert.Equal(original.Label, changed.Label);
        Assert.Equal(original.Bounds, changed.Bounds); Assert.Equal("after", changed.Value);
        Assert.Empty(changed.Children);
    }

    [Fact]
    public void Focus_only_change_has_no_unrelated_node_replacements()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Submit(projection, Frame(tree, Node(1)), adapter);
        Assert.True(Submit(projection, Frame(tree, Node(1) with { Focused = true }), adapter));
        var update = adapter.Updates[1];
        Assert.Empty(update.ChangedNodes);
        Assert.NotEqual(update.Root, update.Focus);
        Assert.False(Submit(projection, Frame(tree, Node(1) with { Focused = true }), adapter));
    }

    [Fact]
    public void Focused_descendant_wins_over_its_focused_containing_scope()
    {
        var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Submit(projection, Frame(new UiTree(), Node(1, UiRole.Dialog) with { Focused = true },
            Node(2, UiRole.Button, 0) with { Focused = true }), adapter);
        var update = Assert.Single(adapter.Updates);
        Assert.Equal(update.ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Button).Id, update.Focus);
    }

    [Fact]
    public void Removing_a_subtree_updates_its_parent_and_never_reemits_removed_nodes()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Submit(projection, Frame(tree, Node(1, UiRole.Dialog), Node(2, UiRole.Button, 0), Node(3, UiRole.Text, 1)), adapter);
        var oldButton = adapter.Updates[0].ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Button).Id;
        Submit(projection, Frame(tree, Node(1, UiRole.Dialog)), adapter);
        var update = adapter.Updates[1];
        Assert.Equal(2, update.RemovedIds.Count);
        var parent = Assert.Single(update.ChangedNodes);
        Assert.Equal(AccessKitProjectedRole.Dialog, parent.Role); Assert.Empty(parent.Children);
        Assert.DoesNotContain(update.ChangedNodes, n => update.RemovedIds.Contains(n.Id));
        Submit(projection, Frame(tree, Node(1, UiRole.Dialog), Node(2, UiRole.Button, 0)), adapter);
        Assert.NotEqual(oldButton, adapter.Updates[2].ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Button).Id);
    }

    [Fact]
    public void Adapter_refusal_keeps_the_last_submitted_baseline_for_retry()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Submit(projection, Frame(tree, Node(1) with { Label = "before" }), adapter);
        var next = Frame(tree, Node(1) with { Label = "after" });
        adapter.Reject = true;
        Assert.Throws<InvalidOperationException>(() => Submit(projection, next, adapter));
        Assert.Single(adapter.Updates);
        adapter.Reject = false;
        Assert.True(Submit(projection, next, adapter));
        Assert.Equal("after", Assert.Single(adapter.Updates[1].ChangedNodes).Label);
        Assert.False(Submit(projection, next, adapter));
    }

    [Fact]
    public void Inactive_adapter_does_not_consume_the_initial_tree()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter { Active = false }; var projection = new AccessKitProjection();
        Assert.False(Submit(projection, Frame(tree, Node(1)), adapter));
        Assert.Empty(adapter.Updates);
        adapter.Active = true;
        Assert.True(Submit(projection, Frame(tree, Node(1)), adapter));
        Assert.True(Assert.Single(adapter.Updates).Initial);
        Assert.Equal(2, adapter.Updates[0].ChangedNodes.Count);
    }
    [Fact]
    public void Empty_group_bounds_do_not_invent_a_hit_rectangle()
    {
        var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Submit(projection, Frame(new UiTree(), Node(1, UiRole.Table) with { Bounds = new UiBounds(10, 10, 0, 0) }), adapter);
        Assert.True(adapter.Updates[0].ChangedNodes.Single(n => n.Role == AccessKitProjectedRole.Table).Bounds.IsEmpty);
    }

    [Fact]
    public void Duplicate_recorded_IDs_are_refused_before_the_adapter_is_called()
    {
        var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        Assert.Throws<ArgumentException>(() => Submit(projection, Frame(new UiTree(), Node(1), Node(1)), adapter));
        Assert.Empty(adapter.Updates);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Invalid_physical_scale_is_refused_before_submission(float scale)
    {
        var adapter = new FakeAdapter();
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccessKitProjection().Submit(Frame(new UiTree(), Node(1)),
            "Synthetic editor", new UiBounds(100, 200, 800, 600), scale, 1, adapter));
        Assert.Empty(adapter.Updates);
    }

    [Fact]
    public void An_older_completed_frame_cannot_replace_a_newer_frame()
    {
        var tree = new UiTree(); var adapter = new FakeAdapter(); var projection = new AccessKitProjection();
        var old = Frame(tree, Node(1));
        Submit(projection, Frame(tree, Node(2)), adapter);
        Assert.Throws<InvalidOperationException>(() => Submit(projection, old, adapter));
        Assert.Single(adapter.Updates);
    }

    [Fact]
    public void Submission_on_a_different_thread_is_refused_without_adapter_work()
    {
        var adapter = new FakeAdapter(); var projection = new AccessKitProjection(); var snapshot = Frame(new UiTree(), Node(1));
        Exception? refusal = null;
        var worker = new Thread(() => { try { Submit(projection, snapshot, adapter); } catch (Exception e) { refusal = e; } });
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "Owned test thread did not return.");
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.Empty(adapter.Updates);
    }
}
