using System.Numerics;
using Brutal.Authoring;
using SceneTool;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class SceneRuntimeConsumerTests
{
    private const string Text = """
        {"kind":"scene","version":1,"objects":[
          {"id":"child","parent":"root","asset":"11111111111111111111111111111111",
           "position":[1,2,3],"rotation":[0,0,0,2],"scale":[0,-2,3],
           "properties":{"title":"Fixture","enabled":true,"weight":2.5},"references":{"peer":"root"}},
          {"id":"root","position":[10,20,30],"references":{"peer":"child"}}
        ]}
        """;

    [Fact]
    public void Consumer_instantiates_forward_parent_and_reference_cycles_as_the_same_objects()
    {
        var source=SceneRuntimeReader.Read(Text).Scene;
        // A serializer boundary, not direct access to an editor's object instances.
        var runtime=RuntimeScene.Load(new SceneFormat().Write(source));
        Assert.Equal(new[]{"child","root"},runtime.Objects.Select(o=>o.Id));
        var child=runtime.Objects[0];var root=runtime.Objects[1];
        Assert.Same(root,child.Parent);Assert.Null(root.Parent);
        Assert.Same(root,child.References["peer"]);Assert.Same(child,root.References["peer"]);
        Assert.Equal(source.Objects[0].Asset,child.Asset);
        Assert.Equal(new Vector3(1,2,3),child.LocalPosition);Assert.Equal(new Vector3(10,20,30),root.LocalPosition);
        Assert.Equal(new Quaternion(0,0,0,2),child.LocalRotation);Assert.Equal(new Vector3(0,-2,3),child.LocalScale);
        Assert.Equal("Fixture",child.Properties["title"].GetString());Assert.True(child.Properties["enabled"].GetBoolean());
        Assert.Equal(2.5,child.Properties["weight"].GetDouble());Assert.False(runtime.AssetsChecked);
        Assert.Empty(runtime.AssetIssues);
        var second=RuntimeScene.Load(Text);Assert.NotSame(child,second.Objects[0]);
        Assert.Same(second.Objects[1],second.Objects[0].Parent);
    }

    [Fact]
    public void Missing_asset_keeps_runtime_identity_and_exact_reader_diagnostics()
    {
        var calls=new List<string>();
        var expected=SceneRuntimeReader.Read(Text,assetExists:_=>false);
        var runtime=RuntimeScene.Load(Text,assetExists:asset=>{calls.Add(asset);return false;});
        Assert.True(runtime.AssetsChecked);Assert.Equal(expected.AssetIssues.ToArray(),runtime.AssetIssues.ToArray());
        Assert.Equal(new[]{"11111111111111111111111111111111"},calls);
        Assert.Equal(calls[0],runtime.Objects[0].Asset);Assert.Same(runtime.Objects[1],runtime.Objects[0].Parent);
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[]}")]
    public void Failed_load_leaves_previous_runtime_graph_and_format_error_unchanged(string bad)
    {
        var current=RuntimeScene.Load(Text);var before=current;
        var expected=Assert.Throws<DocumentFormatException>(()=>SceneRuntimeReader.Read(bad));
        var actual=Assert.Throws<DocumentFormatException>(()=>current=RuntimeScene.Load(bad));
        Assert.Equal(expected.Path,actual.Path);Assert.Equal(expected.Message,actual.Message);
        Assert.Same(before,current);Assert.Same(current.Objects[1],current.Objects[0].Parent);
    }

    [Fact]
    public void Invalid_graph_never_instantiates_partial_objects_and_keeps_ordered_issues()
    {
        const string bad="""{"kind":"scene","version":1,"objects":[{"id":"Wrong","parent":"missing","references":{"peer":"absent"}}]}""";
        var expected=Assert.Throws<InvalidDocumentException>(()=>SceneRuntimeReader.Read(bad));
        var calls=0;var current=RuntimeScene.Load(Text);var before=current;
        var actual=Assert.Throws<InvalidDocumentException>(()=>current=RuntimeScene.Load(bad,assetExists:_=>{calls++;return true;}));
        Assert.Equal(expected.Issues.ToArray(),actual.Issues.ToArray());Assert.Equal(0,calls);Assert.Same(before,current);
    }

    [Fact]
    public void Lookup_fault_is_not_reclassified_as_missing_or_a_partial_runtime()
    {
        var fault=new InvalidOperationException("synthetic resolver fault");
        var current=RuntimeScene.Load(Text);var before=current;
        var actual=Assert.Throws<InvalidOperationException>(()=>current=RuntimeScene.Load(Text,assetExists:_=>throw fault));
        Assert.Same(fault,actual);Assert.Same(before,current);
    }
}
