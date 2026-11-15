using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class Live02Tests
{
    private static IProjectAdapter Adapter()=>Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded);
    [Fact]
    public void Island_declares_its_own_fields_and_trials_change_real_native_gravity_and_mass_without_saving()
    {
        using var copy=new SpriteSampleTests.Copy(true);
        var path=Path.Combine(copy.Root,"data","level.json");var level=JsonNode.Parse(File.ReadAllText(path))!;
        var top=level["instances"]!.AsArray().Single(i=>i!["placement"]!["id"]!.GetValue<string>()=="crate_3")!;
        top["placement"]!["position"]![1]=15;
        // Current saved values can exceed a trial range: they remain readable, but cannot be tried or kept outside it.
        top["overrides"]!.AsArray().Single(o=>o!["field"]!.GetValue<string>()=="/body/massKg")!["value"]=150;File.WriteAllText(path,level.ToJsonString());
        copy.OpenAndCompile();var adapter=Adapter();var target=adapter.LiveValues(ProjectPaths.In(copy.Root)!).Single(t=>t.ObjectId=="crate_3_box");
        Assert.Equal(new[]{"y","speedY","gravityY","massKg"},target.Values.Select(v=>v.Id));Assert.Equal(new[]{"jump"},target.Inputs);
        var before=Directory.GetFiles(Path.Combine(copy.Root,"data"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        var launch=new PlayLaunch(new(Path.Combine(copy.Root,"game","PhysicsStarter.exe"),copy.Root,["--project","."]),null,copy.Root,[]);
        using(var observation=new PlayObservation())
        {
            observation.Start(launch,new(copy.Settings),false,target.ObjectId);
            var initial=observation.ReadValues(target);Assert.Equal(150,initial.Numbers[3],3);Assert.False(target.Values[3].Accepts(initial.Numbers[3]));
            Assert.Throws<ArgumentException>(()=>observation.ReadValues(target,field:"massKg",value:150));
            observation.ReadValues(target,field:"gravityY",value:0);var floating=observation.ReadValues(target,seconds:.1);
            Assert.Equal(initial.Numbers[0],floating.Numbers[0],3);Assert.Equal(0,floating.Numbers[1],3);
            observation.ReadValues(target,field:"gravityY",value:-20);var falling=observation.ReadValues(target,seconds:.1);
            Assert.True(falling.Numbers[0]<floating.Numbers[0]);Assert.True(falling.Numbers[1]<-1);
            var heavier=observation.ReadValues(target,field:"massKg",value:8);Assert.Equal(8,heavier.Numbers[3],3);
            Assert.Equal(falling.Numbers[0],heavier.Numbers[0]);Assert.Equal(falling.Numbers[1],heavier.Numbers[1]);
            Assert.Throws<ArgumentException>(()=>observation.ReadValues(target,field:"massKg",value:0));
            Assert.Throws<ArgumentException>(()=>observation.ReadValues(target,field:"unshared",value:1));
            Assert.Equal(8,observation.ReadValues(target).Numbers[3],3);
            var jumped=observation.ReadValues(target,key:"jump",seconds:.1);
            Assert.True(jumped.Numbers[0]>heavier.Numbers[0]);Assert.InRange(jumped.Numbers[1],2.9,3.1);
            Assert.Equal(8,jumped.Numbers[3],3);
        }
        foreach(var pair in before)Assert.Equal(pair.Value,File.ReadAllBytes(pair.Key));
    }
    [Fact]
    public void Launcher_loads_island_opt_in_and_Keep_saves_only_the_confirmed_crate_mass()
    {
        using var copy=new SpriteSampleTests.Copy(true);copy.OpenAndCompile();
        using var host=new HeadlessHost(1200,900){Drawing=false};EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(copy.Settings,"recent.json"))){Loaded=[Adapter()],ProjectPlayHost=new ProjectPlayBridge()};
        Assert.True(launcher.OpenFolder(copy.Root),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("level.json"),workspace.Said);
        host.Frames(launcher.Draw,2);Assert.Single(workspace.Scene2Window!.Editor.Panels,p=>p.Id==ProjectBuildPanel.Id);
        workspace.Scene2Window.Editor.Select("crate_3_box");workspace.Documents.TryGetOpenScene2("level.json",out var entry);
        var path=Path.Combine(copy.Root,"data","level.json");var original=File.ReadAllBytes(path);var gravity=entry!.Session.Current.Physics!.Gravity;
        Assert.True(workspace.RequestLivePlay(),workspace.Said);var session=Assert.IsType<ProjectLiveSession>(workspace.ProjectPlay);
        void Until(Func<bool> ready){var end=Environment.TickCount64+10000;do{session.Update();if(ready())return;Thread.Sleep(5);}while(Environment.TickCount64<end);Assert.True(ready(),session.Status);}
        Until(()=>session.Values is not null&&!session.Busy);Assert.True(session.TryValue("massKg",9));Assert.True(session.TryValue("gravityY",-15));
        Until(()=>session.Values?.Numbers[3]==9&&session.Values.Numbers[2]==-15&&!session.Busy);Assert.Equal(original,File.ReadAllBytes(path));
        Assert.True(workspace.KeepLiveValue("level.json",entry,"massKg"));var kept=File.ReadAllBytes(path);
        var saved=new Scene2Format().Read(File.ReadAllText(path));Assert.Equal(gravity,saved.Physics!.Gravity);
        Assert.Equal(9,Scene2Resolver.Resolve(saved).Objects.Single(o=>o.Object.Id=="crate_3_box").Body!.MassKg);
        Assert.False(workspace.KeepLiveValue("level.json",entry,"gravityY"));Assert.Equal(kept,File.ReadAllBytes(path));Assert.False(entry.Session.Dirty);
        session.RequestStop();Until(()=>session.HasCompleted);
    }
    [Fact]
    public void Metadata_ranges_and_uniqueness_are_checked_before_any_process_can_start()
    {
        Assert.Throws<ArgumentException>(()=>new LiveValue("mass","Mass","kg",float.NaN,20));
        Assert.Throws<ArgumentException>(()=>new LiveValue("mass","Mass","kg",20,1));
        Assert.Throws<ArgumentException>(()=>new LiveValue("mass","Mass","kg",1,20,"../file.json"));
        Assert.Throws<ArgumentException>(()=>new LiveValueTarget("obj","Object",[new("x","X"),new("x","Again")]));
        Assert.Throws<ArgumentException>(()=>new LiveValueTarget("obj","Object",Enumerable.Range(0,17).Select(i=>new LiveValue(i.ToString(),"X"))));
    }
    private sealed class NotShared: IProjectAdapter
    {
        public string Name=>"Plain data";
        public DataProject Open(ProjectPaths paths)=>DataProject.Open(paths.Data,paths.Schemas);
        public void Configure(EditorModel<DataDocument> editor,DataTableView table) { }
    }
    [Fact]
    public void Games_without_opt_in_show_a_plain_note_and_do_not_launch_a_fallback_game()
    {
        var root=Directory.CreateTempSubdirectory("live02-none-");
        try
        {
            var data=root.CreateSubdirectory("data").FullName;var schemas=root.CreateSubdirectory("schemas").FullName;
            IProjectAdapter adapter=new NotShared();Assert.Empty(adapter.LiveValues(new(data,schemas)));
            using var workspace=new DataWorkspace(adapter.Open(new(data,schemas)));workspace.BindLiveValues(adapter.LiveValues(new(data,schemas)));workspace.BindPlaySettings(root.FullName,new("never.exe"),Path.Combine(root.FullName,"preferences"));workspace.BindProjectPlay(new ProjectPlayBridge());
            using var host=new HeadlessHost(900,700){Drawing=false};EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            void Draw(){if(Ui.Begin("Live values proof"))workspace.DrawLiveValues("",null);Ui.End();}
            host.Frames(Draw,3);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="This game doesn't share live values.");
            Assert.False(workspace.RequestLivePlay());Assert.Null(workspace.ProjectPlay);Assert.Empty(Directory.GetFiles(root.FullName,"*",SearchOption.AllDirectories));
        }
        finally{root.Delete(true);}
    }
}
