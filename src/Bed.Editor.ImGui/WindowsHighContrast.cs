using System.Runtime.InteropServices;
namespace Bed.Editor.ImGui;

internal static class WindowsHighContrast
{
    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast { internal uint Size, Flags; internal nint DefaultScheme; }
    internal static bool? Read()
    {
        if(!OperatingSystem.IsWindows())return null;
        var value=new HighContrast{Size=(uint)Marshal.SizeOf<HighContrast>()};
        try
        {
            // SPI_GETHIGHCONTRAST only; cbSize/uiParam both contain sizeof(HIGHCONTRASTW), no setter flags.
            return SystemParametersInfo(0x0042,value.Size,ref value,0) ? (value.Flags&1)!=0 : null;
        }
        catch(Exception e)when(e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){return null;}
    }
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",ExactSpelling=true,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action,uint size,ref HighContrast value,uint flags);
}
