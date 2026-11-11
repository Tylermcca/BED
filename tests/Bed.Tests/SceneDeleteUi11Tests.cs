using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneDeleteUi11Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("scene-delete-ui11-").FullName;
        public readonly HeadlessHost Host=new(1400,1050){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly Scene Original;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string Disk=>Path.Combine(Root,"data","level.json");
        public Rig(bool shear=false)
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            var parent=SceneObject.At("parent",null,new(3,0,5)) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.6f),Scale=shear?new(2,1,1):new(2,2,2)};
            var child=SceneObject.At("child",null,Vector3.UnitX) with{Parent="parent",Rotation=shear?Quaternion.CreateFromAxisAngle(Vector3.UnitY,.7f):Quaternion.Identity};
            var watcher=SceneObject.At("watcher",null,new(9,0,0));watcher=watcher with{References=watcher.References.Add("target","parent").Add("other","child")};
            Original=new([parent,child,watcher]);File.WriteAllText(Disk,new SceneFormat().Write(Original));
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));Assert.True(Workspace.Show("level.json"));
            EditorWindow<Scene>.Configure(ImGui.GetIO());Assert.True(Window.Editor.Select("parent"));Host.Frames(Workspace.Draw,4);
        }
        public void Key(ImGuiKey key,bool shift=false,bool alt=false){Host.Press(Workspace.Draw,key,shift:shift,alt:alt);Host.Frames(Workspace.Draw,2);}
        public bool Prompt=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Window&&n.Label=="Delete referenced object?");
        public string Canonical()=>new SceneFormat().Write(Window.Editor.Session.Current);
        public void Request(string route="key")
        {
            if(route=="context")
            {
                var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="> parent");
                Host.Click(Workspace.Draw,item.Bounds.X+item.Bounds.Width/2,item.Bounds.Y+item.Bounds.Height/2);Key(ImGuiKey.F10,shift:true);
            }
            else Key(ImGuiKey.E,alt:true);
            if(route=="key"){Key(ImGuiKey.Escape);Key(ImGuiKey.Delete);return;}
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Delete");Assert.True(node.Enabled,node.DisabledReason);
            var action=Ui.RequestAction(node.Id,UiActions.Invoke);Host.Frames(Workspace.Draw,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);
        }
        public void Choose(string label)
        {
            for(var i=0;i<5;i++)
            {if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label==label&&n.Focused)){Key(ImGuiKey.Enter);return;}Key(ImGuiKey.Tab);}
            Assert.Fail("Keyboard did not reach "+label);
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData("key")][InlineData("menu")][InlineData("context")]
    public void All_delete_routes_list_references_then_confirm_one_atomic_undo_with_world_preservation_and_saved_valid_scene(string route)
    {
        using var r=new Rig();var before=r.Canonical();var world=SceneViewport.World(r.Original,"child");r.Request(route);Assert.True(r.Prompt);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("child: is inside 'parent'",StringComparison.Ordinal));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("watcher: refers to 'parent'",StringComparison.Ordinal));
        Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);Assert.Equal(before,r.Canonical());r.Choose("Delete anyway");Assert.False(r.Prompt);
        var editor=r.Window.Editor;var scene=editor.Session.Current;Assert.Null(scene.Find("parent"));Assert.Equal(2,scene.Objects.Length);Assert.Null(scene.Find("child")!.Parent);
        Assert.Equal("child",scene.Find("watcher")!.References["other"]);Assert.False(scene.Find("watcher")!.References.ContainsKey("target"));
        Assert.True(MatrixDistance(world,SceneViewport.World(scene,"child"))<1e-4f);Assert.Empty(new SceneValidator().Validate(scene));Assert.Equal(1,editor.Session.History.UndoCount);
        Assert.Equal(before,File.ReadAllText(r.Disk));Assert.True(r.Window.Run("editor.save"),r.Window.Status);var saved=new SceneFormat().Read(File.ReadAllText(r.Disk));Assert.Empty(new SceneValidator().Validate(saved));
        Assert.True(editor.Session.Undo());Assert.Equal(before,r.Canonical());Assert.True(editor.Session.Redo());Assert.Equal(new SceneFormat().Write(saved),r.Canonical());
    }
    private static float MatrixDistance(Matrix4x4 a,Matrix4x4 b)=>
        MathF.Abs(a.M11-b.M11)+MathF.Abs(a.M12-b.M12)+MathF.Abs(a.M13-b.M13)+MathF.Abs(a.M21-b.M21)+MathF.Abs(a.M22-b.M22)+MathF.Abs(a.M23-b.M23)+MathF.Abs(a.M31-b.M31)+MathF.Abs(a.M32-b.M32)+MathF.Abs(a.M33-b.M33)+Vector3.Distance(a.Translation,b.Translation);
    [Theory][InlineData(false)][InlineData(true)]
    public void Cancel_or_Escape_changes_no_document_disk_history_or_selection(bool escape)
    {
        using var r=new Rig();var before=r.Canonical();r.Request();Assert.True(r.Prompt);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Cancel"&&n.Focused);
        if(escape)r.Key(ImGuiKey.Escape);else r.Choose("Cancel");Assert.False(r.Prompt);
        Assert.Equal(before,r.Canonical());Assert.Equal(before,File.ReadAllText(r.Disk));Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);Assert.Equal("parent",r.Window.Editor.Primary);
    }
    [Fact] public void Changed_revision_cannot_consume_delete_confirmation()
    {
        using var r=new Rig();r.Request();Assert.True(r.Window.Inspector!.Set("position.x",8.0));var changed=r.Canonical();r.Choose("Delete anyway");
        Assert.False(r.Prompt);Assert.Equal(changed,r.Canonical());Assert.NotNull(r.Window.Editor.Session.Current.Find("parent"));Assert.Equal(1,r.Window.Editor.Session.History.UndoCount);Assert.Contains("changed",r.Window.Status);
    }
    [Fact] public void Sheared_child_refuses_without_partial_delete()
    {
        using var r=new Rig(shear:true);var before=r.Canonical();r.Request();r.Choose("Delete anyway");Assert.True(r.Prompt);Assert.Equal(before,r.Canonical());Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("shear",StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void Out_of_focus_referrer_keeps_every_object_and_Escape_preserves_focus()
    {
        using var r=new Rig();Assert.True(r.Window.Editor.FocusOn("parent"));var focus=r.Window.Editor.Focus.ToArray();var before=r.Canonical();
        r.Request();r.Choose("Delete anyway");Assert.True(r.Prompt);Assert.Equal(before,r.Canonical());Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("outside the place",StringComparison.Ordinal));
        r.Key(ImGuiKey.Escape);Assert.False(r.Prompt);Assert.Equal(focus,r.Window.Editor.Focus);Assert.Equal(before,r.Canonical());
    }
    [Fact] public void Changed_selection_cannot_consume_old_delete_and_running_Play_is_refused()
    {
        using var r=new Rig();var before=r.Canonical();r.Request();r.Window.Editor.Select("watcher");r.Choose("Delete anyway");
        Assert.False(r.Prompt);Assert.Equal(before,r.Canonical());Assert.Equal(0,r.Window.Editor.Session.History.UndoCount);
        r.Window.ProjectPlaying=()=>true;Assert.False(r.Window.Run("scene.remove"));Assert.Contains("Stop Play",r.Window.Status);Assert.Equal(before,r.Canonical());
    }
    [Fact] public void Unreferenced_delete_is_immediate_and_one_undo_restores_it()
    {
        using var r=new Rig();r.Window.Editor.Select("watcher");r.Request();Assert.False(r.Prompt);Assert.Null(r.Window.Editor.Session.Current.Find("watcher"));
        Assert.Equal(1,r.Window.Editor.Session.History.UndoCount);Assert.True(r.Window.Editor.Session.Undo());Assert.Equal(new SceneFormat().Write(r.Original),r.Canonical());
    }
}
