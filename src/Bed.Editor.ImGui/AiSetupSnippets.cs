using System.Text.Json;

namespace Bed.Editor.ImGui;

/// <summary>Read-only, unexecuted setup examples for a known project or the packaged sample.</summary>
public sealed record AiSetupSnippets(string Command, string DesktopJson, string ClaudeCode, string GuidePath)
{
    /// <summary>Quotes one PowerShell literal, including embedded apostrophes; never expands variables or expressions.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>Formats known paths. Does not inspect, load, execute or configure any application.</summary>
    public static AiSetupSnippets Create(string bedFolder, ProjectPaths? project = null, bool readOnly = false)
    {
        bedFolder = Path.GetFullPath(bedFolder);
        var sample = Path.Combine(bedFolder, "samples", "recipes");
        project ??= new ProjectPaths(Path.Combine(sample, "data"), Path.Combine(sample, "schemas"),
            Path.Combine(sample, "reference_map.json"));
        if (readOnly && !string.IsNullOrEmpty(project.AdapterPath))
            throw new ArgumentException("--read-only cannot be combined with --adapter.", nameof(readOnly));
        var exe = Path.Combine(bedFolder, "bed-cli.exe");
        var args = new List<string> { "--data", Path.GetFullPath(project.Data), "--schemas", Path.GetFullPath(project.Schemas) };
        if (!string.IsNullOrEmpty(project.Map)) args.AddRange(["--map", Path.GetFullPath(project.Map)]);
        if (!string.IsNullOrEmpty(project.AdapterPath)) args.AddRange(["--adapter", Path.GetFullPath(project.AdapterPath)]);
        if (readOnly) args.Add("--read-only");
        var mcp = args.Append("mcp").ToArray();
        var json = JsonSerializer.Serialize(new { mcpServers = new { bed = new { command = exe, args = mcp } } },
            new JsonSerializerOptions { WriteIndented = true });
        var command = "& " + Quote(exe) + " " + string.Join(" ", args.Append("describe").Select(Quote));
        var code = "claude mcp add bed -- " + Quote(exe) + " " + string.Join(" ", mcp.Select(Quote));
        return new(command, json, code, Path.Combine(bedFolder, "guides", "using_bed_with_ai.md"));
    }
}
