using System.Text.Json;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Numerics;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiViewportTests
{
    private static UiNode Node(ulong id, uint viewport = 0, int parent = -1) =>
        new(id, UiRole.Button, $"Item {id}", new(1, 2, 30, 20), true, null,
            false, false, false, null, null, null, parent, UiActions.Invoke, ViewportId: viewport);

    [Fact]
    public void Existing_nodes_default_to_the_main_viewport_without_changing_ids_or_text()
    {
        var tree = new UiTree(); tree.Begin();
        tree.Record(new(9, UiRole.Button, "Save", new(1, 2, 30, 20), true, null,
            false, false, false, null, null, null, -1, UiActions.Invoke));
        tree.Complete(); var snapshot = tree.Snapshot();
        Assert.Equal(0u, Assert.Single(snapshot.Nodes).ViewportId);
        Assert.Equal(snapshot.Nodes, snapshot.ForViewport(0).Nodes);
        Assert.Equal(snapshot.Text, snapshot.ForViewport(0).Text);
        Assert.Equal(new uint[] { 0 }, snapshot.PartitionByViewport().Keys);
        Assert.Equal(9ul, snapshot.ForViewport(0).Nodes[0].Id);
    }

    [Fact]
    public void Interleaved_viewports_keep_full_state_and_order_with_local_parent_indexes()
    {
        var tree = new UiTree(); tree.Begin();
        tree.Record(Node(11) with { Role = UiRole.Window });
        tree.Record(Node(12, 71) with { Role = UiRole.Window });
        tree.Record(Node(13, parent: 0));
        tree.Record(Node(14, 71, 1) with { Enabled = false, DisabledReason = "not available", Focused = true,
            Hovered = true, Selected = true, Expanded = false, Checked = true, Value = "draft",
            Actions = UiActions.None, IdKind = UiIdKind.Semantic, AccessKey = "D",
            TextEvidence = new(new(0, 0, 10, 10), new(0, 0, 11, 10), -1, true) });
        tree.Record(Node(15, 71, 3)); tree.Complete();
        var snapshot = tree.Snapshot(); var parts = snapshot.PartitionByViewport();
        Assert.Equal(new uint[] { 0, 71 }, parts.Keys);
        Assert.Equal(new ulong[] { 11, 13 }, parts[0].Nodes.Select(n => n.Id));
        Assert.Equal(new ulong[] { 12, 14, 15 }, parts[71].Nodes.Select(n => n.Id));
        Assert.Equal(new[] { -1, 0 }, parts[0].Nodes.Select(n => n.Parent));
        Assert.Equal(new[] { -1, 0, 1 }, parts[71].Nodes.Select(n => n.Parent));
        Assert.Equal(snapshot.Nodes[3] with { Parent = 0 }, parts[71].Nodes[1]);
        foreach (var part in parts)
        {
            Assert.Equal(snapshot.Frame, part.Value.Frame);
            Assert.All(part.Value.Nodes, n => Assert.Equal(part.Key, n.ViewportId));
            Assert.Equal(part.Value.Nodes, snapshot.ForViewport(part.Key).Nodes);
        }
        Assert.Equal(new[] { -1, -1, 0, 1, 3 }, snapshot.Nodes.Select(n => n.Parent));
    }

    [Fact]
    public void Cross_viewport_parents_are_detached_and_never_enlarge_another_window()
    {
        var tree = new UiTree(); tree.Begin();
        tree.Record(Node(1) with { Role = UiRole.Window, Bounds = default }, true);
        tree.Record(Node(2, 19, 0) with { Role = UiRole.Dialog, Bounds = default }, true);
        tree.Record(Node(3, 19, 1) with { Bounds = new(900, 700, 70, 40) });
        tree.Record(Node(4, parent: 0) with { Bounds = new(10, 20, 30, 40) });
        tree.Complete(); var snapshot = tree.Snapshot(); var parts = snapshot.PartitionByViewport();
        Assert.Equal(new UiBounds(10, 20, 30, 40), snapshot.Nodes[0].Bounds);
        Assert.Equal(new UiBounds(900, 700, 70, 40), snapshot.Nodes[1].Bounds);
        Assert.Equal(new[] { -1, 0 }, parts[19].Nodes.Select(n => n.Parent));
        Assert.Equal(snapshot.Nodes[1] with { Parent = -1 }, parts[19].Nodes[0]);
        Assert.Equal(0, snapshot.Nodes[1].Parent);
    }

    [Fact]
    public void Snapshots_stay_owned_across_incomplete_frames_and_viewport_moves()
    {
        var tree = new UiTree(); tree.Begin(); tree.Record(Node(123, 5)); tree.Complete();
        var old = tree.Snapshot(); var parts = old.PartitionByViewport();
        tree.Begin(); tree.Record(Node(123));
        Assert.Equal(5u, Assert.Single(tree.Snapshot().Nodes).ViewportId);
        tree.Complete(); var current = tree.Snapshot();
        Assert.Equal(123ul, Assert.Single(current.ForViewport(0).Nodes).Id);
        Assert.Empty(current.ForViewport(5).Nodes);
        Assert.Equal(5u, Assert.Single(parts[5].Nodes).ViewportId);
        Assert.Equal(1, old.Frame); Assert.Equal(2, current.Frame);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<uint, UiSnapshot>)parts).Clear());
    }

    [Fact]
    public void Empty_and_absent_viewports_have_no_invented_nodes()
    {
        var tree = new UiTree(); tree.Begin(); tree.Complete(); var empty = tree.Snapshot();
        Assert.Empty(empty.PartitionByViewport());
        Assert.Empty(empty.ForViewport(18).Nodes);
        Assert.Equal(empty.Frame, empty.ForViewport(18).Frame);
        tree.Begin(); tree.Record(Node(1, uint.MaxValue)); tree.Complete();
        var snapshot = tree.Snapshot();
        Assert.Empty(snapshot.ForViewport(0).Nodes);
        using var json = JsonDocument.Parse(snapshot.Json);
        Assert.Equal(uint.MaxValue, json.RootElement.GetProperty("nodes")[0].GetProperty("viewportId").GetUInt32());
        Assert.Equal(uint.MaxValue, Assert.Single(snapshot.PartitionByViewport()).Key);
    }

    [Fact]
    public void Ordinary_headless_wrappers_record_main_zero_without_changing_identity()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Im.SetNextWindowSize(new float2(400, 300));
            Ui.Begin("Viewport recording"); Ui.Button("Save"); Ui.Text("Description", "description");
            Ui.BeginChild("Child", new float2(200, 120)); Ui.Button("Child action"); Ui.EndChild();
            Ui.End();
        }
        host.Frames(Draw); var first = Ui.Snapshot();
        Assert.NotEmpty(first.Nodes);
        Assert.All(first.Nodes, n => Assert.Equal(0u, n.ViewportId));
        Assert.Equal(first.Nodes, Ui.Snapshot(0).Nodes);
        Assert.Empty(Ui.Snapshot(900).Nodes);
        host.Frames(Draw); var next = Ui.Snapshot();
        Assert.Equal(first.Nodes.Select(n => n.Id), next.Nodes.Select(n => n.Id));
        Assert.Equal(first.Nodes.Select(n => n.Parent), next.Nodes.Select(n => n.Parent));
        Assert.Equal(first.Text, next.Text);
    }
}
