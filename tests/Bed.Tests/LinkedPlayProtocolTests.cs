using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class LinkedPlayProtocolTests
{
    private static PlayCorrelation Identity()=>new(Guid.NewGuid(),7,new string('a',64));

    [Theory]
    [InlineData(PlayCapability.None)] [InlineData(PlayCapability.Console)]
    public void Exact_loaded_identity_negotiates_only_mutually_offered_optional_capabilities(PlayCapability optional)
    {
        var id=Identity(); var hello=PlayProtocol.Hello(id);
        var result=PlayProtocol.Negotiate(hello,new(PlayProtocol.Version,id,PlayProtocol.Required|optional));
        Assert.True(result.Accepted); Assert.Null(result.Error);
        Assert.Equal(PlayProtocol.Required|optional,result.Capabilities);
        Assert.Equal(id,hello.Correlation);
    }

    [Fact]
    public void Unknown_optional_bits_are_not_silently_granted_and_unoffered_console_is_not_enabled()
    {
        var id=Identity(); var unknown=(PlayCapability)128;
        var hello=PlayProtocol.Hello(id,PlayCapability.None) with { Offered=PlayProtocol.Required|unknown };
        var result=PlayProtocol.Negotiate(hello,new(1,id,PlayProtocol.Known|unknown));
        Assert.True(result.Accepted); Assert.Equal(PlayProtocol.Required,result.Capabilities);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)] [InlineData(-1)]
    public void No_version_downgrade_or_best_effort_handshake(int version)
    {
        var id=Identity(); var hello=PlayProtocol.Hello(id);
        var result=PlayProtocol.Negotiate(hello,new(version,id,PlayProtocol.Known));
        Assert.False(result.Accepted); Assert.Equal(PlayCapability.None,result.Capabilities);
        Assert.Equal(PlayErrorCode.UnsupportedVersion,result.Error!.Code);
        Assert.Contains(version.ToString(),result.Error.Message);
        Assert.Equal(PlayErrorCode.UnsupportedVersion,PlayProtocol.Validate(hello with { Version=version })!.Code);
    }

    [Theory]
    [InlineData(PlayCapability.None)] [InlineData(PlayCapability.Snapshot)] [InlineData(PlayCapability.Stop)]
    public void Missing_required_game_capability_refuses_the_entire_handshake(PlayCapability supported)
    {
        var id=Identity(); var result=PlayProtocol.Negotiate(PlayProtocol.Hello(id),new(1,id,supported));
        Assert.False(result.Accepted); Assert.Equal(PlayCapability.None,result.Capabilities);
        Assert.Equal(PlayErrorCode.UnsupportedCapability,result.Error!.Code);
    }

    [Theory]
    [InlineData("session",PlayErrorCode.SessionMismatch)]
    [InlineData("revision",PlayErrorCode.RevisionMismatch)]
    [InlineData("hash",PlayErrorCode.SnapshotMismatch)]
    public void Ready_stop_completion_and_failure_all_require_the_exact_captured_identity(string field,PlayErrorCode code)
    {
        var id=Identity(); var stale=field switch
        {
            "session"=>id with { SessionId=Guid.NewGuid() },
            "revision"=>id with { Revision=id.Revision+1 },
            _=>id with { SnapshotHash=new string('b',64) },
        };
        PlayMessage[] messages=[new PlayReady(1,stale,PlayProtocol.Known),new PlayStop(1,stale),new PlayStopped(1,stale),
            new PlayFailure(1,stale,new(PlayErrorCode.RuntimeFailed,"Self-authored failure"))];
        foreach(var message in messages) Assert.Equal(code,PlayProtocol.Validate(message,id)!.Code);
        var result=PlayProtocol.Negotiate(PlayProtocol.Hello(id),(PlayReady)messages[0]);
        Assert.False(result.Accepted); Assert.Equal(code,result.Error!.Code);
    }

    [Theory]
    [InlineData("empty-session")] [InlineData("negative-revision")] [InlineData("short-hash")]
    [InlineData("uppercase-hash")] [InlineData("nonhex-hash")]
    public void Invalid_input_identity_is_not_a_usable_session(string field)
    {
        var id=Identity(); id=field switch
        {
            "empty-session"=>id with { SessionId=Guid.Empty },
            "negative-revision"=>id with { Revision=-1 },
            "short-hash"=>id with { SnapshotHash="a" },
            "uppercase-hash"=>id with { SnapshotHash=new string('A',64) },
            _=>id with { SnapshotHash=new string('g',64) },
        };
        var result=PlayProtocol.Negotiate(PlayProtocol.Hello(id),new(1,id,PlayProtocol.Known));
        Assert.False(result.Accepted); Assert.Equal(PlayCapability.None,result.Capabilities);
        Assert.Equal(PlayErrorCode.InvalidMessage,result.Error!.Code);
    }

    [Fact]
    public void Contradictory_editor_offer_or_unknown_required_capability_refuses()
    {
        var id=Identity(); var hello=PlayProtocol.Hello(id);
        Assert.Equal(PlayErrorCode.InvalidMessage,PlayProtocol.Validate(hello with { Required=PlayCapability.Snapshot })!.Code);
        Assert.Equal(PlayErrorCode.InvalidMessage,PlayProtocol.Validate(hello with { Offered=PlayCapability.Snapshot })!.Code);
        Assert.Equal(PlayErrorCode.UnsupportedCapability,
            PlayProtocol.Validate(hello with { Required=hello.Required|(PlayCapability)128 })!.Code);
    }

    [Fact]
    public void Structured_game_error_retains_code_and_plain_detail_without_an_execution_or_source_link()
    {
        var id=Identity(); var error=new PlayProtocolError(PlayErrorCode.LoadFailed,"Could not load the invented input.\nCheck the declared format.");
        var message=new PlayFailure(1,id,error);
        Assert.Null(PlayProtocol.Validate(message,id)); Assert.Same(error,message.Error);
        Assert.Equal(PlayErrorCode.LoadFailed,message.Error.Code); Assert.Contains("invented input",message.Error.Message);
        Assert.Null(PlayProtocol.Validate(new PlayStop(1,id),id));
        Assert.Null(PlayProtocol.Validate(new PlayStopped(1,id),id));
    }

    [Theory]
    [InlineData(" ")] [InlineData("bad\\u0000detail")]
    public void Malformed_error_detail_refuses(string detail)
    {
        if(detail.Contains("\\u0000")) detail=detail.Replace("\\u0000","\0");
        var id=Identity(); Assert.Equal(PlayErrorCode.InvalidMessage,
            PlayProtocol.Validate(new PlayFailure(1,id,new(PlayErrorCode.RuntimeFailed,detail)),id)!.Code);
    }

    [Fact]
    public void Oversized_error_unknown_code_missing_message_or_unknown_message_kind_refuses()
    {
        var id=Identity();
        Assert.Null(PlayProtocol.Validate(new PlayFailure(1,id,new(PlayErrorCode.RuntimeFailed,new string('x',PlayProtocol.MaxErrorLength)))));
        Assert.Equal(PlayErrorCode.InvalidMessage,PlayProtocol.Validate(new PlayFailure(1,id,
            new(PlayErrorCode.RuntimeFailed,new string('x',PlayProtocol.MaxErrorLength+1))))!.Code);
        Assert.Equal(PlayErrorCode.InvalidMessage,PlayProtocol.Validate(new PlayFailure(1,id,new((PlayErrorCode)999,"Invented")))!.Code);
        Assert.Equal(PlayErrorCode.InvalidMessage,PlayProtocol.Validate(null!)!.Code);
        Assert.Equal(PlayErrorCode.InvalidMessage,PlayProtocol.Validate(new Unknown(1,id))!.Code);
    }
    private sealed record Unknown(int Version,PlayCorrelation Correlation) : PlayMessage(Version,Correlation);
}
