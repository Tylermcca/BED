using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Bed.Editor;
using Bed.Runtime;
namespace Bed.Tests;

public sealed class Scene2PhysicsModelTests
{
    private const string Instance="22222222222222222222222222222222",Root="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Fixture(string folder,string name)
    {var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;return File.ReadAllText(Path.Combine(repo!.FullName,"tests/Bed.Tests/Fixtures",folder,name));}
    private static JsonElement J(string json)=>JsonSerializer.Deserialize<JsonElement>(json);
    private static JsonNode Example()=>JsonNode.Parse(Fixture("Physics","physics-3d.example.json"))!;
    private static JsonNode Body(string motion="dynamic")=>JsonNode.Parse(motion=="dynamic"?"""{"motion":"dynamic","massKg":2,"friction":0.5,"shape":{"kind":"sphere","radius":1}}""":"""{"motion":"static","friction":0.5,"shape":{"kind":"sphere","radius":1}}""")!;
    private static JsonNode Prefab()
    {
        var scene=JsonNode.Parse(Fixture("Scene2","scene-3d.example.json"))!;scene["physics"]=JsonNode.Parse("""{"gravity":[0,-9.81,0],"fixedStepSeconds":0.01}""");
        scene["bases"]![0]!["definition"]!["nodes"]![0]!["body"]=Body();Rehash(scene);return scene;
    }
    private static void Rehash(JsonNode scene){var b=scene["bases"]![0]!;b["revision"]="sha256:"+Convert.ToHexStringLower(SHA256.HashData(Scene2Jcs.Canonicalize(b["definition"]!.ToJsonString())));}
    private static DocumentSession<Scene2> Session(JsonNode node)=>new(new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog(),new Scene2Format().Read(node.ToJsonString()));
    private static JsonNode Flat(Scene2 scene)=>JsonNode.Parse(new Scene2Format().Write(Scene2Kit.Flatten(scene)))!;
    private static JsonNode Item(Scene2 scene,string id)=>Flat(scene)["objects"]!.AsArray().Single(o=>o!["id"]!.GetValue<string>()==id)!;
    private static ICommand<Scene2> Set(string id,string field,JsonNode? value)=>Scene2Kit.Command("set",JsonSerializer.SerializeToElement(new{id,field,value}));
    private static void Good(DocumentSession<Scene2> s,ICommand<Scene2> command)=>Assert.True(s.Execute(command),string.Join(" ",s.LastIssues));
    [Theory][InlineData("physics-3d.example.json")][InlineData("physics-2d.example.json")]
    public void Accepted_fields_roundtrip_and_flatten_without_losing_root_physics_or_bodies(string name)
    {
        var format=new Scene2Format();var scene=format.Read(Fixture("Physics",name));var text=format.Write(scene);Assert.Equal(text,format.Write(format.Read(text)));
        var flat=Flat(scene);Assert.NotNull(flat["physics"]);Assert.NotNull(flat["objects"]![0]!["body"]);
        Assert.Equal(flat.ToJsonString(),Flat(SceneRuntimeReader.Read2(flat.ToJsonString()).Scene).ToJsonString());
    }
    [Theory][InlineData("no-physics")][InlineData("missing-mass")][InlineData("static-mass")][InlineData("friction")][InlineData("zero-radius")][InlineData("zero-height")][InlineData("shape-field")][InlineData("step")][InlineData("unknown-world")]
    public void Invalid_owned_physics_candidates_refuse_without_fallback(string change)
    {
        var scene=Example();var body=scene["objects"]![1]!["body"]!;
        switch(change)
        {
            case "no-physics":scene.AsObject().Remove("physics");break;
            case "missing-mass":body.AsObject().Remove("massKg");break;
            case "static-mass":body["motion"]="static";break;
            case "friction":body["friction"]=1.1;break;
            case "zero-radius":body["shape"]=JsonNode.Parse("""{"kind":"sphere","radius":0}""");break;
            case "zero-height":body["shape"]=JsonNode.Parse("""{"kind":"capsule-y","radius":1,"halfHeight":0}""");break;
            case "shape-field":body["shape"]!["radius"]=1;break;
            case "step":scene["physics"]!["fixedStepSeconds"]=0.2;break;
            case "unknown-world":scene["physics"]!["solver"]=1;break;
        }
        Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(scene.ToJsonString()));
    }
    [Theory][InlineData("scale")][InlineData("rotation")][InlineData("parent")][InlineData("plane")]
    public void Effective_body_transform_and_plane_rules_are_wired_into_the_complete_resolver(string change)
    {
        var scene=Example();
        if(change=="scale")scene["objects"]![1]!["scale"]=JsonNode.Parse("[1,2,1]");
        if(change=="rotation")scene["objects"]![1]!["rotation"]=JsonNode.Parse("[0,0,0,0]");
        if(change=="parent")scene["objects"]![1]!["parent"]="floor";
        if(change=="plane"){scene=JsonNode.Parse(Fixture("Physics","physics-2d.example.json"))!;scene["physics"]!["gravity"]![2]=1;}
        Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(scene.ToJsonString()));
    }
    [Fact]
    public void Expanded_body_budget_refuses_before_an_unbounded_physics_allocation()
    {
        var scene=Example();var item=scene["objects"]![0]!.DeepClone();scene["objects"]=new JsonArray(Enumerable.Range(0,4097).Select(i=>{var copy=item.DeepClone();copy["id"]="body_"+i;return copy;}).ToArray());
        Assert.Throws<DocumentFormatException>(()=>new Scene2Format().Read(scene.ToJsonString()));
    }
    [Fact]
    public void Body_override_revert_and_whole_body_motion_transition_are_atomic_and_replayable()
    {
        var s=Session(Prefab());var initial=s.TextToSave;Good(s,Set("frame_a_root","/body/friction",JsonValue.Create(.8)));
        Assert.Equal(.8,Item(s.Current,"frame_a_root")["body"]!["friction"]!.GetValue<double>(),5);var before=s.TextToSave;var count=s.History.UndoCount;
        Assert.False(s.Execute(new Scene2Set("frame_a_root","/body/massKg",null)));Assert.Equal(before,s.TextToSave);Assert.Equal(count,s.History.UndoCount);
        Assert.False(s.Execute(Set("frame_a_root","/body",Body("static"))));Assert.Equal(before,s.TextToSave);
        Good(s,Scene2Kit.Command("revert",JsonSerializer.SerializeToElement(new{instance=Instance,node=Root,field="/body/friction"})));
        Good(s,Set("frame_a_root","/body",Body("static")));Assert.Equal("static",Item(s.Current,"frame_a_root")["body"]!["motion"]!.GetValue<string>());Assert.Null(Item(s.Current,"frame_a_root")["body"]!["massKg"]);
        Assert.True(s.Undo());Assert.Equal("dynamic",Item(s.Current,"frame_a_root")["body"]!["motion"]!.GetValue<string>());Assert.True(s.Redo());
        var replay=DocumentSession<Scene2>.ReplayFrom(s.JournalBase,JournalFile.Read(JournalFile.Write(s.Journal)),new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(s.TextToSave,replay.TextToSave);
        while(s.History.UndoCount>0)Assert.True(s.Undo());Assert.Equal(initial,s.TextToSave);
    }
    [Fact]
    public void Missing_inherited_body_with_child_override_conflicts_while_a_whole_local_body_survives_source_update()
    {
        var original=Prefab();var s=Session(original);Good(s,Set("frame_a_root","/body/friction",JsonValue.Create(.8)));
        var definition=original["bases"]![0]!["definition"]!.DeepClone();definition["nodes"]![0]!.AsObject().Remove("body");
        var update=Scene2Kit.Command("update",JsonSerializer.SerializeToElement(new{instance=Instance,baseId="next",definition,newNodeIds=new{}}));var before=s.TextToSave;
        Assert.False(s.Execute(update));Assert.Equal(before,s.TextToSave);
        var whole=Session(original);Good(whole,Set("frame_a_root","/body",Body("static")));Good(whole,update);Assert.Equal("static",Item(whole.Current,"frame_a_root")["body"]!["motion"]!.GetValue<string>());
    }
    [Fact]
    public void Break_link_duplicate_group_and_rename_retain_body_and_root_metadata()
    {
        var s=Session(Prefab());Good(s,Scene2Kit.Command("break_link",JsonSerializer.SerializeToElement(new{instance=Instance})));Assert.Empty(s.Current.Instances);Assert.NotNull(Item(s.Current,"frame_a_root")["body"]);
        Good(s,Scene2Kit.Command("object.duplicate",JsonSerializer.SerializeToElement(new{ids=new[]{"frame_a_root","frame_a_marker"},prefix="copy_"})));
        Assert.Equal(2,Flat(s.Current)["objects"]!.AsArray().Count(o=>o!["body"] is not null));Assert.NotNull(Flat(s.Current)["physics"]);
        Good(s,Scene2Kit.Command("object.rename",JsonSerializer.SerializeToElement(new{id="frame_a",newId="wrapper"})));
        Good(s,Scene2Kit.Command("group",JsonSerializer.SerializeToElement(new{id="grouped",ids=new[]{"wrapper","owner"}})));
        Assert.Equal(2,Flat(s.Current)["objects"]!.AsArray().Count(o=>o!["body"] is not null));Assert.NotNull(Flat(s.Current)["physics"]);
    }
    [Fact]
    public void Paste_keeps_destination_world_settings_and_refuses_bodies_without_a_destination_world()
    {
        var fragment=Example();fragment["space"]="3d";fragment["objects"]=new JsonArray(JsonNode.Parse("""{"id":"pasted","body":{"motion":"static","friction":0.2,"shape":{"kind":"sphere","radius":1}}}"""));fragment["bases"]=new JsonArray();fragment["instances"]=new JsonArray();fragment["objectOrder"]=new JsonArray("pasted");
        var empty=JsonNode.Parse("""{"kind":"scene","version":2,"space":"3d","objects":[],"physics":{"gravity":[0,0,0],"fixedStepSeconds":0.02}}""")!;
        var s=Session(empty);Good(s,Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment})));Assert.Equal(0,Flat(s.Current)["physics"]!["gravity"]![1]!.GetValue<float>());
        empty.AsObject().Remove("physics");var noWorld=Session(empty);Assert.False(noWorld.Execute(Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment}))));Assert.Empty(noWorld.Current.Objects);
    }

    [Fact]
    public void Detached_public_values_validate_and_all_existing_positional_constructors_remain()
    {
        var scene=new Scene2(SceneSpace.ThreeD,[],[],[],null){Physics=new(new(0,-9.81f,0),.01f)};
        var body=new SceneBody(SceneBodyMotion.Dynamic,new SceneCapsuleYShape(1,2),.4f,12);
        var item=new Scene2Object(SceneObject.At("body",null,System.Numerics.Vector3.Zero),null){Body=body};
        var valid=scene with{Objects=[item]};Assert.Empty(new Scene2Validator().Validate(valid));Assert.NotEmpty(new Scene2Validator().Validate(valid with{Physics=scene.Physics with{FixedStepSeconds=float.NaN}}));
        Assert.NotEmpty(new Scene2Validator().Validate(valid with{Objects=[item with{Body=body with{Shape=new SceneBoxShape(new(-1,1,1))}}]}));
        Assert.NotEmpty(new Scene2Validator().Validate(valid with{Objects=[item with{Body=body with{Shape=new SceneMeshBoundsShape()}}]}));
    }
    [Fact]
    public void Body_geometry_defaults_and_atomic_override_mass_transition_are_preserved()
    {
        var scene=Prefab();var edits=scene["instances"]![0]!["overrides"]!.AsArray();
        edits.Add(JsonNode.Parse("""{"node":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","field":"/body/motion","op":"set","value":"static"}"""));
        edits.Add(JsonNode.Parse("""{"node":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","field":"/body/massKg","op":"unset"}"""));
        var read=new Scene2Format().Read(scene.ToJsonString());var body=Item(read,"frame_a_root")["body"]!;Assert.Equal("static",body["motion"]!.GetValue<string>());Assert.Null(body["massKg"]);Assert.Equal("[0,0,0]",body["shape"]!["center"]!.ToJsonString());
        var origin=Scene2Resolver.Resolve(read).Origins["frame_a_root"];Assert.Contains("/overrides/",origin.FieldOwners["/body/motion"]);
    }
    [Fact]
    public void Body_part_child_field_cannot_override_atomic_shape_and_placement_cannot_own_a_body()
    {
        var s=Session(Prefab());var before=s.TextToSave;Assert.False(s.Execute(Set("frame_a_root","/body/shape/radius",JsonValue.Create(2))));Assert.Equal(before,s.TextToSave);
        Assert.False(s.Execute(Set("frame_a","/body",Body())));Assert.Equal(before,s.TextToSave);
    }
    [Fact]
    public void Sample_compile_and_descriptor_load_refuse_physics_until_the_game_enables_its_runtime()
    {
        var root=Directory.CreateTempSubdirectory("physics-unsupported-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,".bed/generated/scenes"));
            var source=Fixture("Physics","physics-3d.example.json");File.WriteAllText(Path.Combine(root,"data/level.json"),source);
            const string generated=".bed/generated/scenes/level.json";File.WriteAllText(Path.Combine(root,".bed/project.json"),Scene2BuildSettings.WriteProject("{}",new("data/level.json",generated,[SceneSpace.ThreeD])));
            using(var publisher=new AssetCatalogPublisher(root))Assert.True(publisher.Publish(publisher.Prepare("empty",new Dictionary<string,byte[]>()),out _));
            using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(3,SceneStarter.Program.Run(["--compile-scene2",root],output,error));Assert.Contains("authored physics",error.ToString());Assert.False(File.Exists(Path.Combine(root,generated)));
            var text=new Scene2Format().Write(Scene2Kit.Flatten(new Scene2Format().Read(source)));File.WriteAllText(Path.Combine(root,generated),text);var assets=ProjectAssets.Open(root);
            File.WriteAllText(Path.Combine(root,".bed/generated/scenes/runtime.json"),JsonSerializer.Serialize(new{format="scene2-runtime.v1",scene=generated,sceneHash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),assetsId=assets.Id,assetsRevision=assets.Revision}));
            error.GetStringBuilder().Clear();Assert.Equal(3,SceneStarter.Program.Run(["--project",root,"--seconds","0"],output,error));Assert.Contains("authored physics",error.ToString());
        }
        finally{Directory.Delete(root,true);}
    }
}
