using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Import;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class GltfReimportControllerTests
{
    [Fact]
    public void Public_glTF_reimport_pins_external_only_changes_and_preserves_other_assets_and_placed_references()
    {
        const string id="11111111111111111111111111111111",other="22222222222222222222222222222222";
        var root=Directory.CreateTempSubdirectory("as12-controller-").FullName;
        try
        {
            var path=GltfReadSetTests.WriteFixture(root,3);var source=File.ReadAllBytes(path);var primary=Convert.ToHexStringLower(SHA256.HashData(source));
            AssetRecord Record(string guid,string name,byte[] bytes)=>new(guid,name,Convert.ToHexStringLower(SHA256.HashData(bytes)),"gltf",1,JsonSerializer.SerializeToElement(new{}),new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            var record=Record(id,"self.gltf",source);File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());using var catalog=new AssetCatalog();
            Assert.True(catalog.Publish(catalog.BeginRefresh(),[new(record,source,[9],[],true,"initial","same-policy"),new(Record(other,"other.bin",[2]),[2],[8],[],true,"opaque","keep")],out _));
            var unrelated=catalog.Select(other)!;var preview=new ModelPreview(catalog);preview.Select(catalog.Select(id)!);using var controller=new AssetReimportController(root,catalog,ProjectFrame.Gltf,preview);
            var scene=new Scene([SceneObject.At("placed",id,new(2,3,4)) with{Scale=new(3,4,5),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f)}]);
            var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);var before=new SceneFormat().Write(scene);
            Assert.True(controller.TryReimport(controller.OpenSettings(path),1,out var why),why);var first=catalog.Select(id)!;Assert.True(preview.TryMesh(out var mesh,out why),why);Assert.Equal(0,mesh!.Positions[2].X);
            var firstInput=catalog.CurrentInputs().Single(i=>i.Guid==id);var read=Assert.Single(firstInput.SourceFiles);Assert.Equal("vertices.bin",read.RelativePath);
            var bufferPath=Path.Combine(root,"vertices.bin");var bytes=File.ReadAllBytes(bufferPath);BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(24),1);File.WriteAllBytes(bufferPath,bytes);
            Assert.Equal(source,File.ReadAllBytes(path));Assert.True(controller.TryReimport(controller.OpenSettings(path),1,out why),why);
            Assert.False(catalog.TryResolve(first,out _,out _));Assert.True(catalog.TryResolve(unrelated,out _,out _));Assert.True(preview.TryMesh(out mesh,out why),why);Assert.Equal(1,mesh!.Positions[2].X);Assert.Equal(MeshUv0Origin.TopLeft,mesh.Uv0Origin);
            var pin=Assert.Single(catalog.CurrentInputs().Single(i=>i.Guid==id).SourceFiles);Assert.NotEqual(read.SourceHash,pin.SourceHash);Assert.Equal(primary,catalog.Resolve(id).Current!.SourceHash);
            Assert.Equal(before,new SceneFormat().Write(session.Current));Assert.Empty(session.Journal);Assert.False(session.Dirty);Assert.Equal(2,catalog.CurrentInputs().Count);Assert.Equal(2,controller.Published!.Artifacts.Count);
            Assert.Equal(id,AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(path))).Guid);
        }
        finally{Directory.Delete(root,true);}
    }
}
