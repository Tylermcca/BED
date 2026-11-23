using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>Clean-PC test: old status messages stayed up beside newer work. A routine message now leaves the screen after
/// a while; a problem stays until a newer message replaces it; any newer message replaces the last at once.</summary>
public sealed class StatusNoticeTests
{
    [Fact]
    public void A_routine_message_goes_after_a_while_and_a_newer_one_replaces_it()
    {
        long now = 0;
        var notice = new StatusNotice(() => now);
        Assert.False(notice.Showing);
        notice.Done("Saved level.json.");
        now = StatusNotice.ShownFor - 1; Assert.True(notice.Showing);
        now = StatusNotice.ShownFor; Assert.False(notice.Showing);
        Assert.Equal("Saved level.json.", notice.Text);
        notice.Done("Saved level.json.");
        Assert.True(notice.Showing);
        notice.Say("level.json wasn't saved: the disk is full.");
        Assert.Equal("level.json wasn't saved: the disk is full.", notice.Text);
        notice.Say("");
        Assert.False(notice.Showing);
    }

    [Fact]
    public void A_problem_stays_until_something_newer_replaces_it()
    {
        long now = 0;
        var notice = new StatusNotice(() => now);
        notice.Say(false, "The folder dialog could not open.");
        now = StatusNotice.ShownFor * 100; Assert.True(notice.Showing);
        notice.Say(true, "Opened the project.");
        Assert.Equal("Opened the project.", notice.Text);
        now += StatusNotice.ShownFor; Assert.False(notice.Showing);
    }
}
