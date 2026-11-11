using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Bed.Editor.ImGui;

namespace Bed.Editor.App;

/// <summary>The pinned Windows subclassing API. New requires a hidden, never-shown HWND on its owning thread. Action callbacks copy requests into a managed queue; they never call ImGui or document handlers.</summary>
public interface IAccessKitWindowApi
{
    /// <summary>Attaches before showing. Activation runs on the window thread and may defer with zero; receive may run on another thread and must only queue the owned managed copy.</summary>
    nint NewSubclass(nint hwnd, Func<nint> activate, Action<AccessKitNativeActionRequest> receive);
    /// <summary>Detaches on the owning thread before HWND destruction and prevents subsequent activation calls.</summary>
    void FreeSubclass(nint adapter);
    /// <summary>Consumes the encoded update only if active. A nonzero returned event batch must be raised exactly once.</summary>
    nint UpdateIfActive(nint adapter, AccessKitEncodedUpdate update);
    /// <summary>Consumes a queued event batch; call outside projection/tree mutations because UIA can reenter.</summary>
    void RaiseEvents(nint events);
}

/// <summary>AccessKit C 0.23.2 Windows x64 adapter calls. No window is created here. Callback roots live until explicit owning-thread detachment.</summary>
public sealed class AccessKitNativeWindowApi : IAccessKitWindowApi
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly Dictionary<nint, (GCHandle Activation, long Actions)> activations = [];
    private static readonly ConcurrentDictionary<long, Action<AccessKitNativeActionRequest>> Receivers = new();
    private static long nextReceiver;
    private static readonly ActivationCallback Activation = Activate;
    private static readonly ActionCallback ReceiveAction = CopyAndReleaseAction;
    /// <summary>Requires Windows x64 without loading the DLL yet.</summary>
    public AccessKitNativeWindowApi()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("AccessKit C 0.23.2 is packaged only for Windows x64.");
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "accesskit.dll")))
            throw new DllNotFoundException("The packaged accesskit.dll must be beside the BED host.");
    }
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("AccessKit HWND lifetime belongs to the window thread.");
    }
    /// <inheritdoc/>
    public nint NewSubclass(nint hwnd, Func<nint> activate, Action<AccessKitNativeActionRequest> receive)
    {
        CheckThread(); ArgumentNullException.ThrowIfNull(activate); ArgumentNullException.ThrowIfNull(receive);
        if (hwnd == 0) throw new ArgumentException("A real hidden HWND is required.", nameof(hwnd));
        var root = GCHandle.Alloc(activate);
        var token = Interlocked.Increment(ref nextReceiver);
        if (!Receivers.TryAdd(token, receive)) { root.Free(); throw new InvalidOperationException("Accessibility receiver identity was exhausted."); }
        nint adapter = 0;
        try
        {
            adapter = Native.accesskit_windows_subclassing_adapter_new(hwnd, Activation, GCHandle.ToIntPtr(root), ReceiveAction, (nint)token);
            if (adapter == 0) throw new OutOfMemoryException();
            activations.Add(adapter, (root, token));
            return adapter;
        }
        catch
        {
            Receivers.TryRemove(token, out _);
            try { if (adapter != 0) Native.accesskit_windows_subclassing_adapter_free(adapter); }
            finally { root.Free(); }
            throw;
        }
    }
    /// <inheritdoc/>
    public void FreeSubclass(nint adapter)
    {
        CheckThread();
        if (!activations.TryGetValue(adapter, out var root)) throw new ArgumentException("Adapter is not owned by this instance.", nameof(adapter));
        // No more activation can occur after subclass removal on this same window thread.
        Receivers.TryRemove(root.Actions, out _); // late callbacks can only free their request; tokens are never reused.
        Native.accesskit_windows_subclassing_adapter_free(adapter);
        activations.Remove(adapter); root.Activation.Free();
    }
    /// <inheritdoc/>
    public nint UpdateIfActive(nint adapter, AccessKitEncodedUpdate update)
    {
        CheckThread(); ArgumentNullException.ThrowIfNull(update);
        if (!update.IsOwned) throw new ObjectDisposedException(nameof(update));
        if (!activations.ContainsKey(adapter)) throw new ObjectDisposedException(nameof(adapter));
        // The pinned implementation calls this factory synchronously, on this thread, at most once (FnOnce).
        // Encoding already succeeded before entering native code, so no allocation/validation occurs in the callback.
        UpdateCallback factory = _ => update.Transfer();
        try { return Native.accesskit_windows_subclassing_adapter_update_if_active(adapter, factory, 0); }
        finally { GC.KeepAlive(factory); }
    }
    /// <inheritdoc/>
    public void RaiseEvents(nint events) { CheckThread(); if (events != 0) Native.accesskit_windows_queued_events_raise(events); }

    private static nint Activate(nint data)
    {
        // Nullable activation is expressly supported by the C API. Never let managed exceptions cross the ABI.
        try { return ((Func<nint>)GCHandle.FromIntPtr(data).Target!)(); }
        catch (Exception error) { NoteFailure(error); return 0; }
    }
    private static void CopyAndReleaseAction(nint request, nint token)
    {
        if (request == 0) return;
        try
        {
            if (Receivers.TryGetValue((long)token, out var receive) && AccessKitNativeRequestDecoder.Decode(request) is { } managed)
                receive(managed);
        }
        catch (Exception error) { NoteFailure(error); }
        finally
        {
            try { Native.accesskit_action_request_free(request); }
            catch (Exception error) { NoteFailure(error); }
        }
    }
    private static void NoteFailure(Exception error)
    {
        // Even a custom diagnostic listener must not unwind through a native callback.
        try { Trace.TraceError("AccessKit callback deferred: {0}", error.GetType().Name); } catch { }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ActivationCallback(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint UpdateCallback(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ActionCallback(nint request, nint data);
    private static class Native
    {
        private const string Dll = "accesskit.dll";
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern nint accesskit_windows_subclassing_adapter_new(nint hwnd, ActivationCallback activate, nint activationData, ActionCallback action, nint actionData);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_windows_subclassing_adapter_free(nint adapter);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern nint accesskit_windows_subclassing_adapter_update_if_active(nint adapter, UpdateCallback factory, nint data);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_windows_queued_events_raise(nint events);
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern void accesskit_action_request_free(nint request);
    }
}
