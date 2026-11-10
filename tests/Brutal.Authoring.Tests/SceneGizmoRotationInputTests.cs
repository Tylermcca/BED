using System.Numerics;
using Brutal.Authoring.Editor;
using Xunit;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneGizmoRotationInputTests
{
    [Theory]
    [InlineData(1f,4f)] [InlineData(1f,40f)] [InlineData(2f,4f)] [InlineData(2f,40f)]
    public void Ring_screen_radius_and_hit_tolerance_do_not_shrink_with_zoom(float scale,float distance)
    {
        var camera=Camera.Default with { Distance=distance };
        var rings=RotationRings.Layout(camera,Vector3.Zero,new(1100,760),Quaternion.Identity,scale);
        Assert.Equal(3,rings.Count);
        foreach(var ring in rings)
        {
            var outer=ring.Points.MaxBy(point=>Vector2.DistanceSquared(point,ring.Centre));
            Assert.InRange(MathF.Abs(Vector2.Distance(outer,ring.Centre)-76*scale),0,.5f);
            Assert.True(ring.Hit(outer)); Assert.False(ring.Hit(ring.Centre));
            Assert.False(ring.Hit(ring.Centre+(outer-ring.Centre)*1.5f));
        }
    }

    [Theory]
    [InlineData(.25f)] [InlineData(-.75f)] [InlineData(3f)]
    public void Plane_rays_return_the_signed_angle_in_the_explicit_captured_basis(float angle)
    {
        var camera=Camera.Default; var basis=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f);
        var ring=RotationRings.Layout(camera,new(1,2,0),new(1100,760),basis,1).Single(r=>r.Axis==MoveHandleKind.Z);
        var point=ring.Pivot+ring.Radius*(ring.First*MathF.Cos(angle)+ring.Second*MathF.Sin(angle));
        var ray=new Ray(camera.Eye,Vector3.Normalize(point-camera.Eye));
        Assert.True(ring.TryAngle(ray,out var actual)); Assert.InRange(MathF.Abs(actual-angle),0,1e-4f);
        Assert.False(ring.TryAngle(new(ring.Pivot+ring.Normal,ring.Normal),out _));
        Assert.False(ring.TryAngle(new(ring.Pivot+ring.Normal,ring.First),out _));
    }

    [Fact]
    public void Signed_drag_unwraps_the_seam_and_a_complete_turn_without_changing_any_document()
    {
        var camera=Camera.Default;
        var ring=RotationRings.Layout(camera,Vector3.Zero,new(1100,760),Quaternion.Identity,1).Single(r=>r.Axis==MoveHandleKind.Z);
        Ray At(float a)
        {
            var point=ring.Pivot+ring.Radius*(ring.First*MathF.Cos(a)+ring.Second*MathF.Sin(a));
            return new(camera.Eye,Vector3.Normalize(point-camera.Eye));
        }
        var drag=RotationRingDrag.Begin(ring,At(0)); Assert.NotNull(drag);
        foreach(var angle in new[] { MathF.PI/2,MathF.PI,-MathF.PI/2,0 }) Assert.True(drag.Update(At(angle)));
        Assert.InRange(MathF.Abs(drag.Radians-MathF.Tau),0,1e-4f);
        var reverse=RotationRingDrag.Begin(ring,At(-2.9f)); Assert.NotNull(reverse); Assert.True(reverse.Update(At(2.9f)));
        Assert.InRange(MathF.Abs(reverse.Radians-(5.8f-MathF.Tau)),0,1e-4f);
        var before=drag.Radians; Assert.False(drag.Update(new(ring.Pivot+ring.Normal,ring.First))); Assert.Equal(before,drag.Radians);
    }

    [Fact]
    public void Edge_on_or_invalid_rings_do_not_create_phantom_input_targets()
    {
        var camera=Camera.Default with { Yaw=0,Pitch=0 };
        var rings=RotationRings.Layout(camera,Vector3.Zero,new(1100,760),Quaternion.Identity,1);
        Assert.Single(rings); Assert.Equal(MoveHandleKind.Z,rings[0].Axis);
        Assert.Empty(RotationRings.Layout(camera,camera.Eye-camera.Forward,new(1100,760),Quaternion.Identity,1));
        Assert.Empty(RotationRings.Layout(camera,Vector3.Zero,new(1100,760),new(0,0,0,2),1));
        Assert.Empty(RotationRings.Layout(camera,Vector3.Zero,new(1100,760),Quaternion.Identity,float.NaN));
    }
    private sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host=new(1200,850) { Drawing=false };
        public readonly Scene Original;
        public readonly EditorModel<Scene> Editor;
        public readonly ViewportView<Scene> View;
        public Rig(float scale=1,bool moveOnly=false,Scene? scene=null)
        {
            Original=scene ?? new Scene([SceneObject.At("root",null,Vector3.Zero),
                SceneObject.At("child",null,new(2,0,0)) with { Parent="root" }]);
            Editor=new(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Original),SceneEditing.Outline);
            Editor.Select("root");
            View=new(Editor,moveOnly ? new MoveOnlySource() : new SceneViewport());
            EditorWindow<Scene>.Configure(ImGui.GetIO()); EditorWindow<Scene>.SetTextScale(scale);
            Host.Frames(Draw,4);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0)); ImGui.SetNextWindowSize(new float2(1200,850));
            ImGui.Begin("Rotation test"u8,ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
            View.Draw(); ImGui.End();
        }
        public void Focus()
        {
            for(var i=0;i<8;i++)
            {
                Host.Press(Draw,ImGuiKey.Tab);
                if(Ui.Snapshot().Nodes.Any(node=>node.Role==UiRole.Canvas && node.Focused)) return;
            }
            Assert.Fail("Tab must reach the actual viewport through its real controls.");
        }
        public void RotateMode()
        {
            Focus(); Host.Press(Draw,ImGuiKey.R); Host.Frames(Draw,2);
            Assert.Equal(ViewportTool.Rotate,View.Tool);
        }
        public (float2 From,float2 To) QuarterTurn()
        {
            var ring=View.RotationTargets.Single(r=>r.Axis==MoveHandleKind.Z);
            var others=View.RotationTargets.Where(r=>r.Axis!=ring.Axis).ToArray();
            var index=Enumerable.Range(0,ring.Points.Count).First(i=>ring.Hit(ring.Points[i]) && !others.Any(r=>r.Hit(ring.Points[i])));
            var angle=MathF.Tau*index/ring.Points.Count+MathF.PI/2;
            var from=ring.Points[index]+View.LastRect.Origin;
            var to=View.ScreenOf(ring.Pivot+ring.Radius*(ring.First*MathF.Cos(angle)+ring.Second*MathF.Sin(angle)))!.Value;
            return(new(from.X,from.Y),new(to.X,to.Y));
        }
        public void Dispose()=>Host.Dispose();
    }
    private sealed class MoveOnlySource : IViewportSource<Scene>
    {
        public IEnumerable<ViewportItem> Items(Scene scene)=>new SceneViewport().Items(scene);
        public ICommand<Scene>? MoveTo(Scene scene,string id,Vector3 target)=>new SceneViewport().MoveTo(scene,id,target);
    }
    private static void Near(Vector3 a,Vector3 b)=>Assert.InRange(Vector3.Distance(a,b),0,1e-4f);

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Handle_real_ring_preview_then_release_rotates_once_and_undo_restores_everything(float scale)
    {
        using var r=new Rig(scale); r.RotateMode(); var (from,to)=r.QuarterTurn();
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status && n.Label.StartsWith("Rotate Z:"));
        ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        Near(new(0,2,0),SceneViewport.World(r.Editor.Session.Current,"child").Translation);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child"));
        Assert.Single(r.Editor.Session.Journal); Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo()); Assert.Equal(r.Original,r.Editor.Session.Current);
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Handle_keyboard_turns_filtered_roots_and_keeps_orbit_and_precision_override(float scale)
    {
        using var r=new Rig(scale); r.Editor.Select("root","child"); r.RotateMode();
        r.Host.Press(r.Draw,ImGuiKey.Z); r.Host.Press(r.Draw,ImGuiKey.RightArrow); r.Host.Frames(r.Draw,2);
        var q=r.Editor.Session.Current.Find("root")!.Rotation; var direction=Vector3.Transform(Vector3.UnitX,q);
        Assert.InRange(MathF.Abs(MathF.Atan2(direction.Y,direction.X)-MathF.PI/12),0,1e-4f);
        Near(new(2,0,0),SceneViewport.World(r.Editor.Session.Current,"child").Translation);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child")); Assert.Single(r.Editor.Session.Journal);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status && n.Label.StartsWith("Rotate Z:"));
        var camera=r.View.Camera; r.Host.Press(r.Draw,ImGuiKey.RightArrow,ctrl:true);
        Assert.NotEqual(camera,r.View.Camera); Assert.Single(r.Editor.Session.Journal);
        r.Host.Press(r.Draw,ImGuiKey.RightArrow,alt:true);
        direction=Vector3.Transform(Vector3.UnitX,r.Editor.Session.Current.Find("root")!.Rotation);
        Assert.InRange(MathF.Abs(MathF.Atan2(direction.Y,direction.X)-16*MathF.PI/180),0,1e-4f);
        Assert.Equal(2,r.Editor.Session.History.UndoCount);
    }

    [Fact]
    public void Handle_toolbar_is_real_and_returns_keyboard_focus_to_the_canvas()
    {
        using var r=new Rig();
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Rotate (R)");
        var b=button.Bounds; r.Host.Click(r.Draw,b.X+b.Width/2,b.Y+b.Height/2); r.Host.Frames(r.Draw,2);
        Assert.Equal(ViewportTool.Rotate,r.View.Tool);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas && n.Focused);
    }

    [Fact]
    public void Handle_Escape_cancels_real_rotation_and_the_same_live_drag_is_a_positive_control()
    {
        using var r=new Rig(); r.RotateMode(); var (from,to)=r.QuarterTurn();
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        r.Host.Press(r.Draw,ImGuiKey.Escape); ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal); Assert.False(r.Editor.Session.Dirty);
        (from,to)=r.QuarterTurn(); r.Host.Drag(r.Draw,from,to);
        Near(new(0,2,0),SceneViewport.World(r.Editor.Session.Current,"child").Translation); Assert.Single(r.Editor.Session.Journal);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Handle_revision_or_camera_change_cannot_commit_a_stale_rotation(bool edit)
    {
        using var r=new Rig(); r.RotateMode(); var (from,to)=r.QuarterTurn();
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        if(edit) Assert.True(r.Editor.Execute(new MoveObject("root",Vector3.UnitX),"root"));
        else r.View.Camera=r.View.Camera.Orbit(.1f,0);
        r.Host.Frames(r.Draw,1); ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        Assert.Equal(Quaternion.Identity,r.Editor.Session.Current.Find("root")!.Rotation);
        Assert.Equal(edit?1:0,r.Editor.Session.Journal.Count);
    }

    [Fact]
    public void Handle_move_only_sources_keep_rotation_hidden_and_explain_disabled_tool()
    {
        using var r=new Rig(moveOnly:true); r.Focus(); r.Host.Press(r.Draw,ImGuiKey.R); r.Host.Frames(r.Draw,2);
        Assert.Equal(ViewportTool.Move,r.View.Tool); Assert.Empty(r.View.RotationTargets); Assert.Empty(r.Editor.Session.Journal);
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Rotate (R)");
        Assert.False(button.Enabled); Assert.Contains("moving",button.DisabledReason);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Rotate X");
    }

    [Fact]
    public void Handle_shear_refusal_never_commits_a_stale_valid_plan()
    {
        var scene=new Scene([SceneObject.At("parent",null,Vector3.Zero) with { Scale=new(2,1,1) },
            SceneObject.At("root",null,Vector3.UnitX) with { Parent="parent" }]);
        using var r=new Rig(scene:scene); r.View.RotationSnap=MathF.PI/4; r.RotateMode();
        r.Host.Press(r.Draw,ImGuiKey.Z); r.Host.Press(r.Draw,ImGuiKey.RightArrow); r.Host.Frames(r.Draw,2);
        Assert.Same(scene,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        Assert.Contains("shear",r.View.Said);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status && n.Label.Contains("shear"));
    }

    [Fact]
    public void Handle_source_planner_fault_is_contained_without_preview_or_history()
    {
        var scene=new Scene([SceneObject.At("root",null,Vector3.Zero)]);
        var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
        editor.Select("root"); using var capture=ViewportTransformGesture<Scene>.Begin(editor,new SceneViewport()); Assert.NotNull(capture);
        Assert.False(capture.Preview((_,_)=>throw new InvalidOperationException("Invented planner fault")));
        Assert.Contains("Invented planner fault",capture.Reason); Assert.Same(scene,capture.PreviewDocument);
        Assert.False(capture.Commit()); Assert.Same(scene,editor.Session.Current); Assert.Empty(editor.Session.Journal);
    }

    [Fact]
    public void Handle_Play_cancels_capture_and_disables_tool_input_without_a_release_commit()
    {
        using var r=new Rig(); r.RotateMode(); var (from,to)=r.QuarterTurn();
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        r.Editor.Add(new PlayMode<Scene>("synthetic","Synthetic",_=>new SyntheticPlay()));
        Assert.True(r.Editor.Play("synthetic")); r.Host.Frames(r.Draw,2);
        Assert.False(r.View.SetTool(ViewportTool.Move)); Assert.Empty(r.View.RotationTargets);
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Rotate (R)");
        Assert.False(button.Enabled);
        ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2); Assert.True(r.Editor.Stop());
        r.Host.Frames(r.Draw,2); Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
    }
    private sealed class SyntheticPlay : IPlaySession
    {
        public string Status=>"Synthetic lifecycle fixture";
        public void Draw() { }
        public void Dispose() { }
    }

}
