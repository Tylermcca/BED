using System.Numerics;
using System.Reflection;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneSelectionPerf20bTests
{
    private sealed class Source:IViewportSource<Scene>
    {
        private readonly MeshData mesh=new([new(-1,-1,0),new(1,-1,0),new(0,1,0)],[0,1,2]);
        internal bool Reverse;
        public IEnumerable<ViewportItem> Items(Scene scene)
        {
            ViewportItem[] items=[new("red",Matrix4x4.Identity,mesh,0xff0000ff),new("blue",Matrix4x4.Identity,mesh,0xffff0000)];
            return Reverse?items.Reverse():items;
        }
        public ICommand<Scene>? MoveTo(Scene scene,string id,Vector3 world)=>null;
    }
    [Fact]
    public void Selection_reuses_projection_with_live_shading_bounds_and_stable_equal_depth_painter_order()
    {
        using var host=new HeadlessHost(640,480){Drawing=true};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new Scene([SceneObject.At("red",null,Vector3.Zero),SceneObject.At("blue",null,Vector3.Zero)])),SceneEditing.Outline);
        var source=new Source();var camera=new Camera(Vector3.Zero,0,0,6);var view=new ViewportView<Scene>(editor,source){Camera=camera};
        void Draw(){ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(620,460));Ui.Begin("Selection projection fixture"u8);view.Draw();Ui.End();}
        object Cache()=>typeof(ViewportView<Scene>).GetField("cpuGeometryTriangles",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(view)!;
        uint Center()=>host.Pixels[(int)(view.LastRect.Origin.Y+view.LastRect.Size.Y/2)*host.Width+(int)(view.LastRect.Origin.X+view.LastRect.Size.X/2)];
        host.Frames(Draw,4);var geometry=Cache();var pixel=Center();Assert.True(((pixel>>16)&255)>(pixel&255)); // Last equal-depth triangle is blue.
        var before=host.Pixels.ToArray();editor.Select("blue");host.Frames(Draw,2);Assert.Same(geometry,Cache());Assert.False(before.SequenceEqual(host.Pixels));
        var selected=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.ListItem).Select(n=>(n.Label,n.Bounds,n.Selected)).ToArray();Assert.Contains(selected,n=>n.Label=="blue"&&n.Selected);Assert.Contains(selected,n=>n.Label=="red"&&!n.Selected);
        var cached=host.Pixels.ToArray();view=new(editor,source){Camera=camera};host.Frames(Draw,2);Assert.Equal(cached,host.Pixels);Assert.Equal(selected,Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.ListItem).Select(n=>(n.Label,n.Bounds,n.Selected)).ToArray());
        editor.Select([]);source.Reverse=true;host.Frames(Draw,2);pixel=Center();Assert.True((pixel&255)>((pixel>>16)&255)); // Reversing source order puts red last.
        Assert.Empty(editor.Session.Journal);Assert.False(editor.Session.Dirty);
    }
}
