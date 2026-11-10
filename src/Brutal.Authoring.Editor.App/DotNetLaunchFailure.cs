namespace Brutal.Authoring.Editor.App;

// Windows hostfxr FrameworkMissingFailure; retain the real process exit code.
// https://github.com/dotnet/runtime/blob/main/docs/design/features/host-error-codes.md
internal static class DotNetLaunchFailure
{
    internal static string? Framework(string text)
    {
        var line=text.Trim();
        return line.StartsWith("Framework: ",StringComparison.Ordinal)?line[..Math.Min(line.Length,512)]:null;
    }
    internal static string? Describe(int code,string? framework)
    {
        if(code!=unchecked((int)0x80008096))return null;
        return "The program could not start because a compatible .NET runtime is missing. "+
            (framework is null?"Install the .NET runtime required by this game, then try again.":"Required "+framework+". Install that .NET runtime for the program's architecture, then try again.");
    }
}
