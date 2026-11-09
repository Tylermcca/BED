using System.Runtime.InteropServices;

namespace Brutal.Authoring.Launcher;

// The OS bridge does not depend on BRUTAL or adapters; it must run before those are entered.
internal static class RuntimeCheckWindows
{
    internal static int Check(TextWriter errors) => OperatingSystem.IsWindows()
        ? RuntimeCheck.Check(CanLoad, errors, ShowMessage)
        : 0;

    private static bool CanLoad(string library)
    {
        if (!NativeLibrary.TryLoad(library, typeof(RuntimeCheckWindows).Assembly,
            DllImportSearchPath.System32, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }

    private static void ShowMessage(string message) =>
        _ = MessageBoxW(IntPtr.Zero, message, "BED can't start", 0x10); // MB_OK | MB_ICONERROR

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
