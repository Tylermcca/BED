using System.Runtime.InteropServices;

namespace Brutal.Authoring.ExportIO;

// Compiled privately into the UI and App: no adapter or public API change.
internal static class ExportDiskSpace
{
    internal static string Refusal(long needed,long available)=>$"Not enough room on the destination drive: this build needs {needed} bytes; {available} bytes are available.";
    internal static long? AvailableBytes(string folder)
    {
        try
        {
            if(!Path.IsPathFullyQualified(folder))return null;
            var current=Path.GetFullPath(folder);
            while(!Directory.Exists(current))
            {
                if(File.Exists(current))return null;
                current=Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
                if(current is null)return null;
            }
            return GetDiskFreeSpaceExW(current,out var available,out _,out _) ? (long)Math.Min(available,(ulong)long.MaxValue) : null;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {return null;}
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string directory,out ulong available,out ulong total,out ulong free);
}
