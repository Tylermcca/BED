using System.Numerics;
using System.Runtime.CompilerServices;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Runtime;
using Xunit;
namespace Brutal.Authoring.Tests;

public sealed class TexturedCrateSampleTests
{
    private const string Model = "c8a7e000000000000000000000000001", Texture = "c8a7e000000000000000000000000002";
    private static string Source([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../samples/SceneStarterProject/assets"));
    [Fact]
    public void Generated_crate_imports_publishes_and_previews_its_GUID_texture_without_rewriting_sources()
    {
        var root = Directory.CreateTempSubdirectory("bed-crate-").FullName;
        try
        {
            var assets = Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;
            string[] names = ["textured-crate.obj", "textured-crate.mtl", "textured-crate.png", "textured-crate.obj.asset.json", "textured-crate.png.asset.json"];
            var originals = names.ToDictionary(n=>n,n=>File.ReadAllBytes(Path.Combine(Source(),n)));
            foreach(var pair in originals) File.WriteAllBytes(Path.Combine(assets,pair.Key),pair.Value);
            Directory.CreateDirectory(Path.Combine(root,"data")); Directory.CreateDirectory(Path.Combine(root,"schemas"));
            using var opened = new AssetCatalogProvisioner().Open(new(Path.Combine(root,"data"),Path.Combine(root,"schemas")))!;
            Assert.NotNull(opened); Assert.Empty(opened.Problems); Assert.Empty(opened.Warnings);
            Assert.Equal(AssetCatalog.State.Valid,opened.Catalog.Resolve(Model).State);
            Assert.Equal(AssetCatalog.State.Valid,opened.Catalog.Resolve(Texture).State);
            var saved=ProjectAssets.Open(root); Assert.Equal(2,saved.ArtifactNames.Count);
            var part=Assert.Single(saved.ReadMesh(Model).Parts);
            Assert.Equal(36,part.Geometry.Indices.Length); Assert.NotNull(part.Geometry.Uv0);
            Assert.Equal(new Vector3(-.5f,0,-.5f),new(part.Geometry.Positions.Min(p=>p.X),part.Geometry.Positions.Min(p=>p.Y),part.Geometry.Positions.Min(p=>p.Z)));
            Assert.Equal(new Vector3(.5f,1,.5f),new(part.Geometry.Positions.Max(p=>p.X),part.Geometry.Positions.Max(p=>p.Y),part.Geometry.Positions.Max(p=>p.Z)));
            Assert.Equal(Vector4.One,part.Material!.BaseColor); Assert.Equal(Guid.ParseExact(Texture,"N"),part.Material.Texture);
            var image=saved.ReadTexture(part.Material.Texture!.Value); Assert.Equal(8,image.Width); Assert.Equal(8,image.Height);
            var expected=new List<byte>();
            for(var y=0;y<8;y++)for(var x=0;x<8;x++)expected.AddRange(x is 0 or 7 || y is 0 or 7 || x==y ? new byte[]{72,40,20,255}:new byte[]{176,116,56,255});
            Assert.Equal(expected,image.Rgba);
            var preview=new ModelPreview(opened.Catalog); preview.Select(opened.Catalog.Select(Model)!);
            Assert.True(preview.TryMesh(out var mesh,out var why),why); Assert.Equal(Texture,mesh!.Material!.TextureAssetGuid);
            Assert.Equal(names.Order(),Directory.GetFiles(assets).Select(Path.GetFileName).Order());
            foreach(var pair in originals)Assert.Equal(pair.Value,File.ReadAllBytes(Path.Combine(assets,pair.Key)));
        }
        finally { Directory.Delete(root,true); }
    }
}
