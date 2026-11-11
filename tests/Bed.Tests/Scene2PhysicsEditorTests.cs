using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class Scene2PhysicsEditorTests
{
    private static JsonElement J(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
    private static Scene2 Plain()=>new(SceneSpace.ThreeD,[new(SceneObject.At("one",null,Vector3.Zero),null),new(SceneObject.At("two",null,Vector3.UnitX),null)],[],[],null);
    private static DocumentSession<Scene2> Session(Scene2? scene=null)=>new(new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog(),scene??Plain());
    private static ICommand<Scene2> Command(string action,string args)=>Scene2Kit.Command(action,J(args));
    private static void Good(DocumentSession<Scene2> session,string action,string args)=>Assert.True(session.Execute(Command(action,args)),string.Join(" ",session.LastIssues));
    private const string World="""{"physics":{"gravity":[0,-9.81,0],"fixedStepSeconds":0.01}}""";
    private const string Add="""{"edits":[{"id":"one","field":"/body","value":{"motion":"static","shape":{"kind":"box","halfExtents":[0.5,0.5,0.5]},"friction":0.5}}]}""";
    [Fact]
    public void World_and_bodies_are_explicit_atomic_replayable_and_removal_checks_the_final_scene()
    {
        var s=Session();Good(s,"set_physics",World);Good(s,"edit_bodies",Add);var before=s.Current;var history=s.History.UndoCount;
        Assert.False(s.Execute(Command("set","""{"id":"one","field":"/body/motion","value":"dynamic"}""")));Assert.Same(before,s.Current);
        Good(s,"edit_bodies","""{"edits":[{"id":"one","field":"/body/motion","value":"dynamic"},{"id":"one","field":"/body/massKg","value":4}]}""");
        Assert.Equal(history+1,s.History.UndoCount);Assert.Equal(4,s.Current.Objects[0].Body!.MassKg);Assert.False(s.Execute(Command("set_physics","{}")));
        Assert.True(s.Undo());Assert.Equal(SceneBodyMotion.Static,s.Current.Objects[0].Body!.Motion);Assert.True(s.Redo());
        Good(s,"edit_bodies","""{"edits":[{"id":"one","field":"/body/motion","value":"kinematic"},{"id":"one","field":"/body/massKg"}]}""");Assert.Null(s.Current.Objects[0].Body!.MassKg);
        Good(s,"edit_bodies","""{"edits":[{"id":"one","field":"/body"}]}""");Good(s,"set_physics","{}");Assert.Null(s.Current.Physics);
        var replay=DocumentSession<Scene2>.ReplayFrom(s.JournalBase,JournalFile.Read(JournalFile.Write(s.Journal)),new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(s.TextToSave,replay.TextToSave);
    }
    [Theory]
    [InlineData("set_physics","{\"physics\":null}")]
    [InlineData("set_physics","{\"physics\":{\"gravity\":[0,0,0],\"fixedStepSeconds\":0}}")]
    [InlineData("edit_bodies","{\"edits\":[]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"one\",\"field\":\"/asset\",\"value\":\"x\"}]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"one\",\"field\":\"/body\",\"value\":null}]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"one\",\"field\":\"/body\",\"revert\":false}]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"one\",\"field\":\"/body\",\"revert\":true}]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"one\",\"field\":\"/body/friction\",\"value\":0.2},{\"id\":\"one\",\"field\":\"/body/friction\",\"value\":0.3}]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"one\",\"field\":\"/body\"},{\"id\":\"one\",\"field\":\"/body/friction\",\"value\":0.3}]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"missing\",\"field\":\"/body\"}]}")]
    [InlineData("edit_bodies","{\"edits\":[{\"id\":\"one\",\"field\":\"/body/friction\",\"value\":0.2},{\"id\":\"two\",\"field\":\"/body/friction\",\"value\":2}]}")]
    public void Invalid_atomic_payload_leaves_exact_snapshot_and_history(string action,string args)
    {var s=Session();Good(s,"set_physics",World);Good(s,"edit_bodies",Add);var before=s.Current;var history=s.History.UndoCount;Assert.False(s.Execute(Command(action,args)));Assert.Same(before,s.Current);Assert.Equal(history,s.History.UndoCount);}
    [Theory][InlineData("set_physics","{\"extra\":1}")][InlineData("edit_bodies","{\"edits\":[],\"extra\":1}")]
    public void Factory_refuses_unknown_top_level_fields(string action,string args)=>Assert.Throws<DocumentFormatException>(()=>Command(action,args));
    private static EditorWindow<Scene2> Window(DataWorkspace w)=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
    private static void Press(HeadlessHost ui,DataWorkspace workspace,string label)
    {var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);Assert.True(node.Enabled,node.DisabledReason);Assert.False(node.Bounds.IsEmpty);var request=Ui.RequestAction(node.Id,UiActions.Invoke);ui.Frames(workspace.Draw,3);Assert.True(request.Outcome==UiActionOutcome.Applied,label+": "+request.Outcome);}
    [Theory][InlineData(1400,1f)][InlineData(1024,2f)]
    public void Native_world_add_body_motion_mass_shape_revert_save_reopen_and_guards(int width,float scale)
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);File.WriteAllText(r.Disk,new Scene2Format().Write(Plain()));using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));
        using var ui=new HeadlessHost(width,1100){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;var w=Window(workspace);var editor=w.Editor;
        foreach(var panel in editor.Panels.ToArray())editor.SetOpen(panel.Id,false);editor.SetOpen("scene2.physics",true);editor.Select("one");ui.Frames(workspace.Draw,5);var before=editor.Session.Current;Assert.Null(before.Physics);Assert.Equal(0,editor.Session.History.UndoCount);
        Press(ui,workspace,"Add physics");Assert.Equal(Vector3.Zero,editor.Session.Current.Physics!.Gravity);Press(ui,workspace,"Add body");Assert.Equal(SceneBodyMotion.Static,editor.Session.Current.Objects[0].Body!.Motion);
        var history=editor.Session.History.UndoCount;Assert.True(w.Inspector!.Set("body.motion","dynamic"));Assert.Equal(history+1,editor.Session.History.UndoCount);Assert.Equal(1,editor.Session.Current.Objects[0].Body!.MassKg);
        Assert.True(w.Inspector.Set("body.mass",3d));Assert.True(w.Inspector.Set("body.shape","capsule-y"));Assert.True(w.Inspector.Set("body.radius",.75d));Assert.True(w.Inspector.Set("body.center.0",.25d));Assert.IsType<SceneCapsuleYShape>(editor.Session.Current.Objects[0].Body!.Shape);
        var valid=editor.Session.Current;Assert.False(w.Inspector.Set("body.friction",2d));Assert.Same(valid,editor.Session.Current);Assert.True(editor.Session.Undo());Assert.True(editor.Session.Redo());Assert.True(w.Run("editor.save"),w.Status);var saved=File.ReadAllText(r.Disk);
        editor.EditRefusal=(_,_,_)=>"Test edit policy refuses physics.";ui.Frames(workspace.Draw,2);Press(ui,workspace,"Remove body");Assert.Same(valid,editor.Session.Current);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("Test edit policy refuses"));
        Assert.True(workspace.Documents.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(workspace.Show("level.json"));Assert.Equal(saved,Window(workspace).Editor.Session.TextToSave);Assert.Equal(3,Window(workspace).Editor.Session.Current.Objects[0].Body!.MassKg);
    }
    [Fact]
    public void Prefab_field_transition_and_whole_local_body_edit_revert_preserve_accepted_ownership()
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);var json=JsonNode.Parse(File.ReadAllText(r.Disk))!;json["physics"]=JsonNode.Parse(World)!["physics"]!.DeepClone();var accepted=json["bases"]![0]!;accepted["definition"]!["nodes"]![0]!["body"]=JsonNode.Parse(Add)!["edits"]![0]!["value"]!.DeepClone();accepted["revision"]="sha256:"+Convert.ToHexStringLower(SHA256.HashData(Scene2Jcs.Canonicalize(accepted["definition"]!.ToJsonString())));File.WriteAllText(r.Disk,json.ToJsonString());
        using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"),workspace.Said);var w=Window(workspace);w.Editor.Select("frame_a_root");Assert.True(w.Inspector!.Set("body.motion","dynamic"));Assert.True(w.Inspector.Reset("body.motion"));Assert.Equal(SceneBodyMotion.Static,Scene2Resolver.Resolve(w.Editor.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_root").Body!.Motion);
        Assert.True(w.Inspector.Set("body.friction",.2d));var childLocal=w.Editor.Session.Current;
        Assert.False(w.Editor.Execute(Command("edit_bodies","""{"edits":[{"id":"frame_a_root","field":"/body"}]}""")));Assert.Same(childLocal,w.Editor.Session.Current);Assert.Contains(w.Editor.Session.LastIssues,i=>i.Message.Contains("Revert body changes"));Assert.True(w.Inspector.Reset("body.friction"));
        Assert.True(w.Editor.Execute(Command("edit_bodies","""{"edits":[{"id":"frame_a_root","field":"/body","value":{"motion":"static","shape":{"kind":"sphere","radius":1},"friction":0.4}}]}""")));
        var wholeLocal=w.Editor.Session.Current;Assert.False(w.Editor.Execute(Command("edit_bodies","""{"edits":[{"id":"frame_a_root","field":"/body/friction","value":0.8}]}""")));Assert.Same(wholeLocal,w.Editor.Session.Current);Assert.Contains(w.Editor.Session.LastIssues,i=>i.Message.Contains("Revert body changes"));
        Assert.True(w.Inspector.Set("body.motion","dynamic"));Assert.True(w.Inspector.Set("body.radius",2d));Assert.Single(w.Editor.Session.Current.Instances[0].Overrides.Where(e=>e.Field.StartsWith("/body")));Assert.True(w.Inspector.Rows().Single(row=>row.Id=="body.radius").Overridden);Assert.True(w.Inspector.Reset("body.radius"));Assert.IsType<SceneBoxShape>(Scene2Resolver.Resolve(w.Editor.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_root").Body!.Shape);
    }
    [Fact]
    public void Checked_CLI_preview_save_and_stale_plan_share_commands_and_single_authority()
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);File.WriteAllText(r.Disk,new Scene2Format().Write(Plain()));var original=File.ReadAllText(r.Disk);
        using var stdout=new StringWriter();using var stderr=new StringWriter();string[] Args(params string[] flags)=>new[]{"--data",r.Data,"--schemas",Path.Combine(r.Root,"schemas"),"scene","level.json","set_physics",World}.Concat(flags).ToArray();
        Assert.Equal(0,Bed.Agent.Cli.Program.Run(Args(),TextReader.Null,stdout,stderr));Assert.Equal(original,File.ReadAllText(r.Disk));Assert.Equal(0,Bed.Agent.Cli.Program.Run(Args("--save"),TextReader.Null,new StringWriter(),stderr));Assert.NotNull(new Scene2Format().Read(File.ReadAllText(r.Disk)).Physics);
        Assert.Equal(0,Bed.Agent.Cli.Program.Run(["--data",r.Data,"--schemas",Path.Combine(r.Root,"schemas"),"scene","level.json","edit_bodies",Add,"--save"],TextReader.Null,new StringWriter(),stderr));Assert.NotNull(new Scene2Format().Read(File.ReadAllText(r.Disk)).Objects[0].Body);
        Assert.True(r.Host.OpenScene2("level.json",out var entry,out _));Assert.True(r.Host.CaptureScene2Edit("level.json","edit_bodies",J(Add),out var plan,out var why),why);Assert.True(entry!.Session.Execute(Command("object.move","""{"id":"two","position":[4,0,0]}""")));var before=entry.Session.Current;Assert.False(r.Host.CommitScene2Edit("level.json",plan!,out _));Assert.Same(before,entry.Session.Current);Assert.Equal(SceneBodyMotion.Static,entry.Session.Current.Objects[0].Body!.Motion);
    }
    [Fact]
    public void Collision_preview_is_read_only_scaled_once_and_rejects_changed_or_degenerate_model_content()
    {
        const string guid="99999999999999999999999999999999";using var catalog=new AssetCatalog();var type=typeof(DataWorkspace).Assembly.GetType("Bed.Editor.ImGui.Scene2CollisionPreview")!;var preview=Activator.CreateInstance(type,true)!;
        bool Preview(Scene2 value,AssetCatalog? assets,out object? result,out string reason)
        {object?[] args=[value,"one",assets,null,null];var good=(bool)type.GetMethod("TryGet",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(preview,args)!;result=args[3];reason=(string)args[4]!;return good;}
        static T Member<T>(object value,string name)=>(T)value.GetType().GetProperty(name)!.GetValue(value)!;
        var body=new SceneBody(SceneBodyMotion.Static,new SceneBoxShape(new(1,2,3),new(.5f,0,0)),.5f);
        var scene=Plain() with{Physics=new(Vector3.Zero,.01f),Objects=[new(SceneObject.At("one",null,new(10,0,0)) with{Scale=new(2)},null){Body=body}]};
        var original=new Scene2Format().Write(scene);Assert.True(Preview(scene,null,out var bounds,out var why),why);Assert.Equal(new Vector3(9,-4,-6),Member<IReadOnlyList<Vector3>>(bounds!,"Corners")[0]);Assert.Equal(new Vector3(13,4,6),Member<IReadOnlyList<Vector3>>(bounds!,"Corners")[7]);Assert.Equal(original,new Scene2Format().Write(scene));
        scene=scene with{Objects=[new(SceneObject.At("one",guid,Vector3.Zero),null){Body=body with{Shape=new SceneMeshBoundsShape()}}]};
        void Publish(bool flat=false)
        {
            byte[] source=[1,2,3];var record=new AssetRecord(guid,"own.obj",Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","original primitive",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            var mesh=flat?new MeshData([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]):MeshData.Cube;
            Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,source,DerivedMeshCodec.Encode(mesh),[],true,"fixture","owned")],out var said),said);
        }
        Publish();Assert.True(Preview(scene,catalog,out bounds,out why),why);Assert.Contains(catalog.Select(guid)!.ContentRevision,Member<string>(bounds!,"Status"));var old=bounds;Assert.True(Preview(scene,catalog,out bounds,out _));Assert.Same(old,bounds);
        catalog.BeginRefresh();Assert.False(Preview(scene,catalog,out bounds,out why));Assert.Null(bounds);Assert.Contains("unavailable",why);Publish(flat:true);Assert.False(Preview(scene,catalog,out bounds,out why));Assert.Contains("degenerate",why);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Native_keyboard_gravity_and_personal_bounds_reach_both_viewports_without_dirtying_on_toggle(bool flat)
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);var initial=Plain() with{Space=flat?SceneSpace.TwoDXY:SceneSpace.ThreeD,Physics=new(Vector3.Zero,.01f),Objects=[new(SceneObject.At("one",null,Vector3.Zero),null){Body=new(SceneBodyMotion.Static,new SceneSphereShape(1),.5f)}]};File.WriteAllText(r.Disk,new Scene2Format().Write(initial));
        using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var w=Window(workspace);w.Editor.Select("one");var physics=w.Editor.Panels.Single(panel=>panel.Id=="scene2.physics");var view=w.Editor.Panels.Single(panel=>panel.Id==(flat?"scene2.flat":"editor.viewport"));
        using var ui=new HeadlessHost(1600,1100){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());
        void Draw()
        {ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(650,1000));Ui.Begin("Physics controls"u8);physics.Draw();Ui.End();ImGui.SetNextWindowPos(new(660,0));ImGui.SetNextWindowSize(new(900,1000));Ui.Begin("Bounds view"u8);view.Draw();Ui.End();}
        ui.Frames(Draw,4);var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Gravity Y (m/s2)");Assert.False(field.Bounds.IsEmpty);ui.Click(Draw,field.Bounds.X+20,field.Bounds.Y+field.Bounds.Height/2);ui.Press(Draw,ImGuiKey.A,ctrl:true);ui.Type(Draw,"-4");ui.Press(Draw,ImGuiKey.Enter);Assert.Equal(-4,w.Editor.Session.Current.Physics!.Gravity.Y);Assert.Equal(1,w.Editor.Session.History.UndoCount);
        var before=w.Editor.Session.Current;var history=w.Editor.Session.History.UndoCount;var vertices=ImGui.GetDrawData().TotalVtxCount;var toggle=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Checkbox&&n.Label=="Collision bounds");var action=Ui.RequestAction(toggle.Id,UiActions.Toggle);ui.Frames(Draw,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);Assert.Same(before,w.Editor.Session.Current);Assert.Equal(history,w.Editor.Session.History.UndoCount);Assert.True(ImGui.GetDrawData().TotalVtxCount>vertices+40,"Bounds must add visible projected geometry to the native draw lists.");
        r.Blocked=true;field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Gravity Y (m/s2)");ui.Frames(Draw,2);Assert.False(Assert.Single(Ui.Snapshot().Nodes,n=>n.Id==field.Id).Enabled);Assert.Same(before,w.Editor.Session.Current);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Native_explicit_model_fit_conversion_checks_the_captured_revision_after_edit_policy(bool revoke)
    {
        const string guid="77777777777777777777777777777777";using var r=new Scene2HostCommandTests.Rig(open:false);using var catalog=new AssetCatalog();byte[] source=[1,2,3];var record=new AssetRecord(guid,"original.obj",Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","test cube",Declared:AssetOrigin.Ours),AiUse.Forbidden);Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,source,DerivedMeshCodec.Encode(MeshData.Cube),[],true,"fixture","own")],out _));
        var scene=Plain() with{Physics=new(Vector3.Zero,.01f),Objects=[new(SceneObject.At("one",guid,Vector3.Zero),null){Body=new(SceneBodyMotion.Static,new SceneMeshBoundsShape(),.5f)}]};File.WriteAllText(r.Disk,new Scene2Format().Write(scene));using var workspace=new DataWorkspace(r.Project);var assets=Directory.CreateDirectory(Path.Combine(r.Root,"assets")).FullName;Assert.True(workspace.BindSceneAssets(new AssetBrowser(assets,".obj"),catalog));Assert.True(workspace.Show("level.json"));var w=Window(workspace);w.Editor.Select("one");var panel=w.Editor.Panels.Single(p=>p.Id=="scene2.physics");
        using var ui=new HeadlessHost(900,1100){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());void Draw(){ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(850,1050));Ui.Begin("Fit controls"u8);panel.Draw();Ui.End();}ui.Frames(Draw,4);var before=w.Editor.Session.Current;if(revoke)w.Editor.EditRefusal=(_,_,_)=>{catalog.BeginRefresh();return null;};
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Use preview as explicit box");var request=Ui.RequestAction(button.Id,UiActions.Invoke);ui.Frames(Draw,3);Assert.Equal(UiActionOutcome.Applied,request.Outcome);
        if(revoke){Assert.Same(before,w.Editor.Session.Current);Assert.Empty(w.Editor.Session.Journal);}else{var fitted=Assert.IsType<SceneBoxShape>(w.Editor.Session.Current.Objects[0].Body!.Shape);Assert.Equal(new Vector3(.5f),fitted.HalfExtents);Assert.Equal(Vector3.Zero,fitted.Center);Assert.Single(w.Editor.Session.Journal);Assert.True(w.Editor.Session.Undo());Assert.IsType<SceneMeshBoundsShape>(w.Editor.Session.Current.Objects[0].Body!.Shape);}
    }

    [Fact]
    public void Atomic_body_edit_target_and_array_caps_refuse_without_a_partial_candidate()
    {
        var scene=Plain() with{Physics=new(Vector3.Zero,.01f),Objects=Enumerable.Range(0,4097).Select(i=>new Scene2Object(SceneObject.At("item_"+i,null,Vector3.Zero),null)).ToImmutableArray()};var session=Session(scene);
        var edits=new JsonArray(Enumerable.Range(0,4097).Select(i=>(JsonNode?)new JsonObject{{"id","item_"+i},{"field","/body"}}).ToArray());var before=session.Current;
        Assert.False(session.Execute(Scene2Kit.Command("edit_bodies",JsonSerializer.SerializeToElement(new JsonObject{{"edits",edits}}))));Assert.Same(before,session.Current);Assert.Empty(session.Journal);
        edits=new JsonArray(Enumerable.Range(0,20481).Select(_=>(JsonNode?)new JsonObject{{"id","item_0"},{"field","/body"}}).ToArray());Assert.False(session.Execute(Scene2Kit.Command("edit_bodies",JsonSerializer.SerializeToElement(new JsonObject{{"edits",edits}}))));Assert.Same(before,session.Current);Assert.Empty(session.Journal);
    }

    [Fact]
    public void Native_source_review_reports_body_shape_changes_and_accept_undo_keep_world_settings()
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);var json=JsonNode.Parse(File.ReadAllText(r.Disk))!;json["physics"]=JsonNode.Parse(World)!["physics"]!.DeepClone();var accepted=json["bases"]![0]!;var definition=accepted["definition"]!;definition["nodes"]![0]!["body"]=JsonNode.Parse("""{"motion":"static","shape":{"kind":"sphere","radius":1},"friction":0.5}""");accepted["revision"]="sha256:"+Convert.ToHexStringLower(SHA256.HashData(Scene2Jcs.Canonicalize(definition.ToJsonString())));File.WriteAllText(r.Disk,json.ToJsonString());File.WriteAllText(r.Source,definition.ToJsonString());
        using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var w=Window(workspace);w.Editor.Select("frame_a_root");using var ui=new HeadlessHost(1400,1100){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,4);var before=w.Editor.Session.Current;
        definition["nodes"]![0]!["body"]!["shape"]!["radius"]=2;File.WriteAllText(r.Source,definition.ToJsonString());Press(ui,workspace,"Review prefab update");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("radius",StringComparison.Ordinal));Press(ui,workspace,"Cancel update");Assert.Same(before,w.Editor.Session.Current);Press(ui,workspace,"Review prefab update");Press(ui,workspace,"Accept update");Assert.Equal(2,Assert.IsType<SceneSphereShape>(Scene2Resolver.Resolve(w.Editor.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_root").Body!.Shape).Radius);Assert.Equal(before.Physics,w.Editor.Session.Current.Physics);Assert.Single(w.Editor.Session.Journal);Assert.True(w.Editor.Session.Undo());Assert.Equal(1,Assert.IsType<SceneSphereShape>(Scene2Resolver.Resolve(w.Editor.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_root").Body!.Shape).Radius);
    }

    [Fact]
    public void Keyboard_View_menu_opens_Physics_without_changing_the_document()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var w=Window(workspace);using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,4);Assert.False(w.Editor.IsOpen("scene2.physics"));var before=w.Editor.Session.Current;
        ui.Press(workspace.Draw,ImGuiKey.V,alt:true);var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Physics");var ticket=Ui.RequestAction(item.Id,UiActions.Invoke);ui.Frames(workspace.Draw,3);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.True(w.Editor.IsOpen("scene2.physics"));Assert.Same(before,w.Editor.Session.Current);Assert.Empty(w.Editor.Session.Journal);
    }

}
