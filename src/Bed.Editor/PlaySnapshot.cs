using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Bed.Editor;

/// <summary>The explicit input choice for a linked Play start; neither choice saves or reloads the editor.</summary>
public enum PlayInputChoice
{
    /// <summary>The session's last admitted saved baseline, not a fresh filesystem read.</summary>
    Saved,
    /// <summary>The captured current draft, even when it has never been saved.</summary>
    Draft,
}

/// <summary>Immutable text candidates supplied by an admitted host. DocumentId is logical identity, not path authority.
/// Revision identifies the editor capture, including when Saved selects an older saved baseline.
/// A host needing fresh disk contents must perform its existing conflict/admission policy before capture.</summary>
public sealed record PlaySnapshotSource(string DocumentId,long Revision,string? SavedText,string DraftText);

/// <summary>Exactly one immutable selected input given to game-owned conversion, with no session or save callback.
/// The converter never receives the unselected saved/draft text through this surface.</summary>
public sealed record PlaySnapshotDocument(string DocumentId,long Revision,PlayInputChoice Choice,string Text);

/// <summary>A game-owned conversion candidate. Bytes are copied before publication; null bytes or a Refusal means
/// unsupported input. Format identifies the game-owned representation, not an editor-invented runtime schema.</summary>
public sealed record PlayConversion(string Format,byte[]? Bytes,string? Refusal=null);

/// <summary>Pure game-owned conversion of already captured text to its runtime representation.
/// No fallback converter, process launch, filesystem access or save operation is supplied by this contract.</summary>
public delegate PlayConversion PlaySnapshotConverter(PlaySnapshotDocument input);

/// <summary>Snapshot admission result; refusal never publishes partial bytes.</summary>
public sealed record PlaySnapshotResult(PlaySnapshot? Snapshot,PlayProtocolError? Error)
{
    /// <summary>Whether conversion published one complete immutable input.</summary>
    public bool Accepted=>Snapshot is not null && Error is null;
}

/// <summary>An owned immutable runtime input and its metadata. It retains no editor, session, callback or mutable
/// converter buffer. A captured snapshot does not follow later edits, undo, saves or converter-buffer changes.</summary>
public sealed class PlaySnapshot
{
    private readonly byte[] payload;
    private PlaySnapshot(PlaySnapshotDocument input,string format,byte[] bytes)
    {
        DocumentId=input.DocumentId; Revision=input.Revision; Choice=input.Choice; Format=format;
        payload=(byte[])bytes.Clone();
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(ReadOnlySpan<byte> bytes)
        {
            Span<byte> length=stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(length,bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
        // Canonical identity: length-prefixed UTF-8 fields, then 8-byte big-endian revision, then payload.
        Field("linked-play-input-v1"u8); Field(Encoding.UTF8.GetBytes(DocumentId));
        Field(Choice==PlayInputChoice.Saved ? "saved"u8 : "draft"u8);
        Field(Encoding.UTF8.GetBytes(Format));
        Span<byte> revision=stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(revision,Revision);
        Field(revision); Field(payload);
        Hash=Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Logical source identity; never interpreted as an absolute path or read by this object.</summary>
    public string DocumentId { get; }
    /// <summary>Editor revision at capture, not a claim that Saved's baseline was created at this revision.</summary>
    public long Revision { get; }
    /// <summary>The explicitly selected saved baseline or draft.</summary>
    public PlayInputChoice Choice { get; }
    /// <summary>Declared game-owned runtime representation.</summary>
    public string Format { get; }
    /// <summary>Lowercase SHA-256 over the canonical envelope metadata and owned payload.</summary>
    public string Hash { get; }
    /// <summary>Owned payload length, in bytes.</summary>
    public int Length=>payload.Length;
    /// <summary>Returns a detached byte copy; consumers cannot mutate the stored input through this API.</summary>
    public byte[] CopyPayload()=>(byte[])payload.Clone();

    /// <summary>Binds this reusable immutable input to a fresh start identity. Session identity is supplied by the
    /// session owner; empty identity refuses, and reusing it for another start violates the PL01 contract.</summary>
    public PlayCorrelation Correlate(Guid sessionId)=>sessionId!=Guid.Empty ? new(sessionId,Revision,Hash)
        : throw new ArgumentException("A linked Play start needs a nonempty session identity.",nameof(sessionId));

    /// <summary>Captures current session text and admitted saved baseline, then converts the selected one.
    /// No Save, MarkSaved, CheckDisk, Reload, command or session mutation is invoked. Source format/converter faults
    /// become typed refusals. Saved without an admitted file baseline refuses, including never-saved documents.</summary>
    public static PlaySnapshotResult FromSession<TDoc>(DocumentSession<TDoc> session,string documentId,
        PlayInputChoice choice,PlaySnapshotConverter? converter) where TDoc:class
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            var revision=session.Revision;
            // Do not serialize the unselected draft merely to play a saved baseline.
            var source=new PlaySnapshotSource(documentId,revision,session.ExpectedOnDisk,
                choice==PlayInputChoice.Draft ? session.TextToSave : "");
            if(session.Revision!=revision) return Refuse(PlayErrorCode.InvalidMessage,"The draft changed while Play input was captured. Try again.");
            return Create(source,choice,converter);
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        { return Refuse(PlayErrorCode.LoadFailed,"Could not capture Play input: "+error.Message); }
    }

    /// <summary>Converts one explicitly selected immutable text candidate and copies all published bytes.
    /// A null converter is unsupported, not a request to serialize an invented runtime format.
    /// No filesystem, editor session or save facility is reachable through the supplied input.</summary>
    public static PlaySnapshotResult Create(PlaySnapshotSource source,PlayInputChoice choice,PlaySnapshotConverter? converter)
    {
        if(source is null || string.IsNullOrWhiteSpace(source.DocumentId) || source.DocumentId.Length>1024
            || source.DocumentId.Any(char.IsControl) || source.Revision<0 || !Enum.IsDefined(choice))
            return Refuse(PlayErrorCode.InvalidMessage,"Play needs a logical document identity, captured revision and explicit Saved or Draft choice.");
        var text=choice==PlayInputChoice.Saved ? source.SavedText : source.DraftText;
        if(text is null) return Refuse(PlayErrorCode.UnsupportedSnapshot,
            choice==PlayInputChoice.Saved ? "This file hasn't been saved yet. Choose Draft, or save it first."
                : "The captured draft text is unavailable.");
        if(converter is null) return Refuse(PlayErrorCode.UnsupportedSnapshot,"This game has no declared Play-input converter.");
        var input=new PlaySnapshotDocument(source.DocumentId,source.Revision,choice,text);
        try
        {
            var converted=converter(input);
            if(converted is null) return Refuse(PlayErrorCode.UnsupportedSnapshot,"The game returned no Play-input conversion.");
            if(!string.IsNullOrWhiteSpace(converted.Refusal)) return Refuse(PlayErrorCode.UnsupportedSnapshot,converted.Refusal);
            if(converted.Bytes is null) return Refuse(PlayErrorCode.UnsupportedSnapshot,"The game does not support this Play input.");
            if(string.IsNullOrWhiteSpace(converted.Format) || converted.Format.Length>128 || converted.Format.Any(char.IsControl))
                return Refuse(PlayErrorCode.InvalidMessage,"The Play-input converter needs a declared runtime format.");
            return new(new(input,converted.Format,converted.Bytes),null);
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        { return Refuse(PlayErrorCode.LoadFailed,"The game could not convert this Play input: "+error.Message); }
    }

    private static PlaySnapshotResult Refuse(PlayErrorCode code,string message)
    {
        var plain=new string(message.Select(c=>char.IsControl(c) && c is not ('\n' or '\r' or '\t') ? ' ' : c)
            .Take(PlayProtocol.MaxErrorLength).ToArray());
        return new(null,new(code,plain));
    }
}
