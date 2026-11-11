using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Bed.Tests;

public sealed class Scene2FormatTests
{
    private const string RootKey="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",ChildKey="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",SlotKey="cccccccccccccccccccccccccccccccc";
    private static string Fixture(string name)
    {var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;Assert.NotNull(root);return File.ReadAllText(Path.Combine(root.FullName,"tests/Bed.Tests/Fixtures/Scene2",name));}
    private static JsonNode Example()=>JsonNode.Parse(Fixture("scene-3d.example.json"))!;
    private static void Hash(JsonNode scene){var b=scene["bases"]![0]!;b["revision"]="sha256:"+Convert.ToHexStringLower(SHA256.HashData(Scene2Jcs.Canonicalize(b["definition"]!.ToJsonString())));}
    [Theory][InlineData("scene-3d.example.json")][InlineData("scene-2d.example.json")]
    public void Accepted_examples_resolve_round_trip_and_write_deterministically(string name)
    {
        var format=new Scene2Format();var scene=format.Read(Fixture(name));var resolved=Scene2Resolver.Resolve(scene);Assert.NotEmpty(resolved.Objects);Assert.Empty(new Scene2Validator().Validate(scene));
        var once=format.Write(scene);var twice=format.Write(format.Read(once));Assert.Equal(once,twice);Assert.EndsWith("\n",once);
        Assert.Equal(resolved.Objects.Select(o=>o.Object.Id),Scene2Resolver.Resolve(format.Read(once)).Objects.Select(o=>o.Object.Id));
        if(scene.Space==SceneSpace.ThreeD)
        {
            Assert.Equal(["owner","frame_a","frame_a_root","frame_a_marker"],resolved.Objects.Select(o=>o.Object.Id));
            var root=Assert.Single(resolved.Objects,o=>o.Object.Id=="frame_a_root");Assert.True(root.Object.Properties["locked"].GetBoolean());
            var marker=Assert.Single(resolved.Objects,o=>o.Object.Id=="frame_a_marker");Assert.Equal("frame_a_root",marker.Object.References["frame"]);Assert.Equal("owner",marker.Object.References["owner"]);
            Assert.Equal("/instances/0/overrides/0/value",resolved.Origins[root.Object.Id].FieldOwners["/properties/locked"]);
            Assert.Equal("/instances/0/placement/id",resolved.Origins[root.Object.Id].FieldOwners["/parent"]);
            Assert.Equal(Scene2OriginKind.Placement,resolved.Origins["frame_a"].Kind);
        }
        else Assert.Equal(new Vector2(.64f,.96f),Assert.Single(resolved.Objects,o=>o.Object.Id=="sign").Sprite!.Size);
    }
    [Fact]
    public void Optional_unbound_slots_omit_references_and_explicit_defaults_remain_hash_significant()
    {
        var json=Example();json["bases"]![0]!["definition"]!["slots"]![0]!["required"]=false;json["instances"]![0]!["bindings"]!.AsObject().Remove(SlotKey);Hash(json);
        var format=new Scene2Format();var scene=format.Read(json.ToJsonString());var marker=Scene2Resolver.Resolve(scene).Objects.Single(o=>o.Object.Id=="frame_a_marker");Assert.False(marker.Object.References.ContainsKey("owner"));
        var before=scene.Bases[0].Revision;json["bases"]![0]!["definition"]!.AsObject().Remove("space");Hash(json);Assert.NotEqual(before,json["bases"]![0]!["revision"]!.GetValue<string>());_=format.Read(json.ToJsonString());
    }
    [Fact]
    public void Escaped_scalar_reference_and_atomic_transform_overrides_and_unsets_preserve_source()
    {
        var json=Example();var edits=json["instances"]![0]!["overrides"]!.AsArray();
        JsonNode Edit(string field,string value)=>JsonNode.Parse("{\"node\":\""+RootKey+"\",\"field\":"+JsonSerializer.Serialize(field)+",\"op\":\"set\",\"value\":"+value+"}")!;
        edits.Add(Edit("/properties/a~1b~0c","42"));edits.Add(Edit("/references/owner","{\"object\":\"owner\"}"));edits.Add(Edit("/position","[1,2,3]"));
        var original=json.ToJsonString();var format=new Scene2Format();var scene=format.Read(original);var root=Scene2Resolver.Resolve(scene).Objects.Single(o=>o.Object.Id=="frame_a_root").Object;
        Assert.Equal(42,root.Properties["a/b~c"].GetInt32());Assert.Equal("owner",root.References["owner"]);Assert.Equal(new Vector3(1,2,3),root.Position);Assert.Equal(original,json.ToJsonString());
        edits.Clear();edits.Add(JsonNode.Parse("{\"node\":\""+RootKey+"\",\"field\":\"/properties/locked\",\"op\":\"unset\"}"));
        Assert.False(Scene2Resolver.Resolve(format.Read(json.ToJsonString())).Objects.Single(o=>o.Object.Id=="frame_a_root").Object.Properties.ContainsKey("locked"));
    }
    [Fact]
    public void Planar_prefab_and_sprite_override_resolve_without_filesystem_asset_reads()
    {
        var json=Example();json["space"]="2d-xy";var def=json["bases"]![0]!["definition"]!;def["space"]="2d-xy";
        def["nodes"]![0]!["asset"]=new string('3',32);def["nodes"]![0]!["sprite"]=JsonNode.Parse("{\"size\":[1,2],\"pivot\":[0.5,0],\"order\":7,\"filter\":\"nearest\"}");Hash(json);
        json["instances"]![0]!["overrides"]!.AsArray().Add(JsonNode.Parse("{\"node\":\""+RootKey+"\",\"field\":\"/sprite/size\",\"op\":\"set\",\"value\":[2,3]}"));
        var format=new Scene2Format();var scene=format.Read(json.ToJsonString());var resolved=Scene2Resolver.Resolve(scene);var root=resolved.Objects.Single(o=>o.Object.Id=="frame_a_root");
        Assert.Equal(new Vector2(2,3),root.Sprite!.Size);Assert.Equal("/instances/0/overrides/1/value",resolved.Origins[root.Object.Id].FieldOwners["/sprite/size"]);Assert.Equal(scene.Bases[0].Revision,format.Read(format.Write(scene)).Bases[0].Revision);
    }
    [Theory]
    [InlineData("hash")][InlineData("cycle")][InlineData("node-map")][InlineData("duplicate-id")][InlineData("slot")][InlineData("binding-target")][InlineData("unknown-slot")]
    [InlineData("source")][InlineData("space")][InlineData("unused")][InlineData("order")][InlineData("internal-parent")][InlineData("duplicate-override")]
    [InlineData("bad-field")][InlineData("bad-escape")][InlineData("unset-transform")][InlineData("root-parent")][InlineData("node-cycle")][InlineData("node-reference")]
    public void Corrupt_hash_graph_slots_ids_overrides_and_order_are_refused_without_partial_resolution(string mode)
    {
        var json=Example();var instance=json["instances"]![0]!;var def=json["bases"]![0]!["definition"]!;
        switch(mode)
        {
            case "hash":json["bases"]![0]!["revision"]="sha256:"+new string('0',64);break;
            case "cycle":json["objects"]![0]!["parent"]="frame_a";instance["placement"]!["parent"]="owner";break;
            case "node-map":instance["nodeIds"]!.AsObject().Remove(RootKey);break;
            case "duplicate-id":instance["nodeIds"]![RootKey]="owner";break;
            case "slot":instance["bindings"]!.AsObject().Clear();break;
            case "binding-target":instance["bindings"]![SlotKey]="absent";break;
            case "unknown-slot":instance["bindings"]![new string('d',32)]="owner";break;
            case "source":instance["source"]=new string('f',32);break;
            case "space":json["space"]="2d-xy";break;
            case "unused":json["instances"]=new JsonArray();break;
            case "order":json["objectOrder"]=new JsonArray("owner");break;
            case "internal-parent":json["objects"]![0]!["parent"]="frame_a_root";break;
            case "duplicate-override":instance["overrides"]!.AsArray().Add(instance["overrides"]![0]!.DeepClone());break;
            case "bad-field":instance["overrides"]![0]!["field"]="/parent";break;
            case "bad-escape":instance["overrides"]![0]!["field"]="/properties/a~2";break;
            case "unset-transform":instance["overrides"]![0]!["field"]="/position";instance["overrides"]![0]!["op"]="unset";instance["overrides"]![0]!.AsObject().Remove("value");break;
            case "root-parent":def["nodes"]![0]!["parentKey"]=ChildKey;Hash(json);break;
            case "node-cycle":def["nodes"]![1]!["parentKey"]=ChildKey;Hash(json);break;
            case "node-reference":def["nodes"]![1]!["references"]!["frame"]!["node"]=new string('d',32);Hash(json);break;
        }
        var error=Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(json.ToJsonString()));Assert.NotEmpty(error.Path);
    }
    [Theory]
    [InlineData("position")][InlineData("rotation")][InlineData("scale")][InlineData("properties")][InlineData("references")][InlineData("sprite")][InlineData("asset")]
    public void Explicit_null_never_means_an_omitted_default(string field)
    {Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"a\",\""+field+"\":null}]}"));}
    [Theory]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[],\"extra\":1}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"version\":2,\"objects\":[]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"a\",\"properties\":{\"x\":1,\"x\":2}}]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"a\",\"position\":[1e40,0,0]}]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"a\",\"properties\":{\"n\":1e400}}]}")]
    [InlineData("{\"kind\":\"custom\",\"version\":2,\"objects\":[]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":3,\"objects\":[]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"space\":null,\"objects\":[]}")]
    public void Strict_shape_version_duplicates_and_numeric_refusals_are_path_aware(string json)
    {var error=Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(json));Assert.NotEmpty(error.Path);}
    [Theory][InlineData("z")][InlineData("rotation")][InlineData("scale")][InlineData("size")][InlineData("pivot")][InlineData("image")][InlineData("pair")]
    public void Planar_constraints_and_positive_float32_sprite_sizes_are_never_silently_fixed(string mode)
    {
        var json=JsonNode.Parse(Fixture("scene-2d.example.json"))!;var sprite=json["objects"]![2]!;
        switch(mode)
        {case "z":sprite["position"]![2]=1;break;case "rotation":sprite["rotation"]![3]=.5;break;case "scale":sprite["scale"]![0]=0;break;case "size":sprite["sprite"]!["size"]![0]=1e-100;break;case "pivot":sprite["sprite"]!["pivot"]![0]=2;break;case "image":sprite["asset"]="image.png";break;case "pair":sprite.AsObject().Remove("sprite");break;}
        Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(json.ToJsonString()));
    }
    [Fact]
    public void Count_preflight_budgets_and_v1_dispatch_are_separate_and_v1_write_is_unchanged()
    {
        var format=new Scene2Format();var scene=format.Read(Fixture("scene-3d.example.json"));
        var many=scene with{Instances=Enumerable.Repeat(scene.Instances[0],10001).ToImmutableArray()};Assert.Contains("budget",Assert.Throws<DocumentFormatException>(()=>Scene2Resolver.Resolve(many)).Message);
        var objects=Enumerable.Range(0,49999).Select(i=>new Scene2Object(SceneObject.At("o"+i,null,Vector3.Zero),null)).ToImmutableArray();
        Assert.Contains("expanded",Assert.Throws<DocumentFormatException>(()=>Scene2Resolver.Resolve(scene with{Objects=objects})).Message);
        var oversized="{\"kind\":\"scene\",\"version\":2,\"objects\":["+string.Join(',',Enumerable.Repeat("{\"id\":\"a\"}",50001))+"]}";Assert.Contains("50000",Assert.Throws<DocumentFormatException>(()=>format.Read(oversized)).Message);
        const string old="{\"kind\":\"scene\",\"version\":1,\"objects\":[{\"id\":\"a\",\"scale\":[0,-1,1],\"rotation\":[0,0,0,2]}]}";
        var v1=new SceneFormat();var before=v1.Write(v1.Read(old));Assert.IsType<Scene>(SceneVersionDispatch.Read(old));Assert.IsType<Scene>(SceneVersionDispatch.Read(old.Replace("scene","custom",StringComparison.Ordinal)));Assert.Equal(before,v1.Write(v1.Read(old)));
        Assert.IsType<Scene2>(SceneVersionDispatch.Read("{\"kind\":\"scene\",\"version\":2,\"objects\":[]}"));Assert.Throws<DocumentFormatException>(()=>v1.Read("{\"kind\":\"scene\",\"version\":2,\"objects\":[]}"));
    }
    [Fact]
    public void Standalone_definition_round_trip_and_long_parent_chain_remain_immutable()
    {
        var format=new PrefabDefinitionFormat();var definition=format.Read(Fixture("prefab-definition.example.json"));Assert.Empty(new PrefabDefinitionValidator().Validate(definition));Assert.Equal(format.Write(definition),format.Write(format.Read(format.Write(definition))));
        var nodes=new JsonArray();for(var i=0;i<2000;i++){var o=new JsonObject{{"id","n"+i}};if(i>0)o.Add("parent","n"+(i-1));nodes.Add(o);}
        var scene=new JsonObject{{"kind","scene"},{"version",2},{"objects",nodes}};Assert.Equal(2000,Scene2Resolver.Resolve(new Scene2Format().Read(scene.ToJsonString())).Objects.Length);
    }
    [Fact]
    public void Constructors_detach_json_lifetimes_and_typed_candidates_refuse_invalid_unicode()
    {
        Scene2Object item;PrefabOverride edit;
        using(var source=JsonDocument.Parse("{\"value\":\"Own text\"}"))
        {
            var properties=ImmutableSortedDictionary<string,JsonElement>.Empty.Add("name",source.RootElement.GetProperty("value"));
            item=new(SceneObject.At("own",null,Vector3.Zero) with{Properties=properties},null);
            edit=new(RootKey,"/properties/name",PrefabOverrideOperation.Set,source.RootElement.GetProperty("value"));
        }
        Assert.Equal("Own text",item.Object.Properties["name"].GetString());Assert.Equal("Own text",edit.Value!.Value.GetString());
        var scene=new Scene2(SceneSpace.ThreeD,[item],[],[],null);Assert.Empty(new Scene2Validator().Validate(scene));
        var corrupt=item.Object with{Properties=ImmutableSortedDictionary<string,JsonElement>.Empty.Add("\ud800",JsonSerializer.SerializeToElement(true))};
        Assert.NotEmpty(new Scene2Validator().Validate(scene with{Objects=[new(corrupt,null)]}));
    }
    [Theory][InlineData("bases")][InlineData("overrides")][InlineData("nodes")][InlineData("slots")]
    public void Named_count_budgets_refuse_before_graph_or_hash_work(string area)
    {
        var scene=Example();
        if(area=="bases")scene["bases"]=new JsonArray(Enumerable.Range(0,513).Select(_=>scene["bases"]![0]!.DeepClone()).ToArray());
        if(area=="overrides")scene["instances"]![0]!["overrides"]=new JsonArray(Enumerable.Range(0,10001).Select(_=>scene["instances"]![0]!["overrides"]![0]!.DeepClone()).ToArray());
        if(area is "nodes" or "slots")
        {var definition=scene["bases"]![0]!["definition"]!;definition[area]=new JsonArray(Enumerable.Range(0,area=="nodes"?10001:257).Select(_=>definition[area]![0]!.DeepClone()).ToArray());}
        var error=Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(scene.ToJsonString()));Assert.Contains(area,error.Path);
    }
    [Fact]
    public void Byte_depth_and_unicode_budgets_refuse_plainly()
    {
        Assert.Throws<DocumentFormatException>(()=>new PrefabDefinitionFormat().Read(new string(' ',4*1024*1024+1)));
        Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(new string(' ',32*1024*1024+1)));
        Assert.Throws<DocumentFormatException>(()=>Scene2Read.Parse("{\"x\":\"€€€\"}",16));
        Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read("{\"kind\":\"scene\",\"version\":2,\"objects\":[],\"extra\":"+new string('[',65)+"0"+new string(']',65)+"}"));
    }

    [Fact]
    public void Three_dimensional_loose_finite_transforms_remain_unchanged_and_invalid_candidates_report_issues()
    {
        const string json="{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"own\",\"scale\":[0,-1,1],\"rotation\":[0,0,0,2]}]}";
        var format=new Scene2Format();var scene=format.Read(json);var saved=format.Read(format.Write(scene));Assert.Equal(new Vector3(0,-1,1),saved.Objects[0].Object.Scale);Assert.Equal(2,saved.Objects[0].Object.Rotation.W);
        var bad=scene with{Objects=[new(SceneObject.At("bad",null,new(float.PositiveInfinity,0,0)),null)]};Assert.NotEmpty(new Scene2Validator().Validate(bad));Assert.Throws<DocumentFormatException>(()=>format.Write(bad));
    }

    [Fact]
    public void Diagnostic_paths_name_the_instance_field_that_owns_an_invalid_value()
    {
        foreach(var mode in new[]{"base","binding","id"})
        {
            var json=Example();var instance=json["instances"]![0]!;string expected;
            if(mode=="base"){instance["base"]="missing";expected="/instances/0/base";}
            else if(mode=="binding"){instance["bindings"]![SlotKey]="missing";expected="/instances/0/bindings/"+SlotKey;}
            else{instance["nodeIds"]![RootKey]="owner";expected="/instances/0/nodeIds/"+RootKey;}
            Assert.Equal(expected,Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(json.ToJsonString())).Path);
        }
    }

}
