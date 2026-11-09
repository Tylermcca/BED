namespace Brutal.Authoring.Editor;

/// <summary>Version-one linked Play capabilities. Unknown optional bits are ignored, never silently granted.</summary>
[Flags]
public enum PlayCapability
{
    /// <summary>No capability.</summary>
    None=0,
    /// <summary>Accept an immutable snapshot and confirm its exact identity after loading.</summary>
    Snapshot=1,
    /// <summary>Accept a correlated stop request and acknowledge after the session has stopped.</summary>
    Stop=2,
    /// <summary>Emit bounded structured console records; their buffer/transport is a separate layer.</summary>
    Console=4,
}

/// <summary>Immutable session and input identity. Revision is the captured editor revision, not the live revision.
/// SnapshotHash is the lowercase SHA-256 of the immutable play input, supplied by the snapshot builder.
/// A new start uses a fresh nonempty SessionId even when input bytes are identical.</summary>
public sealed record PlayCorrelation(Guid SessionId,long Revision,string SnapshotHash);

/// <summary>Transport-neutral linked Play message. No process, socket, filesystem or serializer is selected here.
/// Version one refuses other protocol versions; it does not reinterpret or migrate them.</summary>
public abstract record PlayMessage(int Version,PlayCorrelation Correlation);

/// <summary>Editor-to-game handshake sent before loading. Offered includes Required; version one requires
/// Snapshot and Stop. Optional capabilities are the intersection of both peers' advertised known bits.</summary>
public sealed record PlayHello(int Version,PlayCorrelation Correlation,PlayCapability Offered,PlayCapability Required)
    : PlayMessage(Version,Correlation);

/// <summary>Game-to-editor readiness, sent only after the exact correlated snapshot has loaded successfully.
/// Receipt alone is not readiness. This message claims loading, not successful gameplay or validation coverage.</summary>
public sealed record PlayReady(int Version,PlayCorrelation Correlation,PlayCapability Supported)
    : PlayMessage(Version,Correlation);

/// <summary>Editor-to-game stop request for this session/input only. Duplicate delivery must not create another session.
/// Resource ownership, deadlines and force termination belong to the supervisor, not this message.</summary>
public sealed record PlayStop(int Version,PlayCorrelation Correlation) : PlayMessage(Version,Correlation);

/// <summary>Game-to-editor completion after the correlated session has ended and released its game-owned resources.
/// A supervisor still owns and must reap its actual process; this message does not grant termination authority.</summary>
public sealed record PlayStopped(int Version,PlayCorrelation Correlation) : PlayMessage(Version,Correlation);

/// <summary>Stable protocol and game failure categories, distinct from human-facing detail.</summary>
public enum PlayErrorCode
{
    /// <summary>Missing, malformed or contradictory message fields.</summary>
    InvalidMessage,
    /// <summary>The peer cannot use this exact protocol version.</summary>
    UnsupportedVersion,
    /// <summary>A required capability is unavailable.</summary>
    UnsupportedCapability,
    /// <summary>The reply belongs to a different start.</summary>
    SessionMismatch,
    /// <summary>The reply identifies another captured editor revision.</summary>
    RevisionMismatch,
    /// <summary>The reply identifies different input bytes.</summary>
    SnapshotMismatch,
    /// <summary>The game cannot convert or consume this declared snapshot.</summary>
    UnsupportedSnapshot,
    /// <summary>The game failed while loading the input.</summary>
    LoadFailed,
    /// <summary>The game reported a failure after loading.</summary>
    RuntimeFailed,
}

/// <summary>A bounded plain-text diagnostic, not executable text, markup, a path or a source-link instruction.</summary>
public sealed record PlayProtocolError(PlayErrorCode Code,string Message);

/// <summary>Either peer reports a correlated error. A stale error is not an error for the current session;
/// the supervisor validates correlation before acting on it. This message never changes an editor draft or save.</summary>
public sealed record PlayFailure(int Version,PlayCorrelation Correlation,PlayProtocolError Error)
    : PlayMessage(Version,Correlation);

/// <summary>Pure handshake result. Rejection grants no capabilities.</summary>
public sealed record PlayNegotiation(PlayCapability Capabilities,PlayProtocolError? Error)
{
    /// <summary>Whether exact version, identity and required capabilities were accepted.</summary>
    public bool Accepted=>Error is null;
}

/// <summary>Pure version-one linked-game handshake validation. Callers validate every message against the captured
/// start identity, never against a live editor revision. No transport, launch, save, lifecycle or authentication
/// implementation is implied. A matching correlation rejects stale replies; it is not a security credential.</summary>
public static class PlayProtocol
{
    /// <summary>The one supported protocol version.</summary>
    public const int Version=1;
    /// <summary>Maximum UTF-16 code units in a structured diagnostic; oversized detail refuses rather than truncating.</summary>
    public const int MaxErrorLength=2048;
    /// <summary>Capabilities every version-one linked session must support.</summary>
    public const PlayCapability Required=PlayCapability.Snapshot|PlayCapability.Stop;
    /// <summary>Known version-one capabilities. Unknown optional bits confer no behavior.</summary>
    public const PlayCapability Known=Required|PlayCapability.Console;

    /// <summary>Creates the standard editor offer. Validate still checks the supplied captured identity.</summary>
    public static PlayHello Hello(PlayCorrelation correlation,PlayCapability optional=PlayCapability.Console)
        =>new(Version,correlation,Required|(optional&Known),Required);

    /// <summary>Checks the version, fields and optionally the exact captured identity of a message.
    /// This is structural admission only; legal message order and deadlines belong to the linked-session owner.</summary>
    public static PlayProtocolError? Validate(PlayMessage message,PlayCorrelation? expected=null)
    {
        PlayProtocolError Refuse(PlayErrorCode code,string text)=>new(code,text);
        if(message is null) return Refuse(PlayErrorCode.InvalidMessage,"The linked Play message is missing.");
        if(message.Version!=Version) return Refuse(PlayErrorCode.UnsupportedVersion,
            $"Linked Play protocol {message.Version} is unsupported; this editor requires version {Version}.");
        if(IdentityError(message.Correlation) is { } identityError) return identityError;
        if(expected is not null)
        {
            if(IdentityError(expected) is { } expectedError) return expectedError;
            if(message.Correlation.SessionId!=expected.SessionId)
                return Refuse(PlayErrorCode.SessionMismatch,"This linked Play reply belongs to another session.");
            if(message.Correlation.Revision!=expected.Revision)
                return Refuse(PlayErrorCode.RevisionMismatch,"This linked Play reply identifies another captured revision.");
            if(message.Correlation.SnapshotHash!=expected.SnapshotHash)
                return Refuse(PlayErrorCode.SnapshotMismatch,"This linked Play reply identifies different snapshot bytes.");
        }
        switch(message)
        {
            case PlayHello hello:
                if((hello.Required&Required)!=Required)
                    return Refuse(PlayErrorCode.InvalidMessage,"Linked Play must require snapshot loading and stop support.");
                if((hello.Required&~Known)!=0)
                    return Refuse(PlayErrorCode.UnsupportedCapability,"This editor does not recognize a required Play capability.");
                if((hello.Offered&hello.Required)!=hello.Required)
                    return Refuse(PlayErrorCode.InvalidMessage,"Offered Play capabilities do not include every required capability.");
                break;
            case PlayReady: case PlayStop: case PlayStopped: break;
            case PlayFailure failure:
                if(failure.Error is null || !Enum.IsDefined(failure.Error.Code)
                    || string.IsNullOrWhiteSpace(failure.Error.Message) || failure.Error.Message.Length>MaxErrorLength
                    || failure.Error.Message.Any(c=>char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
                    return Refuse(PlayErrorCode.InvalidMessage,"The linked Play error needs a known code and bounded plain-text detail.");
                break;
            default: return Refuse(PlayErrorCode.InvalidMessage,"The linked Play message kind is unsupported.");
        }
        return null;
    }

    /// <summary>Accepts readiness only after both messages are valid, exactly correlated and all required capabilities
    /// are supported. Missing optional Console is allowed. Unknown optional bits are never negotiated.</summary>
    public static PlayNegotiation Negotiate(PlayHello hello,PlayReady ready)
    {
        PlayNegotiation Refuse(PlayProtocolError error)=>new(PlayCapability.None,error);
        if(Validate(hello) is { } badHello) return Refuse(badHello);
        if(Validate(ready,hello.Correlation) is { } badReady) return Refuse(badReady);
        if((ready.Supported&hello.Required)!=hello.Required)
            return Refuse(new(PlayErrorCode.UnsupportedCapability,"The game is missing a required linked Play capability."));
        return new(hello.Offered&ready.Supported&Known,null);
    }

    private static PlayProtocolError? IdentityError(PlayCorrelation identity)
    {
        if(identity is null || identity.SessionId==Guid.Empty || identity.Revision<0
            || identity.SnapshotHash is not { Length:64 } hash || hash.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return new(PlayErrorCode.InvalidMessage,"Linked Play needs a nonempty session, a nonnegative captured revision and a lowercase SHA-256 snapshot hash.");
        return null;
    }
}
