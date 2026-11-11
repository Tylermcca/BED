using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ContextMenuTests
{
    private sealed class Rig : IDisposable
    {
        public HeadlessHost Host { get; } = new(1000, 700) { Drawing = false };
        public EditorModel<DataDocument> Editor { get; }
        public DataTableView Table { get; }
        public Rig(bool protectMinimum = false)
        {
            const string schema = """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"amount":{"type":"integer"}}}}}}""";
            var schemas = new JsonSchemas().Add("context", protectMinimum ? schema.Replace("\"type\":\"array\"", "\"type\":\"array\",\"minItems\":2") : schema);
            var session = DataKit.Session(new DataFormat().Read("""{"items":[{"name":"Alpha","amount":1},{"name":"Beta","amount":2}]}"""), schemas, "context");
            Editor = new(session, DataEditing.OutlineFor(schemas, "context"));
            Table = new(Editor, DataEditing.ObjectType(schemas, "context"));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Host.Frames(Draw, 5);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(950, 650));
            Ui.Begin("Table context fixture"u8); Table.Draw(); Ui.End();
        }
        public UiNode Node(string label, UiRole role) => Assert.Single(Ui.Snapshot().Nodes, n => n.Label == label && n.Role == role);
        public void Click(string label, UiRole role)
        {
            var b = Node(label, role).Bounds; Host.Click(Draw, b.X + b.Width/2, b.Y + b.Height/2); Host.Frames(Draw, 3);
        }
        public void Open(string route)
        {
            var b = Node("Beta", UiRole.ListItem).Bounds;
            if (route == "right")
            {
                Host.MoveMouse(Draw, b.X + b.Width/2, b.Y + b.Height/2);
                ImGui.GetIO().AddMouseButtonEvent(1, true); Host.Frame(Draw);
                ImGui.GetIO().AddMouseButtonEvent(1, false); Host.Frames(Draw, 3);
            }
            else
            {
                Click("Beta", UiRole.ListItem);
                Host.Press(Draw, route == "menu" ? ImGuiKey.Menu : ImGuiKey.F10, shift: route == "shift");
                Host.Frames(Draw, 3);
            }
        }
        public void Dispose() => Host.Dispose();
    }

    [Theory]
    [InlineData("right")]
    [InlineData("menu")]
    [InlineData("shift")]
    public void Row_target_is_selected_before_menu_and_disabled_reasons_are_truthful(string route)
    {
        using var r = new Rig(); var before = r.Editor.Session.Current;
        r.Editor.Select("/items/0");
        r.Open(route);
        Assert.Equal(new[] { "/items/1" }, r.Editor.Selection);
        foreach (var label in new[] { "Copy", "Paste", "Fill column...", "Delete row", "Insert row above", "Insert row below" })
        {
            var node = r.Node(label, UiRole.MenuItem);
            if (label.StartsWith("Insert"))
            { Assert.False(node.Enabled); Assert.False(string.IsNullOrWhiteSpace(node.DisabledReason)); }
        }
        Assert.Same(before, r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        r.Click("Copy", UiRole.MenuItem);
        Assert.Equal("Beta\t2", ImGui.GetClipboardText().ToString());
        Assert.Same(before, r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
    }

    [Fact]
    public void Paste_stages_on_clicked_row_then_normal_Apply_and_Undo_preserve_other_rows()
    {
        using var r = new Rig(); var before = r.Editor.Session.Current;
        ImGui.SetClipboardText("Changed\t7"); r.Open("right"); r.Click("Paste", UiRole.MenuItem);
        Assert.NotNull(r.Table.Pending); Assert.True(r.Table.Pending.Ok);
        Assert.Same(before, r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        r.Click("Apply", UiRole.Button);
        Assert.Equal("Changed", r.Editor.Session.Current.At("/items/1/name")!.Value.GetString());
        Assert.Equal(7, r.Editor.Session.Current.At("/items/1/amount")!.Value.GetInt32());
        Assert.Equal("Alpha", r.Editor.Session.Current.At("/items/0/name")!.Value.GetString());
        Assert.Equal(1, r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo());
        Assert.Equal(new DataFormat().Write(before), new DataFormat().Write(r.Editor.Session.Current));
    }

    [Fact]
    public void Delete_row_uses_existing_inspector_command_and_one_undo_restores_exact_document()
    {
        using var r = new Rig(); var before = r.Editor.Session.Current;
        r.Open("right");
        Assert.True(r.Node("Delete row", UiRole.MenuItem).Enabled);
        r.Click("Delete row", UiRole.MenuItem);
        Assert.Single(r.Editor.Session.Current.At("/items")!.Value.EnumerateArray());
        Assert.Equal("Alpha", r.Editor.Session.Current.At("/items/0/name")!.Value.GetString());
        Assert.Equal(1, r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo());
        Assert.Equal(new DataFormat().Write(before), new DataFormat().Write(r.Editor.Session.Current));
    }

    [Fact]
    public void Schema_refusal_keeps_row_document_journal_and_history_unchanged()
    {
        using var r = new Rig(protectMinimum: true); var before = r.Editor.Session.Current;
        r.Open("right"); r.Click("Delete row", UiRole.MenuItem);
        Assert.Same(before, r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        Assert.Equal(0, r.Editor.Session.History.UndoCount); Assert.False(r.Editor.Session.Dirty);
        Assert.False(string.IsNullOrWhiteSpace(r.Table.Said));
        Assert.NotEqual("Deleted row; Undo restores it.", r.Table.Said);
    }
}

