using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class Live01Tests
{
    private static PlayLaunch Launch(SpriteSampleTests.Copy copy)=>new(new(Path.Combine(copy.Root,"game","SceneStarter.exe"),copy.Root,["--project","."]),null,copy.Root,[]);
    private static LiveValueTarget Target(SpriteSampleTests.Copy copy)=>Assert.Single(Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded).LiveValues(ProjectPaths.In(copy.Root)!));
    private static void Until(ProjectLiveSession session,Func<bool> predicate)
    {
        var deadline=Environment.TickCount64+10000;
        do{session.Update();if(predicate())return;Thread.Sleep(5);}while(Environment.TickCount64<deadline);
        Assert.True(predicate(),session.Status);
    }
    [Fact]
    public void Owned_game_reports_five_values_tunes_the_next_jump_and_leaves_saved_files_unchanged()
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();
        var before=Directory.GetFiles(Path.Combine(copy.Root,"data"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        using(var play=new PlayObservation())
        {
            var target=Target(copy);play.Start(Launch(copy),new(copy.Settings),false,target.ObjectId);
            (long Frame,float X,float SpeedX,float SpeedY,float JumpStrength) Read(string? key=null,double seconds=0,float? jumpStrength=null)
            {var v=play.ReadValues(target,key,seconds,jumpStrength.HasValue?"jumpStrength":null,jumpStrength);return(v.Frame,v.Numbers[0],v.Numbers[2],v.Numbers[3],v.Numbers[4]);}
            var first=Read();var moved=Read("right",.25);Assert.True(moved.Frame>first.Frame);Assert.True(moved.X>first.X);Assert.True(moved.SpeedX>0);
            Read("restart",.01);for(var i=0;i<4;i++)Read(seconds:.25);
            var ordinary=Read("jump",1.0/60);Assert.True(ordinary.SpeedY>0);
            Read("restart",.01);for(var i=0;i<4;i++)Read(seconds:.25);
            var tuned=Read(jumpStrength:16);Assert.Equal(16,tuned.JumpStrength);
            var stronger=Read("jump",1.0/60);Assert.True(stronger.SpeedY>ordinary.SpeedY+4);
            Assert.Throws<ArgumentException>(()=>Read(jumpStrength:float.NaN));Assert.Throws<ArgumentException>(()=>Read(jumpStrength:26));
            Assert.Throws<ArgumentException>(()=>Read("desktop-key",.1));
        }
        foreach(var item in before)Assert.Equal(item.Value,File.ReadAllBytes(item.Key));
    }
    [Fact]
    public void Workspace_live_Play_keeps_only_confirmed_jump_value_through_the_checked_scene_save()
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();
        var paths=ProjectPaths.In(copy.Root)!;var adapter=Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded);
        var project=adapter.Open(paths);using var workspace=new DataWorkspace(project);Assert.True(workspace.Show("level.json"));
        var entry=workspace.Scene2Window!.Editor;entry.Select("round_friend");
        workspace.BindPlaySettings(copy.Root,new("game/SceneStarter.exe",["--project","."]),copy.Settings);workspace.BindLiveValues(adapter.LiveValues(ProjectPaths.In(copy.Root)!));workspace.BindProjectPlay(new ProjectPlayBridge());
        var file=Path.Combine(copy.Root,"data","level.json");var original=File.ReadAllBytes(file);
        Assert.True(workspace.RequestLivePlay(),workspace.Said);var session=Assert.IsType<ProjectLiveSession>(workspace.ProjectPlay);
        Until(session,()=>session.Values is not null&&!session.Busy);Assert.True(session.TryValue("jumpStrength",17));
        Until(session,()=>session.Values?.Numbers[4]==17&&!session.Busy);
        Assert.Equal(original,File.ReadAllBytes(file));Assert.False(entry.Session.Dirty);
        Assert.True(workspace.Documents.TryGetOpenScene2("level.json",out var document));
        Assert.True(workspace.KeepLiveValue("level.json",document!));Assert.False(entry.Session.Dirty);
        var saved=new Scene2Format().Read(File.ReadAllText(file));var friend=Scene2Resolver.Resolve(saved).Objects.Single(o=>o.Object.Id=="round_friend");
        Assert.Equal(17,friend.Object.Properties["jumpStrength"].GetSingle());
        // Saved defaults survive a rebuild and a new owned process.
        session.RequestStop();Until(session,()=>session.HasCompleted);
        using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(0,SceneStarter.Program.Run(["--compile-scene2",copy.Root],output,error));
        using(var again=new PlayObservation()){var target=Target(copy);again.Start(Launch(copy),new(copy.Settings),false,target.ObjectId);Assert.Equal(17,again.ReadValues(target).Numbers[4]);}
    }
    [Fact]
    public void Keep_refuses_an_outside_file_change_and_stop_reaps_the_owned_game()
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();var paths=ProjectPaths.In(copy.Root)!;
        var adapter=Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded);
        using var workspace=new DataWorkspace(adapter.Open(paths));Assert.True(workspace.Show("level.json"));workspace.Scene2Window!.Editor.Select("round_friend");
        workspace.BindPlaySettings(copy.Root,new("game/SceneStarter.exe",["--project","."]),copy.Settings);workspace.BindLiveValues(adapter.LiveValues(ProjectPaths.In(copy.Root)!));workspace.BindProjectPlay(new ProjectPlayBridge());Assert.True(workspace.RequestLivePlay());
        var session=Assert.IsType<ProjectLiveSession>(workspace.ProjectPlay);Until(session,()=>session.Values is not null&&!session.Busy);
        var path=Path.Combine(copy.Root,"data","level.json");File.AppendAllText(path," ");var changed=File.ReadAllBytes(path);
        workspace.Documents.TryGetOpenScene2("level.json",out var entry);Assert.False(workspace.KeepLiveValue("another-level.json",entry!));Assert.False(workspace.KeepLiveValue("level.json",entry!));Assert.Equal(changed,File.ReadAllBytes(path));
        Assert.True(workspace.StopProjectPlay());Until(session,()=>session.HasCompleted);
    }
    [Fact]
    public void Live_panel_types_a_value_into_the_real_game_and_Keep_is_an_explicit_click()
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();
        var adapter=Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded);
        using var workspace=new DataWorkspace(adapter.Open(ProjectPaths.In(copy.Root)!));Assert.True(workspace.Show("level.json"));
        workspace.Scene2Window!.Editor.Select("round_friend");workspace.Documents.TryGetOpenScene2("level.json",out var entry);
        workspace.BindPlaySettings(copy.Root,new("game/SceneStarter.exe",["--project","."]),copy.Settings);workspace.BindLiveValues(adapter.LiveValues(ProjectPaths.In(copy.Root)!));workspace.BindProjectPlay(new ProjectPlayBridge());
        Assert.True(workspace.RequestLivePlay());var session=Assert.IsType<ProjectLiveSession>(workspace.ProjectPlay);Until(session,()=>session.Values is not null&&!session.Busy);
        using var host=new HeadlessHost(900,900){Drawing=false};EditorWindow<Scene2>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        void Draw()
        {
            session.Update();Brutal.ImGuiApi.ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0,0));Brutal.ImGuiApi.ImGui.SetNextWindowSize(new Brutal.Numerics.float2(850,850));
            if(Ui.Begin("Live values test"))workspace.DrawLiveValues("level.json",entry!);Ui.End();
        }
        host.Frames(Draw,3);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Speed X:"));
        var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Try jump strength (1-25)");
        host.Click(Draw,field.Bounds.X+15,field.Bounds.Y+field.Bounds.Height/2);host.Press(Draw,Brutal.ImGuiApi.ImGuiKey.A,ctrl:true);host.Type(Draw,"18");
        Until(session,()=>session.Values?.Numbers[4]==18&&!session.Busy);host.Frames(Draw,2);
        var path=Path.Combine(copy.Root,"data","level.json");Assert.DoesNotContain("jumpStrength",File.ReadAllText(path));
        var keep=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Keep this value");Assert.True(keep.Enabled);
        host.Click(Draw,keep.Bounds.X+keep.Bounds.Width/2,keep.Bounds.Y+keep.Bounds.Height/2);
        Assert.Contains("jumpStrength",File.ReadAllText(path));Assert.False(entry!.Session.Dirty);
        session.RequestStop();Until(session,()=>session.HasCompleted);
    }

    [Fact]
    public void Workspace_loss_during_start_observes_exit_before_releasing_the_project_folder()
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();
        var play=new ProjectLiveSession(Launch(copy),new(copy.Settings),Target(copy));Assert.True(play.Start(false));play.Dispose();Assert.True(play.HasCompleted);
        Assert.False(play.TryValue("jumpStrength",14));Assert.False(play.TryKey("jump"));
    }

}
