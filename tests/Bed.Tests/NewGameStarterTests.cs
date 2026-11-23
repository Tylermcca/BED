using System.Diagnostics;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class NewGameStarterTests:IDisposable
{
    private readonly string root=Directory.CreateTempSubdirectory("bed-new-game-").FullName;
    private static string Repo
    {
        get{for(var p=new DirectoryInfo(AppContext.BaseDirectory);p is not null;p=p.Parent)if(File.Exists(Path.Combine(p.FullName,"Bed.slnx")))return p.FullName;throw new InvalidOperationException("BED source root unavailable.");}
    }
    private static string Cli=>Path.Combine(Repo,"src/Bed.Agent.Cli/bin/Release/net10.0/bed-cli.exe");
    private static ProjectCreator Creator()=>new(Path.Combine(Path.GetDirectoryName(Cli)!,"samples/recipes"));
    private string GameFolder=>Path.Combine(root,"My first game");
    public void Dispose()
    {
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),Path.GetDirectoryName(Path.GetFullPath(root)),ignoreCase:true);
        if(Directory.Exists(root))Directory.Delete(root,true);
    }
    private static ProcessStartInfo Start(string program,IEnumerable<string> arguments,string? working=null)
    {
        var info=new ProcessStartInfo(program){WorkingDirectory=working??Repo,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment["BED_GAME_HEADLESS"]="1";foreach(var arg in arguments)info.ArgumentList.Add(arg);return info;
    }
    private static async Task<(int Code,string Text,string Error)> Run(string program,IEnumerable<string> arguments,string? working=null)
    {
        using var process=Process.Start(Start(program,arguments,working))!;process.StandardInput.Close();var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
        return(process.ExitCode,await output,await error);
    }
    private static JsonNode Json((int Code,string Text,string Error) run){Assert.True(run.Code==0,run.Error+run.Text);return JsonNode.Parse(run.Text)!;}
    [Fact]
    public async Task CLI_creates_a_complete_independent_game_checks_changes_and_plays_both_screens()
    {
        var preview=Json(await Run(Cli,["new",GameFolder,"--preview"]));Assert.True(preview["canCreate"]!.GetValue<bool>());Assert.False(Directory.Exists(GameFolder));
        var created=Json(await Run(Cli,["new",GameFolder]));Assert.Equal("created",created["status"]!.GetValue<string>());
        var files=ProjectTemplates.All.Single(t=>t.Id=="game").Files;Assert.All(files,f=>Assert.True(File.Exists(Path.Combine(GameFolder,f)),f));
        var unchanged=File.ReadAllText(Path.Combine(GameFolder,"data/settings.json"));
        var args=new[]{"--data",Path.Combine(GameFolder,"data"),"--schemas",Path.Combine(GameFolder,"schemas")};
        Json(await Run(Cli,[..args,"set","settings.json","/goal","3","--preview"]));Assert.Equal(unchanged,File.ReadAllText(Path.Combine(GameFolder,"data/settings.json")));
        Json(await Run(Cli,[..args,"set","settings.json","/goal","3","--save"]));Assert.Equal(0,Json(await Run(Cli,[..args,"problems","settings.json"]))["count"]!.GetValue<int>());
        var game=Path.Combine(GameFolder,"game/StarterGame.exe");var played=Json(await Run(game,["--project",GameFolder,"--headless","--script","start,collect,collect,collect"]));
        Assert.Equal("Play",played["screen"]!.GetValue<string>());Assert.True(played["Won"]!.GetValue<bool>());Assert.Equal(3,played["Score"]!.GetValue<int>());
        Assert.Equal("Menu",Json(await Run(game,["--project",GameFolder,"--headless","--script","start,back"]))["screen"]!.GetValue<string>());
        var notices=File.ReadAllText(Path.Combine(GameFolder,"game/THIRD-PARTY-NOTICES.txt"));Assert.Contains("FlatRedBall",notices);Assert.Contains("Skia",notices);Assert.Contains("Microsoft",notices);
        var cache=Environment.GetEnvironmentVariable("NUGET_PACKAGES")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".nuget/packages");
        var built=await Run("dotnet",["publish","source/StarterGame.csproj","-c","Release","--self-contained","true","-o","game","--source",cache,"--nologo","-v:q"],GameFolder);Assert.True(built.Code==0,built.Error+built.Text);
        Assert.True(Json(await Run(game,["--project",GameFolder,"--headless","--script","start,collect,collect,collect"]))["Won"]!.GetValue<bool>());
        var again=await Run(Cli,["new",GameFolder]);Assert.Equal(1,again.Code);Assert.Contains("already there",again.Text);Assert.Equal(3,JsonNode.Parse(File.ReadAllText(Path.Combine(GameFolder,"data/settings.json")))!["goal"]!.GetValue<int>());
    }
    [Fact]
    public async Task A_saved_Gum_screen_reloads_in_the_same_running_game_and_keeps_its_score()
    {
        var made=Creator().Create("game",root,"My first game");Assert.Equal(ProjectCreationStatus.Created,made.Status);
        using var player=Process.Start(Start(Path.Combine(GameFolder,"game/StarterGame.exe"),["--project",GameFolder,"--observe"]))!;var diagnostics=player.StandardError.ReadToEndAsync();
        async Task<JsonNode> Read()=>JsonNode.Parse(await player.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))??throw new InvalidOperationException("Game closed before replying."))!;
        async Task<JsonNode> Action(string action){await player.StandardInput.WriteLineAsync(new JsonObject{["action"]=action}.ToJsonString());await player.StandardInput.FlushAsync();return await Read();}
        try
        {
            await Read();await Action("start");await Action("collect");var before=await Action("back");
            var path=Path.Combine(GameFolder,"ui/Screens/Menu.gusj");var screen=JsonNode.Parse(File.ReadAllText(path))!;var tip=Assert.Single(screen["States"]![0]!["Variables"]!.AsArray(),v=>v!["Name"]!.GetValue<string>()=="Tip.Text")!;tip["ValueAsString"]="A new saved menu, in this same running game.";File.WriteAllText(path,screen.ToJsonString());
            var after=await Action("frame");Assert.Equal(before["Score"]!.GetValue<int>(),after["Score"]!.GetValue<int>());Assert.True(after["Reloads"]!.GetValue<int>()>before["Reloads"]!.GetValue<int>());Assert.NotEqual(before["picture"]!.GetValue<string>(),after["picture"]!.GetValue<string>());
            await player.StandardInput.WriteLineAsync("{\"action\":\"stop\"}");player.StandardInput.Close();await player.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal(0,player.ExitCode);Assert.Equal("",await diagnostics);
        }
        finally{if(!player.HasExited)player.Kill(true);}
    }
    [Fact]
    public void Launcher_creates_the_same_game_and_its_File_menu_reaches_the_game_chooser()
    {
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")),creator:Creator(),projectsFolder:root);
        Assert.True(launcher.CreateProject("game",root,"My first game"),launcher.Said);
        var workspace = Assert.IsType<DataWorkspace>(launcher.Workspace);
        Assert.Equal("settings.json", workspace.Active?.Name);
        var editor = workspace.Window!.Editor;
        Assert.Equal("", editor.Primary);
        Assert.False(editor.IsOpen("editor.table"));
        Assert.True(editor.IsOpen(GumScreenPanel.Id));
        Assert.True(workspace.PlaySettings!.TryLaunch(out var play,out var why),why);Assert.Equal(Path.GetFullPath(Path.Combine(GameFolder,"game/StarterGame.exe")),play!.Run.Executable,ignoreCase:true);Assert.Equal(new[]{"--project","."},play.Run.Arguments);
        using var host=new HeadlessHost(1400,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(launcher.Draw,4);
        var nodes = Ui.Snapshot().Nodes;
        Assert.Contains(nodes, n => n.Label == "1 file");
        Assert.Contains(nodes, n => n.Label == "Gum screens" && n.Role == UiRole.Window);
        Assert.Contains(nodes, n => n.Role == UiRole.TextField && n.Label.Contains("title", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nodes, n => n.Label.Contains("no list of items", StringComparison.Ordinal));
        var file=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="File"&&n.Role==UiRole.Menu);host.Click(launcher.Draw,file.Bounds.X+5,file.Bounds.Y+5);host.Frames(launcher.Draw,2);
        var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="New game..."&&n.Role==UiRole.MenuItem);Assert.True(item.Enabled);host.Click(launcher.Draw,item.Bounds.X+5,item.Bounds.Y+5);host.Frames(launcher.Draw,3);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "New game" && n.Role == UiRole.Text);
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Label == "Start from" && n.Role == UiRole.Combo);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Create and open");
    }
    [Fact]
    public void The_start_screen_New_game_button_opens_its_chooser_without_an_open_project()
    {
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")),creator:Creator(),projectsFolder:root);
        using var host=new HeadlessHost(1280,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(launcher.Draw,4);
        Assert.Null(launcher.Workspace);
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="New game..."&&n.Role==UiRole.Button);Assert.True(button.Enabled);
        host.Click(launcher.Draw,button.Bounds.X+button.Bounds.Width/2,button.Bounds.Y+button.Bounds.Height/2);host.Frames(launcher.Draw,3);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "New game" && n.Role == UiRole.Text);
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Label == "Start from" && n.Role == UiRole.Combo);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Create and open"&&n.Enabled);Assert.Null(launcher.Workspace);
        Assert.DoesNotContain("No project is open",launcher.Said);
    }
    [Fact]
    public void Installed_samples_have_plain_tree_names_and_are_findable_by_those_names()
    {
        var samples=Path.Combine(root,"samples");
        foreach(var name in new[]{"checkers","first-task","new-game","prefab-physics","recipes","scene-starter","sprite-2d"})
        {
            var data = Directory.CreateDirectory(Path.Combine(samples,name,"data"));
            var schemas = Directory.CreateDirectory(Path.Combine(samples,name,"schemas"));
            File.WriteAllText(Path.Combine(data.FullName,"item.json"), "{}");
            File.WriteAllText(Path.Combine(schemas.FullName,"item.schema.json"), "{}");
        }
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")),creator:Creator(),projectsFolder:root){SamplesFolder=samples};
        using var host=new HeadlessHost(1280,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(launcher.Draw,4);
        var labels=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.ListItem).Select(n=>n.Label).ToArray();
        Assert.All(new[]{"Checkers","First task","New game starter","Physics island","Recipes","3D scene","2D garden"},label=>Assert.Contains(label,labels));
        var garden=Assert.Single(launcher.SearchRecent("garden"));Assert.Equal("sprite-2d",ProjectLauncher.Name(garden));
        Assert.Equal(garden,Assert.Single(launcher.SearchRecent("sprite-2d")));
        Assert.True(launcher.OpenRow(garden), launcher.Said);
        Assert.Contains("Opened 2D garden: 1 file", launcher.Said);
        Assert.Equal("item.json - 2D garden - BED", WindowTitle.For(launcher));
        host.Frames(launcher.Draw, 3);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label.StartsWith("Project: 2D garden (", StringComparison.Ordinal));
        Assert.True(launcher.Close(), launcher.Said);
        host.Frames(launcher.Draw, 3);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "2D garden" && n.Role == UiRole.ListItem);
    }
    [Fact]
    public void A_created_game_shares_its_real_score_target_and_win_without_a_window_or_saved_changes()
    {
        var adapter=Assert.Single(Adapters.Load([Path.Combine(Repo,"samples/StarterAdapter/bin/Release/net10.0/StarterAdapter.dll")]).Loaded);
        using var host=new HeadlessHost(1280,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")),creator:Creator(),projectsFolder:root)
            {Loaded=[adapter],ProjectPlayHost=new ProjectPlayBridge()};
        Assert.True(launcher.CreateProject("game",root,"My first game"),launcher.Said);
        var paths=ProjectPaths.In(GameFolder)!;Assert.True(adapter.Recognises(paths));var target=Assert.Single(adapter.LiveValues(paths));
        Assert.Equal(new[]{"score","goal","won"},target.Values.Select(v=>v.Id));Assert.All(target.Values,v=>Assert.False(v.CanTry));
        Assert.Equal(new[]{"start","collect","back","restart"},target.Inputs);
        Assert.True(launcher.Workspace!.Show("settings.json"));host.Frames(launcher.Draw,3);var workspace=launcher.Workspace;
        Assert.Contains(workspace.Window!.Editor.Panels,p=>p.Id=="project.live"&&p.Title=="Live values");
        var saved=File.ReadAllBytes(Path.Combine(GameFolder,"data/settings.json"));
        Assert.True(workspace.RequestLivePlay(),workspace.Said);var session=Assert.IsType<ProjectLiveSession>(workspace.ProjectPlay);
        void Until(Func<bool> ready)
        {
            var end=Environment.TickCount64+10000;
            do{session.Update();if(ready())return;Thread.Sleep(5);}while(Environment.TickCount64<end);
            Assert.True(ready(),session.Status);
        }
        Until(()=>session.Values is not null&&!session.Busy);Assert.Equal(new float[]{0,5,0},session.Values!.Numbers);
        void Key(string key)
        {
            var frame=session.Values!.Frame;Assert.True(session.TryKey(key));Until(()=>!session.Busy&&session.Values?.Frame>frame);
        }
        Key("collect");Assert.Equal(0,session.Values!.Numbers[0]);Key("start");
        for(var i=0;i<5;i++)Key("collect");Assert.Equal(new float[]{5,5,1},session.Values!.Numbers);
        Key("back");Key("collect");Assert.Equal(new float[]{5,5,1},session.Values!.Numbers);
        Key("restart");Assert.Equal(new float[]{0,5,0},session.Values!.Numbers);
        Assert.False(session.TryKey("unshared"));Assert.False(session.TryValue("goal",3));
        void Draw(){if(Ui.Begin("Starter live check"))workspace.DrawLiveValues("settings.json",null);Ui.End();}
        host.Frames(Draw,3);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Score: 0",StringComparison.Ordinal));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Winning target: 5",StringComparison.Ordinal));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Round won (1 = yes): 0",StringComparison.Ordinal));
        session.RequestStop();Until(()=>session.HasCompleted);Assert.Equal("Stopped live Play.",session.Status);
        Assert.Equal(saved,File.ReadAllBytes(Path.Combine(GameFolder,"data/settings.json")));
        var recipes=ProjectPaths.In(Path.Combine(Repo,"samples/StarterProject"));Assert.NotNull(recipes);Assert.Empty(adapter.LiveValues(recipes));
    }
    [Fact]
    public void A_created_game_runs_takes_a_picture_and_stops_through_the_View_menu()
    {
        var adapter=Assert.Single(Adapters.Load([Path.Combine(Repo,"samples/StarterAdapter/bin/Release/net10.0/StarterAdapter.dll")]).Loaded);
        using var host=new HeadlessHost(1280,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using var launcher=global::Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")),creator:Creator(),projectsFolder:root){Loaded=[adapter]});
        Assert.True(launcher.CreateProject("game",root,"My first game"),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("settings.json"),workspace.Said);
        var saved=new[]{"data","ui"}.SelectMany(folder=>Directory.GetFiles(Path.Combine(GameFolder,folder),"*",SearchOption.AllDirectories)).ToDictionary(path=>path,File.ReadAllBytes);
        host.Frames(launcher.Draw,4);host.Press(launcher.Draw,ImGuiKey.V,alt:true);
        var menu=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Game picture");Assert.True(menu.Enabled);Assert.False(string.IsNullOrEmpty(menu.AccessKey));
        host.Press(launcher.Draw,Enum.Parse<ImGuiKey>(menu.AccessKey!.ToUpperInvariant()),alt:true);host.Frames(launcher.Draw,3);
        void Click(string label){var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);Assert.True(button.Enabled);host.Click(launcher.Draw,button.Bounds.X+button.Bounds.Width/2,button.Bounds.Y+button.Bounds.Height/2);}
        Click("Start picture run");var session=Assert.IsType<ProjectPictureSession>(workspace.ProjectPlay);
        void Until(Func<bool> ready){var end=Environment.TickCount64+15000;do{host.Frames(launcher.Draw,1);if(ready())return;Thread.Sleep(5);}while(Environment.TickCount64<end);Assert.True(ready(),session.Status);}
        Until(()=>session.Picture is not null&&!session.Busy);var first=session.Picture!;
        Assert.Equal((800,600),(first.Width,first.Height));Assert.Equal(800*600*4,first.Rgba.Length);Assert.True(first.Rgba.Distinct().Count()>10);Assert.Contains("Menu. Score 0 of 5.",first.Description);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Game picture:")&&n.Label.Contains("pixels"));
        Click("Take picture");Until(()=>session.Picture!=first&&!session.Busy);Assert.Equal(first.Frame,session.Picture!.Frame);Assert.Equal(first.Rgba,session.Picture.Rgba);
        Click("Stop picture run");Until(()=>session.HasCompleted);Assert.Contains("stopped",session.Status,StringComparison.OrdinalIgnoreCase);
        foreach(var file in saved)Assert.Equal(file.Value,File.ReadAllBytes(file.Key));
    }
    [Fact]
    public void The_generated_rules_keep_shared_AI_policy_and_the_actual_game_and_Gum_paths()
    {
        var made=Creator().Create("game",root,"My first game");Assert.Equal(ProjectCreationStatus.Created,made.Status);
        var rules=File.ReadAllText(Path.Combine(GameFolder,"AGENTS.md"));
        foreach(var expected in new[]{"bed-cli","data files by hand","couldn't check:","person decides","no run-any-code tool","one undoable command","source/StarterGame.csproj","--headless --script start,collect,back","https://github.com/vchelaru/Gum/blob/main/CLAUDE.md"})Assert.Contains(expected,rules);
        Assert.Equal("Read AGENTS.md before changing this project.\n",File.ReadAllText(Path.Combine(GameFolder,"CLAUDE.md")));
        Assert.Equal(File.ReadAllText(Path.Combine(Repo,"docs/guide/first_game_with_ai.md")),File.ReadAllText(Path.Combine(GameFolder,"FIRST_GAME.md")));
    }
    [Fact]
    public void Preview_checks_the_installed_player_without_allocating_its_bytes()
    {
        var creator=Creator();Assert.True(creator.Preview("game",root,"Warmup").CanCreate);var before=GC.GetAllocatedBytesForCurrentThread();var preview=creator.Preview("game",root,"Preview only");var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.True(preview.CanCreate,preview.Reason);Assert.InRange(allocated,0,1024*1024);Assert.False(Directory.Exists(Path.Combine(root,"Preview only")));
    }
}
