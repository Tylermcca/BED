using System.Runtime.InteropServices;
namespace Bed.Launcher;

// Windows argv parsing only. No shell, expansion, saved settings or approval writes.
internal static class ProbeArguments
{
    internal static string[] Parse(string text)
    {
        if(text.Length>32700||text.Contains('\0'))throw new ArgumentException("Probe arguments are too long or contain NUL.");
        var argv=CommandLineToArgvW("BED.exe "+text,out var count);
        if(argv==IntPtr.Zero)throw new ArgumentException("Could not parse probe arguments.");
        try
        {
            if(count>129)throw new ArgumentException("Use at most 128 probe arguments.");
            return Enumerable.Range(1,count-1).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size))!).ToArray();
        }
        finally{LocalFree(argv);}
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CommandLineToArgvW(string commandLine,out int count);
    [DllImport("kernel32.dll",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
