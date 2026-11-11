using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class Scene2ClipboardTests
{
    private static EditorWindow<Scene2> Window(DataWorkspace w)=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
    private static void Replay(DocumentSession<Scene2> session)
    {var copy=DocumentSession<Scene2>.ReplayFrom(session.JournalBase,JournalFile.Read(JournalFile.Write(session.Journal)),new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(session.TextToSave,copy.TextToSave);Assert.Equal(session.History.UndoCount,copy.History.UndoCount);}
    [Fact]
    public void Copying_an_inherited_part_keeps_the_whole_accepted_piece_old_overrides_links_and_replay_in_one_paste_undo()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=Window(workspace);var snapshot=r.Entry.Session.Current;window.Editor.Select("frame_a_marker");Assert.True(window.Run("scene.copy"),window.Status);
        Assert.True(r.Entry.Session.Execute(Scene2Kit.Command("set",JsonSerializer.SerializeToElement(new{id="frame_a_root",field="/position",value=new[]{9,0,0}}))));var before=r.Entry.Session.TextToSave;var history=r.Entry.Session.History.UndoCount;
        Assert.True(window.Run("scene.paste"),window.Status);var scene=r.Entry.Session.Current;Assert.Equal(2,scene.Instances.Length);Assert.Single(scene.Bases);var pasted=scene.Instances[1];Assert.NotEqual(snapshot.Instances[0].Key,pasted.Key);Assert.Equal(snapshot.Instances[0].Overrides.Select(o=>$"{o.Node}:{o.Field}:{o.Operation}:{o.Value?.GetRawText()}"),pasted.Overrides.Select(o=>$"{o.Node}:{o.Field}:{o.Operation}:{o.Value?.GetRawText()}"));Assert.Equal("owner",pasted.Bindings.Values.Single());
        var expanded=Scene2Resolver.Resolve(scene).Objects;var child=expanded.Single(o=>o.Object.Id==pasted.NodeIds["bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]);Assert.Equal(pasted.NodeIds["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"],child.Object.References["frame"]);Assert.Equal("owner",child.Object.References["owner"]);Assert.Equal(history+1,r.Entry.Session.History.UndoCount);Assert.Equal(3,window.Editor.Selection.Count);
        var old=Scene2Resolver.Resolve(snapshot).Objects.Single(o=>o.Object.Id=="frame_a_root").Object;var copied=expanded.Single(o=>o.Object.Id==pasted.NodeIds["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]).Object;Assert.Equal(old.Position,copied.Position);
        var saved=r.Entry.Session.TextToSave;Assert.True(r.Entry.Session.Undo());Assert.Equal(before,r.Entry.Session.TextToSave);Assert.True(r.Entry.Session.Redo());Assert.Equal(saved,r.Entry.Session.TextToSave);Replay(r.Entry.Session);
        Assert.True(window.Run("scene.paste"),window.Status);Assert.Equal(3,r.Entry.Session.Current.Instances.Length);Assert.Single(r.Entry.Session.Current.Bases);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
    }
    [Theory][InlineData("present")][InlineData("missing")][InlineData("space")]
    public void Cross_level_paste_retains_destination_external_targets_or_refuses_atomically(string mode)
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));Window(workspace).Editor.Select("frame_a_marker");Assert.True(Window(workspace).Run("scene.copy"));
        var objects=mode=="present"?r.Entry.Session.Current.Objects:ImmutableArray<Scene2Object>.Empty;var destination=new Scene2(mode=="space"?SceneSpace.TwoDXY:SceneSpace.ThreeD,objects,[],[],null);File.WriteAllText(Path.Combine(r.Data,"other.json"),new Scene2Format().Write(destination));r.Project.Refresh();Assert.True(r.Host.BindScene("other.json","scene",out _));Assert.True(workspace.Show("other.json"));var window=Window(workspace);var before=window.Editor.Session.Current;
        Assert.Equal(mode=="present",window.Run("scene.paste"));if(mode=="present"){Assert.Single(window.Editor.Session.Current.Instances);Assert.Single(window.Editor.Session.Current.Bases);Replay(window.Editor.Session);}else {Assert.Same(before,window.Editor.Session.Current);Assert.Empty(window.Editor.Session.Journal);Assert.NotEmpty(window.Status);}
    }
    [Fact]
    public void Ordinary_sprite_subtree_preserves_geometry_reflections_properties_order_and_internal_reference_cycles()
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json",open:false);const string asset="55555555555555555555555555555555";
        var parent=SceneObject.At("parent",asset,Vector3.Zero)with{Scale=new(-2,3,1),References=ImmutableSortedDictionary<string,string>.Empty.Add("child","child")};var child=SceneObject.At("child",asset,Vector3.UnitX)with{Parent="parent",References=ImmutableSortedDictionary<string,string>.Empty.Add("parent","parent")};var sprite=new SceneSprite(new(2,3),new(.25f,.75f),17,SceneSpriteFilter.Linear);
        File.WriteAllText(r.Disk,new Scene2Format().Write(new(SceneSpace.TwoDXY,[new(parent,sprite),new(child,sprite)],[],[],null)));using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=Window(workspace);window.Editor.Select("parent");Assert.True(window.Run("scene.copy"));Assert.True(window.Run("scene.paste"),window.Status);
        var scene=window.Editor.Session.Current;Assert.Equal(4,scene.Objects.Length);var copies=scene.Objects.Where(o=>window.Editor.Selection.Contains(o.Object.Id)).ToArray();Assert.Equal(2,copies.Length);Assert.All(copies,o=>Assert.Equal(sprite,o.Sprite));var p=Assert.Single(copies,o=>o.Object.Parent is null);var c=Assert.Single(copies,o=>o.Object.Parent==p.Object.Id);Assert.Equal(c.Object.Id,p.Object.References["child"]);Assert.Equal(p.Object.Id,c.Object.References["parent"]);Assert.Equal(parent.Scale,p.Object.Scale);Assert.Equal(Vector3.UnitX,p.Object.Position);Assert.Equal(child.Position,c.Object.Position);Assert.Equal(1,window.Editor.Session.History.UndoCount);Replay(window.Editor.Session);
    }
    [Fact]
    public void Workspace_isolation_read_only_focus_and_closed_old_window_refuse_paste_without_edits()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=Window(workspace);window.Editor.Select("frame_a");Assert.False(window.Run("scene.paste"));Assert.True(window.Run("scene.copy"));var before=window.Editor.Session.Current;
        r.Blocked=true;Assert.False(window.Run("scene.paste"));r.Blocked=false;Assert.True(window.Editor.FocusOn("frame_a"));Assert.False(window.Run("scene.paste"));Assert.True(window.Editor.FocusOut());
        using var other=new Scene2HostCommandTests.Rig();using var otherWorkspace=new DataWorkspace(other.Project);Assert.True(otherWorkspace.Show("level.json"));Assert.False(Window(otherWorkspace).Run("scene.paste"));Assert.Empty(Window(otherWorkspace).Editor.Session.Journal);
        workspace.Dispose();Assert.False(window.Run("scene.copy"));Assert.False(window.Run("scene.paste"));Assert.Same(before,window.Editor.Session.Current);Assert.Empty(window.Editor.Session.Journal);
    }
    [Fact]
    public void Actual_native_copy_and_paste_shortcuts_use_the_shared_editor_action_and_one_undo_batch()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=Window(workspace);window.Editor.Select("frame_a_marker");using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,4);
        void Shortcut(ImGuiKey key){ui.Press(workspace.Draw,ImGuiKey.E,alt:true);ui.Press(workspace.Draw,ImGuiKey.Escape);ui.Press(workspace.Draw,key,ctrl:true);ui.Frames(workspace.Draw,2);}
        Shortcut(ImGuiKey.C);Assert.Empty(r.Entry.Session.Journal);Shortcut(ImGuiKey.V);Assert.Equal(2,r.Entry.Session.Current.Instances.Length);Assert.Equal(1,r.Entry.Session.History.UndoCount);Assert.True(r.Entry.Session.Undo());Assert.Single(r.Entry.Session.Current.Instances);
    }
    [Theory][InlineData("unknown")][InlineData("space")][InlineData("id")][InlineData("order")][InlineData("parent")][InlineData("reference")][InlineData("null-base")][InlineData("null-instance")][InlineData("unused-base")]
    public void Paste_command_refuses_invalid_fragment_without_partial_state_or_unhandled_nulls(string mode)
    {
        var fragment=JsonNode.Parse("""{"kind":"scene","version":2,"space":"3d","objects":[{"id":"new"}],"bases":[],"instances":[],"objectOrder":["new"]}""")!;
        switch(mode)
        {
            case "unknown":fragment["extra"]=true;break;case "space":fragment["space"]="2d_xy";break;case "id":fragment["objects"]!.AsArray().Add(fragment["objects"]![0]!.DeepClone());break;case "order":fragment["objectOrder"]=new JsonArray();break;case "parent":fragment["objects"]![0]!["parent"]="absent";break;
            case "reference":fragment["objects"]![0]!["references"]=JsonNode.Parse("""{"target":"absent"}""");break;case "null-base":fragment["bases"]!.AsArray().Add((JsonNode?)null);break;case "null-instance":fragment["instances"]!.AsArray().Add((JsonNode?)null);break;case "unused-base":fragment["bases"]!.AsArray().Add(new JsonObject{{"id","unused"}});break;
        }
        var scene=new Scene2(SceneSpace.ThreeD,[],[],[],null);var session=new DocumentSession<Scene2>(new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog(),scene);Assert.False(session.Execute(Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment}))));Assert.Same(scene,session.Current);Assert.Empty(session.Journal);Assert.NotEmpty(session.LastIssues);
    }
    [Fact]
    public void Copying_a_slot_target_with_the_piece_remaps_both_bindings_and_explicit_reference_overrides()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=Window(workspace);
        Assert.True(r.Entry.Session.Execute(Scene2Kit.Command("set",JsonSerializer.SerializeToElement(new{id="frame_a_root",field="/references/saved_target",value=new{@object="owner"}}))));window.Editor.Select("owner","frame_a_marker");Assert.True(window.Run("scene.copy"));Assert.True(window.Run("scene.paste"),window.Status);
        var scene=r.Entry.Session.Current;var instance=scene.Instances[1];var owner=scene.Objects.Single(o=>o.Object.Id!="owner");Assert.Equal(owner.Object.Id,instance.Bindings.Values.Single());var objects=Scene2Resolver.Resolve(scene).Objects;
        Assert.Equal(owner.Object.Id,objects.Single(o=>o.Object.Id==instance.NodeIds["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]).Object.References["saved_target"]);Assert.Equal(owner.Object.Id,objects.Single(o=>o.Object.Id==instance.NodeIds["bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]).Object.References["owner"]);Replay(r.Entry.Session);
    }
    [Fact]
    public void Existing_base_identity_cannot_be_reused_for_changed_accepted_content()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));var window=Window(workspace);window.Editor.Select("frame_a");Assert.True(window.Run("scene.copy"));Assert.True(window.Run("scene.paste"),window.Status);
        var top=Assert.Single(r.Entry.Session.Journal);var entry=top.Command=="scene2.paste"?top:Assert.Single(top.Batch!,e=>e.Command=="scene2.paste");var fragment=JsonNode.Parse(entry.Args!.Value.GetProperty("fragment").GetRawText())!;fragment["bases"]![0]!["revision"]=new string('0',64);var before=r.Entry.Session.Current;var count=r.Entry.Session.Journal.Count;
        Assert.False(r.Entry.Session.Execute(Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment}))));Assert.Same(before,r.Entry.Session.Current);Assert.Equal(count,r.Entry.Session.Journal.Count);Assert.Contains(r.Entry.Session.LastIssues,i=>i.Message.Contains("different accepted base",StringComparison.Ordinal));
    }
    [Fact]
    public void Paste_factory_keeps_strict_argument_shape_and_journal_registration()
    {
        Assert.Throws<DocumentFormatException>(()=>Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment=new{},unknown=true})));
        var command=Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment=new{}}));var entry=Scene2Kit.Catalog().ToEntry(command);Assert.Equal("scene2.paste",entry.Command);Assert.Equal(1,entry.Version);
    }
}
