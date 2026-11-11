using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.FrameTransport;
using Xunit;
namespace Bed.Tests;

public sealed class LateFrameHelloPause02Tests
{
    private sealed class Clock:TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(int seconds)=>now=now.AddSeconds(seconds);
    }
    private static async Task Until(Func<bool> done,Action? pump=null)
    {
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed<TimeSpan.FromSeconds(5)){pump?.Invoke();if(done())return;await Task.Delay(5);}
        Assert.True(done(),"Frame-control condition did not complete.");
    }
    private static NamedPipeClientStream Client(ProjectFrameServer server)=>new(".",server.Name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Late_hello_after_six_seconds_enables_controls_but_ack_deadline_remains(bool connectBeforeDelay)
    {
        var clock=new Clock();using var server=new ProjectFrameServer(clock);var env=new Dictionary<string,string>();server.AddEnvironment(env);server.Start(Environment.ProcessId);
        using var client=Client(server);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if(connectBeforeDelay)await client.ConnectAsync(timeout.Token);
        clock.Advance(6);server.Update();
        Assert.False(server.Completion.IsCompleted);Assert.False(server.Snapshot.Available);Assert.False(server.Snapshot.Pending);
        foreach(var command in Enum.GetValues<ProjectFrameCommand>())
        {Assert.False(server.Request(command));Assert.Equal("This game hasn't enabled frame control yet.",server.Snapshot.Status);}
        if(!connectBeforeDelay)await client.ConnectAsync(timeout.Token);
        await FrameControlWire.Write(client,new(1,env["BED_FRAME_TOKEN"],0,"hello",0,"running"),timeout.Token);
        await Until(()=>server.Snapshot.Available);Assert.Equal("Simulation running.",server.Snapshot.Status);
        Assert.True(server.Request(ProjectFrameCommand.Pause));
        var request=await FrameControlWire.Read(client,env["BED_FRAME_TOKEN"],timeout.Token);Assert.Equal("pause",request.Op);
        clock.Advance(5);server.Update();Assert.True(server.Snapshot.Available);Assert.True(server.Snapshot.Pending);
        clock.Advance(1);server.Update();Assert.False(server.Snapshot.Available);Assert.Contains("acknowledgement timed out",server.Snapshot.Status);
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact]
    public async Task Disconnect_is_terminal_and_start_cannot_reopen_the_pipe()
    {
        using var server=new ProjectFrameServer();var env=new Dictionary<string,string>();server.AddEnvironment(env);server.Start(Environment.ProcessId);
        using(var client=Client(server))
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await client.ConnectAsync(timeout.Token);
            await FrameControlWire.Write(client,new(1,env["BED_FRAME_TOKEN"],0,"hello",0,"running"),timeout.Token);await Until(()=>server.Snapshot.Available);
        }
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));Assert.False(server.Snapshot.Available);
        server.Start(Environment.ProcessId);using var retry=Client(server);using var deadline=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>retry.ConnectAsync(deadline.Token));Assert.False(server.Snapshot.Available);
    }
    [Fact]
    public async Task Owned_process_exit_before_hello_closes_the_wait_cleanly()
    {
        var root=Directory.CreateTempSubdirectory("late-frame-exit-").FullName;
        try
        {
            var helper=Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame.exe");
            using var host=new ProjectPlayHost(new(new(helper,root,["ok"]),null,root,[]),TimeProvider.System,TimeSpan.FromSeconds(10));
            Assert.True(host.Start(false),host.Status);
            var server=Assert.IsType<ProjectFrameServer>(typeof(ProjectPlayHost).GetField("frameControl",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host));
            Assert.False(host.FrameControl.Available);await Until(()=>host.ExitCode is not null,host.Update);
            Assert.Equal(0,host.ExitCode);Assert.Equal(ProjectPlayState.Stopped,host.State);
            await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));Assert.False(server.Snapshot.Available);
            foreach(var command in Enum.GetValues<ProjectFrameCommand>())Assert.False(host.RequestFrameControl(command));
        }
        finally{Directory.Delete(root,true);}
    }
}
