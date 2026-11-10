using System.Text.Json;
using System.Text.Json.Nodes;

namespace Brutal.Authoring.Editor;

/// <summary>Project-owned declarations for the single saved-files Play path. Values grant no execution authority.</summary>
public sealed class PlaySettings
{
    /// <summary>Copies literal argument lists. An empty program means Play still needs configuration.</summary>
    public PlaySettings(string program = "", IEnumerable<string>? arguments = null, string workingFolder = ".", PlayBuildSettings? build = null)
    {
        Program = Text(program, nameof(program)); WorkingFolder = Text(workingFolder, nameof(workingFolder));
        Arguments = Copy(arguments ?? [], nameof(arguments)); Build = build;
    }
    /// <summary>Declared program, normally relative to the project root; never resolved on PATH by this model.</summary>
    public string Program { get; }
    /// <summary>Owned literal argv, not shell text. A path argument remains relative to the declared working folder.</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Declared working folder, normally relative to the project root.</summary>
    public string WorkingFolder { get; }
    /// <summary>Optional declared build and freshness inputs. This model never invokes it.</summary>
    public PlayBuildSettings? Build { get; }

    /// <summary>Reads a project settings document. Missing fields inherit adapter defaults; empty arrays explicitly clear them.</summary>
    public static PlaySettings? ReadProject(string json, PlaySettings? defaults = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = Object(doc.RootElement, "project settings");
        if (!root.TryGetProperty("play", out var value)) return defaults;
        var play = Object(value, "play");
        var build = defaults?.Build;
        if (play.TryGetProperty("build", out var b))
        {
            b = Object(b, "play.build");
            build = new(String(b,"command",build?.Command ?? ""), Array(b,"arguments",build?.Arguments), Array(b,"inputs",build?.Inputs));
        }
        return new(String(play,"program",defaults?.Program ?? ""), Array(play,"arguments",defaults?.Arguments),
            String(play,"workingFolder",defaults?.WorkingFolder ?? "."), build);
    }
    /// <summary>Produces updated JSON while preserving unrelated root and play fields. It writes no file and approves no program.</summary>
    public static string WriteProject(string json, PlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = ReadProject(json); // Reject ambiguous duplicate properties and malformed existing play data.
        var root = JsonNode.Parse(json)!.AsObject();
        var play = root["play"] as JsonObject ?? new JsonObject();
        root["play"] = null; root["play"] = play;
        play["program"] = settings.Program; play["arguments"] = JsonSerializer.SerializeToNode(settings.Arguments);
        play["workingFolder"] = settings.WorkingFolder;
        if(settings.Build is { } build)
        {
            var node = play["build"] as JsonObject ?? new JsonObject();
            play["build"] = null; play["build"] = node;
            node["command"] = build.Command; node["arguments"] = JsonSerializer.SerializeToNode(build.Arguments);
            node["inputs"] = JsonSerializer.SerializeToNode(build.Inputs);
        }
        else play.Remove("build");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    /// <summary>Lexically resolves only a relative path inside the root. Does not inspect files, links, existence or grant browse-out authority.</summary>
    public static bool TryProjectPath(string root, string relative, out string? fullPath, out string? why)
    {
        fullPath=null; why=null;
        if(!Path.IsPathFullyQualified(root)) { why="Choose an absolute project root."; return false; }
        if(string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\0'))
        { why="Choose a path inside this project, or explicitly browse to an external path."; return false; }
        try
        {
            var basePath=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var candidate=Path.GetFullPath(Path.Combine(basePath,relative));
            if(!candidate.Equals(basePath,StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith(basePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            { why="The path leaves this project. Explicitly browse to authorize an external path."; return false; }
            fullPath=candidate;return true;
        }
        catch(Exception e) when(e is ArgumentException or NotSupportedException or PathTooLongException)
        { why="The project path is invalid: "+e.Message;return false; }
    }
    internal static string Text(string value,string name)
    {
        ArgumentNullException.ThrowIfNull(value,name);
        if(value.Length>8192 || value.Contains('\0')) throw new ArgumentException("Use text up to 8192 characters without NUL.",name);
        return value;
    }
    internal static IReadOnlyList<string> Copy(IEnumerable<string> values,string name)
    {
        var result=values.Take(129).Select(v=>Text(v,name)).ToArray();
        if(result.Length>128)throw new ArgumentException("Use at most 128 entries.",name);
        return System.Array.AsReadOnly(result);
    }
    private static JsonElement Object(JsonElement value,string label)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new JsonException(label+" must be an object.");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in value.EnumerateObject())if(!names.Add(p.Name))throw new JsonException(label+" has duplicate field "+p.Name+".");
        return value;
    }
    private static string String(JsonElement value,string name,string fallback)
        => !value.TryGetProperty(name,out var item)?fallback:item.ValueKind==JsonValueKind.String?item.GetString()!:throw new JsonException(name+" must be text.");
    private static IReadOnlyList<string> Array(JsonElement value,string name,IReadOnlyList<string>? fallback)
    {
        if(!value.TryGetProperty(name,out var item))return fallback ?? [];
        if(item.ValueKind!=JsonValueKind.Array)throw new JsonException(name+" must be an array of literal strings.");
        return Copy(item.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.String?v.GetString()!:throw new JsonException(name+" must contain only strings.")),name);
    }
}

/// <summary>Immutable build declaration and relative freshness input globs. No process, shell or file enumeration occurs here.</summary>
public sealed class PlayBuildSettings
{
    /// <summary>Copies bounded literal argv and globs; command resolution belongs to the approved host boundary.</summary>
    public PlayBuildSettings(string command, IEnumerable<string>? arguments = null, IEnumerable<string>? inputs = null)
    { Command=PlaySettings.Text(command,nameof(command));Arguments=PlaySettings.Copy(arguments ?? [],nameof(arguments));Inputs=PlaySettings.Copy(inputs ?? [],nameof(inputs)); }
    /// <summary>The declared build tool; not execution authority or a PATH lookup.</summary>
    public string Command { get; }
    /// <summary>Owned literal build argv.</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Owned project-relative freshness patterns, interpreted only by the host.</summary>
    public IReadOnlyList<string> Inputs { get; }
}
