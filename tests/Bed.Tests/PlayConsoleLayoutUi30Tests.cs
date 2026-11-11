using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlayConsoleLayoutUi30Tests
{
    private sealed class Fake:IProjectPlaySession,IProjectFrameControlSession,IProjectPlayHost
    {
        public bool Paused,Available=true;public int Steps;public string Line="";private bool drained;
        public ProjectFrameState FrameControl=>new(Available,Paused,false,Steps,Available?Paused?"Simulation paused.":"Simulation running.":"This game hasn't enabled frame control yet.");
        public bool RequestFrameControl(ProjectFrameCommand command){if(!Available)return false;switch(command){case ProjectFrameCommand.Pause:Paused=true;break;case ProjectFrameCommand.Step:Steps++;break;case ProjectFrameCommand.Resume:Paused=false;break;}return true;}
        public IProjectPlaySession Start(PlayLaunch launch)=>this;
        public bool Start(bool dirty,bool buildAndPlay=false)=>!dirty;public bool HasCompleted=>false;public bool CanBuildAndPlay=>false;public int? ExitCode=>null;public string Status=>"Running own fixture.";
        public void RequestStop(){}public void Update(){}public void Draw(){}public void Dispose(){}
        public IReadOnlyList<ProjectPlayOutput> DrainLogs(){if(drained)return [];drained=true;return [new(false,Line)];}
    }
    [Theory][InlineData(1f,false)][InlineData(2f,false)][InlineData(2f,true)]
    public void Console_controls_and_long_diagnostic_are_readable_and_keyboard_reachable_at_1024(float scale,bool unavailable)
    {
        var root=Directory.CreateTempSubdirectory("console-ui30-").FullName;var old=EditorTheme.Preference;
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));Directory.CreateDirectory(Path.Combine(root,"local"));
            var file="item_"+new string('x',75)+"_界.json";var field=new string('f',80);var pointer="/items/0/"+field;
            var text=JsonSerializer.Serialize(new{items=new[]{new Dictionary<string,string>{{field,"Own value"}}}});
            File.WriteAllText(Path.Combine(root,"data",file),text);File.WriteAllText(Path.Combine(root,"schemas",Path.GetFileNameWithoutExtension(file)+".schema.json"),"{}");
            File.WriteAllText(Path.Combine(root,"game.exe"),"Not executed");PlaySettingsStore.Save(root,new("game.exe"));
            File.WriteAllText(Path.Combine(root,"local/settings.json"),JsonSerializer.Serialize(new{theme="HighContrast",textScale=scale}));
            var fake=new Fake{Available=!unavailable,Line=ProjectDiagnostic.Format("data/"+file,pointer,"Own diagnostic with a long saved location.")};
            using var host=new HeadlessHost(1024,900){Drawing=false};EditorWindow<DataDocument>.Configure(Im.GetIO());
            using var launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json"))){ProjectPlayHost=fake});
            Assert.True(launcher.OpenFolder(root),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show(file),workspace.Said);
            host.Frames(launcher.Draw,3);Assert.True(workspace.RequestProjectPlay(),workspace.Said);host.Frames(launcher.Draw,3);
            const string name="Play console###project.play.console";var window=Im.Internal.FindWindowByName(name);Assert.False(window.IsNull());
            Im.Internal.DockContextProcessUndockWindow(Im.GetCurrentContext(),window,true);Im.SetWindowPos(name,new(0,30),ImGuiCond.Always);Im.SetWindowSize(name,new(1008,740),ImGuiCond.Always);Im.SetWindowFocus(name);host.Frames(launcher.Draw,3);
            UiNode Node(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role==UiRole.Button);
            void Visible(UiNode n)
            {
                Assert.True(n.Bounds.X>=window.InnerClipRect.Min.X&&n.Bounds.X+n.Bounds.Width<=window.InnerClipRect.Max.X,n.Label);
                Assert.NotNull(n.TextEvidence);Assert.False(n.TextEvidence!.Value.Truncated,n.Label);
            }
            foreach(var label in new[]{"Clear console","Copy all","Pause simulation (F9)","Step one frame (F10)","Resume simulation (F11)"})Visible(Node(label));
            Assert.Equal(!unavailable,Node("Pause simulation (F9)").Enabled);
            var expected="Open data/"+file+" "+pointer;
            var link=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Link&&n.Label==expected);Visible(link);
            // Width changes must preserve the native action identity and full semantic location.
            var id=link.Id;Im.SetWindowSize(name,new(960,740),ImGuiCond.Always);host.Frames(launcher.Draw,3);
            link=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Link&&n.Label==expected);Assert.Equal(id,link.Id);Visible(link);
            void Reach(string label,UiRole role)
            {
                for(var i=0;i<40;i++){if(Ui.Snapshot().Nodes.Any(n=>n.Focused&&n.Label==label&&n.Role==role))return;host.Press(launcher.Draw,ImGuiKey.Tab);}
                Assert.Fail("Keyboard did not reach "+label+"; enabled="+Node(label).Enabled+"; "+string.Join("|",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Label)));
            }
            Reach("Copy all",UiRole.Button);host.Press(launcher.Draw,ImGuiKey.Enter);Assert.Contains(fake.Line,Im.GetClipboardText().ToString());
            if(!unavailable)
            {
                Reach("Pause simulation (F9)",UiRole.Button);host.Press(launcher.Draw,ImGuiKey.Enter);Assert.True(fake.Paused);
                Reach("Step one frame (F10)",UiRole.Button);host.Press(launcher.Draw,ImGuiKey.Enter);Assert.Equal(1,fake.Steps);
                Reach("Resume simulation (F11)",UiRole.Button);host.Press(launcher.Draw,ImGuiKey.Enter);Assert.False(fake.Paused);
            }
            Reach(expected,UiRole.Link);link=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Link&&n.Label==expected);Visible(link);
            host.Press(launcher.Draw,ImGuiKey.Enter);Assert.Equal("/items/0",workspace.Window!.Editor.Primary);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==field&&n.Focused);
            Assert.Equal(text,File.ReadAllText(Path.Combine(root,"data",file)));Assert.Empty(workspace.Project.Dirty);
        }
        finally{EditorTheme.Preference=old;Directory.Delete(root,true);}
    }
}
