using Bed.Editor.App;
using Bed.Editor.ImGui;
using System.Security.Cryptography;
using Xunit;

namespace Bed.Tests;

public sealed class AccessKitWindowA457Tests
{
    private static UiNode Node(ulong id = 10, string value = "current") => new(id, UiRole.TextField, "Visible field", new(10, 20, 100, 30),
        true, null, true, false, false, null, null, value, -1, UiActions.Focus | UiActions.SetValue);
    private static UiSnapshot Frame(UiTree tree, params UiNode[] nodes)
    {
        tree.Begin(); foreach (var node in nodes) tree.Record(node); tree.Complete(); return tree.Snapshot();
    }
    private static bool Publish(AccessKitWindowBridge bridge, UiSnapshot snapshot, string title = "Current title") =>
        bridge.Publish(snapshot, title, new(0, 0, 800, 600), 1, 1);

    [Fact]
    public void Inactive_frames_use_no_native_tree_allocations_and_activation_gets_only_latest_complete_state()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        var tree = new UiTree();
        Assert.True(Publish(bridge, Frame(tree, Node(), Node(11, "removed"))));
        Assert.True(Publish(bridge, Frame(tree, Node() with { Value = "latest", AccessKey = "F" })));
        Assert.Equal(0, trees.NewUpdates); Assert.Equal(0, windows.Updates);
        Assert.NotEqual(0, windows.Activate());
        Assert.Equal(2, trees.Ids.Count); // window plus the remaining field, never the removed field
        Assert.Equal("latest", trees.ValueText);
        Assert.Equal("F", trees.AccessKeyText);
        Assert.Equal(new AccessKitNativeRect(10, 20, 110, 50), trees.Rect);
        Assert.Equal(trees.Ids[1], trees.Focus);
        Assert.Empty(trees.LiveNodes);
    }

    [Fact]
    public void Activation_before_the_first_frame_is_a_safe_window_only_tree()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        Assert.NotEqual(0, windows.Activate());
        Assert.Equal(new ulong[] { 0 }, trees.Ids);
        Assert.Equal("Initial", trees.LabelText);
        Assert.Equal(0UL, trees.Focus);
        Assert.Empty(trees.ChildIds);
    }

    [Fact]
    public void Active_updates_transfer_once_and_events_reenter_only_after_the_new_cache_commits()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        windows.Activate();
        var tree = new UiTree();
        windows.WhenRaised = () => { windows.Activate(); Assert.Equal("accepted", trees.ValueText); };
        Assert.True(Publish(bridge, Frame(tree, Node() with { Value = "accepted" })));
        Assert.Equal(1, windows.Updates); Assert.Equal(1, windows.Raises);
        Assert.Equal(new[] { "attach", "update", "raise" }, windows.Order);
        Assert.Empty(trees.LiveNodes);
        Assert.Equal(trees.NewUpdates, trees.UpdateFrees);
    }

    [Fact]
    public void No_changed_projection_does_not_allocate_or_send_another_update()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        var tree = new UiTree(); windows.Activate();
        Assert.True(Publish(bridge, Frame(tree, Node())));
        var allocations = trees.NewUpdates;
        Assert.False(Publish(bridge, Frame(tree, Node() with { Hovered = true })));
        Assert.Equal(allocations, trees.NewUpdates);
        Assert.Equal(1, windows.Updates);
    }

    [Fact]
    public void Adapter_decline_frees_the_unconsumed_update_and_later_reactivation_has_latest_values()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        var tree = new UiTree(); windows.Activate(); windows.Active = false;
        Assert.True(Publish(bridge, Frame(tree, Node() with { Value = "while inactive" })));
        Assert.Equal(0, windows.Raises); Assert.Empty(trees.LiveNodes);
        trees.Ids.Clear(); windows.Activate();
        Assert.Equal("while inactive", trees.ValueText);
        Assert.Equal(2, trees.Ids.Count);
        Assert.Equal(trees.NewUpdates, trees.UpdateFrees);
    }

    [Fact]
    public void Native_update_failure_keeps_the_previous_cache_and_projection_can_retry_the_same_frame()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        var tree = new UiTree(); windows.Activate(); windows.ThrowUpdate = true;
        var snapshot = Frame(tree, Node());
        Assert.Throws<InvalidOperationException>(() => Publish(bridge, snapshot, "Rejected"));
        Assert.Empty(trees.LiveNodes);
        trees.Ids.Clear(); windows.Activate();
        Assert.Equal(new ulong[] { 0 }, trees.Ids); Assert.Equal("Initial", trees.LabelText);
        windows.ThrowUpdate = false;
        Assert.True(Publish(bridge, snapshot, "Accepted"));
        Assert.Equal(1, windows.Raises);
        Assert.Equal(trees.NewUpdates, trees.UpdateFrees);
    }

    [Fact]
    public void Activation_failure_defers_without_leaking_and_a_later_activation_can_succeed()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        trees.Fail = "new-node";
        Assert.Equal(0, windows.Activate());
        Assert.NotNull(bridge.ActivationFailure); Assert.Empty(trees.LiveNodes);
        trees.Fail = null;
        Assert.NotEqual(0, windows.Activate()); Assert.Null(bridge.ActivationFailure);
    }

    [Fact]
    public void Cross_thread_activation_defers_and_cross_thread_dispose_is_refused_without_detaching()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        using var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        Exception? error = null; nint result = -1;
        var thread = new Thread(() => { result = windows.Callback!(); error = Record.Exception(bridge.Dispose); });
        thread.Start(); thread.Join();
        Assert.Equal(0, result); Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, trees.NewUpdates); Assert.Equal(0, windows.Frees);
    }

    [Fact]
    public void Disposal_raises_pending_events_then_detaches_once_and_reentrant_activation_defers()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees);
        var bridge = new AccessKitWindowBridge(42, "Initial", windows, trees);
        windows.Activate();
        var node = new AccessKitProjectedNode(0, AccessKitProjectedRole.Window, "Changed", null, null, new(0, 0, 800, 600), false, false, null, null, true, []);
        bridge.TryApply(new(true, 0, 0, [node], []));
        Assert.Throws<InvalidOperationException>(() => bridge.TryApply(new(false, 0, 0, [], [])));
        windows.WhenRaised = () => Assert.Equal(0, windows.Callback!());
        bridge.Dispose(); bridge.Dispose();
        Assert.Equal(new[] { "attach", "update", "raise", "free" }, windows.Order);
        Assert.Equal(1, windows.Raises); Assert.Equal(1, windows.Frees);
        Assert.Throws<ObjectDisposedException>(() => bridge.RaisePendingEvents());
        Assert.Equal(0, windows.Callback!());
    }

    [Fact]
    public void Missing_native_attachment_is_refused_before_any_frame_can_be_published()
    {
        var trees = new AccessKitNativeTreeA457Tests.Fake(); var windows = new WindowFake(trees) { ZeroAttach = true };
        Assert.Throws<InvalidOperationException>(() => new AccessKitWindowBridge(42, "Initial", windows, trees));
        Assert.Equal(0, trees.NewUpdates);
        Assert.Throws<ArgumentException>(() => new AccessKitWindowBridge(0, "Initial", windows, trees));
    }

    [Fact]
    public void Native_dependency_is_copied_beside_the_host_without_loading_it()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "accesskit.dll");
        Assert.True(File.Exists(file));
        Assert.Equal("C8E2B528A2F4BBC5EE8E091AE48D55B43156518A0105E48E94C64E101BF1C29B", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
    }

    internal sealed class WindowFake(AccessKitNativeTreeA457Tests.Fake trees) : IAccessKitWindowApi
    {
        public Func<nint>? Callback;
        public Action<AccessKitNativeActionRequest>? Receive;
        public Action? WhenRaised;
        public bool Active, ThrowUpdate, ZeroAttach;
        public int Updates, Raises, Frees;
        public List<string> Order = [];
        public nint NewSubclass(nint hwnd, Func<nint> activate, Action<AccessKitNativeActionRequest> receive) { Assert.Equal((nint)42, hwnd); Order.Add("attach"); Callback = activate; Receive = receive; return ZeroAttach ? 0 : 9; }
        public void FreeSubclass(nint adapter) { Assert.Equal((nint)9, adapter); Order.Add("free"); Frees++; }
        public nint UpdateIfActive(nint adapter, AccessKitEncodedUpdate update)
        {
            Assert.Equal((nint)9, adapter); Assert.True(update.IsOwned);
            Order.Add("update"); Updates++;
            if (ThrowUpdate) throw new InvalidOperationException("Synthetic refusal before ownership transfer");
            if (!Active) return 0;
            trees.FreeUpdate(update.Transfer());
            Assert.False(update.IsOwned);
            return 7;
        }
        public void RaiseEvents(nint events) { Assert.Equal((nint)7, events); Order.Add("raise"); Raises++; WhenRaised?.Invoke(); }
        public nint Activate()
        {
            var pointer = Callback!();
            if (pointer != 0) { Active = true; trees.FreeUpdate(pointer); }
            return pointer;
        }
    }
}
