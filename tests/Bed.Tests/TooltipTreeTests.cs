using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Sdk;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class TooltipTreeTests
{
    // Covers the controls actually present in this completed frame, not undisplayed application states.
    private static void DescriptionsCover(UiSnapshot snapshot)
    {
        foreach (var node in snapshot.Nodes)
        {
            if (node.Role is UiRole.Button or UiRole.Link or UiRole.MenuItem
                && node.Label.Length > 0 && !node.Label.Any(char.IsLetterOrDigit))
            {
                Assert.False(string.IsNullOrWhiteSpace(node.Tooltip), $"Symbol control '{node.Label}' has no tooltip.");
                Assert.True(node.Tooltip!.Any(char.IsLetterOrDigit), "A symbol repeated as a tooltip does not name its action.");
            }
            if (!node.Enabled)
            {
                Assert.False(string.IsNullOrWhiteSpace(node.DisabledReason));
                Assert.Contains(node.DisabledReason!, node.Tooltip ?? "");
            }
            if (node.TextEvidence is { Truncated: true }) Assert.Contains(node.Label, node.Tooltip ?? "");
        }
    }
    private static void Begin()
    {
        Im.SetNextWindowPos(new float2(0, 0)); Im.SetNextWindowSize(new float2(850, 600));
        Ui.Begin("Tooltip metadata");
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Actual_reference_arrow_has_a_name_before_hover_without_editing_the_table(float scale)
    {
        const string text = """{"wares":[{"id":"rope"}],"crew":[{"favourite":"rope"}]}""";
        const string schema = """{"properties":{"wares":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"}}}},"crew":{"type":"array","items":{"type":"object","properties":{"favourite":{"type":"string","x-ref":"/wares"}}}}}}""";
        var schemas = new JsonSchemas().Add("tooltip.schema.json", schema); var format = new DataFormat();
        var editor = new EditorModel<DataDocument>(DataKit.Session(format.Read(text), schemas, "tooltip.schema.json"), DataEditing.Outline);
        var table = new DataTableView(editor, DataEditing.ObjectType(schemas, "tooltip.schema.json"))
        { List = "/crew", References = DataEditing.SchemaReferences(schemas, "tooltip.schema.json") };
        var before = format.Write(editor.Session.Current);
        using var host = new HeadlessHost(1000, 700) { Drawing = false };
        Im.GetStyle().FontScaleMain = scale;
        void Draw() { Begin(); table.Draw(); Ui.End(); }
        host.MoveMouse(Draw, -100, -100); host.Frames(Draw, 4);
        var snapshot = Ui.Snapshot();
        var arrow = Assert.Single(snapshot.Nodes, n => n.Role == UiRole.Button && n.Label == "->");
        Assert.Equal("Follow reference", arrow.Tooltip); Assert.False(arrow.Hovered);
        DescriptionsCover(snapshot);
        Assert.Equal(before, format.Write(editor.Session.Current)); Assert.Empty(editor.Session.Journal);
        Assert.Equal(0, editor.Session.Revision);
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Proven_truncated_labels_and_paths_keep_their_full_description_without_hover(float scale)
    {
        using var host = new HeadlessHost(1000, 700) { Drawing = false };
        Im.GetStyle().FontScaleMain = scale;
        const string path = "C:/invented/workshop/long path/data/example.json";
        void Draw()
        {
            Begin(); Ui.Button("A deliberately long button label", new float2(35, 60));
            var p = Im.GetCursorScreenPos(); Im.PushClipRect(p, p + new float2(30, 80), true);
            Ui.Text(path, "path"); Im.PopClipRect(); Ui.End();
        }
        host.MoveMouse(Draw, -100, -100); host.Frames(Draw, 4);
        var snapshot = Ui.Snapshot();
        foreach (var label in new[] { "A deliberately long button label", path })
        {
            var node = Assert.Single(snapshot.Nodes, n => n.Label == label);
            Assert.True(node.TextEvidence!.Value.Truncated); Assert.Equal(label, node.Tooltip); Assert.False(node.Hovered);
        }
        DescriptionsCover(snapshot);
    }

    [Fact]
    public void Disabled_menu_and_button_metadata_contain_their_actual_reason_before_hover()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Begin(); Ui.BeginDisabled("Select an item first"); Ui.Button("Delete"); Ui.EndDisabled();
            Ui.MenuItem("Unavailable", enabled: false, disabledReason: "This adapter cannot export"); Ui.End();
        }
        host.MoveMouse(Draw, -100, -100); host.Frames(Draw, 3);
        var snapshot = Ui.Snapshot();
        var button = Assert.Single(snapshot.Nodes, n => n.Label == "Delete");
        var menu = Assert.Single(snapshot.Nodes, n => n.Label == "Unavailable");
        Assert.Equal("Select an item first", button.Tooltip);
        Assert.Equal("This adapter cannot export", menu.DisabledReason);
        Assert.Equal(menu.DisabledReason, menu.Tooltip);
        Assert.False(button.Hovered); Assert.False(menu.Hovered);
        DescriptionsCover(snapshot);
    }

    [Fact]
    public void Guard_rejects_a_new_undescribed_symbol_and_accepts_a_named_one_without_mutating_old_snapshot()
    {
        using var host = new HeadlessHost { Drawing = false }; var annotate = false;
        void Draw() { Begin(); Ui.Button("??"); if (annotate) Ui.TooltipLast("Show available actions"); Ui.End(); }
        host.MoveMouse(Draw, -100, -100); host.Frames(Draw, 3); var missing = Ui.Snapshot();
        Assert.ThrowsAny<XunitException>(() => DescriptionsCover(missing));
        annotate = true; host.Frame(Draw); DescriptionsCover(Ui.Snapshot());
        Assert.Null(Assert.Single(missing.Nodes, n => n.Label == "??").Tooltip);
    }
}
