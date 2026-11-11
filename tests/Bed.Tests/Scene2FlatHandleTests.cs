using System.Numerics;
using System.Reflection;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class Scene2FlatHandleTests
{
    internal sealed class Rig:IDisposable
    {
        internal readonly HeadlessHost Ui=new(1000,800){Drawing=false};internal readonly EditorModel<Scene2> Editor;internal bool Blocked;internal readonly object View;private readonly MethodInfo draw;
        internal Rig(Vector3? scale=null,Quaternion? rotation=null,float uiScale=1)
        {
            ImGui.GetStyle().FontScaleMain=uiScale;
            System.Collections.Immutable.ImmutableArray<Scene2Object> items=[new(SceneObject.At("parent",null,Vector3.Zero)with{Scale=scale??Vector3.One,Rotation=rotation??Quaternion.Identity},null),new(SceneObject.At("child",null,new(1,0,0))with{Parent="parent"},null)];
            Editor=new(new(new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog(),new(SceneSpace.TwoDXY,items,[],[],null)),Scene2Editing.Outline);Editor.Select("parent");
            var type=typeof(ScenePresentation).Assembly.GetType("Bed.Editor.ImGui.Scene2FlatView",true)!;View=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[Editor,(Func<string?>)(()=>Blocked?"fixture read-only":null),(Func<AssetCatalog?>)(()=>null)],null)!;draw=type.GetMethod("Draw",BindingFlags.Instance|BindingFlags.NonPublic)!;Ui.Frames(Draw,3);
        }
        internal void Draw(){ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(950,750));Bed.Editor.ImGui.Ui.Begin("Flat handles"u8);draw.Invoke(View,null);Bed.Editor.ImGui.Ui.End();}
        internal UiNode Node(string label)=>Assert.Single(Bed.Editor.ImGui.Ui.Snapshot().Nodes,n=>n.Label==label);
        internal static Vector2 Centre(UiNode n)=>new(n.Bounds.X+n.Bounds.Width/2,n.Bounds.Y+n.Bounds.Height/2);
        internal void Tool(string label){var p=Centre(Node(label));Ui.Click(Draw,p.X,p.Y);Ui.Frames(Draw,2);}
        internal void Start(Vector2 at,Vector2 end){var io=ImGui.GetIO();io.AddMousePosEvent(at.X,at.Y);Ui.Frame(Draw);io.AddMouseButtonEvent(0,true);Ui.Frame(Draw);io.AddMousePosEvent(end.X,end.Y);Ui.Frames(Draw,2);}
        internal void Release(){ImGui.GetIO().AddMouseButtonEvent(0,false);Ui.Frames(Draw,2);}
        public void Dispose()=>Ui.Dispose();
    }
    [Theory][InlineData("Move X axis",96f,-96f,2f,0f)][InlineData("Move Y axis",96f,-96f,0f,2f)]
    public void Native_axis_gestures_constrain_world_motion_and_selected_descendants_follow_once(string label,float dx,float dy,float x,float y)
    {
        using var r=new Rig();r.Editor.Select(["parent","child"]);r.Ui.Frames(r.Draw,2);var before=r.Editor.Session.Current;var at=Rig.Centre(r.Node(label));r.Start(at,at+new Vector2(dx,dy));Assert.Same(before,r.Editor.Session.Current);Assert.Empty(r.Editor.Session.Journal);r.Release();
        var resolved=Scene2Resolver.Resolve(r.Editor.Session.Current);Assert.Equal(new Vector3(x,y,0),resolved.Objects.Single(o=>o.Object.Id=="parent").Object.Position);Assert.Equal(new Vector3(1,0,0),resolved.Objects.Single(o=>o.Object.Id=="child").Object.Position);Assert.Single(r.Editor.Session.Journal);Assert.Equal(1,r.Editor.Session.History.UndoCount);Assert.True(r.Editor.Session.Undo());Assert.Same(before,r.Editor.Session.Current);
    }
    [Theory][InlineData("Scale local X axis",3f,-3f)][InlineData("Scale local Y axis",2f,-4.5f)][InlineData("Scale XY handle",3f,-4.5f)]
    public void Scale_handles_follow_the_selected_local_axes_preserve_other_components_and_reflected_signs(string label,float x,float y)
    {
        using var r=new Rig(new(2,-3,1),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/6));r.Tool("Scale");var anchor=Rig.Centre(r.Node("Flat XY"));var at=Rig.Centre(r.Node(label));r.Start(at,anchor+(at-anchor)*1.5f);Assert.Empty(r.Editor.Session.Journal);r.Release();
        var scale=r.Editor.Session.Current.Objects.Single(o=>o.Object.Id=="parent").Object.Scale;Assert.Equal(new Vector3(x,y,1),scale);Assert.Single(r.Editor.Session.Journal);
    }
    [Fact]
    public void Native_Z_ring_rotates_only_in_plane_in_one_undo_step()
    {
        using var r=new Rig();r.Tool("Rotate");var ring=r.Node("Rotate Z ring");var centre=Rig.Centre(ring);var radius=ring.Bounds.Width/2-8;var at=centre+new Vector2(radius,0);r.Start(at,centre+new Vector2(0,-radius));Assert.Empty(r.Editor.Session.Journal);r.Release();var q=r.Editor.Session.Current.Objects[0].Object.Rotation;
        Assert.Equal(0,q.X);Assert.Equal(0,q.Y);Assert.InRange(q.Z,.7070f,.7072f);Assert.InRange(q.W,.7070f,.7072f);Assert.Single(r.Editor.Session.Journal);
    }
    [Fact]
    public void Enlarged_UI_keeps_both_axes_and_ring_hit_targets_in_the_canvas_and_zero_scale_refuses_atomically()
    {
        using var r=new Rig(uiScale:2);var canvas=r.Node("Flat XY");
        foreach(var label in new[]{"Move X axis","Move Y axis"}){var n=r.Node(label);Assert.False(n.Bounds.IsEmpty);Assert.True(n.Bounds.X>=canvas.Bounds.X&&n.Bounds.Y>=canvas.Bounds.Y);}
        var at=Rig.Centre(r.Node("Move Y axis"));r.Start(at,at+new Vector2(50,-64));r.Release();Assert.Equal(new Vector3(0,1,0),r.Editor.Session.Current.Objects[0].Object.Position);Assert.True(r.Editor.Session.Undo());
        r.Tool("Rotate");Assert.False(r.Node("Rotate Z ring").Bounds.IsEmpty);r.Tool("Scale");var anchor=Rig.Centre(r.Node("Flat XY"));at=Rig.Centre(r.Node("Scale local X axis"));var before=r.Editor.Session.Current;var journal=r.Editor.Session.Journal.Count;r.Start(at,anchor);r.Release();Assert.Same(before,r.Editor.Session.Current);Assert.Equal(journal,r.Editor.Session.Journal.Count);
    }
    [Theory][InlineData("escape")][InlineData("selection")][InlineData("refusal")]
    public void Cancelled_or_invalidated_axis_capture_never_commits_a_transform(string mode)
    {
        using var r=new Rig();var before=r.Editor.Session.Current;var at=Rig.Centre(r.Node("Move X axis"));r.Start(at,at+new Vector2(100,20));
        if(mode=="escape")r.Ui.Press(r.Draw,ImGuiKey.Escape);else if(mode=="selection")r.Editor.Select("child");else r.Blocked=true;
        r.Ui.Frame(r.Draw);r.Release();Assert.Same(before,r.Editor.Session.Current);Assert.Empty(r.Editor.Session.Journal);
    }
}
