using System.Runtime.InteropServices;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AccessWalk05Tests
{
    private sealed class Rig:IDisposable
    {
        internal readonly SpriteSampleTests.Copy Copy=new();
        internal readonly HeadlessHost Host;
        internal readonly ProjectLauncher Launcher;
        internal DataWorkspace Workspace=>Launcher.Workspace!;
        internal AiConnectionLocations Locations {get;}
        internal Rig()
        {
            Copy.OpenAndCompile();Host=new(1600,1000){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
            var creator=new ProjectCreator(Path.Combine(repo!.FullName,"src/Bed.Agent.Cli/bin/Release/net10.0/samples/recipes"));
            var adapter=Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded);
            Launcher=global::Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(Copy.Settings,"recent.json")),creator:creator,projectsFolder:Copy.Settings){Loaded=[adapter]});
            var cli=Path.Combine(Copy.Settings,"connect");Directory.CreateDirectory(cli);File.WriteAllText(Path.Combine(cli,"bed-cli.exe"),"fixture; never executed");
            Locations=new(Path.Combine(cli,"profile"),Path.Combine(cli,"roaming"));
            var connect=new AiConnectPanel(AiSetupSnippets.Create(cli),null,Locations);
            Launcher.SetUpWorkspace+=workspace=>
            {
                for(var i=0;i<workspace.ProjectPanels.Count;i++)
                {var original=workspace.ProjectPanels[i];workspace.ProjectPanels[i]=()=>{var panel=original();return panel.Id==AiConnectPanel.Id?connect.AsPanel():panel;};}
            };
            Assert.True(Launcher.OpenFolder(Copy.Root),Launcher.Said);Assert.True(Workspace.Show("level.json"),Workspace.Said);
            Frames();
        }
        internal void Frames()=>Host.Frames(Launcher.Draw,3);
        internal void Key(ImGuiKey key,bool ctrl=false,bool alt=false)=>Host.Press(Launcher.Draw,key,ctrl:ctrl,alt:alt);
        internal UiNode Node(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role is not(UiRole.Text or UiRole.Window));
        internal void Menu(ImGuiKey key,string item)
        {
            Key(key,alt:true);var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label==item);
            Assert.True(node.Enabled,node.DisabledReason);Assert.False(string.IsNullOrEmpty(node.AccessKey));
            Key(Enum.Parse<ImGuiKey>(node.AccessKey!.ToUpperInvariant()),alt:true);Frames();
        }
        internal void Window(string label)
        {
            string Title(ImGuiWindowPtr window)=>window.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)window.Name)??"";
            if(Title(ImGui.GetCurrentContext().NavWindow).Split("###")[0]==label)return;
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);var found=false;
            try{for(var i=0;i<40;i++){Key(ImGuiKey.Tab);if(Title(ImGui.GetCurrentContext().NavWindowingTarget).Split("###")[0]==label){found=true;break;}}}
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Frames();}
            Assert.True(found,"Ctrl+Tab did not reach "+label+"; windows="+string.Join(",",Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Window).Select(n=>n.Label)));
        }
        internal void Reach(string label)
        {
            var snapshot=Ui.Snapshot();var node=Node(label);Assert.True(node.Enabled,node.DisabledReason);
            for(var i=node.Parent;i>=0;i=snapshot.Nodes[i].Parent)if(snapshot.Nodes[i].Role==UiRole.Window){Window(snapshot.Nodes[i].Label);break;}
            for(var i=0;i<80&&!Node(label).Focused;i++)Key(ImGuiKey.Tab);
            node=Node(label);if(!node.Focused)Shot("unreached-"+label);
            var native=ImGui.GetCurrentContext().NavWindow;
            Assert.True(node.Focused,"Tab did not reach "+label+"; nav="+(native.IsNull()?"none":Marshal.PtrToStringUTF8((IntPtr)native.Name))+"/"+ImGui.GetCurrentContext().NavId.Value+"; wanted="+(uint)node.Id+"; focused="+string.Join(",",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Label)));
            var clip=native.InnerClipRect;
            Assert.True(node.Bounds.X>=clip.Min.X-1&&node.Bounds.Y>=clip.Min.Y-1&&node.Bounds.X+node.Bounds.Width<=clip.Max.X+1&&node.Bounds.Y+node.Bounds.Height<=clip.Max.Y+1,label+" is not fully visible in its focused panel");
            Assert.InRange(node.Bounds.X,0,Host.Width-1);Assert.InRange(node.Bounds.Y,0,Host.Height-1);
            Assert.True(node.Bounds.X+node.Bounds.Width<=Host.Width+1&&node.Bounds.Y+node.Bounds.Height<=Host.Height+1,label+" outside viewport");
        }
        internal void Press(string label){Reach(label);Key(ImGuiKey.Enter);Frames();}
        internal void Type(string label,string text){Reach(label);Key(ImGuiKey.A,ctrl:true);Host.Type(Launcher.Draw,text);Frames();}
        internal void Until(Func<bool> done)
        {var end=Environment.TickCount64+15000;do{Host.Frame(Launcher.Draw);if(done())return;Thread.Sleep(5);}while(Environment.TickCount64<end);Assert.True(done(),Workspace.ProjectPlay?.Status??Workspace.Said);}
        internal void Shot(string name)
        {if(Environment.GetEnvironmentVariable("BED_ACCESS_WALK_PNGS") is not{} folder)return;Directory.CreateDirectory(folder);Host.Drawing=true;Host.Frame(Launcher.Draw);Host.Save(Path.Combine(folder,name+".png"));Host.Drawing=false;}
        public void Dispose(){Launcher.Dispose();Host.Dispose();Copy.Dispose();}
    }

    [Fact]
    public void File_New_game_is_named_and_created_with_only_keys()
    {
        using var r=new Rig();r.Menu(ImGuiKey.F,"New game...");r.Type("Name","Keyboard starter");r.Type("Location",r.Copy.Settings);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "New game" && n.Role == UiRole.Text);
        r.Shot("new-game");r.Press("Create and open");
        Assert.True(File.Exists(Path.Combine(r.Copy.Settings,"Keyboard starter/data/settings.json")));Assert.NotNull(r.Launcher.Workspace);
    }
    [Fact]
    public void Help_Connect_reaches_preview_and_changes_only_owned_fixture_settings()
    {
        using var r=new Rig();r.Menu(ImGuiKey.H,"Connect an AI app");r.Reach("Refresh preview");r.Reach("Connect");
        Assert.False(File.Exists(r.Locations.Targets[0].File));r.Shot("connect");r.Press("Connect");
        Assert.True(File.Exists(r.Locations.Targets[0].File));r.Press("Remove connection");
        using var after=System.Text.Json.JsonDocument.Parse(File.ReadAllText(r.Locations.Targets[0].File));Assert.False(after.RootElement.TryGetProperty("mcpServers",out var servers)&&servers.EnumerateObject().Any());
    }
    [Fact]
    public void View_Game_picture_controls_are_reached_and_operate_with_only_keys()
    {
        using var r=new Rig();r.Menu(ImGuiKey.V,"Game picture");r.Press("Start picture run");
        var session=Assert.IsType<ProjectPictureSession>(r.Workspace.ProjectPlay);r.Until(()=>session.Picture is not null&&!session.Busy);
        var first=session.Picture!;r.Reach("Take picture");r.Shot("picture");r.Press("Take picture");r.Until(()=>session.Picture!=first&&!session.Busy);
        Assert.Equal(first.Frame,session.Picture!.Frame);r.Press("Stop picture run");r.Until(()=>session.HasCompleted);
    }
    [Fact]
    public void Tools_Build_game_can_preview_cancel_and_build_with_only_keys()
    {
        using var r=new Rig();r.Menu(ImGuiKey.T,"Build game");r.Press("Build saved files");r.Reach("Allow this exact command and build");r.Shot("build");
        r.Press("Cancel");r.Press("Build saved files");r.Press("Allow this exact command and build");
        r.Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Label=="Build saved files"));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.StartsWith("Build finished with exit code 0.",StringComparison.Ordinal));
    }
    [Fact]
    public void Live_values_try_and_keep_are_reached_with_only_keys()
    {
        using var r=new Rig();r.Window("Outliner");
        for(var i=0;i<80&&!Ui.Snapshot().Nodes.Any(n=>n.Label=="round_friend"&&n.Focused);i++)
        {if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.ListItem&&n.Focused))r.Key(ImGuiKey.DownArrow);else r.Key(ImGuiKey.Tab);}
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="round_friend"&&n.Focused);r.Key(ImGuiKey.Enter);
        r.Press("Start live values");var session=Assert.IsType<ProjectLiveSession>(r.Workspace.ProjectPlay);r.Until(()=>session.Values is not null&&!session.Busy);
        var before=session.Values!;r.Press("Try right (quarter second)");r.Until(()=>!session.Busy&&session.Values!.Frame>before.Frame);Assert.True(session.Values!.Numbers[2]>0);
        r.Type("Try jump strength (1-25)","18");r.Until(()=>!session.Busy&&session.Values!.Numbers[4]==18);r.Reach("Keep this value");r.Shot("live");r.Press("Keep this value");
        Assert.Contains("jumpStrength",File.ReadAllText(Path.Combine(r.Copy.Root,"data/level.json")));r.Menu(ImGuiKey.T,"Stop");r.Until(()=>session.HasCompleted);
    }
}
