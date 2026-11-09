using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Editor.Text;
using Brutal.Authoring.Launcher;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiTreeConversionA446Tests
{
    private const string Schema = """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"price":{"type":"number"},"enabled":{"type":"boolean"}}}}}}""";
    private const string Data = """{"items":[{"name":"Rope","price":2,"enabled":true}]}""";
    private static EditorModel<DataDocument> Editor() => new(DataKit.Session(new DataFormat().Read(Data),
        new JsonSchemas().Add("items.schema.json", Schema), "items.schema.json"), DataEditing.Outline);
    private static void Draw(Action body)
    {
        Im.SetNextWindowPos(new float2(0, 0)); Im.SetNextWindowSize(new float2(1200, 850));
        Ui.Begin("Converted panels"); body(); Ui.End();
    }
    private static void LabelledButtons(IReadOnlyList<UiNode> nodes)
    {
        var buttons = nodes.Where(n => n.Role == UiRole.Button).ToArray();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, n => Assert.False(string.IsNullOrWhiteSpace(n.Label), $"Unlabelled button {n.Id}"));
    }
    [Fact]
    public void Sample_project_table_records_rows_values_and_every_button_without_editing()
    {
        var root = Path.Combine(Path.GetTempPath(), "ui-tree-sample-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data")); Directory.CreateDirectory(Path.Combine(root, "schemas"));
            File.WriteAllText(Path.Combine(root, "data", "items.json"), Data);
            File.WriteAllText(Path.Combine(root, "schemas", "items.schema.json"), Schema);
            using var workspace = new DataWorkspace(DataProject.Open(Path.Combine(root, "data"), Path.Combine(root, "schemas")));
            Assert.True(workspace.Show("items.json"));
            var table = workspace.TableOf("items.json")!;
            using var host = new HeadlessHost(1200, 850) { Drawing = false };
            host.Frames(() => Draw(table.Draw), 3);
            var nodes = Ui.Snapshot().Nodes; LabelledButtons(nodes);
            Assert.Contains(nodes, n => n.Role == UiRole.ListItem && n.Label == "Rope");
            Assert.Contains(nodes, n => n.Role == UiRole.TextField && n.Label.Contains("price") && n.Value == "2");
            Assert.Contains(nodes, n => n.Role == UiRole.Checkbox && n.Checked == true && n.Label.Contains("enabled"));
            Assert.Contains(nodes, n => n.Role == UiRole.Button && n.Label == "Fill shown rows");
            Assert.Contains(nodes, n => n.Role == UiRole.Button && n.Label == "Paste at cell" && !n.Enabled);
            Assert.Contains(nodes, n => n.Role == UiRole.Table);
            Assert.Empty(table.Editor.Session.Journal); Assert.False(table.Editor.Session.Dirty);
            Assert.Equal(Data, File.ReadAllText(Path.Combine(root, "data", "items.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void Text_panel_records_existing_read_only_reason_and_keeps_draft_unapplied()
    {
        var editor = Editor(); var pane = new JsonTextPane(editor, () => "generated source");
        using var host = new HeadlessHost(1200, 850) { Drawing = false };
        host.Frames(() => Draw(pane.Draw), 3);
        var nodes = Ui.Snapshot().Nodes; LabelledButtons(nodes);
        var apply = Assert.Single(nodes, n => n.Role == UiRole.Button && n.Label == "Apply");
        Assert.False(apply.Enabled); Assert.Equal("generated source", apply.DisabledReason);
        Assert.Contains(nodes, n => n.Role == UiRole.Button && n.Label == "Revert" && !n.Enabled);
        Assert.Empty(editor.Session.Journal); Assert.False(editor.Session.Dirty);
    }
    [Fact]
    public void Ai_panel_records_copy_controls_and_guide_without_invoking_them()
    {
        var panel = new AiSetupPanel(AppContext.BaseDirectory);
        using var host = new HeadlessHost(1200, 850) { Drawing = false };
        host.Frames(() => Draw(panel.Draw), 3);
        var nodes = Ui.Snapshot().Nodes; LabelledButtons(nodes);
        Assert.Equal(new[] { "Copy bed-cli command", "Copy Claude Desktop JSON", "Copy claude mcp add" },
            nodes.Where(n => n.Role == UiRole.Button).Select(n => n.Label));
        Assert.Contains(nodes, n => n.Role == UiRole.Link && n.Label == "Read the packaged AI guide");
        Assert.Contains(nodes, n => n.Role == UiRole.Text && n.Label.Contains("does not contact an AI provider"));
    }
    [Fact]
    public void Canvas_records_clipped_children_with_stable_identity_and_no_native_item_change()
    {
        using var host = new HeadlessHost(1200, 850) { Drawing = false };
        void Canvas()
        {
            Im.InvisibleButton("##canvas", new float2(200, 100));
            var native = Im.GetItemID(); var a = Im.GetItemRectMin();
            var parent = Ui.RecordCanvas("Map");
            Ui.RecordCanvasItem(parent, "one", "Beacon", new(a.X-10, a.Y+10, 30, 20), true);
            Assert.Equal(native, Im.GetItemID());
        }
        host.Frames(() => Draw(Canvas), 3); var first = Ui.Snapshot().Nodes;
        var parent = Assert.Single(first, n => n.Role == UiRole.Canvas);
        var child = Assert.Single(first, n => n.Label == "Beacon");
        Assert.Equal(parent.Bounds.X, child.Bounds.X); Assert.Equal(20, child.Bounds.Width);
        Assert.True(child.Selected); Assert.Equal(parent.Id, first[child.Parent].Id);
        host.Frame(() => Draw(Canvas));
        Assert.Equal(child.Id, Assert.Single(Ui.Snapshot().Nodes, n => n.Label == "Beacon").Id);
    }

    private sealed class Plan : IMapSource<DataDocument>
    {
        public MapSnapshot Read(DataDocument document) => new(new MapFrame("plan", 0, "metres", "x", "y"),
            [new(new("plan", "point", "one"), "/items/0", "Rope", new(0, 0), false)], []);
        public MapEdit<DataDocument>? MoveBy(DataDocument document, MapKey key, MapPoint delta) => null;
    }
    private sealed class Boxes : IViewportSource<DataDocument>
    {
        public IEnumerable<ViewportItem> Items(DataDocument document) =>
            [new("/items/0", Matrix4x4.Identity, MeshData.Cube, 0xffffffff)];
        public ICommand<DataDocument>? MoveTo(DataDocument document, string id, Vector3 world) => null;
    }
    [Fact]
    public void Real_map_and_viewport_record_their_drawn_items_without_document_changes()
    {
        var editor = Editor(); editor.Select("/items/0");
        var map = new MapCanvas<DataDocument>(editor, new Plan());
        var view = new ViewportView<DataDocument>(editor, new Boxes());
        using var host = new HeadlessHost(1200, 850) { Drawing = false };
        host.Frames(() => Draw(() => map.Draw()), 3);
        var mapNodes = Ui.Snapshot().Nodes;
        var mapItem = Assert.Single(mapNodes, n => n.Role == UiRole.ListItem && n.Label == "Rope");
        Assert.True(mapItem.Selected); Assert.False(mapItem.Bounds.IsEmpty);
        Assert.Equal(UiRole.Canvas, mapNodes[mapItem.Parent].Role);
        host.Frames(() => Draw(view.Draw), 3);
        var viewport = Ui.Snapshot().Nodes;
        var item = Assert.Single(viewport, n => n.Role == UiRole.ListItem && n.Label == "/items/0");
        Assert.True(item.Selected); Assert.False(item.Bounds.IsEmpty);
        Assert.Equal("Viewport", viewport[item.Parent].Label);
        Assert.Empty(editor.Session.Journal); Assert.False(editor.Session.Dirty);
    }

    [Fact]
    public async Task Compare_panel_records_its_read_only_controls_and_keeps_saved_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ui-tree-compare-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data")); Directory.CreateDirectory(Path.Combine(root, "schemas"));
            var path = Path.Combine(root, "data", "items.json"); File.WriteAllText(path, Data);
            File.WriteAllText(Path.Combine(root, "schemas", "items.schema.json"), Schema);
            var project = DataProject.Open(Path.Combine(root, "data"), Path.Combine(root, "schemas"));
            var file = Assert.Single(project.Files); Assert.True(file.Open());
            var editor = new EditorModel<DataDocument>(file.Session!, DataEditing.Outline);
            var compare = new CompareFilePanel(file, editor, project.Schemas); var panel = compare.Panel();
            editor.Add(panel);
            Assert.True(Assert.Single(panel.Commands!, c => c.Id == "editor.compare.saved").Run());
            await compare.Pending!.WaitAsync(TimeSpan.FromSeconds(10));
            using var host = new HeadlessHost(1200, 850) { Drawing = false };
            host.Frames(() => Draw(panel.Draw), 3);
            var nodes = Ui.Snapshot().Nodes; LabelledButtons(nodes);
            Assert.Contains(nodes, n => n.Role == UiRole.Button && n.Label == "Choose another copy...");
            Assert.Contains(nodes, n => n.Role == UiRole.Button && n.Label == "Read copy and compare again");
            Assert.Contains(nodes, n => n.Role == UiRole.Text && n.Label.Contains("0 added, 0 removed, 0 changed"));
            Assert.Equal(Data, File.ReadAllText(path)); Assert.Empty(editor.Session.Journal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
