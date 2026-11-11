using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class TableLayoutUi35Tests
{
    private sealed class Files : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("own-table-layout-").FullName;
        public string Data=>Path.Combine(Root,"data");public string Schemas=>Path.Combine(Root,"schemas");
        public EditorLayoutStore Store=>new(Path.Combine(Root,"user","layout.ini"));
        public Files()
        {
            Assert.StartsWith(Path.GetFullPath(Root),Path.GetFullPath(Store.Path),StringComparison.OrdinalIgnoreCase);
            Assert.False(string.Equals(Path.GetFullPath(Store.Path),Path.GetFullPath(EditorLayoutStore.DefaultPath),StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            const string text="""{"items":[{"name":"Zebra","amount":9},{"name":"Amber","amount":2}],"other":[{"name":"Birch","amount":4}]}""";
            const string schema="""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string","title":"Name"},"amount":{"type":"number","title":"Amount"}}}},"other":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string","title":"Name"},"amount":{"type":"number","title":"Amount"}}}}}}""";
            foreach(var name in new[]{"alpha","beta"}){File.WriteAllText(Path.Combine(Data,name+".json"),text);File.WriteAllText(Path.Combine(Schemas,name+".schema.json"),schema);}
        }
        public void Dispose(){Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    private sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host=new(1200,750){Drawing=false};
        public readonly DataTableView Table;
        public Rig(Files files,string file="alpha.json",bool load=false)
        {
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());if(load)Assert.True(files.Store.LoadCurrent());
            var project=DataProject.Open(files.Data,files.Schemas);var data=project.Find(file)!;Assert.True(data.Open());
            var editor=new EditorModel<DataDocument>(data.Session!,DataEditing.OutlineFor(project.Schemas,data.Schema!));
            Table=new(editor,DataEditing.ObjectType(project.Schemas,data.Schema!)){File=file,List="/items"};
            Host.Frames(Draw,4);
        }
        public void Draw(){ImGui.SetNextWindowPos(new float2(20,20));ImGui.SetNextWindowSize(new float2(1000,650));Ui.Begin("Table layout proof"u8);Table.Draw();Ui.End();}
        public uint Id=>(uint)Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Table).Id;
        public ImGuiTablePtr Native=>ImGui.Internal.TableFindByID(new ImGuiID(Id));
        public UiBounds Cell(int column)=>Ui.Snapshot().Nodes.First(n=>n.Role==UiRole.TextField&&n.Label.EndsWith(column==1?": Name":": Amount",StringComparison.Ordinal)).Bounds;
        public float2 Header(int column)=>new(Cell(column).X+Cell(column).Width/2,Native.OuterRect.Min.Y+ImGui.GetFrameHeight()/2);
        public float Width(int column)=>Cell(column).Width;
        public bool Reordered=>Cell(1).X>Cell(2).X;
        public void Resize(int column,float delta)
        {
            var t=Native;var cell=Cell(column);var x=cell.X+cell.Width+ImGui.GetStyle().CellPadding.X+1;var y=t.OuterRect.Min.Y+ImGui.GetFrameHeight()/2;
            Host.Drag(Draw,new float2(x,y),new float2(x+delta,y),steps:5);Host.Frames(Draw,2);
        }
        public void Dispose()=>Host.Dispose();
    }
    [Fact]
    public void Native_width_order_and_sort_survive_fresh_context_and_do_not_change_project_bytes()
    {
        using var files=new Files();var before=Directory.GetFiles(files.Data).ToDictionary(p=>p,File.ReadAllBytes);
        float width;uint id;
        using(var r=new Rig(files))
        {
            id=r.Id;var initial=r.Width(1);r.Resize(1,75);width=r.Width(1);Assert.True(width>initial+40,$"initial={initial}, final={width}");
            var from=r.Header(1);var to=r.Header(2);r.Host.Drag(r.Draw,from,new float2(to.X+30,to.Y),steps:8);r.Host.Frames(r.Draw,2);
            Assert.True(r.Reordered);
            var header=r.Header(2);r.Host.Click(r.Draw,header.X,header.Y);r.Host.Frames(r.Draw,2);
            Assert.Equal(("amount",false),r.Table.Sort);Assert.Equal("/items/1",r.Table.Visible()[0]);
            Assert.True(files.Store.SaveCurrent());Assert.False(r.Table.Editor.Session.Dirty);Assert.Equal(0,r.Table.Editor.Session.History.UndoCount);
        }
        using(var r=new Rig(files,load:true))
        {
            Assert.Equal(id,r.Id);Assert.InRange(r.Width(1),width-1,width+1);Assert.True(r.Reordered);
            Assert.Equal(("amount",false),r.Table.Sort);Assert.Equal("/items/1",r.Table.Visible()[0]);
        }
        foreach(var pair in before)Assert.Equal(pair.Value,File.ReadAllBytes(pair.Key));
    }
    [Fact]
    public void File_and_list_settings_are_independent_and_header_reset_is_an_action()
    {
        using var files=new Files();uint first;float width;
        using(var r=new Rig(files))
        {
            first=r.Id;r.Resize(1,80);width=r.Width(1);
            var amount=r.Header(2);r.Host.Click(r.Draw,amount.X,amount.Y);r.Host.Frames(r.Draw,2);Assert.Equal(("amount",false),r.Table.Sort);
            r.Table.List="/other";r.Host.Frames(r.Draw,3);Assert.NotEqual(first,r.Id);Assert.True(r.Width(1)<width-40);
            Assert.Null(r.Table.Sort);
            r.Table.List="/items";r.Host.Frames(r.Draw,3);Assert.Equal(first,r.Id);Assert.InRange(r.Width(1),width-1,width+1);
            Assert.Equal(("amount",false),r.Table.Sort);Assert.Equal("/items/1",r.Table.Visible()[0]);
            Assert.True(files.Store.SaveCurrent());
        }
        using(var r=new Rig(files,"beta.json",load:true)){Assert.NotEqual(first,r.Id);Assert.True(r.Width(1)<width-40);}
        using(var r=new Rig(files,load:true))
        {
            var from=r.Header(1);var to=r.Header(2);r.Host.Drag(r.Draw,from,new float2(to.X+30,to.Y),steps:8);r.Host.Frames(r.Draw,2);
            Assert.True(r.Reordered);var sort=r.Header(2);r.Host.Click(r.Draw,sort.X,sort.Y);r.Host.Frames(r.Draw,2);Assert.NotNull(r.Table.Sort);
            var header=r.Header(1);var io=ImGui.GetIO();io.AddMousePosEvent(header.X,header.Y);r.Host.Frame(r.Draw);
            io.AddMouseButtonEvent(1,true);r.Host.Frame(r.Draw);io.AddMouseButtonEvent(1,false);r.Host.Frames(r.Draw,2);
            var reset=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Reset table layout");
            Assert.True(reset.Actions.HasFlag(UiActions.Invoke));var ticket=Ui.RequestAction(reset.Id,UiActions.Invoke);r.Host.Frames(r.Draw,3);
            Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.False(r.Reordered);Assert.True(r.Width(1)<width-40,$"before={width}, after={r.Width(1)}");Assert.Null(r.Table.Sort);
            Assert.Equal("Reset table widths, column order and sort.",r.Table.Said);Assert.False(r.Table.Editor.Session.Dirty);
            Assert.True(files.Store.SaveCurrent());
        }
        using(var r=new Rig(files,load:true)){Assert.True(r.Width(1)<width-40,$"before={width}, after={r.Width(1)}");Assert.Null(r.Table.Sort);Assert.False(r.Reordered);}
    }
    [Fact]
    public void Header_keyboard_menu_reaches_named_reset_and_Enter_uses_its_handler()
    {
        using var files=new Files();using var r=new Rig(files);var initial=r.Width(1);r.Resize(1,75);
        var header=r.Header(1);r.Host.Click(r.Draw,header.X,header.Y);r.Host.Press(r.Draw,ImGuiKey.F10,shift:true);r.Host.Frames(r.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Dialog&&n.Label=="Table column actions");
        UiNode Reset()=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Reset table layout");
        for(var i=0;i<8&&!Reset().Focused;i++)r.Host.Press(r.Draw,ImGuiKey.DownArrow);
        Assert.True(Reset().Focused);r.Host.Press(r.Draw,ImGuiKey.Enter);r.Host.Frames(r.Draw,3);
        Assert.InRange(r.Width(1),initial-1,initial+1);Assert.Null(r.Table.Sort);Assert.Equal(0,r.Table.Editor.Session.History.UndoCount);
    }

}
