using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Text.Json;

namespace Brutal.Authoring.Editor.App;

/// <summary>One bounded decoded game record: either a protocol reply or a structured console entry.</summary>
public sealed record GameProcessRecord(PlayCorrelation Correlation,PlayMessage? Message,PlayLogSeverity? Severity,
    string? Text,PlaySourceLink? Source);

/// <summary>Version-one UTF-8 JSON-lines framing for the declared linked-game stdin/stdout channel.
/// No shell, executable discovery, file access or process launch occurs here.</summary>
public static class PlayProcessWire
{
    /// <summary>Encodes one Hello plus exact immutable snapshot in one line, refusing oversized input before cloning.</summary>
    public static string Start(PlayHello hello,PlaySnapshot snapshot,int maxInputBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if(maxInputBytes<=0) throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        if(PlayProtocol.Validate(hello) is { } error) throw new ArgumentException(error.Message,nameof(hello));
        if(hello.Correlation!=snapshot.Correlate(hello.Correlation.SessionId))
            throw new ArgumentException("The Hello does not identify these exact Play inputs.",nameof(hello));
        if(snapshot.Length>maxInputBytes) throw new ArgumentException("The Play snapshot exceeds this host's input budget.",nameof(snapshot));
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); Header(writer,"hello",hello.Version,hello.Correlation);
            writer.WriteNumber("offered"u8,(int)hello.Offered);writer.WriteNumber("required"u8,(int)hello.Required);
            writer.WriteString("document"u8,snapshot.DocumentId);writer.WriteString("choice"u8,snapshot.Choice.ToString());
            writer.WriteString("format"u8,snapshot.Format);writer.WriteBase64String("payload"u8,snapshot.CopyPayload());writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Encodes a correlated cooperative stop, with no command text or process identifier.</summary>
    public static string Stop(PlayStop stop)
    {
        if(PlayProtocol.Validate(stop) is { } error) throw new ArgumentException(error.Message,nameof(stop));
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)) {writer.WriteStartObject();Header(writer,"stop",stop.Version,stop.Correlation);writer.WriteEndObject();}
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool Refuse(out PlayProtocolError? error,string why)
    { error=new(PlayErrorCode.InvalidMessage,why);return false; }

    private static void Header(Utf8JsonWriter writer,string kind,int version,PlayCorrelation id)
    {
        writer.WriteString("kind"u8,kind);writer.WriteNumber("version"u8,version);
        writer.WriteString("session"u8,id.SessionId.ToString("N"));writer.WriteNumber("revision"u8,id.Revision);writer.WriteString("hash"u8,id.SnapshotHash);
    }

    /// <summary>Decodes a bounded peer line with a shallow strict JSON object and no duplicate fields.
    /// Only Ready, Stopped, Failure and structured Log records are accepted; unknown versions remain refused.</summary>
    public static bool TryRead(string text,int maxCharacters,out GameProcessRecord? record,out PlayProtocolError? error,PlayCorrelation? expected=null)
    {
        record=null;error=null;
        if(maxCharacters<=0) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        if(text is null || text.Length>maxCharacters) return Refuse(out error,"The linked Play output line exceeds its budget or is missing.");
        try
        {
            using var document=JsonDocument.Parse(text,new JsonDocumentOptions {MaxDepth=8});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object) return Refuse(out error,"The linked Play output must be a JSON object.");
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in root.EnumerateObject()) if(!names.Add(property.Name)) return Refuse(out error,"The linked Play output repeats a field.");
            var kind=root.GetProperty("kind"u8).GetString();
            var version=root.GetProperty("version"u8).GetInt32();
            var session=root.GetProperty("session"u8).GetString();
            if(!Guid.TryParseExact(session,"N",out var sessionId)) return Refuse(out error,"The linked Play session identity is invalid.");
            var id=new PlayCorrelation(sessionId,root.GetProperty("revision"u8).GetInt64(),root.GetProperty("hash"u8).GetString()!);
            if(expected is not null && id!=expected)
            {
                error=new(id.SessionId!=expected.SessionId ? PlayErrorCode.SessionMismatch
                    : id.Revision!=expected.Revision ? PlayErrorCode.RevisionMismatch : PlayErrorCode.SnapshotMismatch,
                    "This output belongs to another linked Play input.");
                return false;
            }
            if(PlayProtocol.Validate(new PlayStop(version,id)) is { } invalid) {error=invalid;return false;}
            PlayMessage? message=null;PlayLogSeverity? severity=null;string? detail=null;PlaySourceLink? source=null;
            switch(kind)
            {
                case "ready":message=new PlayReady(version,id,(PlayCapability)root.GetProperty("supported"u8).GetInt32());break;
                case "stopped":message=new PlayStopped(version,id);break;
                case "failure":
                    if(!Enum.TryParse<PlayErrorCode>(root.GetProperty("code"u8).GetString(),false,out var code) || !Enum.IsDefined(code))
                        return Refuse(out error,"The linked Play failure code is invalid.");
                    message=new PlayFailure(version,id,new(code,root.GetProperty("message"u8).GetString()!));break;
                case "log":
                    if(!Enum.TryParse<PlayLogSeverity>(root.GetProperty("severity"u8).GetString(),false,out var level) || !Enum.IsDefined(level))
                        return Refuse(out error,"The linked Play log severity is invalid.");
                    severity=level;detail=root.GetProperty("message"u8).GetString();
                    if(detail is null) return Refuse(out error,"The linked Play log text is missing.");
                    if(root.TryGetProperty("document"u8,out var file))
                    {
                        var name=file.GetString();
                        if(name is null) return Refuse(out error,"The linked Play source identity is missing.");
                        source=new(name,root.TryGetProperty("object"u8,out var target) ? target.GetString() : null,
                            root.TryGetProperty("line"u8,out var line) ? line.GetInt32() : null);
                    }
                    break;
                default:return Refuse(out error,"The linked Play output kind is unsupported.");
            }
            if(message is not null && PlayProtocol.Validate(message) is { } bad) {error=bad;return false;}
            record=new(id,message,severity,detail,source);return true;
        }
        catch(Exception exception) when(exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {return Refuse(out error,"The linked Play output is not a valid protocol record.");}
    }
}


/// <summary>One Windows-only owned child with suspended-start Job assignment and bounded stdio.
/// Background IO queues records only; Pump dispatches them on the caller's owner thread.
/// The caller's LinkedPlaySession supplies cooperative stop/deadlines and invokes AbortOwned when ending.</summary>
public sealed class GameProcessHost : ILinkedPlayControl,IDisposable
{
    private readonly GameLaunchSpec spec;
    private readonly int maxInputBytes,maxLineCharacters,maxPending;
    private readonly PlayLogBuffer? logs;
    private readonly object gate=new();
    private readonly Queue<GameProcessRecord> pending=[];
    private SupervisedProcess? process;
    private PlayHello? hello;
    private bool aborted,started,rootExited,stopSent;
    private string? pendingFault,exitReason;
    private bool exitPending;
    private Task<int>? completion;
    /// <summary>Creates no resources; retains the declared protocol budgets.</summary>
    public GameProcessHost(GameLaunchSpec spec,int maxInputBytes,int maxLineCharacters,int maxPendingRecords,PlayLogBuffer? logs=null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if(maxInputBytes<=0||maxLineCharacters<=0||maxPendingRecords<=0)throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        this.spec=spec;this.maxInputBytes=maxInputBytes;this.maxLineCharacters=maxLineCharacters;maxPending=maxPendingRecords;this.logs=logs;
    }
    /// <summary>Actual owned-child exit, not readiness.</summary>
    public Task<int>? Completion=>completion;
    /// <summary>Starts the declared linked child with the shared suspended Job supervisor.</summary>
    public void Start(PlayHello offer,PlaySnapshot snapshot)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Play in a separate process needs Windows for now");
        lock(gate){if(started||aborted)throw new InvalidOperationException("This process host is single-use.");if(logs is not null&&logs.Correlation!=offer.Correlation)throw new ArgumentException("The Console belongs to another Play start.");started=true;hello=offer;}
        var start=PlayProcessWire.Start(offer,snapshot,maxInputBytes);
        try
        {
            // Wire adds bounded JSON/base64 overhead to the already bounded snapshot.
            process=new(spec,checked(maxInputBytes*2+4096),maxLineCharacters,Record,Fail);
            completion=Watch(process.Completion);_=process.Send(start);
        }
        catch{AbortOwned();throw;}
    }
    /// <summary>Sends at most one correlated cooperative stop.</summary>
    public void RequestStop(PlayStop stop)
    {
        if(hello is null||stop.Correlation!=hello.Correlation)throw new ArgumentException("Stop belongs to another Play start.",nameof(stop));
        var line=PlayProcessWire.Stop(stop);lock(gate){if(aborted||rootExited||stopSent)return;stopSent=true;}
        if(process is not null)_=process.Send(line);
    }
    private void Record(string line,bool stderr)
    {
        var id=hello!.Correlation;
        GameProcessRecord? record;
        if(stderr) record=new(id,null,PlayLogSeverity.Error,line,null);
        else if(!PlayProcessWire.TryRead(line,maxLineCharacters,out record,out var invalid,id))
        {
            if(invalid!.Code is not (PlayErrorCode.SessionMismatch or PlayErrorCode.RevisionMismatch or PlayErrorCode.SnapshotMismatch)) Fail(invalid.Message);
            return;
        }
        lock(gate)
        {
            if(aborted) return;
            if(pending.Count>=maxPending) {pendingFault="The linked Play output queue exceeded its budget.";return;}
            pending.Enqueue(record!);
        }
    }
    private void Fail(string why) {lock(gate) {if(!aborted && !rootExited) pendingFault??=why;}}
    /// <summary>Dispatches queued records only to this exact linked owner and optional pinned log buffer.
    /// Callback execution and session deadlines remain on the caller's owner thread, never an IO thread.</summary>
    public void Pump(LinkedPlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if(hello is null || session.Correlation!=hello.Correlation) throw new ArgumentException("The process belongs to another linked session.",nameof(session));
        GameProcessRecord[] records;string? fault,exit;
        lock(gate)
        {
            records=pending.ToArray();pending.Clear();fault=pendingFault;pendingFault=null;
            exit=exitPending ? exitReason : null;exitPending=false;
        }
        foreach(var record in records)
        {
            if(record.Message is { } message) session.Receive(message);
            else if(record.Severity is { } severity) logs?.Add(record.Correlation,severity,record.Text!,record.Source);
        }
        if(fault is not null) session.Receive(new PlayFailure(1,hello.Correlation,new(PlayErrorCode.RuntimeFailed,fault)));
        if(exit is not null) session.Exited(exit);
    }
    private async Task<int> Watch(Task<int> task)
    {var code=await task.ConfigureAwait(false);lock(gate){rootExited=true;exitReason="Owned Play process exited with code "+code+".";exitPending=true;}return code;}
    /// <summary>Terminates only the owned Job. Repetition is inert.</summary>
    public void AbortOwned(){lock(gate){if(aborted)return;aborted=true;}process?.AbortOwned();}
    /// <summary>Releases owned child resources.</summary>
    public void Dispose()=>AbortOwned();
}
