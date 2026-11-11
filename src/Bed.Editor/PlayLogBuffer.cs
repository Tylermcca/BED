namespace Bed.Editor;

/// <summary>Structured linked-game console severity; no colour is required to read it.</summary>
public enum PlayLogSeverity
{
    /// <summary>Detailed diagnostics.</summary>
    Trace,
    /// <summary>Ordinary information.</summary>
    Info,
    /// <summary>A recoverable problem.</summary>
    Warning,
    /// <summary>A failure.</summary>
    Error,
}

/// <summary>A logical admitted document target, never an executable URL or filesystem-opening instruction.
/// ObjectId and Line are optional game-owned selection hints; the host must revalidate before navigation.</summary>
public sealed record PlaySourceLink(string DocumentId,string? ObjectId=null,int? Line=null);

/// <summary>One retained owned log record. Sequence is local arrival order; Correlation is the buffer's captured
/// identity, not a reference to an untrusted incoming identity. SourceOmitted says a link was refused safely.</summary>
public sealed record PlayConsoleRecord(long Sequence,PlayCorrelation Correlation,PlayLogSeverity Severity,string Message,
    PlaySourceLink? Source,bool SourceOmitted);

/// <summary>Owner-thread console buffer for one exact session/input, with caller-supplied count and UTF-16 size
/// budgets. It truncates bounded message text, evicts oldest records, counts loss and freezes allowed logical source
/// identities. It performs no file access, transport, process operation, parsing of commands or editor mutation.</summary>
public sealed class PlayLogBuffer
{
    private readonly Queue<PlayConsoleRecord> records=[];
    private readonly HashSet<string> documents;
    private long sequence;
    /// <summary>Creates explicit positive budgets; one message's limit must fit the total character budget.
    /// Allowed documents are a captured host-owned admission list, using canonical relative forward-slash identities.</summary>
    public PlayLogBuffer(PlayCorrelation correlation,int maxRecords,int maxMessageCharacters,int maxStoredCharacters,
        IEnumerable<string>? allowedDocuments=null)
    {
        if(PlayProtocol.Validate(PlayProtocol.Hello(correlation)) is { } error) throw new ArgumentException(error.Message,nameof(correlation));
        if(maxRecords<=0) throw new ArgumentOutOfRangeException(nameof(maxRecords));
        if(maxMessageCharacters<=0) throw new ArgumentOutOfRangeException(nameof(maxMessageCharacters));
        if(maxStoredCharacters<maxMessageCharacters) throw new ArgumentOutOfRangeException(nameof(maxStoredCharacters));
        Correlation=correlation; MaxRecords=maxRecords; MaxMessageCharacters=maxMessageCharacters; MaxStoredCharacters=maxStoredCharacters;
        documents=new(StringComparer.Ordinal);
        foreach(var document in allowedDocuments ?? [])
        {
            if(!SafeDocument(document)) throw new ArgumentException("Console sources need admitted relative document identities.",nameof(allowedDocuments));
            documents.Add(document);
        }
    }

    /// <summary>Only this exact session/revision/hash can contribute records.</summary>
    public PlayCorrelation Correlation { get; }
    /// <summary>Maximum retained record count.</summary>
    public int MaxRecords { get; }
    /// <summary>Maximum retained UTF-16 message code units per record.</summary>
    public int MaxMessageCharacters { get; }
    /// <summary>Maximum retained characters across messages and source metadata.</summary>
    public int MaxStoredCharacters { get; }
    /// <summary>Current record count.</summary>
    public int Count=>records.Count;
    /// <summary>Retained message plus document/object-link characters.</summary>
    public int StoredCharacters { get; private set; }
    /// <summary>Saturating count of wholly rejected or evicted records; a truncated retained record is counted separately.</summary>
    public long DroppedCount { get; private set; }
    /// <summary>Saturating count of messages whose text exceeded the per-message budget.</summary>
    public long TruncatedCount { get; private set; }
    /// <summary>Saturating count of source links refused or omitted to keep the total size budget.</summary>
    public long OmittedSourceCount { get; private set; }

    /// <summary>Adds one decoded record only for the pinned identity. Control characters other than line breaks/tab
    /// become spaces; clipped UTF-16 never splits a valid surrogate pair. Invalid links are omitted, not followed.
    /// Arrival never changes the session identity or resurrects a linked game.</summary>
    public bool Add(PlayCorrelation correlation,PlayLogSeverity severity,string message,PlaySourceLink? source=null)
    {
        if(correlation!=Correlation || !Enum.IsDefined(severity) || message is null || sequence==long.MaxValue) return Drop();
        var length=Math.Min(message.Length,MaxMessageCharacters);
        if(length>0 && length<message.Length && char.IsHighSurrogate(message[length-1]) && char.IsLowSurrogate(message[length])) length--;
        var characters=new char[length];
        for(var i=0;i<length;i++)
        {
            var c=message[i];
            var unpaired=char.IsHighSurrogate(c) ? i+1>=length || !char.IsLowSurrogate(message[i+1])
                : char.IsLowSurrogate(c) && (i==0 || !char.IsHighSurrogate(message[i-1]));
            characters[i]=unpaired ? '?' : char.IsControl(c) && c is not ('\n' or '\r' or '\t') ? ' ' : c;
        }
        var text=new string(characters);
        if(length<message.Length) TruncatedCount=Increment(TruncatedCount);
        var link=Admit(source); var omitted=source is not null && link is null;
        if(link is not null && (long)text.Length+LinkSize(link)>MaxStoredCharacters) { link=null; omitted=true; }
        if(omitted) OmittedSourceCount=Increment(OmittedSourceCount);
        var record=new PlayConsoleRecord(++sequence,Correlation,severity,text,link,omitted);
        // Evict before addition so even the accounting cannot overflow with caller-supplied int budgets.
        var cost=text.Length+LinkSize(link);
        while(records.Count>0 && (records.Count>=MaxRecords || (long)StoredCharacters+cost>MaxStoredCharacters))
        {
            var old=records.Dequeue(); StoredCharacters-=old.Message.Length+LinkSize(old.Source); DroppedCount=Increment(DroppedCount);
        }
        records.Enqueue(record); StoredCharacters+=cost; return true;
    }

    /// <summary>Returns a detached read-only arrival-order list; later Add/eviction cannot alter an earlier snapshot.</summary>
    public IReadOnlyList<PlayConsoleRecord> Snapshot()=>Array.AsReadOnly(records.ToArray());

    private PlaySourceLink? Admit(PlaySourceLink? link)
    {
        if(link is null || !SafeDocument(link.DocumentId) || !documents.TryGetValue(link.DocumentId,out var admitted)
            || link.Line is <=0 || link.ObjectId is { } id && (string.IsNullOrWhiteSpace(id) || id.Length>256 || id.Any(char.IsControl))) return null;
        return new(admitted,link.ObjectId,link.Line);
    }
    private static bool SafeDocument(string id)=>!string.IsNullOrWhiteSpace(id) && id.Length<=1024
        && !id.Any(char.IsControl) && !id.Contains(':') && !id.Contains('\\')
        && id.Split('/').All(part=>part.Length>0 && part is not ("." or ".."));
    private static int LinkSize(PlaySourceLink? source)=>source is null ? 0 : source.DocumentId.Length+(source.ObjectId?.Length ?? 0);
    private bool Drop() { DroppedCount=Increment(DroppedCount); return false; }
    private static long Increment(long count)=>count<long.MaxValue ? count+1 : count;
}
