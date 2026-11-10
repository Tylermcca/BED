namespace Brutal.Authoring.Editor;

/// <summary>Adapter-declared build command. The literal {output} argument is replaced by BED's owned output directory.
/// An optional whole {input} argument declares BED-owned captured Saved input. No SDK discovery or shell inference occurs.</summary>
public sealed class GameBuildSpec
{
    private readonly GameLaunchSpec template;
    /// <summary>Copies an admitted executable, working directory and bounded argv with exactly one whole output placeholder.</summary>
    public GameBuildSpec(string executable,string workingDirectory,IEnumerable<string> arguments)
    {
        template=new(executable,workingDirectory,arguments);
        if(template.Arguments.Count(a=>a=="{output}")!=1||template.Arguments.Any(a=>a.Contains("{output}",StringComparison.Ordinal)&&a!="{output}"))
            throw new ArgumentException("Build argv needs exactly one whole {output} argument.",nameof(arguments));
        if(template.Arguments.Count(a=>a=="{input}")>1||template.Arguments.Any(a=>a.Contains("{input}",StringComparison.Ordinal)&&a!="{input}"))
            throw new ArgumentException("Build argv accepts at most one whole {input} argument.",nameof(arguments));
    }
    /// <summary>Whether the game explicitly accepts the BED-owned Saved input directory.</summary>
    public bool AcceptsSavedInput=>Arguments.Contains("{input}");
    /// <summary>Exact admitted executable.</summary>
    public string Executable=>template.Executable;
    /// <summary>Exact admitted working directory.</summary>
    public string WorkingDirectory=>template.WorkingDirectory;
    /// <summary>Owned argv template.</summary>
    public IReadOnlyList<string> Arguments=>template.Arguments;
    /// <summary>Binds the literal placeholder only to an admitted absolute owned output path; this does not start a process.</summary>
    public GameLaunchSpec BindOutput(string output,string? input=null)
    {
        if(!Path.IsPathFullyQualified(output))throw new ArgumentException("Declare an absolute owned output directory.",nameof(output));
        if(AcceptsSavedInput&&!Path.IsPathFullyQualified(input??""))throw new ArgumentException("Declare an absolute owned Saved input directory.",nameof(input));
        return new(Executable,WorkingDirectory,Arguments.Select(a=>a=="{output}"?output:a=="{input}"?input!:a));
    }
}
