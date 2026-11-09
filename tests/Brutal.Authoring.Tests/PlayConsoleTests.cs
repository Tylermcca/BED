using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlayConsoleTests
{
    private static PlayCorrelation Identity()=>new(Guid.NewGuid(),4,new string('a',64));

    [Fact]
    public void Count_and_total_character_limits_evict_oldest_and_keep_snapshot_immutable()
    {
        var id=Identity(); var buffer=new PlayLogBuffer(id,2,10,15);
        Assert.True(buffer.Add(id,PlayLogSeverity.Info,"1234567890"));
        var before=buffer.Snapshot();
        Assert.True(buffer.Add(id,PlayLogSeverity.Warning,"abcdef"));
        Assert.Single(buffer.Snapshot()); Assert.Equal("abcdef",buffer.Snapshot()[0].Message); Assert.Equal(1,buffer.DroppedCount);
        Assert.True(buffer.Add(id,PlayLogSeverity.Error,"x")); Assert.True(buffer.Add(id,PlayLogSeverity.Trace,"y"));
        Assert.Equal(2,buffer.Count); Assert.Equal(2,buffer.DroppedCount); Assert.InRange(buffer.StoredCharacters,0,15);
        Assert.Equal("1234567890",Assert.Single(before).Message);
        Assert.Equal(new long[]{3,4},buffer.Snapshot().Select(row=>row.Sequence));
        Assert.All(buffer.Snapshot(),row=>Assert.Same(id,row.Correlation));
    }

    [Fact]
    public void Oversized_text_is_bounded_without_split_surrogates_or_control_commands()
    {
        var id=Identity(); var buffer=new PlayLogBuffer(id,3,3,20);
        Assert.True(buffer.Add(id,PlayLogSeverity.Info,"ab\U0001F600tail"));
        Assert.Equal("ab",buffer.Snapshot()[0].Message); Assert.Equal(1,buffer.TruncatedCount);
        Assert.True(buffer.Add(id,PlayLogSeverity.Info,"x\0y"));
        Assert.Equal("x y",buffer.Snapshot()[1].Message);
        Assert.True(buffer.Add(id,PlayLogSeverity.Info,"\ud800x"));
        Assert.Equal("?x",buffer.Snapshot()[2].Message); Assert.InRange(buffer.StoredCharacters,0,20);
    }

    [Theory]
    [InlineData("session")] [InlineData("revision")] [InlineData("hash")] [InlineData("severity")]
    public void Stale_or_invalid_records_cannot_rebind_a_buffer(string bad)
    {
        var id=Identity(); var buffer=new PlayLogBuffer(id,2,20,40);
        var incoming=bad switch { "session"=>id with { SessionId=Guid.NewGuid() },
            "revision"=>id with { Revision=5 },"hash"=>id with { SnapshotHash=new string('b',64) },_=>id };
        Assert.False(buffer.Add(incoming,bad=="severity" ? (PlayLogSeverity)99 : PlayLogSeverity.Info,"stale secret"));
        Assert.Empty(buffer.Snapshot()); Assert.Equal(1,buffer.DroppedCount); Assert.Same(id,buffer.Correlation);
        Assert.True(buffer.Add(id,PlayLogSeverity.Info,"current")); Assert.Equal("current",Assert.Single(buffer.Snapshot()).Message);
    }

    [Theory]
    [InlineData("../outside.scene")] [InlineData("/absolute.scene")] [InlineData("C:/outside.scene")]
    [InlineData("https://example.test/x")] [InlineData("levels/../outside.scene")]
    [InlineData("unknown.scene")] [InlineData("levels\\known.scene")]
    public void Unadmitted_or_unsafe_source_is_omitted_without_dropping_diagnostic(string document)
    {
        var id=Identity(); var buffer=new PlayLogBuffer(id,2,40,100,["levels/known.scene"]);
        Assert.True(buffer.Add(id,PlayLogSeverity.Error,"invented diagnostic",new(document)));
        var record=Assert.Single(buffer.Snapshot());
        Assert.Equal("invented diagnostic",record.Message); Assert.Null(record.Source); Assert.True(record.SourceOmitted);
        Assert.Equal(1,buffer.OmittedSourceCount); Assert.Equal(0,buffer.DroppedCount);
    }

    [Fact]
    public void Source_admission_is_captured_and_all_metadata_fits_the_total_budget()
    {
        var id=Identity(); var allowed=new List<string>{"known.scene"};
        var buffer=new PlayLogBuffer(id,2,10,15,allowed); allowed.Add("later.scene");
        Assert.True(buffer.Add(id,PlayLogSeverity.Warning,"1234567890",new("known.scene","box",2)));
        var first=Assert.Single(buffer.Snapshot()); Assert.Null(first.Source); Assert.True(first.SourceOmitted);
        Assert.Equal(10,buffer.StoredCharacters);
        Assert.True(buffer.Add(id,PlayLogSeverity.Info,"ok",new("later.scene")));
        Assert.Null(buffer.Snapshot()[1].Source);
        Assert.True(buffer.Add(id,PlayLogSeverity.Info,"ok",new("known.scene",Line:0)));
        Assert.Null(buffer.Snapshot()[1].Source); Assert.InRange(buffer.StoredCharacters,0,15);
    }

    [Fact]
    public void Exact_known_link_retains_target_hints_and_budgets_must_be_explicit_and_valid()
    {
        var id=Identity(); var buffer=new PlayLogBuffer(id,2,20,100,["known.scene"]);
        var source=new PlaySourceLink("known.scene","box",3);
        Assert.True(buffer.Add(id,PlayLogSeverity.Warning,"detail",source));
        Assert.Equal(source,Assert.Single(buffer.Snapshot()).Source);
        Assert.Equal("detail".Length+"known.scene".Length+"box".Length,buffer.StoredCharacters);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PlayLogBuffer(id,0,20,100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PlayLogBuffer(id,2,0,100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PlayLogBuffer(id,2,20,10));
        Assert.Throws<ArgumentException>(()=>new PlayLogBuffer(id,2,20,100,["../bad"]));
    }

    private sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host=new(1000,800) { Drawing=false };
        public readonly PlayLogBuffer Logs;
        public readonly ConsolePanel Panel;
        public Rig(float scale=1,Func<PlaySourceLink,bool>? open=null)
        {
            var id=Identity(); Logs=new(id,4,120,600,["known.scene"]);
            Logs.Add(id,PlayLogSeverity.Error,"Invented failure with 100% detail ## still plain",new("known.scene","box",3));
            Panel=new(Logs,open is null ? null : new Dictionary<string,Func<PlaySourceLink,bool>> { ["known.scene"]=open });
            EditorWindow<Scene>.Configure(ImGui.GetIO()); EditorWindow<Scene>.SetTextScale(scale); Host.Frames(Draw,4);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0)); ImGui.SetNextWindowSize(new float2(1000,800));
            ImGui.Begin("Play Console test"u8,ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings);
            Panel.Draw(); ImGui.End();
        }
        public void ClickSource()
        {
            var button=Assert.Single(Ui.Snapshot().Nodes,node=>node.Role==UiRole.Button && node.Label=="Open source");
            var b=button.Bounds; Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2); Host.Frames(Draw,2);
        }
        public void Dispose()=>Host.Dispose();
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Console_tree_exposes_full_plain_message_severity_session_and_loss_counts(float scale)
    {
        using var r=new Rig(scale);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label=="Error: Invented failure with 100% detail ## still plain");
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label.Contains(r.Logs.Correlation.SessionId.ToString("N")));
        r.Logs.Add(r.Logs.Correlation with { Revision=99 },PlayLogSeverity.Error,"stale message"); r.Host.Frames(r.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text && n.Label.Contains("1 dropped"));
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.Contains("stale message"));
        var panel=r.Panel.AsPanel(); Assert.False(panel.OpenAtStart); Assert.Equal("Console",panel.Title);
    }

    [Fact]
    public void Navigation_requires_an_exact_host_action_and_disabled_reason_is_visible()
    {
        using var r=new Rig();
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Open source");
        Assert.False(button.Enabled); Assert.Contains("admitted",button.DisabledReason);
        r.ClickSource(); Assert.Equal("",r.Panel.Said);
    }

    [Theory]
    [InlineData("open")] [InlineData("stale")] [InlineData("fault")]
    public void Source_click_only_calls_the_known_host_action_and_contains_stale_or_faulted_navigation(string outcome)
    {
        var calls=0; PlaySourceLink? seen=null;
        using var r=new Rig(open:link=>
        {
            calls++; seen=link;
            if(outcome=="fault") throw new InvalidOperationException("Invented navigation fault");
            return outcome=="open";
        });
        r.ClickSource(); Assert.Equal(1,calls); Assert.Equal(new PlaySourceLink("known.scene","box",3),seen);
        Assert.Equal(outcome switch { "open"=>"Opened source.","stale"=>"Source is no longer available in this project.",
            _=>"Could not open source." },r.Panel.Said);
        Assert.Equal(1,r.Logs.Count); Assert.Equal(0,r.Logs.DroppedCount);
    }
}
