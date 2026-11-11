using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Import;
using Xunit;
namespace Bed.Tests;
public sealed class AssetReimportApiTests
{
    private const string Id="11111111111111111111111111111111",Dep="22222222222222222222222222222222";
    private static JsonElement Json(string s)=>JsonDocument.Parse(s).RootElement.Clone();
    private static AssetRecord Record(string id,byte[] b)=>new(id,"self.obj",Convert.ToHexStringLower(SHA256.HashData(b)),"obj",1,Json("{}"),new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
    private static AssetCatalog.Input Input(string id,params string[] deps)=>new(Record(id,[1]),[1],[5],deps,true,"test","explicit-policy");
    [Fact]
    public void Current_inputs_are_complete_read_only_and_not_replaced_by_a_refused_publication()
    {
        using var catalog=new AssetCatalog();var a=Input(Id,Dep);var b=Input(Dep);var token=catalog.BeginRefresh();Assert.True(catalog.Publish(token,[a,b],out _));
        var snapshot=catalog.CurrentInputs();Assert.Equal(2,snapshot.Count);Assert.Same(a,snapshot[0]);Assert.Same(b,snapshot[1]);
        Assert.Throws<NotSupportedException>(()=>((IList<AssetCatalog.Input>)snapshot)[0]=b);
        Assert.False(catalog.Publish(token,[],out _));Assert.Equal(2,catalog.CurrentInputs().Count);
        var next=a.WithReimportedContent(Record(Id,[1]) with{Settings=Json("{\"scale\":2}")},[1],[8],"BEDMESH1");
        Assert.True(catalog.Publish(catalog.BeginRefresh(),[next,b],out _));Assert.Equal(2,snapshot.Count);Assert.Same(a,snapshot[0]);
        Assert.True(catalog.Publish(catalog.BeginRefresh(),[next],out _));Assert.Equal(AssetCatalog.State.Stale,catalog.Resolve(Id).State);
    }
    [Theory]
    [InlineData("{\"scale\":0}")][InlineData("{\"scale\":1001}")][InlineData("{\"scale\":\"2\"}")][InlineData("{\"upAxis\":\"X\"}")][InlineData("{\"other\":1}")][InlineData("[]")]
    public void Unsupported_settings_refuse_without_any_import_or_write(string json)=>Assert.Throws<ArgumentException>(()=>AssetReimportSettings.Parse(Json(json),ProjectFrame.Gltf));
    [Fact]
    public void Settings_use_only_approved_defaults_and_postimport_uniform_scale_axis_rotation()
    {
        Assert.Equal(new AssetReimportSettings(1,"Y"),AssetReimportSettings.Parse(Json("{}"),ProjectFrame.Gltf));
        Assert.Equal(new AssetReimportSettings(1,"Z"),AssetReimportSettings.Parse(Json("{}"),ProjectFrame.ZUpRightHanded));
        var settings=AssetReimportSettings.Parse(Json("{\"scale\":2,\"upAxis\":\"Z\"}"),ProjectFrame.Gltf);
        Assert.InRange(Vector3.Distance(new(0,0,2),Vector3.Transform(Vector3.UnitY,settings.Transform(ProjectFrame.Gltf))),0,.00001f);
    }
    [Fact]
    public void Prepare_preserves_sidecar_and_identity_then_explicit_save_has_exact_predecessor_guard()
    {
        var root=Directory.CreateTempSubdirectory("as11-api-").FullName;
        try
        {
            var path=Path.Combine(root,"self.obj");File.WriteAllText(path,"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");var source=File.ReadAllBytes(path);
            var original=Record(Id,source);var sidecar=AssetRecord.SidecarFor(path);File.WriteAllText(sidecar,original.Write());var before=File.ReadAllText(sidecar);
            var candidate=original with{Settings=Json("{\"scale\":2}")};var prepared=candidate.PrepareReimport(path,2,["self"]);
            Assert.Equal(before,File.ReadAllText(sidecar));Assert.Equal(Id,prepared.Guid);Assert.Equal(original.SourceHash,prepared.SourceHash);Assert.Equal(2,prepared.ImporterVersion);
            Assert.Throws<InvalidOperationException>(()=>prepared.SaveReimport(path,"wrong predecessor"));Assert.Equal(before,File.ReadAllText(sidecar));
            prepared.SaveReimport(path,before);Assert.Equal(prepared.Write(),File.ReadAllText(sidecar));
            var updated=prepared.Reimport(path,3,["self"]);Assert.Equal(Id,updated.Guid);Assert.Equal(3,AssetRecord.Read(File.ReadAllText(sidecar)).ImporterVersion);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Preview_failure_retains_last_good_with_label_and_never_returns_it_as_current()
    {
        using var catalog=new AssetCatalog();Assert.True(catalog.Publish(catalog.BeginRefresh(),[Input(Id)],out _));var preview=new ModelPreview(catalog);preview.Select(catalog.Select(Id)!);
        Assert.True(preview.TryCurrent(out var old,out _));preview.Invalidate("Reimport failed: synthetic refusal");Assert.False(preview.TryCurrent(out var current,out var why));
        Assert.Null(current);Assert.Same(old,preview.LastGood);Assert.Equal("Reimport failed: synthetic refusal",why);
        preview.Select(catalog.Select(Id)!);Assert.True(preview.TryCurrent(out _,out _));
    }
}
