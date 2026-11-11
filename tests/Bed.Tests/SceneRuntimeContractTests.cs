using System.Numerics;
using Xunit;

namespace Bed.Tests;

public sealed class SceneRuntimeContractTests
{
    private static string Document(params string[] objects) =>
        "{\"kind\":\"scene\",\"version\":1,\"objects\":[" + string.Join(",", objects) + "]}";

    [Fact]
    public void Defaults_are_existing_local_values_without_a_session()
    {
        var result = SceneRuntimeReader.Read(Document("{\"id\":\"origin\"}"));
        var item = Assert.Single(result.Scene.Objects);
        Assert.Equal("origin", item.Id); Assert.Null(item.Parent); Assert.Null(item.Asset);
        Assert.Equal(Vector3.Zero, item.Position); Assert.Equal(Quaternion.Identity, item.Rotation);
        Assert.Equal(Vector3.One, item.Scale); Assert.Empty(item.Properties); Assert.Empty(item.References);
        Assert.False(result.AssetsChecked); Assert.Empty(result.AssetIssues);
    }

    [Fact]
    public void Forward_parent_order_local_TRS_properties_and_refs_survive_canonical_round_trip()
    {
        var text = Document(
            """{"id":"child","parent":"root","asset":"asset:lamp","position":[1,2,3],"rotation":[0,0,0.6,0.8],"scale":[2,3,4],"properties":{"name":"Lamp","enabled":true,"strength":2.5},"references":{"target":"root"}}""",
            """{"id":"root","position":[10,20,30]}""");
        var result = SceneRuntimeReader.Read(text);
        Assert.Equal(new[] { "child", "root" }, result.Scene.Objects.Select(o => o.Id));
        var child = result.Scene.Objects[0];
        Assert.Equal("root", child.Parent); Assert.Equal("asset:lamp", child.Asset);
        Assert.Equal(new Vector3(1,2,3), child.Position);
        Assert.Equal(new Quaternion(0,0,0.6f,0.8f), child.Rotation); Assert.Equal(new Vector3(2,3,4), child.Scale);
        Assert.Equal("Lamp", child.Properties["name"].GetString()); Assert.True(child.Properties["enabled"].GetBoolean());
        Assert.Equal(2.5,child.Properties["strength"].GetDouble()); Assert.Equal("root",child.References["target"]);
        var format = new SceneFormat(); var canonical = format.Write(result.Scene);
        Assert.Equal(canonical,format.Write(SceneRuntimeReader.Read(canonical).Scene));
        Assert.Equal(new Vector3(10,20,30), result.Scene.Objects[1].Position);
    }

    [Fact]
    public void Custom_kind_must_be_explicit_and_schema_is_not_reinterpreted()
    {
        const string text = """{"kind":"level","version":1,"objects":[]}""";
        Assert.Empty(SceneRuntimeReader.Read(text,"level").Scene.Objects);
        var exception = Assert.Throws<DocumentFormatException>(() => SceneRuntimeReader.Read(text));
        Assert.Equal("$.kind",exception.Path);
        Assert.Equal("{\n  \"kind\": \"level\",\n  \"version\": 1,\n  \"objects\": []\n}\n",
            new SceneFormat("level").Write(SceneRuntimeReader.Read(text,"level").Scene));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(2)] [InlineData(2147483647)]
    public void Unsupported_versions_preserve_the_format_error(int version)
    {
        var text = "{\"kind\":\"scene\",\"version\":" + version + ",\"objects\":[]}";
        var direct = Assert.Throws<DocumentFormatException>(() => new SceneFormat().Read(text));
        var read = Assert.Throws<DocumentFormatException>(() => SceneRuntimeReader.Read(text));
        Assert.Equal(direct.Path,read.Path); Assert.Equal(direct.Message,read.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"kind\":\"scene\",\"kind\":\"scene\",\"version\":1,\"objects\":[]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":1,\"extra\":true,\"objects\":[]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":1,\"objects\":[{\"id\":\"root\",\"position\":[0,1]}]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":1,\"objects\":[{\"id\":\"root\",\"scale\":[1,1,1e100]}]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":1,\"objects\":[{\"id\":\"root\",\"rotation\":[0,0,1]}]}")]
    public void Malformed_input_preserves_path_message_and_never_calls_asset_lookup(string text)
    {
        var direct = Assert.Throws<DocumentFormatException>(() => new SceneFormat().Read(text));
        var calls = 0;
        var read = Assert.Throws<DocumentFormatException>(() => SceneRuntimeReader.Read(text,assetExists: _ => { calls++; return true; }));
        Assert.Equal(direct.Path,read.Path); Assert.Equal(direct.Message,read.Message); Assert.Equal(0,calls);
    }

    [Theory]
    [InlineData("{\"id\":\"Wrong\"}")]
    [InlineData("{\"id\":\"same\"},{\"id\":\"same\"}")]
    [InlineData("{\"id\":\"root\",\"parent\":\"missing\"}")]
    [InlineData("{\"id\":\"root\",\"parent\":\"root\"}")]
    [InlineData("{\"id\":\"a\",\"parent\":\"b\"},{\"id\":\"b\",\"parent\":\"a\"}")]
    [InlineData("{\"id\":\"root\",\"references\":{\"target\":\"missing\"}}")]
    [InlineData("{\"id\":\"Wrong\",\"parent\":\"missing\",\"references\":{\"target\":\"absent\"}}")]
    public void Invalid_ids_and_graphs_preserve_every_ordered_issue(string objects)
    {
        var text = Document(objects); var parsed = new SceneFormat().Read(text);
        var expected = new SceneValidator().Validate(parsed); Assert.True(expected.HasErrors());
        var calls = 0;
        var exception = Assert.Throws<InvalidDocumentException>(() => SceneRuntimeReader.Read(text,assetExists: _ => { calls++; return true; }));
        Assert.Equal(expected.ToArray(),exception.Issues.ToArray()); Assert.Equal(0,calls);
    }

    [Fact]
    public void Existing_named_reference_cycles_and_non_normalized_finite_TRS_are_preserved()
    {
        var text = Document("""{"id":"a","rotation":[0,0,0,2],"scale":[0,-2,3],"references":{"target":"b"}}""",
            """{"id":"b","references":{"target":"a"}}""");
        var scene = SceneRuntimeReader.Read(text).Scene;
        Assert.Equal(new Quaternion(0,0,0,2),scene.Objects[0].Rotation);
        Assert.Equal(new Vector3(0,-2,3),scene.Objects[0].Scale);
        Assert.Equal("b",scene.Objects[0].References["target"]); Assert.Equal("a",scene.Objects[1].References["target"]);
    }

    [Fact]
    public void Missing_assets_keep_placeholders_and_exact_per_object_findings_in_order()
    {
        var text = Document("""{"id":"a","asset":"missing:mesh"}""", """{"id":"b","asset":"present:mesh"}""",
            """{"id":"c","asset":"missing:mesh"}""", """{"id":"empty"}""");
        var calls = new List<string>();
        var result = SceneRuntimeReader.Read(text,assetExists: asset => { calls.Add(asset); return asset == "present:mesh"; });
        Assert.True(result.AssetsChecked); Assert.Equal(new[] { "missing:mesh", "present:mesh", "missing:mesh" },calls);
        Assert.Equal(4,result.Scene.Objects.Length); Assert.Equal("missing:mesh",result.Scene.Objects[0].Asset);
        Assert.Equal("missing:mesh",result.Scene.Objects[2].Asset);
        Assert.Equal(SceneAssets.Check(result.Scene, a => a == "present:mesh").ToArray(),result.AssetIssues.ToArray());
        Assert.Equal(new[] { "a", "c" },result.AssetIssues.Select(i => i.ObjectId)); Assert.All(result.AssetIssues,i => Assert.Equal(Severity.Error,i.Severity));
        Assert.Throws<NotSupportedException>(() => ((IList<Issue>)result.AssetIssues).Add(Issue.Error("mutation")));
    }

    [Fact]
    public void An_absent_lookup_does_not_claim_referenced_assets_exist()
    {
        var result = SceneRuntimeReader.Read(Document("""{"id":"a","asset":"unresolved:mesh"}"""));
        Assert.False(result.AssetsChecked); Assert.Empty(result.AssetIssues);
        Assert.Equal("unresolved:mesh", Assert.Single(result.Scene.Objects).Asset);
    }

    [Fact]
    public void Resolver_failure_is_not_reported_as_missing_asset()
    {
        var fault = new InvalidOperationException("resolver unavailable");
        var actual = Assert.Throws<InvalidOperationException>(() => SceneRuntimeReader.Read(Document("""{"id":"a","asset":"asset:lamp"}"""),assetExists: _ => throw fault));
        Assert.Same(fault,actual);
    }
}
