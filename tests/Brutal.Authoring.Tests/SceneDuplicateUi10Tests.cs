using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneDuplicateUi10Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("scene-duplicate-ui10-").FullName;
        public readonly HeadlessHost Host=new(1400,1050){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly Scene Original;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string Disk=>Path.Combine(Root,"data","level.json");
        public Rig()
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            var root=SceneObject.At("group",null,new(2,0,3)) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.7f),Scale=new(2,2,2)};
            var child=SceneObject.At("child",null,new(1,0,0)) with{Parent="group"};
            root=root with{References=root.References.Add("inside","child").Add("outside","target")};
            Original=new([root,child,SceneObject.At("target",null,new(10,0,10)),SceneObject.At("copy_group",null,new(-5,0,0))]);
            File.WriteAllText(Disk,new SceneFormat().Write(Original));Workspace=new(DataProject.Open(data,schemas));
            Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));Assert.True(Workspace.Show("level.json"));
            EditorWindow<Scene>.Configure(ImGui.GetIO());Assert.True(Window.Editor.Select("group","child"));Host.Frames(Workspace.Draw,4);
        }
        public void Menu()
        {Host.Press(Workspace.Draw,ImGuiKey.E,alt:true);Host.Frames(Workspace.Draw,2);}
        public void MenuDuplicate()
        {
            Menu();var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Duplicate");Assert.True(node.Enabled);
            var request=Ui.RequestAction(node.Id,UiActions.Invoke);Host.Frames(Workspace.Draw,3);Assert.Equal(UiActionOutcome.Applied,request.Outcome);
        }
        public void NativeDuplicate()
        {
            // Enter and leave the native Edit menu to focus the editor without changing object selection.
            Menu();Host.Press(Workspace.Draw,ImGuiKey.Escape);Host.Frames(Workspace.Draw,2);
            Host.Press(Workspace.Draw,ImGuiKey.D,ctrl:true);Host.Frames(Workspace.Draw,2);
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Shortcut_and_Edit_menu_duplicate_group_beside_original_with_links_selection_and_one_saved_undo(bool menu)
    {
        using var r=new Rig();var originalText=File.ReadAllText(r.Disk);
        Assert.Equal("Ctrl+D",r.Window.Shortcuts.For("scene.duplicate")!.Value.ToString());
        if(menu)r.MenuDuplicate();else r.NativeDuplicate();
        var editor=r.Window.Editor;var scene=editor.Session.Current;Assert.Equal(6,scene.Objects.Length);Assert.Equal(6,scene.Objects.Select(o=>o.Id).Distinct().Count());
        Assert.Equal(1,editor.Session.History.UndoCount);Assert.Equal(2,editor.Selection.Count);
        var copies=editor.Selection.Select(id=>scene.Find(id)!).ToArray();Assert.All(copies,c=>Assert.Null(r.Original.Find(c.Id)));
        var root=Assert.Single(copies,c=>c.Parent is null);var child=Assert.Single(copies,c=>c.Parent==root.Id);
        Assert.Equal(child.Id,root.References["inside"]);Assert.Equal("target",root.References["outside"]);Assert.Equal(r.Original.Find("child")!.Position,child.Position);
        foreach(var pair in new[]{(Original:"group",Copy:root.Id),(Original:"child",Copy:child.Id)})
        {var delta=SceneViewport.World(scene,pair.Copy).Translation-SceneViewport.World(r.Original,pair.Original).Translation;Assert.True(Vector3.Distance(Vector3.UnitX,delta)<1e-5f);}
        Assert.Equal(originalText,File.ReadAllText(r.Disk));Assert.True(r.Window.Run("editor.save"),r.Window.Status);
        var saved=new SceneFormat().Read(File.ReadAllText(r.Disk));Assert.Equal(new SceneFormat().Write(scene),new SceneFormat().Write(saved));
        Assert.True(editor.Session.Undo());Assert.Equal(new SceneFormat().Write(r.Original),new SceneFormat().Write(editor.Session.Current));Assert.True(editor.Session.Redo());Assert.Equal(new SceneFormat().Write(scene),new SceneFormat().Write(editor.Session.Current));
    }
    [Fact] public void Repeated_duplicate_uses_new_ids_and_single_child_respects_external_parent_world_transform()
    {
        using var r=new Rig();r.Window.Editor.Select("child");Assert.True(r.Window.HandleKey(Chord.Parse("Ctrl+D")),r.Window.Status);
        var editor=r.Window.Editor;var first=Assert.Single(editor.Selection);var copy=editor.Session.Current.Find(first)!;
        Assert.Equal("group",copy.Parent);Assert.True(Vector3.Distance(Vector3.UnitX,SceneViewport.World(editor.Session.Current,first).Translation-SceneViewport.World(r.Original,"child").Translation)<1e-5f);
        Assert.True(r.Window.HandleKey(Chord.Parse("Ctrl+D")),r.Window.Status);var second=Assert.Single(editor.Selection);Assert.NotEqual(first,second);
        Assert.Equal(6,editor.Session.Current.Objects.Length);Assert.Equal(2,editor.Session.History.UndoCount);
    }
    [Fact] public void Empty_selection_and_running_Play_leave_duplicate_disabled_and_handler_inert()
    {
        using var r=new Rig();r.Window.Editor.Select();r.Menu();
        var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Duplicate");Assert.False(node.Enabled);Assert.Contains("Select objects",node.DisabledReason);
        Assert.False(r.Window.HandleKey(Chord.Parse("Ctrl+D")));Assert.Equal(4,r.Window.Editor.Session.Current.Objects.Length);
        r.Host.Press(r.Workspace.Draw,ImGuiKey.Escape);r.Window.Editor.Select("group");r.Window.ProjectPlaying=()=>true;
        Assert.False(r.Window.HandleKey(Chord.Parse("Ctrl+D")));Assert.Contains("Stop Play",r.Window.Status);Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
    }
}
