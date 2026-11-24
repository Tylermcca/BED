using System.Collections;
using System.Diagnostics;
namespace Bed.Editor;

/// <summary>
/// The environment BED gives the programs it starts (game builds, games, tools).
/// When BED itself was started through the dotnet command (run, a test runner), that command leaves variables behind
/// that point MSBuild at its own SDK folder. A child dotnet build or publish may pick a different SDK (another
/// global.json, a newer SDK installed), and then mixes two SDKs' parts: builds fail with errors such as a missing
/// method in a build task. Those variables are dropped so each child dotnet finds its own SDK.
/// </summary>
public static class ChildEnvironment
{
    /// <summary>Variables the dotnet command sets for its own MSBuild, which must not leak into another dotnet.</summary>
    public static IReadOnlyList<string> LeftByDotnet { get; } =
    [
        "MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly",
        "DOTNET_HOST_PATH", "MSBUILDENSURESTDOUTFORTASKPROCESSES", "MSBUILDFAILONDRIVEENUMERATINGWILDCARD", "_MSBUILDTLENABLED",
    ];

    /// <summary>This process's environment without the dotnet command's leftovers, sorted case-insensitively as Windows needs.</summary>
    public static SortedDictionary<string, string> Current()
    {
        var entries = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry item in Environment.GetEnvironmentVariables()) entries[(string)item.Key] = (string?)item.Value ?? "";
        foreach (var name in LeftByDotnet) entries.Remove(name);
        return entries;
    }

    /// <summary>Removes the dotnet command's leftovers from a start request's environment.</summary>
    public static ProcessStartInfo Clean(ProcessStartInfo start)
    {
        foreach (var name in LeftByDotnet) start.Environment.Remove(name);
        return start;
    }
}
