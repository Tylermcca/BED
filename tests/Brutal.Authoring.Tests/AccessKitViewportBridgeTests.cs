using System.Reflection;
using System.Runtime.InteropServices;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AccessKitViewportBridgeTests
{
    private sealed class Window(AccessKitNativeTreeA457Tests.Fake trees, List<string> order) : IAccessKitWindowApi
    {
        public Func<nint>? Activate;
        public Action<AccessKitNativeActionRequest>? Receive;
        public int Frees;
        public bool ThrowFree;
        public nint NewSubclass(nint hwnd, Func<nint> activate, Action<AccessKitNativeActionRequest> receive)
        { order.Add($"attach:{hwnd}"); Activate = activate; Receive = receive; return hwnd; }
        public void FreeSubclass(nint adapter) { order.Add($"free:{adapter}"); Frees++; if (ThrowFree) throw new InvalidOperationException("fake detach error"); }
        public nint UpdateIfActive(nint adapter, AccessKitEncodedUpdate update) { trees.FreeUpdate(update.Transfer()); return 0; }
        public void RaiseEvents(nint events) => throw new InvalidOperationException("No events from this fake.");
        public void Read() { var update = Activate!(); if (update != 0) trees.FreeUpdate(update); }
    }
    private sealed class Rig
    {
        public readonly List<string> Order = [];
        public readonly List<(AccessKitWindowBridge Bridge, Window Window, AccessKitNativeTreeA457Tests.Fake Tree)> Made = [];
        public AccessKitWindowBridge Attach(nint hwnd, string title)
        {
            var tree = new AccessKitNativeTreeA457Tests.Fake(); var window = new Window(tree, Order);
            var bridge = new AccessKitWindowBridge(hwnd, title, window, tree); Made.Add((bridge, window, tree)); return bridge;
        }
    }
    private static UiNode Node(ulong id, uint view, string value) => new(id, UiRole.TextField, "Field", new(110, 220, 20, 10),
        true, null, false, false, false, null, null, value, -1, UiActions.SetValue, ViewportId: view);
    private static UiSnapshot Frame(params UiNode[] nodes)
    { var tree = new UiTree(); tree.Begin(); foreach (var node in nodes) tree.Record(node); tree.Complete(); return tree.Snapshot(); }
    private static void Publish(AccessKitViewportBridges bridges, uint view, nint hwnd, UiSnapshot frame) =>
        bridges.Publish(view, hwnd, frame, "Window", new(100, 200, 400, 300), 2, 2);

    [Fact]
    public void Attach_precedes_show_and_detach_precedes_destroy_with_strict_lifetime_identity()
    {
        var rig = new Rig(); using var bridges = new AccessKitViewportBridges(rig.Attach);
        Assert.Throws<InvalidOperationException>(() => bridges.Show(71, 42, () => rig.Order.Add("bad show")));
        bridges.Create(71, 42, "First");
        Assert.Throws<InvalidOperationException>(() => bridges.Create(71, 43, "Duplicate ID"));
        Assert.Throws<InvalidOperationException>(() => bridges.Create(72, 42, "Duplicate HWND"));
        Assert.Throws<InvalidOperationException>(() => bridges.Show(71, 43, () => rig.Order.Add("bad show")));
        bridges.Show(71, 42, () => rig.Order.Add("show"));
        bridges.Destroy(71, 42, () => rig.Order.Add("destroy"));
        Assert.Equal(new[] { "attach:42", "show", "free:42", "destroy" }, rig.Order);
        Assert.Equal(1, rig.Made[0].Window.Frees);
        Assert.Throws<InvalidOperationException>(() => Publish(bridges, 71, 42, Frame()));
    }

    [Fact]
    public void A_secondary_window_without_size_or_scale_yet_is_skipped_not_fatal()
    {
        // Found in the 0.2.1-rc.1 real-window walk: opening a project whose layout floats a panel closed BED with
        // "A visible viewport and positive finite physical scale are required" on the new window's first frame.
        var rig = new Rig(); using var bridges = new AccessKitViewportBridges(rig.Attach);
        bridges.Create(71, 42, "Floating");
        var frame = Frame(Node(2, 71, "panel"));
        Assert.False(bridges.Publish(71, 42, frame, "Floating", new(100, 200, 0, 0), 2, 2));
        Assert.False(bridges.Publish(71, 42, frame, "Floating", new(100, 200, 400, 300), 0, 0));
        Assert.False(bridges.Publish(71, 42, frame, "Floating", new(100, 200, 400, 300), float.NaN, 1));
        Publish(bridges, 71, 42, frame);   // once it has a size and scale, it publishes as normal
        rig.Made[0].Window.Read();
        Assert.Equal("panel", rig.Made[0].Tree.ValueText);
    }

    [Fact]
    public void Each_HWND_receives_only_its_own_nodes_and_its_actual_origin_and_scale()
    {
        var rig = new Rig(); using var bridges = new AccessKitViewportBridges(rig.Attach);
        bridges.Create(71, 42, "First"); bridges.Create(72, 43, "Second");
        var frame = Frame(Node(1, 0, "main secret"), Node(2, 71, "first"), Node(3, 72, "second"));
        Publish(bridges, 71, 42, frame); Publish(bridges, 72, 43, frame);
        foreach (var made in rig.Made) made.Window.Read();
        Assert.Equal("first", rig.Made[0].Tree.ValueText); Assert.Equal("second", rig.Made[1].Tree.ValueText);
        Assert.All(rig.Made, m => { Assert.Equal(2, m.Tree.Ids.Count); Assert.Equal(new AccessKitNativeRect(20, 40, 60, 60), m.Tree.Rect); });
        Assert.Equal(3, frame.Nodes.Count); // caller's aggregate remains untouched
    }

    [Fact]
    public void Empty_partition_removes_previous_nodes_and_closed_lifetime_cannot_reactivate()
    {
        var rig = new Rig(); using var bridges = new AccessKitViewportBridges(rig.Attach);
        bridges.Create(71, 42, "First"); Publish(bridges, 71, 42, Frame(Node(2, 71, "old")));
        var old = rig.Made[0]; old.Window.Read(); old.Tree.ChildIds.Clear(); old.Tree.Ids.Clear();
        Publish(bridges, 71, 42, Frame(Node(1, 0, "main")));
        Assert.Equal(new ulong[] { 0 }, old.Tree.Ids); Assert.Empty(old.Tree.ChildIds);
        bridges.Destroy(71, 42, () => { });
        Assert.Equal((nint)0, old.Window.Activate!());
        bridges.Create(71, 43, "Reopened"); rig.Made[1].Window.Read();
        Assert.Equal("Reopened", rig.Made[1].Tree.LabelText); Assert.Equal(new ulong[] { 0 }, rig.Made[1].Tree.Ids);
        Assert.Equal(1, old.Window.Frees);
    }

    [Fact]
    public void Main_zero_failed_attachment_and_cross_thread_calls_do_not_show_or_mutate_lifetimes()
    {
        var rig = new Rig(); using var bridges = new AccessKitViewportBridges(rig.Attach);
        Assert.Throws<ArgumentException>(() => bridges.Create(0, 42, "Main"));
        Exception? failure = null; var other = new Thread(() => failure = Record.Exception(() => bridges.Create(71, 42, "Wrong thread")));
        other.Start(); other.Join(); Assert.IsType<InvalidOperationException>(failure); Assert.Empty(rig.Made);
        using var refused = new AccessKitViewportBridges((_, _) => throw new InvalidOperationException("attach refused"));
        Assert.Throws<InvalidOperationException>(() => refused.Create(71, 42, "Refused"));
        var shown = false; Assert.Throws<InvalidOperationException>(() => refused.Show(71, 42, () => shown = true)); Assert.False(shown);
    }

    [Fact]
    public void Shutdown_attempts_every_detach_even_if_an_earlier_native_adapter_reports_failure()
    {
        var rig = new Rig(); var bridges = new AccessKitViewportBridges(rig.Attach);
        bridges.Create(71, 42, "First"); bridges.Create(72, 43, "Second");
        rig.Made[0].Window.ThrowFree = true;
        Assert.Throws<InvalidOperationException>(bridges.Dispose);
        Assert.All(rig.Made, m => Assert.Equal(1, m.Window.Frees));
        Assert.All(rig.Made, m => Assert.Equal((nint)0, m.Window.Activate!()));
        bridges.Dispose(); Assert.All(rig.Made, m => Assert.Equal(1, m.Window.Frees));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WindowCallback(nint viewport);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TitleCallback(nint viewport, nint text);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_platform_hooks_chain_hidden_create_show_destroy_and_restore_original_callbacks(bool refuseAttach)
    {
        using var host = new HeadlessHost(400, 300);
        var io = Im.GetPlatformIO();
        var old = new[] { io.Platform_CreateWindow, io.Platform_ShowWindow, io.Platform_DestroyWindow, io.Platform_SetWindowTitle };
        var rig = new Rig(); using var bridges = new AccessKitViewportBridges((hwnd, label) =>
        {
            if (!refuseAttach) return rig.Attach(hwnd, label);
            rig.Order.Add("attach refused"); throw new InvalidOperationException("synthetic attachment refusal");
        });
        WindowCallback create = p => { var v = Marshal.PtrToStructure<ImGuiViewport>(p); v.PlatformHandleRaw = 42; Marshal.StructureToPtr(v, p, false); rig.Order.Add("hidden create"); };
        WindowCallback show = _ => rig.Order.Add("show");
        WindowCallback destroy = _ => rig.Order.Add("destroy");
        TitleCallback title = (_, _) => { };
        var callbacks = new[] { Marshal.GetFunctionPointerForDelegate(create), Marshal.GetFunctionPointerForDelegate(show), Marshal.GetFunctionPointerForDelegate(destroy), Marshal.GetFunctionPointerForDelegate(title) };
        io.Platform_CreateWindow = callbacks[0]; io.Platform_ShowWindow = callbacks[1]; io.Platform_DestroyWindow = callbacks[2]; io.Platform_SetWindowTitle = callbacks[3];
        var address = Marshal.AllocHGlobal(Marshal.SizeOf<ImGuiViewport>());
        var type = typeof(EditorApp).Assembly.GetType("Brutal.Authoring.Editor.App.AccessKitPlatformWindows", true)!;
        try
        {
            Marshal.StructureToPtr(new ImGuiViewport { ID = new(71) }, address, false);
            var failure = Record.Exception(() =>
            {
                using var hooks = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "Window", bridges }, null)!;
                void Call(nint pointer) => Marshal.GetDelegateForFunctionPointer(pointer, type.GetNestedType("WindowCallback", BindingFlags.NonPublic)!).DynamicInvoke(address);
                Call(io.Platform_CreateWindow); Call(io.Platform_ShowWindow); Call(io.Platform_DestroyWindow);
                Assert.Equal(refuseAttach ? new[] { "hidden create", "attach refused", "destroy" }
                    : new[] { "hidden create", "attach:42", "show", "free:42", "destroy" }, rig.Order);
            });
            if (refuseAttach) Assert.Equal("synthetic attachment refusal", Assert.IsType<InvalidOperationException>(failure).Message);
            else Assert.Null(failure);
            Assert.Equal(callbacks, new[] { io.Platform_CreateWindow, io.Platform_ShowWindow, io.Platform_DestroyWindow, io.Platform_SetWindowTitle });
        }
        finally
        {
            io.Platform_CreateWindow = old[0]; io.Platform_ShowWindow = old[1]; io.Platform_DestroyWindow = old[2]; io.Platform_SetWindowTitle = old[3];
            Marshal.FreeHGlobal(address);
            GC.KeepAlive(create); GC.KeepAlive(show); GC.KeepAlive(destroy); GC.KeepAlive(title);
        }
    }
}
