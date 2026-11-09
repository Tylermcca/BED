using System.Numerics;
using System.Text;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class LinkedPlayAcceptanceTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>ticks;
        public void Advance(TimeSpan time)=>ticks+=time.Ticks;
    }
    private sealed class Control : ILinkedPlayControl
    {
        public int Starts,Stops,Aborts;
        public bool ThrowOnStart;
        public void Start(PlayHello hello,PlaySnapshot input) {Starts++;if(ThrowOnStart) throw new InvalidOperationException("Invented start failure");}
        public void RequestStop(PlayStop stop)=>Stops++;
        public void AbortOwned()=>Aborts++;
    }
    private sealed class Rig : IDisposable
    {
        private readonly string directory=Directory.CreateTempSubdirectory("bed-linked-play-").FullName;
        public readonly string File;
        public readonly string Saved;
        public readonly DocumentSession<Scene> Draft;
        public readonly EditorModel<Scene> Editor;
        public readonly Scene Before;
        public readonly long Revision;
        public readonly int Undo,Journal;
        public readonly Scene RedoDocument;
        public bool Complete;
        public int Changes,Pumps;
        public bool ThrowOnPump;
        public readonly Clock Clock=new();
        public readonly Control Control=new();
        public readonly Queue<PlayMessage> Incoming=[];
        public readonly LinkedPlaySession Linked;
        public readonly LinkedPlayPanel Panel;
        public readonly HeadlessHost Host=new(1000,800) {Drawing=false};
        public Rig(float scale=1,PlayInputChoice choice=PlayInputChoice.Draft,TimeSpan? stopTimeout=null)
        {
            File=Path.Combine(directory,"invented.scene");
            Saved=new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)]));
            System.IO.File.WriteAllText(File,Saved);
            Draft=DocumentSession<Scene>.Open(File,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());
            Editor=new(Draft,SceneEditing.Outline);
            Assert.True(Draft.Execute(new MoveObject("box",Vector3.UnitX)));
            Assert.True(Draft.Execute(new MoveObject("box",new(2,0,0))));Assert.True(Draft.Undo());
            Before=Draft.Current;Revision=Draft.Revision;Undo=Draft.History.UndoCount;Journal=Draft.Journal.Count;
            var history=Draft.History.Copy();Assert.True(history.TryRedo(Before,out var redo));RedoDocument=redo;Assert.False(history.CanRedo);
            Draft.Changed+=()=>Changes++;
            var input=PlaySnapshot.FromSession(Draft,"invented.scene",choice,source=>new("invented.runtime",Encoding.UTF8.GetBytes(source.Text))).Snapshot!;
            Linked=new(Control,input,Guid.NewGuid(),Clock,TimeSpan.FromSeconds(2),stopTimeout ?? TimeSpan.FromSeconds(1));
            Panel=new(Linked,Pump);
            EditorWindow<Scene>.Configure(ImGui.GetIO());EditorWindow<Scene>.SetTextScale(scale);Host.Frames(Draw,3);
        }
        public void Pump(LinkedPlaySession session)
        {
            Pumps++;if(ThrowOnPump) throw new InvalidOperationException("Invented pump failure");
            while(Incoming.TryDequeue(out var message)) session.Receive(message);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0));ImGui.SetNextWindowSize(new float2(1000,800));
            ImGui.Begin("Supplied linked Play"u8,ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings);
            Panel.Draw();ImGui.End();
        }
        public void Click(string label)
        {
            var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label==label);
            var b=button.Bounds;Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);Host.Frames(Draw,2);
        }
        public void Ready()
        {
            Incoming.Enqueue(new PlayReady(1,Linked.Correlation,PlayProtocol.Known));Host.Frames(Draw,2);
            Assert.Equal(LinkedPlayState.Running,Linked.State);
        }
        public void AssertUnchanged()
        {
            Assert.Same(Before,Draft.Current);Assert.Equal(Revision,Draft.Revision);
            Assert.Equal(Undo,Draft.History.UndoCount);Assert.Equal(Journal,Draft.Journal.Count);
            var history=Draft.History.Copy();Assert.True(history.TryRedo(Draft.Current,out var redo));Assert.Same(RedoDocument,redo);Assert.False(history.CanRedo);
            Assert.True(Draft.Dirty);Assert.Equal(0,Changes);Assert.Equal(Saved,System.IO.File.ReadAllText(File));
        }
        public void Dispose()
        {
            Panel.Dispose();Host.Dispose();System.IO.File.Delete(File);Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Supplied_panel_start_stop_and_ack_show_real_states_without_draft_history_or_disk_changes(float scale)
    {
        using var r=new Rig(scale);
        Assert.Equal(0,r.Control.Starts);Assert.Equal(0,r.Pumps);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label.StartsWith("Input: Draft,"));
        r.Click("Start linked Play");Assert.Equal(1,r.Control.Starts);Assert.Equal(LinkedPlayState.Starting,r.Linked.State);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label.StartsWith("Starting:"));
        Assert.False(r.Panel.Start());r.Ready();r.AssertUnchanged();
        r.Click("Stop linked Play");
        Assert.Equal(LinkedPlayState.Stopping,r.Linked.State);Assert.Equal(1,r.Control.Stops);Assert.Equal(0,r.Control.Aborts);
        var stop=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Stop linked Play");
        Assert.False(stop.Enabled);Assert.Contains("already",stop.DisabledReason);
        Assert.False(r.Panel.RequestStop());r.AssertUnchanged();
        r.Incoming.Enqueue(new PlayStopped(1,r.Linked.Correlation));r.Host.Frames(r.Draw,2);
        Assert.Equal(LinkedPlayState.Stopped,r.Linked.State);Assert.Equal(1,r.Control.Aborts);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label=="Stopped.");
        r.AssertUnchanged();
    }

    [Theory]
    [InlineData("start")] [InlineData("runtime")] [InlineData("pump")] [InlineData("timeout")]
    public void Supplied_failures_keep_exact_unsaved_undo_redo_journal_and_saved_bytes(string failure)
    {
        using var r=new Rig();r.Control.ThrowOnStart=failure=="start";
        var started=r.Panel.Start();
        if(failure=="start") Assert.False(started);
        else
        {
            Assert.True(started);r.Ready();
            if(failure=="runtime") r.Incoming.Enqueue(new PlayFailure(1,r.Linked.Correlation,new(PlayErrorCode.RuntimeFailed,"Invented runtime failure")));
            if(failure=="pump") r.ThrowOnPump=true;
            if(failure=="timeout") {Assert.True(r.Panel.RequestStop());r.Clock.Advance(TimeSpan.FromSeconds(1));}
        }
        r.Host.Frames(r.Draw,2);Assert.Equal(LinkedPlayState.Faulted,r.Linked.State);Assert.Equal(1,r.Control.Aborts);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label.StartsWith("Play failed:"));
        r.AssertUnchanged();
    }

    [Theory]
    [InlineData(PlayInputChoice.Saved)] [InlineData(PlayInputChoice.Draft)]
    public void Supplied_choice_and_revision_never_silently_change_on_start_or_stop(PlayInputChoice choice)
    {
        using var r=new Rig(choice:choice);var hash=r.Linked.Snapshot.Hash;
        Assert.Equal(choice,r.Linked.Snapshot.Choice);
        var payload=Encoding.UTF8.GetString(r.Linked.Snapshot.CopyPayload());
        Assert.Equal(choice==PlayInputChoice.Saved ? r.Saved : r.Draft.TextToSave,payload);
        Assert.True(r.Panel.Start());r.Ready();Assert.True(r.Panel.RequestStop());
        r.Incoming.Enqueue(new PlayStopped(1,r.Linked.Correlation));r.Host.Frames(r.Draw,2);
        Assert.Equal(hash,r.Linked.Snapshot.Hash);Assert.Equal(r.Revision,r.Linked.Snapshot.Revision);r.AssertUnchanged();
    }

    [Fact]
    public void Workspace_owner_dispose_aborts_once_and_never_keeps_ticking_a_closed_owner()
    {
        using var r=new Rig();Assert.True(r.Panel.Start());r.Host.Frames(r.Draw,2);
        r.Panel.Dispose();var pumps=r.Pumps;r.Panel.Dispose();r.Host.Frames(r.Draw,2);
        Assert.Equal(1,r.Control.Aborts);Assert.Equal(pumps,r.Pumps);
        Assert.False(r.Panel.Start());Assert.False(r.Panel.RequestStop());r.AssertUnchanged();
    }

    [Fact]
    public void Existing_in_process_preview_registration_and_stop_are_unchanged_by_standalone_linked_presentation()
    {
        using var r=new Rig();var probe=new Preview();
        r.Editor.Add(new PlayMode<Scene>("invented.preview","Invented preview",_=>probe));
        var window=new EditorWindow<Scene>(r.Editor);
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+"invented.preview"));Assert.Same(probe,r.Editor.Game);
        Assert.True(window.Run("editor.stop"));Assert.True(probe.Disposed);
        Assert.Equal(0,r.Control.Starts);Assert.Equal(LinkedPlayState.Created,r.Linked.State);r.AssertUnchanged();
    }
    [Fact]
    public void Existing_window_stop_requests_then_waits_for_actual_completion_before_returning_to_editing()
    {
        using var r=new Rig(choice:PlayInputChoice.Saved,stopTimeout:TimeSpan.FromSeconds(5));
        var window=new EditorWindow<Scene>(r.Editor);
        r.Editor.Add(new PlayMode<Scene>("invented.linked","Invented linked",_=>new LinkedPlayPreview(r.Linked,r.Pump,()=>r.Complete)));
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+"invented.linked"));
        r.Incoming.Enqueue(new PlayReady(1,r.Linked.Correlation,PlayProtocol.Known));r.Host.Frames(window.Draw,2);
        Assert.Equal(LinkedPlayState.Running,r.Linked.State);r.AssertUnchanged();
        Assert.True(window.Run("editor.stop"));Assert.Equal(LinkedPlayState.Stopping,r.Linked.State);
        Assert.NotNull(r.Editor.Playing);Assert.Equal(0,r.Control.Aborts);
        r.Incoming.Enqueue(new PlayStopped(1,r.Linked.Correlation));r.Host.Frames(window.Draw,2);
        Assert.Equal(LinkedPlayState.Stopped,r.Linked.State);Assert.NotNull(r.Editor.Playing);Assert.Equal(1,r.Control.Aborts);
        r.Complete=true;r.Host.Frames(window.Draw,2);
        Assert.Null(r.Editor.Playing);Assert.Null(r.Editor.Game);Assert.Equal("Stopped.",window.Status);r.AssertUnchanged();
    }

    [Fact]
    public void Linked_window_stop_uses_explicit_five_second_deadline_but_owner_teardown_is_immediate()
    {
        using var r=new Rig(choice:PlayInputChoice.Saved,stopTimeout:TimeSpan.FromSeconds(5));
        var window=new EditorWindow<Scene>(r.Editor);
        r.Editor.Add(new PlayMode<Scene>("invented.linked","Invented linked",_=>new LinkedPlayPreview(r.Linked,r.Pump,()=>r.Complete)));
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+"invented.linked"));
        r.Incoming.Enqueue(new PlayReady(1,r.Linked.Correlation,PlayProtocol.Known));r.Host.Frames(window.Draw,2);
        Assert.True(window.Run("editor.stop"));r.Clock.Advance(TimeSpan.FromSeconds(5)-TimeSpan.FromTicks(1));r.Host.Frames(window.Draw,2);
        Assert.Equal(LinkedPlayState.Stopping,r.Linked.State);Assert.Equal(0,r.Control.Aborts);
        r.Clock.Advance(TimeSpan.FromTicks(1));r.Host.Frames(window.Draw,2);
        Assert.Equal(LinkedPlayState.Faulted,r.Linked.State);Assert.Equal(1,r.Control.Aborts);Assert.NotNull(r.Editor.Playing);
        r.Complete=true;r.Host.Frames(window.Draw,2);
        Assert.Null(r.Editor.Playing);Assert.Contains("timed out",window.Status);r.AssertUnchanged();
    }

    [Fact]
    public void Concrete_linked_preview_refuses_draft_input_and_force_dispose_keeps_saved_and_dirty_state()
    {
        using(var draft=new Rig())
        {
            Assert.Throws<InvalidOperationException>(()=>new LinkedPlayPreview(draft.Linked,draft.Pump,()=>true));
            Assert.Equal(0,draft.Control.Starts);draft.AssertUnchanged();
        }
        using var r=new Rig(choice:PlayInputChoice.Saved);
        r.Editor.Add(new PlayMode<Scene>("invented.linked","Invented linked",_=>new LinkedPlayPreview(r.Linked,r.Pump,()=>false)));
        Assert.True(r.Editor.Play("invented.linked"));Assert.Equal(LinkedPlayState.Starting,r.Linked.State);
        Assert.True(r.Editor.Stop());Assert.Equal(1,r.Control.Aborts);Assert.Null(r.Editor.Playing);r.AssertUnchanged();
    }

    [Fact]
    public void Window_contains_completion_observer_fault_and_returns_to_editing_without_mutation()
    {
        using var r=new Rig(choice:PlayInputChoice.Saved);
        var window=new EditorWindow<Scene>(r.Editor);
        r.Editor.Add(new PlayMode<Scene>("invented.linked","Invented linked",_=>new LinkedPlayPreview(r.Linked,r.Pump,
            ()=>throw new InvalidOperationException("Invented completion failure"))));
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+"invented.linked"));
        r.Incoming.Enqueue(new PlayReady(1,r.Linked.Correlation,PlayProtocol.Known));
        r.Incoming.Enqueue(new PlayStopped(1,r.Linked.Correlation));r.Host.Frames(window.Draw,2);
        Assert.Null(r.Editor.Playing);Assert.Null(r.Editor.Game);Assert.Equal(1,r.Control.Aborts);
        Assert.Contains("Invented completion failure",window.Status);r.AssertUnchanged();
    }

    private sealed class DeclaredHost : ILinkedPlayHost
    {
        public int Starts;
        private Control? last;
        public int Aborts=>last?.Aborts ?? 0;
        public PlaySnapshot? Input;
        public LinkedPlayPreview Start(GameLaunchSpec spec,PlaySnapshot snapshot)
        {
            Starts++;Input=snapshot;
            var control=last=new Control();var session=new LinkedPlaySession(control,snapshot,Guid.NewGuid(),new Clock(),
                TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(5));
            return new(session,_=>{},()=>true);
        }
    }
    private static EditorWindow<Scene> BoundWindow(Rig r,DeclaredHost host)
    {
        var window=new EditorWindow<Scene>(r.Editor);
        window.BindLinkedPlay(new GameLaunchSpec(Path.Combine(AppContext.BaseDirectory,"invented.exe"),AppContext.BaseDirectory,[]),
            input=>new("invented.runtime",Encoding.UTF8.GetBytes(input.Text)),host,"invented.scene");
        return window;
    }

    [Theory]
    [InlineData(SaveChangesChoice.Cancel)] [InlineData(SaveChangesChoice.DontSave)]
    public void Bound_play_requires_explicit_saved_choice_and_cancel_or_dont_save_preserves_exact_draft(SaveChangesChoice choice)
    {
        using var r=new Rig();var host=new DeclaredHost();var window=BoundWindow(r,host);
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+EditorWindow<Scene>.LinkedPlayModeId));
        Assert.True(window.LinkedPlaySavePending);Assert.Equal(0,host.Starts);
        r.Host.Frames(window.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label=="Play runs your saved files");
        var started=window.AnswerLinkedPlay(choice);
        Assert.Equal(choice==SaveChangesChoice.DontSave,started);Assert.False(window.LinkedPlaySavePending);
        Assert.Equal(choice==SaveChangesChoice.DontSave ? 1 : 0,host.Starts);
        if(host.Input is { } input)
        {
            Assert.Equal(PlayInputChoice.Saved,input.Choice);Assert.Equal(r.Saved,Encoding.UTF8.GetString(input.CopyPayload()));
            Assert.True(r.Editor.Stop());
        }
        r.AssertUnchanged();
    }

    [Fact]
    public void Bound_save_choice_explicitly_saves_then_captures_that_saved_version_once()
    {
        using var r=new Rig();var host=new DeclaredHost();var window=BoundWindow(r,host);var expected=r.Draft.TextToSave;
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+EditorWindow<Scene>.LinkedPlayModeId));
        Assert.True(window.AnswerLinkedPlay(SaveChangesChoice.Save));Assert.Equal(1,host.Starts);
        Assert.False(r.Draft.Dirty);Assert.Same(r.Before,r.Draft.Current);Assert.Equal(r.Revision,r.Draft.Revision);
        Assert.Equal(r.Undo,r.Draft.History.UndoCount);Assert.Equal(r.Journal,r.Draft.Journal.Count);
        Assert.Equal(expected,System.IO.File.ReadAllText(r.File));
        Assert.Equal(expected,Encoding.UTF8.GetString(host.Input!.CopyPayload()));Assert.Equal(PlayInputChoice.Saved,host.Input.Choice);
        Assert.True(r.Editor.Stop());Assert.False(r.Draft.Dirty);
    }

    [Fact]
    public void Bound_refused_save_keeps_prompt_and_never_starts_the_declared_game()
    {
        using var r=new Rig();var host=new DeclaredHost();var window=BoundWindow(r,host);
        window.SaveAction=()=> (false,"Invented authoritative save refusal");
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+EditorWindow<Scene>.LinkedPlayModeId));
        Assert.False(window.AnswerLinkedPlay(SaveChangesChoice.Save));Assert.True(window.LinkedPlaySavePending);
        Assert.Equal(0,host.Starts);Assert.Contains("Invented authoritative save refusal",window.Status);r.AssertUnchanged();
        Assert.True(window.HandleKey(Chord.Parse("Escape")));Assert.False(window.LinkedPlaySavePending);r.AssertUnchanged();
    }

    [Fact]
    public void Bound_stale_prompt_refuses_without_discarding_the_newer_edit()
    {
        using var r=new Rig();var host=new DeclaredHost();var window=BoundWindow(r,host);
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+EditorWindow<Scene>.LinkedPlayModeId));
        Assert.True(r.Draft.Execute(new MoveObject("box",new(9,0,0))));
        Assert.False(window.AnswerLinkedPlay(SaveChangesChoice.DontSave));
        Assert.Equal(0,host.Starts);Assert.False(window.LinkedPlaySavePending);
        Assert.Equal(new Vector3(9,0,0),r.Draft.Current.Find("box")!.Position);Assert.True(r.Draft.Dirty);
        Assert.Equal(r.Saved,System.IO.File.ReadAllText(r.File));
    }

    [Fact]
    public void Bound_background_save_completion_does_not_start_another_save_check()
    {
        using var r=new Rig();var host=new DeclaredHost();var window=BoundWindow(r,host);var saves=0;
        window.SaveAction=()=>{saves++;return(false,"Invented pending authoritative save");};
        Assert.True(window.Run(EditorWindow<Scene>.PlayAction+EditorWindow<Scene>.LinkedPlayModeId));
        Assert.False(window.AnswerLinkedPlay(SaveChangesChoice.Save));Assert.True(window.LinkedPlaySavePending);
        r.Draft.Save(); // the already-requested authoritative background operation completes
        Assert.True(window.AnswerLinkedPlay(SaveChangesChoice.Save));Assert.Equal(1,saves);Assert.Equal(1,host.Starts);
        Assert.Equal(PlayInputChoice.Saved,host.Input!.Choice);Assert.False(r.Draft.Dirty);Assert.True(r.Editor.Stop());
    }

    [Fact]
    public void Bound_workspace_reuses_scene_mode_and_file_switch_force_stops_without_saving_drafts()
    {
        var root=Directory.CreateTempSubdirectory("bed-linked-workspace-").FullName;
        var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
        var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
        var saved=new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)]));
        try
        {
            System.IO.File.WriteAllText(Path.Combine(data,"one.json"),saved);System.IO.File.WriteAllText(Path.Combine(data,"two.json"),saved);
            var project=DataProject.Open(data,schemas);
            Assert.True(DocumentHost.For(project).BindScene("one.json","scene",out _));
            Assert.True(DocumentHost.For(project).BindScene("two.json","scene",out _));
            using var first=new DataWorkspace(project);using var second=new DataWorkspace(project);
            var host=new DeclaredHost();var spec=new GameLaunchSpec(Path.Combine(AppContext.BaseDirectory,"invented.exe"),AppContext.BaseDirectory,[]);
            PlaySnapshotConverter convert=input=>new("invented.runtime",Encoding.UTF8.GetBytes(input.Text));
            first.BindLinkedPlay(spec,convert,host);second.BindLinkedPlay(spec,convert,host);
            Assert.True(first.Show("one.json"),first.Said);Assert.True(second.Show("one.json"),second.Said);
            var window=second.SceneWindow!;Assert.Single(window.Editor.PlayModes,m=>m.Id==EditorWindow<Scene>.LinkedPlayModeId);
            Assert.True(window.Editor.Execute(new MoveObject("box",Vector3.UnitX),"box"));var draft=window.Editor.Session.Current;
            Assert.True(window.Run(EditorWindow<Scene>.PlayAction+EditorWindow<Scene>.LinkedPlayModeId));
            Assert.True(window.AnswerLinkedPlay(SaveChangesChoice.DontSave),window.Status);Assert.Equal(1,host.Starts);
            Assert.True(first.Show("two.json"),first.Said);
            Assert.Null(window.Editor.Playing);Assert.Equal(1,host.Aborts);Assert.Same(draft,window.Editor.Session.Current);
            Assert.True(window.Editor.Session.Dirty);Assert.Equal(1,window.Editor.Session.History.UndoCount);
            Assert.Equal(saved,System.IO.File.ReadAllText(Path.Combine(data,"one.json")));
        }
        finally
        {
            System.IO.File.Delete(Path.Combine(data,"one.json"));System.IO.File.Delete(Path.Combine(data,"two.json"));
            Directory.Delete(data);Directory.Delete(schemas);Directory.Delete(root);
        }
    }

    private sealed class Preview : IPlaySession
    {
        public bool Disposed;
        public string Status=>"Invented in-process preview";
        public void Draw() { }
        public void Dispose()=>Disposed=true;
    }
}
