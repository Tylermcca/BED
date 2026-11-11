using Microsoft.Win32;
namespace Bed.Editor.App;
internal static class WindowsTextScale
{
    private static long nextRead;private static float cached=1;
    internal static float Read()
    {
        if(!OperatingSystem.IsWindows())return 1;
        var now=Environment.TickCount64;if(now<nextRead)return cached;nextRead=now+1000;
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility",false);
            cached=key?.GetValue("TextScaleFactor") is int percent&&percent>=100&&percent<=225?percent/100f:1;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){cached=1;}
        return cached;
    }
}
