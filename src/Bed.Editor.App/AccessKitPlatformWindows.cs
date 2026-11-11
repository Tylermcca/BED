using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.App;

// Chains GLFW's existing callbacks; neither creates a window nor enables a new layout policy itself.
internal sealed unsafe class AccessKitPlatformWindows : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WindowCallback(nint viewport);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TitleCallback(nint viewport, nint text);
    private readonly WindowCallback create, show, destroy;
    private readonly TitleCallback setTitle;
    private readonly nint oldCreate, oldShow, oldDestroy, oldTitle;
    private readonly uint main;
    private readonly string fallbackTitle;
    private readonly AccessKitViewportBridges bridges;
    private readonly Dictionary<uint, string> titles = new();
    private ExceptionDispatchInfo? failure;
    private bool closed;

    internal AccessKitPlatformWindows(string title, AccessKitViewportBridges? bridges = null)
    {
        this.bridges = bridges ?? new(AccessKitWindowBridge.Attach);
        fallbackTitle = title; main = Im.GetMainViewport().ID.Value;
        var io = Im.GetPlatformIO();
        oldCreate = io.Platform_CreateWindow; oldShow = io.Platform_ShowWindow;
        oldDestroy = io.Platform_DestroyWindow; oldTitle = io.Platform_SetWindowTitle;
        if (oldCreate == 0 || oldShow == 0 || oldDestroy == 0 || oldTitle == 0)
            throw new InvalidOperationException("The GLFW viewport callbacks must be installed before accessibility hooks.");
        create = Created; show = Show; destroy = Destroy; setTitle = SetTitle;
        io.Platform_CreateWindow = Marshal.GetFunctionPointerForDelegate(create);
        io.Platform_ShowWindow = Marshal.GetFunctionPointerForDelegate(show);
        io.Platform_DestroyWindow = Marshal.GetFunctionPointerForDelegate(destroy);
        io.Platform_SetWindowTitle = Marshal.GetFunctionPointerForDelegate(setTitle);
    }

    private void Created(nint address)
    {
        try
        {
            var view = new ImGuiViewportPtr((ImGuiViewport*)address);
            ((delegate* unmanaged[Cdecl]<ImGuiViewportPtr, void>)oldCreate)(view);
            if (view.ID.Value != main)
            {
                bridges.Create(view.ID.Value, view.PlatformHandleRaw, fallbackTitle);
                titles[view.ID.Value] = fallbackTitle;
            }
        }
        catch (Exception error) { SaveFailure(error); }
    }

    private void Show(nint address)
    {
        try
        {
            var view = new ImGuiViewportPtr((ImGuiViewport*)address);
            if (failure is not null) return; // Fail closed: never show an unattached secondary window.
            if (view.ID.Value == main) Call(oldShow, view);
            else bridges.Show(view.ID.Value, view.PlatformHandleRaw, () => Call(oldShow, view));
        }
        catch (Exception error) { SaveFailure(error); }
    }

    private void Destroy(nint address)
    {
        var view = new ImGuiViewportPtr((ImGuiViewport*)address);
        try
        {
            if (view.ID.Value == main || !titles.Remove(view.ID.Value)) Call(oldDestroy, view);
            else bridges.Destroy(view.ID.Value, view.PlatformHandleRaw, () => Call(oldDestroy, view));
        }
        catch (Exception error) { SaveFailure(error); }
    }

    private void SetTitle(nint address, nint text)
    {
        try
        {
            var view = new ImGuiViewportPtr((ImGuiViewport*)address);
            ((delegate* unmanaged[Cdecl]<ImGuiViewportPtr, byte*, void>)oldTitle)(view, (byte*)text);
            if (titles.ContainsKey(view.ID.Value)) titles[view.ID.Value] = Marshal.PtrToStringUTF8(text) ?? fallbackTitle;
        }
        catch (Exception error) { SaveFailure(error); }
    }

    private static void Call(nint callback, ImGuiViewportPtr view) =>
        ((delegate* unmanaged[Cdecl]<ImGuiViewportPtr, void>)callback)(view);
    private void SaveFailure(Exception error) => failure ??= ExceptionDispatchInfo.Capture(error);
    internal void ThrowPendingFailure() => failure?.Throw();
    internal void DrainRequests() { ThrowPendingFailure(); bridges.DrainRequests(); }

    internal void Publish(UiSnapshot snapshot)
    {
        ThrowPendingFailure();
        foreach (var view in Im.GetPlatformIO().Viewports)
        {
            if (view.ID.Value == main || view.PlatformHandleRaw == 0 || !titles.TryGetValue(view.ID.Value, out var title)) continue;
            bridges.Publish(view.ID.Value, view.PlatformHandleRaw, snapshot, title,
                new(view.Pos.X, view.Pos.Y, view.Size.X, view.Size.Y), view.FramebufferScale.X, view.FramebufferScale.Y);
        }
    }

    public void Dispose()
    {
        if (closed) return;
        closed = true;
        try { Im.DestroyPlatformWindows(); } // Destroy callbacks detach before GLFW frees each HWND.
        finally
        {
            var io = Im.GetPlatformIO();
            io.Platform_CreateWindow = oldCreate; io.Platform_ShowWindow = oldShow;
            io.Platform_DestroyWindow = oldDestroy; io.Platform_SetWindowTitle = oldTitle;
            bridges.Dispose();
            GC.KeepAlive(create); GC.KeepAlive(show); GC.KeepAlive(destroy); GC.KeepAlive(setTitle);
        }
        ThrowPendingFailure();
    }
}
