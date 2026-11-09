using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneCloseKeyboardTests
{
    private sealed class Rig : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("scene-close-").FullName;
        public readonly string PathName;
        public readonly string Original;
        public readonly DataWorkspace Workspace;
        public readonly HeadlessHost Host=new(1400,900){Drawing=false};
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public Rig(bool dirty)
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            PathName=Path.Combine(data,"room.json");
            Original=new SceneFormat().Write(new Scene([SceneObject.At("item",null,Vector3.Zero)]));File.WriteAllText(PathName,Original);
            Workspace=new(DataProject.Open(data,schemas));
            Assert.True(Workspace.Documents.BindScene("room.json","scene",out _));Assert.True(Workspace.Show("room.json"));
            Assert.True(Window.Editor.Select("item"));
            if(dirty)Assert.True(Window.Inspector!.Set("position.x",3.0));
            EditorWindow<Scene>.Configure(ImGui.GetIO());Host.Frames(Workspace.Draw,4);
        }
        public void Key(ImGuiKey key,bool ctrl=false){Host.Press(Workspace.Draw,key,ctrl:ctrl);Host.Frames(Workspace.Draw,2);}
        public bool Prompt=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Window&&n.Label=="Save changes?");
        public void Choose(string label)
        {
            for(var i=0;i<6;i++)
            {
                if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label==label&&n.Focused)){Key(ImGuiKey.Enter);return;}
                Key(ImGuiKey.Tab);
            }
            Assert.Fail("Prompt focus never reached "+label);
        }
        public void Reopen()
        {
            Assert.Null(Workspace.SceneWindow);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.Contains("room.json",StringComparison.Ordinal)&&n.Focused);
            Key(ImGuiKey.Enter);Assert.NotNull(Workspace.SceneWindow);
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact] public void Clean_CtrlW_closes_only_document_and_Files_Enter_reopens_new_authority()
    {
        using var r=new Rig(false);var old=r.Window;var session=old.Editor.Session;
        r.Key(ImGuiKey.W,ctrl:true);Assert.False(r.Prompt);r.Reopen();
        Assert.NotSame(session,r.Window.Editor.Session);Assert.False(old.Run("editor.close"));
        Assert.Same(r.Window.Editor, r.Workspace.SceneWindow!.Editor);Assert.Equal(r.Original,File.ReadAllText(r.PathName));
        Assert.False(r.Workspace.Documents.TryGetOpenScene("missing.json",out _));
    }
    [Theory][InlineData("Save")][InlineData("Don't Save")][InlineData("Cancel")][InlineData("Escape")]
    public void Dirty_close_uses_real_keyboard_decision_preserving_save_discard_cancel_facts(string choice)
    {
        using var r=new Rig(true);var old=r.Window;var draft=old.Editor.Session.Current;var undo=old.Editor.Session.History.UndoCount;
        r.Key(ImGuiKey.W,ctrl:true);Assert.True(r.Prompt);Assert.Same(old,r.Window);
        if(choice=="Escape")r.Key(ImGuiKey.Escape);else r.Choose(choice);
        if(choice is "Cancel" or "Escape")
        {
            Assert.False(r.Prompt);Assert.Same(old,r.Window);Assert.Equal(draft,r.Window.Editor.Session.Current);
            Assert.Equal(undo,r.Window.Editor.Session.History.UndoCount);Assert.True(r.Window.Editor.Session.Dirty);Assert.Equal(r.Original,File.ReadAllText(r.PathName));return;
        }
        Assert.False(r.Prompt);r.Reopen();Assert.NotSame(old,r.Window);
        Assert.Equal(choice=="Save"?3f:0f,r.Window.Editor.Session.Current.Find("item")!.Position.X);
        Assert.False(r.Window.Editor.Session.Dirty);Assert.False(r.Window.Editor.Session.History.CanUndo);
        if(choice=="Don't Save")Assert.Equal(r.Original,File.ReadAllText(r.PathName));else Assert.NotEqual(r.Original,File.ReadAllText(r.PathName));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Refused_or_conflicting_save_keeps_exact_draft_history_and_pending_decision(bool conflict)
    {
        using var r=new Rig(true);var old=r.Window;var draft=old.Editor.Session.Current;var undo=old.Editor.Session.History.UndoCount;
        if(conflict)File.WriteAllText(r.PathName,r.Original+" ");else r.Workspace.Documents.SceneSaveCheck=_=>"Synthetic scene rule refused.";
        var disk=File.ReadAllText(r.PathName);r.Key(ImGuiKey.W,ctrl:true);r.Choose("Save");
        Assert.True(r.Prompt);Assert.Same(old,r.Window);Assert.Equal(draft,r.Window.Editor.Session.Current);
        Assert.Equal(undo,r.Window.Editor.Session.History.UndoCount);Assert.Equal(disk,File.ReadAllText(r.PathName));
        Assert.Contains(conflict?"changed or removed":"Synthetic scene rule refused",r.Workspace.Said);
        r.Key(ImGuiKey.Escape);Assert.False(r.Prompt);Assert.Same(old,r.Window);
    }
    [Fact] public void File_menu_Close_is_the_same_document_route()
    {
        using var r=new Rig(false);
        r.Host.Press(r.Workspace.Draw,ImGuiKey.F,alt:true);r.Host.Frames(r.Workspace.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Close"&&n.Enabled);
        for(var i=0;i<16;i++)
        {
            if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.MenuItem&&n.Label=="Close"&&n.Focused))
            {r.Key(ImGuiKey.Enter);r.Reopen();Assert.Equal(r.Original,File.ReadAllText(r.PathName));return;}
            r.Key(ImGuiKey.DownArrow);
        }
        Assert.Fail("File menu keyboard navigation did not reach Close.");
    }
    [Fact] public void Changed_scene_after_prompt_is_not_discarded_by_an_old_decision()
    {
        using var r=new Rig(true);var old=r.Window;r.Key(ImGuiKey.W,ctrl:true);
        Assert.True(old.Inspector!.Set("position.x",5.0));var undo=old.Editor.Session.History.UndoCount;
        r.Choose("Don't Save");Assert.False(r.Prompt);Assert.Same(old,r.Window);
        Assert.Equal(5f,old.Editor.Session.Current.Find("item")!.Position.X);Assert.Equal(undo,old.Editor.Session.History.UndoCount);
        Assert.Equal(r.Original,File.ReadAllText(r.PathName));Assert.Contains("scene changed",r.Workspace.Said);
    }

}
