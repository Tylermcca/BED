using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Editor.App;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ProjectPlayBridgeTests
{
    private sealed class FakeSession : IProjectPlaySession
    {
        public int Starts,Stops,Updates,Disposals;public bool Build,Dirty,Missing;
        public bool CanBuildAndPlay=>Missing;
        public bool HasCompleted {get;set;}=true;
        public int? ExitCode=>null;
        public string Status=>Missing?"Build needed.":HasCompleted?"Stopped.":"Running.";
        public bool Start(bool hasUnsavedChanges,bool buildAndPlay=false){Starts++;Dirty=hasUnsavedChanges;Build=buildAndPlay;HasCompleted=Missing;return !Missing;}
        public void RequestStop(){Stops++;HasCompleted=true;}
        public void Update()=>Updates++;
        public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[new(false,"ordinary game output")];
        public void Draw() { }
        public void Dispose(){Disposals++;HasCompleted=true;}
    }
    private sealed class FakeHost : IProjectPlayHost
    {
        public readonly List<FakeSession> Sessions=[];public bool Missing;
        public IProjectPlaySession Start(PlayLaunch launch){var s=new FakeSession{Missing=Missing};Sessions.Add(s);return s;}
    }
    private sealed class Rig : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("project-play-").FullName;
        public string ProjectRoot=>Path.Combine(Root,"project");public string Local=>Path.Combine(Root,"local");
        public string SceneFile=>Path.Combine(ProjectRoot,"data","room.json");
        public readonly DataWorkspace Workspace;public readonly FakeHost Factory=new();
        public Rig(bool settings=true)
        {
            var data=Directory.CreateDirectory(Path.Combine(ProjectRoot,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(ProjectRoot,"schemas")).FullName;
            File.WriteAllText(SceneFile,new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)])));
            var project=DataProject.Open(data,schemas);Assert.True(DocumentHost.For(project).BindScene("room.json","scene",out _));
            Workspace=new(project);Assert.True(Workspace.Show("room.json"));
            File.WriteAllText(Path.Combine(ProjectRoot,"game.exe"),"inert fixture: fake host only");
            if(settings)PlaySettingsStore.Save(ProjectRoot,new("game.exe"));
            Workspace.BindPlaySettings(ProjectRoot,null,Local);Workspace.BindProjectPlay(Factory);
        }
        public void Edit()=>Assert.True(Workspace.SceneWindow!.Editor.Session.Execute(new MoveObject("box",Vector3.UnitX)));
        public void Dispose(){Workspace.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Real_toolbar_uses_common_host_console_stop_and_owner_update_without_linked_protocol()
    {
        using var r=new Rig();using var ui=new HeadlessHost(1200,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        ui.Frames(r.Workspace.Draw,3);
        var b=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Play");
        ui.Click(r.Workspace.Draw,b.Bounds.X+b.Bounds.Width/2,b.Bounds.Y+b.Bounds.Height/2);ui.Frames(r.Workspace.Draw,2);
        var s=Assert.Single(r.Factory.Sessions);Assert.Equal(1,s.Starts);Assert.False(s.Dirty);Assert.True(s.Updates>0);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="ordinary game output");
        Assert.True(r.Workspace.SceneWindow!.Run("editor.stop"));Assert.Equal(1,s.Stops);Assert.False(r.Workspace.SceneWindow.Editor.Session.Dirty);
        r.Workspace.Dispose();Assert.Equal(1,s.Disposals);
    }
    [Fact]
    public void Dirty_cancel_and_dont_save_leave_draft_and_disk_unchanged_until_explicit_saved_launch()
    {
        using var r=new Rig();var saved=File.ReadAllText(r.SceneFile);r.Edit();var session=r.Workspace.SceneWindow!.Editor.Session;
        var revision=session.Revision;var journal=session.Journal.Count;
        Assert.True(r.Workspace.RequestProjectPlay());Assert.True(r.Workspace.ProjectPlaySavePending);Assert.Empty(r.Factory.Sessions);
        Assert.False(r.Workspace.AnswerProjectPlay(SaveChangesChoice.Cancel));Assert.True(session.Dirty);Assert.Equal(saved,File.ReadAllText(r.SceneFile));
        Assert.True(r.Workspace.RequestProjectPlay());Assert.True(r.Workspace.AnswerProjectPlay(SaveChangesChoice.DontSave));
        Assert.False(Assert.Single(r.Factory.Sessions).Dirty);Assert.True(session.Dirty);Assert.Equal(revision,session.Revision);Assert.Equal(journal,session.Journal.Count);Assert.Equal(saved,File.ReadAllText(r.SceneFile));
    }
    [Fact]
    public void Typed_save_refusal_never_launches_and_success_saves_before_launch()
    {
        using var r=new Rig();r.Edit();var saved=File.ReadAllText(r.SceneFile);
        r.Workspace.Documents.SceneSaveCheck=_=>"Invented policy refusal.";
        Assert.True(r.Workspace.RequestProjectPlay());Assert.False(r.Workspace.AnswerProjectPlay(SaveChangesChoice.Save));Assert.Empty(r.Factory.Sessions);Assert.Equal(saved,File.ReadAllText(r.SceneFile));
        Assert.Contains("Invented policy refusal",r.Workspace.Said);r.Workspace.Documents.SceneSaveCheck=null;
        Assert.True(r.Workspace.AnswerProjectPlay(SaveChangesChoice.Save),r.Workspace.Said);
        Assert.False(r.Workspace.SceneWindow!.Editor.Session.Dirty);Assert.Equal(Vector3.UnitX,new SceneFormat().Read(File.ReadAllText(r.SceneFile)).Find("box")!.Position);Assert.Single(r.Factory.Sessions);
    }
    [Fact]
    public void Changed_documents_or_changed_command_cannot_consume_pending_approval()
    {
        using var r=new Rig();var external=Path.Combine(r.Root,"outside.exe");File.WriteAllText(external,"inert");PlaySettingsStore.Save(r.ProjectRoot,new(external,["first"]));
        Assert.False(r.Workspace.RequestProjectPlay());Assert.True(r.Workspace.ProjectPlayApprovalPending);Assert.Empty(r.Factory.Sessions);
        PlaySettingsStore.Save(r.ProjectRoot,new(external,["changed"]));Assert.False(r.Workspace.ApproveProjectPlay());Assert.Empty(r.Factory.Sessions);Assert.False(File.Exists(Path.Combine(r.Local,"play-approvals.json")));
        Assert.False(r.Workspace.RequestProjectPlay());r.Edit();Assert.False(r.Workspace.ApproveProjectPlay());Assert.Empty(r.Factory.Sessions);Assert.False(File.Exists(Path.Combine(r.Local,"play-approvals.json")));
    }
    [Fact]
    public void Build_requires_separate_exact_local_approval_and_no_project_approval_field()
    {
        using var r=new Rig();File.WriteAllText(Path.Combine(r.ProjectRoot,"build.exe"),"inert");
        PlaySettingsStore.Save(r.ProjectRoot,new("game.exe",build:new("./build.exe",["--build"])));r.Factory.Missing=true;
        Assert.False(r.Workspace.RequestProjectPlay());Assert.NotNull(r.Workspace.ProjectPlay);Assert.True(r.Workspace.ProjectPlay!.CanBuildAndPlay);Assert.Single(r.Factory.Sessions);
        Assert.False(r.Workspace.RequestProjectPlay(true));Assert.True(r.Workspace.ProjectPlayApprovalPending);Assert.Contains("build.exe",r.Workspace.Said);Assert.Single(r.Factory.Sessions);
        r.Factory.Missing=false;Assert.True(r.Workspace.ApproveProjectPlay());Assert.Equal(2,r.Factory.Sessions.Count);Assert.True(r.Factory.Sessions[1].Build);
        Assert.True(File.Exists(Path.Combine(r.Local,"play-approvals.json")));Assert.DoesNotContain("approval",File.ReadAllText(Path.Combine(r.ProjectRoot,".bed","project.json")),StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Missing_settings_opens_form_without_factory_and_external_change_refuses_saved_input()
    {
        using var r=new Rig(false);Assert.False(r.Workspace.RequestProjectPlay());Assert.True(r.Workspace.PlaySettings!.Open);Assert.Empty(r.Factory.Sessions);
        PlaySettingsStore.Save(r.ProjectRoot,new("game.exe"));File.AppendAllText(r.SceneFile," ");
        Assert.False(r.Workspace.RequestProjectPlay());Assert.Contains("choose which version to keep before Play",r.Workspace.Said);Assert.Empty(r.Factory.Sessions);
    }
    [Fact]
    public void App_facade_constructs_without_execution_and_delegates_dirty_refusal()
    {
        using var r=new Rig();var launch=new PlayLaunch(new(Path.Combine(r.ProjectRoot,"game.exe"),r.ProjectRoot,[]),null,r.ProjectRoot,[]);
        using var session=new ProjectPlayBridge().Start(launch);Assert.True(session.HasCompleted);Assert.False(session.Start(true));Assert.Contains("Save changes",session.Status);Assert.Empty(session.DrainLogs());
    }
    [Fact]
    public void Real_program_setup_plain_project_launch_and_close_dispose_only_its_owned_session()
    {
        var root=Directory.CreateTempSubdirectory("plain-play-launcher-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            Directory.CreateDirectory(Path.Combine(root,"schemas"));File.WriteAllText(Path.Combine(data,"item.json"),"{}");File.WriteAllText(Path.Combine(root,"schemas","item.schema.json"),"{}");
            File.WriteAllText(Path.Combine(root,"game.exe"),"inert");PlaySettingsStore.Save(root,new("game.exe"));
            var factory=new FakeHost();
            using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local","recent.json"))){ProjectPlayHost=factory};
            Bed.Launcher.Program.SetUp(launcher);Assert.Same(factory,launcher.ProjectPlayHost);
            Assert.True(launcher.OpenFolder(root),launcher.Said);Assert.True(launcher.Workspace!.Show("item.json"));
            Assert.True(launcher.Workspace.RequestProjectPlay());var owner=Assert.Single(factory.Sessions);Assert.False(owner.HasCompleted);
            Assert.True(launcher.Close(),launcher.Said);Assert.Null(launcher.Workspace);Assert.Equal(1,owner.Disposals);Assert.Equal("{}",File.ReadAllText(Path.Combine(data,"item.json")));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Workspace_real_approval_button_authorizes_only_displayed_external_run_then_build()
    {
        using var r=new Rig();var outside=Path.Combine(r.Root,"external.exe");File.WriteAllText(outside,"inert");
        File.WriteAllText(Path.Combine(r.ProjectRoot,"build.exe"),"inert");
        PlaySettingsStore.Save(r.ProjectRoot,new(outside,["literal"],build:new("./build.exe",["build-arg"])));
        using var ui=new HeadlessHost(1200,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        Assert.False(r.Workspace.RequestProjectPlay(true));Assert.Empty(r.Factory.Sessions);ui.Frames(r.Workspace.Draw,3);
        void Allow(){var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Allow this exact command and continue");ui.Click(r.Workspace.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);ui.Frames(r.Workspace.Draw,2);}
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains(outside)&&n.Label.Contains("literal"));
        Allow();Assert.Empty(r.Factory.Sessions);Assert.True(r.Workspace.ProjectPlayApprovalPending);Assert.Contains("build-arg",r.Workspace.Said);
        Allow();var session=Assert.Single(r.Factory.Sessions);Assert.True(session.Build);Assert.False(r.Workspace.ProjectPlayApprovalPending);
        Assert.Equal("inert",File.ReadAllText(outside));
    }

    [Theory]
    [InlineData("ok",0)][InlineData("crash",7)]
    public void Real_bridge_forwards_nullable_exit_code_after_owned_completion(string mode,int expected)
    {
        var root=Directory.CreateTempSubdirectory("bridge-exit-").FullName;
        try
        {
            using var session=new ProjectPlayBridge().Start(new(new(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame.exe"),root,[mode]),null,root,[]));
            Assert.Null(session.ExitCode);Assert.True(session.Start(false));
            var watch=System.Diagnostics.Stopwatch.StartNew();
            do {session.Update();if(!session.HasCompleted)Thread.Sleep(5);}while(!session.HasCompleted&&watch.Elapsed<TimeSpan.FromSeconds(5));
            session.Update();Assert.True(session.HasCompleted,session.Status);Assert.Equal(expected,session.ExitCode);
        }
        finally{Directory.Delete(root,true);}
    }

}
