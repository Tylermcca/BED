using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ObjectContextCallbackTests
{
    private sealed class Rig : IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("bed-object-callback-").FullName;
        private readonly string data,schemas;
        public const string Text="""{"items":[{"name":"Alpha","amount":1,"note":"first"},{"name":"Beta","amount":2,"note":"two\t\"quoted\"\nlines"}],"other":[{"name":"Elsewhere","amount":3,"note":"third"}]}""";
        public readonly HeadlessHost Host=new(1000,700) {Drawing=false};
        public readonly DataWorkspace Workspace;
        public EditorWindow<DataDocument> Window=>Workspace.Window!;
        public DataTableView Table=>Workspace.TableOf("invented.json")!;
        public Rig()
        {
            data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"invented.schema.json"),"""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"amount":{"type":"integer"},"note":{"type":"string"}}}},"other":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"amount":{"type":"integer"},"note":{"type":"string"}}}}}}""");
            File.WriteAllText(Path.Combine(data,"invented.json"),Text);
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Show("invented.json"),Workspace.Said);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());Window.Editor.SetOpen("editor.table",false);Host.Frames(Draw,3);
        }
        public void Draw()=>Window.DrawDocked();
        public UiNode Node(string label,UiRole role)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label && n.Role==role);
        public void Open(string route="right")
        {
            Window.Editor.Select("/items/0");var b=Node("Beta",UiRole.ListItem).Bounds;
            if(route=="right")
            {
                Host.MoveMouse(Draw,b.X+b.Width/2,b.Y+b.Height/2);
                ImGui.GetIO().AddMouseButtonEvent(1,true);Host.Frame(Draw);
                ImGui.GetIO().AddMouseButtonEvent(1,false);Host.Frames(Draw,2);
            }
            else
            {
                Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);
                Host.Press(Draw,route=="menu" ? ImGuiKey.Menu : ImGuiKey.F10,shift:route=="shift");Host.Frames(Draw,2);
            }
            Assert.Equal("/items/1",Window.Editor.Primary);
        }
        public void Click(string label)
        {
            var n=Node(label,UiRole.MenuItem);Assert.True(n.Enabled,n.DisabledReason);
            var b=n.Bounds;Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);Host.Frames(Draw,2);
        }
        public void Unchanged()
        {
            Assert.False(Window.Editor.Session.Dirty);Assert.Equal(0,Window.Editor.Session.History.UndoCount);
            Assert.Empty(Window.Editor.Session.Journal);Assert.Equal(Text,File.ReadAllText(Path.Combine(data,"invented.json")));
        }
        public void Dispose()
        {
            Workspace.Dispose();Host.Dispose();File.Delete(Path.Combine(data,"invented.json"));
            File.Delete(Path.Combine(schemas,"invented.schema.json"));Directory.Delete(data);Directory.Delete(schemas);Directory.Delete(root);
        }
    }

    [Theory]
    [InlineData("right")] [InlineData("menu")] [InlineData("shift")]
    public void Show_in_table_uses_the_target_and_existing_navigation_without_changing_data(string route)
    {
        using var r=new Rig();r.Table.List="/other";r.Table.Filter="hidden";
        r.Open(route);r.Click("Show in table");
        Assert.Equal("/items",r.Table.List);Assert.Equal("",r.Table.Filter);
        Assert.Equal("/items/1",r.Window.Editor.Primary);Assert.True(r.Window.Editor.IsOpen("editor.table"));r.Unchanged();
    }

    [Theory]
    [InlineData("right")] [InlineData("menu")] [InlineData("shift")]
    public void Copy_uses_the_context_row_and_existing_tsv_quoting_not_the_old_anchor(string route)
    {
        using var r=new Rig();r.Table.List="/other";r.Table.Filter="Elsewhere";
        r.Table.Anchor=("/other/0","amount");var anchor=r.Table.Anchor;var range=r.Table.RangeEnd;
        ImGui.SetClipboardText("Invented old clipboard");r.Open(route);r.Click("Copy");
        Assert.Equal("Beta\t2\t\"two\t\"\"quoted\"\"\nlines\"",ImGui.GetClipboardText().ToString());
        Assert.Equal("/other",r.Table.List);Assert.Equal("Elsewhere",r.Table.Filter);
        Assert.Equal(anchor,r.Table.Anchor);Assert.Equal(range,r.Table.RangeEnd);
        Assert.Equal("/items/1",r.Window.Editor.Primary);r.Unchanged();
    }
}
