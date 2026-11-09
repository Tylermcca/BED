using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class EditorFolderDropTests
{
    sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("editor-folder-drop-").FullName;
        public readonly HeadlessHost Host=new(1200,800){Drawing=false};
        public readonly ProjectLauncher Launcher;
        public readonly string First,Second;
        public Rig()
        {
            First=Make("First");Second=Make("Second");
            Launcher=new(new RecentProjects(Path.Combine(Root,"recent.json")));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Assert.True(Launcher.OpenFolder(First));Host.Frames(Launcher.Draw,3);
        }
        string Make(string name)
        {
            var dir=Path.Combine(Root,name);Directory.CreateDirectory(Path.Combine(dir,"data"));Directory.CreateDirectory(Path.Combine(dir,"schemas"));
            File.WriteAllText(Path.Combine(dir,"data","items.json"),"{\"value\":1}");
            File.WriteAllText(Path.Combine(dir,"schemas","items.schema.json"),"{\"type\":\"object\"}");return dir;
        }
        public void Dirty()=>Assert.True(Launcher.Workspace!.Active!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(2))));
        public void Drop()
        {
            Host.Frames(Launcher.Draw,2);var frame=Ui.Snapshot();
            var native=ImGui.Internal.FindWindowByName("Inspector###inspector");Assert.False(native.IsNull());
            var id=(((ulong)UiRole.Window+1)<<32)|native.ID.Value;
            var node=Assert.Single(frame.Nodes,n=>n.Role==UiRole.Window && n.Id==id);
            Assert.True(Launcher.DispatchDrop([Second],frame,node),Launcher.Said);
            Host.Frames(Launcher.Draw,2);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label==$"Open {Second} instead?");
        }
        public void Dispose(){Launcher.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData(SaveChangesChoice.Cancel)][InlineData(SaveChangesChoice.Save)][InlineData(SaveChangesChoice.DontSave)]
    public void Editor_drop_confirms_then_uses_existing_dirty_decision(SaveChangesChoice choice)
    {
        using var r=new Rig();r.Dirty();var old=r.Launcher.Workspace;r.Drop();Assert.Same(old,r.Launcher.Workspace);
        Assert.False(r.Launcher.AnswerFolderReplacement(true));Assert.True(r.Launcher.SaveChangesPending);
        r.Launcher.AnswerSaveChanges(choice);
        if(choice==SaveChangesChoice.Cancel){Assert.Same(old,r.Launcher.Workspace);Assert.True(old!.Active!.Dirty);}
        else Assert.Equal(Path.Combine(r.Second,"data"),r.Launcher.Current!.Data);
        Assert.Equal(choice==SaveChangesChoice.Save?2:1,JsonDocument.Parse(File.ReadAllText(Path.Combine(r.First,"data","items.json"))).RootElement.GetProperty("value").GetInt32());
        Assert.Equal("{\"value\":1}",File.ReadAllText(Path.Combine(r.Second,"data","items.json")));
    }
    [Fact] public void Escape_cancels_folder_prompt_without_touching_draft()
    {
        using var r=new Rig();r.Dirty();var old=r.Launcher.Workspace;r.Drop();
        r.Host.Press(r.Launcher.Draw,ImGuiKey.Escape);r.Host.Frames(r.Launcher.Draw,2);
        Assert.Same(old,r.Launcher.Workspace);Assert.True(old!.Active!.Dirty);Assert.False(r.Launcher.SaveChangesPending);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label==$"Open {r.Second} instead?");
    }
    [Fact] public void Failed_save_or_destination_retains_workspace_and_sources()
    {
        using var r=new Rig();r.Dirty();var old=r.Launcher.Workspace!;old.Project.SaveCheck=_=>"refused by check";
        r.Drop();Assert.False(r.Launcher.AnswerFolderReplacement(true));Assert.False(r.Launcher.AnswerSaveChanges(SaveChangesChoice.Save));
        Assert.Same(old,r.Launcher.Workspace);Assert.True(old.Active!.Dirty);Assert.Contains("refused by check",r.Launcher.Said);
        Directory.Delete(r.Second,true);Assert.False(r.Launcher.AnswerSaveChanges(SaveChangesChoice.DontSave));
        Assert.Same(old,r.Launcher.Workspace);Assert.True(old.Active.Dirty);Assert.Equal("{\"value\":1}",File.ReadAllText(Path.Combine(r.First,"data","items.json")));
    }
}
