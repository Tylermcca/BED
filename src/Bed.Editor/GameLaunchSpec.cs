using System.Text;
namespace Bed.Editor;

/// <summary>Immutable adapter declaration of an already admitted executable, working directory and literal argv.
/// It never discovers, builds, searches PATH, expands shell text or interprets a build-project path as executable.</summary>
public sealed class GameLaunchSpec
{
    /// <summary>Copies a bounded argv and validates absolute declarations without accessing the filesystem.</summary>
    public GameLaunchSpec(string executable,string workingDirectory,IEnumerable<string> arguments)
    {
        if(!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)
            || executable.Contains('\0')) throw new ArgumentException("Declare an admitted absolute executable.",nameof(executable));
        if(!Path.IsPathFullyQualified(workingDirectory) || workingDirectory.Contains('\0'))
            throw new ArgumentException("Declare an admitted absolute working directory.",nameof(workingDirectory));
        ArgumentNullException.ThrowIfNull(arguments);
        var copy=arguments.Take(129).ToArray();
        if(copy.Length>128 || copy.Any(arg=>arg is null || arg.Length>8192 || arg.Contains('\0')))
            throw new ArgumentException("Use a bounded list of literal process arguments.",nameof(arguments));
        Executable=executable;WorkingDirectory=workingDirectory;Arguments=Array.AsReadOnly(copy);
        if(CommandLine().Length>32766) throw new ArgumentException("The declared Windows argument list is too long.",nameof(arguments));
    }
    /// <summary>The exact admitted absolute executable.</summary>
    public string Executable { get; }
    /// <summary>The exact admitted absolute working directory.</summary>
    public string WorkingDirectory { get; }
    /// <summary>Owned literal arguments, never shell text.</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Windows CRT argv serialization for CreateProcess, including the executable as argv[0].
    /// Every element is quoted; backslashes are doubled only before quotes and the closing quote.</summary>
    public string CommandLine()
    {
        var output=new StringBuilder();
        foreach(var argument in new[]{Executable}.Concat(Arguments))
        {
            if(output.Length>0) output.Append(' ');
            output.Append('"');var slashes=0;
            foreach(var c in argument)
            {
                if(c=='\\') {slashes++;continue;}
                if(c=='"') {output.Append('\\',slashes*2+1);output.Append(c);}
                else {output.Append('\\',slashes);output.Append(c);}
                slashes=0;
            }
            output.Append('\\',slashes*2);output.Append('"');
        }
        return output.ToString();
    }
}

