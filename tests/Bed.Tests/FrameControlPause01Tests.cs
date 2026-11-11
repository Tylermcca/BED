using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.FrameTransport;
using Bed.Runtime;
using Xunit;
namespace Bed.Tests;

public sealed class FrameControlPause01Tests
{
    private static void Until(Func<bool> done,Action? pump=null)
    {var time=Stopwatch.StartNew();while(time.Elapsed<TimeSpan.FromSeconds(4)){pump?.Invoke();if(done())return;Thread.Sleep(5);}Assert.True(done(),"Timed out waiting for frame-control evidence.");}
    private static FieldInfo Field(string name)=>typeof(FrameControl).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!;
    [Fact]
    public void Gate_applies_pause_at_boundary_and_step_acknowledges_only_after_one_completed_tick()
    {
        using var control=(FrameControl)Activator.CreateInstance(typeof(FrameControl),true)!;
        Assert.True(control.PollFrame());
        Field("connected").SetValue(control,true);
        var message=typeof(FrameControl).Assembly.GetType("Bed.FrameTransport.FrameControlWire+Message")!;
        object Command(long id,string op)=>Activator.CreateInstance(message,[1,new string('a',64),id,op,0L,"request"])!;
        Field("pending").SetValue(control,Command(1,"pause"));
        Assert.False(control.PollFrame());Assert.False(control.PollFrame());
        Assert.Equal(1L,Field("frames").GetValue(control));
        var tcs=Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(message),TaskCreationOptions.RunContinuationsAsynchronously)!;
        Field("response").SetValue(control,tcs);var task=(Task)tcs.GetType().GetProperty("Task")!.GetValue(tcs)!;
        Field("pending").SetValue(control,Command(2,"step"));
        Assert.True(control.PollFrame());Assert.False(task.IsCompleted);Assert.False(control.PollFrame());Assert.True(task.IsCompleted);Assert.Equal(2L,Field("frames").GetValue(control));Assert.False(control.PollFrame());
        Field("pending").SetValue(control,Command(3,"resume"));Assert.True(control.PollFrame());
        Field("connected").SetValue(control,false);Assert.True(control.PollFrame());control.Dispose();Assert.True(control.PollFrame());
    }
    [Theory]
    [InlineData("token")][InlineData("version")][InlineData("duplicate")][InlineData("oversize")][InlineData("stale")][InlineData("peer")][InlineData("malformed")]
    public async Task Wrong_peer_or_invalid_wire_never_enables_controls(string mode)
    {
        using var server=new ProjectFrameServer();var environment=new Dictionary<string,string>();server.AddEnvironment(environment);
        server.Start(mode=="peer"?Environment.ProcessId+1:Environment.ProcessId);
        using var client=new NamedPipeClientStream(".",server.Name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));await client.ConnectAsync(deadline.Token);
        try
        {
            if(mode is "duplicate" or "oversize" or "malformed")
            {
                var json=mode=="duplicate"?"{\"Version\":1,\"Version\":1,\"Token\":\"a\",\"Id\":0,\"Op\":\"hello\",\"Frames\":0,\"State\":\"running\"}":"{";
                var bytes=Encoding.UTF8.GetBytes(json);var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,mode=="oversize"?8193:bytes.Length);
                await client.WriteAsync(header,deadline.Token);if(mode!="oversize")await client.WriteAsync(bytes,deadline.Token);await client.FlushAsync(deadline.Token);
            }
            else
            {
                await FrameControlWire.Write(client,new(mode=="version"?2:1,mode=="token"?new string('0',64):environment["BED_FRAME_TOKEN"],mode=="stale"?1:0,"hello",0,"running"),deadline.Token);
            }
            var probe=new byte[1];Assert.Equal(0,await client.ReadAsync(probe,deadline.Token));
        }
        catch(IOException) { } // The rejected owned server may close before the client's write.
        Until(()=>!server.Snapshot.Available&&server.Snapshot.Status.Contains("unknown"));Assert.False(server.Request(ProjectFrameCommand.Pause));
    }
    [Fact]
    public void Real_owned_route_process_pauses_steps_once_resumes_and_preserves_saved_input()
    {
        var root=Directory.CreateTempSubdirectory("frame-route-").FullName;
        var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
        var source=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"FrameRoute","data","routes.json"));File.WriteAllBytes(Path.Combine(data,"routes.json"),source);
        var log=Path.Combine(root,"ticks.log");
        var launch=new PlayLaunch(new(Path.Combine(AppContext.BaseDirectory,"FrameRouteGame","RoutePuzzle.Game.exe"),root,["--project",root,"--seconds","20","--tick-log",log,"--route","brook,summit"]),null,root,[]);
        using var session=new ProjectPlayBridge().Start(launch);
        var controls=Assert.IsAssignableFrom<IProjectFrameControlSession>(session);
        try
        {
            Assert.True(session.Start(false),session.Status);Until(()=>controls.FrameControl.Available,session.Update);
            long Count()
            {
                if(!File.Exists(log))return 0;
                using var stream=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var reader=new StreamReader(stream);
                return reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(l=>l.Contains('|')).Select(l=>long.Parse(l.Split('|')[0])).DefaultIfEmpty().Last();
            }
            Assert.True(controls.RequestFrameControl(ProjectFrameCommand.Pause));Assert.False(controls.RequestFrameControl(ProjectFrameCommand.Step));
            Until(()=>controls.FrameControl.Paused,session.Update);var paused=Count();Assert.Equal(controls.FrameControl.CompletedFrames,paused);Thread.Sleep(80);Assert.Equal(paused,Count());
            for(var i=1;i<=2;i++)
            {
                Assert.True(controls.RequestFrameControl(ProjectFrameCommand.Step));Assert.False(controls.RequestFrameControl(ProjectFrameCommand.Step));Until(()=>controls.FrameControl.Paused,session.Update);Assert.Equal(paused+i,Count());
            }
            Assert.True(controls.RequestFrameControl(ProjectFrameCommand.Resume));Until(()=>!controls.FrameControl.Pending,session.Update);Until(()=>Count()>paused+2,session.Update);
            Assert.Equal(source,File.ReadAllBytes(Path.Combine(data,"routes.json")));
            Assert.False(session.HasCompleted);session.RequestStop();Until(()=>session.HasCompleted,session.Update);
            Assert.False(controls.FrameControl.Available);Assert.False(controls.FrameControl.Paused);
        }
        finally{session.Dispose();Until(()=>session.HasCompleted);Directory.Delete(root,true);}
    }
    [Fact]
    public void Lost_real_pipe_releases_a_paused_game_without_stopping_its_process()
    {
        var root=Directory.CreateTempSubdirectory("frame-loss-").FullName;
        Directory.CreateDirectory(Path.Combine(root,"data"));File.Copy(Path.Combine(AppContext.BaseDirectory,"FrameRoute","data","routes.json"),Path.Combine(root,"data","routes.json"));var log=Path.Combine(root,"ticks.log");
        using var server=new ProjectFrameServer();var environment=new Dictionary<string,string>();server.AddEnvironment(environment);
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"FrameRouteGame","RoutePuzzle.Game.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var item in environment)start.Environment[item.Key]=item.Value;
        foreach(var argument in new[]{"--project",root,"--seconds","3","--tick-log",log})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;process.StandardInput.Close();server.Start(process.Id);
        try
        {
            long Count(){if(!File.Exists(log))return 0;using var stream=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var reader=new StreamReader(stream);return reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Length;}
            Until(()=>server.Snapshot.Available);Assert.True(server.Request(ProjectFrameCommand.Pause));Until(()=>server.Snapshot.Paused);var count=Count();Thread.Sleep(60);Assert.Equal(count,Count());
            server.Dispose();Until(()=>Count()>count);Assert.False(process.HasExited);Assert.False(server.Snapshot.Paused);Assert.Contains("unknown",server.Snapshot.Status);
            Assert.True(process.WaitForExit(4000));Assert.Equal(0,process.ExitCode);
        }
        finally{if(!process.HasExited){process.Kill(true);process.WaitForExit(3000);}Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Authenticated_peer_timeout_and_duplicate_request_disable_control_without_a_process_action()
    {
        using var server=new ProjectFrameServer();var environment=new Dictionary<string,string>();server.AddEnvironment(environment);server.Start(Environment.ProcessId);
        using var client=new NamedPipeClientStream(".",server.Name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));await client.ConnectAsync(deadline.Token);
        await FrameControlWire.Write(client,new(1,environment["BED_FRAME_TOKEN"],0,"hello",0,"running"),deadline.Token);Until(()=>server.Snapshot.Available);
        Assert.True(server.Request(ProjectFrameCommand.Pause));Assert.False(server.Request(ProjectFrameCommand.Pause));
        var command=await FrameControlWire.Read(client,environment["BED_FRAME_TOKEN"],deadline.Token);
        await FrameControlWire.Write(client,new(1,command.Token,command.Id,"ack",7,"paused"),deadline.Token);Until(()=>server.Snapshot.Paused);
        Assert.True(server.Request(ProjectFrameCommand.Step));typeof(ProjectFrameServer).GetField("sentAt",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(server,DateTime.UtcNow-TimeSpan.FromSeconds(6));server.Update();
        Assert.False(server.Snapshot.Available);Assert.False(server.Snapshot.Paused);Assert.Contains("timed out",server.Snapshot.Status);Assert.False(server.Request(ProjectFrameCommand.Resume));
    }

}
