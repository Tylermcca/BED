using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneRenameUi18Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("scene-rename-ui18-").FullName;
        public readonly HeadlessHost Host=new(1600,1100){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly Scene Original;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string Disk=>Path.Combine(Root,"data","level.json");
        public Rig()
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            var parent=SceneObject.At("parent",null,new(3,0,5));parent=parent with{References=parent.References.Add("self","parent")};
            var watcher=SceneObject.At("watcher",null,new(8,0,0));watcher=watcher with{References=watcher.References.Add("target","parent").Add("child","child")};
            Original=new([parent,SceneObject.At("child",null,Vector3.UnitY) with{Parent="parent"},watcher]);
            File.WriteAllText(Disk,new SceneFormat().Write(Original));Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));Assert.True(Workspace.Show("level.json"));
            EditorWindow<Scene>.Configure(ImGui.GetIO());Assert.True(Window.Editor.Select("parent"));Host.Frames(Workspace.Draw,4);
        }
        public void Begin()
        {
            var row=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="> parent");Host.Click(Workspace.Draw,row.Bounds.X+row.Bounds.Width/2,row.Bounds.Y+row.Bounds.Height/2);
            Host.Press(Workspace.Draw,ImGuiKey.F2);Host.Frames(Workspace.Draw,3);
            var field=Field();Assert.True(field.Focused);
            var tree=Ui.Snapshot();Assert.True(Inside(tree,field,"Outliner"));
        }
        public UiNode Field()=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Object ID");
        public void Enter(string id){Host.Press(Workspace.Draw,ImGuiKey.A,ctrl:true);if(id.Length==0)Host.Press(Workspace.Draw,ImGuiKey.Backspace);else Host.Type(Workspace.Draw,id);Host.Press(Workspace.Draw,ImGuiKey.Enter);Host.Frames(Workspace.Draw,3);}
        private static bool Inside(UiSnapshot tree,UiNode node,string name)
        {for(var parent=node.Parent;parent>=0;parent=tree.Nodes[parent].Parent)if(tree.Nodes[parent].Role==UiRole.Window&&tree.Nodes[parent].Label==name)return true;return false;}
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact] public void F2_renames_in_the_Outliner_preserves_order_remaps_all_links_and_saves_one_replayable_undo()
    {
        using var r=new Rig();var before=File.ReadAllText(r.Disk);r.Begin();r.Enter("renamed_parent");var editor=r.Window.Editor;var scene=editor.Session.Current;
        Assert.Equal(new[]{"renamed_parent","child","watcher"},scene.Objects.Select(o=>o.Id));Assert.Null(scene.Find("parent"));Assert.Equal("renamed_parent",scene.Find("child")!.Parent);
        Assert.Equal("renamed_parent",scene.Find("watcher")!.References["target"]);Assert.Equal("child",scene.Find("watcher")!.References["child"]);Assert.Equal("renamed_parent",scene.Find("renamed_parent")!.References["self"]);
        Assert.Equal(r.Original.Objects.Select(o=>o.Position),scene.Objects.Select(o=>o.Position));Assert.Equal(r.Original.Objects.Select(o=>o.Rotation),scene.Objects.Select(o=>o.Rotation));Assert.Equal(r.Original.Objects.Select(o=>o.Scale),scene.Objects.Select(o=>o.Scale));Assert.Equal(new[]{"renamed_parent"},editor.Selection);Assert.Equal(1,editor.Session.History.UndoCount);Assert.Equal(before,File.ReadAllText(r.Disk));
        Assert.True(r.Window.Run("editor.save"));Assert.Equal(new SceneFormat().Write(scene),File.ReadAllText(r.Disk));
        var replay=DocumentSession<Scene>.Replay(r.Original,editor.Session.Journal,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());Assert.Equal(new SceneFormat().Write(scene),new SceneFormat().Write(replay.Current));
        Assert.True(editor.Session.Undo());Assert.Equal(before,new SceneFormat().Write(editor.Session.Current));Assert.True(editor.Session.Redo());Assert.Same(scene,editor.Session.Current);
    }
    [Theory][InlineData("Bad-ID")][InlineData("watcher")][InlineData("")]
    public void Invalid_or_duplicate_id_keeps_inline_text_and_original_document_without_history(string id)
    {
        using var r=new Rig();r.Begin();r.Enter(id);Assert.Equal(id,r.Field().Value);Assert.Equal(new SceneFormat().Write(r.Original),new SceneFormat().Write(r.Window.Editor.Session.Current));Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
        Assert.Contains(id=="watcher"?"already exists":"lowercase",r.Window.Status);r.Enter("corrected");Assert.NotNull(r.Window.Editor.Session.Current.Find("corrected"));Assert.Equal(1,r.Window.Editor.Session.History.UndoCount);
    }
    [Fact] public void Escape_cancels_inline_edit_with_no_document_or_history_change()
    {
        using var r=new Rig();r.Begin();r.Host.Press(r.Workspace.Draw,ImGuiKey.A,ctrl:true);r.Host.Type(r.Workspace.Draw,"draft_name");r.Host.Press(r.Workspace.Draw,ImGuiKey.Escape);r.Host.Frames(r.Workspace.Draw,3);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Object ID");Assert.Equal(new SceneFormat().Write(r.Original),new SceneFormat().Write(r.Window.Editor.Session.Current));Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);Assert.Equal("parent",r.Window.Editor.Primary);
    }
    [Fact] public void Stale_inline_edit_refuses_and_Play_or_multiple_selection_cannot_begin_rename()
    {
        using var r=new Rig();r.Window.ProjectPlaying=()=>true;
        Assert.False(r.Window.Run("editor.rename_object"));Assert.Contains("Stop Play",r.Window.Status);
        r.Window.ProjectPlaying=()=>false;r.Window.Editor.Select("parent","child");
        Assert.False(r.Window.Run("editor.rename_object"));Assert.Contains("Select one",r.Window.Status);
        r.Window.Editor.Select("parent");r.Begin();
        Assert.True(r.Window.Editor.Session.Execute(new MoveObject("parent",new(9,0,0))));
        r.Host.Frames(r.Workspace.Draw,3);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Object ID");
        Assert.NotNull(r.Window.Editor.Session.Current.Find("parent"));Assert.Equal(1,r.Window.Editor.Session.History.UndoCount);
        Assert.Contains("changed",r.Window.Status);
    }
    [Fact] public void Rename_refuses_when_reference_remapping_would_edit_outside_the_focused_branch()
    {
        using var r=new Rig();Assert.True(r.Window.Editor.FocusOn("parent"));r.Host.Frames(r.Workspace.Draw,3);
        r.Begin();r.Enter("new_parent");Assert.NotNull(r.Window.Editor.Session.Current.Find("parent"));
        Assert.Contains("watcher",r.Window.Status);Assert.Contains("outside",r.Window.Status);
        Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);Assert.Equal("parent",r.Window.Editor.FocusedOn);
    }
    [Fact] public void Reentering_the_same_id_finishes_without_a_document_edit()
    {
        using var r=new Rig();r.Begin();r.Enter("parent");Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Object ID");Assert.Equal("parent",r.Window.Editor.Primary);
    }
}
