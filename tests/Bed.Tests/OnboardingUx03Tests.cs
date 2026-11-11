using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class OnboardingUx03Tests
{
    private sealed class Play: IProjectPlaySession
    {
        public bool HasCompleted{get;private set;}=true;public bool CanBuildAndPlay=>false;public int? ExitCode=>null;public string Status=>HasCompleted?"Stopped.":"Running.";
        public bool Start(bool dirty,bool buildAndPlay=false){Assert.False(dirty);HasCompleted=false;return true;}
        public void RequestStop()=>HasCompleted=true;public void Update(){}public void Draw(){}public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];public void Dispose()=>HasCompleted=true;
    }
    private sealed class Games:IProjectPlayHost
    {
        internal int Starts;public IProjectPlaySession Start(PlayLaunch launch){Starts++;return new Play();}
    }
    private sealed class Adapter:IProjectAdapter
    {
        public string Name=>"Tour fixture";public bool Recognises(ProjectPaths paths)=>File.Exists(Path.Combine(paths.Data,"level.json"));
        public DataProject Open(ProjectPaths paths){var project=DataProject.Open(paths.Data,paths.Schemas);Assert.True(DocumentHost.For(project).BindScene("level.json","scene",out _));return project;}
        public void Configure(EditorModel<DataDocument> model,DataTableView table){}
        public PlaySettings DefaultPlay(ProjectPaths paths)=>new("game/SceneStarter.exe",[],".");
    }
    private static string Repo(){var at=new DirectoryInfo(AppContext.BaseDirectory);while(at is not null&&!File.Exists(Path.Combine(at.FullName,"Bed.slnx")))at=at.Parent;return at!.FullName;}
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("tour-ux-").FullName;
        internal readonly HeadlessHost Host=new(1600,1000){Drawing=false};internal readonly ProjectLauncher Launcher;internal readonly Games Games=new();internal readonly string Installed,Personal,Preferences;internal readonly Dictionary<string,string> Original;
        internal Rig(JsonNode? dismissal=null,bool includeKey=false,bool enable=true)
        {
            EditorWindow<Scene>.Configure(ImGui.GetIO());Installed=Directory.CreateDirectory(Path.Combine(Root,"installed","samples")).FullName;
            var recipes=Path.Combine(Installed,"recipes");var scene=Path.Combine(Installed,"scene-starter");
            foreach(var relative in new[]{"README.md","reference_map.json","data/recipes.json","data/wares.json","schemas/recipes.schema.json","schemas/wares.schema.json"})Copy(Path.Combine(Repo(),"samples","StarterProject",relative),Path.Combine(recipes,relative));
            foreach(var relative in new[]{"data/level.json","schemas/scene.schema.json","reference_map.json","assets/cube.obj","assets/cube.obj.asset.json","assets/pillar.obj","assets/pillar.obj.asset.json","assets/textured-crate.obj","assets/textured-crate.obj.asset.json","assets/textured-crate.mtl","assets/textured-crate.png","assets/textured-crate.png.asset.json"})Copy(Path.Combine(Repo(),"samples","SceneStarterProject",relative),Path.Combine(scene,relative));
            Directory.CreateDirectory(Path.Combine(scene,"game"));foreach(var name in new[]{"SceneStarter.exe","LICENSE.txt","NOTICE.txt"})File.WriteAllText(Path.Combine(scene,"game",name),"Inert self-authored tour fixture; never executed.");
            Original=Snapshot(Installed);Personal=Path.Combine(Root,"personal");Preferences=Directory.CreateDirectory(Path.Combine(Root,"preferences")).FullName;
            var settings=new JsonObject{["keep"]="retained"};if(includeKey)settings["tourDismissed"]=dismissal?.DeepClone();File.WriteAllText(Path.Combine(Preferences,"settings.json"),settings.ToJsonString());
            Launcher=new(new RecentProjects(Path.Combine(Preferences,"recent.json")),creator:new ProjectCreator(recipes),projectsFolder:Personal){Loaded=[new Adapter()],ProjectPlayHost=Games,EnableOnboarding=enable};Bed.Launcher.Program.SetUp(Launcher);Frames();
        }
        private static void Copy(string source,string destination){Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,destination);}
        internal static Dictionary<string,string> Snapshot(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(root,p),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        internal void Frames(int count=4)=>Host.Frames(Launcher.Draw,count);
        internal UiNode Node(string label,UiRole role=UiRole.Button)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==role&&n.Label==label);
        internal void Act(string label,UiRole role=UiRole.Button,UiActions action=UiActions.Invoke,string? value=null)
        {
            var request=Ui.RequestAction(Node(label,role).Id,action,value);Frames();Assert.Equal(UiActionOutcome.Applied,request.Outcome);
        }
        internal void Key(ImGuiKey key,bool ctrl=false,bool alt=false){Host.Press(Launcher.Draw,key,ctrl:ctrl,alt:alt);Frames();}
        internal void FocusNext(){Act("Focus next action");Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Focused&&n.Role==UiRole.Button&&n.Label=="Focus next action");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Focused&&n.Role!=UiRole.Window);}
        internal void Capture(string name){var folder=Path.Combine(Repo(),".validation","UX03");Directory.CreateDirectory(folder);Host.Drawing=true;Host.Frame(Launcher.Draw);Host.Save(Path.Combine(folder,name+".png"));Host.Drawing=false;}
        internal void Start(){Act("Take the tour");Assert.Equal("OpenExample",Launcher.TourStage);Assert.False(Directory.Exists(Personal));FocusNext();Act("Try the example");Assert.Equal("SelectCrate",Launcher.TourStage);}
        public void Dispose(){Launcher.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Tour_advances_only_from_real_actions_preserves_installed_sources_and_writes_dismissal_only_at_completion(bool keyboardValues)
    {
        using var r=new Rig();var settings=File.ReadAllText(r.Launcher.SettingsFile);r.Start();Assert.Equal(settings,File.ReadAllText(r.Launcher.SettingsFile));
        r.Capture(keyboardValues?"tour-select-keyboard":"tour-select-accessibility");var w=r.Launcher.Workspace!;var scene=w.SceneWindow!.Editor.Session;var original=scene.Current;r.Frames(10);Assert.Equal("SelectCrate",r.Launcher.TourStage);
        r.FocusNext();r.Key(ImGuiKey.Enter);Assert.Equal("Move",r.Launcher.TourStage);
        r.FocusNext();r.Key(ImGuiKey.RightArrow);Assert.Equal("Undo",r.Launcher.TourStage);Assert.NotSame(original,scene.Current);
        r.Key(ImGuiKey.Z,ctrl:true);Assert.Same(original,scene.Current);Assert.Equal("OpenTable",r.Launcher.TourStage);
        r.FocusNext();r.Key(ImGuiKey.Enter);Assert.Equal("EditPrice",r.Launcher.TourStage);
        void Value(string value)
        {
            if(!keyboardValues)r.Act("Bread: price",UiRole.TextField,UiActions.SetValue,value);
            else {r.FocusNext();r.Key(ImGuiKey.A,ctrl:true);r.Host.Type(r.Launcher.Draw,value);r.Key(ImGuiKey.Enter);}
        }
        Value("4");Assert.Equal("RejectPrice",r.Launcher.TourStage);var table=w.TableOf("wares.json")!;var data=w.Active!.Session!;var valid=data.Current;var undo=data.History.UndoCount;
        Value("-1");Assert.Equal("CorrectAndSave",r.Launcher.TourStage);Assert.Same(valid,data.Current);Assert.Equal(undo,data.History.UndoCount);
        var field=r.Node("Bread: price",UiRole.TextField);var issue=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Parent==field.Parent&&n.Label==table.CellError("/wares/0","price"));Assert.True(issue.Bounds.Y>=field.Bounds.Y+field.Bounds.Height);
        r.Capture(keyboardValues?"tour-validation-keyboard":"tour-validation-accessibility");r.Frames(10);Assert.Equal("CorrectAndSave",r.Launcher.TourStage);Assert.Equal(settings,File.ReadAllText(r.Launcher.SettingsFile));
        Value("5");r.Key(ImGuiKey.S,ctrl:true);Assert.Equal("Play",r.Launcher.TourStage);Assert.False(data.Dirty);
        r.FocusNext();r.Key(ImGuiKey.Space);Assert.Equal("Stop",r.Launcher.TourStage);Assert.Equal(1,r.Games.Starts);
        r.FocusNext();r.Key(ImGuiKey.Space);Assert.Equal("Complete",r.Launcher.TourStage);Assert.True(w.ProjectPlay!.HasCompleted);
        using var stored=JsonDocument.Parse(File.ReadAllText(r.Launcher.SettingsFile));Assert.True(stored.RootElement.GetProperty("tourDismissed").GetBoolean());Assert.Equal("retained",stored.RootElement.GetProperty("keep").GetString());
        Assert.Equal(r.Original,Rig.Snapshot(r.Installed));Assert.Equal(5,new DataFormat().Read(File.ReadAllText(w.Project.Find("wares.json")!.Path)).At("/wares/0/price")!.Value.GetDouble());
        var folder=Path.Combine(Repo(),".validation","UX03");Directory.CreateDirectory(folder);r.Host.Drawing=true;r.Host.Frame(r.Launcher.Draw);r.Host.Save(Path.Combine(folder,keyboardValues?"tour-complete-keyboard.png":"tour-complete-accessibility.png"));
        r.Act("Close tour");var after=File.ReadAllText(r.Launcher.SettingsFile);r.Key(ImGuiKey.H,alt:true);r.Act("Take the tour",UiRole.MenuItem);Assert.Equal("OpenExample",r.Launcher.TourStage);Assert.Equal(after,File.ReadAllText(r.Launcher.SettingsFile));
    }
    [Theory][InlineData("missing",true)][InlineData("false",true)][InlineData("true",false)][InlineData("\"wrong\"",true)][InlineData("7",true)]
    public void Missing_or_invalid_dismissal_offers_tour_without_rewriting_settings(string json,bool offered)
    {
        using var r=new Rig(json=="missing"?null:JsonNode.Parse(json),json!="missing");var before=File.ReadAllBytes(r.Launcher.SettingsFile);r.Frames(8);
        Assert.Equal(offered,Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label=="Take the tour"));Assert.Equal(before,File.ReadAllBytes(r.Launcher.SettingsFile));Assert.False(Directory.Exists(r.Personal));
        if(offered){r.Act("Skip tour");using var value=JsonDocument.Parse(File.ReadAllText(r.Launcher.SettingsFile));Assert.True(value.RootElement.GetProperty("tourDismissed").GetBoolean());}
    }
    [Theory][InlineData(1f)][InlineData(2f)]
    public void Tour_instruction_and_controls_fit_at_normal_and_large_scale(float scale)
    {
        using var r=new Rig();new DpiScale().Apply(scale);r.Frames(10);r.Start();r.Frames(10);
        var instruction=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label=="Select the crate in the Outliner.");
        Assert.False(instruction.TextEvidence!.Value.Truncated);Assert.True(instruction.Bounds.X>=0&&instruction.Bounds.Y>=0&&instruction.Bounds.X+instruction.Bounds.Width<=1600&&instruction.Bounds.Y+instruction.Bounds.Height<=1000);
        foreach(var label in new[]{"Focus next action","Skip tour"}){var button=r.Node(label);Assert.True(button.Enabled);Assert.False(button.TextEvidence!.Value.Truncated);Assert.True(button.Bounds.Y+button.Bounds.Height<=1000);}
        r.Capture("tour-scale-"+scale);r.Act("Skip tour");Assert.Equal("Closed",r.Launcher.TourStage);
    }
    private static UiNode Window(string title)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label==title);
    private static bool Overlaps(UiBounds a,UiBounds b)=>a.X<b.X+b.Width&&b.X<a.X+a.Width&&a.Y<b.Y+b.Height&&b.Y<a.Y+a.Height;
    // The box sits inside the centre view, so no docked panel (Files, a tabbed Used by, Prefab and images, the outliner or inspector) is under it.
    private static void AssertClearOfDocks(string title)
    {
        var box=Window(title).Bounds;var centre=Window("Viewport").Bounds;Assert.False(box.IsEmpty);
        Assert.True(box.X>=centre.X&&box.Y>=centre.Y&&box.X+box.Width<=centre.X+centre.Width&&box.Y+box.Height<=centre.Y+centre.Height,$"{title} {box} leaves the centre view {centre}");
        foreach(var dock in Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Window&&n.Label is "Files" or "Used by" or "Outliner" or "Inspector" or "Prefab and images"&&!n.Bounds.IsEmpty))
            Assert.False(Overlaps(box,dock.Bounds),$"{title} {box} covers {dock.Label} {dock.Bounds}");
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Files"&&!n.Bounds.IsEmpty);
    }
    [Theory][InlineData(1f)][InlineData(2f)]
    public void Welcome_and_tour_boxes_stay_in_the_centre_view_clear_of_Files_and_Used_by(float scale)
    {
        using(var offered=new Rig())
        {
            offered.Act("Try the example");new DpiScale().Apply(scale);offered.Frames(6);
            Assert.NotNull(offered.Launcher.Workspace?.SceneWindow);AssertClearOfDocks("Welcome to BED");
            offered.Act("Skip tour");
        }
        using var r=new Rig();r.Start();new DpiScale().Apply(scale);r.Frames(6);AssertClearOfDocks("Take the tour");
        r.Act("Skip tour");Assert.Equal("Closed",r.Launcher.TourStage);new DpiScale().Apply(1f);
    }
    [Fact]
    public void Owner_change_stops_the_tour_without_dismissing_or_mutating_the_replacement()
    {
        using var r=new Rig();r.Start();var before=File.ReadAllText(r.Launcher.SettingsFile);Assert.True(r.Launcher.Close());r.Frames();Assert.Equal("Closed",r.Launcher.TourStage);Assert.Equal(before,File.ReadAllText(r.Launcher.SettingsFile));Assert.Equal(r.Original,Rig.Snapshot(r.Installed));
    }
    [Fact]
    public void Skipping_mid_tour_preserves_the_copy_and_Help_restarts_with_a_separate_copy()
    {
        using var r=new Rig();r.Start();var first=r.Launcher.Current!.Folder!;var original=Rig.Snapshot(first);
        r.Act("Skip tour");Assert.Equal("Closed",r.Launcher.TourStage);Assert.Equal(original,Rig.Snapshot(first));
        var saved=File.ReadAllText(r.Launcher.SettingsFile);r.Key(ImGuiKey.H,alt:true);r.Act("Take the tour",UiRole.MenuItem);
        Assert.Equal("OpenExample",r.Launcher.TourStage);Assert.Equal(saved,File.ReadAllText(r.Launcher.SettingsFile));
        r.Act("Try the example");Assert.Equal("SelectCrate",r.Launcher.TourStage);Assert.NotEqual(first,r.Launcher.Current!.Folder);
        Assert.Equal(original,Rig.Snapshot(first));Assert.Equal(r.Original,Rig.Snapshot(r.Installed));Assert.Equal(saved,File.ReadAllText(r.Launcher.SettingsFile));
    }
    [Fact]
    public void Empty_level_and_selection_offer_real_outliner_actions_without_editing_the_document()
    {
        using var r=new Rig();r.Start();r.Act("Skip tour");
        var originalProject=r.Launcher.Workspace!.Project;
        var project=DataProject.Open(originalProject.DataFolder,originalProject.SchemaFolder);Assert.True(DocumentHost.For(project).BindScene("level.json","scene",out _));using var workspace=new DataWorkspace(project);
        var before=project.Files.Select(file=>file.Dirty).ToArray();r.Host.Frames(workspace.Draw,4);
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Open first level");
        var request=Ui.RequestAction(button.Id,UiActions.Invoke);r.Host.Frames(workspace.Draw,4);Assert.Equal(UiActionOutcome.Applied,request.Outcome);
        Assert.NotNull(workspace.SceneWindow);var scene=workspace.SceneWindow!.Editor.Session;var document=scene.Current;
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Select an Outliner item to see its properties.");
        var select=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&(n.Actions&UiActions.Select)!=0&&n.Label.Contains("textured_crate",StringComparison.Ordinal));
        var selection=Ui.RequestAction(select.Id,UiActions.Select);r.Host.Frames(workspace.Draw,4);Assert.Equal(UiActionOutcome.Applied,selection.Outcome);
        Assert.NotEmpty(workspace.SceneWindow.Editor.Selection);Assert.Same(document,scene.Current);Assert.Equal(0,scene.History.UndoCount);Assert.Equal(before,project.Files.Select(file=>file.Dirty).ToArray());
    }
    [Fact]
    public void Disabled_hosts_and_shot_never_show_or_write_tour_settings()
    {
        using(var r=new Rig(enable:false)){Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Take the tour"||n.Label=="Skip tour");}
        var root=Directory.CreateTempSubdirectory("tour-shot-").FullName;
        try
        {
            var prefs=Directory.CreateDirectory(Path.Combine(root,"preferences")).FullName;File.WriteAllText(Path.Combine(prefs,"settings.json"),"{\"tourDismissed\":false,\"keep\":1}");var before=Rig.Snapshot(prefs);
            var output=new StringWriter();var error=new StringWriter();Assert.Equal(0,Bed.Launcher.Program.Run(["--settings",prefs,"--shot",Path.Combine(root,"shot.png")],output,error,prefs));Assert.Equal(before,Rig.Snapshot(prefs));
        }
        finally{Directory.Delete(root,true);}
    }
}
