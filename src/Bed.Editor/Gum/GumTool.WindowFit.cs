using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bed.Editor.Gum;

public static partial class GumTool
{
    // Apply the monitor work area to a window of the exact process BED just opened.
    // Leave unrelated Gum windows and windows already filling the monitor alone.
    private static bool FitStartedWindow(Process process, string project)
    {
        if (!OperatingSystem.IsWindows()) return true;
        process.Refresh();
        if (process.HasExited) return true;
        var window = process.MainWindowHandle;
        if (window == 0 || !SameFile(process.MainWindowTitle, project)) return false;
        GetWindowThreadProcessId(window, out var owner);
        if (owner != process.Id) return false;
        if (IsZoomed(window)) return true;
        if (IsIconic(window) || !GetWindowRect(window, out var bounds)) return false;
        var monitor = MonitorFromWindow(window, 2); // nearest monitor, including a window left off-screen
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info)) return false;

        var fitted = FitWorkArea(bounds, info.Work);
        if (fitted.Equals(bounds)) return true;
        GetWindowThreadProcessId(window, out owner);
        const uint noZOrderOrActivate = 0x0004 | 0x0010 | 0x4000;
        return owner == process.Id && SetWindowPos(window, 0, fitted.Left, fitted.Top,
            fitted.Right - fitted.Left, fitted.Bottom - fitted.Top, noZOrderOrActivate);
    }

    internal static WindowRect FitWorkArea(WindowRect window, WindowRect area)
    {
        var width = Math.Clamp(window.Right - window.Left, 1, area.Right - area.Left);
        var height = Math.Clamp(window.Bottom - window.Top, 1, area.Bottom - area.Top);
        var left = Math.Clamp(window.Left, area.Left, area.Right - width);
        var top = Math.Clamp(window.Top, area.Top, area.Bottom - height);
        return new(left, top, left + width, top + height);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct WindowRect(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public WindowRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(nint window, out int process);
    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetWindowRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
