using System.Collections.Immutable;
namespace Brutal.Authoring.Editor;

/// <summary>The first specified desktop target. This is not proof a game supports it or that its toolchain exists.</summary>
public enum GameBuildTarget
{
    /// <summary>Windows desktop, x64; requires a separately admitted compatible game project.</summary>
    WindowsX64
}
/// <summary>A game-owned runtime input identity and its relative destination in an owned export staging directory.
/// Neither value is a command, a live source path, or permission to read/publish anything.</summary>
public sealed record GameContentMapping(string InputId,string Destination,string RuntimeFormat);

/// <summary>Pure version-one build-profile specification, not a process host or persistent project setting.
/// The later supervisor must admit the project under its owned root and supply structured arguments itself.</summary>
public sealed class GameBuildProfile
{
    private GameBuildProfile(string id,string project,GameBuildTarget target,ImmutableArray<GameContentMapping> content)
    { Id=id;Project=project;Target=target;Content=content; }
    /// <summary>Logical profile identifier.</summary>
    public string Id { get; }
    /// <summary>Relative .csproj path beneath a separately admitted game source root; no shell text.</summary>
    public string Project { get; }
    /// <summary>Declared target, not detected platform capability.</summary>
    public GameBuildTarget Target { get; }
    /// <summary>Fixed first-profile runtime identifier.</summary>
    public string RuntimeIdentifier=>"win-x64";
    /// <summary>Fixed build configuration; no arbitrary MSBuild properties.</summary>
    public string Configuration=>"Release";
    /// <summary>Linked-game protocol expected by the profile; load readiness remains PL01's distinct handshake.</summary>
    public int ProtocolVersion=>PlayProtocol.Version;
    /// <summary>Copied, deterministic mapping declarations. EX02 must prove closure, not infer it from this list.</summary>
    public ImmutableArray<GameContentMapping> Content { get; }
    /// <summary>Validates a bounded pure declaration without reading a path, loading a game or launching a command.
    /// Paths use a portable forward-slash subset; Windows aliases and case-insensitive output collisions refuse.</summary>
    public static GameBuildProfile Create(string id,string project,GameBuildTarget target,IEnumerable<GameContentMapping> content)
    {
        if(!Identifier(id))throw new ArgumentException("Use a bounded plain profile identifier.",nameof(id));
        if(!RelativePath(project)||!project.EndsWith(".csproj",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a relative game .csproj path.",nameof(project));
        if(target!=GameBuildTarget.WindowsX64)throw new ArgumentOutOfRangeException(nameof(target));
        ArgumentNullException.ThrowIfNull(content);
        var rows=content.Take(1025).ToArray();
        if(rows.Length>1024)throw new ArgumentException("The profile has more than 1024 mappings.",nameof(content));
        var ids=new HashSet<string>(StringComparer.Ordinal);var outputs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows)
        {
            if(row is null||!Identifier(row.InputId)||!Identifier(row.RuntimeFormat)||!RelativePath(row.Destination))
                throw new ArgumentException("Every mapping needs a logical input, runtime format and relative output path.",nameof(content));
            if(!ids.Add(row.InputId)||!outputs.Add(row.Destination))throw new ArgumentException("Duplicate input or output mapping.",nameof(content));
        }
        foreach(var output in outputs)
            for(var slash=output.IndexOf('/');slash>=0;slash=output.IndexOf('/',slash+1))
                if(outputs.Contains(output[..slash]))throw new ArgumentException("An output file is also used as a directory.",nameof(content));
        return new(id,project,target,rows.OrderBy(r=>r.Destination,StringComparer.Ordinal).ToImmutableArray());
    }
    private static bool Identifier(string? value)=>value is {Length:>0 and <=128}
        && value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-');
    internal static bool RelativePath(string? value)
    {
        if(value is not {Length:>0 and <=240}||value.Contains('\\')||value.Any(c=>char.IsControl(c)||c is ':' or '"' or '<' or '>' or '|' or '?' or '*'))return false;
        foreach(var part in value.Split('/'))
        {
            if(part.Length==0||part is "." or ".."||part.EndsWith('.')||part.EndsWith(' '))return false;
            var stem=part.Split('.')[0];
            if(stem.Equals("CON",StringComparison.OrdinalIgnoreCase)||stem.Equals("PRN",StringComparison.OrdinalIgnoreCase)
                ||stem.Equals("AUX",StringComparison.OrdinalIgnoreCase)||stem.Equals("NUL",StringComparison.OrdinalIgnoreCase)
                ||Enumerable.Range(1,9).Any(i=>stem.Equals("COM"+i,StringComparison.OrdinalIgnoreCase)||stem.Equals("LPT"+i,StringComparison.OrdinalIgnoreCase)))return false;
        }
        return true;
    }
}

