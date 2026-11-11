using System.Numerics;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class SceneMaterialPresentationTests
{
    private sealed class Source:IViewportSource<Scene>
    {
        public IEnumerable<ViewportItem> Items(Scene document)
        {
            var cube=MeshData.Cube;yield return new("crate",Matrix4x4.Identity,cube with{Uv0=Enumerable.Repeat(Vector2.Zero,cube.Positions.Length).ToArray(),
                Material=new(new(1,1,1,1),new string('a',32),new string('b',64))},0xffffffff);
        }
        public ICommand<Scene>? MoveTo(Scene document,string id,Vector3 world)=>new SceneViewport().MoveTo(document,id,world);
    }
    private static EditorModel<Scene> Editor()=>new(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new Scene([SceneObject.At("crate",null,Vector3.Zero)])),SceneEditing.Outline);
    private static ImTextureRef Texture=>new(){_TexID=new ImTextureID((nint)0x435678)};
    private static void Draw(ViewportView<Scene> view){Im.SetNextWindowSize(new(600,450));Ui.Begin("Materials"u8);view.Draw();Ui.End();}
    [Fact]
    public void The_view_passes_only_its_explicit_catalog_and_replacement_invalidates_old_scene_and_pick_identity()
    {
        using var host=new HeadlessHost(800,600){Drawing=false};using var first=new AssetCatalog();using var second=new AssetCatalog();
        var editor=Editor();var view=new ViewportView<Scene>(editor,new Source()){MaterialCatalog=first};var seen=new List<AssetCatalog?>();
        using var presenter=ScenePresentation.BindMaterials((_,r,current,catalog)=>{seen.Add(catalog);Assert.Equal(r.Identity,current());return new(r,Texture,()=>true);});
        host.Frames(()=>Draw(view),3);Assert.All(seen,c=>Assert.Same(first,c));var old=view.RenderRequest!;editor.Select("crate");
        var miss=old.Pick(new(-1000));view.MaterialCatalog=second;Assert.False(view.TrySelectRenderResult(miss));Assert.Equal(new[]{"crate"},editor.Selection);
        host.Frame(()=>Draw(view));Assert.Same(second,seen[^1]);Assert.NotEqual(old.Identity.ViewGeneration,view.RenderRequest!.Identity.ViewGeneration);
        view.MaterialCatalog=null;host.Frame(()=>Draw(view));Assert.Null(seen[^1]);Assert.Contains("placeholder",view.RenderStatus);Assert.Empty(editor.Session.Journal);
    }
    [Fact]
    public void Missing_catalog_and_CPU_fallback_say_which_texture_feature_is_unavailable_without_changing_selection()
    {
        using var host=new HeadlessHost(800,600){Drawing=false};var editor=Editor();var view=new ViewportView<Scene>(editor,new Source());
        host.Frames(()=>Draw(view),3);Assert.Contains("textures are not displayed",view.RenderStatus);
        using var catalog=new AssetCatalog();view.MaterialCatalog=catalog;
        using var presenter=ScenePresentation.BindMaterials((_,r,_,supplied)=>{Assert.Same(catalog,supplied);return new(r,Texture,()=>true){Status="Material texture placeholder: missing source."};});
        host.Frame(()=>Draw(view));Assert.Contains("missing source",view.RenderStatus);Assert.Empty(editor.Selection);Assert.Empty(editor.Session.Journal);
    }
    [Fact]
    public void Vulkan_factory_is_catalog_aware_and_OpenGL_stays_CPU_only()
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        var loop=typeof(EditorApp).GetMethod("Loop",flags)!;
        Assert.Single(SceneFrameLifecycleTests.Calls(loop),c=>c.Method.Name=="BindMaterials"&&c.Method.DeclaringType==typeof(ScenePresentation));
        var gl=typeof(EditorApp).GetMethod("RunOpenGl",flags)!;
        Assert.DoesNotContain(SceneFrameLifecycleTests.Calls(gl),c=>c.Method.DeclaringType==typeof(ScenePresentation)&&(c.Method.Name=="Bind"||c.Method.Name=="BindMaterials"));
    }

}
