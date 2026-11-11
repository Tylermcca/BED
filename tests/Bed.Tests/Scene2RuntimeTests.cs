using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Runtime;
using Xunit;
namespace Bed.Tests;

public sealed class Scene2RuntimeTests
{
    private static string Fixture(string name)
    {var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;Assert.NotNull(root);return File.ReadAllText(Path.Combine(root.FullName,"tests/Bed.Tests/Fixtures/Scene2",name));}
    private static Scene2 SpritePrefab()
    {
        var definition=JsonNode.Parse(Fixture("prefab-definition.example.json"))!;definition["space"]="2d-xy";
        var node=definition["nodes"]![0]!;node["asset"]="33333333333333333333333333333333";
        node["sprite"]=JsonNode.Parse("""{"size":[2,3],"pivot":[0.25,0.75],"order":20,"filter":"nearest"}""");
        var scene=new Scene2Format().Read(Fixture("scene-2d.example.json"));
        var command=Scene2Kit.Command("instantiate",JsonSerializer.SerializeToElement(new{
            key="22222222222222222222222222222222",baseId="piece_base",definition,
            placement=new{id="piece",parent="room",position=new[]{2,3,0},scale=new[]{-2,3,1}},
            nodeIds=new Dictionary<string,string>{{"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","piece_root"},{"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","piece_marker"}},
            bindings=new Dictionary<string,string>{{"cccccccccccccccccccccccccccccccc","room"}}}));
        var result=command.Apply(scene);Assert.True(result.Ok,string.Join(" ",result.Issues));
        var changed=Scene2Kit.Command("set",JsonSerializer.SerializeToElement(new{id="piece_root",field="/properties/locked",value=true})).Apply(result.Document!);
        Assert.True(changed.Ok,string.Join(" ",changed.Issues));return changed.Document!;
    }
    [Theory][InlineData("scene-3d.example.json")][InlineData("scene-2d.example.json")]
    public void Flatten_retains_effective_metadata_order_and_local_transforms_without_mutating_authority(string fixture)
    {
        var source=new Scene2Format().Read(Fixture(fixture));Check(source);
    }
    private static void Check(Scene2 source)
    {
        var format=new Scene2Format();var before=format.Write(source);var expected=Scene2Resolver.Resolve(source);
        var flat=Scene2Kit.Flatten(source);Assert.Empty(flat.Bases);Assert.Empty(flat.Instances);Assert.Equal(source.Space,flat.Space);
        Assert.Equal(expected.Objects.Select(o=>JsonSerializer.Serialize(o)),flat.Objects.Select(o=>JsonSerializer.Serialize(o)));
        Assert.Equal(expected.Objects.Select(o=>o.Object.Id).ToArray(),flat.ObjectOrder!.Value.ToArray());
        var text=format.Write(flat);using var json=JsonDocument.Parse(text);Assert.False(json.RootElement.TryGetProperty("bases",out _));Assert.False(json.RootElement.TryGetProperty("instances",out _));
        var read=SceneRuntimeReader.Read2(text);Assert.Equal(text,format.Write(read.Scene));Assert.False(read.AssetsChecked);Assert.Empty(read.AssetIssues);
        Assert.Equal(before,format.Write(source));Assert.Throws<DocumentFormatException>(()=>SceneRuntimeReader.Read(text));
    }
    [Fact]
    public void Accepted_sprite_prefab_survives_flattening_with_world_corners_mirroring_slots_and_tie_order()
    {
        var source=SpritePrefab();Check(source);var editor=Scene2Resolver.Resolve(source).Objects;
        var runtime=SceneRuntimeReader.Read2(new Scene2Format().Write(Scene2Kit.Flatten(source))).Scene;
        var scene=new Scene(editor.Select(o=>o.Object).ToImmutableArray());
        var runtimeById=runtime.Objects.ToDictionary(o=>o.Object.Id,o=>o.Object);
        Matrix4x4 World(string id)
        {
            var item=runtimeById[id];var world=Matrix4x4.CreateScale(item.Scale)*Matrix4x4.CreateFromQuaternion(item.Rotation)*Matrix4x4.CreateTranslation(item.Position);
            while(item.Parent is {} parent){item=runtimeById[parent];world*=Matrix4x4.CreateScale(item.Scale)*Matrix4x4.CreateFromQuaternion(item.Rotation)*Matrix4x4.CreateTranslation(item.Position);}
            return world;
        }
        foreach(var item in editor.Where(o=>o.Sprite is not null))
        {
            var sprite=item.Sprite!;var expected=SpriteGeometry.Corners(sprite.Size,sprite.Pivot,SceneViewport.World(scene,item.Object.Id));
            var actual=SpriteGeometry.Corners(runtime.Objects.Single(o=>o.Object.Id==item.Object.Id).Sprite!.Size,sprite.Pivot,World(item.Object.Id));
            for(var i=0;i<4;i++)Assert.True(Vector3.Distance(expected[i],actual[i])<.00001f);
        }
        string[] Order(IEnumerable<Scene2Object> objects){var sprites=objects.Where(o=>o.Sprite is not null).ToArray();return SpriteGeometry.DrawOrder(sprites.Select(o=>o.Sprite!.Order).ToArray()).Select(i=>sprites[i].Object.Id).ToArray();}
        Assert.Equal(Order(editor),Order(runtime.Objects));Assert.Equal(Order(editor).Reverse(),Order(runtime.Objects).Reverse());
        Assert.Equal("room",runtime.Objects.Single(o=>o.Object.Id=="piece_marker").Object.References["owner"]);
        Assert.True(runtime.Objects.Single(o=>o.Object.Id=="piece_root").Object.Properties["locked"].GetBoolean());
    }
    [Fact]
    public void Asset_checks_follow_effective_order_and_keep_missing_sprite_objects()
    {
        var flat=Scene2Kit.Flatten(SpritePrefab());var calls=new List<string>();
        var read=SceneRuntimeReader.Read2(new Scene2Format().Write(flat),id=>{calls.Add(id);return false;});
        Assert.True(read.AssetsChecked);Assert.Equal(flat.Objects.Where(o=>o.Object.Asset is not null).Select(o=>o.Object.Asset),calls);
        Assert.Equal(calls.Count,read.AssetIssues.Count);Assert.Equal(flat.Objects.Length,read.Scene.Objects.Length);Assert.NotNull(read.Scene.Objects.Single(o=>o.Object.Id=="piece_root").Sprite);
        Assert.Throws<InvalidOperationException>(()=>SceneRuntimeReader.Read2(new Scene2Format().Write(flat),_=>throw new InvalidOperationException("lookup failed")));
    }
    [Theory][InlineData("bases")][InlineData("instances")]
    public void Unflattened_content_refuses_before_authoring_resolution_or_asset_lookup(string field)
    {
        var text="{\"kind\":\"scene\",\"version\":2,\"objects\":[],\""+field+"\":[{\"deliberately_bad_authoring\":true}]}";var calls=0;
        var error=Assert.Throws<DocumentFormatException>(()=>SceneRuntimeReader.Read2(text,_=>{calls++;return true;}));
        Assert.Contains("Build this authoring scene",error.Message);Assert.Contains(field,error.Path);Assert.Equal(0,calls);
    }
    [Theory]
    [InlineData("not json")][InlineData("[]")][InlineData("{\"kind\":\"scene\",\"version\":1,\"objects\":[]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"version\":2,\"objects\":[]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[],\"extra\":true}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"a\",\"parent\":\"a\"}]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"a\"}],\"objectOrder\":[\"missing\"]}")]
    [InlineData("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"a\",\"position\":[0,0,1e100]}]}")]
    public void Invalid_flat_input_refuses_before_asset_lookup(string text)
    {var calls=0;Assert.Throws<DocumentFormatException>(()=>SceneRuntimeReader.Read2(text,_=>{calls++;return true;}));Assert.Equal(0,calls);}
    [Fact]
    public void Flat_reader_enforces_byte_object_and_depth_budgets()
    {
        Assert.Throws<DocumentFormatException>(()=>SceneRuntimeReader.Read2(new string(' ',32*1024*1024+1)));
        Assert.Throws<DocumentFormatException>(()=>SceneRuntimeReader.Read2("{\"kind\":\"scene\",\"version\":2,\"objects\":["+string.Join(',',Enumerable.Repeat("{}",50001))+"]}"));
        Assert.Throws<DocumentFormatException>(()=>SceneRuntimeReader.Read2(new string('[',65)+"0"+new string(']',65)));
    }
    [Fact]
    public void A_saved_accepted_snapshot_does_not_read_a_missing_or_changed_definition_file()
    {
        var root=Directory.CreateTempSubdirectory("own-flat-source-").FullName;
        try
        {
            var path=Path.Combine(root,"piece.prefab.json");File.WriteAllText(path,Fixture("prefab-definition.example.json"));
            var source=new Scene2Format().Read(Fixture("scene-3d.example.json"));var before=Scene2Kit.Flatten(source);File.Delete(path);
            Assert.Equal(new Scene2Format().Write(before),new Scene2Format().Write(Scene2Kit.Flatten(source)));
            File.WriteAllText(path,"changed and invalid source");Assert.Equal(new Scene2Format().Write(before),new Scene2Format().Write(Scene2Kit.Flatten(source)));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
}
