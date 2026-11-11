using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Launcher;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class FrameConsolePause01Tests
{
    private sealed class Fake : IProjectPlaySession,IProjectFrameControlSession,IProjectPlayHost
    {
        public bool Available=true,Paused;public int Steps,Pauses,Resumes;
        public ProjectFrameState FrameControl=>new(Available,Paused,false,Steps,Available?Paused?"Simulation paused.":"Simulation running.":"Game has no frame helper.");
        public bool RequestFrameControl(ProjectFrameCommand command)
        {if(!Available||command==ProjectFrameCommand.Pause&&Paused||command!=ProjectFrameCommand.Pause&&!Paused)return false;switch(command){case ProjectFrameCommand.Pause:Paused=true;Pauses++;break;case ProjectFrameCommand.Step:Steps++;break;case ProjectFrameCommand.Resume:Paused=false;Resumes++;break;}return true;}
        public IProjectPlaySession Start(PlayLaunch launch)=>this;
        public bool CanBuildAndPlay=>false;public bool HasCompleted{get;private set;}=true;public int? ExitCode=>null;public string Status=>"Running.";
        public bool Start(bool dirty,bool build=false){HasCompleted=false;return true;}
        public void RequestStop()=>HasCompleted=true;public void Update(){}public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];public void Draw(){}public void Dispose()=>HasCompleted=true;
    }
    [Fact]
    public void Console_buttons_native_keys_and_palette_share_optional_control_without_saving()
    {
        var root=Directory.CreateTempSubdirectory("frame-ui-").FullName;
        using var host=new HeadlessHost(1200,1000){Drawing=false};var fake=new Fake();
        Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));File.WriteAllText(Path.Combine(root,"data","item.json"),"{}");File.WriteAllText(Path.Combine(root,"schemas","item.schema.json"),"{}");File.WriteAllText(Path.Combine(root,"game.exe"),"inert fixture");PlaySettingsStore.Save(root,new("game.exe"));
        EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());
        using var launcher=Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"local","recent.json"))){ProjectPlayHost=fake});
        try
        {
            Assert.True(launcher.OpenFolder(root),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("item.json"));host.Frames(workspace.Draw,3);Assert.True(workspace.RequestProjectPlay());host.Frames(workspace.Draw,3);
            UiNode Node(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);
            void Click(string label){var b=Node(label).Bounds;host.Click(workspace.Draw,b.X+b.Width/2,b.Y+b.Height/2);host.Frames(workspace.Draw,2);}
            Assert.False(Node("Step one frame (F10)").Enabled);Click("Pause simulation (F9)");Assert.True(fake.Paused);Click("Step one frame (F10)");Assert.Equal(1,fake.Steps);
            Assert.True(workspace.Window!.Run("editor.resume_simulation"));Assert.False(fake.Paused);
            host.Press(workspace.Draw,ImGuiKey.F9);host.Frames(workspace.Draw,2);Assert.True(fake.Paused);
            host.Press(workspace.Draw,ImGuiKey.F10);Assert.Equal(2,fake.Steps);host.Press(workspace.Draw,ImGuiKey.F11);Assert.False(fake.Paused);
            fake.Available=false;host.Frames(workspace.Draw,2);Assert.False(Node("Pause simulation (F9)").Enabled);Assert.False(workspace.Window.Run("editor.pause_simulation"));
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label=="Running. Frame control unavailable.");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Game has no frame helper.");Assert.Equal("{}",File.ReadAllText(Path.Combine(root,"data","item.json")));
        }
        finally{launcher.Dispose();Directory.Delete(root,true);}
    }
}
