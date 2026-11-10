using System.Diagnostics;
using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram=Brutal.Authoring.Launcher.Program;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SamplePlayJourneyTests
{
    internal const string Saved="""{"kind":"scene","version":1,"objects":[{"id":"root","position":[2,3,4]},{"id":"child","parent":"root","position":[1,0,0],"references":{"anchor":"root"}}]}""";
    internal sealed class ObservedSession(IProjectPlaySession inner,PlayLaunch launch):IProjectPlaySession
    {
        public readonly List<ProjectPlayOutput> Logs=[];
        public string? SavedAtStart,StatusAtStart;
        public int Starts,Stops;
        public bool Start(bool dirty,bool build=false)
        {Starts++;SavedAtStart=File.ReadAllText(Path.Combine(launch.ProjectRoot,"data","level.json"));var result=inner.Start(dirty,build);StatusAtStart=inner.Status;return result;}
        public bool CanBuildAndPlay=>inner.CanBuildAndPlay;
        public bool HasCompleted=>inner.HasCompleted;
        public int? ExitCode=>inner.ExitCode;
        public string Status=>inner.Status;
        public void RequestStop(){Stops++;inner.RequestStop();}
        public void Update()=>inner.Update();
        public IReadOnlyList<ProjectPlayOutput> DrainLogs(){var lines=inner.DrainLogs();Logs.AddRange(lines);return lines;}
        public void Draw()=>inner.Draw();
        public void Dispose()=>inner.Dispose();
    }
    // Pure observation around the exact App bridge selected by Program.SetUp; no fake process/message/state.
    internal sealed class ObservedHost(IProjectPlayHost bridge):IProjectPlayHost
    {
        public readonly IProjectPlayHost Bridge=bridge;
        public int Starts;
        public PlayLaunch? Launch;
        public ObservedSession? Session;
        public IProjectPlaySession Start(PlayLaunch launch)
        {Starts++;Launch=launch;return Session=new(Bridge.Start(launch),launch);}
    }
    internal sealed class CommonRig:IDisposable
    {
        private readonly string temporary=Directory.CreateTempSubdirectory("bed-saved-journey-").FullName;
        public readonly string Root,File;
        public readonly HeadlessHost Headless=new(1100,850){Drawing=false};
        public readonly ObservedHost Factory;
        public readonly ProjectLauncher Launcher;
        public readonly IProjectAdapter Adapter;
        public readonly Scene Draft,Redo;
        public readonly long Revision;
        public readonly int Undo,Journal;
        public int Changes;
        private readonly bool external;
        public DataWorkspace Workspace=>Launcher.Workspace!;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public CommonRig(double seconds=5,int exitCode=0,bool external=false)
        {
            this.external=external;Root=Directory.CreateDirectory(Path.Combine(temporary,"project")).FullName;
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            File=Path.Combine(data,"level.json");System.IO.File.WriteAllText(File,Saved);
            System.IO.File.WriteAllText(Path.Combine(schemas,"scene.schema.json"),"""{"type":"object","required":["kind","version","objects"],"properties":{"kind":{"const":"scene"},"version":{"const":1},"objects":{"type":"array"}}}""");
            System.IO.File.WriteAllText(Path.Combine(Root,"reference_map.json"),"""{"game":"SceneStarter","labels":[],"references":[]}""");
            var game=Directory.CreateDirectory(Path.Combine(external?temporary:Root,external?"outside-game":"game")).FullName;
            foreach(var name in SceneStarter.ExportInventory.Required.Where(name=>name!="scene.json"))
                System.IO.File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name),Path.Combine(game,name));
            PlaySettingsStore.Save(Root,new(external?Path.Combine(game,"SceneStarter.exe"):"game/SceneStarter.exe",
                ["--project",".","--seconds",seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),"--exit-code",exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
            var adapters=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(adapters.Problems);Adapter=Assert.Single(adapters.Loaded);
            Launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(temporary,"local","recent.json"))){Loaded=adapters.Loaded});
            Factory=new(Assert.IsType<ProjectPlayBridge>(Launcher.ProjectPlayHost));Launcher.ProjectPlayHost=Factory;
            Assert.True(Launcher.Open(ProjectPaths.In(Root)!),Launcher.Said);Assert.Same(Adapter,Launcher.Using);
            Assert.True(Workspace.Show("level.json"),Workspace.Said);EditorWindow<Scene>.Configure(ImGui.GetIO());Headless.Frames(Draw,3);
            Assert.True(Window.Editor.Execute(new MoveObject("root",new Vector3(9,8,7)),"root"));
            Assert.True(Window.Editor.Execute(new MoveObject("root",new Vector3(10,8,7)),"root"));Assert.True(Window.Editor.Session.Undo());
            var session=Window.Editor.Session;Draft=session.Current;Revision=session.Revision;Undo=session.History.UndoCount;Journal=session.Journal.Count;
            var history=session.History.Copy();Assert.True(history.TryRedo(Draft,out var redo));Redo=redo;session.Changed+=()=>Changes++;Headless.Frames(Draw,2);
        }
        public void Draw()=>Workspace.Draw();
        public void Click(UiNode node)
        {Assert.True(node.Enabled,node.DisabledReason);Assert.False(node.Bounds.IsEmpty);var b=node.Bounds;Headless.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);Headless.Frames(Draw,2);}
        public void Play()
        {
            Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Play"));
            Assert.True(Workspace.ProjectPlaySavePending);Assert.Equal(0,Factory.Starts);Assert.Null(Factory.Session);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("Play runs saved files",StringComparison.Ordinal));
            foreach(var label in new[]{"Save","Don't Save","Cancel"})Assert.True(PromptButton(label).Enabled);
        }
        public UiNode PromptButton(string label)
        {
            var nodes=Ui.Snapshot().Nodes;var prompt=Assert.Single(nodes.Select((node,index)=>(node,index)),pair=>pair.node.Role==UiRole.Window&&pair.node.Label=="Play saved files").index;
            bool Inside(int index){for(var parent=nodes[index].Parent;parent>=0;parent=nodes[parent].Parent)if(parent==prompt)return true;return false;}
            return Assert.Single(nodes.Select((node,index)=>(node,index)),pair=>pair.node.Role==UiRole.Button&&pair.node.Label==label&&Inside(pair.index)).node;
        }
        public ObservedSession Choose(string label)
        {
            Click(PromptButton(label));Assert.False(Workspace.ProjectPlaySavePending);
            if(external)
            {
                Assert.True(Workspace.ProjectPlayApprovalPending);Assert.Equal(0,Factory.Starts);Assert.Null(Factory.Session);
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("SceneStarter.exe",StringComparison.Ordinal));
                Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Allow this exact command and continue"));
            }
            Assert.False(Workspace.ProjectPlayApprovalPending);Assert.Equal(1,Factory.Starts);
            var session=Assert.IsType<ObservedSession>(Workspace.ProjectPlay);Assert.Same(Factory.Session,session);Assert.Equal(1,session.Starts);
            Assert.Equal("Running.",session.StatusAtStart);Until(()=>session.Logs.Any(line=>line.Text.StartsWith("SceneStarter loaded saved level:",StringComparison.Ordinal)),session);
            Assert.False(session.HasCompleted);Assert.DoesNotContain(session.Logs,line=>line.Text.Contains("\"kind\"",StringComparison.Ordinal)||line.Text.Contains("Ready",StringComparison.Ordinal));
            return session;
        }
        public void AssertSavedInput(string expected)
        {
            Assert.Equal(expected,Factory.Session!.SavedAtStart);Assert.Equal(expected,System.IO.File.ReadAllText(File));
            var launch=Factory.Launch!;Assert.Equal(Root,launch.ProjectRoot);Assert.Equal(Root,launch.Run.WorkingDirectory);
            Assert.Equal(new[]{"--project","."},launch.Run.Arguments.Take(2));Assert.DoesNotContain(launch.Run.Arguments,arg=>arg.Contains(expected,StringComparison.Ordinal));
            var loaded=SceneStarter.SavedScene.Open(Root);var parsed=new SceneFormat().Read(expected);
            Assert.Equal(parsed.Objects.Select(o=>(o.Id,o.Parent,o.Position,o.Rotation,o.Scale)),loaded.Level.Scene.Objects.Select(o=>(o.Id,o.Parent,o.Position,o.Rotation,o.Scale)));
            Assert.Equal("root",loaded.Level.Scene.Find("child")!.References["anchor"]);
        }
        public void Until(Func<bool> condition,ObservedSession session)
        {var time=Stopwatch.StartNew();while(!condition()&&time.Elapsed<TimeSpan.FromSeconds(6)){Headless.Frame(Draw);Thread.Sleep(5);}Assert.True(condition(),session.Status);}
        public void Unchanged(bool dirty=true,string? disk=null)
        {
            var session=Window.Editor.Session;Assert.Same(Draft,session.Current);Assert.Equal(Revision,session.Revision);
            Assert.Equal(Undo,session.History.UndoCount);Assert.Equal(Journal,session.Journal.Count);Assert.Equal(dirty,session.Dirty);
            var history=session.History.Copy();Assert.True(history.TryRedo(session.Current,out var redo));Assert.Same(Redo,redo);
            Assert.Equal(0,Changes);Assert.Equal(disk??Saved,System.IO.File.ReadAllText(File));
        }
        public void Stop(ObservedSession session)
        {
            Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Stop"));Until(()=>session.HasCompleted,session);Headless.Frame(Draw);
            Assert.Equal(1,session.Stops);Assert.Equal("The program has no window to close, so BED ended it.",session.Status);
            Assert.False(Window.ProjectPlaying!());Assert.Null(Window.Editor.Playing);Assert.True(session.HasCompleted);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains(session.Status,StringComparison.Ordinal));
        }
        public void Dispose()
        {
            Launcher.Dispose();var timer=Stopwatch.StartNew();while(Factory.Session is {HasCompleted:false}&&timer.Elapsed<TimeSpan.FromSeconds(3))Thread.Sleep(5);
            Assert.True(Factory.Session?.HasCompleted??true);Headless.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),temporary,StringComparison.OrdinalIgnoreCase);Directory.Delete(temporary,true);
        }
    }
    [Fact]
    public void Unsaved_sample_play_prompts_and_cancel_click_never_starts_or_saves()
    {
        using var r=new CommonRig();r.Play();r.Unchanged();r.Click(r.PromptButton("Cancel"));
        Assert.False(r.Workspace.ProjectPlaySavePending);Assert.False(r.Workspace.ProjectPlayApprovalPending);Assert.Equal(0,r.Factory.Starts);
        Assert.Null(r.Factory.Session);Assert.Null(r.Workspace.ProjectPlay);Assert.Null(r.Window.Editor.Game);r.Unchanged();
    }
    [Fact]
    public void Dont_save_then_natural_nonzero_owned_sample_exit_keeps_exact_draft_redo_journal_and_disk()
    {
        using var r=new CommonRig(seconds:1,exitCode:7);r.Play();var game=r.Choose("Don't Save");r.Unchanged();r.AssertSavedInput(Saved);
        r.Until(()=>game.HasCompleted,game);r.Headless.Frame(r.Draw);Assert.Equal(0,game.Stops);Assert.True(game.HasCompleted);
        Assert.Contains("Play failed",game.Status);Assert.Contains("code 7",game.Status);Assert.Contains(game.Logs,line=>line.Error&&line.Text.Contains("code 7",StringComparison.Ordinal));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("code 7",StringComparison.Ordinal));
        Assert.False(r.Window.ProjectPlaying!());Assert.Null(r.Window.Editor.Playing);r.Unchanged();
        Assert.True(r.Window.Editor.Execute(new MoveObject("root",new Vector3(11,8,7)),"root"));Assert.True(r.Window.Editor.Session.Undo());Assert.Same(r.Draft,r.Window.Editor.Session.Current);
        Assert.Equal(Saved,System.IO.File.ReadAllText(r.File));
    }
    [Fact]
    public void Save_click_uses_authoritative_scene_save_then_runs_that_exact_saved_input()
    {
        using var r=new CommonRig();var expected=r.Window.Editor.Session.TextToSave;r.Play();var game=r.Choose("Save");
        r.AssertSavedInput(expected);Assert.Equal(expected,r.Window.Editor.Session.ExpectedOnDisk);r.Unchanged(false,expected);r.Stop(game);r.Unchanged(false,expected);
    }
    [Fact]
    public void Refused_scene_save_keeps_the_visible_prompt_and_all_edits_without_starting()
    {
        using var r=new CommonRig();r.Workspace.Documents.SceneSaveCheck=_=>"Invented scene policy refuses Save.";
        r.Play();r.Click(r.PromptButton("Save"));Assert.True(r.Workspace.ProjectPlaySavePending);Assert.Equal(0,r.Factory.Starts);Assert.Null(r.Factory.Session);
        Assert.Null(r.Workspace.ProjectPlay);Assert.Null(r.Window.Editor.Game);Assert.Contains("Invented scene policy refuses Save",r.Workspace.Said);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("Invented scene policy refuses Save",StringComparison.Ordinal));
        r.Unchanged();r.Click(r.PromptButton("Cancel"));Assert.False(r.Workspace.ProjectPlaySavePending);r.Unchanged();
    }
    [Fact]
    public void External_sample_command_waits_for_exact_visible_approval_before_any_owned_process()
    {
        using var r=new CommonRig(external:true);r.Play();var game=r.Choose("Don't Save");r.AssertSavedInput(Saved);r.Unchanged();r.Stop(game);r.Unchanged();
    }
}
