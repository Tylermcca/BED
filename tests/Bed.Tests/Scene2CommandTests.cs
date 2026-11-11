using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Bed.Tests;

public sealed class Scene2CommandTests
{
    private const string Instance="22222222222222222222222222222222",Root="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",Child="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",Slot="cccccccccccccccccccccccccccccccc",Added="dddddddddddddddddddddddddddddddd";
    private static string Fixture(string name="scene-3d.example.json")
    {var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;return File.ReadAllText(Path.Combine(root!.FullName,"tests/Bed.Tests/Fixtures/Scene2",name));}
    private static JsonElement J(string text)=>JsonSerializer.Deserialize<JsonElement>(text.Replace('\'', '"'));
    private static JsonElement Map(params (string Key,string Value)[] pairs)=>JsonSerializer.SerializeToElement(pairs.ToDictionary(p=>p.Key,p=>p.Value));
    private static DocumentSession<Scene2> Session(string? text=null)=>new(new Scene2Format(),new Scene2Validator(),Scene2Commands.Catalog(),new Scene2Format().Read(text??Fixture()));
    private static Scene2Object Find(Scene2 scene,string id)=>Scene2Resolver.Resolve(scene).Objects.Single(o=>o.Object.Id==id);
    private static JsonElement Definition(Action<JsonNode>? change=null)
    {var node=JsonNode.Parse(Fixture())!["bases"]![0]!["definition"]!.DeepClone();change?.Invoke(node);return J(node.ToJsonString());}
    private static void Good(DocumentSession<Scene2> s,ICommand<Scene2> command)=>Assert.True(s.Execute(command),string.Join(" ",s.LastIssues));
    private static void Replay(DocumentSession<Scene2> s)
    {
        var text=JournalFile.Write(s.Journal);var entries=JournalFile.Read(text);
        var replay=DocumentSession<Scene2>.ReplayFrom(s.JournalBase,entries,new Scene2Format(),new Scene2Validator(),Scene2Commands.Catalog());
        Assert.Equal(s.TextToSave,replay.TextToSave);Assert.Equal(s.History.UndoCount,replay.History.UndoCount);
    }
    [Fact]
    public void Node_edit_is_explicit_even_when_equal_to_base_revert_keeps_placement_and_undo_replays()
    {
        var s=Session();var original=s.TextToSave;
        Good(s,new Scene2Set("frame_a_root","/properties/locked",J("false")));
        Assert.Single(s.Current.Instances[0].Overrides);Assert.False(Find(s.Current,"frame_a_root").Object.Properties["locked"].GetBoolean());
        Good(s,new Scene2Set("frame_a","/position",J("[8,0,0]")));
        Good(s,new Scene2Revert(Instance,Root,"/properties/locked"));Assert.Empty(s.Current.Instances[0].Overrides);
        Assert.Equal(new Vector3(8,0,0),Find(s.Current,"frame_a").Object.Position);Assert.True(s.Undo());Assert.Single(s.Current.Instances[0].Overrides);Assert.True(s.Redo());Replay(s);
        Assert.True(s.Undo());Assert.True(s.Undo());Assert.True(s.Undo());Assert.Equal(original,s.TextToSave);
    }
    [Fact]
    public void Escaped_property_and_reference_edits_own_detached_values_for_both_origins()
    {
        var s=Session();using(var doc=JsonDocument.Parse("false"))Good(s,new Scene2Set("frame_a_root","/properties/a~1b~0c",doc.RootElement));
        Assert.False(Find(s.Current,"frame_a_root").Object.Properties["a/b~c"].GetBoolean());
        Good(s,new Scene2Set("owner","/references/linked",J("{'object':'frame_a_root'}")));
        Good(s,new Scene2Set("frame_a_marker","/references/frame",J("{'object':'owner'}")));
        Assert.Equal("frame_a_root",Find(s.Current,"owner").Object.References["linked"]);Assert.Equal("owner",Find(s.Current,"frame_a_marker").Object.References["frame"]);
        Good(s,new Scene2Set("owner","/references/linked",null));Assert.Empty(Find(s.Current,"owner").Object.References);Replay(s);
    }
    [Theory]
    [InlineData("/parent","'owner'")][InlineData("/id","'renamed'")][InlineData("/position/x","1")]
    [InlineData("/properties/bad~2","true")][InlineData("/properties/x","null")][InlineData("/position","null")]
    [InlineData("/position","[1,2]")][InlineData("/references/x","'owner'")][InlineData("/references/x","{'object':'absent'}")]
    [InlineData("/sprite/order","1")]
    public void Invalid_or_structural_node_edit_is_atomic(string field,string value)
    {var s=Session();var original=s.TextToSave;Assert.False(s.Execute(new Scene2Set("frame_a_root",field,J(value))));Assert.Equal(original,s.TextToSave);Assert.Empty(s.Journal);Assert.Equal(0,s.History.UndoCount);}
    private static Scene2DuplicateInstance Copy()=>new(Instance,new string('3',32),Map(("frame_a","copy_a"),("frame_a_root","copy_root"),("frame_a_marker","copy_marker")));
    [Fact]
    public void Duplicate_maps_all_internal_targets_but_keeps_external_links_and_order()
    {
        var s=Session();Good(s,new Scene2Set("frame_a_marker","/references/local",J("{'object':'frame_a_root'}")));Good(s,Copy());
        Assert.Equal(["owner","frame_a","frame_a_root","frame_a_marker","copy_a","copy_root","copy_marker"],s.Current.ObjectOrder!.Value.ToArray());
        var copy=Find(s.Current,"copy_marker").Object;Assert.Equal("copy_root",copy.Parent);Assert.Equal("copy_root",copy.References["frame"]);Assert.Equal("copy_root",copy.References["local"]);Assert.Equal("owner",copy.References["owner"]);
        Assert.Equal(2,s.Current.Instances.Length);Assert.Single(s.Current.Bases);Assert.Equal(2,s.History.UndoCount);Replay(s);
    }
    [Theory][InlineData("{}")][InlineData("{'frame_a':'x','frame_a_root':'x','frame_a_marker':'y'}")][InlineData("{'frame_a':'owner','frame_a_root':'x','frame_a_marker':'y'}")]
    public void Bad_duplicate_map_never_partially_allocates(string map)
    {var s=Session();var original=s.TextToSave;Assert.False(s.Execute(new Scene2DuplicateInstance(Instance,new string('3',32),J(map))));Assert.Equal(original,s.TextToSave);Assert.Empty(s.Journal);}
    [Fact]
    public void Instantiate_captures_definition_maps_bindings_and_allocations_in_replay()
    {
        var s=Session("{\"kind\":\"scene\",\"version\":2,\"objects\":[{\"id\":\"owner\"}]}");
        Good(s,new Scene2Instantiate(Instance,"first",Definition(),J("{'id':'piece','position':[2,0,0]}"),Map((Root,"root"),(Child,"marker")),Map((Slot,"owner"))));
        Assert.Equal(4,Scene2Resolver.Resolve(s.Current).Objects.Length);Assert.Equal("root",Find(s.Current,"marker").Object.Parent);Assert.Equal("owner",Find(s.Current,"marker").Object.References["owner"]);Replay(s);
        Assert.True(s.Undo());Assert.Empty(s.Current.Bases);Assert.Empty(s.Current.Instances);Assert.True(s.Redo());Replay(s);
    }
    [Fact]
    public void Break_link_keeps_every_effective_field_order_and_other_instances()
    {
        var s=Session();Good(s,Copy());var before=Scene2Resolver.Resolve(s.Current);Good(s,new Scene2BreakLink(Instance));var after=Scene2Resolver.Resolve(s.Current);
        Assert.Equal(before.Objects.Select(o=>JsonSerializer.Serialize(o)),after.Objects.Select(o=>JsonSerializer.Serialize(o)));Assert.Single(s.Current.Instances);Assert.Equal(new string('3',32),s.Current.Instances[0].Key);Assert.Equal(4,s.Current.Objects.Length);Assert.Single(s.Current.Bases);
        Assert.Equal(Scene2OriginKind.Ordinary,after.Origins["frame_a_root"].Kind);Assert.True(s.Undo());Assert.Equal(2,s.Current.Instances.Length);Assert.True(s.Redo());Replay(s);
    }
    [Fact]
    public void Break_link_preserves_equal_order_sprite_ties_and_all_sprite_metadata()
    {
        var definition=Definition(d=>{d["space"]="2d-xy";var root=d["nodes"]![0]!;root["asset"]=new string('5',32);root["sprite"]=JsonSerializer.SerializeToNode(new{size=new[]{2,3},pivot=new[]{0.2,0.7},order=10,filter="linear"});});
        var s=Session(Fixture("scene-2d.example.json"));Good(s,new Scene2Instantiate(Instance,"piece_base",definition,J("{'id':'piece'}"),Map((Root,"root"),(Child,"marker")),Map((Slot,"room"))));
        Good(s,new Scene2Set("root","/sprite/pivot",J("[0.5,0.5]")));var before=Scene2Resolver.Resolve(s.Current);
        var draw=before.Objects.Select((o,i)=>(o,i)).Where(p=>p.o.Sprite is not null).OrderBy(p=>p.o.Sprite!.Order).ThenBy(p=>p.i).Select(p=>p.o.Object.Id).ToArray();
        Good(s,new Scene2BreakLink(Instance));var after=Scene2Resolver.Resolve(s.Current);
        Assert.Equal(before.Objects.Select(o=>JsonSerializer.Serialize(o)),after.Objects.Select(o=>JsonSerializer.Serialize(o)));
        Assert.Equal(draw,after.Objects.Select((o,i)=>(o,i)).Where(p=>p.o.Sprite is not null).OrderBy(p=>p.o.Sprite!.Order).ThenBy(p=>p.i).Select(p=>p.o.Object.Id));Assert.Equal(before.Objects.Select(o=>o.Sprite),after.Objects.Select(o=>o.Sprite));Assert.Equal(new Vector2(2,3),Find(s.Current,"root").Sprite!.Size);Assert.Equal(new Vector2(0.5f,0.5f),Find(s.Current,"root").Sprite!.Pivot);Assert.Empty(s.Current.Bases);Replay(s);
    }
    [Fact]
    public void Update_preserves_local_values_ids_and_survivor_order_inserts_new_nodes_and_is_one_undo()
    {
        var s=Session();var original=s.TextToSave;var definition=Definition(d=>{d["nodes"]![0]!["position"]=JsonSerializer.SerializeToNode(new[]{2,0,0});var added=d["nodes"]![1]!.DeepClone();added["key"]=Added;added["label"]="New child";d["nodes"]!.AsArray().Insert(1,added);});
        Good(s,new Scene2Update(Instance,"updated",definition,Map((Added,"new_child"))));Assert.True(Find(s.Current,"frame_a_root").Object.Properties["locked"].GetBoolean());Assert.Equal(new Vector3(2,0,0),Find(s.Current,"frame_a_root").Object.Position);
        Assert.Equal(["owner","frame_a","frame_a_root","new_child","frame_a_marker"],s.Current.ObjectOrder!.Value.ToArray());Assert.Single(s.Current.Bases);Assert.Equal(1,s.History.UndoCount);Replay(s);
        Assert.True(s.Undo());Assert.Equal(original,s.TextToSave);Assert.True(s.Redo());Replay(s);
    }
    [Theory][InlineData("overridden-node")][InlineData("bound-slot")][InlineData("property-type")][InlineData("new-required-slot")][InlineData("wrong-guid")][InlineData("bad-map")]
    public void Source_update_conflicts_keep_the_entire_accepted_draft_and_history(string mode)
    {
        var s=Session();var original=s.TextToSave;var definition=Definition(d=>
        {
            switch(mode)
            {
                case "overridden-node":d["nodes"]![0]!["key"]=Added;d["root"]=Added;d["nodes"]![1]!["parentKey"]=Added;d["nodes"]![1]!["references"]!["frame"]!["node"]=Added;break;
                case "bound-slot":d["slots"]!.AsArray().Clear();d["nodes"]![1]!["references"]!.AsObject().Remove("owner");break;
                case "property-type":d["nodes"]![0]!["properties"]!["locked"]="changed type";break;
                case "new-required-slot":d["slots"]!.AsArray().Add(JsonSerializer.SerializeToNode(new{key=Added,label="Required",required=true}));break;
                case "wrong-guid":d["guid"]=new string('f',32);break;
            }
        });
        Assert.False(s.Execute(new Scene2Update(Instance,"updated",definition,J(mode=="bad-map"?"{'extra':'id'}":"{}"))));Assert.Equal(original,s.TextToSave);Assert.Empty(s.Journal);Assert.Equal(0,s.History.UndoCount);
    }
    [Fact]
    public void Plan_is_single_owner_revision_bound_even_after_undo_and_never_writes_during_preview()
    {
        var s=Session();var original=s.TextToSave;Assert.True(Scene2EditPlan.Capture(s,new Scene2BreakLink(Instance),out var plan,out _));Assert.Equal(original,s.TextToSave);Assert.Empty(s.Journal);Assert.Empty(plan!.Candidate.Instances);
        Assert.False(plan.Commit(Session(),out _));Good(s,new Scene2Set("owner","/position",J("[1,0,0]")));Assert.True(s.Undo());Assert.Equal(original,s.TextToSave);Assert.False(plan.Commit(s,out var why));Assert.Contains("owner changed",why);
        Assert.True(Scene2EditPlan.Capture(s,new Scene2BreakLink(Instance),out var fresh,out _));Assert.True(fresh!.Commit(s,out _));Assert.False(fresh.Commit(s,out _));
    }
    [Fact]
    public void Plan_refuses_saved_baseline_conflict_without_dropping_the_draft()
    {
        var directory=Path.Combine(Path.GetTempPath(),"scene2-plan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var path=Path.Combine(directory,"scene.json");
        try
        {File.WriteAllText(path,Fixture());var s=DocumentSession<Scene2>.Open(path,new Scene2Format(),new Scene2Validator(),Scene2Commands.Catalog());var before=s.TextToSave;Assert.True(Scene2EditPlan.Capture(s,new Scene2BreakLink(Instance),out var plan,out _));File.AppendAllText(path," ");Assert.False(plan!.Commit(s,out var why));Assert.Contains("saved scene changed",why);Assert.Equal(before,s.TextToSave);Assert.Empty(s.Journal);}
        finally{Directory.Delete(directory,true);}
    }
    [Fact]
    public void Public_factory_is_strict_and_detaches_the_command_arguments()
    {
        var s=Session();ICommand<Scene2> command;
        using(var args=JsonDocument.Parse("{\"id\":\"owner\",\"position\":[1,2,3]}"))command=Scene2Kit.Command("object.move",args.RootElement);
        Good(s,command);Assert.Equal(new Vector3(1,2,3),Find(s.Current,"owner").Object.Position);
        foreach(var args in new[]{"{'id':'owner','field':'/position','value':[1,2,3],'extra':1}","{'id':'owner','id':'frame_a','field':'/position','value':[1,2,3]}","[]"})
            Assert.Throws<DocumentFormatException>(()=>Scene2Kit.Command("set",J(args)));
        Assert.Throws<DocumentFormatException>(()=>Scene2Kit.Command("unknown",J("{}")));Replay(s);
    }
    [Fact]
    public void Ordinary_sprite_compatible_move_duplicate_rename_reparent_and_remove_preserve_metadata()
    {
        var s=Session(Fixture("scene-2d.example.json"));var sprite=Find(s.Current,"sign").Sprite;
        Good(s,Scene2Kit.Command("object.move",J("{'id':'sign','position':[3,4,0]}")));
        Good(s,Scene2Kit.Command("object.duplicate",J("{'ids':['sign'],'prefix':'copy_'}")));
        Assert.Equal(sprite,Find(s.Current,"copy_sign").Sprite);Assert.Equal(5,Scene2Resolver.Resolve(s.Current).Objects.Length);
        Assert.True(s.Journal.Last().Args!.Value.GetProperty("arguments").TryGetProperty("map",out var map));Assert.Equal("copy_sign",map.GetProperty("sign").GetString());
        Good(s,Scene2Kit.Command("object.rename",J("{'id':'door','newId':'gate'}")));Assert.Equal("gate",Find(s.Current,"sign").Object.References["door"]);Assert.Equal("gate",Find(s.Current,"copy_sign").Object.References["door"]);
        Good(s,Scene2Kit.Command("object.set_parent",J("{'id':'copy_sign','parent':null}")));
        Good(s,Scene2Kit.Command("object.remove",J("{'id':'copy_sign'}")));Assert.Equal(sprite,Find(s.Current,"sign").Sprite);Assert.Equal(SceneSpace.TwoDXY,s.Current.Space);Replay(s);
    }
    [Fact]
    public void Compatible_operations_retain_bases_overrides_maps_and_remap_cross_instance_bindings_on_rename()
    {
        var s=Session();var accepted=s.Current.Bases[0].Revision;
        Good(s,Scene2Kit.Command("object.move",J("{'id':'frame_a_root','position':[3,4,5]}")));
        Good(s,Scene2Kit.Command("object.rename",J("{'id':'owner','newId':'owner_new'}")));
        Good(s,Scene2Kit.Command("object.rename",J("{'id':'frame_a','newId':'wrapper_new'}")));
        Assert.Equal(accepted,s.Current.Bases[0].Revision);Assert.Equal(2,s.Current.Instances[0].Overrides.Length);Assert.Equal("owner_new",s.Current.Instances[0].Bindings[Slot]);Assert.Equal("wrapper_new",Find(s.Current,"frame_a_root").Object.Parent);
        Assert.Equal(["owner_new","wrapper_new","frame_a_root","frame_a_marker"],s.Current.ObjectOrder!.Value.ToArray());Replay(s);
    }
    [Theory][InlineData("rename","{'id':'frame_a_root','newId':'changed'}")][InlineData("remove","{'id':'frame_a_marker'}")]
    [InlineData("duplicate","{'ids':['frame_a_root']}")][InlineData("set_parent","{'id':'frame_a_marker','parent':'owner'}")]
    public void Compatible_partial_instance_structural_edits_refuse_with_break_link_guidance(string action,string args)
    {
        var s=Session();var before=s.TextToSave;Assert.False(s.Execute(Scene2Kit.Command("object."+action,J(args))));Assert.Equal(before,s.TextToSave);Assert.Empty(s.Journal);Assert.Contains("Break Link",string.Join(" ",s.LastIssues));
    }
    [Fact]
    public void Add_and_set_sprite_commands_keep_explicit_metadata_and_one_undo_step()
    {
        var s=Session(Fixture("scene-2d.example.json"));
        Good(s,Scene2Kit.Command("add_sprite",J("{'object':{'id':'new_sprite','asset':'55555555555555555555555555555555','sprite':{'size':[2,3],'pivot':[0,1],'order':10,'filter':'nearest'}}}")));
        var count=s.History.UndoCount;
        Good(s,Scene2Kit.Command("set_sprite",J("{'id':'new_sprite','sprite':{'size':[4,5],'pivot':[1,0],'order':12,'filter':'linear'}}")));
        Assert.Equal(count+1,s.History.UndoCount);Assert.Equal(new Vector2(4,5),Find(s.Current,"new_sprite").Sprite!.Size);Assert.Equal(SceneSpriteFilter.Linear,Find(s.Current,"new_sprite").Sprite!.Filter);
        Assert.True(s.Undo());Assert.Equal(new Vector2(2,3),Find(s.Current,"new_sprite").Sprite!.Size);Assert.True(s.Redo());Replay(s);
    }
    [Fact]
    public void Grouping_a_wrapper_and_ordinary_object_preserves_world_pose_and_every_record_in_one_step()
    {
        var s=Session();var before=Scene2Resolver.Resolve(s.Current);var baseToken=s.Current.Bases[0].Revision;
        Good(s,Scene2Kit.Command("group",J("{'id':'grouped','ids':['owner','frame_a']}")));
        Assert.Equal(1,s.History.UndoCount);Assert.Equal(baseToken,s.Current.Bases[0].Revision);Assert.Single(s.Current.Instances);Assert.Equal("grouped",Find(s.Current,"frame_a").Object.Parent);Assert.Equal("grouped",Find(s.Current,"owner").Object.Parent);
        var flat=new Scene([..Scene2Resolver.Resolve(s.Current).Objects.Select(o=>o.Object)]);foreach(var item in before.Objects)
        {
            var old=Bed.Editor.SceneViewport.World(new Scene([..before.Objects.Select(o=>o.Object)]),item.Object.Id);
            var now=Bed.Editor.SceneViewport.World(flat,item.Object.Id);Assert.Equal(old,now);
        }
        Assert.Equal(before.Objects.Select(o=>o.Object.Id).Append("grouped"),s.Current.ObjectOrder!.Value);Replay(s);
    }
    [Fact]
    public void Group_and_whole_instance_removal_refuse_partial_or_external_dependencies_atomically()
    {
        var s=Session();var before=s.TextToSave;Assert.False(s.Execute(Scene2Kit.Command("group",J("{'id':'grouped','ids':['frame_a_root']}"))));Assert.Equal(before,s.TextToSave);
        Good(s,new Scene2Set("owner","/references/link",J("{'object':'frame_a_root'}")));var withLink=s.TextToSave;
        Assert.False(s.Execute(Scene2Kit.Command("remove_instance",J("{'instance':'"+Instance+"'}"))));Assert.Equal(withLink,s.TextToSave);
        Good(s,new Scene2Set("owner","/references/link",null));Good(s,Scene2Kit.Command("remove_instance",J("{'instance':'"+Instance+"'}")));Assert.Empty(s.Current.Instances);Assert.Empty(s.Current.Bases);Assert.Single(s.Current.Objects);Replay(s);
    }
    [Fact]
    public void Source_array_reordering_does_not_reorder_surviving_ids_and_deleted_unreferenced_nodes_are_pruned()
    {
        var s=Session();var oldOrder=Scene2Resolver.Resolve(s.Current).Objects.Select(o=>o.Object.Id).ToArray();
        var reordered=Definition(d=>{var nodes=d["nodes"]!.AsArray();var node=nodes[0]!;nodes.RemoveAt(0);nodes.Add(node);});
        Good(s,new Scene2Update(Instance,"reordered",reordered,J("{}")));Assert.Equal(oldOrder,s.Current.ObjectOrder!.Value.ToArray());
        var smaller=Definition(d=>d["nodes"]!.AsArray().RemoveAt(1));Good(s,new Scene2Update(Instance,"smaller",smaller,J("{}")));
        Assert.Equal(["owner","frame_a","frame_a_root"],s.Current.ObjectOrder!.Value.ToArray());Assert.Single(s.Current.Bases);Assert.Single(s.Current.Instances[0].NodeIds);Replay(s);
    }

    [Fact]
    public void Grouping_planar_mirrors_preserves_signed_scales_and_world_transforms()
    {
        var node=JsonNode.Parse(Fixture("scene-2d.example.json"))!;node["objects"]![2]!["scale"]=JsonSerializer.SerializeToNode(new[]{-1,1,1});node["objects"]![3]!["parent"]="room";
        var s=Session(node.ToJsonString());var before=Scene2Resolver.Resolve(s.Current);
        Good(s,Scene2Kit.Command("group",J("{'id':'grouped','ids':['sign','overlay']}")));
        var after=Scene2Resolver.Resolve(s.Current);
        foreach(var item in before.Objects)
        {
            var old=Bed.Editor.SceneViewport.World(new Scene([..before.Objects.Select(o=>o.Object)]),item.Object.Id);
            var now=Bed.Editor.SceneViewport.World(new Scene([..after.Objects.Select(o=>o.Object)]),item.Object.Id);
            Assert.True(Vector3.Distance(old.Translation,now.Translation)<1e-5f);Assert.Equal(old.M11,now.M11);Assert.Equal(old.M22,now.M22);
        }
        Assert.Equal(new Vector3(-1,1,1),Find(s.Current,"sign").Object.Scale);Assert.Equal(before.Objects.Select(o=>o.Sprite),Scene2Resolver.Resolve(s.Current).Objects.Take(4).Select(o=>o.Sprite));Replay(s);
    }
    [Fact]
    public void Source_deleting_an_externally_referenced_node_is_an_atomic_conflict()
    {
        var s=Session();Good(s,new Scene2Set("owner","/references/child",J("{'object':'frame_a_marker'}")));var before=s.TextToSave;var count=s.History.UndoCount;
        var definition=Definition(d=>d["nodes"]!.AsArray().RemoveAt(1));Assert.False(s.Execute(new Scene2Update(Instance,"smaller",definition,J("{}"))));Assert.Equal(before,s.TextToSave);Assert.Equal(count,s.History.UndoCount);Assert.Contains("referenced",string.Join(" ",s.LastIssues));
    }

    [Fact]
    public void Explicit_reorder_survives_break_link_and_refuses_incomplete_permutations()
    {
        var s=Session();var ids=new[]{"frame_a_marker","owner","frame_a_root","frame_a"};
        Good(s,Scene2Kit.Command("reorder",JsonSerializer.SerializeToElement(new{ids})));Good(s,new Scene2BreakLink(Instance));
        Assert.Equal(ids,Scene2Resolver.Resolve(s.Current).Objects.Select(o=>o.Object.Id));var before=s.TextToSave;
        Assert.False(s.Execute(Scene2Kit.Command("reorder",J("{'ids':['owner']}"))));Assert.Equal(before,s.TextToSave);Replay(s);
    }
    [Fact]
    public void A_successfully_saved_new_baseline_invalidates_an_older_preview()
    {
        var directory=Path.Combine(Path.GetTempPath(),"scene2-plan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var path=Path.Combine(directory,"scene.json");
        try
        {
            File.WriteAllText(path,Fixture());var s=DocumentSession<Scene2>.Open(path,new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());
            Assert.True(Scene2EditPlan.Capture(s,new Scene2BreakLink(Instance),out var plan,out _));var revision=s.Revision;
            File.WriteAllText(path,s.TextToSave);s.MarkSaved(s.TextToSave);Assert.Equal(revision,s.Revision);Assert.False(plan!.Commit(s,out var why));Assert.Contains("saved scene changed",why);Assert.Empty(s.Journal);
        }
        finally{Directory.Delete(directory,true);}
    }

    private sealed class PreviewValidator:IValidator<Scene2>
    {
        internal Action? OnValidate;
        public IReadOnlyList<Issue> Validate(Scene2 scene)
        {var callback=OnValidate;OnValidate=null;callback?.Invoke();return new Scene2Validator().Validate(scene);}
    }
    [Fact]
    public void Owner_changes_during_preview_do_not_publish_a_mismatched_candidate()
    {
        var validator=new PreviewValidator();var s=new DocumentSession<Scene2>(new Scene2Format(),validator,Scene2Kit.Catalog(),new Scene2Format().Read(Fixture()));
        validator.OnValidate=()=>Good(s,new Scene2Set("owner","/position",J("[1,0,0]")));
        Assert.False(Scene2EditPlan.Capture(s,new Scene2BreakLink(Instance),out var plan,out var why));Assert.Null(plan);Assert.Contains("during preview",why);Assert.Single(s.Current.Instances);Assert.Equal(1,s.History.UndoCount);
    }

    [Fact]
    public void Update_allocations_cannot_reuse_a_deleted_nodes_scene_identity()
    {
        var s=Session();var before=s.TextToSave;
        var definition=Definition(d=>d["nodes"]![1]!["key"]=Added);
        Assert.False(s.Execute(new Scene2Update(Instance,"changed",definition,Map((Added,"frame_a_marker")))));
        Assert.Equal(before,s.TextToSave);Assert.Empty(s.Journal);Assert.Contains("distinct and unused",string.Join(" ",s.LastIssues));
    }
}
