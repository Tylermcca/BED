using System.Numerics;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneFrameCachePerf20Tests
{
    private sealed class Source:IViewportSource<Scene>
    {
        public MeshData Mesh=new([new(-1,0,0),new(1,0,0),new(0,2,0)],[0,1,2]);
        public Matrix4x4 World=Matrix4x4.Identity;public uint Colour=0xff7090b0;
        public IEnumerable<ViewportItem> Items(Scene scene)=>[new("one",World,Mesh,Colour),new("two",Matrix4x4.CreateTranslation(3,0,0),Mesh,0xff4070a0)];
        public ICommand<Scene>? MoveTo(Scene scene,string id,Vector3 world)=>null;
    }
    [Theory]
    [InlineData("positions")][InlineData("indices")][InlineData("material")][InlineData("colour")]
    [InlineData("world")][InlineData("camera")][InlineData("extent")][InlineData("selection")][InlineData("invalidate")]
    public void Cached_CPU_pixels_and_semantic_bounds_match_a_fresh_view_after_input_changes(string change)
    {
        using var host=new HeadlessHost(640,480){Drawing=true};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new Scene([SceneObject.At("one",null,Vector3.Zero),SceneObject.At("two",null,new(3,0,0))])),SceneEditing.Outline);
        var source=new Source();var view=new ViewportView<Scene>(editor,source);var width=620f;
        void Draw()
        {ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(width,460));Ui.Begin("Frame cache fixture"u8);view.Draw();Ui.End();}
        host.Frames(Draw,4);var old=view.RenderRequest!;var oldPoint=old.Items[0].Positions[2];
        switch(change)
        {
            case "positions":source.Mesh.Positions[2]=new(0,4,0);break;
            case "indices":source.Mesh.Indices[2]=1;break;
            case "material":source.Mesh=source.Mesh with{Material=new SceneMaterial(new(.9f,.1f,.2f,1))};break;
            case "colour":source.Colour=0xff00ff00;break;
            case "world":source.World=Matrix4x4.CreateTranslation(1,2,0);break;
            case "camera":view.Camera=view.Camera.Orbit(.2f,0);break;
            case "extent":width=540;break;
            case "selection":editor.Select("one");break;
            case "invalidate":view.InvalidateRenderRequest();Assert.Null(view.RenderRequest);break;
        }
        host.Frames(Draw,2);Assert.Equal(oldPoint,old.Items[0].Positions[2]);
        var cached=host.Pixels.ToArray();var bounds=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.ListItem).Select(n=>(n.Label,n.Bounds,n.Selected)).ToArray();
        view=new(editor,source){Camera=view.Camera};host.Frames(Draw,2);
        Assert.Equal(cached,host.Pixels);Assert.Equal(bounds,Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.ListItem).Select(n=>(n.Label,n.Bounds,n.Selected)).ToArray());
        Assert.Empty(editor.Session.Journal);Assert.False(editor.Session.Dirty);
    }
    [Fact]
    public void Shared_mesh_capture_is_owned_per_frame_and_default_colours_remain_per_item()
    {
        var mesh=new MeshData([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]);
        var identity=new SceneRenderIdentity(0,0,0,0,0,Vector2.Zero,new(600,400),Vector2.One);
        ViewportItem Item(string id,uint colour)=>new(id,Matrix4x4.Identity,mesh,colour);
        var request=SceneRenderRequest.Capture(identity,Camera.Default,[Item("a",0xff0000ff),Item("b",0xff0000ff),Item("c",0xff00ff00)]);
        Assert.Same(request.Items[0].Payload,request.Items[1].Payload);Assert.NotSame(request.Items[0].Payload,request.Items[2].Payload);
        Assert.NotEqual(request.Items[0].Payload.Material.BaseColor,request.Items[2].Payload.Material.BaseColor);
        mesh.Positions[0]=new(5,6,7);Assert.Equal(Vector3.Zero,request.Items[0].Positions[0]);
        var fresh=SceneRenderRequest.Capture(identity,Camera.Default,[Item("a",0xff0000ff)]);Assert.Equal(new Vector3(5,6,7),fresh.Items[0].Positions[0]);
        var copies=request.CopyItems();copies[0].Mesh.Positions[0]=new(9,9,9);Assert.Equal(Vector3.Zero,copies[1].Mesh.Positions[0]);Assert.Equal(Vector3.Zero,request.Items[0].Positions[0]);
    }
}
