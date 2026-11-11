using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneGroupUi15Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("scene-group-ui15-").FullName;
        public readonly HeadlessHost Host=new(1400,1100){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly Scene Original;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string Disk=>Path.Combine(Root,"data","level.json");
        public Rig()
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            var frame=SceneObject.At("frame",null,new(5,0,7)) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.6f),Scale=new(2,2,2)};
            var first=SceneObject.At("first",null,new(-2,0,0)) with{Parent="frame"};first=first with{References=first.References.Add("partner","second")};
            Original=new([frame,first,SceneObject.At("second",null,new(4,0,0)) with{Parent="frame"},SceneObject.At("descendant",null,new(0,1,0)) with{Parent="first"},SceneObject.At("group",null,Vector3.Zero),SceneObject.At("group_2",null,Vector3.Zero)]);
            File.WriteAllText(Disk,new SceneFormat().Write(Original));Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));Assert.True(Workspace.Show("level.json"));
            EditorWindow<Scene>.Configure(ImGui.GetIO());Assert.True(Window.Editor.Select("first","second"));Host.Frames(Workspace.Draw,4);
        }
        public void Menu(){Host.Press(Workspace.Draw,ImGuiKey.E,alt:true);Host.Frames(Workspace.Draw,2);}
        public void Native(bool ungroup=false)
        {Menu();Host.Press(Workspace.Draw,ImGuiKey.Escape);Host.Frames(Workspace.Draw,2);Host.Press(Workspace.Draw,ImGuiKey.G,ctrl:true,shift:ungroup);Host.Frames(Workspace.Draw,2);}
        public void MenuAction(string label)
        {
            Menu();var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label==label);Assert.True(node.Enabled,node.DisabledReason);
            var action=Ui.RequestAction(node.Id,UiActions.Invoke);Host.Frames(Workspace.Draw,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);
        }
        public void PoseKept(Scene expected,Scene actual)
        {foreach(var o in expected.Objects)Assert.True(Distance(SceneViewport.World(expected,o.Id),SceneViewport.World(actual,o.Id))<1e-4f,"World pose changed: "+o.Id);}
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    private static float Distance(Matrix4x4 a,Matrix4x4 b)=>MathF.Abs(a.M11-b.M11)+MathF.Abs(a.M12-b.M12)+MathF.Abs(a.M13-b.M13)+MathF.Abs(a.M21-b.M21)+MathF.Abs(a.M22-b.M22)+MathF.Abs(a.M23-b.M23)+MathF.Abs(a.M31-b.M31)+MathF.Abs(a.M32-b.M32)+MathF.Abs(a.M33-b.M33)+Vector3.Distance(a.Translation,b.Translation);
    [Theory][InlineData(false)][InlineData(true)]
    public void Keyboard_and_Edit_group_at_selected_world_centre_then_ungroup_with_one_undo_each_and_valid_saved_level(bool menu)
    {
        using var r=new Rig();var original=File.ReadAllText(r.Disk);var expectedCentre=(SceneViewport.World(r.Original,"first").Translation+SceneViewport.World(r.Original,"second").Translation)/2;
        if(menu)r.MenuAction("Group");else r.Native();var editor=r.Window.Editor;var grouped=editor.Session.Current;var groupId=Assert.Single(editor.Selection);Assert.Equal("group_3",groupId);
        var group=grouped.Find(groupId)!;Assert.Null(group.Asset);Assert.Equal("frame",group.Parent);Assert.Equal(7,grouped.Objects.Length);Assert.Equal(1,editor.Session.History.UndoCount);
        Assert.True(Vector3.Distance(expectedCentre,SceneViewport.World(grouped,groupId).Translation)<1e-5f);Assert.Equal(groupId,grouped.Find("first")!.Parent);Assert.Equal(groupId,grouped.Find("second")!.Parent);r.PoseKept(r.Original,grouped);
        Assert.Equal("second",grouped.Find("first")!.References["partner"]);Assert.Equal(original,File.ReadAllText(r.Disk));Assert.True(r.Window.Run("editor.save"));Assert.Equal(new SceneFormat().Write(grouped),File.ReadAllText(r.Disk));
        Assert.True(editor.Session.Undo());Assert.Equal(original,new SceneFormat().Write(editor.Session.Current));Assert.True(editor.Session.Redo());Assert.Same(grouped,editor.Session.Current);editor.Select(groupId);
        if(menu)r.MenuAction("Ungroup");else r.Native(ungroup:true);var ungrouped=editor.Session.Current;Assert.Null(ungrouped.Find(groupId));Assert.Equal(6,ungrouped.Objects.Length);Assert.Equal(2,editor.Session.History.UndoCount);
        Assert.Equal(new[]{"first","second"},editor.Selection);Assert.Equal("frame",ungrouped.Find("first")!.Parent);Assert.Equal("frame",ungrouped.Find("second")!.Parent);foreach(var originalObject in r.Original.Objects)Assert.True(Distance(SceneViewport.World(grouped,originalObject.Id),SceneViewport.World(ungrouped,originalObject.Id))<1e-4f);
        Assert.True(r.Window.Run("editor.save"));Assert.Empty(new SceneValidator().Validate(new SceneFormat().Read(File.ReadAllText(r.Disk))));Assert.True(editor.Session.Undo());Assert.Equal(new SceneFormat().Write(grouped),new SceneFormat().Write(editor.Session.Current));Assert.True(editor.Session.Redo());Assert.Same(ungrouped,editor.Session.Current);
    }
    [Fact] public void Selected_ancestor_and_descendant_keep_their_existing_relationship_once()
    {
        using var r=new Rig();r.Window.Editor.Select("first","descendant","second");Assert.True(r.Window.Run("scene.group"),r.Window.Status);
        var scene=r.Window.Editor.Session.Current;Assert.Equal("first",scene.Find("descendant")!.Parent);r.PoseKept(r.Original,scene);Assert.Equal(1,r.Window.Editor.Session.History.UndoCount);
        var centre=new[]{"first","descendant","second"}.Select(id=>SceneViewport.World(r.Original,id).Translation).Aggregate(Vector3.Zero,(a,b)=>a+b)/3;
        Assert.True(Vector3.Distance(centre,SceneViewport.World(scene,Assert.Single(r.Window.Editor.Selection)).Translation)<1e-5f);
    }
    [Fact] public void Named_reference_to_parent_requires_the_existing_delete_review_instead_of_clearing_it_silently()
    {
        using var r=new Rig();Assert.True(r.Window.Run("scene.group"));var editor=r.Window.Editor;var group=Assert.Single(editor.Selection);
        Assert.True(editor.Execute(new SetReference("frame","group_link",group)));var before=editor.Session.Current;var steps=editor.Session.History.UndoCount;
        Assert.False(r.Window.Run("scene.ungroup"));Assert.Contains("referenced by frame",r.Window.Status);Assert.Same(before,editor.Session.Current);Assert.Equal(steps,editor.Session.History.UndoCount);
    }
    [Fact] public void Empty_selection_nonparent_focus_and_Play_refuse_without_history()
    {
        using var r=new Rig();var editor=r.Window.Editor;editor.Select();Assert.False(r.Window.Run("scene.group"));editor.Select("second");Assert.False(r.Window.Run("scene.ungroup"));
        Assert.True(editor.FocusOn("frame"));Assert.False(r.Window.Run("scene.group"));Assert.Contains("Step out",r.Window.Status);Assert.True(editor.FocusOut());
        r.Window.ProjectPlaying=()=>true;Assert.False(r.Window.Run("scene.group"));Assert.Contains("Stop Play",r.Window.Status);Assert.Equal(0,editor.Session.History.UndoCount);Assert.Equal(new SceneFormat().Write(r.Original),new SceneFormat().Write(editor.Session.Current));
    }
}
