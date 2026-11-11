using Bed.Editor.App;
using Xunit;
using Bed.Editor.ImGui;

namespace Bed.Tests;

public sealed class DropTests
{
    // A missing synthetic window records origin coordinates without initializing or calling GLFW.
    [Fact]
    public void Glfw_callback_owns_managed_unicode_and_space_paths_before_the_source_batch_changes()
    {
        using var queue = new WindowDropQueue();
        string[] source = [@"C:\Invented projects\O'Brien\café.json", @"C:\Invented projects\second folder"];
        queue.Callback(null!, source.Length, source); source[0] = "changed after callback";
        Assert.True(queue.TryTake(out var batch));
        Assert.Equal([@"C:\Invented projects\O'Brien\café.json", @"C:\Invented projects\second folder"], batch);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)batch)[0] = "caller edit");
        Assert.False(queue.TryTake(out _)); Assert.Null(queue.Problem);
    }

    [Fact]
    public void Batches_keep_arrival_order_and_an_invalid_batch_never_becomes_an_action()
    {
        using var queue = new WindowDropQueue();
        Assert.True(queue.Post(["first.json"])); Assert.False(queue.Post([])); Assert.NotNull(queue.Problem);
        Assert.False(queue.Post([" "])); Assert.True(queue.Post(["last folder"])); Assert.Null(queue.Problem);
        Assert.True(queue.TryTake(out var first)); Assert.Equal(["first.json"], first);
        Assert.True(queue.TryTake(out var last)); Assert.Equal(["last folder"], last);
        Assert.False(queue.TryTake(out var empty)); Assert.Empty(empty);
    }

    [Fact]
    public void Inconsistent_callback_count_is_refused_without_queueing_an_action()
    {
        using var queue = new WindowDropQueue();
        queue.Callback(null!, 2, ["one.json"]);
        Assert.NotNull(queue.Problem); Assert.False(queue.TryTake(out _));
        queue.Callback(null!, 1, ["valid.json"]);
        Assert.Null(queue.Problem); Assert.True(queue.TryTake(out var valid)); Assert.Equal(["valid.json"], valid);
    }

    [Fact]
    public void Captured_cursor_uses_completed_window_bounds_and_ignores_child_item_extents()
    {
        var tree = new UiTree(); tree.Begin();
        tree.Record(new UiNode(1, UiRole.Window, "Files", new(10, 20, 100, 100), true, null, false, false, false, null, null, null, -1, UiActions.None));
        tree.Record(new UiNode(2, UiRole.Button, "Not a window", new(0, 0, 300, 300), true, null, false, false, false, null, null, null, -1, UiActions.None));
        tree.Complete(); var completed = tree.Snapshot();
        using var queue = new WindowDropQueue();
        Assert.True(queue.Post(["invented.json"], 15, 25));
        Assert.True(queue.TryTakeDrop(out var drop)); Assert.NotNull(drop);
        tree.Begin();
        Assert.Equal(1UL, drop.TargetWindow(completed)?.Id);
        Assert.Null((drop with { X = 110 }).TargetWindow(completed));
        Assert.Equal(15, drop.X); Assert.Equal(25, drop.Y);
    }

    [Fact]
    public void Closing_discards_pending_drops_and_refuses_late_posts()
    {
        var queue = new WindowDropQueue(); Assert.True(queue.Post(["pending.json"]));
        queue.Dispose(); queue.Dispose();
        queue.Callback(null!, 1, ["late callback.json"]);
        Assert.False(queue.TryTake(out var empty)); Assert.Empty(empty);
        Assert.False(queue.Post(["late.json"])); Assert.Contains("window is closed", queue.Problem);
    }
}
