using System.Numerics;
using System.Text;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class PlaySnapshotTests
{
    private static PlaySnapshotSource Source()=>new("level.scene",7,"invented saved text","invented draft text");
    private static PlayConversion ConvertText(PlaySnapshotDocument input)=>new("invented.runtime.v1",Encoding.UTF8.GetBytes(input.Text));
    private static DocumentSession<Scene> Session()=>new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),
        new Scene([SceneObject.At("box",null,Vector3.Zero)]));

    [Theory]
    [InlineData(PlayInputChoice.Saved,"invented saved text")]
    [InlineData(PlayInputChoice.Draft,"invented draft text")]
    public void Choice_is_explicit_and_converter_receives_only_selected_immutable_text(PlayInputChoice choice,string expected)
    {
        PlaySnapshotDocument? seen=null;
        var result=PlaySnapshot.Create(Source(),choice,input=>{seen=input;return ConvertText(input);});
        Assert.True(result.Accepted); Assert.Null(result.Error); Assert.NotNull(result.Snapshot);
        Assert.NotNull(seen); Assert.Equal(expected,seen.Text); Assert.Equal(choice,seen.Choice);
        Assert.Equal("level.scene",seen.DocumentId); Assert.Equal(7,seen.Revision);
        Assert.Equal(expected,Encoding.UTF8.GetString(result.Snapshot.CopyPayload()));
        Assert.Equal(choice,result.Snapshot.Choice); Assert.Equal("invented.runtime.v1",result.Snapshot.Format);
    }

    [Fact]
    public void Converter_buffer_and_consumer_copies_cannot_mutate_published_bytes_or_hash()
    {
        byte[] buffer=[1,2,3,4];
        var snapshot=PlaySnapshot.Create(Source(),PlayInputChoice.Draft,_=>new("invented.binary",buffer)).Snapshot!;
        var hash=snapshot.Hash; buffer[0]=99;
        var copy=snapshot.CopyPayload(); copy[1]=88;
        Assert.Equal(new byte[]{1,2,3,4},snapshot.CopyPayload()); Assert.Equal(4,snapshot.Length); Assert.Equal(hash,snapshot.Hash);
        var id=snapshot.Correlate(Guid.NewGuid());
        Assert.Equal(7,id.Revision); Assert.Equal(hash,id.SnapshotHash);
        Assert.Null(PlayProtocol.Validate(PlayProtocol.Hello(id)));
        Assert.Throws<ArgumentException>(()=>snapshot.Correlate(Guid.Empty));
    }

    [Fact]
    public void Draft_snapshot_stays_pinned_after_later_edit_and_undo_without_save_or_history_mutation()
    {
        var session=Session(); Assert.True(session.Execute(new MoveObject("box",Vector3.UnitX)));
        var current=session.Current; var revision=session.Revision; var history=session.History.UndoCount; var journal=session.Journal.Count;
        var result=PlaySnapshot.FromSession(session,"level.scene",PlayInputChoice.Draft,ConvertText);
        Assert.True(result.Accepted); var snapshot=result.Snapshot!;
        var text=Encoding.UTF8.GetString(snapshot.CopyPayload()); var hash=snapshot.Hash;
        Assert.Contains("1",text); Assert.Equal(session.TextToSave,text);
        Assert.Same(current,session.Current); Assert.Equal(revision,session.Revision); Assert.True(session.Dirty);
        Assert.Equal(history,session.History.UndoCount); Assert.Equal(journal,session.Journal.Count); Assert.Null(session.FilePath);
        Assert.True(session.Execute(new MoveObject("box",new(3,0,0)))); Assert.True(session.Undo());
        Assert.Equal(text,Encoding.UTF8.GetString(snapshot.CopyPayload())); Assert.Equal(hash,snapshot.Hash);
        Assert.Equal(revision,snapshot.Revision); Assert.Null(session.FilePath); Assert.True(session.Dirty);
    }

    [Fact]
    public void Saved_session_baseline_is_a_live_positive_control_and_draft_and_disk_stay_exact()
    {
        var dir=Directory.CreateTempSubdirectory("bed-play-snapshot-").FullName;
        var path=Path.Combine(dir,"level.scene");
        try
        {
            var original=Session().TextToSave;
            File.WriteAllText(path,original);
            var session=DocumentSession<Scene>.Open(path,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());
            Assert.True(session.Execute(new MoveObject("box",Vector3.UnitX)));
            var draft=session.Current; var revision=session.Revision; var journal=session.Journal.Count;
            var result=PlaySnapshot.FromSession(session,"level.scene",PlayInputChoice.Saved,ConvertText);
            Assert.True(result.Accepted);
            Assert.Equal(original,Encoding.UTF8.GetString(result.Snapshot!.CopyPayload()));
            Assert.Equal(PlayInputChoice.Saved,result.Snapshot.Choice); Assert.Equal(revision,result.Snapshot.Revision);
            Assert.Same(draft,session.Current); Assert.Equal(revision,session.Revision);
            Assert.True(session.Dirty); Assert.Equal(journal,session.Journal.Count);
            Assert.Equal(1,session.History.UndoCount); Assert.Equal(original,File.ReadAllText(path));
        }
        finally
        {
            // Only this self-authored fixture file and its unique empty parent were created here.
            File.Delete(path); Directory.Delete(dir);
        }
    }

    [Fact]
    public void Never_saved_choice_refuses_before_conversion_and_does_not_silently_save()
    {
        var session=Session(); Assert.True(session.Execute(new MoveObject("box",Vector3.UnitX)));
        var before=session.Current; var count=session.Journal.Count; var calls=0;
        var result=PlaySnapshot.FromSession(session,"level.scene",PlayInputChoice.Saved,input=>{calls++;return ConvertText(input);});
        Assert.False(result.Accepted); Assert.Null(result.Snapshot); Assert.Equal(PlayErrorCode.UnsupportedSnapshot,result.Error!.Code);
        Assert.Contains("Choose Draft",result.Error.Message); Assert.Equal(0,calls);
        Assert.Same(before,session.Current); Assert.Equal(count,session.Journal.Count); Assert.True(session.Dirty); Assert.Null(session.FilePath);
    }

    [Fact]
    public void Unsupported_or_faulted_conversion_never_publishes_partial_bytes_or_changes_editor()
    {
        var session=Session(); var current=session.Current;
        var unsupported=PlaySnapshot.FromSession(session,"level.scene",PlayInputChoice.Draft,null);
        Assert.False(unsupported.Accepted); Assert.Equal(PlayErrorCode.UnsupportedSnapshot,unsupported.Error!.Code);
        var refusal=PlaySnapshot.FromSession(session,"level.scene",PlayInputChoice.Draft,_=>new("invented",[1],"This invented mode is not supported."));
        Assert.False(refusal.Accepted); Assert.Null(refusal.Snapshot); Assert.Contains("not supported",refusal.Error!.Message);
        var fault=PlaySnapshot.FromSession(session,"level.scene",PlayInputChoice.Draft,_=>throw new InvalidOperationException("Invented conversion fault"));
        Assert.False(fault.Accepted); Assert.Null(fault.Snapshot); Assert.Equal(PlayErrorCode.LoadFailed,fault.Error!.Code);
        Assert.Same(current,session.Current); Assert.Equal(0,session.Revision); Assert.False(session.Dirty);
        Assert.Empty(session.Journal); Assert.Equal(0,session.History.UndoCount); Assert.Null(session.FilePath);
    }

    [Fact]
    public void Identity_is_deterministic_and_covers_choice_logical_source_revision_format_and_payload()
    {
        var source=Source(); var first=PlaySnapshot.Create(source,PlayInputChoice.Draft,ConvertText).Snapshot!;
        Assert.Equal(first.Hash,PlaySnapshot.Create(source,PlayInputChoice.Draft,ConvertText).Snapshot!.Hash);
        PlaySnapshot[] changed=[
            PlaySnapshot.Create(source with { DocumentId="other.scene" },PlayInputChoice.Draft,ConvertText).Snapshot!,
            PlaySnapshot.Create(source with { Revision=8 },PlayInputChoice.Draft,ConvertText).Snapshot!,
            PlaySnapshot.Create(source with { SavedText=source.DraftText },PlayInputChoice.Saved,ConvertText).Snapshot!,
            PlaySnapshot.Create(source,PlayInputChoice.Draft,_=>new("invented.other",Encoding.UTF8.GetBytes(source.DraftText))).Snapshot!,
            PlaySnapshot.Create(source with { DraftText="other bytes" },PlayInputChoice.Draft,ConvertText).Snapshot!,
        ];
        foreach(var snapshot in changed) Assert.NotEqual(first.Hash,snapshot.Hash);
        Assert.Equal(64,first.Hash.Length);
        Assert.NotEqual(first.Correlate(Guid.NewGuid()).SessionId,first.Correlate(Guid.NewGuid()).SessionId);
    }

    [Theory]
    [InlineData("name")] [InlineData("revision")] [InlineData("choice")] [InlineData("missing-draft")]
    public void Invalid_source_refuses_without_calling_game_conversion(string invalid)
    {
        var source=Source(); var choice=PlayInputChoice.Draft; var calls=0;
        switch(invalid)
        {
            case "name":source=source with { DocumentId=" " };break;
            case "revision":source=source with { Revision=-1 };break;
            case "choice":choice=(PlayInputChoice)99;break;
            case "missing-draft":source=source with { DraftText=null! };break;
        }
        var result=PlaySnapshot.Create(source,choice,input=>{calls++;return ConvertText(input);});
        Assert.False(result.Accepted); Assert.Null(result.Snapshot); Assert.NotNull(result.Error); Assert.Equal(0,calls);
    }

    [Theory]
    [InlineData("null-result")] [InlineData("null-bytes")] [InlineData("blank-format")] [InlineData("fault")]
    public void Malformed_conversion_refuses_and_error_detail_is_bounded(string invalid)
    {
        var result=PlaySnapshot.Create(Source(),PlayInputChoice.Draft,_=>invalid switch
        {
            "null-result"=>null!,
            "null-bytes"=>new("invented",null),
            "blank-format"=>new(" ",[1]),
            _=>throw new InvalidOperationException(new string('x',PlayProtocol.MaxErrorLength+300)),
        });
        Assert.False(result.Accepted); Assert.Null(result.Snapshot);
        Assert.InRange(result.Error!.Message.Length,1,PlayProtocol.MaxErrorLength);
        Assert.Null(PlayProtocol.Validate(new PlayFailure(1,new(Guid.NewGuid(),7,new string('a',64)),result.Error)));
    }
}
