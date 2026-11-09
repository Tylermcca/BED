using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Import;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class AssetReimportControllerTests
{
    private const string Id="11111111111111111111111111111111",Other="22222222222222222222222222222222";
    private static JsonElement Json(string text)=>JsonDocument.Parse(text).RootElement.Clone();
    private static AssetRecord Record(string id,string name,byte[] source)=>new(id,name,Convert.ToHexStringLower(SHA256.HashData(source)),"obj",1,Json("{}"),new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
    private sealed class Fixture:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("as11-journey-").FullName;public readonly AssetCatalog Catalog=new();
        public string Path=>System.IO.Path.Combine(Root,"self.obj");public string Sidecar=>AssetRecord.SidecarFor(Path);
        public readonly ModelPreview Preview;public readonly AssetReimportController Controller;
        public Fixture()
        {
            File.WriteAllText(Path,"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");var source=File.ReadAllBytes(Path);var record=Record(Id,"self.obj",source);File.WriteAllText(Sidecar,record.Write());
            var bytes=DerivedMeshCodec.Encode(new([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]));
            Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new(record,source,bytes,[],true,"BEDMESH1","keep-policy"),new(Record(Other,"other.obj",[2]),[2],[9],[],true,"opaque","keep")],out _));
            Preview=new(Catalog);Preview.Select(Catalog.Select(Id)!);Assert.True(Preview.TryCurrent(out _,out _));Controller=new(Root,Catalog,ProjectFrame.Gltf,Preview);
        }
        public void Dispose(){Controller.Dispose();Catalog.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Public_OBJ_reimport_changes_geometry_and_refreshes_preview_preserving_other_assets_and_placed_scene()
    {
        using var f=new Fixture();var old=f.Catalog.Select(Id)!;var other=f.Catalog.Select(Other)!;var draft=f.Controller.OpenSettings(f.Path);
        Assert.True(draft.TryReplace(Json("{\"scale\":2,\"upAxis\":\"Z\"}")));
        var scene=new Scene([SceneObject.At("placed",Id,new(2,3,4)) with{Scale=new(3,4,5),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f)}]);
        var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);var before=new SceneFormat().Write(scene);
        Assert.True(f.Controller.TryReimport(draft,2,out var why),why);Assert.False(f.Catalog.TryResolve(old,out _,out _));Assert.True(f.Catalog.TryResolve(other,out _,out _));
        Assert.Equal(2,f.Catalog.CurrentInputs().Count);Assert.True(f.Preview.TryMesh(out var mesh,out why),why);Assert.Equal(3,mesh!.Positions.Length);
        Assert.Contains(mesh.Positions,p=>Vector3.Distance(p,new(2,0,0))<.00001f);Assert.Contains(mesh.Positions,p=>Vector3.Distance(p,new(0,0,2))<.00001f);
        Assert.Equal(MeshUv0Origin.ObjBottomLeft,mesh.Uv0Origin);Assert.Equal(before,new SceneFormat().Write(session.Current));Assert.Empty(session.Journal);Assert.False(session.Dirty);
        var record=AssetRecord.Read(File.ReadAllText(f.Sidecar));Assert.Equal(Id,record.Guid);Assert.Equal(2,record.ImporterVersion);Assert.Equal(2,record.Settings.GetProperty("scale").GetInt32());Assert.Equal(2,f.Controller.Published!.Artifacts.Count);
    }
    [Fact]
    public void Failed_import_and_cancel_retain_sidecar_generation_and_label_last_good_preview()
    {
        using var f=new Fixture();var first=f.Controller.OpenSettings(f.Path);Assert.True(f.Controller.TryReimport(first,1,out _));
        var stored=f.Controller.Published;var sidecar=File.ReadAllText(f.Sidecar);Assert.True(f.Preview.TryCurrent(out var good,out _));
        var draft=f.Controller.OpenSettings(f.Path);Assert.True(draft.TryReplace(Json("{\"scale\":3}")));File.WriteAllText(f.Path,"not an obj mesh");
        Assert.False(f.Controller.TryReimport(draft,1,out var why));Assert.StartsWith("Reimport failed:",why);Assert.Equal(sidecar,File.ReadAllText(f.Sidecar));Assert.Same(stored,f.Controller.Published);
        Assert.False(f.Preview.TryCurrent(out var current,out why));Assert.Null(current);Assert.Same(good,f.Preview.LastGood);Assert.StartsWith("Reimport failed:",why);
        var canceled=f.Controller.OpenSettings(f.Path);Assert.False(f.Controller.TryReimport(canceled,1,out _,new CancellationToken(true)));Assert.Equal(sidecar,File.ReadAllText(f.Sidecar));
    }
    [Fact]
    public void Sidecar_save_failure_rolls_back_storage_and_keeps_the_last_good_catalog_generation()
    {
        using var f=new Fixture();Assert.True(f.Controller.TryReimport(f.Controller.OpenSettings(f.Path),1,out _));
        var previous=f.Controller.Published;var selection=f.Catalog.Select(Id)!;var sidecar=File.ReadAllText(f.Sidecar);
        var draft=f.Controller.OpenSettings(f.Path);Assert.True(draft.TryReplace(Json("{\"scale\":2}")));
        using(var held=new FileStream(f.Sidecar,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Assert.False(f.Controller.TryReimport(draft,2,out var why));Assert.StartsWith("Reimport failed:",why);
            Assert.DoesNotContain("could not be undone",why);Assert.Same(previous,f.Controller.Published);Assert.Equal(sidecar,File.ReadAllText(f.Sidecar));
            Assert.True(f.Catalog.TryResolve(selection,out _,out _));Assert.Equal(selection.Generation,f.Catalog.Select(Id)!.Generation);
        }
    }
    [Fact]
    public void Failed_reimport_does_not_invalidate_an_unrelated_preview_selection()
    {
        using var f=new Fixture();f.Preview.Select(f.Catalog.Select(Other)!);Assert.True(f.Preview.TryCurrent(out var other,out _));
        var draft=f.Controller.OpenSettings(f.Path);File.WriteAllText(f.Path,"not an obj mesh");Assert.False(f.Controller.TryReimport(draft,1,out _));
        Assert.True(f.Preview.TryCurrent(out var current,out _));Assert.Same(other,current);Assert.Equal(Other,f.Preview.Selection!.AssetGuid);
    }
    [Fact]
    public void Rollback_restores_only_current_owned_pointer_and_never_deletes_generations()
    {
        var root=Directory.CreateTempSubdirectory("as11-rollback-").FullName;
        try
        {
            using var publisher=new AssetCatalogPublisher(root);var first=publisher.Prepare("one",new Dictionary<string,byte[]>{{"one.bin",[1]}});Assert.True(publisher.Publish(first,out _));
            var pointer=File.ReadAllBytes(System.IO.Path.Combine(root,".bed","cache","current.json"));var previous=publisher.Current;
            var second=publisher.Prepare("two",new Dictionary<string,byte[]>{{"two.bin",[2]}});Assert.True(publisher.Publish(second,out _));Assert.False(publisher.Rollback(first,out _));
            Assert.True(publisher.Rollback(second,out var why),why);Assert.Same(previous,publisher.Current);Assert.Equal(pointer,File.ReadAllBytes(System.IO.Path.Combine(root,".bed","cache","current.json")));
            Assert.Equal(2,Directory.GetDirectories(System.IO.Path.Combine(root,".bed","cache","generations")).Length);Assert.False(publisher.Rollback(second,out _));
        }
        finally{Directory.Delete(root,true);}
    }
}
