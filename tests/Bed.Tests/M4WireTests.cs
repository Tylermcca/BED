using System.Numerics;
using System.Runtime.CompilerServices;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class M4WireTests
{
    private const string Model = "c8a7e000000000000000000000000001", Texture = "c8a7e000000000000000000000000002";
    private static string Sample([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../samples/SceneStarterProject"));
    private sealed class Rig : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("m4-wire-").FullName;
        public readonly HeadlessHost Host = new(900, 700) { Drawing = false };
        public readonly ProjectAssetCatalog Assets;
        public readonly DataWorkspace Workspace;
        public ViewportView<Scene> View => Workspace.SceneViewport!;
        public Scene Scene => Workspace.SceneWindow!.Editor.Session.Current;
        public Rig(bool multipart=false)
        {
            var assets = Directory.CreateDirectory(Path.Combine(Root, "assets")).FullName;
            foreach (var file in Directory.GetFiles(Path.Combine(Sample(), "assets"))) File.Copy(file, Path.Combine(assets, Path.GetFileName(file)));
            var data = Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(Root, "schemas")).FullName;
            File.Copy(Path.Combine(Sample(), "data", "level.json"), Path.Combine(data, "level.json"));
            Assets = new AssetCatalogProvisioner().Open(new(data, schemas))!; Assert.Empty(Assets.Problems);
            if(multipart)
            {
                static MeshData Quad(float left,float right) => new([new(left,0,0),new(right,0,0),new(right,1,0),new(left,1,0)],[0,1,2,0,2,3])
                {Uv0=[Vector2.Zero,Vector2.UnitX,Vector2.One,Vector2.UnitY],Uv0Origin=MeshUv0Origin.ObjBottomLeft};
                var derived=DerivedMeshCodec.Encode(new MeshPart[] {
                    new(Quad(-1,-.1f),new(0,new(1,0,0,1),Guid.ParseExact(Texture,"N"))),
                    new(Quad(.1f,1),new(1,new(0,1,0,1),null)) });
                var path=Path.Combine(assets,"textured-crate.obj");var record=AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(path)));
                var input=new AssetCatalog.Input(record,File.ReadAllBytes(path),derived,[Texture],true,"BEDMESH2",record.Write());
                var inputs=Assets.Catalog.CurrentInputs().Select(i=>i.Guid==Model?input:i).ToArray();
                Assert.True(Assets.Catalog.Publish(Assets.Catalog.BeginRefresh(),inputs,out var why),why);
            }
            Workspace = new(DataProject.Open(data, schemas)); Assert.True(Workspace.BindSceneAssets(Assets.Browser, Assets.Catalog));
            Assert.True(Workspace.Documents.BindScene("level.json", "scene", out var bindWhy), bindWhy); Assert.True(Workspace.Show("level.json"), Workspace.Said);
            View.Camera = new(new Vector3(0, .5f, 0), 0, 0, 4);
            EditorWindow<Scene>.Configure(ImGui.GetIO()); Host.Frames(Draw, 3);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(float2.Zero); ImGui.SetNextWindowSize(new float2(850, 650));
            Ui.Begin("M4 viewport fixture"u8); View.Draw(); Ui.End();
        }
        public void Dispose() { Workspace.Dispose(); Assets.Dispose(); Host.Dispose(); Directory.Delete(Root, true); }
    }

    [Fact]
    public void Sample_level_crate_uses_real_catalog_geometry_and_texture_in_the_captured_frame_and_UI_tree()
    {
        using var r = new Rig();
        Assert.Equal(Model, r.Scene.Find("textured_crate")!.Asset);
        var item = Assert.Single(r.View.Source.Items(r.Scene), i => i.Id == "textured_crate");
        Assert.Equal(36, item.Mesh.Indices.Length); Assert.Equal(Texture, item.Mesh.Material!.TextureAssetGuid);
        Assert.Equal(Vector4.One, item.Mesh.Material.BaseColor); Assert.Equal(MeshUv0Origin.ObjBottomLeft, item.Mesh.Uv0Origin);
        Assert.Equal(0, item.Mesh.Bounds.Min.Y); Assert.Equal(1, item.Mesh.Bounds.Max.Y); // Real crate, not the centred missing-asset cube.
        var captured = Assert.Single(r.View.RenderRequest!.Items, i => i.Id == "textured_crate");
        Assert.Equal(Texture, captured.Payload.Material.TextureAssetGuid);
        Assert.Equal(item.Mesh.Uv0!.Select(uv => new Vector2(uv.X, 1 - uv.Y)), captured.Payload.Uv0);
        var indexed = new SceneIndexedGeometry(captured.Payload); Assert.Equal(36, indexed.IndexCount);
        var saved = ProjectAssets.Open(r.Root).ReadMesh(Model); Assert.Equal(36, Assert.Single(saved.Parts).Geometry.Indices.Length);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "textured_crate");
        Assert.Equal("textured_crate", r.View.RenderRequest.Pick(r.View.LastRect.Origin + r.View.LastRect.Size / 2).ItemId);
        r.Host.Drawing = true; r.Host.Frame(r.Draw);
        var bounds = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "textured_crate").Bounds;
        var centre = r.Host.Pixels[(int)(bounds.Y + bounds.Height / 2) * r.Host.Width + (int)(bounds.X + bounds.Width / 2)];
        Assert.NotEqual(0xff2c2826u,centre); // Offscreen fallback paints actual crate geometry over the viewport background.
        Assert.Contains("Material textures are not displayed.", r.View.RenderStatus); // Headless CPU fallback stays explicit.
        Assert.Empty(r.Workspace.Project.Dirty);
    }

    [Fact]
    public void Pending_catalog_removes_resolved_geometry_and_invalidates_a_captured_pick()
    {
        using var r = new Rig(); var request = r.View.RenderRequest!;
        var hit = request.Pick(r.View.LastRect.Origin + r.View.LastRect.Size / 2);
        Assert.Equal("textured_crate", hit.ItemId);
        r.Assets.Catalog.BeginRefresh(); r.Host.Frames(r.Draw, 3);
        var placeholder = Assert.Single(r.View.Source.Items(r.Scene), i => i.Id == "textured_crate");
        Assert.Same(MeshData.Cube, placeholder.Mesh); Assert.Null(placeholder.Mesh.Material);
        Assert.False(r.View.TrySelectRenderResult(hit)); Assert.Empty(r.Workspace.SceneWindow!.Editor.Selection);
        Assert.Empty(r.Workspace.Project.Dirty);
    }

    [Fact]
    public void Multi_part_model_keeps_one_pick_and_tree_item_and_every_part_tint_texture_and_indexed_draw()
    {
        using var r=new Rig(multipart:true);
        var item=Assert.Single(r.View.Source.Items(r.Scene),i=>i.Id=="textured_crate");
        Assert.Equal(2,item.Mesh.Parts!.Count); Assert.Equal(8,item.Mesh.Positions.Length); Assert.Equal(12,item.Mesh.Indices.Length);
        var request=r.View.RenderRequest!;var captured=Assert.Single(request.Items,i=>i.Id=="textured_crate");
        Assert.Equal(2,captured.Payloads.Count);
        Assert.Equal(new Vector4(1,0,0,1),captured.Payloads[0].Material.BaseColor);
        Assert.Equal(Texture,captured.Payloads[0].Material.TextureAssetGuid);
        Assert.Equal(r.Assets.Catalog.Resolve(Texture).Current!.SourceHash,captured.Payloads[0].Material.TextureSourceHash);
        Assert.Equal(new Vector4(0,1,0,1),captured.Payloads[1].Material.BaseColor);Assert.Null(captured.Payloads[1].Material.TextureAssetGuid);
        Assert.All(captured.Payloads,p=>Assert.Equal(6,new SceneIndexedGeometry(p).IndexCount));
        Assert.Equal(4,SceneMaterialDraws.Build(request).Count(d=>d.ItemId=="textured_crate"));
        var part0=Assert.Single(request.CopyItems(),i=>i.Id=="textured_crate").Mesh.Parts![0];
        part0.Positions[0]=new(99);part0.Uv0![0]=new(99);Assert.Equal(new Vector3(-1,0,0),captured.Payloads[0].Positions[0]);
        Assert.Equal(new Vector2(0,1),captured.Payloads[0].Uv0[0]);
        var screenLeft=r.View.Camera.Project(new(-.5f,.5f,0),r.View.LastRect.Size)!.Value.Screen+r.View.LastRect.Origin;
        var screenRight=r.View.Camera.Project(new(.5f,.5f,0),r.View.LastRect.Size)!.Value.Screen+r.View.LastRect.Origin;
        Assert.Equal("textured_crate",request.Pick(screenLeft).ItemId);Assert.Equal("textured_crate",request.Pick(screenRight).ItemId);
        Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="textured_crate");
        r.Host.Drawing=true;r.Host.Frame(r.Draw);
        uint Pixel(Vector2 screen)=>r.Host.Pixels[(int)screen.Y*r.Host.Width+(int)screen.X];
        var left=Pixel(screenLeft);var right=Pixel(screenRight);
        Assert.True((left&255)>((left>>8)&255));Assert.True(((right>>8)&255)>(right&255));
        Assert.Contains("Material textures are not displayed.",r.View.RenderStatus);
        Assert.True(r.View.TrySelectRenderResult(r.View.RenderRequest!.Pick(screenRight)));
        Assert.Equal(new[]{"textured_crate"},r.Workspace.SceneWindow!.Editor.Selection);
        Assert.Empty(r.Workspace.Project.Dirty);
    }

    [Fact]
    public void Compiled_native_collection_and_draw_both_consume_every_payload_with_object_part_buffer_keys()
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var assembly=typeof(EditorApp).Assembly;
        var owner=assembly.GetType("Bed.Editor.App.SceneDeviceOwner",true)!;
        var target=Assert.Single(owner.GetMethods(flags),m=>m.Name=="Target"&&m.GetParameters()[1].ParameterType==typeof(SceneRenderRequest));
        var pipeline=assembly.GetType("Bed.Editor.App.SceneTrianglePipeline",true)!;
        var draw=pipeline.GetMethod("Draw",flags)!;
        foreach(var method in new[]{target,draw})
        {
            var calls=SceneFrameLifecycleTests.Calls(method);
            Assert.Contains(calls,c=>c.Method.Name=="get_Payloads"&&c.Method.DeclaringType==typeof(SceneRenderItem));
            Assert.DoesNotContain(calls,c=>c.Method.Name=="get_Payload"&&c.Method.DeclaringType==typeof(SceneRenderItem));
        }
        Assert.Equal(typeof(ValueTuple<string,int>),draw.GetParameters()[2].ParameterType.GetGenericArguments()[0]);
    }
}
