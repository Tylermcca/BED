using System.Numerics;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class MultiTransformUi33Tests
{
    private sealed class Validator(bool refuse):IValidator<Scene>
    {
        public IReadOnlyList<Issue> Validate(Scene scene)=>new SceneValidator().Validate(scene).Concat(refuse&&scene.Find("b")!.Position.X>5?[Issue.Error("b must stay at X <= 5","b")]:Array.Empty<Issue>()).ToArray();
    }
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1600,1100){Drawing=false};
        public readonly Scene Original;public readonly EditorWindow<Scene> Window;public EditorModel<Scene> Editor=>Window.Editor;
        public Rig(bool refuse=false)
        {
            static Quaternion R(float y,float p,float r)=>Quaternion.CreateFromYawPitchRoll(y*MathF.PI/180,p*MathF.PI/180,r*MathF.PI/180);
            Original=new([SceneObject.At("a",null,new(1,2,3)) with{Rotation=R(10,20,30)},SceneObject.At("b",null,new(4,2,6)) with{Rotation=R(40,50,60),Scale=new(2,2,2)}]);
            var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new Validator(refuse),SceneKit.Catalog(),Original),SceneEditing.Outline);
            Window=new(editor,new Inspector<Scene>(editor).Add(SceneEditing.ObjectType()));EditorWindow<Scene>.Configure(ImGui.GetIO());editor.Select("a","b");Host.Frames(Window.DrawDocked,4);
        }
        public UiNode Field(string name)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==name);
        public void Type(string field,string value,bool enter=true)
        {var node=Field(field);Assert.False(node.Bounds.IsEmpty);Host.Click(Window.DrawDocked,node.Bounds.X+20,node.Bounds.Y+node.Bounds.Height/2);Host.Press(Window.DrawDocked,ImGuiKey.A,ctrl:true);Host.Type(Window.DrawDocked,value);if(enter)Host.Press(Window.DrawDocked,ImGuiKey.Enter);Host.Frames(Window.DrawDocked,2);}
        public void Dispose()=>Host.Dispose();
    }
    [Fact]
    public void Shared_transform_rows_are_editable_and_mixed_values_are_exposed_without_a_fictitious_number()
    {
        using var r=new Rig();
        foreach(var label in new[]{"Position X","Position Z","Yaw","Pitch","Roll","Scale X","Scale Y","Scale Z"})
        {var row=r.Field(label);Assert.Equal("mixed",row.Value);Assert.True(row.Enabled);Assert.False(row.Bounds.IsEmpty);}
        Assert.Equal("2",r.Field("Position Y").Value);Assert.Empty(r.Editor.Session.Journal);Assert.Same(r.Original,r.Editor.Session.Current);
        var field=r.Field("Position X");r.Host.Click(r.Window.DrawDocked,field.Bounds.X+20,field.Bounds.Y+field.Bounds.Height/2);r.Host.Press(r.Window.DrawDocked,ImGuiKey.Enter);r.Host.Frames(r.Window.DrawDocked,2);
        Assert.Empty(r.Editor.Session.Journal);Assert.Same(r.Original,r.Editor.Session.Current);
    }
    [Theory][InlineData("Position X","7")][InlineData("Position Y","8")][InlineData("Yaw","60")][InlineData("Scale Z","3")]
    public void Native_field_edit_sets_all_selected_objects_as_one_atomic_undo_and_preserves_other_axes(string field,string value)
    {
        using var r=new Rig();r.Type(field,value);var changed=r.Editor.Session.Current;
        foreach(var old in r.Original.Objects)
        {
            var item=changed.Find(old.Id)!;
            if(field=="Position X"){Assert.Equal(old.Position with{X=7},item.Position);Assert.Equal(old.Rotation,item.Rotation);Assert.Equal(old.Scale,item.Scale);}
            if(field=="Position Y"){Assert.Equal(old.Position with{Y=8},item.Position);Assert.Equal(old.Rotation,item.Rotation);Assert.Equal(old.Scale,item.Scale);}
            if(field=="Scale Z"){Assert.Equal(old.Scale with{Z=3},item.Scale);Assert.Equal(old.Position,item.Position);Assert.Equal(old.Rotation,item.Rotation);}
            if(field=="Yaw")
            {var angles=SceneEditing.Angles(old.Rotation);var expected=Quaternion.CreateFromYawPitchRoll(MathF.PI/3,(float)angles.Pitch*MathF.PI/180,(float)angles.Roll*MathF.PI/180);Assert.True(Math.Abs(Quaternion.Dot(expected,item.Rotation))>.99999);Assert.Equal(old.Position,item.Position);Assert.Equal(old.Scale,item.Scale);}
        }
        Assert.Equal(1,r.Editor.Session.History.UndoCount);Assert.Single(r.Editor.Session.Journal);Assert.Equal(new[]{"a","b"},r.Editor.Selection);
        Assert.True(r.Editor.Session.Undo());Assert.Same(r.Original,r.Editor.Session.Current);Assert.True(r.Editor.Session.Redo());Assert.Same(changed,r.Editor.Session.Current);
    }
    [Fact]
    public void Refused_shared_edit_names_the_object_and_keeps_every_selected_object_unchanged()
    {
        using var r=new Rig(refuse:true);r.Type("Position X","9");Assert.Same(r.Original,r.Editor.Session.Current);Assert.Empty(r.Editor.Session.Journal);Assert.Equal(0,r.Editor.Session.History.UndoCount);
        Assert.Contains("b",r.Window.Status);Assert.Contains("X <= 5",r.Window.Status);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.Contains("b must stay",StringComparison.Ordinal));
    }
    [Fact]
    public void Selection_change_drops_uncommitted_mixed_field_text()
    {
        using var r=new Rig();r.Type("Position X","9",enter:false);Assert.Same(r.Original,r.Editor.Session.Current);
        r.Editor.Select("a");r.Host.Frames(r.Window.DrawDocked,3);r.Host.Press(r.Window.DrawDocked,ImGuiKey.Enter);r.Host.Frames(r.Window.DrawDocked,2);Assert.Same(r.Original,r.Editor.Session.Current);Assert.Empty(r.Editor.Session.Journal);
    }
}
