using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ObjectContextMenuTests
{
    [Theory]
    [InlineData("right", false)][InlineData("menu", false)][InlineData("shift", false)][InlineData("right", true)]
    public void Target_selection_and_existing_removal_preserve_other_objects_and_validation(string route, bool minimum)
    {
        const string schema = """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}}}}}}""";
        var schemas = new JsonSchemas().Add("objects", minimum ? schema.Replace("\"type\":\"array\"", "\"type\":\"array\",\"minItems\":2") : schema);
        var session = DataKit.Session(new DataFormat().Read("""{"items":[{"name":"Alpha"},{"name":"Beta"}]}"""), schemas, "objects");
        var before = new DataFormat().Write(session.Current);
        var editor = new EditorModel<DataDocument>(session, DataEditing.OutlineFor(schemas, "objects"));
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "objects"));
        using var host = new HeadlessHost(1000,700) { Drawing=false };
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var window = new EditorWindow<DataDocument>(editor, inspector);
        void Draw() => window.DrawDocked();
        host.Frames(Draw,3); editor.Select("/items/0");
        UiNode Node(string label, UiRole role) => Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role==role);
        var targetLabel=editor.Outline.Label(session.Current,"/items/1")!;
        var b=Node(targetLabel,UiRole.ListItem).Bounds;
        if(route=="right")
        {
            host.MoveMouse(Draw,b.X+b.Width/2,b.Y+b.Height/2);
            ImGui.GetIO().AddMouseButtonEvent(1,true);host.Frame(Draw);
            ImGui.GetIO().AddMouseButtonEvent(1,false);host.Frames(Draw,2);
        }
        else
        {
            host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);
            host.Press(Draw,route=="menu"?ImGuiKey.Menu:ImGuiKey.F10,shift:route=="shift");host.Frames(Draw,2);
        }
        Assert.Equal("/items/1",editor.Primary);Assert.Equal(before,new DataFormat().Write(session.Current));
        foreach(var label in new[]{"Copy","Show in table"})
        {var node=Node(label,UiRole.MenuItem);Assert.False(node.Enabled);Assert.NotEmpty(node.DisabledReason!);}
        var delete=Node("Delete",UiRole.MenuItem);Assert.True(delete.Enabled);
        host.Click(Draw,delete.Bounds.X+delete.Bounds.Width/2,delete.Bounds.Y+delete.Bounds.Height/2);
        if(minimum)
        {Assert.Equal(before,new DataFormat().Write(session.Current));Assert.False(session.History.CanUndo);Assert.False(string.IsNullOrEmpty(window.Status));}
        else
        {
            Assert.Equal("Alpha",session.Current.At("/items/0/name")!.Value.GetString());
            Assert.Equal(1,session.Current.At("/items")!.Value.GetArrayLength());
            Assert.True(session.Undo());Assert.Equal(before,new DataFormat().Write(session.Current));Assert.False(session.History.CanUndo);
        }
    }
}
