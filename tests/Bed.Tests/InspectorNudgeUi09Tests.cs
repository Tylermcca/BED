using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class InspectorNudgeUi09Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1300,950){Drawing=false};
        public readonly EditorWindow<DataDocument> Window;
        public EditorModel<DataDocument> Editor=>Window.Editor;
        public Rig(double amount=5)
        {
            var schemas=new JsonSchemas().Add("fixture", """{"type":"object","properties":{"amount":{"type":"number","minimum":0,"maximum":50},"count":{"type":"integer"},"note":{"type":"string"}}}""");
            var document=new DataFormat().Read(System.Text.Json.JsonSerializer.Serialize(new{amount,count=3,note="plain"}));
            var editor=new EditorModel<DataDocument>(DataKit.Session(document,schemas,"fixture"),DataEditing.Outline);
            Window=new(editor,new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas,"fixture")));
            Assert.True(editor.Select(""));EditorWindow<DataDocument>.Configure(ImGui.GetIO());Host.Frames(Window.DrawDocked,5);
        }
        public UiNode Field(string label)
        {
            var fields=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.TextField).ToArray();
            Assert.True(fields.Any(n=>n.Label==label),"No "+label+" among: "+string.Join(" | ",fields.Select(n=>n.Label+"="+n.Value)));
            return Assert.Single(fields,n=>n.Label==label);
        }
        public void Focus(string label)
        {
            var node=Field(label);Host.Click(Window.DrawDocked,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);
            Assert.True(Field(label).Focused);
        }
        public double Value(string name)=>Editor.Session.Current.At("/"+name)!.Value.GetDouble();
        public void Key(ImGuiKey key,bool shift=false,bool ctrl=false)=>Host.Press(Window.DrawDocked,key,shift:shift,ctrl:ctrl);
        public void Hold(ImGuiKey key,int frames=45)
        {ImGui.GetIO().AddKeyEvent(key,true);Host.Frames(Window.DrawDocked,frames);}
        public void Release(ImGuiKey key){ImGui.GetIO().AddKeyEvent(key,false);Host.Frames(Window.DrawDocked,3);}
        public void Dispose()=>Host.Dispose();
    }
    [Theory][InlineData(false,false,6)][InlineData(true,false,15)][InlineData(false,true,5.1)][InlineData(true,true,6)]
    public void Focused_number_nudges_by_declared_modifier_step_and_tree_keeps_label_and_value(bool shift,bool ctrl,double expected)
    {
        using var r=new Rig();r.Focus("amount");r.Key(ImGuiKey.UpArrow,shift,ctrl);
        Assert.Equal(expected,r.Value("amount"),10);Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.Equal(expected.ToString("R",System.Globalization.CultureInfo.InvariantCulture),r.Field("amount").Value);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("Up/Down +/-1; Shift x10; Ctrl x0.1",StringComparison.Ordinal));
        r.Key(ImGuiKey.DownArrow,shift,ctrl);Assert.Equal(5,r.Value("amount"),10);Assert.Equal(2,r.Editor.Session.History.UndoCount);
    }
    [Fact] public void Held_arrow_repeats_in_field_then_commits_one_undo_step_on_release()
    {
        using var r=new Rig();r.Focus("amount");r.Hold(ImGuiKey.UpArrow);
        var preview=double.Parse(r.Field("amount").Value!,System.Globalization.CultureInfo.InvariantCulture);Assert.True(preview>6);
        Assert.Equal(5,r.Value("amount"));Assert.Equal(0,r.Editor.Session.History.UndoCount);
        r.Release(ImGuiKey.UpArrow);Assert.Equal(preview,r.Value("amount"));Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo());Assert.Equal(5,r.Value("amount"));Assert.True(r.Editor.Session.Redo());Assert.Equal(preview,r.Value("amount"));
    }
    [Fact] public void Range_and_integer_validation_refuse_without_history_or_replacing_typed_input()
    {
        using var r=new Rig(50);var steps=r.Editor.Session.History.UndoCount;
        r.Focus("amount");r.Key(ImGuiKey.UpArrow);Assert.Equal(50,r.Value("amount"));Assert.Equal(steps,r.Editor.Session.History.UndoCount);Assert.Contains("allowed range",r.Window.Status);
        r.Focus("count");r.Key(ImGuiKey.UpArrow,ctrl:true);Assert.Equal(3,r.Value("count"));Assert.Equal(steps,r.Editor.Session.History.UndoCount);Assert.Contains("whole number",r.Window.Status);
        r.Key(ImGuiKey.UpArrow);Assert.Equal(4,r.Value("count"));Assert.Equal(steps+1,r.Editor.Session.History.UndoCount);
    }
    [Fact] public void Document_change_during_held_nudge_does_not_commit_stale_preview()
    {
        using var r=new Rig();r.Focus("amount");r.Hold(ImGuiKey.UpArrow,25);
        Assert.True(r.Editor.Session.Execute(new SetValue("/amount",System.Text.Json.JsonSerializer.SerializeToElement(20))));
        r.Release(ImGuiKey.UpArrow);Assert.Equal(20,r.Value("amount"));Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.Contains("document changed",r.Window.Status,StringComparison.OrdinalIgnoreCase);
        r.Key(ImGuiKey.Tab);Assert.Equal(20,r.Value("amount"));Assert.Equal(1,r.Editor.Session.History.UndoCount);
    }
    [Theory][InlineData("Position X",1)][InlineData("Yaw",1)][InlineData("Scale X",2)]
    public void Scene_transform_numbers_use_the_same_keyboard_command_and_undo(string label,double expected)
    {
        using var host=new HeadlessHost(1300,1100){Drawing=false};
        var scene=new Scene([SceneObject.At("crate",null,System.Numerics.Vector3.Zero)]);
        var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
        var window=new EditorWindow<Scene>(editor,new Inspector<Scene>(editor).Add(SceneEditing.ObjectType()));
        Assert.True(editor.Select("crate"));EditorWindow<Scene>.Configure(ImGui.GetIO());host.Frames(window.DrawDocked,4);
        var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==label);
        host.Click(window.DrawDocked,field.Bounds.X+field.Bounds.Width/2,field.Bounds.Y+field.Bounds.Height/2);
        host.Press(window.DrawDocked,ImGuiKey.UpArrow);
        field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==label);
        Assert.Equal(expected,double.Parse(field.Value!,System.Globalization.CultureInfo.InvariantCulture),5);Assert.Equal(1,editor.Session.History.UndoCount);
        Assert.True(editor.Session.Undo());Assert.Equal(scene,editor.Session.Current);
    }
    [Fact] public void Arrow_in_text_field_never_changes_document_or_creates_undo()
    {
        using var r=new Rig();r.Focus("note");r.Key(ImGuiKey.UpArrow);r.Key(ImGuiKey.DownArrow);
        Assert.Equal("plain",r.Editor.Session.Current.At("/note")!.Value.GetString());Assert.Equal(0,r.Editor.Session.History.UndoCount);
    }
}
