using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class TableBulkUi20Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1600,1000){Drawing=false};
        public readonly DataFormat Format=new();public readonly DocumentSession<DataDocument> Session;public readonly EditorModel<DataDocument> Editor;
        public readonly DataTableView Table;public readonly EditorWindow<DataDocument> Window;
        private readonly string root=Directory.CreateTempSubdirectory("table-bulk-ui20-").FullName;
        public string Disk=>Path.Combine(root,"items.json");
        public Action Draw=>Window.DrawDocked;
        public Rig()
        {
            const string schema="""
            {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","required":["name","amount"],"properties":{"name":{"type":"string","title":"Name"},"amount":{"type":"number","title":"Amount","minimum":0,"maximum":100}}}}}}
            """;
            const string text="""{"items":[{"name":"Alpha","amount":11},{"name":"Beta","amount":22},{"name":"Gamma","amount":33}]}""";
            var schemas=new JsonSchemas().Add("bulk",schema);Session=DataKit.Session(Format.Read(text),schemas,"bulk");Session.Save(Disk);
            Editor=new(Session,DataEditing.OutlineFor(schemas,"bulk"));var type=DataEditing.ObjectType(schemas,"bulk");Table=new(Editor,type);Editor.Add(Table.AsPanel());
            Window=new(Editor,new Inspector<DataDocument>(Editor).Add(type),new Shortcuts());EditorWindow<DataDocument>.Configure(ImGui.GetIO());Frames(5);
        }
        public void Frames(int count=3)=>Host.Frames(Draw,count);
        private static bool InTable(UiSnapshot tree,UiNode node)
        {for(var p=node.Parent;p>=0;p=tree.Nodes[p].Parent)if(tree.Nodes[p].Role==UiRole.Window&&tree.Nodes[p].Label=="Table")return true;return false;}
        public UiNode Node(UiRole role,string name)
        {var tree=Ui.Snapshot();return Assert.Single(tree.Nodes,n=>n.Role==role&&n.Label==name&&InTable(tree,n));}
        public void Click(UiNode node)=>Host.Click(Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);
        public void Row(string name,bool ctrl=false,bool shift=false)
        {
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,ctrl);io.AddKeyEvent(ImGuiKey.Mod_Shift,shift);
            try{Click(Node(UiRole.ListItem,name));}finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);io.AddKeyEvent(ImGuiKey.Mod_Shift,false);Frames();}
        }
        public void Pick()=>Pick("Alpha","Gamma");
        public void Pick(string first,string second){Row(first);Row(second,ctrl:true);Assert.Equal(2,Editor.Selection.Count);Assert.True(Node(UiRole.ListItem,first).Selected);Assert.True(Node(UiRole.ListItem,second).Selected);}
        public void Preview(string value)
        {
            Click(Node(UiRole.Combo,"Fill column"));var amount=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="Amount");Click(amount);
            Click(Node(UiRole.TextField,"Fill value"));Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,value);
            Click(Node(UiRole.Button,"Fill selected rows"));Frames();Assert.NotNull(Table.Pending);
        }
        public void Apply(){Click(Node(UiRole.Button,"Apply"));Frames();}
        public double Value(int row)=>Session.Current.At($"/items/{row}/amount")!.Value.GetDouble();
        public void Clean(){Assert.Equal(0,Session.History.UndoCount);Assert.Empty(Session.Journal);Assert.False(Session.Dirty);}
        public void Dispose(){Host.Dispose();Directory.Delete(root,true);}
    }
    [Fact] public void Ctrl_click_rows_set_one_field_atomically_and_native_undo_restores_both_without_saving_other_rows()
    {
        using var r=new Rig();var before=r.Session.Current;var disk=File.ReadAllBytes(r.Disk);r.Pick();r.Preview("8");r.Clean();
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Preview targets 2 selected rows; other rows keep their values.");
        Assert.True(r.Node(UiRole.Button,"Apply").Enabled);r.Apply();Assert.Equal(8,r.Value(0));Assert.Equal(22,r.Value(1));Assert.Equal(8,r.Value(2));
        Assert.Equal(1,r.Session.History.UndoCount);Assert.Single(r.Session.Journal);Assert.Equal(disk,File.ReadAllBytes(r.Disk));
        Assert.Equal("Set 2 cells in selected rows; one Undo reverses the whole edit.",r.Table.Said);
        r.Host.Press(r.Draw,ImGuiKey.Z,ctrl:true);r.Frames();Assert.Same(before,r.Session.Current);Assert.Equal(0,r.Session.History.UndoCount);
    }
    [Fact] public void Native_CtrlSpace_toggles_focused_row_once_and_ShiftClick_extends_a_range()
    {
        using var r=new Rig();r.Row("Alpha");r.Host.Press(r.Draw,ImGuiKey.DownArrow);r.Frames();Assert.True(r.Node(UiRole.ListItem,"Beta").Focused);
        r.Host.Press(r.Draw,ImGuiKey.Space,ctrl:true);r.Frames();Assert.Equal(new[]{"/items/0","/items/1"},r.Editor.Selection);
        r.Host.Press(r.Draw,ImGuiKey.Space,ctrl:true);r.Frames();Assert.Equal(new[]{"/items/0"},r.Editor.Selection);
        r.Row("Alpha");r.Row("Gamma",shift:true);Assert.Equal(new[]{"/items/0","/items/1","/items/2"},r.Editor.Selection);r.Clean();
    }
    [Theory][InlineData("bad-number",true)][InlineData("-5",false)]
    public void Scalar_and_schema_refusals_name_each_selected_row_and_never_partially_edit(string value,bool scalar)
    {
        using var r=new Rig();var before=r.Session.Current;r.Pick();r.Preview(value);
        if(scalar)
        {
            Assert.False(r.Node(UiRole.Button,"Apply").Enabled);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Alpha, amount:",StringComparison.Ordinal));
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Gamma, amount:",StringComparison.Ordinal));
            r.Click(r.Node(UiRole.Button,"Cancel"));r.Frames();
        }
        else
        {
            Assert.True(r.Node(UiRole.Button,"Apply").Enabled);r.Apply();Assert.Contains("no rows changed",r.Table.Said);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Alpha, Amount:",StringComparison.Ordinal));
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Gamma, Amount:",StringComparison.Ordinal));
        }
        Assert.Same(before,r.Session.Current);r.Clean();
    }
    [Fact] public void Filtering_and_sorting_do_not_change_the_selected_row_targets()
    {
        using var r=new Rig();r.Pick();r.Table.Sort=("amount",true);r.Table.Filter="Alpha";r.Frames();
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("2 rows selected (1 hidden by the filter)",StringComparison.Ordinal));
        r.Preview("9");r.Apply();Assert.Equal(9,r.Value(0));Assert.Equal(22,r.Value(1));Assert.Equal(9,r.Value(2));Assert.Equal(1,r.Session.History.UndoCount);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void A_changed_document_or_selection_refuses_the_captured_preview_without_retargeting(bool document)
    {
        using var r=new Rig();r.Pick();r.Preview("7");
        if(document)Assert.True(r.Session.Execute(new SetValue("/items/1/amount",JsonSerializer.SerializeToElement(5))));else Assert.True(r.Editor.Select("/items/1","/items/2"));
        r.Frames();r.Apply();Assert.Contains("changed",r.Table.Said);Assert.Equal(11,r.Value(0));Assert.Equal(33,r.Value(2));Assert.Equal(document?1:0,r.Session.History.UndoCount);
    }
    [Fact] public void Host_policy_receives_the_selected_row_touches_and_refuses_the_whole_batch()
    {
        using var r=new Rig();r.Pick();r.Preview("8");var called=false;
        r.Editor.EditRefusal=(document,commands,touches)=>
        {
            called=true;Assert.IsType<SetValues>(Assert.Single(commands));Assert.Equal(new[]{"/items/0","/items/2"},touches);
            return "Gamma, Amount: fixture policy refuses this row.";
        };
        r.Apply();Assert.True(called);Assert.Contains("Gamma, Amount",r.Table.Said);Assert.Equal(11,r.Value(0));Assert.Equal(33,r.Value(2));r.Clean();
    }
    [Fact] public void Selection_outside_the_shown_table_cannot_silently_be_dropped_from_the_edit()
    {
        using var r=new Rig();Assert.True(r.Editor.Select("/items","/items/0"));r.Frames();
        var button=r.Node(UiRole.Button,"Fill selected rows");Assert.False(button.Enabled);Assert.Equal("Select rows from this table only.",button.DisabledReason);r.Clean();
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void Ending_a_preview_after_selection_shrinks_restores_a_surviving_button_focus(bool apply)
    {
        using var r=new Rig();r.Pick();r.Preview("7");Assert.True(r.Editor.Select("/items/0"));r.Frames();
        r.Click(r.Node(UiRole.Button,apply?"Apply":"Cancel"));r.Frames(4);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Focused&&n.Label is "Fill shown rows" or "Paste at cell");r.Clean();
    }
}
