using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlaceholderSweepQa31Tests
{
    private sealed class Session:IProjectPlaySession
    {
        public bool HasCompleted {get;private set;}
        public bool CanBuildAndPlay=>true;public int? ExitCode=>null;public string Status=>HasCompleted?"Stopped fixture program.":"Running fixture program.";
        public bool Start(bool hasUnsavedChanges,bool buildAndPlay=false){Assert.False(hasUnsavedChanges);return true;}
        public void RequestStop()=>HasCompleted=true;public void Update(){}public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];public void Draw(){}public void Dispose()=>HasCompleted=true;
    }
    private sealed class Host:IProjectPlayHost
    {
        public int Starts;public Session? Last;public IProjectPlaySession Start(PlayLaunch launch){Starts++;return Last=new();}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Sample_adapter_menus_use_common_Play_and_publish_actionable_refusals_without_placeholders(bool flat)
    {
        var root=Directory.CreateTempSubdirectory("placeholder-menu-").FullName;var profile=flat?"xy":"3d";
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;Directory.CreateDirectory(Path.Combine(root,"schemas"));File.WriteAllText(Path.Combine(root,"schemas/scene.schema.json"),"{}");Directory.CreateDirectory(Path.Combine(root,".bed"));var game=Directory.CreateDirectory(Path.Combine(root,"game")).FullName;
            File.WriteAllText(Path.Combine(game,"SceneStarter.exe"),"Inert command fixture. IProjectPlayHost is replaced; no process executes.");
            File.WriteAllText(Path.Combine(root,"reference_map.json"),"{\"game\":\"SceneStarter\",\"labels\":[],\"references\":[]}");
            var scene=new Scene2(flat?SceneSpace.TwoDXY:SceneSpace.ThreeD,[new(SceneObject.At("one",null,Vector3.Zero),null)],[],[],null);var level=Path.Combine(data,"level.json");File.WriteAllText(level,new Scene2Format().Write(scene));File.WriteAllText(Path.Combine(root,".bed/project.json"),Scene2BuildSettings.WriteProject("{}",new("data/level.json",".bed/generated/scenes/level.json",[scene.Space])));
            var adapters=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]);Assert.Empty(adapters.Problems);var factory=new Host();using var launcher=Bed.Launcher.Program.SetUp(new(new RecentProjects(Path.Combine(root,"local/recent.json"))){Loaded=adapters.Loaded});launcher.ProjectPlayHost=factory;
            Assert.True(launcher.Open(ProjectPaths.In(root)!),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("level.json"),workspace.Said);Assert.True(workspace.Documents.TryGetOpenScene2("level.json",out var entry));var window=(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(workspace)!;
            using var ui=new HeadlessHost(1500,1100){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(launcher.Draw,4);
            entry!.Editor.PlayRefusal=()=>throw new InvalidOperationException("Legacy scene Play must never run from the shipped project menu.");
            var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;var folder=Path.Combine(repo!.FullName,".validation/QA31");Directory.CreateDirectory(Path.Combine(folder,"captures"));var refusals=new List<object>();
            void Open(ImGuiKey key){ui.Press(launcher.Draw,key,alt:true);ui.Frames(launcher.Draw,2);}
            void Capture(string name){ui.Drawing=true;ui.Frame(launcher.Draw);ui.Drawing=false;File.WriteAllBytes(Path.Combine(folder,"captures",profile+"-"+name+"-headless.png"),Png.Encode(ui.Width,ui.Height,ui.Pixels));}
            void Invoke(UiNode node){Assert.True(node.Enabled,node.DisabledReason);var ticket=Ui.RequestAction(node.Id,UiActions.Invoke);ui.Frames(launcher.Draw,3);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);}
            foreach(var pair in new[]{("file",ImGuiKey.F),("edit",ImGuiKey.E),("view",ImGuiKey.V),("tools",ImGuiKey.T),("help",ImGuiKey.H)})
            {
                Open(pair.Item2);var nodes=Ui.Snapshot().Nodes;Assert.Contains(nodes,n=>n.Role==UiRole.MenuItem);foreach(var node in nodes.Where(n=>!n.Enabled&&n.DisabledReason is not null))refusals.Add(new{menu=pair.Item1,node.Label,node.DisabledReason});Assert.DoesNotContain(nodes,n=>Placeholder(n.Label)||Placeholder(n.DisabledReason));
                if(pair.Item1=="file"){var saveAs=Assert.Single(nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Save as...");Assert.False(saveAs.Enabled);Assert.Contains("Use Save",saveAs.DisabledReason);}
                Capture(pair.Item1);ui.Press(launcher.Draw,ImGuiKey.Escape);ui.Frames(launcher.Draw,2);
            }
            Assert.False(window.Run("editor.file_save_as"));Assert.False(window.SaveAsOpen);Assert.False(window.SaveAs(Path.Combine(data,"another.json")));Assert.False(File.Exists(Path.Combine(data,"another.json")));
            entry.Editor.Select("one");ui.Frames(launcher.Draw,2);Open(ImGuiKey.E);Invoke(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Copy"));Open(ImGuiKey.E);Invoke(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Paste"));Assert.Equal(2,entry.Session.Current.Objects.Length);Assert.True(entry.Session.Undo());Assert.False(entry.Session.Dirty);
            Open(ImGuiKey.T);Invoke(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Play"));Assert.Equal(1,factory.Starts);Assert.NotNull(workspace.ProjectPlay);Assert.True(window.Run("editor.stop"));Assert.True(factory.Last!.HasCompleted);
            Assert.True(entry.Editor.Execute(Scene2Kit.Command("object.move",JsonSerializer.SerializeToElement(new{id="one",position=new[]{2,0,0}}))));ui.Frames(launcher.Draw,2);Open(ImGuiKey.F);Invoke(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Save all"));Assert.False(entry.Session.Dirty);Assert.Null(workspace.Project.Find("level.json")!.Session);Capture("saved");File.WriteAllText(Path.Combine(folder,profile+"-refusals.json"),JsonSerializer.Serialize(refusals,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Version_one_Save_as_is_disabled_before_the_dialog_and_keeps_the_bound_draft()
    {
        var root=Directory.CreateTempSubdirectory("placeholder-v1-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;var path=Path.Combine(data,"level.json");var original=new SceneFormat().Write(new([SceneObject.At("one",null,Vector3.Zero)]));File.WriteAllText(path,original);using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.Documents.BindScene("level.json","scene",out _));Assert.True(workspace.Show("level.json"));var window=workspace.SceneWindow!;File.WriteAllText(Path.Combine(root,"game.exe"),"Inert fake-host fixture");PlaySettingsStore.Save(root,new("game.exe"));workspace.BindPlaySettings(root,null,Path.Combine(root,"local"));var factory=new Host();workspace.BindProjectPlay(factory);window.Editor.PlayRefusal=()=>throw new InvalidOperationException("Legacy v1 scene Play must never run from the project menu.");Assert.True(window.Editor.Execute(new MoveObject("one",Vector3.UnitX)));using var ui=new HeadlessHost(1500,1100){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,4);ui.Press(workspace.Draw,ImGuiKey.F,alt:true);var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Save as...");Assert.False(item.Enabled);Assert.Contains("Use Save",item.DisabledReason);Assert.False(window.Run("editor.file_save_as"));Assert.False(window.SaveAsOpen);Assert.True(window.Editor.Session.Dirty);Assert.Equal(original,File.ReadAllText(path));ui.Press(workspace.Draw,ImGuiKey.Escape);ui.Press(workspace.Draw,ImGuiKey.T,alt:true);var play=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Play");Assert.True(play.Enabled,play.DisabledReason);var ticket=Ui.RequestAction(play.Id,UiActions.Invoke);ui.Frames(workspace.Draw,3);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.True(workspace.ProjectPlaySavePending);Assert.Equal(0,factory.Starts);Assert.False(workspace.AnswerProjectPlay(SaveChangesChoice.Cancel));Assert.True(window.Editor.Session.Dirty);Assert.Equal(original,File.ReadAllText(path));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    private static bool Placeholder(string? text)=>text is not null&&(text.Contains("not connected",StringComparison.OrdinalIgnoreCase)||text.Contains("not wired",StringComparison.OrdinalIgnoreCase)||text.Contains("unavailable until",StringComparison.OrdinalIgnoreCase)||text.Contains("available yet",StringComparison.OrdinalIgnoreCase)||text.Contains("editor yet",StringComparison.OrdinalIgnoreCase)||text.Contains("A433",StringComparison.Ordinal));
}
