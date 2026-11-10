using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneClipboardUi14Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("scene-clipboard-ui14-").FullName;
        public readonly HeadlessHost Host=new(1400,1100){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly Scene Source,Destination;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string Disk(string name)=>Path.Combine(Root,"data",name);
        public Rig(bool target=true)
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            var group=SceneObject.At("group",null,new(2,0,3)) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.6f),Scale=new(2,2,2)};
            group=group with{References=group.References.Add("inside","child").Add("outside","shared")};
            Source=new([group,SceneObject.At("child",null,Vector3.UnitX) with{Parent="group"},SceneObject.At("shared",null,new(8,0,8))]);
            Destination=new(target?[SceneObject.At("shared",null,Vector3.Zero),SceneObject.At("copy_group",null,new(-10,0,0))]:[SceneObject.At("copy_group",null,new(-10,0,0))]);
            File.WriteAllText(Disk("a.json"),new SceneFormat().Write(Source));File.WriteAllText(Disk("b.json"),new SceneFormat().Write(Destination));
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Documents.BindScene("a.json","scene",out _));Assert.True(Workspace.Documents.BindScene("b.json","scene",out _));
            EditorWindow<Scene>.Configure(ImGui.GetIO());Show("a.json");Assert.True(Window.Editor.Select("group","child"));Host.Frames(Workspace.Draw,3);
        }
        public void Show(string name){Assert.True(Workspace.Show(name));Host.Frames(Workspace.Draw,4);}
        public void Menu(){Host.Press(Workspace.Draw,ImGuiKey.E,alt:true);Host.Frames(Workspace.Draw,2);}
        public void Native(ImGuiKey key)
        {Menu();Host.Press(Workspace.Draw,ImGuiKey.Escape);Host.Frames(Workspace.Draw,2);Host.Press(Workspace.Draw,key,ctrl:true);Host.Frames(Workspace.Draw,2);}
        public void MenuAction(string label)
        {
            Menu();var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label==label);Assert.True(node.Enabled,node.DisabledReason);
            var action=Ui.RequestAction(node.Id,UiActions.Invoke);Host.Frames(Workspace.Draw,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Native_shortcuts_and_Edit_menu_copy_between_levels_with_unique_ids_links_selection_and_one_saved_undo(bool menu)
    {
        using var r=new Rig();ImGui.SetClipboardText("private text sentinel");var sourceWindow=r.Window;var sourceText=File.ReadAllText(r.Disk("a.json"));
        if(menu)r.MenuAction("Copy");else r.Native(ImGuiKey.C);
        Assert.Equal(0,sourceWindow.Editor.Session.History.UndoCount);Assert.Equal(sourceText,File.ReadAllText(r.Disk("a.json")));Assert.Equal("private text sentinel",ImGui.GetClipboardText().ToString());
        r.Show("b.json");if(menu)r.MenuAction("Paste");else r.Native(ImGuiKey.V);
        var editor=r.Window.Editor;var scene=editor.Session.Current;Assert.Equal(4,scene.Objects.Length);Assert.Equal(4,scene.Objects.Select(o=>o.Id).Distinct().Count());Assert.Equal(1,editor.Session.History.UndoCount);
        var copies=editor.Selection.Select(id=>scene.Find(id)!).ToArray();Assert.Equal(2,copies.Length);var root=Assert.Single(copies,o=>o.Parent is null);var child=Assert.Single(copies,o=>o.Parent==root.Id);
        Assert.Equal(child.Id,root.References["inside"]);Assert.Equal("shared",root.References["outside"]);Assert.Equal(r.Source.Find("child")!.Position,child.Position);
        foreach(var pair in new[]{(Old:"group",New:root.Id),(Old:"child",New:child.Id)})Assert.True(Vector3.Distance(Vector3.UnitX,SceneViewport.World(scene,pair.New).Translation-SceneViewport.World(r.Source,pair.Old).Translation)<1e-5f);
        Assert.Equal("private text sentinel",ImGui.GetClipboardText().ToString());Assert.True(r.Window.Run("editor.save"),r.Window.Status);Assert.Equal(new SceneFormat().Write(scene),File.ReadAllText(r.Disk("b.json")));
        Assert.True(editor.Session.Undo());Assert.Equal(new SceneFormat().Write(r.Destination),new SceneFormat().Write(editor.Session.Current));Assert.True(editor.Session.Redo());Assert.Same(scene,editor.Session.Current);
        Assert.True(r.Window.Run("scene.paste"),r.Window.Status);Assert.Equal(6,editor.Session.Current.Objects.Length);Assert.Equal(6,editor.Session.Current.Objects.Select(o=>o.Id).Distinct().Count());Assert.Equal(2,editor.Session.History.UndoCount);
        Assert.Equal(sourceText,File.ReadAllText(r.Disk("a.json")));
    }
    [Fact] public void Copy_keeps_an_immutable_pose_snapshot_when_the_source_is_edited_later()
    {
        using var r=new Rig();Assert.True(r.Window.Run("scene.copy"));r.Window.Editor.Select("group");Assert.True(r.Window.Inspector!.Set("position.x",100.0));
        r.Show("b.json");Assert.True(r.Window.Run("scene.paste"),r.Window.Status);var copy=Assert.Single(r.Window.Editor.Selection.Select(id=>r.Window.Editor.Session.Current.Find(id)!),o=>o.Parent is null);
        Assert.Equal(3,copy.Position.X);Assert.Equal(1,r.Window.Editor.Session.History.UndoCount);
    }
    [Fact] public void Copied_child_detaches_from_absent_external_parent_at_preserved_world_pose()
    {
        using var r=new Rig();r.Window.Editor.Select("child");Assert.True(r.Window.Run("scene.copy"));r.Show("b.json");Assert.True(r.Window.Run("scene.paste"),r.Window.Status);
        var scene=r.Window.Editor.Session.Current;var copy=scene.Find(Assert.Single(r.Window.Editor.Selection))!;Assert.Null(copy.Parent);
        Assert.True(Vector3.Distance(SceneViewport.World(r.Source,"child").Translation+Vector3.UnitX,SceneViewport.World(scene,copy.Id).Translation)<1e-5f);Assert.Empty(new SceneValidator().Validate(scene));
    }
    [Fact] public void Missing_external_reference_refuses_paste_without_partial_objects_or_undo()
    {
        using var r=new Rig(target:false);Assert.True(r.Window.Run("scene.copy"));r.Show("b.json");var before=r.Window.Editor.Session.Current;
        Assert.False(r.Window.Run("scene.paste"));Assert.Contains("needs 'shared'",r.Window.Status);Assert.Same(before,r.Window.Editor.Session.Current);Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
    }
    [Fact] public void A_different_project_has_no_access_to_the_first_projects_object_clipboard()
    {
        using var r=new Rig();Assert.True(r.Window.Run("scene.copy"));
        var data=Directory.CreateDirectory(Path.Combine(r.Root,"other","data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(r.Root,"other","schemas")).FullName;
        File.WriteAllText(Path.Combine(data,"level.json"),new SceneFormat().Write(Scene.Empty));using var other=new DataWorkspace(DataProject.Open(data,schemas));
        Assert.True(other.Documents.BindScene("level.json","scene",out _));Assert.True(other.Show("level.json"));r.Host.Frames(other.Draw,4);
        Assert.False(other.SceneWindow!.Run("scene.paste"));Assert.Contains("Copy objects in this project",other.SceneWindow.Status);Assert.Empty(other.SceneWindow.Editor.Session.Current.Objects);Assert.Equal(0,other.SceneWindow.Editor.Session.History.UndoCount);
    }
    [Fact] public void CtrlC_in_a_number_text_field_uses_text_clipboard_and_does_not_copy_objects()
    {
        using var r=new Rig();r.Window.Editor.Select("group");r.Host.Frames(r.Workspace.Draw,3);var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Position X");
        r.Host.Click(r.Workspace.Draw,field.Bounds.X+field.Bounds.Width/2,field.Bounds.Y+field.Bounds.Height/2);r.Host.Press(r.Workspace.Draw,ImGuiKey.A,ctrl:true);r.Host.Press(r.Workspace.Draw,ImGuiKey.C,ctrl:true);
        Assert.Equal("2",ImGui.GetClipboardText().ToString());r.Show("b.json");Assert.False(r.Window.Run("scene.paste"));Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
    }
    [Fact] public void Closed_project_releases_clipboard_and_old_window_cannot_paste_or_refill_it()
    {
        using var r=new Rig();var window=r.Window;Assert.True(window.Run("scene.copy"));var before=window.Editor.Session.Current;r.Workspace.Dispose();
        Assert.False(window.Run("scene.paste"));Assert.False(window.Run("scene.copy"));Assert.Same(before,window.Editor.Session.Current);Assert.Equal(0,window.Editor.Session.History.UndoCount);
    }
    [Fact] public void Empty_clipboard_focused_branch_and_Play_refuse_paste_with_plain_reasons()
    {
        using var r=new Rig();Assert.False(r.Window.Run("scene.paste"));Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
        Assert.True(r.Window.Run("scene.copy"));Assert.True(r.Window.Editor.FocusOn("group"));Assert.Contains(r.Window.PaletteEntries("Paste objects"),p=>p.Id=="scene.paste"&&p.Unavailable=="Step out of focus before pasting objects.");Assert.False(r.Window.Run("scene.paste"));Assert.Contains("Step out of focus",r.Window.Status);Assert.True(r.Window.Editor.FocusOut());
        r.Window.ProjectPlaying=()=>true;Assert.False(r.Window.Run("scene.paste"));Assert.Contains("Stop Play",r.Window.Status);Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
    }
}
