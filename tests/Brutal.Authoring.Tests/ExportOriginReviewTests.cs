using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class ExportOriginReviewTests
{
    private const string Guid1="11111111111111111111111111111111";
    private static readonly string SourceHash=new('a',64);
    private static GameContentManifest Manifest(ExportSource? source=null)
    {
        var mapping=new GameContentMapping("mesh","content/mesh.bin","test-v1");
        return GameContentManifest.Create(GameBuildProfile.Create("desktop","Game.csproj",GameBuildTarget.WindowsX64,[mapping]),"game","r1",true,
            [new(mapping,new byte[]{1},Convert.ToHexStringLower(SHA256.HashData(new byte[]{1})),[],true,source)]);
    }
    private static AssetRecord Record(AssetOrigin origin=AssetOrigin.Ours,string licence="self-authored")=>new(Guid1,"synthetic.bin",SourceHash,"test",1,
        JsonSerializer.SerializeToElement(new{}),new("Example author",licence,"synthetic generator",Declared:origin),AiUse.Forbidden);
    private static ExportSource Catalog()=>ExportSource.Catalog(Guid1,SourceHash);
    [Fact] public void Missing_mapping_refuses_and_explicit_game_output_passes()
    {
        Assert.False(ExportOriginReview.Evaluate(Manifest(),[]).Accepted);
        Assert.True(ExportOriginReview.Evaluate(Manifest(ExportSource.GameOwned),[]).Accepted);
        Assert.NotEqual(Manifest().Hash,Manifest(ExportSource.GameOwned).Hash);
        Assert.NotEqual(Manifest(Catalog()).Hash,Manifest(ExportSource.GameOwned).Hash);
    }
    [Fact] public void Exact_source_record_is_required_without_ai_use_substitution()
    {
        var m=Manifest(Catalog());Assert.True(ExportOriginReview.Evaluate(m,[Record()]).Accepted);
        Assert.False(ExportOriginReview.Evaluate(m,[]).Accepted);
        Assert.False(ExportOriginReview.Evaluate(m,[Record(),Record()]).Accepted);
        Assert.False(ExportOriginReview.Evaluate(m,[Record() with {SourceHash=new string('b',64)}]).Accepted);
        Assert.False(ExportOriginReview.Evaluate(m,[Record(AssetOrigin.Undeclared) with {AiUse=AiUse.Allowed}]).Accepted);
    }
    [Theory]
    [InlineData("CC0-1.0")][InlineData("CC-BY-3.0")][InlineData("CC-BY-4.0")][InlineData("MIT")][InlineData("Zlib")]
    [InlineData("Apache-2.0")][InlineData("BSD-2-Clause")][InlineData("BSD-3-Clause")][InlineData("OFL-1.1")]
    public void Approved_licence_has_exact_attribution(string licence)
    {
        var receipt=ExportOriginReview.Evaluate(Manifest(Catalog()),[Record(AssetOrigin.Licensed,licence)]);
        Assert.True(receipt.Accepted);Assert.Equal(new ExportOriginNotice(Guid1,"Example author","synthetic generator",licence),Assert.Single(receipt.Notices));
    }
    [Fact] public void Unknown_licence_missing_attribution_and_unread_rights_refuse()
    {
        var m=Manifest(Catalog());var licensed=Record(AssetOrigin.Licensed,"MIT");
        var unknown=ExportOriginReview.Evaluate(m,[Record(AssetOrigin.Licensed,"custom")]);
        Assert.Contains(unknown.Refusals,s=>s.Contains("licence not known to allow redistribution: custom"));
        Assert.False(ExportOriginReview.Evaluate(m,[licensed with {Provenance=licensed.Provenance with {Author=""}}]).Accepted);
        Assert.False(ExportOriginReview.Evaluate(m,[licensed with {RightsUnread="incomplete"}]).Accepted);
        var pd=Record(AssetOrigin.PublicDomain);Assert.True(ExportOriginReview.Evaluate(m,[pd]).Accepted);
        Assert.False(ExportOriginReview.Evaluate(m,[pd with {Provenance=pd.Provenance with {Origin=""}}]).Accepted);
    }
    [Fact] public void Both_gates_must_cover_the_same_manifest()
    {
        var m=Manifest(Catalog());var origin=ExportOriginReview.Evaluate(m,[Record()]);
        var check=new ExportCheckReceipt(m.Hash,"required",ExportCheckVerdict.Passed,DateTimeOffset.UtcNow,TimeSpan.Zero);
        Assert.True(ExportGate.Evaluate(m,["required"],[check],origin).CompleteExport);
        Assert.False(ExportGate.Evaluate(m,["required"],[],origin).CompleteExport);
        Assert.False(ExportGate.Evaluate(m,["required"],[check]).CompleteExport);
        Assert.False(ExportGate.Evaluate(m,["required"],[check],ExportOriginReview.Evaluate(Manifest(ExportSource.GameOwned),[])).CompleteExport);
        Assert.False(ExportGate.Evaluate(m,["required"],[check],ExportOriginReview.Evaluate(m,[])).CompleteExport);
    }
    [Fact] public void Source_hash_changes_manifest_and_invalid_source_identity_refuses()
    {
        Assert.NotEqual(Manifest(Catalog()).Hash,Manifest(ExportSource.Catalog(Guid1,new string('b',64))).Hash);
        Assert.Throws<ArgumentException>(()=>ExportSource.Catalog("bad",SourceHash));
        Assert.Throws<ArgumentException>(()=>ExportSource.Catalog(Guid1,"bad"));
    }
}
