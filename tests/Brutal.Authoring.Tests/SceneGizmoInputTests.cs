using System.Numerics;
using Brutal.Authoring.Editor;
using Xunit;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneGizmoInputTests
{
    private static void Near(Vector3 a, Vector3 b) => Assert.InRange(Vector3.Distance(a,b),0,1e-4f);

    [Theory]
    [InlineData(1f,4f)] [InlineData(1f,40f)] [InlineData(2f,4f)] [InlineData(2f,40f)]
    public void Axis_hit_regions_keep_their_screen_size_at_zoom_and_ui_scale(float scale,float distance)
    {
        var camera=Camera.Default with { Distance=distance }; var size=new Vector2(1000,700);
        var centre=camera.Project(Vector3.Zero,size)!.Value.Screen;
        var handles=MoveHandles.Layout(camera,Vector3.Zero,size,Quaternion.Identity,scale);
        Assert.Equal(3,handles.Count(h=>!h.IsPlane));
        foreach(var handle in handles.Where(h=>!h.IsPlane))
        {
            Assert.InRange(MathF.Abs(Vector2.Distance(handle.Start,handle.End)-68*scale),0,.0001f);
            Assert.InRange(MathF.Abs(Vector2.Distance(centre,handle.End)-88*scale),0,.0001f);
            var middle=(handle.Start+handle.End)/2;
            Assert.True(handle.Hit(middle,8*scale)); Assert.False(handle.Hit(centre,8*scale));
            var direction=Vector2.Normalize(handle.End-handle.Start); var side=new Vector2(-direction.Y,direction.X);
            Assert.False(handle.Hit(middle+side*12*scale,8*scale)); Assert.False(handle.Hit(new(float.NaN,0),8*scale));
        }
        foreach(var handle in handles.Where(h=>h.IsPlane))
        {
            var middle=handle.Corners.Aggregate(Vector2.Zero,(a,b)=>a+b)/4;
            Assert.True(handle.Hit(middle,8*scale)); Assert.False(handle.Hit(centre,8*scale));
        }
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Axis_and_plane_constraints_project_real_rays_without_cross_axis_motion(float scale)
    {
        var camera=Camera.Default; var pivot=new Vector3(1,2,3);
        var handles=MoveHandles.Layout(camera,pivot,new(1000,700),Quaternion.Identity,scale);
        Ray To(Vector3 point)=>new(camera.Eye,Vector3.Normalize(point-camera.Eye));
        var x=handles.Single(h=>h.Kind==MoveHandleKind.X);
        Assert.True(x.TryDelta(pivot,To(pivot),To(pivot+new Vector3(2,0,0)),out var axis));
        Near(new(2,0,0),axis);
        var xy=handles.Single(h=>h.Kind==MoveHandleKind.XY);
        Assert.True(xy.TryDelta(pivot,To(pivot),To(pivot+new Vector3(1,2,0)),out var plane));
        Near(new(1,2,0),plane);
        Assert.False(x.TryDelta(pivot,new(Vector3.Zero,Vector3.UnitX),To(pivot),out _));
        Assert.False(xy.TryDelta(pivot,new(new(0,0,4),Vector3.UnitZ),To(pivot),out _));
    }

    [Fact]
    public void Delta_snapping_preserves_spacing_and_ignores_the_positional_grid_origin()
    {
        var grid=Grid.Uniform(.5f,new(.25f)); var basis=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2);
        var world=Vector3.Transform(new Vector3(1.26f,0,0),basis);
        Assert.True(MoveHandles.TrySnapDelta(MoveHandleKind.X,world,basis,grid,false,out var snapped));
        Near(Vector3.Transform(new Vector3(1.5f,0,0),basis),snapped);
        Assert.True(MoveHandles.TrySnapDelta(MoveHandleKind.X,world,basis,grid,true,out var free)); Near(world,free);
        Assert.True(MoveHandles.TrySnapDelta(MoveHandleKind.XY,Vector3.Zero,basis,grid,false,out var noop));
        Assert.Equal(Vector3.Zero,noop);
        Assert.False(MoveHandles.TrySnapDelta(MoveHandleKind.X,world,new(0,0,0,2),grid,false,out _));
    }

    [Fact]
    public void Behind_camera_and_degenerate_axis_projections_do_not_create_phantom_targets()
    {
        var camera=Camera.Default; var size=new Vector2(1000,700);
        Assert.Empty(MoveHandles.Layout(camera,camera.Eye-camera.Forward,size,Quaternion.Identity,1));
        Assert.Empty(MoveHandles.Layout(camera,Vector3.Zero,size,new(0,0,0,2),1));
        Assert.Empty(MoveHandles.Layout(camera,Vector3.Zero,size,Quaternion.Identity,float.NaN));
        var straight=camera with { Yaw=0,Pitch=0 };
        var handles=MoveHandles.Layout(straight,Vector3.Zero,size,Quaternion.Identity,1);
        Assert.DoesNotContain(handles,h=>h.Kind==MoveHandleKind.Z);
        Assert.Contains(handles,h=>h.Kind==MoveHandleKind.X);
        Assert.Contains(handles,h=>h.Kind==MoveHandleKind.Y);
    }
    private sealed class Rig : IDisposable
    {
        public readonly Scene Original = new([
            SceneObject.At("parent",null,Vector3.Zero),
            SceneObject.At("child",null,new(2,0,0)) with { Parent="parent" }]);
        public readonly EditorModel<Scene> Editor;
        public readonly ViewportView<Scene> View;
        public readonly HeadlessHost Host = new(1100,760) { Drawing=false };
        public Rig(float scale)
        {
            Editor = new(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Original),SceneEditing.Outline);
            Editor.Select("parent","child");
            View = new(Editor,new SceneViewport()) { Grid=Grid.Uniform(.5f) };
            EditorWindow<Scene>.Configure(ImGui.GetIO()); EditorWindow<Scene>.SetTextScale(scale);
            Host.Frames(Draw,4);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0)); ImGui.SetNextWindowSize(new float2(1100,760));
            ImGui.Begin("Move handles test"u8,ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
            View.Draw(); ImGui.End();
        }
        public void FocusCanvas()
        {
            for(var i=0;i<4;i++)
            {
                Host.Press(Draw,ImGuiKey.Tab);
                if(Ui.Snapshot().Nodes.Any(node=>node.Role==UiRole.Canvas && node.Focused)) return;
            }
            Assert.Fail("Tab must reach the viewport through its real toolbar controls.");
        }
        public (float2 From,float2 To) AxisDrag()
        {
            var handle=View.MoveTargets.Single(target=>target.Kind==MoveHandleKind.X);
            var a=(handle.Start+handle.End)/2; var b=a+Vector2.Normalize(handle.End-handle.Start)*75*EditorWindow<Scene>.TextScale;
            a+=View.LastRect.Origin; b+=View.LastRect.Origin;
            return(new(a.X,a.Y),new(b.X,b.Y));
        }
        public void Dispose()=>Host.Dispose();
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Handle_pointer_moves_selected_roots_once_and_release_is_one_undo(float scale)
    {
        using var r=new Rig(scale); var (from,to)=r.AxisDrag();
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        Assert.Contains(Ui.Snapshot().Nodes,node=>node.Role==UiRole.Status && node.Label.StartsWith("Move X:"));
        ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        var moved=r.Editor.Session.Current.Find("parent")!.Position;
        Assert.True(moved.X>0); Assert.Equal(0,moved.Y); Assert.Equal(0,moved.Z);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child"));
        Near(new Vector3(2,0,0)+moved,SceneViewport.World(r.Editor.Session.Current,"child").Translation);
        Assert.Single(r.Editor.Session.Journal); Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo()); Assert.Equal(r.Original,r.Editor.Session.Current);
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Handle_keyboard_constraint_has_visible_semantics_grid_override_and_ctrl_orbit(float scale)
    {
        using var r=new Rig(scale); r.View.Grid=Grid.Uniform(1);
        r.FocusCanvas(); r.Host.Press(r.Draw,ImGuiKey.Y);
        r.Host.Press(r.Draw,ImGuiKey.RightArrow); r.Host.Frames(r.Draw,2);
        Near(Vector3.UnitY,r.Editor.Session.Current.Find("parent")!.Position);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child"));
        Assert.Contains(Ui.Snapshot().Nodes,node=>node.Role==UiRole.Status && node.Label.StartsWith("Move Y:"));
        var camera=r.View.Camera; var count=r.Editor.Session.Journal.Count;
        r.Host.Press(r.Draw,ImGuiKey.RightArrow,ctrl:true);
        Assert.NotEqual(camera,r.View.Camera); Assert.Equal(count,r.Editor.Session.Journal.Count);
        r.Host.Press(r.Draw,ImGuiKey.RightArrow,alt:true);
        Near(new(0,1.5f,0),r.Editor.Session.Current.Find("parent")!.Position);
        r.Host.Press(r.Draw,ImGuiKey.Z,ctrl:true); r.Host.Frames(r.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,node=>node.Role==UiRole.Status && node.Label.StartsWith("Move Y:"));
        Assert.Equal(2,r.Editor.Session.History.UndoCount);
    }

    [Fact]
    public void Handle_Escape_cancels_a_real_drag_and_the_same_drag_without_Escape_still_commits()
    {
        using var r=new Rig(1); var (from,to)=r.AxisDrag();
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        r.Host.Press(r.Draw,ImGuiKey.Escape); ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal); Assert.False(r.Editor.Session.Dirty);
        (from,to)=r.AxisDrag(); r.Host.Drag(r.Draw,from,to);
        Assert.True(r.Editor.Session.Current.Find("parent")!.Position.X>0); Assert.Single(r.Editor.Session.Journal);
    }

    [Fact]
    public void Handle_capture_owns_wheel_and_right_drag_until_release()
    {
        using var r=new Rig(1); var (from,to)=r.AxisDrag(); var camera=r.View.Camera;
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        ImGui.GetIO().AddMouseButtonEvent(1,true); ImGui.GetIO().AddMouseWheelEvent(0,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        Assert.Equal(camera,r.View.Camera); Assert.Same(r.Original,r.Editor.Session.Current);
        ImGui.GetIO().AddMouseButtonEvent(1,false); ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        Assert.Equal(camera,r.View.Camera); Assert.Single(r.Editor.Session.Journal);
    }

    [Fact]
    public void Handle_group_commit_refuses_all_when_one_existing_source_callback_cannot_move()
    {
        var scene=new Scene([SceneObject.At("first",null,Vector3.Zero),SceneObject.At("second",null,Vector3.UnitX)]);
        var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
        editor.Select("first","second"); var source=new RefusingSource();
        using var captured=ViewportGesture<Scene>.BeginSelection(editor,source);
        Assert.NotNull(captured); Assert.True(captured.Preview(captured.Start+Vector3.UnitY,source));
        Assert.False(captured.Commit(source)); Assert.Same(scene,editor.Session.Current); Assert.Empty(editor.Session.Journal);
        Assert.False(captured.Active);
    }
    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Handle_extra_plane_drag_constrains_two_axes_and_is_one_atomic_move(float scale)
    {
        using var r=new Rig(scale);
        var handle=r.View.MoveTargets.Single(target=>target.Kind==MoveHandleKind.XY);
        var start=handle.Corners.Aggregate(Vector2.Zero,(a,b)=>a+b)/4+r.View.LastRect.Origin;
        var end=r.View.ScreenOf(SceneViewport.World(r.Editor.Session.Current,"child").Translation+new Vector3(3,3,0))!.Value;
        r.Host.MoveMouse(r.Draw,start.X,start.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,end.X,end.Y); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current);
        Assert.Contains(Ui.Snapshot().Nodes,node=>node.Role==UiRole.Status && node.Label.StartsWith("Move XY:"));
        ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        var moved=r.Editor.Session.Current.Find("parent")!.Position;
        Assert.True(moved.X>0,$"Actual plane move: {moved}"); Assert.True(moved.Y>0,$"Actual plane move: {moved}"); Assert.Equal(0,moved.Z);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child"));
        Assert.Single(r.Editor.Session.Journal); Assert.Equal(1,r.Editor.Session.History.UndoCount);
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Handle_extra_local_basis_is_explicit_and_translation_never_rotates_the_objects(float scale)
    {
        using var r=new Rig(scale);
        r.View.MoveBasis=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2); r.Host.Frames(r.Draw,2);
        var (from,to)=r.AxisDrag(); r.Host.Drag(r.Draw,from,to);
        var moved=r.Editor.Session.Current.Find("parent")!;
        Assert.True(moved.Position.Z<0); Near(new(0,0,moved.Position.Z),moved.Position);
        Assert.Equal(Quaternion.Identity,moved.Rotation); Assert.Equal(Vector3.One,moved.Scale);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child")); Assert.Single(r.Editor.Session.Journal);
    }

    [Fact]
    public void Handle_extra_changing_captured_basis_cancels_without_a_release_command()
    {
        using var r=new Rig(1); var (from,to)=r.AxisDrag();
        r.Host.MoveMouse(r.Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); r.Host.Frames(r.Draw,2);
        r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        r.View.MoveBasis=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f); r.Host.Frames(r.Draw,1);
        ImGui.GetIO().AddMouseButtonEvent(0,false); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal); Assert.False(r.Editor.Session.Dirty);
    }

    private sealed class RefusingSource : IViewportSource<Scene>
    {
        public IEnumerable<ViewportItem> Items(Scene scene)=>new SceneViewport().Items(scene);
        public ICommand<Scene>? MoveTo(Scene scene,string id,Vector3 world)=>id=="second" ? null : new SceneViewport().MoveTo(scene,id,world);
    }

}
