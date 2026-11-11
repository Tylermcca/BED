using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bed.Editor;

/// <summary>Project JSON persistence, separate from user-local execution approvals.</summary>
public static class PlaySettingsStore
{
    /// <summary>Reads .bed/project.json, inheriting supplied adapter defaults when absent. Malformed content is not replaced.</summary>
    public static PlaySettings? Load(string root, PlaySettings? defaults = null)
    { var path=FilePath(root);return PlaySettings.ReadProject(File.Exists(path)?File.ReadAllText(path):"{}",defaults); }
    /// <summary>Explicitly saves settings, preserving unrelated JSON. Reading settings never calls this operation.</summary>
    public static void Save(string root, PlaySettings settings)
    {
        var path=FilePath(root);var text=PlaySettings.WriteProject(File.Exists(path)?File.ReadAllText(path):"{}",settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);
    }
    private static string FilePath(string root)
    {
        if(!Path.IsPathFullyQualified(root))throw new ArgumentException("Choose an absolute project root.",nameof(root));
        var folder=Path.Combine(Path.GetFullPath(root),".bed");var path=Path.Combine(folder,"project.json");
        foreach(var p in new[]{folder,path})
            if(Path.Exists(p) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Project settings cannot be read or written through a linked path.");
        return path;
    }
}

/// <summary>Resolves declarations, without launching, building or granting approval. Missing programs remain available to stale-build handling.</summary>
public static class PlaySettingsResolver
{
    /// <summary>Resolves paths and optional PATH build tool at call time. The returned launch still requires PlayApprovals checks.</summary>
    public static bool TryResolve(string projectRoot, PlaySettings settings, out PlayLaunch? launch, out string? why, string? searchPath = null)
    {
        launch=null;why=null;
        try
        {
            if(!Path.IsPathFullyQualified(projectRoot))throw new ArgumentException("Choose an absolute project root.");
            var root=Canonical(projectRoot);
            if(string.IsNullOrWhiteSpace(settings.Program))throw new ArgumentException("Choose a program in Play settings.");
            var working=Resolve(root,settings.WorkingFolder);
            if(!Directory.Exists(working))throw new ArgumentException("The Play working folder does not exist.");
            var run=new GameLaunchSpec(Resolve(root,settings.Program),working,settings.Arguments);
            GameLaunchSpec? build=null;var inputs=settings.Build?.Inputs ?? [];
            foreach(var glob in inputs)
                if(string.IsNullOrWhiteSpace(glob) || Path.IsPathRooted(glob) || glob.Contains(':') || glob.Replace('\\','/').Split('/').Contains(".."))
                    throw new ArgumentException("Build input patterns must stay inside the project.");
            if(settings.Build is { } declared && !string.IsNullOrWhiteSpace(declared.Command))
            {
                var command=declared.Command;
                var bare=command.IndexOfAny(['/', '\\', ':'])<0;
                var executable=bare?FindTool(command,searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? ""):Resolve(root,command);
                if(!bare&&!Inside(root,executable))throw new ArgumentException("A relative build command must stay inside the project.");
                build=new(executable,root,declared.Arguments);
            }
            string? stamp=null;
            if(settings.Build?.Stamp is {} declaredStamp)
            {
                if(!PlaySettings.TryProjectPath(root,declaredStamp,out stamp,out var stampWhy))throw new ArgumentException(stampWhy);
                if(build is null)throw new ArgumentException("A build stamp requires a declared build command.");
            }
            launch=new(run,build,root,Array.AsReadOnly(inputs.ToArray())){BuildStamp=stamp};return true;
        }
        catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {why=e.Message;return false;}
    }
    /// <summary>Whether the exact resolved Play executable or working folder lies outside the project root.</summary>
    public static bool NeedsExternalApproval(PlayLaunch launch)
        => !Inside(launch.ProjectRoot,launch.Run.Executable) || !Inside(launch.ProjectRoot,launch.Run.WorkingDirectory);
    private static bool Inside(string root,string path)
    {root=Path.TrimEndingDirectorySeparator(root);return path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
    private static string Resolve(string root,string value)
    {if(string.IsNullOrWhiteSpace(value)||value.Contains('\0'))throw new ArgumentException("Choose a valid program or folder.");return Canonical(Path.IsPathFullyQualified(value)?value:Path.Combine(root,value));}
    private static string FindTool(string command,string searchPath)
    {
        var name=command.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?command:command+".exe";
        foreach(var folder in searchPath.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            var directory=folder.Trim('"');if(!Path.IsPathFullyQualified(directory))continue;
            var candidate=Path.Combine(directory,name);if(File.Exists(candidate))return Canonical(candidate);
        }
        throw new ArgumentException("Build tool was not found on PATH: "+command);
    }
    private static string Canonical(string path)
    {
        var full=Path.GetFullPath(path);var root=Path.GetPathRoot(full)!;var current=root;
        foreach(var part in full[root.Length..].Split(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar},StringSplitOptions.RemoveEmptyEntries))
        {
            if(part.Length==0)continue;current=Path.Combine(current,part);
            if(Path.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
            {
                FileSystemInfo info=Directory.Exists(current)?new DirectoryInfo(current):new FileInfo(current);
                current=info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("The launch path contains an unresolved link.");
            }
        }
        return Path.GetFullPath(current);
    }
}

/// <summary>User-local exact-command approvals. The host supplies its local preferences directory, never a path taken from project settings.</summary>
public sealed class PlayApprovals
{
    private readonly string file;
    /// <summary>Uses a separate user-local preferences file; no constructor read approves or executes anything.</summary>
    public PlayApprovals(string userPreferencesFolder)
    {
        if(!Path.IsPathFullyQualified(userPreferencesFolder))throw new ArgumentException("Use the user's absolute preferences directory.");
        file=Path.Combine(userPreferencesFolder,"play-approvals.json");
    }
    /// <summary>Returns the exact command to confirm, or null when the declared action is already allowed.</summary>
    public string? NeedsApproval(PlayLaunch launch,bool build)
    {
        var command=build?launch.Build:launch.Run;
        if(command is null)return "No build command is configured.";
        if(!build&&!PlaySettingsResolver.NeedsExternalApproval(launch))return null;
        return Read().Contains(Key(launch.ProjectRoot,command))?null:"Needs your OK: "+command.CommandLine()+" (working folder: "+command.WorkingDirectory+")";
    }
    /// <summary>Call only after the person approves the displayed exact command. Never call merely because JSON supplied a value.</summary>
    public void Approve(PlayLaunch launch,bool build)
    {
        var command=(build?launch.Build:launch.Run)??throw new InvalidOperationException("No build command is configured.");
        var keys=Read();keys.Add(Key(launch.ProjectRoot,command));Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file,JsonSerializer.Serialize(keys.Order(StringComparer.Ordinal)));
    }
    private HashSet<string> Read()
    {
        if(!File.Exists(file))return new(StringComparer.Ordinal);
        try{return new(JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [],StringComparer.Ordinal);}
        catch(JsonException){return new(StringComparer.Ordinal);}
    }
    private static string Key(string root,GameLaunchSpec command)
    {
        var exact=JsonSerializer.Serialize(new[]{Path.GetFullPath(root),command.Executable,command.WorkingDirectory}.Concat(command.Arguments));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(exact)));
    }
}
