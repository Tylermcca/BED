using System.Runtime.InteropServices;
using System.Text;

namespace Brutal.Authoring.Launcher;

// WinExe never allocates a console. A terminal or redirected caller may supply one.
internal static class ParentConsole
{
    private const int StdOutput = -11;
    private const int StdError = -12;

    internal static void Connect()
    {
        if (!OperatingSystem.IsWindows()) return;
        var output = GetStdHandle(StdOutput);
        var error = GetStdHandle(StdError);
        if (Usable(output) && Usable(error)) return;
        if (!AttachConsole(uint.MaxValue)) return; // No parent console (double-click), or already attached.
        // AttachConsole can replace inherited handles. Keep a caller's redirected pipe/file intact.
        if (Usable(output)) SetStdHandle(StdOutput, output);
        if (Usable(error)) SetStdHandle(StdError, error);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
    }

    private static bool Usable(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int handle, IntPtr value);
}
