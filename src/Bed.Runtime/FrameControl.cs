using System.IO.Pipes;
using Bed.FrameTransport;
namespace Bed.Runtime;

/// <summary>Optional cooperative simulation gate; call once per simulation tick while continuing input/render pumping.</summary>
public sealed class FrameControl : IDisposable
{
    private readonly object gate=new();
    private readonly CancellationTokenSource lifetime=new();
    private NamedPipeClientStream? pipe;
    private bool connected,disposed,paused,granted;
    private long frames;
    private FrameControlWire.Message? pending,stepping;
    private TaskCompletionSource<FrameControlWire.Message>? response;
    private FrameControl() { }
    /// <summary>Connects asynchronously using BED's child-only launch environment; absent/invalid settings are a no-op.</summary>
    public static FrameControl ConnectFromEnvironment()
    {
        var control=new FrameControl();
        var name=Environment.GetEnvironmentVariable("BED_FRAME_PIPE");var token=Environment.GetEnvironmentVariable("BED_FRAME_TOKEN");
        if(Environment.GetEnvironmentVariable("BED_FRAME_VERSION")!="1"||name is null||!name.StartsWith("bed-frame-",StringComparison.Ordinal)||name.Length!=42
            ||name[10..].Any(c=>!char.IsAsciiHexDigit(c))||token is null||token.Length!=64||token.Any(c=>!char.IsAsciiHexDigit(c)))return control;
        _=control.Connect(name,token);return control;
    }
    private async Task Connect(string name,string token)
    {
        try
        {
            pipe=new(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {timeout.CancelAfter(TimeSpan.FromSeconds(5));await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);}
            await FrameControlWire.Write(pipe,new(1,token,0,"hello",0,"running"),lifetime.Token).ConfigureAwait(false);
            lock(gate){if(disposed)return;connected=true;}
            long last=0;
            while(!lifetime.IsCancellationRequested)
            {
                var command=await FrameControlWire.Read(pipe,token,lifetime.Token).ConfigureAwait(false);
                if(command.Id!=last+1||command.Op is not ("pause" or "step" or "resume")||command.State!="request"||command.Frames!=0)throw new InvalidDataException("Invalid frame control command.");
                last=command.Id;
                Task<FrameControlWire.Message> reply;
                lock(gate){pending=command;response=new(TaskCreationOptions.RunContinuationsAsynchronously);reply=response.Task;}
                var ack=await reply.WaitAsync(lifetime.Token).ConfigureAwait(false);
                await FrameControlWire.Write(pipe,ack,lifetime.Token).ConfigureAwait(false);
            }
        }
        catch(Exception e)when(e is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { }
        finally {lock(gate){connected=false;pending=null;stepping=null;response=null;}pipe?.Dispose();}
    }
    /// <summary>Returns one simulation permit. False pauses simulation only. A step is acknowledged on the next call,
    /// after its permitted tick finishes. Disconnect resumes on the next call; never accumulate paused wall time.</summary>
    public bool PollFrame()
    {
        lock(gate)
        {
            if(granted&&frames<long.MaxValue)frames++;
            granted=false;
            if(disposed||!connected){paused=false;return granted=true;}
            if(stepping is {} step){stepping=null;paused=true;Reply(step);}
            if(pending is {} command)
            {
                pending=null;
                if(command.Op=="pause"){paused=true;Reply(command);}
                else if(command.Op=="resume"){paused=false;Reply(command);}
                else if(paused){stepping=command;return granted=true;}
                else { connected=false;pipe?.Dispose();return granted=true; }
            }
            return granted=!paused;
        }
    }
    private void Reply(FrameControlWire.Message command)=>response?.TrySetResult(new(1,command.Token,command.Id,"ack",frames,paused?"paused":"running"));
    /// <summary>Disconnects this helper and releases its gate; never affects another process.</summary>
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;connected=false;pending=null;stepping=null;}
        lifetime.Cancel();pipe?.Dispose();
    }
}
