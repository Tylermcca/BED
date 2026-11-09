using System.Diagnostics;
using System.Numerics;
using System.Text;
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
    // Observes only the production host instance this fixture owns. No fake message, PID discovery or unrelated kill.
    private sealed class ObservedHost : ILinkedPlayHost,IDisposable
    {
        public int Starts;
        public GameProcessHost? Peer;
        public LinkedPlayPreview Start(GameLaunchSpec spec,PlaySnapshot snapshot)
        {
            Starts++;Peer=new GameProcessHost(spec,16*1024*1024,64*1024,256);
            var peer=Peer;var session=new LinkedPlaySession(peer,snapshot,Guid.NewGuid(),TimeProvider.System,TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(5));
            return new(session,peer.Pump,()=>peer.Completion?.IsCompleted==true);
        }
        public void Dispose()=>Peer?.Dispose();
    }
    private sealed class Rig : IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("bed-qa05-").FullName;
        public const string Saved="""{"kind":"scene","version":1,"objects":[{"id":"root","position":[2,3,4]},{"id":"child","parent":"root","position":[1,0,0],"references":{"anchor":"root"}}]}""";
        public readonly HeadlessHost Host=new(1100,850) {Drawing=false};
        public readonly ObservedHost Linked=new();
        public readonly ProjectLauncher Launcher;
        public readonly string File;
        public readonly Scene Draft,Redo;
        public readonly long Revision;
        public readonly int Undo,Journal;
        public int Changes;
        public DataWorkspace Workspace=>Launcher.Workspace!;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public Rig()
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File=Path.Combine(data,"level.json");System.IO.File.WriteAllText(File,Saved);
            System.IO.File.WriteAllText(Path.Combine(schemas,"scene.schema.json"),"""{"type":"object","required":["kind","version","objects"],"properties":{"kind":{"const":"scene"},"version":{"const":1},"objects":{"type":"array"}}}""");
            System.IO.File.WriteAllText(Path.Combine(root,"reference_map.json"),"""{"game":"SceneStarter","labels":[],"references":[]}""");
            var adapters=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);
            Assert.Empty(adapters.Problems);Assert.Single(adapters.Loaded);
            Launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(root,"recent.json"))) {Loaded=adapters.Loaded,LinkedPlayHost=Linked});
            Assert.Same(Linked,Launcher.LinkedPlayHost);Assert.True(Launcher.Open(ProjectPaths.In(root)!),Launcher.Said);
            Assert.True(Workspace.Show("level.json"),Workspace.Said);EditorWindow<Scene>.Configure(ImGui.GetIO());
            Host.Frames(Draw,3);
            Assert.True(Window.Editor.Execute(new MoveObject("root",new Vector3(9,8,7)),"root"));
            Assert.True(Window.Editor.Execute(new MoveObject("root",new Vector3(10,8,7)),"root"));Assert.True(Window.Editor.Session.Undo());
            var session=Window.Editor.Session;Draft=session.Current;Revision=session.Revision;Undo=session.History.UndoCount;Journal=session.Journal.Count;
            var history=session.History.Copy();Assert.True(history.TryRedo(Draft,out var redo));Redo=redo;
            session.Changed+=()=>Changes++;Host.Frames(Draw,2);
        }
        public void Draw()=>Window.DrawDocked();
        public void Click(UiNode node)
        {
            Assert.True(node.Enabled,node.DisabledReason);Assert.False(node.Bounds.IsEmpty);
            var b=node.Bounds;Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);Host.Frames(Draw,2);
        }
        public void Play()
        {
            Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Play"));
            Assert.True(Window.LinkedPlaySavePending);Assert.Equal(0,Linked.Starts);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label=="Play runs your saved files");
            foreach(var label in new[]{"Save","Don't Save","Cancel"}) Assert.True(PromptButton(label).Enabled);
        }
        public UiNode PromptButton(string label)
        {
            var nodes=Ui.Snapshot().Nodes;
            var prompt=Assert.Single(nodes.Select((node,index)=>(node,index)),pair=>pair.node.Role==UiRole.Window && pair.node.Label=="Play saved files").index;
            bool Inside(int index)
            {for(var parent=nodes[index].Parent;parent>=0;parent=nodes[parent].Parent) if(parent==prompt) return true;return false;}
            return Assert.Single(nodes.Select((node,index)=>(node,index)),pair=>pair.node.Role==UiRole.Button && pair.node.Label==label && Inside(pair.index)).node;
        }
        public LinkedPlayPreview Choose(string label)
        {
            Click(PromptButton(label));Assert.False(Window.LinkedPlaySavePending);
            var preview=Assert.IsType<LinkedPlayPreview>(Window.Editor.Game);Assert.Equal(1,Linked.Starts);
            Until(()=>preview.Session.State is LinkedPlayState.Running or LinkedPlayState.Faulted,preview);
            Assert.Equal(LinkedPlayState.Running,preview.Session.State);Assert.Null(preview.Session.Error);return preview;
        }
        public void Until(Func<bool> condition,LinkedPlayPreview preview)
        {
            var clock=Stopwatch.StartNew();while(!condition() && clock.Elapsed<TimeSpan.FromSeconds(5)) {Host.Frame(Draw);Thread.Sleep(5);}
            Assert.True(condition(),preview.Status);
        }
        public void Unchanged(bool dirty=true,string? disk=null)
        {
            var session=Window.Editor.Session;Assert.Same(Draft,session.Current);Assert.Equal(Revision,session.Revision);
            Assert.Equal(Undo,session.History.UndoCount);Assert.Equal(Journal,session.Journal.Count);Assert.Equal(dirty,session.Dirty);
            var history=session.History.Copy();Assert.True(history.TryRedo(session.Current,out var redo));Assert.Same(Redo,redo);
            Assert.Equal(0,Changes);Assert.Equal(disk ?? Saved,System.IO.File.ReadAllText(File));
        }
        public void Stop(LinkedPlayPreview preview)
        {
            Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Stop"));
            Until(()=>Window.Editor.Playing is null,preview);
            Assert.Equal(LinkedPlayState.Stopped,preview.Session.State);Assert.Null(preview.Session.Error);Assert.True(preview.HasCompleted);
        }
        public void Dispose()
        {
            Launcher.Dispose();Linked.Dispose();Host.Dispose();
            var owned=Path.GetFullPath(root);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),owned,StringComparison.OrdinalIgnoreCase);
            Directory.Delete(owned,true);
        }
    }

    [Fact]
    public void Unsaved_sample_play_prompts_and_cancel_click_never_starts_or_saves()
    {
        using var r=new Rig();r.Play();r.Unchanged();r.Click(r.PromptButton("Cancel"));
        Assert.False(r.Window.LinkedPlaySavePending);Assert.Equal(0,r.Linked.Starts);Assert.Null(r.Linked.Peer);Assert.Null(r.Window.Editor.Game);r.Unchanged();
    }

    [Fact]
    public void Dont_save_then_abrupt_owned_sample_exit_keeps_exact_draft_redo_journal_and_disk()
    {
        using var r=new Rig();r.Play();var preview=r.Choose("Don't Save");r.Unchanged();
        Assert.Equal(Rig.Saved,Encoding.UTF8.GetString(preview.Session.Snapshot.CopyPayload()));
        // Fault injection closes only this sample's owned Job, without RequestStop; actual EOF/Completion drives the UI fault.
        r.Linked.Peer!.AbortOwned();r.Until(()=>r.Window.Editor.Playing is null,preview);
        Assert.True(r.Linked.Peer.Completion!.IsCompleted);Assert.Equal(LinkedPlayState.Faulted,preview.Session.State);
        Assert.Equal(PlayErrorCode.RuntimeFailed,preview.Session.Error!.Code);Assert.Contains("exited unexpectedly",preview.Session.Error.Message);
        Assert.Contains("Play failed",r.Window.Status);Assert.True(preview.HasCompleted);r.Unchanged();
        Assert.True(r.Window.Editor.Execute(new MoveObject("root",new Vector3(11,8,7)),"root"));
        Assert.True(r.Window.Editor.Session.Undo());Assert.Same(r.Draft,r.Window.Editor.Session.Current);
        Assert.Equal(Rig.Saved,System.IO.File.ReadAllText(r.File));
    }

    [Fact]
    public void Save_click_uses_authoritative_scene_save_then_runs_that_exact_saved_input()
    {
        using var r=new Rig();var expected=r.Window.Editor.Session.TextToSave;r.Play();var preview=r.Choose("Save");
        Assert.Equal(expected,Encoding.UTF8.GetString(preview.Session.Snapshot.CopyPayload()));
        Assert.Equal(expected,r.Window.Editor.Session.ExpectedOnDisk);r.Unchanged(false,expected);r.Stop(preview);r.Unchanged(false,expected);
    }

    [Fact]
    public void Refused_scene_save_keeps_the_visible_prompt_and_all_edits_without_starting()
    {
        using var r=new Rig();r.Workspace.Documents.SceneSaveCheck=_=>"Invented scene policy refuses Save.";
        r.Play();r.Click(r.PromptButton("Save"));Assert.True(r.Window.LinkedPlaySavePending);
        Assert.Equal(0,r.Linked.Starts);Assert.Null(r.Window.Editor.Game);Assert.Contains("Invented scene policy refuses Save",r.Window.Status);
        r.Unchanged();r.Click(r.PromptButton("Cancel"));Assert.False(r.Window.LinkedPlaySavePending);r.Unchanged();
    }
}
