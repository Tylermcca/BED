using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ConsoleStatusUi31Tests
{
    private sealed class Session:IProjectPlayHost,IProjectPlaySession,IProjectFrameControlSession
    {
        public ProjectFrameState FrameControl{get;set;}=new(true,false,false,0,"Running.");
        public bool HasCompleted{get;set;}public int? ExitCode{get;set;}public string Status{get;set;}="Running.";public bool CanBuildAndPlay=>false;
        public IProjectPlaySession Start(PlayLaunch launch)=>this;public bool Start(bool dirty,bool buildAndPlay=false)=>!dirty;public bool RequestFrameControl(ProjectFrameCommand command)=>false;
        public void RequestStop()=>Status="Requested polite close; waiting up to 5 seconds.";public void Update(){}public void Dispose(){}public void Draw(){}
        private bool logged;public IReadOnlyList<ProjectPlayOutput> DrainLogs(){if(logged)return [];logged=true;return [new(false,"Own log line")];}
    }
    private sealed class Sink:IAccessKitUpdateSink
    {public readonly List<AccessKitProjectedUpdate> Updates=[];public bool TryApply(AccessKitProjectedUpdate update){Updates.Add(update);return true;}}
    [Fact]
    public void One_status_below_toolbar_tracks_acknowledged_ticks_unknown_and_exit_without_idle_reannouncements()
    {
        var root=Directory.CreateTempSubdirectory("console-status-ui31-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));File.WriteAllText(Path.Combine(root,"data/item.json"),"{}");File.WriteAllText(Path.Combine(root,"schemas/item.schema.json"),"{}");File.WriteAllText(Path.Combine(root,"game.exe"),"Not executed");PlaySettingsStore.Save(root,new("game.exe"));
            using var host=new HeadlessHost(1200,950){Drawing=false};EditorWindow<DataDocument>.Configure(Im.GetIO());var session=new Session();
            using var launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json"))){ProjectPlayHost=session});
            Assert.True(launcher.OpenFolder(root));var workspace=launcher.Workspace!;Assert.True(workspace.Show("item.json"));host.Frames(launcher.Draw,3);Assert.True(workspace.RequestProjectPlay());
            var projection=new AccessKitProjection();var sink=new Sink();
            void Frame(int count=1){host.Frames(launcher.Draw,count);projection.Submit(Ui.Snapshot(),"Own status fixture",new(0,0,1200,950),1,1,sink);}
            UiNode Status(){var nodes=Ui.Snapshot().Nodes;return Assert.Single(nodes,n=>n.Role==UiRole.Status&&n.Parent>=0&&nodes[n.Parent].Label=="Play console");}
            Frame(3);var id=Status().Id;Assert.Equal("Running.",Status().Label);
            var snapshot=Ui.Snapshot();var filters=snapshot.Nodes.Where(n=>n.Role==UiRole.Checkbox&&n.Label is "Show game output" or "Show game errors").ToArray();Assert.Equal(2,filters.Length);
            Assert.True(Status().Bounds.Y>=filters.Max(n=>n.Bounds.Y+n.Bounds.Height));Assert.True(Status().Bounds.Y<Assert.Single(snapshot.Nodes,n=>n.Label=="Own log line").Bounds.Y);
            int Changes()=>sink.Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.Role==AccessKitProjectedRole.Status&&n.SourceId==id);
            var initial=Changes();Assert.Equal(1,initial);Frame(5);Assert.Equal(initial,Changes());
            void State(ProjectFrameState state,string expected){session.FrameControl=state;Frame();Assert.Equal(id,Status().Id);Assert.Equal(expected,Status().Label);}
            State(new(true,true,false,13,"Simulation paused after 13 ticks."),"Paused after 13 ticks.");
            State(new(true,true,true,13,"Waiting for the next simulation boundary."),"Waiting for simulation acknowledgement.");
            State(new(false,false,false,13,"Frame control disconnected; simulation state is unknown."),"Frame control unknown.");
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label==session.FrameControl.Status);
            Assert.True(workspace.StopProjectPlay());session.ExitCode=143;session.HasCompleted=true;Frame();
            Assert.Equal(id,Status().Id);Assert.Equal("Stopped (code 143).",Status().Label);
            var terminal=Changes();Frame(5);Assert.Equal(terminal,Changes());
            Assert.Contains(sink.Updates.SelectMany(u=>u.ChangedNodes),n=>n.SourceId==id&&n.Role==AccessKitProjectedRole.Status&&n.StableIdentity);
            Assert.Equal("{}",File.ReadAllText(Path.Combine(root,"data/item.json")));Assert.Empty(workspace.Project.Dirty);
        }
        finally{Directory.Delete(root,true);}
    }
}
