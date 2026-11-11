using System.Numerics;
using Bed;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ViewportSelectionUi23Tests
{
    private static MeshData DensePlane()
    {
        const int n=16;
        var positions=new List<Vector3>();var indices=new List<int>();
        for(var y=0;y<=n;y++)for(var x=0;x<=n;x++)positions.Add(new(x/(float)n-0.5f,y/(float)n-0.5f,0));
        for(var y=0;y<n;y++)for(var x=0;x<n;x++)
        {var a=y*(n+1)+x;indices.AddRange([a,a+1,a+n+2,a,a+n+2,a+n+1]);}
        return new(positions.ToArray(),indices.ToArray());
    }
    private static List<Vector2> YellowVertices()
    {
        var result=new List<Vector2>();var data=Im.GetDrawData();
        for(var l=0;l<data.CmdListsCount;l++)
            for(var v=0;v<data.CmdLists[l].VtxBuffer.Count;v++)
            {
                var vertex=data.CmdLists[l].VtxBuffer[v];
                if(vertex.col==0xff3cc8ff)result.Add(new(vertex.pos.X,vertex.pos.Y));
            }
        return result;
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Dense_selection_draws_one_outer_bound_without_internal_triangle_edges_on_CPU_or_GPU(bool gpu)
    {
        var mesh=DensePlane();
        var scene=new Scene([SceneObject.At("dense","dense.obj",new(.25f,.1f,0)) with {Scale=new(2,1.5f,1),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.2f)}]);
        var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
        editor.Select("dense");
        using var host=new HeadlessHost(1000,800){Drawing=false};EditorWindow<Scene>.Configure(Im.GetIO());
        var view=new ViewportView<Scene>(editor,new SceneViewport(_=>mesh)){Camera=new(Vector3.Zero,0,0,6)};
        using var presenter=gpu?ScenePresentation.Bind((_,request,_)=>new(request,new(){_TexID=new ImTextureID((nint)0x123456)},()=>true)):null;
        void Draw(){Im.SetNextWindowPos(new(10,10));Im.SetNextWindowSize(new(950,750));Ui.Begin("Selection bounds"u8);view.Draw();Ui.End();}
        host.Frames(Draw,4);
        Assert.Equal(512,view.RenderRequest!.Triangles().Count);
        var snapshot=Ui.Snapshot();var item=Assert.Single(snapshot.Nodes,n=>n.Label=="dense"&&n.Parent>=0&&snapshot.Nodes[n.Parent].Role==UiRole.Canvas);
        var yellow=YellowVertices();Assert.InRange(yellow.Count,8,32);
        var bounds=item.Bounds;Assert.False(bounds.IsEmpty);
        Assert.All(yellow,p=>Assert.True(p.X<bounds.X-1||p.X>bounds.X+bounds.Width+1||p.Y<bounds.Y-1||p.Y>bounds.Y+bounds.Height+1,$"Selection line covers mesh interior at {p}."));
        foreach(var position in mesh.Positions)
        {
            var world=Vector3.Transform(position,SceneViewport.World(scene,"dense"));
            var point=view.Camera.Project(world,view.LastRect.Size)!.Value.Screen+view.LastRect.Origin;
            Assert.InRange(point.X,bounds.X-.01f,bounds.X+bounds.Width+.01f);
            Assert.InRange(point.Y,bounds.Y-.01f,bounds.Y+bounds.Height+.01f);
        }
        editor.Select();host.Frames(Draw,2);Assert.Empty(YellowVertices());
        Assert.False(editor.Session.Dirty);Assert.Empty(editor.Session.Journal);
    }
    private sealed class ReadOnlySource : IViewportSource<Scene>
    {
        public IEnumerable<ViewportItem> Items(Scene scene)=>[new("preview",Matrix4x4.Identity,MeshData.Cube,0xff808080)];
        public ICommand<Scene>? MoveTo(Scene scene,string id,Vector3 position)=>null;
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Readonly_transform_reason_disables_all_tools_and_keeps_navigation_and_snap_available(bool moveOnly)
    {
        const string reason="Generated geometry is read-only; edit its source instead.";
        var scene=new Scene([SceneObject.At("preview",null,Vector3.Zero)]);
        var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);editor.Select("preview");
        using var host=new HeadlessHost(1000,800){Drawing=false};EditorWindow<Scene>.Configure(Im.GetIO());
        var view=new ViewportView<Scene>(editor,moveOnly?new ReadOnlySource():new SceneViewport());
        Assert.Null(view.TransformRefusal);
        view.TransformRefusal=()=>reason;
        void Draw(){Im.SetNextWindowPos(new(10,10));Im.SetNextWindowSize(new(950,750));Ui.Begin("Readonly preview"u8);view.Draw();Ui.End();}
        host.Frames(Draw,4);
        foreach(var label in new[]{"Move (G)","Rotate (R)","Scale (S)"})
        {
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);
            Assert.False(node.Enabled);Assert.Equal(reason,node.DisabledReason);Assert.Equal(reason,node.Tooltip);Assert.Equal(UiActions.None,node.Actions);
            host.Click(Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);
        }
        Assert.Empty(view.MoveTargets);Assert.Empty(view.RotationTargets);Assert.Empty(view.ScaleTargets);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label==reason);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Snap (B)").Enabled);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Grid step (m)").Enabled);
        for(var i=0;i<10&&!Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Canvas&&n.Focused);i++)host.Press(Draw,ImGuiKey.Tab);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas&&n.Focused);
        foreach(var key in new[]{ImGuiKey.R,ImGuiKey.S,ImGuiKey.G,ImGuiKey.RightArrow})host.Press(Draw,key);
        Assert.Equal(ViewportTool.Move,view.Tool);Assert.Equal(reason,view.Said);
        foreach(var tool in new[]{ViewportTool.Move,ViewportTool.Rotate,ViewportTool.Scale}){Assert.False(view.SetTool(tool));Assert.Equal(reason,view.Said);}
        var camera=view.Camera;host.Press(Draw,ImGuiKey.RightArrow,ctrl:true);Assert.NotEqual(camera,view.Camera);
        Assert.Same(scene,editor.Session.Current);Assert.False(editor.Session.Dirty);Assert.Empty(editor.Session.Journal);
    }

}
