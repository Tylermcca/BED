using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
namespace Bed.Editor;

// One private stdio channel to one owned Play process; never targets a desktop window or a different game.
internal class PlayObservation : IDisposable
{
    private readonly object gate=new();
    private SupervisedProcess? child;
    private TaskCompletionSource<JsonObject>? waiting;
    private long sequence,frame;
    private bool expectingLive;
    private string? selectedTarget;
    private string? projectRoot;
    private string? fault;
    private readonly List<string> errors=[];
    internal JsonObject Start(PlayLaunch launch,PlayApprovals approvals,bool dirty,string? liveTarget=null)
    {
        AssetAiAccess.Require(launch.ProjectRoot);
        projectRoot=launch.ProjectRoot;
        if(child is not null)throw new InvalidOperationException("Stop this picture run before starting another.");
        if(dirty)throw new InvalidOperationException("Save edits before starting a picture run.");
        if(approvals.NeedsApproval(launch,false) is {} why)throw new InvalidOperationException(why+". Approve the exact Play command in BED first.");
        if(!File.Exists(launch.Run.Executable))throw new InvalidOperationException("Build the saved game before starting a picture run.");
        if(launch.BuildStamp is {} stamp&&!Bed.ProjectBuildStamp.Current(launch.ProjectRoot,Path.GetRelativePath(launch.ProjectRoot,stamp).Replace('\\','/'),launch.BuildInputs,out var stale))
            throw new InvalidOperationException("Build the saved game first: "+stale);
        selectedTarget=liveTarget;sequence=0;frame=0;fault=null;errors.Clear();waiting=new(TaskCreationOptions.RunContinuationsAsynchronously);
        child=new(launch.Run,512,1500000,Record,Fail,new Dictionary<string,string>
        {
            ["BED_PROJECT"]=launch.ProjectRoot,["BED_PLAY"]="1",["BED_AI_OBSERVE"]="1",["BED_GAME_HEADLESS"]="1",
            ["BED_LIVE_TARGET"]=liveTarget??"",["BED_FRAME_PIPE"]="",["BED_FRAME_TOKEN"]="",["BED_FRAME_VERSION"]=""
        });
        return FinishStart();
    }
    private JsonObject FinishStart()
    {
        try
        {
            if(fault is not null)throw new InvalidOperationException(fault);
            var hello=Receive();
            if(hello["png"] is not null||hello["error"] is not null)throw new InvalidDataException("The game did not confirm that it can share pictures with BED.");
            hello.Remove("png");hello.Remove("error");hello["processId"]=child!.ProcessId;hello["status"]="Started the game without a window. It pauses between requests. This does not prove the game works.";return hello;
        }
        catch{Dispose();throw;}
    }
    private void Record(string text,bool stderr)
    {
        lock(gate)
        {
            if(stderr){if(errors.Count<10)errors.Add(text.Length>1024?text[..1024]:text);return;}
            try
            {
                if(waiting is null||waiting.Task.IsCompleted)throw new InvalidDataException("The game sent a picture reply when BED had not asked for one.");
                var value=JsonNode.Parse(text,new JsonNodeOptions(),new JsonDocumentOptions{MaxDepth=4})?.AsObject()??throw new InvalidDataException("The game did not send a picture reply BED could read.");
                var fields=value.Select(p=>p.Key).ToArray();
                if(fields.Length!=(expectingLive?7:6)||fields.Except(expectingLive?new[]{"id","frame","description","png","error","mode","values"}:new[]{"id","frame","description","png","error","mode"},StringComparer.Ordinal).Any())throw new InvalidDataException("The picture reply contains missing or extra information.");
                if(value["id"]!.GetValue<long>()!=sequence||value["frame"]!.GetValue<long>()<frame||value["frame"]!.GetValue<long>()>int.MaxValue)throw new InvalidDataException("The picture reply is out of date or belongs to a different request.");
                if(value["description"]!.GetValue<string>().Length>2048||value["mode"]!.GetValue<string>()!="headless; simulation pauses between requests")throw new InvalidDataException("The game does not support taking pictures while paused.");
                frame=value["frame"]!.GetValue<long>();waiting.TrySetResult(value);
            }
            catch(Exception e)when(e is JsonException or InvalidOperationException or InvalidDataException or FormatException or ArgumentException or NullReferenceException)
            {Fail("This game cannot share pictures with BED, or its reply could not be read.");}
        }
    }
    private void Fail(string why)
    {lock(gate){fault??=why;waiting?.TrySetException(new InvalidDataException(why));}child?.AbortOwned();}
    private JsonObject Receive()
    {
        var current=waiting!.Task;var completed=Task.WhenAny(current,child!.Completion,Task.Delay(TimeSpan.FromSeconds(10))).GetAwaiter().GetResult();
        AssetAiAccess.Require(projectRoot);
        if(completed!=current)throw new InvalidOperationException("The game BED started closed or did not answer within ten seconds. "+string.Join(" ",errors));
        var result=current.GetAwaiter().GetResult();
        AssetAiAccess.Require(projectRoot);
        return result;
    }
    internal JsonObject Request(string op,string? key=null,double seconds=0)
    {
        if(op is not ("shot" or "hold" or "stop")||op=="hold"&&(key is not ("left" or "right" or "jump" or "restart")||!double.IsFinite(seconds)||seconds<=0||seconds>5)||op!="hold"&&(key is not null||seconds!=0))
            throw new ArgumentException("Take a picture, or hold left/right/jump/restart for more than zero and at most five seconds of play.");
        if(child is null||child.Completion.IsCompleted)throw new InvalidOperationException("Start a picture run first.");
        try
        {
            AssetAiAccess.Require(projectRoot);
            var before=frame;
            lock(gate){sequence++;waiting=new(TaskCreationOptions.RunContinuationsAsynchronously);}
            child.Send(JsonSerializer.Serialize(new{id=sequence,op,key,seconds})).GetAwaiter().GetResult();
            var result=Receive();
            if(result["error"] is not null)return result;
            result.Remove("error");
            if(op=="stop"){Dispose();result.Remove("png");result["status"]="Stopped the game started for this picture run.";AssetAiAccess.Require(projectRoot);return result;}
            var png=result["png"]?.GetValue<string>()??throw new InvalidDataException("The game returned no image.");
            var bytes=ValidatePng(png);result.Remove("png");result["imageData"]=png;
            result["processId"]=child!.ProcessId;result["sha256"]=Convert.ToHexStringLower(SHA256.HashData(bytes));result["requestedSeconds"]=seconds;result["advancedFrames"]=frame-before;result["simulatedSeconds"]=(frame-before)/60.0;
            result["status"]=op=="shot"?"Took a picture without advancing the game. This does not prove the game works.":$"Held {key} for {seconds:0.###} seconds of play; the game reported {frame-before} updates and returned a picture after release. This does not prove the game works.";
            AssetAiAccess.Require(projectRoot);
            return result;
        }
        catch{Dispose();throw;}
    }
    internal LiveValuesSnapshot ReadValues(LiveValueTarget target,string? key=null,double seconds=0,string? field=null,float? value=null)
    {
        if(target.ObjectId!=selectedTarget)throw new ArgumentException("Read only the object selected when live Play started.");
        if(field is not null&&(key is not null||seconds!=0)||key is not null&&!target.Inputs.Contains(key,StringComparer.Ordinal)||!double.IsFinite(seconds)||seconds<0||seconds>.25||field is not null&&(value is null||target.Values.FirstOrDefault(v=>v.Id==field)?.Accepts(value.Value)!=true)||field is null&&value is not null)
            throw new ArgumentException("Use a declared game input or a value inside its trial range.");
        if(child is null||child.Completion.IsCompleted)throw new InvalidOperationException("Start live Play first.");
        try
        {
            AssetAiAccess.Require(projectRoot);
            lock(gate){sequence++;expectingLive=true;waiting=new(TaskCreationOptions.RunContinuationsAsynchronously);}
            child.Send(JsonSerializer.Serialize(new{id=sequence,op=field is null?"live":"tune",key=field??key,seconds=value.HasValue?value.Value:seconds})).GetAwaiter().GetResult();
            var reply=Receive();if(reply["error"] is not null)throw new InvalidDataException(reply["error"]!.GetValue<string>());
            var values=reply["values"]!.AsArray();if(values.Count!=target.Values.Count)throw new InvalidDataException("The game sent a different number of live values than expected.");
            var numbers=values.Select(v=>v!.GetValue<float>()).ToArray();
            if(numbers.Any(n=>!float.IsFinite(n)))throw new InvalidDataException("The game returned a live value that is not a finite number.");
            AssetAiAccess.Require(projectRoot);
            return new(frame,Array.AsReadOnly(numbers));
        }
        catch{Dispose();throw;}
        finally{expectingLive=false;}
    }
    internal static byte[] ValidatePng(string data)
    {
        if(data.Length>1398104)throw new InvalidDataException("The game picture is larger than the allowed size.");
        var bytes=Convert.FromBase64String(data);
        if(bytes.Length<45||bytes.Length>1024*1024||!bytes.AsSpan(bytes.Length-8,4).SequenceEqual("IEND"u8)||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||!bytes.AsSpan(12,4).SequenceEqual("IHDR"u8)||BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8,4))!=13)
            throw new InvalidDataException("The game did not return a readable PNG picture within the size limit.");
        var width=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));var height=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        if(width<=0||height<=0||width>1920||height>1080||(long)width*height>2073600)throw new InvalidDataException("The game picture is too wide or tall for BED to read.");
        return bytes;
    }
    public void Dispose()
    {
        var owned=child;child=null;
        lock(gate){waiting?.TrySetCanceled();waiting=null;}
        if(owned is null)return;
        owned.Dispose();
        // Observe owned exit before reporting stopped or releasing its working folder.
        // Never wait while holding the reply lock: the output readers may still be finishing.
        try{owned.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();}
        catch(TimeoutException error){throw new IOException("The game BED started is still closing.",error);}
    }
}
