using System.Text;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class LinkedPlayLifecycleTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>ticks;
        public void Advance(TimeSpan elapsed)=>ticks+=elapsed.Ticks;
    }
    private sealed class Control : ILinkedPlayControl
    {
        public int Starts,Stops,Aborts;
        public PlayHello? Hello;
        public PlaySnapshot? Input;
        public PlayStop? Stop;
        public Action? OnStart,OnStop,OnAbort;
        public void Start(PlayHello hello,PlaySnapshot snapshot) { Starts++;Hello=hello;Input=snapshot;OnStart?.Invoke(); }
        public void RequestStop(PlayStop stop) { Stops++;Stop=stop;OnStop?.Invoke(); }
        public void AbortOwned() { Aborts++;OnAbort?.Invoke(); }
    }
    private sealed class Rig : IDisposable
    {
        public readonly Control Control=new();
        public readonly Clock Clock=new();
        public readonly PlaySnapshot Snapshot=PlaySnapshot.Create(new("invented.scene",4,null,"invented draft"),PlayInputChoice.Draft,
            input=>new("invented.runtime",Encoding.UTF8.GetBytes(input.Text))).Snapshot!;
        public readonly LinkedPlaySession Session;
        public Rig()=>Session=new(Control,Snapshot,Guid.NewGuid(),Clock,TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(1));
        public PlayReady Ready()=>new(1,Session.Correlation,PlayProtocol.Known);
        public void Run() { Assert.True(Session.Start());Assert.True(Session.Receive(Ready()));Assert.Equal(LinkedPlayState.Running,Session.State); }
        public void Dispose()=>Session.Dispose();
    }

    [Fact]
    public void Start_ready_stop_complete_owns_exact_input_and_cleanup_once()
    {
        using var r=new Rig();
        Assert.Equal(LinkedPlayState.Created,r.Session.State); Assert.Equal(0,r.Control.Starts);
        Assert.True(r.Session.Start()); Assert.False(r.Session.Start()); Assert.Equal(1,r.Control.Starts);
        Assert.Same(r.Snapshot,r.Control.Input); Assert.Same(r.Session.Hello,r.Control.Hello);
        Assert.Equal(LinkedPlayState.Starting,r.Session.State); Assert.Equal(PlayCapability.None,r.Session.Capabilities);
        Assert.True(r.Session.Receive(r.Ready())); Assert.Equal(LinkedPlayState.Running,r.Session.State);
        Assert.Equal(PlayProtocol.Known,r.Session.Capabilities);
        Assert.True(r.Session.RequestStop()); Assert.False(r.Session.RequestStop()); Assert.Equal(1,r.Control.Stops);
        Assert.Equal(r.Session.Correlation,r.Control.Stop!.Correlation); Assert.Equal(LinkedPlayState.Stopping,r.Session.State);
        Assert.True(r.Session.Receive(new PlayStopped(1,r.Session.Correlation)));
        Assert.Equal(LinkedPlayState.Stopped,r.Session.State); Assert.Equal(1,r.Control.Aborts);
        r.Session.Dispose(); r.Session.Tick(); Assert.False(r.Session.Start()); Assert.Equal(1,r.Control.Aborts);
    }

    [Theory]
    [InlineData("session")] [InlineData("revision")] [InlineData("hash")]
    public void Stale_ready_failure_and_completion_never_change_the_current_start(string field)
    {
        using var r=new Rig(); Assert.True(r.Session.Start());
        var id=field switch { "session"=>r.Session.Correlation with { SessionId=Guid.NewGuid() },
            "revision"=>r.Session.Correlation with { Revision=99 },_=>r.Session.Correlation with { SnapshotHash=new string('b',64) } };
        Assert.False(r.Session.Receive(new PlayReady(999,id,PlayProtocol.Known)));
        Assert.False(r.Session.Receive(new PlayFailure(1,id,new(PlayErrorCode.RuntimeFailed,"Stale invented failure"))));
        Assert.False(r.Session.Receive(new PlayStopped(1,id)));
        Assert.Equal(LinkedPlayState.Starting,r.Session.State); Assert.Null(r.Session.Error); Assert.Equal(0,r.Control.Aborts);
        Assert.Equal(3,r.Session.IgnoredMessages); Assert.True(r.Session.Receive(r.Ready()));
        Assert.Equal(LinkedPlayState.Running,r.Session.State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Ready_deadline_is_exact_and_a_late_ready_cannot_revive_it(bool deliverReady)
    {
        using var r=new Rig(); Assert.True(r.Session.Start());
        r.Clock.Advance(TimeSpan.FromSeconds(2)-TimeSpan.FromTicks(1)); r.Session.Tick();
        Assert.Equal(LinkedPlayState.Starting,r.Session.State);
        r.Clock.Advance(TimeSpan.FromTicks(1));
        if(deliverReady) Assert.False(r.Session.Receive(r.Ready())); else r.Session.Tick();
        Assert.Equal(LinkedPlayState.Faulted,r.Session.State); Assert.Contains("timed out",r.Session.Error!.Message);
        Assert.Equal(1,r.Control.Aborts); r.Session.Tick(); Assert.False(r.Session.Receive(r.Ready())); Assert.Equal(1,r.Control.Aborts);
    }

    [Fact]
    public void Running_has_no_hidden_deadline_and_stop_uses_its_own_clock()
    {
        using var r=new Rig(); r.Run(); r.Clock.Advance(TimeSpan.FromDays(10)); r.Session.Tick();
        Assert.Equal(LinkedPlayState.Running,r.Session.State); Assert.True(r.Session.RequestStop());
        r.Clock.Advance(TimeSpan.FromSeconds(1)-TimeSpan.FromTicks(1)); r.Session.Tick();
        Assert.Equal(LinkedPlayState.Stopping,r.Session.State);
        r.Clock.Advance(TimeSpan.FromTicks(1)); r.Session.Tick();
        Assert.Equal(LinkedPlayState.Faulted,r.Session.State); Assert.Contains("stop completion",r.Session.Error!.Message);
        Assert.Equal(1,r.Control.Stops); Assert.Equal(1,r.Control.Aborts);
    }

    [Fact]
    public void Stop_during_start_and_late_ready_never_enter_running_and_exit_completes()
    {
        using var r=new Rig(); Assert.True(r.Session.Start()); Assert.True(r.Session.RequestStop());
        Assert.False(r.Session.Receive(r.Ready())); Assert.Equal(LinkedPlayState.Stopping,r.Session.State);
        Assert.True(r.Session.Exited("cooperative exit")); Assert.Equal(LinkedPlayState.Stopped,r.Session.State);
        Assert.Equal(1,r.Control.Aborts); Assert.False(r.Session.Exited("duplicate")); Assert.False(r.Session.Receive(r.Ready()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unexpected_exit_faults_start_or_run_without_touching_the_snapshot(bool running)
    {
        using var r=new Rig(); var bytes=r.Snapshot.CopyPayload(); var hash=r.Snapshot.Hash;
        if(running) r.Run(); else Assert.True(r.Session.Start());
        Assert.True(r.Session.Exited("invented EOF")); Assert.Equal(LinkedPlayState.Faulted,r.Session.State);
        Assert.Contains("invented EOF",r.Session.Error!.Message); Assert.Equal(1,r.Control.Aborts);
        Assert.Equal(bytes,r.Snapshot.CopyPayload()); Assert.Equal(hash,r.Snapshot.Hash);
    }

    [Theory]
    [InlineData("start")] [InlineData("stop")] [InlineData("abort")]
    public void Control_faults_are_contained_and_cleanup_never_repeats(string operation)
    {
        using var r=new Rig();
        if(operation=="start") r.Control.OnStart=()=>throw new InvalidOperationException("Invented start fault");
        if(operation=="stop") r.Control.OnStop=()=>throw new InvalidOperationException("Invented stop fault");
        if(operation=="abort") r.Control.OnAbort=()=>throw new InvalidOperationException("Invented cleanup fault");
        var started=r.Session.Start();
        if(operation=="start") Assert.False(started);
        else { Assert.True(started);Assert.True(r.Session.Receive(r.Ready()));Assert.True(r.Session.RequestStop()); }
        if(operation=="abort") Assert.True(r.Session.Receive(new PlayStopped(1,r.Session.Correlation)));
        else Assert.Equal(LinkedPlayState.Faulted,r.Session.State);
        r.Session.Dispose(); r.Session.Dispose(); r.Session.Tick();
        Assert.Equal(1,r.Control.Aborts);
        if(operation=="abort") { Assert.Null(r.Session.Error);Assert.Contains("cleanup fault",r.Session.CleanupError!.Message); }
        else { Assert.NotNull(r.Session.Error);Assert.Null(r.Session.CleanupError); }
    }

    [Fact]
    public void Primary_failure_and_owned_cleanup_failure_remain_distinct()
    {
        using var r=new Rig(); r.Control.OnAbort=()=>throw new InvalidOperationException("Invented cleanup");
        r.Run(); Assert.True(r.Session.Receive(new PlayFailure(1,r.Session.Correlation,new(PlayErrorCode.RuntimeFailed,"Invented runtime"))));
        Assert.Equal("Invented runtime",r.Session.Error!.Message); Assert.Contains("Invented cleanup",r.Session.CleanupError!.Message);
        Assert.Equal(LinkedPlayState.Faulted,r.Session.State); Assert.Equal(1,r.Control.Aborts);
    }

    [Fact]
    public void Synchronous_control_callbacks_see_installed_state_and_cannot_double_start_or_stop()
    {
        using var r=new Rig(); r.Control.OnStart=()=>Assert.True(r.Session.Receive(r.Ready()));
        r.Control.OnStop=()=>Assert.True(r.Session.Receive(new PlayStopped(1,r.Session.Correlation)));
        Assert.True(r.Session.Start()); Assert.Equal(LinkedPlayState.Running,r.Session.State);
        Assert.True(r.Session.RequestStop()); Assert.Equal(LinkedPlayState.Stopped,r.Session.State);
        Assert.Equal(1,r.Control.Starts); Assert.Equal(1,r.Control.Stops); Assert.Equal(1,r.Control.Aborts);
    }

    [Theory]
    [InlineData("version")] [InlineData("capability")] [InlineData("early-completion")] [InlineData("unexpected")]
    public void Exact_identity_bad_handshake_or_message_order_faults(string bad)
    {
        using var r=new Rig(); Assert.True(r.Session.Start());
        PlayMessage message=bad switch { "version"=>r.Ready() with { Version=2 },
            "capability"=>r.Ready() with { Supported=PlayCapability.Console },
            "early-completion"=>new PlayStopped(1,r.Session.Correlation),_=>new PlayStop(1,r.Session.Correlation) };
        Assert.True(r.Session.Receive(message)); Assert.Equal(LinkedPlayState.Faulted,r.Session.State);
        Assert.NotNull(r.Session.Error); Assert.Equal(1,r.Control.Aborts);
    }

    [Fact]
    public void Duplicate_ready_and_terminal_messages_are_inert_and_restart_needs_a_fresh_owner()
    {
        using var r=new Rig(); r.Run(); Assert.False(r.Session.Receive(r.Ready()));
        Assert.True(r.Session.Receive(new PlayStopped(1,r.Session.Correlation))); Assert.False(r.Session.Receive(r.Ready()));
        Assert.False(r.Session.Start()); Assert.Equal(1,r.Control.Starts); Assert.Equal(1,r.Control.Aborts);
        var nextControl=new Control(); using var next=new LinkedPlaySession(nextControl,r.Snapshot,Guid.NewGuid(),r.Clock,
            TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(1));
        Assert.NotEqual(r.Session.Correlation.SessionId,next.Correlation.SessionId); Assert.True(next.Start());
        Assert.False(next.Receive(r.Ready())); Assert.Equal(LinkedPlayState.Starting,next.State); Assert.Equal(0,nextControl.Aborts);
    }

    [Fact]
    public void Never_started_stop_or_dispose_has_no_owned_resource_to_abort_and_deadlines_are_required_positive()
    {
        using var r=new Rig(); Assert.True(r.Session.RequestStop()); r.Session.Dispose();
        Assert.Equal(0,r.Control.Starts); Assert.Equal(0,r.Control.Stops); Assert.Equal(0,r.Control.Aborts);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LinkedPlaySession(r.Control,r.Snapshot,Guid.NewGuid(),r.Clock,
            TimeSpan.Zero,TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LinkedPlaySession(r.Control,r.Snapshot,Guid.NewGuid(),r.Clock,
            TimeSpan.FromSeconds(1),TimeSpan.FromTicks(-1)));
    }
}
