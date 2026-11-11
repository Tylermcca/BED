using System.Text.Json;
using Bed.Data;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;
public sealed class LauncherSaveDecisionTests : IDisposable
{
    private readonly string root=Directory.CreateTempSubdirectory("save-decision-").FullName;
    private readonly ProjectLauncher launcher;
    private readonly ProjectPaths first;
    public LauncherSaveDecisionTests()
    {
        first=Make("first");launcher=new(new RecentProjects(Path.Combine(root,"recent.json")));
        Assert.True(launcher.Open(first));
    }
    private ProjectPaths Make(string name)
    {
        var data=Directory.CreateDirectory(Path.Combine(root,name,"data")).FullName;
        var schemas=Directory.CreateDirectory(Path.Combine(root,name,"schemas")).FullName;
        File.WriteAllText(Path.Combine(data,"values.json"),"{\"value\":1}");
        File.WriteAllText(Path.Combine(schemas,"values.schema.json"),"{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"number\"}}}");
        return new(data,schemas);
    }
    private void Dirty()=>Assert.True(launcher.Workspace!.Active!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(2))));
    public void Dispose(){launcher.Dispose();Directory.Delete(root,true);}

    [Theory]
    [InlineData(SaveChangesChoice.Save)][InlineData(SaveChangesChoice.DontSave)][InlineData(SaveChangesChoice.Cancel)]
    public void Os_close_uses_same_decision_after_clearing_native_request(SaveChangesChoice choice)
    {
        Dirty();var workspace=launcher.Workspace;var native=true;
        Assert.False(ShellClose.Decide(native,()=>native=false,launcher.RequestWindowClose));
        Assert.False(native);Assert.True(launcher.SaveChangesPending);Assert.Same(workspace,launcher.Workspace);
        launcher.AnswerSaveChanges(choice);
        Assert.Equal(choice!=SaveChangesChoice.Cancel,ShellClose.Decide(false,()=>native=false,launcher.RequestWindowClose));
        if(choice==SaveChangesChoice.Cancel){Assert.Same(workspace,launcher.Workspace);Assert.True(workspace!.Active!.Dirty);}
        else Assert.Null(launcher.Workspace);
        var text=File.ReadAllText(Path.Combine(first.Data,"values.json"));
        Assert.Equal(choice==SaveChangesChoice.Save?2:1,JsonDocument.Parse(text).RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Clean_os_close_is_immediate()
    { Assert.True(launcher.RequestWindowClose(true));Assert.Null(launcher.Workspace);Assert.False(launcher.SaveChangesPending); }

    [Fact]
    public void Failed_save_keeps_pending_decision_and_exact_draft()
    {
        Dirty();var w=launcher.Workspace!;var session=w.Active!.Session;
        w.Project.SaveCheck=_=>"synthetic refusal";
        Assert.False(launcher.RequestWindowClose(true));Assert.False(launcher.AnswerSaveChanges(SaveChangesChoice.Save));
        Assert.True(launcher.SaveChangesPending);Assert.Same(w,launcher.Workspace);Assert.Same(session,w.Active!.Session);
        Assert.True(w.Active.Dirty);Assert.Contains("synthetic refusal",launcher.Said);Assert.False(launcher.RequestWindowClose(false));
    }

    [Fact]
    public void Replacement_is_deferred_and_failed_destination_does_not_destroy_old_draft()
    {
        Dirty();var w=launcher.Workspace;var next=Make("next");
        Assert.False(launcher.Open(next));Assert.Same(w,launcher.Workspace);
        Assert.False(launcher.AnswerSaveChanges(SaveChangesChoice.Cancel));Assert.Same(w,launcher.Workspace);
        Assert.False(launcher.Open(new ProjectPaths(Path.Combine(root,"absent"),next.Schemas)));
        Assert.False(launcher.AnswerSaveChanges(SaveChangesChoice.DontSave));Assert.Same(w,launcher.Workspace);Assert.True(w!.Active!.Dirty);
        Assert.False(launcher.Open(next));Assert.True(launcher.AnswerSaveChanges(SaveChangesChoice.DontSave));
        Assert.NotSame(w,launcher.Workspace);Assert.Equal(next.Data,launcher.Current!.Data);
        Assert.Equal(1,JsonDocument.Parse(File.ReadAllText(Path.Combine(first.Data,"values.json"))).RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Create_is_not_attempted_before_cancelled_decision()
    {
        Dirty();var w=launcher.Workspace;
        Assert.False(launcher.CreateProject("not-a-template",root,"new-project"));
        Assert.True(launcher.SaveChangesPending);Assert.False(Directory.Exists(Path.Combine(root,"new-project")));
        Assert.False(launcher.AnswerSaveChanges(SaveChangesChoice.Cancel));Assert.Same(w,launcher.Workspace);
        Assert.False(Directory.Exists(Path.Combine(root,"new-project")));
    }
}
