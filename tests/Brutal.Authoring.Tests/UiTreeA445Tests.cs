using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiTreeA445Tests
{
    private readonly ITestOutputHelper output;
    public UiTreeA445Tests(ITestOutputHelper output) => this.output = output;
    private static UiNode Node(ulong id, string label, int parent = -1) =>
        new(id, UiRole.Button, label, new(1, 2, 30, 20), true, null, false, false, false, null, null, null, parent, UiActions.Invoke);

    [Fact]
    public void Incomplete_frames_never_replace_the_completed_snapshot()
    {
        var tree = new UiTree(); tree.Begin(); tree.Record(Node(1, "Saved")); tree.Complete();
        var old = tree.Snapshot(); tree.Begin(); tree.Record(Node(2, "Unfinished"));
        Assert.Equal("Saved", Assert.Single(tree.Snapshot().Nodes).Label);
        tree.Begin(); tree.Record(Node(3, "Next")); tree.Complete();
        Assert.Equal("Saved", Assert.Single(old.Nodes).Label);
        Assert.Equal("Next", Assert.Single(tree.Snapshot().Nodes).Label);
        Assert.Equal(2, tree.Snapshot().Frame);
    }

    [Fact]
    public void Snapshot_preserves_all_states_parent_and_escaped_text()
    {
        var tree = new UiTree(); tree.Begin();
        tree.Record(Node(1, "Parent") with { Role = UiRole.Window });
        tree.Record(Node(2, "Save\"\n", 0) with { Enabled = false, DisabledReason = "no unsaved changes", Focused = true,
            Selected = true, Expanded = false, Checked = true, Value = "value\n", Actions = UiActions.None });
        tree.Complete(); var result = tree.Snapshot();
        Assert.Contains("  button ", result.Text); Assert.Contains("disabled: no unsaved changes", result.Text);
        Assert.Contains("[focused] [selected] [collapsed] [checked]", result.Text);
        using var json = JsonDocument.Parse(result.Json);
        var n = json.RootElement.GetProperty("nodes")[1];
        Assert.Equal("Save\"\n", n.GetProperty("label").GetString());
        Assert.Equal(0, n.GetProperty("parent").GetInt32());
        Assert.Equal("value\n", n.GetProperty("value").GetString());
        Assert.False(n.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Reused_buffers_keep_more_than_initial_capacity_and_do_not_leak_old_nodes()
    {
        var tree = new UiTree(); tree.Begin();
        for (var i = 0; i < 4097; i++) tree.Record(Node((ulong)i, "node"));
        tree.Complete(); Assert.Equal(4097, tree.Snapshot().Nodes.Count);
        tree.Begin(); tree.Record(Node(9, "one")); tree.Complete();
        Assert.Single(tree.Snapshot().Nodes);
        tree.Begin(); tree.Complete(); Assert.Empty(tree.Snapshot().Nodes);
    }

    [Fact]
    public void Core_recording_budget_has_a_generous_owner_run_guard()
    {
        var tree = new UiTree(); var n = Node(1, "item");
        void Frame() { tree.Begin(); for (var i = 0; i < 2000; i++) tree.Record(n with { Id = (ulong)i }); tree.Complete(); }
        Frame(); Frame(); Frame();
        var allocation = GC.GetAllocatedBytesForCurrentThread(); var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) Frame();
        sw.Stop(); var bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        output.WriteLine($"Core recorder: {sw.Elapsed.TotalMilliseconds / 100:F3} ms/frame, {bytes} warm bytes over 100 frames.");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"2000-node core frames: {sw.Elapsed.TotalMilliseconds / 100:F3} ms/frame");
        Assert.True(bytes < 65536, $"Warm core recorder allocated {bytes} bytes.");
        // This generous smoke guard does NOT prove the target (<1 ms including wrappers). Owner must measure that separately.
    }

    [Fact]
    public void Ordinary_wrappers_return_native_results_and_record_actual_item_bounds()
    {
        using var host = new HeadlessHost { Drawing = false };
        bool check = false; var input = new ImInputString(128, "draft");
        UiBounds button = default; uint id = 0; bool pressed = true;
        void Draw()
        {
            Im.SetNextWindowPos(new float2(20, 20)); Im.SetNextWindowSize(new float2(800, 600));
            Im.Begin("Wrapper harness");
            pressed = Ui.Button("Save"); id = Im.GetItemID().Value;
            var a = Im.GetItemRectMin(); var b = Im.GetItemRectMax(); button = new(a.X, a.Y, b.X-a.X, b.Y-a.Y);
            Assert.False(Ui.SmallButton("Small")); Assert.False(Ui.Selectable("Row", true));
            Assert.False(Ui.InputText("Name", input)); Assert.False(Ui.Checkbox("Check", ref check));
            Ui.BeginDisabled(true, "no unsaved changes"); Assert.False(Ui.Button("Disabled")); Ui.EndDisabled();
            Im.End();
        }
        host.Frames(Draw);
        Assert.False(pressed);
        var nodes = Ui.Snapshot().Nodes;
        var save = Assert.Single(nodes, n => n.Label == "Save");
        Assert.Equal(button, save.Bounds); Assert.Equal(id, (uint)save.Id); Assert.True(save.Enabled);
        var disabled = Assert.Single(nodes, n => n.Label == "Disabled");
        Assert.False(disabled.Enabled); Assert.Equal("no unsaved changes", disabled.DisabledReason);
        Assert.Equal("draft", Assert.Single(nodes, n => n.Role == UiRole.TextField).Value);
        Assert.False(Assert.Single(nodes, n => n.Role == UiRole.Checkbox).Checked!.Value);
        // Event-frame activation and stack identity are proved separately below.
    }

    [Fact]
    public void Button_return_and_id_stack_follow_the_same_native_input_contract()
    {
        using var host = new HeadlessHost { Drawing = false };
        var hits = 0; UiBounds target = default;
        void Draw()
        {
            Im.Begin("Input harness");
            Im.PushID(1); if (Ui.Button("Same")) hits++;
            var a = Im.GetItemRectMin(); var b = Im.GetItemRectMax(); target = new(a.X,a.Y,b.X-a.X,b.Y-a.Y); Im.PopID();
            Im.PushID(2); Ui.Button("Same"); Im.PopID(); Im.End();
        }
        host.Frames(Draw); var first = Ui.Snapshot().Nodes.Select(n => n.Id).ToArray();
        Assert.Equal(2, first.Distinct().Count());
        host.Click(Draw, target.X + 5, target.Y + 5); Assert.Equal(1, hits);
        Assert.Equal(first, Ui.Snapshot().Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Nested_windows_tables_rows_cells_and_tree_nodes_keep_real_parents()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Im.SetNextWindowSize(new float2(800, 650));
            if (Ui.Begin("Groups"))
            {
                Ui.Text("literal ## text", "description");
                Ui.TextWrapped("Wrapped", "wrapped");
                Ui.Status("Ready", "status");
                if (Ui.BeginTable("Grid", 2))
                {
                    Ui.TableNextRow(semanticId: "row-a"); Ui.TableSetColumnIndex(0, "name"); Ui.Text("A", "cell-a");
                    Ui.TableNextColumn("value"); Ui.Button("Cell button");
                    Ui.EndTable();
                }
                if (Ui.TreeNodeEx("Branch", ImGuiTreeNodeFlags.DefaultOpen))
                { Ui.SmallButton("Child"); Ui.TreePop(); }
                if (Ui.BeginTabBar("Tabs"))
                { if (Ui.BeginTabItem("First")) { Ui.Text("Tab text", "tab-text"); Ui.EndTabItem(); } Ui.EndTabBar(); }
            }
            Ui.End();
        }
        host.Frames(Draw);
        var nodes = Ui.Snapshot().Nodes;
        UiNode Find(string label) => Assert.Single(nodes, n => n.Label == label);
        Assert.Equal("literal ## text", Find("literal ## text").Label);
        Assert.Equal(UiIdKind.Semantic, Find("literal ## text").IdKind);
        var table = Find("Grid"); var row = Assert.Single(nodes, n => n.Role == UiRole.Row);
        Assert.Equal(UiRole.Table, nodes[row.Parent].Role);
        Assert.Equal(2, nodes.Count(n => n.Role == UiRole.Cell));
        Assert.Equal(UiRole.Cell, nodes[Find("Cell button").Parent].Role);
        Assert.False(table.Bounds.IsEmpty);
        Assert.Equal(UiRole.TreeItem, nodes[Find("Child").Parent].Role);
        Assert.Equal(UiRole.Tab, nodes[Find("Tab text").Parent].Role);
        Assert.True(Find("First").Selected);
        Assert.True(Find("Branch").Expanded!.Value);
        Assert.Equal(UiRole.Status, Find("Ready").Role);
    }

    [Fact]
    public void Empty_containers_have_no_invented_bounds_and_positional_ids_are_explicit()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Ui.Begin("Empty"); Ui.Text("positional");
            if (Ui.BeginTable("Empty grid", 1))
            { Ui.TableNextRow(); Ui.TableNextColumn(); Ui.EndTable(); }
            Ui.End();
        }
        host.Frames(Draw); var snap = Ui.Snapshot();
        foreach (var node in snap.Nodes.Where(n => n.Role is UiRole.Table or UiRole.Row or UiRole.Cell)) Assert.True(node.Bounds.IsEmpty);
        Assert.Equal(UiIdKind.Positional, Assert.Single(snap.Nodes, n => n.Label == "positional").IdKind);
        using var json = JsonDocument.Parse(snap.Json);
        Assert.Contains(json.RootElement.GetProperty("nodes").EnumerateArray(), n => n.GetProperty("idKind").GetString() == "positional");
        Assert.Contains("[idKind: positional]", snap.Text);
    }

    [Fact]
    public void Menu_combo_modal_and_link_roles_preserve_begin_end_contracts()
    {
        using var host = new HeadlessHost { Drawing = false };
        bool open = true;
        void Draw()
        {
            if (Ui.BeginMainMenuBar())
            { Assert.False(Ui.BeginMenu("Unavailable menu", false)); Ui.MenuItem("Menu command"); Ui.EndMainMenuBar(); }
            Ui.Begin("Popups", ImGuiWindowFlags.MenuBar);
            if (Ui.BeginMenuBar()) { Ui.MenuItem("Local command"); Ui.EndMenuBar(); }
            if (Ui.BeginCombo("Choice", "Current")) Ui.EndCombo();
            Ui.TextLink("Link");
            Im.OpenPopup("Dialog");
            if (Ui.BeginPopupModal("Dialog", ref open)) { Ui.Text("Dialog body", "body"); Ui.EndPopup(); }
            Ui.End();
        }
        host.Frames(Draw); var nodes = Ui.Snapshot().Nodes;
        Assert.Contains(nodes, n => n.Role == UiRole.MenuBar);
        Assert.Contains(nodes, n => n.Role == UiRole.MenuItem && n.Label == "Menu command");
        Assert.Contains(nodes, n => n.Role == UiRole.Menu && !n.Enabled && n.Label == "Unavailable menu");
        Assert.Contains(nodes, n => n.Role == UiRole.Combo && n.Value == "Current" && n.Expanded == false);
        Assert.Contains(nodes, n => n.Role == UiRole.Link && n.Label == "Link");
        Assert.Contains(nodes, n => n.Role == UiRole.Dialog && !n.Bounds.IsEmpty);
        var body = Assert.Single(nodes, n => n.Label == "Dialog body");
        Assert.Equal(UiRole.Dialog, nodes[body.Parent].Role);
    }

    [Fact]
    public void External_disabled_scopes_are_observed_and_password_values_are_not_exposed()
    {
        using var host = new HeadlessHost { Drawing = false };
        var value = new ImInputString(64, "private-value");
        void Draw()
        {
            Ui.Begin("State"); Im.BeginDisabled(); Ui.Button("Raw disabled"); Im.EndDisabled();
            Ui.InputText("Secret", value, ImGuiInputTextFlags.Password); Ui.End();
        }
        host.Frames(Draw); var snap = Ui.Snapshot();
        var raw = Assert.Single(snap.Nodes, n => n.Label == "Raw disabled");
        Assert.False(raw.Enabled); Assert.Equal("disabled", raw.DisabledReason);
        Assert.DoesNotContain("private-value", snap.Json); Assert.DoesNotContain("private-value", snap.Text);
        Assert.Equal("[password]", Assert.Single(snap.Nodes, n => n.Role == UiRole.TextField).Value);
    }

    [Fact]
    public void Wrapper_frame_cost_is_reportable_without_a_claim_of_meeting_one_millisecond()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Ui.Begin("Budget");
            for (var i = 0; i < 2000; i++) { Im.PushID(i); Ui.SmallButton("Item"); Im.PopID(); }
            Ui.End();
        }
        void Native()
        {
            Im.Begin("Budget");
            for (var i = 0; i < 2000; i++) { Im.PushID(i); Im.SmallButton("Item"); Im.PopID(); }
            Im.End();
        }
        host.Frames(Native); host.Frames(Draw);
        var sw = Stopwatch.StartNew(); host.Frames(Native, 5); sw.Stop(); var native = sw.Elapsed.TotalMilliseconds / 5;
        host.Frames(Draw, 2); // Rewarm recorder strings after the empty native-only snapshots.
        sw.Restart(); host.Frames(Draw, 5); sw.Stop(); var wrapped = sw.Elapsed.TotalMilliseconds / 5;
        Assert.Equal(2000, Ui.Snapshot().Nodes.Count(n => n.Role == UiRole.Button));
        output.WriteLine($"2000 controls: native {native:F3} ms/frame; wrapped {wrapped:F3}; observed delta {wrapped-native:F3}. Headless comparison includes scheduling noise, not an isolated recorder guarantee.");
        Assert.True(wrapped < 2000, $"2000-wrapper headless frame: {wrapped:F3} ms; owner evaluates the separate <1 ms recorder target.");
    }

    [Fact]
    public void Edited_field_focus_checkbox_and_ref_selection_are_recorded_after_native_updates()
    {
        using var host = new HeadlessHost { Drawing = false };
        var input = new ImInputString(128, "start"); bool check = false, selected = false;
        void Draw()
        {
            Im.SetNextWindowSize(new float2(600, 500)); Ui.Begin("Editing");
            Ui.InputText("Field", input); Ui.Checkbox("Flag", ref check); Ui.Selectable("Selected row", ref selected); Ui.End();
        }
        UiNode Find(string label) => Assert.Single(Ui.Snapshot().Nodes, n => n.Label == label);
        host.Frames(Draw); var field = Find("Field");
        host.Click(Draw, field.Bounds.X + 10, field.Bounds.Y + 5); host.Type(Draw, "x");
        input.EvaluateLength(); Assert.Equal(input.ToString(), Find("Field").Value); Assert.True(Find("Field").Focused);
        var flag = Find("Flag"); host.Click(Draw, flag.Bounds.X + 5, flag.Bounds.Y + 5);
        Assert.True(check); Assert.True(Find("Flag").Checked!.Value);
        var row = Find("Selected row"); host.Click(Draw, row.Bounds.X + 5, row.Bounds.Y + 5);
        Assert.True(selected); Assert.True(Find("Selected row").Selected);
    }

    [Fact]
    public void Open_combo_captures_header_bounds_after_popup_end_and_keeps_children()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Im.SetNextWindowSize(new float2(600, 500)); Ui.Begin("Combo");
            if (Ui.BeginCombo("Pick", "Current")) { Ui.Selectable("Option"); Ui.EndCombo(); }
            Ui.End();
        }
        host.Frames(Draw); var header = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo);
        Assert.False(header.Bounds.IsEmpty);
        host.Click(Draw, header.Bounds.X + 10, header.Bounds.Y + 5);
        var nodes = Ui.Snapshot().Nodes; var open = Assert.Single(nodes, n => n.Role == UiRole.Combo);
        Assert.True(open.Expanded!.Value); Assert.Equal(header.Bounds, open.Bounds);
        Assert.Equal(UiRole.Combo, nodes[Assert.Single(nodes, n => n.Label == "Option").Parent].Role);
    }

    [Fact]
    public void Semantic_text_identity_survives_insertion_while_window_collapse_still_balances()
    {
        using var host = new HeadlessHost { Drawing = false }; bool prepend = false, collapse = false;
        void Draw()
        {
            Im.SetNextWindowCollapsed(collapse);
            if (Ui.Begin("Identity"))
            { if (prepend) Ui.Text("Added"); Ui.Text("Stable text", "stable-key"); }
            Ui.End();
        }
        host.Frames(Draw); var id = Assert.Single(Ui.Snapshot().Nodes, n => n.Label == "Stable text").Id;
        prepend = true; host.Frame(Draw);
        Assert.Equal(id, Assert.Single(Ui.Snapshot().Nodes, n => n.Label == "Stable text").Id);
        collapse = true; host.Frame(Draw);
        Assert.Equal(UiRole.Window, Assert.Single(Ui.Snapshot().Nodes).Role);
    }
}
