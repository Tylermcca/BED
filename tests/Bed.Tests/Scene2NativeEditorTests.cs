using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class Scene2NativeEditorTests
{
    private static EditorWindow<Scene2> W(DataWorkspace w)=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
    private static int Dirty(DataWorkspace w)=>(int)typeof(DataWorkspace).GetProperty("TransitionDraftCount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
    private static UiNode Button(string label)
    {var matches=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Button&&n.Label==label).ToArray();Assert.True(matches.Length==1,$"Wanted {label}; actual buttons: "+string.Join("; ",Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Button).Select(n=>$"{n.Label} {n.Bounds}")));return Assert.Single(matches);}
    private static void Click(HeadlessHost host,Action draw,string label)
    {var node=Button(label);Assert.False(node.Bounds.IsEmpty,label+" must be visible before clicking");host.Click(draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);host.Frames(draw,2);}
    [Fact]
    public void Native_v2_Inspector_creates_local_override_reset_reverts_and_save_reopen_keeps_one_authority()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"),workspace.Said);
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());
        var window=W(workspace)!;Assert.NotNull(window);Assert.Same(r.Entry.Editor,window.Editor);Assert.Null(workspace.SceneWindow);Assert.Null(r.Project.Find("level.json")!.Session);
        Assert.True(window.Editor.Select("frame_a_root"));ui.Frames(workspace.Draw,5);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("Part of a prefab copy"));
        var inspector=window.Inspector!;var row=inspector.Rows().Single(x=>x.Id=="position.0");Assert.False(row.Overridden);Assert.Contains("Comes from the prefab",row.Help);
        Assert.True(inspector.Set("position.0",2d));Assert.Equal(2,r.Entry.Session.Current.Instances[0].Overrides.Length);Assert.Equal(1,r.Entry.Session.History.UndoCount);
        Assert.Equal(2,Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_root").Object.Position.X);
        Assert.True(inspector.Rows().Single(x=>x.Id=="position.0").Overridden);ui.Frames(workspace.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.Contains("changed in this copy"));
        Assert.True(inspector.Reset("position.0"));Assert.Single(r.Entry.Session.Current.Instances[0].Overrides);Assert.Equal("/properties/locked",r.Entry.Session.Current.Instances[0].Overrides[0].Field);Assert.True(r.Entry.Session.Undo());Assert.Equal(2,r.Entry.Session.Current.Instances[0].Overrides.Length);
        Assert.True(window.Run("editor.save"),window.Status);var saved=File.ReadAllText(r.Disk);Assert.False(r.Entry.Session.Dirty);
        Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(workspace.Show("level.json"),workspace.Said);Assert.NotSame(window,W(workspace));
        Assert.Equal(saved,W(workspace)!.Editor.Session.TextToSave);Assert.Equal(2,W(workspace).Editor.Session.Current.Instances[0].Overrides.Length);
    }
    [Theory][InlineData(1400,1f)][InlineData(1024,2f)]
    public void Native_review_cancel_stale_refusal_accept_and_Break_Link_keep_explicit_update_semantics(int width,float scale)
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));
        using var ui=new HeadlessHost(width,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;W(workspace)!.Editor.Select("frame_a_root");ui.Frames(workspace.Draw,5);
        r.ChangeSource();var original=r.Entry.Session.Current;Click(ui,workspace.Draw,"Review prefab update");Assert.Same(original,r.Entry.Session.Current);Assert.Empty(r.Entry.Session.Journal);
        Click(ui,workspace.Draw,"Cancel update");Assert.Same(original,r.Entry.Session.Current);
        Click(ui,workspace.Draw,"Review prefab update");r.ChangeSource();File.AppendAllText(r.Source," ");Click(ui,workspace.Draw,"Accept update");Assert.Same(original,r.Entry.Session.Current);Assert.Empty(r.Entry.Session.Journal);
        Click(ui,workspace.Draw,"Cancel update");Click(ui,workspace.Draw,"Review prefab update");Click(ui,workspace.Draw,"Accept update");Assert.Single(r.Entry.Session.Journal);
        var linked=Scene2Resolver.Resolve(r.Entry.Session.Current);Click(ui,workspace.Draw,"Break Link");Assert.Empty(r.Entry.Session.Current.Instances);Assert.Equal(linked.Objects.Select(o=>o.Object.Id),Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Select(o=>o.Object.Id));
        Assert.True(r.Entry.Session.Undo());Assert.Single(r.Entry.Session.Current.Instances);
    }
    [Fact]
    public void Native_v2_draft_participates_in_project_transition_snapshots_and_close_decisions()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));
        var window=W(workspace)!;window.Editor.Select("frame_a_root");Assert.True(window.Inspector!.Set("position.0",4d));Assert.Equal(1,Dirty(workspace));
        Assert.True((bool)typeof(DataWorkspace).GetMethod("SaveTransitionDrafts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(workspace,null)!);Assert.Equal(0,Dirty(workspace));
        Assert.True(window.Inspector.Set("position.1",5d));var disk=File.ReadAllText(r.Disk);
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        Assert.True(window.CloseDocument!());ui.Frames(workspace.Draw,2);Click(ui,workspace.Draw,"Cancel");Assert.Same(window,W(workspace));Assert.True(r.Entry.Session.Dirty);
        Assert.True(window.CloseDocument());ui.Frames(workspace.Draw,2);Click(ui,workspace.Draw,"Don't Save");Assert.Null(workspace.Active);Assert.Equal(disk,File.ReadAllText(r.Disk));
        Assert.True(workspace.Show("level.json"));Assert.False(W(workspace)!.Editor.Session.Dirty);
    }
    [Fact]
    public void Native_sprite_rows_preserve_other_axes_and_sampling_and_refused_size_is_atomic()
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json");using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));
        var window=W(workspace)!;var sprite=Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.First(o=>o.Sprite is not null);window.Editor.Select(sprite.Object.Id);
        var inspector=window.Inspector!;Assert.True(inspector.Set("size.0",2d));var changed=Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Single(o=>o.Object.Id==sprite.Object.Id);
        Assert.Equal(new Vector2(2,sprite.Sprite!.Size.Y),changed.Sprite!.Size);Assert.Equal(sprite.Sprite.Pivot,changed.Sprite.Pivot);Assert.Equal(sprite.Sprite.Filter,changed.Sprite.Filter);
        var text=r.Entry.Session.TextToSave;var journal=r.Entry.Session.Journal.Count;Assert.False(inspector.Set("size.1",0d));Assert.Equal(text,r.Entry.Session.TextToSave);Assert.Equal(journal,r.Entry.Session.Journal.Count);
        Assert.True(r.Entry.Session.Undo());Assert.Equal(sprite.Sprite,Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Single(o=>o.Object.Id==sprite.Object.Id).Sprite);
    }
    [Fact]
    public void Flat_canvas_picks_reverse_draw_order_previews_one_snapped_move_and_Escape_cancels()
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json",open:false);
        Scene2Object Sprite(string id,int order)=>new(SceneObject.At(id,"11111111111111111111111111111111",Vector3.Zero),new(Vector2.One,new(.5f,.5f),order,SceneSpriteFilter.Nearest));
        File.WriteAllText(r.Disk,new Scene2Format().Write(new(SceneSpace.TwoDXY,[Sprite("under",0),Sprite("top",1)],[],[],null)));
        using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);var original=window.Editor.Session.Current;
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        UiNode Artwork(string name)
        {var tree=Ui.Snapshot();return Assert.Single(tree.Nodes,n=>n.Role==UiRole.ListItem&&n.Label==name&&n.Parent>=0&&tree.Nodes[n.Parent].Role==UiRole.Canvas);}
        var top=Artwork("top");var x=top.Bounds.X+top.Bounds.Width/2;var y=top.Bounds.Y+top.Bounds.Height/2;ui.Click(workspace.Draw,x,y);Assert.Equal("top",window.Editor.Primary);Assert.Empty(window.Editor.Session.Journal);
        var io=ImGui.GetIO();io.AddMousePosEvent(x,y);ui.Frame(workspace.Draw);io.AddMouseButtonEvent(0,true);ui.Frame(workspace.Draw);io.AddMousePosEvent(x+90,y);ui.Frames(workspace.Draw,2);
        Assert.Same(original,window.Editor.Session.Current);Assert.Empty(window.Editor.Session.Journal);
        io.AddMouseButtonEvent(0,false);ui.Frames(workspace.Draw,2);Assert.Single(window.Editor.Session.Journal);Assert.Equal(1,window.Editor.Session.History.UndoCount);
        var moved=window.Editor.Session.Current;Assert.Equal(new Vector3(1,0,0),Scene2Resolver.Resolve(moved).Objects.Single(o=>o.Object.Id=="top").Object.Position);Assert.Equal(original.Objects[1].Sprite,moved.Objects[1].Sprite);
        Assert.True(window.Editor.Session.Undo());Assert.Same(original,window.Editor.Session.Current);ui.Frames(workspace.Draw,2);
        top=Artwork("top");x=top.Bounds.X+top.Bounds.Width/2;y=top.Bounds.Y+top.Bounds.Height/2;io.AddMousePosEvent(x,y);ui.Frame(workspace.Draw);io.AddMouseButtonEvent(0,true);ui.Frame(workspace.Draw);io.AddMousePosEvent(x+100,y);ui.Frames(workspace.Draw,2);
        ui.Press(workspace.Draw,ImGuiKey.Escape);io.AddMouseButtonEvent(0,false);ui.Frames(workspace.Draw,2);Assert.Same(original,window.Editor.Session.Current);
        Assert.Equal(0,window.Editor.Session.History.UndoCount);
    }
    [Fact]
    public void Native_numeric_field_typing_routes_to_one_inherited_override_and_read_only_refuses()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);window.Editor.Select("frame_a_root");
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        void Type(string text)
        {var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label.StartsWith("Position X",StringComparison.Ordinal));ui.Click(workspace.Draw,field.Bounds.X+20,field.Bounds.Y+field.Bounds.Height/2);ui.Press(workspace.Draw,ImGuiKey.A,ctrl:true);ui.Type(workspace.Draw,text);ui.Press(workspace.Draw,ImGuiKey.Enter);ui.Frames(workspace.Draw,2);}
        Type("7");Assert.Equal(7,Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_root").Object.Position.X);Assert.Single(r.Entry.Session.Journal);
        r.Blocked=true;var current=r.Entry.Session.Current;Type("8");Assert.Same(current,r.Entry.Session.Current);Assert.Contains("read-only",window.Status);
    }

    [Fact]
    public void Admitted_source_list_has_stable_names_GUIDs_and_spaces_and_refuses_duplicates_or_dirty_sources()
    {
        using var r=new Scene2HostCommandTests.Rig();Assert.True(r.Host.ListPrefabSources(out var sources,out var why),why);var first=Assert.Single(sources);
        Assert.Equal("prefab.json",first.Name);Assert.Equal(Guid.ParseExact(r.Guid,"N"),first.Guid);Assert.Equal(SceneSpace.ThreeD,first.Space);
        var copy=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(r.Source))!;copy["guid"]="44444444444444444444444444444444";File.WriteAllText(Path.Combine(r.Data,"aaa.json"),copy.ToJsonString());
        Assert.True(r.Host.ListPrefabSources(out sources,out why),why);Assert.Equal(new[]{"aaa.json","prefab.json"},sources.Select(s=>s.Name));
        copy["guid"]=r.Guid;File.WriteAllText(Path.Combine(r.Data,"aaa.json"),copy.ToJsonString());Assert.False(r.Host.ListPrefabSources(out sources,out why));Assert.Empty(sources);Assert.Contains("Two prefabs share the ID",why);
        File.Delete(Path.Combine(r.Data,"aaa.json"));r.Project.Refresh();var source=r.Project.Find("prefab.json")!;Assert.True(source.Open());Assert.True(source.Session!.Execute(new SetValue("/nodes/0/label",System.Text.Json.JsonSerializer.SerializeToElement("draft label"))));
        Assert.False(r.Host.ListPrefabSources(out sources,out why));Assert.Empty(sources);Assert.Contains("unsaved",why);
    }
    [Fact]
    public void Native_named_prefab_picker_places_duplicates_and_removes_whole_pieces_through_one_session()
    {
        using var r=new Scene2HostCommandTests.Rig();var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(r.Source))!;json["slots"]=new System.Text.Json.Nodes.JsonArray();json["nodes"]![1]!["references"]!.AsObject().Remove("owner");File.WriteAllText(r.Source,json.ToJsonString());
        using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);
        using var ui=new HeadlessHost(1600,1200){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Combo&&n.Label=="Prefab"&&n.Value=="prefab.json");
        var before=r.Entry.Session.Current.Instances.Length;Click(ui,workspace.Draw,"Place prefab");Assert.Equal(before+1,r.Entry.Session.Current.Instances.Length);Assert.Single(r.Entry.Session.Journal);
        var placed=r.Entry.Session.Current.Instances.Last();window.Editor.Select(placed.Placement.Id);ui.Frames(workspace.Draw,2);Click(ui,workspace.Draw,"Duplicate prefab copy");Assert.Equal(before+2,r.Entry.Session.Current.Instances.Length);
        Click(ui,workspace.Draw,"Delete prefab copy");Assert.Equal(before+1,r.Entry.Session.Current.Instances.Length);Assert.True(r.Entry.Session.Undo());Assert.Equal(before+2,r.Entry.Session.Current.Instances.Length);
    }
    [Fact]
    public void Native_image_picker_uses_admitted_defaults_once_and_stale_selection_requires_explicit_refresh()
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json");using var workspace=new DataWorkspace(r.Project);using var catalog=new AssetCatalog();
        var assets=Directory.CreateDirectory(Path.Combine(r.Root,"assets")).FullName;
        const string id="55555555555555555555555555555555";
        void Publish(double density)
        {
            byte[] bytes=[1];var record=new AssetRecord(id,"sign.png",Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),"png",1,
                new SpriteAssetDefaults(density,new(.5f,0),SceneSpriteFilter.Linear).WithSettings(System.Text.Json.JsonSerializer.SerializeToElement(new{})),new("Fixture","self-authored","synthetic"),AiUse.Forbidden);
            Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,new byte[64*96*4],[],true,"BEDTEX1","policy",texture:new(64,96,AssetCatalog.TextureEncoding.Rgba8Srgb))],out _));
        }
        Publish(100);Assert.True(workspace.BindSceneAssets(new AssetBrowser(assets,".png"),catalog));Assert.True(workspace.Show("level.json"));
        using var ui=new HeadlessHost(1600,1200){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        Click(ui,workspace.Draw,"Add sprite");var sprite=r.Entry.Session.Current.Objects.Last();Assert.Equal(id,sprite.Object.Asset);Assert.Equal(new Vector2(.64f,.96f),sprite.Sprite!.Size);
        Publish(200);var count=r.Entry.Session.Current.Objects.Length;Click(ui,workspace.Draw,"Add sprite");Assert.Equal(count,r.Entry.Session.Current.Objects.Length);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.Contains("changed"));Click(ui,workspace.Draw,"Refresh images");Click(ui,workspace.Draw,"Add sprite");
        Assert.Equal(new Vector2(.32f,.48f),r.Entry.Session.Current.Objects.Last().Sprite!.Size);Assert.Equal(sprite.Sprite,r.Entry.Session.Current.Objects.Single(o=>o.Object.Id==sprite.Object.Id).Sprite);
    }

    [Theory][InlineData(1f)][InlineData(2f)]
    public void Native_scene_reference_and_pointer_navigation_Back_restore_selection_and_draft_at_both_scales(float scale)
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);window.Editor.Select("frame_a_marker");
        using var ui=new HeadlessHost(1400,1100){Drawing=false};ImGui.GetStyle().FontScaleMain=scale;EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        var session=r.Entry.Session;var original=session.Current;var target=Button("Go to owner: Reference target");Assert.True(target.Enabled);Assert.False(target.Bounds.IsEmpty);Click(ui,workspace.Draw,"Go to owner: Reference target");Assert.Equal("owner",window.Editor.Primary);
        Click(ui,workspace.Draw,"Back");Assert.Equal("frame_a_marker",window.Editor.Primary);Assert.Same(original,session.Current);Assert.Empty(session.Journal);
        Assert.True(workspace.GoTo(new("level.json","/objects/0/position/0")),workspace.Said);Assert.Equal("owner",window.Editor.Primary);Assert.True(workspace.Back());Assert.Equal("frame_a_marker",window.Editor.Primary);
        Assert.Same(session,W(workspace).Editor.Session);
    }
    [Fact]
    public void Stale_native_save_helper_refuses_after_authority_replacement()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var stale=W(workspace);
        Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(workspace.Show("level.json"));var current=W(workspace);current.Editor.Select("frame_a_root");Assert.True(current.Inspector!.Set("position.0",6d));
        var before=File.ReadAllText(r.Disk);Assert.False(stale.Run("editor.save"));Assert.Contains("no longer current",stale.Status);Assert.Equal(before,File.ReadAllText(r.Disk));Assert.True(current.Editor.Session.Dirty);
    }
    private sealed class PlayFactory:IProjectPlayHost
    {
        internal readonly PlayRun Run=new();internal int Starts;
        public IProjectPlaySession Start(PlayLaunch launch){Starts++;return Run;}
    }
    private sealed class PlayRun:IProjectPlaySession
    {
        public bool CanBuildAndPlay=>false;public bool HasCompleted{get;private set;}=true;public int? ExitCode=>HasCompleted?0:null;public string Status=>HasCompleted?"Stopped":"Running";
        public bool Start(bool hasUnsavedChanges,bool buildAndPlay=false){Assert.False(hasUnsavedChanges);HasCompleted=false;return true;}
        public void RequestStop()=>HasCompleted=true;public void Update(){}public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];public void Draw(){}public void Dispose()=>HasCompleted=true;
    }
    [Fact]
    public void Native_scene2_Play_uses_common_dirty_decision_and_current_saved_authority()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);window.Editor.Select("frame_a_root");
        File.WriteAllText(Path.Combine(r.Root,"game.exe"),"inert fixture: fake factory only");PlaySettingsStore.Save(r.Root,new("game.exe"));workspace.BindPlaySettings(r.Root,null,Path.Combine(r.Root,"local"));var factory=new PlayFactory();workspace.BindProjectPlay(factory);
        Assert.True(window.Inspector!.Set("position.0",3d));var disk=File.ReadAllText(r.Disk);
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,4);
        Assert.True(window.Run("editor.play.project"),window.Status);Assert.True(workspace.ProjectPlaySavePending);Assert.Equal(0,factory.Starts);Assert.True(window.Inspector.Set("position.0",4d));
        Assert.False(workspace.AnswerProjectPlay(SaveChangesChoice.Save));Assert.Contains("changed",workspace.Said);Assert.Equal(disk,File.ReadAllText(r.Disk));Assert.Equal(0,factory.Starts);
        Assert.True(workspace.RequestProjectPlay());Assert.True(workspace.AnswerProjectPlay(SaveChangesChoice.Save),workspace.Said);Assert.Equal(1,factory.Starts);Assert.False(r.Entry.Session.Dirty);Assert.NotEqual(disk,File.ReadAllText(r.Disk));
        var running=r.Entry.Session.Current;Assert.False(window.Inspector.Set("position.0",5d));Assert.Same(running,r.Entry.Session.Current);Assert.True(window.Run("editor.stop"));Assert.True(factory.Run.HasCompleted);
    }

    [Theory][InlineData("rotate")][InlineData("scale")]
    public void Flat_transform_tools_preview_then_commit_one_planar_undo_and_preserve_sprite_metadata(string tool)
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json",open:false);
        var sprite=new Scene2Object(SceneObject.At("sign","11111111111111111111111111111111",Vector3.Zero),new(Vector2.One,new(.5f,.5f),0,SceneSpriteFilter.Nearest));
        File.WriteAllText(r.Disk,new Scene2Format().Write(new(SceneSpace.TwoDXY,[sprite],[],[],null)));
        using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);
        using var ui=new HeadlessHost(1400,1100){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        var tree=Ui.Snapshot();var box=Assert.Single(tree.Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="sign"&&n.Parent>=0&&tree.Nodes[n.Parent].Role==UiRole.Canvas).Bounds;
        var cx=box.X+box.Width/2;var cy=box.Y+box.Height/2;ui.Click(workspace.Draw,cx,cy);ui.Press(workspace.Draw,tool=="rotate"?ImGuiKey.R:ImGuiKey.S);
        var original=window.Editor.Session.Current;var io=ImGui.GetIO();io.AddMousePosEvent(cx+20,cy);ui.Frame(workspace.Draw);io.AddMouseButtonEvent(0,true);ui.Frame(workspace.Draw);
        io.AddMousePosEvent(tool=="rotate"?cx:cx+40,tool=="rotate"?cy-20:cy);ui.Frames(workspace.Draw,2);Assert.Same(original,window.Editor.Session.Current);Assert.Empty(window.Editor.Session.Journal);
        io.AddMouseButtonEvent(0,false);ui.Frames(workspace.Draw,2);Assert.Single(window.Editor.Session.Journal);Assert.Equal(1,window.Editor.Session.History.UndoCount);
        var changed=window.Editor.Session.Current.Objects.Single();Assert.Equal(sprite.Sprite,changed.Sprite);Assert.Equal(0,changed.Object.Position.Z);Assert.Equal(1,changed.Object.Scale.Z);
        if(tool=="rotate"){Assert.True(MathF.Abs(changed.Object.Rotation.Z)>.6f);Assert.Equal(Vector3.One,changed.Object.Scale);}
        else Assert.Equal(new Vector3(2,2,1),changed.Object.Scale);
        Assert.True(window.Editor.Session.Undo());Assert.Same(original,window.Editor.Session.Current);
    }

    [Fact]
    public void Native_3d_viewport_nudge_edits_linked_node_without_a_flat_mutable_shadow()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        var canvas=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas&&n.Label=="Viewport");ui.Click(workspace.Draw,canvas.Bounds.X+30,canvas.Bounds.Y+30);window.Editor.Select("frame_a_root");ui.Frames(workspace.Draw,2);
        var before=r.Entry.Session.Current;var old=Scene2Resolver.Resolve(before).Objects.Single(o=>o.Object.Id=="frame_a_root").Object;
        ui.Press(workspace.Draw,ImGuiKey.RightArrow);ui.Frames(workspace.Draw,2);Assert.Single(r.Entry.Session.Journal);
        var after=Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_root").Object;Assert.NotEqual(old.Position,after.Position);Assert.Equal(old.Rotation,after.Rotation);Assert.Equal(old.Scale,after.Scale);
        Assert.Contains(r.Entry.Session.Current.Instances[0].Overrides,e=>e.Node=="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"&&e.Field=="/position");Assert.Null(workspace.SceneWindow);Assert.Null(r.Project.Find("level.json")!.Session);
        Assert.True(r.Entry.Session.Undo());Assert.Same(before,r.Entry.Session.Current);
    }

    [Fact]
    public void Native_required_slot_picker_binds_by_label_and_Revert_is_a_named_real_button()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);
        Assert.True(r.Host.ListPrefabSources(out var sources,out var why),why);var slot=Assert.Single(Assert.Single(sources).Slots);Assert.Equal("Owner",slot.Label);Assert.True(slot.Required);
        Assert.False(sources[0].Slots is PrefabSlot[]);
        using var ui=new HeadlessHost(1600,1200){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        var place=Button("Place prefab");Assert.False(place.Enabled);Assert.Contains("Owner",place.DisabledReason);
        var combo=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Combo&&n.Label=="Bind Owner (required)");ui.Click(workspace.Draw,combo.Bounds.X+20,combo.Bounds.Y+combo.Bounds.Height/2);ui.Frames(workspace.Draw,2);
        var target=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="Reference target (owner)");ui.Click(workspace.Draw,target.Bounds.X+20,target.Bounds.Y+target.Bounds.Height/2);ui.Frames(workspace.Draw,2);
        Assert.True(Button("Place prefab").Enabled);var count=r.Entry.Session.Current.Instances.Length;Click(ui,workspace.Draw,"Place prefab");Assert.Equal(count+1,r.Entry.Session.Current.Instances.Length);
        var instance=r.Entry.Session.Current.Instances.Last();Assert.Equal("owner",instance.Bindings[slot.Key]);Assert.Equal(instance.Placement.Id,window.Editor.Primary);
        window.Editor.Select(instance.NodeIds["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]);Assert.True(window.Inspector!.Set("position.0",2d));ui.Frames(workspace.Draw,2);
        Click(ui,workspace.Draw,"Revert Position X");Assert.DoesNotContain(r.Entry.Session.Current.Instances.Last().Overrides,e=>e.Field=="/position");
    }
    [Fact]
    public void Disposed_workspace_native_window_cannot_save_a_retained_draft()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=W(workspace);window.Editor.Select("frame_a_root");Assert.True(window.Inspector!.Set("position.0",5d));var disk=File.ReadAllText(r.Disk);
        workspace.Dispose();Assert.False(window.Run("editor.save"));Assert.Contains("closed",window.Status);Assert.Equal(disk,File.ReadAllText(r.Disk));Assert.False(window.Inspector.Set("position.0",6d));
    }

    private sealed class NativeAdapter:IProjectAdapter
    {
        public string Name=>"Native scene fixture";public bool Recognises(ProjectPaths paths)=>true;
        public DataProject Open(ProjectPaths paths){var p=DataProject.Open(paths.Data,paths.Schemas);Assert.True(DocumentHost.For(p).BindScene("level.json","scene",out _));return p;}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
    [Fact]
    public void Project_close_lists_native_scene2_and_data_drafts_and_disposes_stale_window_after_choices()
    {
        using var r=new Scene2HostCommandTests.Rig();File.WriteAllText(Path.Combine(r.Data,"items.json"),"{\"value\":1}");File.WriteAllText(Path.Combine(r.Root,"schemas/items.schema.json"),"""{"type":"object","properties":{"value":{"type":"integer"}}}""");
        using var ui=new HeadlessHost(1500,1100){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(r.Root,"local/recent.json"))){Loaded=[new NativeAdapter()]};Assert.True(launcher.OpenFolder(r.Root),launcher.Said);var workspace=launcher.Workspace!;
        Assert.True(workspace.Show("level.json"));var window=W(workspace);window.Editor.Select("frame_a_root");Assert.True(window.Inspector!.Set("position.0",5d));var original=File.ReadAllText(r.Disk);
        var items=workspace.Project.Find("items.json")!.Session!;Assert.True(items.Execute(new SetValue("/value",System.Text.Json.JsonSerializer.SerializeToElement(2))));
        Assert.False(launcher.Close());Assert.True(launcher.SaveChangesPending);ui.Frames(launcher.Draw,4);
        Click(ui,launcher.Draw,"Save items.json");Assert.NotNull(launcher.Workspace);Assert.False(items.Dirty);Assert.True(window.Editor.Session.Dirty);
        Click(ui,launcher.Draw,"Don't save level.json");Assert.Null(launcher.Workspace);Assert.Equal(original,File.ReadAllText(r.Disk));Assert.Equal(2,System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Data,"items.json"))).RootElement.GetProperty("value").GetInt32());
        Assert.False(window.Run("editor.save"));Assert.Equal(original,File.ReadAllText(r.Disk));
    }

    [Fact]
    public void Flat_asset_edit_refuses_a_known_model_GUID_while_retaining_the_exact_draft()
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json");using var workspace=new DataWorkspace(r.Project);using var catalog=new AssetCatalog();
        var folder=Directory.CreateDirectory(Path.Combine(r.Root,"assets")).FullName;byte[] bytes=[1];const string id="66666666666666666666666666666666";
        var record=new AssetRecord(id,"piece.obj",Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),"obj",1,System.Text.Json.JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","synthetic"),AiUse.Forbidden);
        Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,bytes,[],true,"model","policy")],out _));Assert.True(workspace.BindSceneAssets(new AssetBrowser(folder,".obj"),catalog));Assert.True(workspace.Show("level.json"));
        var window=W(workspace);var original=window.Editor.Session.Current;window.Editor.Select(original.Objects.First(o=>o.Sprite is not null).Object.Id);
        Assert.False(window.Inspector!.Set("asset",id));Assert.Same(original,window.Editor.Session.Current);Assert.Empty(window.Editor.Session.Journal);Assert.Contains(window.Editor.Refused,i=>i.Message.Contains("unsupported"));
    }

}
