using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ViewportSnapUi16Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1200,850){Drawing=false};
        public readonly Scene Original=new([SceneObject.At("box",null,Vector3.Zero)]);
        public readonly EditorModel<Scene> Editor;
        public readonly ViewportView<Scene> View;
        public Rig()
        {
            Editor=new(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Original),SceneEditing.Outline);Editor.Select("box");
            View=new(Editor,new SceneViewport()){Camera=new(Vector3.Zero,0,.3f,8)};EditorWindow<Scene>.Configure(ImGui.GetIO());Host.Frames(Draw,4);
        }
        public void Draw(){ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(1200,850));Ui.Begin("Viewport snap"u8);View.Draw();Ui.End();}
        public UiNode Node(UiRole role,string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==role&&n.Label==label);
        public void Click(UiNode node){Assert.True(node.Enabled,node.DisabledReason);Host.Click(Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);Host.Frames(Draw,2);}
        public void Focus()
        {
            for(var i=0;!Node(UiRole.Canvas,"Viewport").Focused&&i<10;i++)Host.Press(Draw,ImGuiKey.Tab);
            Assert.True(Node(UiRole.Canvas,"Viewport").Focused);
        }
        public void Key(ImGuiKey key,bool alt=false){Focus();Host.Press(Draw,key,alt:alt);Host.Frames(Draw,2);}
        public void Step(float value)
        {
            Click(Node(UiRole.Combo,"Grid step (m)"));
            var label=value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+" m";
            Click(Node(UiRole.ListItem,label));Assert.Equal(label,Node(UiRole.Combo,"Grid step (m)").Value);
        }
        public void Dispose()=>Host.Dispose();
    }
    private static void Angle(Rig rig,float degrees)
    {
        var expected=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,degrees*MathF.PI/180);
        Assert.InRange(1-MathF.Abs(Quaternion.Dot(expected,rig.Editor.Session.Current.Find("box")!.Rotation)),0,1e-5f);
    }
    [Theory][InlineData(.1f)][InlineData(.25f)][InlineData(.5f)][InlineData(1f)]
    public void Toolbar_step_drives_arrow_moves_and_B_toggles_15_degree_rotation_with_Alt_override(float step)
    {
        using var r=new Rig();Assert.True(r.Node(UiRole.Checkbox,"Snap (B)").Checked);var snapId=r.Node(UiRole.Checkbox,"Snap (B)").Id;
        var comboId=r.Node(UiRole.Combo,"Grid step (m)").Id;r.Step(step);Assert.Equal(new Vector3(step),r.View.Grid.Size);Assert.Empty(r.Editor.Session.Journal);
        r.Key(ImGuiKey.RightArrow);Assert.InRange(MathF.Abs(r.Editor.Session.Current.Find("box")!.Position.X-step),0,1e-5f);Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo());r.Key(ImGuiKey.R);r.Key(ImGuiKey.Z);r.Key(ImGuiKey.RightArrow);Angle(r,15);Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo());var journal=r.Editor.Session.Journal.Count;r.Key(ImGuiKey.B);Assert.False(r.View.Grid.Enabled);Assert.Equal(0,r.View.RotationSnap);
        Assert.False(r.Node(UiRole.Checkbox,"Snap (B)").Checked);Assert.Equal(journal,r.Editor.Session.Journal.Count);
        r.Key(ImGuiKey.RightArrow);Angle(r,1);Assert.True(r.Editor.Session.Undo());r.Key(ImGuiKey.RightArrow,alt:true);Angle(r,15);
        Assert.True(r.Editor.Session.Undo());r.Click(r.Node(UiRole.Checkbox,"Snap (B)"));Assert.True(r.View.Grid.Enabled);Assert.InRange(r.View.RotationSnap,MathF.PI/12-1e-6f,MathF.PI/12+1e-6f);
        Assert.Equal(snapId,r.Node(UiRole.Checkbox,"Snap (B)").Id);Assert.Equal(comboId,r.Node(UiRole.Combo,"Grid step (m)").Id);
        Assert.Equal(r.Original,r.Editor.Session.Current);Assert.False(r.Editor.Session.Dirty);
    }
    [Fact]
    public void Controls_are_native_keyboard_reachable_and_disabled_during_Play_without_changing_grid_or_history()
    {
        using var r=new Rig();
        for(var i=0;!r.Node(UiRole.Checkbox,"Snap (B)").Focused&&i<10;i++)r.Host.Press(r.Draw,ImGuiKey.Tab);
        Assert.True(r.Node(UiRole.Checkbox,"Snap (B)").Focused);r.Host.Press(r.Draw,ImGuiKey.Space);r.Host.Frames(r.Draw,2);Assert.False(r.View.Grid.Enabled);
        for(var i=0;!r.Node(UiRole.Combo,"Grid step (m)").Focused&&i<10;i++)r.Host.Press(r.Draw,ImGuiKey.Tab);
        Assert.True(r.Node(UiRole.Combo,"Grid step (m)").Focused);r.Host.Press(r.Draw,ImGuiKey.Enter);r.Host.Frames(r.Draw,2);
        Assert.True(r.Node(UiRole.Combo,"Grid step (m)").Expanded);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="0.25 m");
        r.Host.Press(r.Draw,ImGuiKey.Escape);r.Host.Frames(r.Draw,2);
        if(Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS") is {} shots)
        {
            Directory.CreateDirectory(shots);r.Host.Drawing=true;r.Host.Frame(r.Draw);r.Host.Save(Path.Combine(shots,"viewport-snap.png"));r.Host.Drawing=false;
        }
        r.Editor.Add(new PlayMode<Scene>("fixture","Fixture",_=>new Preview()));Assert.True(r.Editor.Play("fixture"));var before=r.View.Grid;r.Host.Frames(r.Draw,3);
        Assert.False(r.Node(UiRole.Checkbox,"Snap (B)").Enabled);Assert.False(r.Node(UiRole.Combo,"Grid step (m)").Enabled);
        r.Host.Press(r.Draw,ImGuiKey.B);Assert.Equal(before,r.View.Grid);Assert.Empty(r.Editor.Session.Journal);Assert.Same(r.Original,r.Editor.Session.Current);
    }
    private sealed class Preview:IPlaySession{public string Status=>"Running.";public void Draw(){}public void Dispose(){}}
}
