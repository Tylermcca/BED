using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class AssetImportQueueTests
{
    private static AssetImportQueue<string>.Ticket Start(AssetImportQueue<string> q,string revision)
    { var requested=q.Request(revision);Assert.True(q.TryStart(out var ticket));Assert.Same(requested,ticket);return ticket!; }

    [Fact]
    public void Only_one_worker_starts_and_latest_request_waits_for_old_completion()
    {
        var q=new AssetImportQueue<string>();var old=Start(q,"r1");
        var latest=q.Request("r2");Assert.True(old.CancellationRequested);Assert.False(q.TryStart(out _));
        Assert.False(q.Complete(old,"old",out var why));Assert.NotEmpty(why);Assert.Null(q.LastGood);
        Assert.True(q.TryStart(out var next));Assert.Same(latest,next);
        Assert.True(q.Complete(next!,"new",out _));Assert.Equal("r2",q.LastGood!.Revision);Assert.True(q.LastGoodIsCurrent);
        Assert.False(q.TryStart(out _));
    }
    [Fact]
    public void Cancel_retains_slot_until_acknowledged_and_never_promotes_result()
    {
        var q=new AssetImportQueue<string>();var first=Start(q,"r1");Assert.True(q.Complete(first,"good",out _));
        var canceled=Start(q,"r2");q.Cancel();Assert.True(q.InFlight);Assert.False(q.LastGoodIsCurrent);
        Assert.False(q.TryStart(out _));Assert.False(q.Fail(canceled,"canceled",out _));
        Assert.False(q.InFlight);Assert.Equal("good",q.LastGood!.Value);Assert.Null(q.Failure);Assert.False(q.TryStart(out _));
    }
    [Fact]
    public void Failure_preserves_last_good_identity_and_retry_is_a_new_generation()
    {
        var q=new AssetImportQueue<string>();var good=Start(q,"r1");q.Complete(good,"good",out _);
        var failed=Start(q,"r2");Assert.True(q.Fail(failed,"unsupported settings",out _));
        Assert.Equal("unsupported settings",q.Failure);Assert.Equal(good.Generation,q.LastGood!.Generation);Assert.False(q.LastGoodIsCurrent);
        Assert.False(q.TryStart(out _));var retry=Start(q,"r2");Assert.True(retry.Generation>failed.Generation);Assert.Null(q.Failure);
        Assert.True(q.Complete(retry,"retry",out _));Assert.True(q.LastGoodIsCurrent);
    }
    [Fact]
    public void Foreign_duplicate_and_late_tickets_cannot_release_or_replace_active_request()
    {
        var q=new AssetImportQueue<string>();var other=new AssetImportQueue<string>();
        var own=Start(q,"same");var foreign=Start(other,"same");
        Assert.False(q.Complete(foreign,"wrong",out _));Assert.True(q.InFlight);
        Assert.True(q.Complete(own,"right",out _));Assert.False(q.Complete(own,"duplicate",out _));
        var next=Start(q,"next");Assert.False(q.Fail(own,"late",out _));Assert.True(q.InFlight);Assert.Null(q.Failure);
        Assert.True(q.Complete(next,"next",out _));
    }
    [Fact]
    public void Closing_admission_refuses_late_publish_and_reopened_queue_is_distinct()
    {
        var old=new AssetImportQueue<string>();var ticket=Start(old,"r1");old.Close();
        Assert.False(old.TryStart(out _));Assert.Throws<ObjectDisposedException>(()=>old.Request("r2"));
        var reopened=new AssetImportQueue<string>();var fresh=Start(reopened,"r1");
        Assert.False(reopened.Complete(ticket,"old",out _));Assert.True(reopened.InFlight);
        Assert.False(old.Complete(ticket,"late",out _));Assert.Null(old.LastGood);
        Assert.True(reopened.Complete(fresh,"fresh",out _));
    }
}
