using System.Numerics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class WorkspaceStatusUi12Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("workspace-status-ui12-").FullName;
        public string ProjectRoot=>Path.Combine(Root,"project");
        public string Data=>Path.Combine(ProjectRoot,"content","data");
        public readonly DataWorkspace Workspace;
        public Rig()
        {
            Directory.CreateDirectory(Data);var schemas=Directory.CreateDirectory(Path.Combine(ProjectRoot,"schemas")).FullName;
            File.WriteAllText(Path.Combine(Data,"items.json"),"""{"items":[{"name":"Alpha","value":1}]}""");
            File.WriteAllText(Path.Combine(schemas,"items.schema.json"),"""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"value":{"type":"number"}}}}}}""");
            File.WriteAllText(Path.Combine(Data,"level.json"),new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)])));
            Workspace=new(DataProject.Open(Data,schemas));Assert.True(Workspace.Documents.BindScene("level.json","scene",out var why),why);
        }
        public void BindPlay(FakeHost host)
        {
            File.WriteAllText(Path.Combine(ProjectRoot,"game.exe"),"inert: fake host never starts a process");
            PlaySettingsStore.Save(ProjectRoot,new("game.exe"));
            Workspace.BindPlaySettings(ProjectRoot,null,Path.Combine(Root,"local"));Workspace.BindProjectPlay(host);
        }
        public void Dispose(){Workspace.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Root,StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    private sealed class FakeHost:IProjectPlayHost
    {
        public FakeSession? Last;
        public IProjectPlaySession Start(PlayLaunch launch)=>Last=new();
    }
    private sealed class FakeSession:IProjectPlaySession
    {
        public string Status {get;set;}="Building saved project files.";
        public bool CanBuildAndPlay=>false;
        public bool HasCompleted {get;set;}
        public int? ExitCode {get;set;}
        public int Updates;
        public bool Start(bool dirty,bool buildAndPlay=false){Assert.False(dirty);return true;}
        public void RequestStop(){HasCompleted=true;Status="Stopped.";}
        public void Update()=>Updates++;
        public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];
        public void Draw() { }
        public void Dispose()=>HasCompleted=true;
    }
    private sealed class Preview:IPlaySession
    {public string Status=>"Preview running.";public void Draw(){}public void Dispose(){}}
    private static UiNode Status(string prefix)
    {
        var snapshot=Ui.Snapshot();var bar=Assert.Single(snapshot.Nodes,n=>n.Role==UiRole.Window&&n.Label=="Status bar");
        var index=snapshot.Nodes.ToList().IndexOf(bar);
        return Assert.Single(snapshot.Nodes,n=>n.Parent==index&&n.Role==UiRole.Text&&n.Label.StartsWith(prefix,StringComparison.Ordinal));
    }
    [Theory][InlineData(400)][InlineData(1200)]
    public void Status_reserves_bottom_space_and_counts_JSON_and_typed_scene_drafts_then_saves_without_mutating_history(int width)
    {
        using var r=new Rig();using var host=new HeadlessHost(width,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var w=r.Workspace;host.Frames(w.Draw,3);
        Assert.Equal("Project: data ("+r.Data+")",Status("Project:").Label);Assert.Equal("Active file: None",Status("Active file:").Label);
        Assert.Equal("Unsaved changes: None",Status("Unsaved changes:").Label);Assert.Equal("Play: Not running.",Status("Play:").Label);
        var ids=new[]{Status("Project:").Id,Status("Active file:").Id,Status("Unsaved changes:").Id,Status("Play:").Id};
        Assert.True(w.Show("items.json"));var data=w.Window!.Editor.Session;
        Assert.True(data.Execute(new SetValue("/items/0/value",JsonSerializer.SerializeToElement(2))));host.Frames(w.Draw,3);
        Assert.Equal("Active file: items.json",Status("Active file:").Label);Assert.Equal("Unsaved changes: 1 file",Status("Unsaved changes:").Label);
        Assert.True(w.Show("level.json"));var scene=w.SceneWindow!.Editor.Session;
        Assert.True(scene.Execute(new MoveObject("box",Vector3.UnitX)));host.Frames(w.Draw,3);
        Assert.Equal("Active file: level.json",Status("Active file:").Label);Assert.Equal("Unsaved changes: 2 files",Status("Unsaved changes:").Label);
        Assert.True(w.Project.Find("items.json")!.Save());host.Frames(w.Draw,2);Assert.Equal("Unsaved changes: 1 file",Status("Unsaved changes:").Label);
        Assert.True(w.Documents.SaveScene("level.json",out var why),why);host.Frames(w.Draw,2);Assert.Equal("Unsaved changes: None",Status("Unsaved changes:").Label);
        var saved=File.ReadAllText(Path.Combine(r.Data,"level.json"));Assert.Equal(1,new SceneFormat().Read(saved).Find("box")!.Position.X);
        var journal=scene.Journal.Count;var revision=scene.Revision;host.Frames(w.Draw,3);
        Assert.Equal(journal,scene.Journal.Count);Assert.Equal(revision,scene.Revision);Assert.Equal(saved,File.ReadAllText(Path.Combine(r.Data,"level.json")));
        Assert.Equal(ids,new[]{Status("Project:").Id,Status("Active file:").Id,Status("Unsaved changes:").Id,Status("Play:").Id});
        var bar=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Status bar");
        Assert.InRange(bar.Bounds.Y,host.Height-120,host.Height-20);Assert.InRange(bar.Bounds.Y+bar.Bounds.Height,host.Height-1,host.Height+1);
        var viewport=ImGui.GetMainViewport();Assert.True(viewport.WorkPos.Y+viewport.WorkSize.Y<=bar.Bounds.Y+1);
        Assert.All(new[]{Status("Project:"),Status("Active file:"),Status("Unsaved changes:"),Status("Play:")},n=>
        {Assert.Equal(UiIdKind.Semantic,n.IdKind);Assert.Equal(UiActions.None,n.Actions);Assert.InRange(n.Bounds.Y,bar.Bounds.Y,bar.Bounds.Y+bar.Bounds.Height);});
        Assert.NotNull(Status("Project:").TextEvidence);Assert.False(Status("Project:").TextEvidence!.Value.Truncated);Assert.Equal(Status("Project:").Label,Status("Project:").Tooltip);
    }
    [Fact]
    public void Bound_root_and_common_Play_states_follow_the_existing_owner_without_extra_update_or_approval_side_effects()
    {
        using var r=new Rig();var factory=new FakeHost();r.BindPlay(factory);using var host=new HeadlessHost(1200,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var w=r.Workspace;Assert.True(w.Show("level.json"));host.Frames(w.Draw,3);
        Assert.Equal("Project: project ("+r.ProjectRoot+")",Status("Project:").Label);var id=Status("Play:").Id;
        Assert.True(w.RequestProjectPlay());var owner=Assert.IsType<FakeSession>(factory.Last);
        foreach(var state in new[]{"Building saved project files.","Running.","The process exited normally (code 0).","Play failed: test failure."})
        {
            owner.Status=state;owner.HasCompleted=state.Contains("exited",StringComparison.Ordinal)||state.StartsWith("Play failed",StringComparison.Ordinal);
            var updates=owner.Updates;host.Frames(w.Draw,2);Assert.Equal(updates+2,owner.Updates);
            Assert.Equal("Play: "+state,Status("Play:").Label);Assert.Equal(id,Status("Play:").Id);
        }
        Assert.True(w.SceneWindow!.Editor.Session.Execute(new MoveObject("box",Vector3.UnitX)));
        Assert.True(w.RequestProjectPlay());Assert.True(w.ProjectPlaySavePending);host.Frames(w.Draw,2);
        Assert.Equal("Play: Waiting for save choice.",Status("Play:").Label);w.AnswerProjectPlay(SaveChangesChoice.Cancel);
        Assert.False(File.Exists(Path.Combine(r.Root,"local","play-approvals.json")));Assert.True(w.SceneWindow.Editor.Session.Dirty);
    }
    [Fact]
    public void Embedded_preview_and_replaced_workspace_report_their_own_status_without_stale_project_labels()
    {
        using var first=new Rig();using var second=new Rig();using var host=new HeadlessHost(1200,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var w=first.Workspace;Assert.True(w.Show("items.json"));var editor=w.Window!.Editor;
        editor.Add(new PlayMode<DataDocument>("fixture","Preview",_=>new Preview()));Assert.True(editor.Play("fixture"));host.Frames(w.Draw,3);
        Assert.Equal("Play: Preview running.",Status("Play:").Label);Assert.True(editor.Stop());host.Frames(w.Draw,2);Assert.Equal("Play: Not running.",Status("Play:").Label);
        var id=Status("Project:").Id;host.Frames(second.Workspace.Draw,3);
        Assert.Equal("Project: data ("+second.Data+")",Status("Project:").Label);Assert.Equal(id,Status("Project:").Id);
        Assert.Equal("Active file: None",Status("Active file:").Label);Assert.Equal("Unsaved changes: None",Status("Unsaved changes:").Label);
    }
}
