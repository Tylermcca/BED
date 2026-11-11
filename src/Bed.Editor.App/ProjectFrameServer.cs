using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Bed.Editor.ImGui;
using Bed.FrameTransport;
namespace Bed.Editor.App;

// MIXED: owned Play child + BCL local pipes; authenticated frame-boundary protocol is local.
internal sealed class ProjectFrameServer : IDisposable
{
    private readonly object gate=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly NamedPipeServerStream pipe;
    private readonly Channel<FrameControlWire.Message> commands=Channel.CreateBounded<FrameControlWire.Message>(1);
    private readonly string token=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private bool connected,paused,pending,disposed,terminated,started;
    private readonly TimeProvider clock;
    internal Task Completion {get;private set;}=Task.CompletedTask;
    private long sequence,frames;
    private DateTime sentAt;
    private string status="This game hasn't enabled frame control yet.";
    internal string Name {get;}="bed-frame-"+Guid.NewGuid().ToString("N");
    internal ProjectFrameServer(TimeProvider? clock=null)
    {this.clock=clock??TimeProvider.System;pipe=new(Name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,8192,8192);}
    internal void AddEnvironment(Dictionary<string,string> environment)
    {environment["BED_FRAME_PIPE"]=Name;environment["BED_FRAME_TOKEN"]=token;environment["BED_FRAME_VERSION"]="1";}
    internal ProjectFrameState Snapshot {get{lock(gate)return new(connected&&!disposed,connected&&!pending&&paused,pending,frames,status);}}
    internal void Start(int ownedProcessId)
    {lock(gate){if(started||disposed||terminated)return;started=true;Completion=Run(ownedProcessId);}}
    private async Task Run(int ownedProcessId)
    {
        try
        {
            // Initialization can take arbitrarily long. The owned process lifetime closes this wait.
            await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
            if(!OperatingSystem.IsWindows()||!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out var peer)||peer!=ownedProcessId)throw new InvalidDataException("Frame control peer is not the owned game process.");
            var hello=await FrameControlWire.Read(pipe,token,lifetime.Token).ConfigureAwait(false);
            if(hello.Id!=0||hello.Op!="hello"||hello.Frames!=0||hello.State!="running")throw new InvalidDataException("Invalid frame control hello.");
            lock(gate){if(disposed)return;connected=true;status="Simulation running.";}
            var writer=WriteCommands();
            try
            {
                while(!lifetime.IsCancellationRequested)
                {
                    var ack=await FrameControlWire.Read(pipe,token,lifetime.Token).ConfigureAwait(false);
                    lock(gate)
                    {
                        var expected=lastCommand=="resume"?"running":"paused";
                        if(!pending||ack.Id!=sequence||ack.Op!="ack"||ack.State!=expected||ack.Frames<frames||lastCommand=="step"&&ack.Frames!=frames+1)
                            throw new InvalidDataException("Stale or invalid frame control acknowledgement.");
                        frames=ack.Frames;paused=ack.State=="paused";pending=false;
                        status=paused?$"Simulation paused after {frames} ticks.":"Simulation running.";
                    }
                }
            }
            finally {lifetime.Cancel();try{await writer.ConfigureAwait(false);}catch(Exception e)when(e is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException) { }}
        }
        catch(Exception e)when(e is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {Disconnect("Frame control unavailable or disconnected; simulation state is unknown.");}
        finally {pipe.Dispose();}
    }
    private async Task WriteCommands()
    {
        try
        {
            await foreach(var message in commands.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
                await FrameControlWire.Write(pipe,message,lifetime.Token).ConfigureAwait(false);
        }
        catch(Exception e)when(e is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {Disconnect("Frame control disconnected; simulation state is unknown.");}
    }
    private string lastCommand="";
    internal bool Request(ProjectFrameCommand command)
    {
        lock(gate)
        {
            if(disposed||!connected||pending||!Enum.IsDefined(command)||command==ProjectFrameCommand.Pause&&paused||command is ProjectFrameCommand.Step or ProjectFrameCommand.Resume&&!paused)return false;
            lastCommand=command switch{ProjectFrameCommand.Pause=>"pause",ProjectFrameCommand.Step=>"step",_=>"resume"};
            sequence++;pending=true;sentAt=clock.GetUtcNow().UtcDateTime;status="Waiting for the next simulation boundary.";
            if(commands.Writer.TryWrite(new(1,token,sequence,lastCommand,0,"request")))return true;
            Disconnect("Frame control command queue unavailable; simulation state is unknown.");return false;
        }
    }
    internal void Update()
    {lock(gate){if(pending&&clock.GetUtcNow().UtcDateTime-sentAt>TimeSpan.FromSeconds(5))Disconnect("Frame control acknowledgement timed out; simulation state is unknown.");}}
    private void Disconnect(string why)
    {
        lock(gate){if(terminated)return;terminated=true;connected=false;pending=false;paused=false;status=why;}
        lifetime.Cancel();pipe.Dispose();
    }
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;}
        Disconnect("Frame control closed; simulation state is unknown.");
    }
    // Verified against Microsoft's winbase.h contract: HANDLE -> BOOL, out ULONG process ID.
    [DllImport("kernel32.dll",SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint clientProcessId);
}
