using System.Reflection;

namespace Bed;

/// <summary>The shared build version generated from Directory.Build.props, without a source-revision suffix.</summary>
public static class BuildVersion
{
    /// <summary>Informational version of the shared library, or unknown if build metadata is absent.</summary>
    public static string Current { get; } = typeof(BuildVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}
