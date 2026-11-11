using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Bed.Editor.ImGui;
using Brutal.GlfwApi;
using Brutal.GlfwApi.Win32;
namespace Bed.Editor.App;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint ThemeWindowProcedure(nint window,uint message,nuint wParam,nint lParam,nuint id,nuint data);
internal interface IWindowsThemeNotificationApi
{
    bool Attach(nint window,ThemeWindowProcedure procedure,nuint id);
    bool Detach(nint window,ThemeWindowProcedure procedure,nuint id);
    nint Next(nint window,uint message,nuint wParam,nint lParam);
}

// Observes an already-owned HWND. It creates no window and never consumes a message or calls ImGui in the callback.
internal sealed class WindowsThemeNotifications:IDisposable
{
    private static long nextId;
    private static readonly ConcurrentDictionary<nuint,WindowsThemeNotifications> roots=new();
    private readonly nuint id=unchecked((nuint)Interlocked.Increment(ref nextId));
    private readonly int thread=Environment.CurrentManagedThreadId;
    private readonly nint window;
    private readonly IWindowsThemeNotificationApi api;
    private readonly ThemeWindowProcedure procedure;
    private readonly Action changed;
    private bool closed;
    internal bool Retained=>roots.ContainsKey(id);
    internal static int RetainedCount=>roots.Count;

    internal WindowsThemeNotifications(nint window,IWindowsThemeNotificationApi api,Action changed)
    {
        this.window=window;this.api=api;this.changed=changed;procedure=Message;
        roots[id]=this;
        try{if(!api.Attach(window,procedure,id))throw new InvalidOperationException("Windows theme notifications could not attach.");}
        catch{roots.TryRemove(id,out _);throw;}
    }
    internal static void Changed()
    {
        // Invalidate the existing one-second fallback cache; resolve colours on the next ordinary UI frame.
        if(EditorTheme.Preference==EditorThemePreference.System)EditorTheme.Preference=EditorThemePreference.System;
    }
    internal static unsafe WindowsThemeNotifications? Attach(GlfwWindow window)
    {
        if(!OperatingSystem.IsWindows())return null;
        try{return new((nint)window.GetWin32Window(),new Native(),Changed);}
        catch(Exception e)when(e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException){return null;}
    }
    private nint Message(nint hwnd,uint message,nuint wParam,nint lParam,nuint subclass,nuint data)
    {
        try
        {
            if(!closed&&message is 0x001A or 0x031A)changed(); // WM_SETTINGCHANGE / WM_THEMECHANGED
            if(message==0x0082) // WM_NCDESTROY: the HWND is ending even if a prior detach failed.
            {closed=true;try{api.Detach(window,procedure,id);}finally{roots.TryRemove(id,out _);}}
        }
        catch{ } // No managed exception may escape a native callback; default processing still runs.
        nint result;try{result=api.Next(hwnd,message,wParam,lParam);}catch{result=0;}
        GC.KeepAlive(procedure);return result;
    }
    public void Dispose()
    {
        if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Detach Windows theme notifications on their owning thread.");
        if(closed)return;closed=true;
        // Keep a failed detach's delegate rooted until WM_NCDESTROY, never leave a dangling native callback.
        try{if(api.Detach(window,procedure,id))roots.TryRemove(id,out _);}
        catch(Exception e)when(e is not OutOfMemoryException){ } // Remain rooted until HWND destruction.
    }
    private sealed class Native:IWindowsThemeNotificationApi
    {
        public bool Attach(nint window,ThemeWindowProcedure procedure,nuint id)=>SetWindowSubclass(window,procedure,id,0);
        public bool Detach(nint window,ThemeWindowProcedure procedure,nuint id)=>RemoveWindowSubclass(window,procedure,id);
        public nint Next(nint window,uint message,nuint wParam,nint lParam)=>DefSubclassProc(window,message,wParam,lParam);
        [DllImport("comctl32.dll",ExactSpelling=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(nint window,ThemeWindowProcedure procedure,nuint id,nuint data);
        [DllImport("comctl32.dll",ExactSpelling=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(nint window,ThemeWindowProcedure procedure,nuint id);
        [DllImport("comctl32.dll",ExactSpelling=true)]
        private static extern nint DefSubclassProc(nint window,uint message,nuint wParam,nint lParam);
    }
}
