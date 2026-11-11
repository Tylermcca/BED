using System.Diagnostics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using StarterAdapter;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ManagedSourceHooksDoc19Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("managed-hooks-doc19-").FullName;
        public readonly HeadlessHost Host=new(1100,800){Drawing=false};
        public readonly DataProject Project;public readonly DataWorkspace Workspace;
        public string Data=>Path.Combine(Root,"sources");
        public Rig(bool readOnly=false,string text="seed")
        {
            Directory.CreateDirectory(Path.Combine(Data,"entries","sub"));Directory.CreateDirectory(Path.Combine(Data,"generated"));var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"source.schema.json"),"""{"type":"object","required":["text"],"properties":{"text":{"type":"string","minLength":1}}}""");
            File.WriteAllText(Path.Combine(Data,"index.json"),JsonSerializer.Serialize(new{text}));
            File.WriteAllText(Path.Combine(Data,"entries","one.json"),"""{"text":"other"}""");
            File.WriteAllText(Path.Combine(Data,"entries","sub","two.json"),"""{"text":"third"}""");
            File.WriteAllText(Path.Combine(Data,"generated","keep.json"),"not an authored document");File.WriteAllText(Path.Combine(Data,"ignored.json"),"also excluded");
            Project=ManagedSourceHooks.OpenSources(new(Data,schemas){Root=Root},readOnly);Workspace=new(Project);EditorWindow<DataDocument>.Configure(ImGui.GetIO());Assert.True(Workspace.Show("index.json"));
        }
        public DataFile Index=>Project.Find("index.json")!;
        public void Set(string value)=>Assert.True(Workspace.Window!.Editor.Execute(new SetValue("/text",JsonSerializer.SerializeToElement(value))));
        public void Finish(){var timer=Stopwatch.StartNew();while(Workspace.SavePending("index.json")&&timer.Elapsed<TimeSpan.FromSeconds(8)){Workspace.FinishSaves();Thread.Sleep(1);}Assert.False(Workspace.SavePending("index.json"),Workspace.Said);}
        public void Dispose(){Workspace.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Fact]
    public void Admission_is_recursive_and_generated_or_unregistered_files_never_become_documents()
    {
        using var r=new Rig();Assert.Equal(new[]{"entries/one.json","entries/sub/two.json","index.json"},r.Project.Files.Select(f=>f.Name).Order());
        Assert.Null(r.Project.Find("generated/keep.json"));Assert.Null(r.Project.Find("ignored.json"));
        Assert.False(r.Project.CreateFromText("generated/new.json","{\"text\":\"valid\"}",out var why));Assert.Contains("outside this project's data folder",why);
        Assert.Equal("not an authored document",File.ReadAllText(Path.Combine(r.Data,"generated","keep.json")));
    }
    [Fact]
    public void Saved_observer_runs_after_commit_once_noop_is_silent_and_disposal_unsubscribes()
    {
        using var r=new Rig();var calls=0;var owner=Environment.CurrentManagedThreadId;var actual=0;string[] names=[];
        using var subscription=ManagedSourceHooks.AfterSaved(r.Project,files=>{calls++;actual=Environment.CurrentManagedThreadId;names=files.ToArray();Assert.False(r.Index.Dirty);Assert.Equal(r.Index.Session!.ExpectedOnDisk,File.ReadAllText(r.Index.Path));});
        r.Set("changed");Assert.True(r.Workspace.Window!.Run("editor.save"));Assert.Equal(1,calls);Assert.Equal(owner,actual);Assert.Equal(new[]{"index.json"},names);
        Assert.True(r.Workspace.Window.Run("editor.save"));Assert.Equal(1,calls);
        subscription.Dispose();r.Set("after disposal");Assert.True(r.Workspace.Window.Run("editor.save"));Assert.Equal(1,calls);
    }
    [Fact]
    public void Completed_work_is_delivered_once_by_owner_pump_with_no_panel_draw_and_can_be_unhooked()
    {
        using var r=new Rig();var result=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);var owner=Environment.CurrentManagedThreadId;var calls=0;
        using var subscription=ManagedSourceHooks.DeliverOnTick(r.Workspace,result.Task,value=>{Assert.Equal(42,value);Assert.Equal(owner,Environment.CurrentManagedThreadId);calls++;},why=>Assert.Fail(why));
        r.Workspace.FinishSaves();Assert.Equal(0,calls);result.SetResult(42);r.Workspace.FinishSaves();r.Workspace.FinishSaves();Assert.Equal(1,calls);
        var ignored=new TaskCompletionSource<int>();using var removed=ManagedSourceHooks.DeliverOnTick(r.Workspace,ignored.Task,_=>calls++,_=>calls++);removed.Dispose();ignored.SetResult(1);r.Workspace.FinishSaves();Assert.Equal(1,calls);
        var failed=0;using var cancelled=ManagedSourceHooks.DeliverOnTick(r.Workspace,Task.FromCanceled<int>(new CancellationToken(true)),_=>Assert.Fail("Cancelled result accepted"),why=>{Assert.Contains("cancelled",why);failed++;});r.Workspace.FinishSaves();r.Workspace.FinishSaves();Assert.Equal(1,failed);
    }
    [Fact]
    public void Background_check_refuses_oversize_then_commits_on_owner_and_ignores_other_unsaved_drafts()
    {
        using var r=new Rig();ManagedSourceHooks.ConfigureBackgroundCheck(r.Workspace,30);var check=r.Project.SaveCheck!;var thread=0;var owner=Environment.CurrentManagedThreadId;var calls=0;
        r.Project.SaveCheck=file=>{thread=Environment.CurrentManagedThreadId;return check(file);};using var observer=ManagedSourceHooks.AfterSaved(r.Project,_=>{Assert.Equal(owner,Environment.CurrentManagedThreadId);calls++;});
        var before=File.ReadAllBytes(r.Index.Path);r.Set(new string('x',40));Assert.False(r.Workspace.Window!.Run("editor.save"));r.Finish();Assert.NotEqual(owner,thread);Assert.True(r.Index.Dirty);Assert.Equal(before,File.ReadAllBytes(r.Index.Path));Assert.Contains("exceeds 30",r.Workspace.Said);Assert.Equal(0,calls);
        var other=r.Project.Find("entries/one.json")!;Assert.True(other.Open());Assert.True(other.Session!.Execute(new SetValue("/text",JsonSerializer.SerializeToElement(new string('y',40)))));
        r.Set("small");Assert.False(r.Workspace.Window.Run("editor.save"));r.Finish();Assert.False(r.Index.Dirty);Assert.True(other.Dirty);Assert.Equal(1,calls);Assert.Contains("small",File.ReadAllText(r.Index.Path));Assert.Equal("{\"text\":\"other\"}",File.ReadAllText(other.Path));
    }
    [Fact]
    public void Multiline_sample_types_uses_the_same_session_saves_and_refreshes_after_undo()
    {
        using var r=new Rig();var editor=r.Workspace.Window!.Editor;var panel=ManagedSourceHooks.TextPanel(r.Index,editor);
        void Draw(){ImGui.SetNextWindowSize(new float2(900,600));Ui.Begin("Own source panel"u8);panel.Draw();Ui.End();}
        r.Host.Frames(Draw,3);var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Source prose");
        r.Host.Click(Draw,node.Bounds.X+15,node.Bounds.Y+12);r.Host.Press(Draw,ImGuiKey.A,ctrl:true);r.Host.Type(Draw,"First line\nSecond line");r.Host.Frames(Draw,2);
        Assert.Equal("First line\nSecond line",r.Index.Session!.Current.At("/text")!.Value.GetString());Assert.True(r.Index.Dirty);Assert.True(r.Index.Session.Undo());r.Host.Frames(Draw,2);Assert.Equal("seed",Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField).Value);
        node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField);var ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"Saved first\nSaved second");r.Host.Frames(Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.True(r.Workspace.Window.Run("editor.save"));Assert.False(r.Index.Dirty);Assert.Contains("Saved first",File.ReadAllText(r.Index.Path));
        node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField);ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"");r.Host.Frames(Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.Equal("Saved first\nSaved second",r.Index.Session.Current.At("/text")!.Value.GetString());Assert.False(r.Index.Dirty);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.Length>0);
    }
    [Fact]
    public void Multiline_sample_refuses_readonly_or_oversize_sources_without_a_hidden_edit()
    {
        using(var r=new Rig(readOnly:true))
        {
            var panel=ManagedSourceHooks.TextPanel(r.Index,r.Workspace.Window!.Editor);void Draw(){Ui.Begin("Own readonly"u8);panel.Draw();Ui.End();}r.Host.Frames(Draw,3);var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField);Assert.False(node.Actions.HasFlag(UiActions.SetValue));Assert.Equal(UiActionOutcome.NotAvailable,Ui.RequestAction(node.Id,UiActions.SetValue,"change").Outcome);Assert.False(r.Index.Dirty);
        }
        using(var r=new Rig(text:new string('é',3000)))
        {
            var panel=ManagedSourceHooks.TextPanel(r.Index,r.Workspace.Window!.Editor);void Draw(){Ui.Begin("Own oversize"u8);panel.Draw();Ui.End();}var before=File.ReadAllBytes(r.Index.Path);r.Host.Frames(Draw,3);Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("exceeds its buffer",StringComparison.Ordinal));Assert.False(r.Index.Dirty);Assert.Equal(before,File.ReadAllBytes(r.Index.Path));
        }
    }
}
