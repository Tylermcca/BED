
namespace Bed.Editor.ImGui;

/// <summary>The outcome of a project creation request; refusal never means an existing project was replaced.</summary>
public enum ProjectCreationStatus
{
    /// <summary>A complete new project was published.</summary>
    Created,
    /// <summary>The request or installed template could not be used; nothing was created.</summary>
    Refused,
    /// <summary>The destination already existed, or another creator claimed it first.</summary>
    DestinationExists,
    /// <summary>A write failed; inspect the reason and any cleanup leftovers.</summary>
    Failed
}

/// <summary>A read-only preview. File names are relative to Folder; no folder is created by a preview.</summary>
public sealed record ProjectCreationPreview(bool CanCreate, string Reason, string? Folder, IReadOnlyList<string> Files);

/// <summary>A plain-language outcome, project paths on success, and retained new location parents or private staging paths cleanup could not remove.</summary>
public sealed record ProjectCreationResult(ProjectCreationStatus Status, string Reason, string? Folder,
    ProjectPaths? Paths, IReadOnlyList<string> Leftovers);

/// <summary>
/// Creates a new personal project from a built-in template. Does not open it, change recents or edit installed samples.
/// A fresh staging directory is published with a non-overwriting move; an existing destination, even empty, refuses.
/// </summary>
public sealed class ProjectCreator
{
    private readonly string recipes;
    private readonly Action<Stream, ReadOnlyMemory<byte>> write;
    private readonly Action<string, string> publish;

    /// <summary>Uses the bundled recipes location unless the host supplies its installed sample folder.</summary>
    /// <param name="installedRecipes">The installed recipes folder, or null for samples/recipes beside BED.</param>
    public ProjectCreator(string? installedRecipes = null) : this(installedRecipes, null, null) { }

    /// <summary>Creates a creator with replaceable file write and publish steps, used to test copy failures and publication races.</summary>
    public ProjectCreator(string? installedRecipes, Action<Stream, ReadOnlyMemory<byte>>? write,
        Action<string, string>? publish)
    {
        recipes = Path.GetFullPath(installedRecipes ?? Path.Combine(AppContext.BaseDirectory, "samples", "recipes"));
        this.write = write ?? ((stream, bytes) => stream.Write(bytes.Span));
        this.publish = publish ?? Directory.Move;
    }

    /// <summary>The OS Documents folder's BED Projects child, or null when the OS provides no Documents folder.
    /// Does not guess a user profile path or create the folder.</summary>
    public static string? DefaultLocation
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents) ? null : Path.Combine(documents, "BED Projects");
        }
    }

    /// <summary>Validates and lists the exact files, including installed-template availability, without any write.</summary>
    public ProjectCreationPreview Preview(string templateId, string location, string name)
    {
        var prepared = Prepare(templateId, location, name, out _, out var why);
        return prepared is null ? new(false, why, null, Array.Empty<string>())
            : new(true, "", prepared.Folder, prepared.Template.Files);
    }

    /// <summary>Creates location/name. Refuses invalid names, missing templates and every existing destination before writing.</summary>
    public ProjectCreationResult Create(string templateId, string location, string name) =>
        Create(templateId, location, name, out _);

    private ProjectCreationResult Create(string templateId, string location, string name, out bool retryCollision, bool sceneExample = false, bool tourData = false)
    {
        retryCollision = false;
        var prepared = sceneExample ? PrepareSceneExample(location, name, out var status, out var why) : Prepare(templateId, location, name, out status, out why);
        if (prepared is null)
        {
            retryCollision = status == ProjectCreationStatus.DestinationExists;
            return new(status, why, null, null, Array.Empty<string>());
        }
        if(tourData)
        {
            try
            {
                var contents=new Dictionary<string,byte[]>(prepared.Contents,StringComparer.Ordinal);
                var recipesContents=ProjectTemplates.Contents(ProjectTemplates.Find("recipes")!,recipes);
                foreach(var path in new[]{"data/wares.json","schemas/wares.schema.json"})contents.Add(path,recipesContents[path]);
                prepared=prepared with{Contents=contents,Template=prepared.Template with{Files=Array.AsReadOnly(contents.Keys.ToArray())}};
            }
            catch(Exception error)when(FileProblem(error)){return new(ProjectCreationStatus.Refused,"The tour data could not be copied: "+error.Message,null,null,[]);}
        }
        var sharedParents = new List<string>();
        var directories = new List<string>(); // private staging only; never shared location ancestors
        var files = new List<string>();
        string? stage = null;
        var publishing = false;
        try
        {
            MakeDirectories(prepared.Location, sharedParents);
            stage = Path.Combine(prepared.Location, ".bed-new-" + Guid.NewGuid().ToString("N"));
            // Never reuse even an improbable staging-name collision.
            if (File.Exists(stage) || Directory.Exists(stage)) throw new IOException("The temporary project name is already in use. Try again.");
            Directory.CreateDirectory(stage);
            directories.Add(stage);
            foreach (var (relative, bytes) in prepared.Contents)
            {
                var target = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                MakeDirectories(Path.GetDirectoryName(target)!, directories, stage);
                using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                files.Add(target); // includes a file whose write subsequently fails partway
                write(stream, bytes);
            }
            publishing = true;
            publish(stage, prepared.Folder); // Directory.Move does not replace an existing destination.
            var paths = new ProjectPaths(Path.Combine(prepared.Folder, "data"), Path.Combine(prepared.Folder, "schemas"),
                prepared.Contents.ContainsKey("reference_map.json") ? Path.Combine(prepared.Folder, "reference_map.json") : null)
                { Folder = prepared.Folder };
            return new(ProjectCreationStatus.Created, "", prepared.Folder, paths, Array.Empty<string>());
        }
        catch (Exception e) when (FileProblem(e))
        {
            // Do not inspect or remove the destination. It can belong to another writer.
            var collision = publishing && (File.Exists(prepared.Folder) || Directory.Exists(prepared.Folder));
            var leftovers = RollBack(files, directories).ToList();
            var stagingClean = leftovers.Count == 0;
            var reason = collision ? AlreadyThere(name) : $"The project couldn't be created: {e.Message}";
            if (!stagingClean)
                reason += " Some temporary files or folders were left in place; see Leftovers. They were not removed recursively.";
            // Exists/Create cannot establish ownership of a shared parent. Retain every such ancestor,
            // including an empty one, and tell the caller rather than risking another creator's directory.
            foreach (var parent in sharedParents.AsEnumerable().Reverse())
            {
                if (!Directory.Exists(parent)) continue;
                leftovers.Add(parent);
                var empty = false;
                try { empty = !Directory.EnumerateFileSystemEntries(parent).Any(); }
                catch (Exception check) when (FileProblem(check)) { } // do not call an unreadable folder empty
                reason += empty ? $" Left the empty folder {parent}." : $" Left the folder {parent}.";
            }
            // A competing destination can make the shared parent useful. It is not failed staging cleanup.
            retryCollision = collision && stagingClean;
            return new(collision ? ProjectCreationStatus.DestinationExists : ProjectCreationStatus.Failed,
                reason, null, null, Array.AsReadOnly(leftovers.ToArray()));
        }
    }

    /// <summary>Copies the bundled scene example when installed, otherwise recipes, into the next free Example, Example 2, Example 3, ... in the actual OS Documents/BED Projects.
    /// No existing file or folder is overwritten. A known publication collision tries the next name.</summary>
    public ProjectCreationResult TryExample() => DefaultLocation is { } location ? TryExampleAt(location)
        : new(ProjectCreationStatus.Refused, "Windows didn't provide a Documents folder. Choose a project location instead.",
            null, null, Array.Empty<string>());

    /// <summary>Copies the example project into a new folder under <paramref name="location"/> and reports what happened.</summary>
    public ProjectCreationResult TryExampleAt(string location)
    {
        // Bounded against a destination continually claimed by other processes; ordinary existing names are cheap checks.
        for (var number = 1; number <= 10000; number++)
        {
            var name = number == 1 ? "Example" : "Example " + number;
            var result = Create("recipes", location, name, out var retryCollision, Directory.Exists(SceneExampleFolder));
            if (result.Status != ProjectCreationStatus.DestinationExists || !retryCollision) return result;
        }
        return new(ProjectCreationStatus.Refused, "No free example name was found. Choose a project name and location instead.",
            null, null, Array.Empty<string>());
    }

    internal ProjectCreationResult TryTourAt(string location)
    {
        if(!Directory.Exists(SceneExampleFolder))return new(ProjectCreationStatus.Refused,"The tour needs the bundled 3D example. Reinstall the complete BED package.",null,null,[]);
        for(var number=1;number<=10000;number++)
        {
            var result=Create("recipes",location,number==1?"Tour example":"Tour example "+number,out var retry,true,true);
            if(result.Status!=ProjectCreationStatus.DestinationExists||!retry)return result;
        }
        return new(ProjectCreationStatus.Refused,"Choose another location for the tour example.",null,null,[]);
    }
    private string SceneExampleFolder => Path.Combine(Path.GetDirectoryName(recipes)!, "scene-starter");
    private Prepared? PrepareSceneExample(string location, string name, out ProjectCreationStatus status, out string why)
    {
        var target = Prepare("empty", location, name, out status, out why);
        if (target is null) return null;
        var source = SceneExampleFolder;
        if (Within(target.Folder, source) || Within(source, target.Folder))
        { why = "Choose a location outside the installed scene example."; return null; }
        // Declared original sample only. No recursive install copy, cache, symlinks or arbitrary project template.
        string[] required = ["data/level.json", "schemas/scene.schema.json", "reference_map.json",
            "assets/cube.obj", "assets/cube.obj.asset.json", "assets/pillar.obj", "assets/pillar.obj.asset.json",
            "assets/textured-crate.obj", "assets/textured-crate.obj.asset.json", "assets/textured-crate.mtl",
            "assets/textured-crate.png", "assets/textured-crate.png.asset.json",
            "game/SceneStarter.exe", "game/LICENSE.txt", "game/NOTICE.txt"];
        string[] optional = ["README.md", ".bed/project.json", "game/SceneStarter.dll", "game/SceneStarter.deps.json",
            "game/SceneStarter.runtimeconfig.json", "game/Bed.dll", "game/Bed.Runtime.dll",
            "game/export/SceneStarter.exe", "game/export/SceneStarter.dll", "game/export/SceneStarter.deps.json", "game/export/SceneStarter.runtimeconfig.json",
            "game/export/Bed.dll", "game/export/Bed.Runtime.dll", "game/export/LICENSE.txt", "game/export/NOTICE.txt"];
        try
        {
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal); long total = 0;
            foreach (var relative in required.Concat(optional.Where(p => File.Exists(Path.Combine(source, p)))))
            {
                var path = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
                for (var at = path; at is not null && Within(at, source); at = Path.GetDirectoryName(at))
                    if ((File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0) throw new IOException("Installed scene files cannot follow links.");
                var length = new FileInfo(path).Length;
                if (length > 256L * 1024 * 1024 || (total += length) > 512L * 1024 * 1024) throw new IOException("Installed scene example exceeds the copy limit.");
                var bytes = File.ReadAllBytes(path);
                if (bytes.LongLength != length) throw new IOException("Installed scene example changed while copying; try again.");
                contents.Add(relative, bytes);
            }
            var template = new ProjectTemplate("scene-example", "3D example", "A personal copy of the bundled level and game.", Array.AsReadOnly(contents.Keys.ToArray()));
            return new(template, target.Location, target.Folder, contents);
        }
        catch (Exception error) when (FileProblem(error))
        { why = "The installed 3D example could not be copied: " + error.Message; return null; }
    }

    private sealed record Prepared(ProjectTemplate Template, string Location, string Folder, IReadOnlyDictionary<string, byte[]> Contents);

    private Prepared? Prepare(string id, string location, string name, out ProjectCreationStatus status, out string why)
    {
        status = ProjectCreationStatus.Refused;
        why = NameProblem(name) ?? "";
        if (why.Length != 0) return null;
        var template = ProjectTemplates.Find(id);
        if (template is null) { why = "Choose Empty project or Recipes example."; return null; }
        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location))
        { why = "Choose a full path for the project location."; return null; }
        try
        {
            location = Path.GetFullPath(location);
            var folder = Path.Combine(location, name);
            if (Within(folder, recipes) || Within(recipes, folder))
            { why = "Choose a location outside the installed example. Installed samples must stay unchanged."; return null; }
            if (Directory.Exists(folder) || File.Exists(folder))
            { status = ProjectCreationStatus.DestinationExists; why = AlreadyThere(name); return null; }
            // Detect a file used as a parent before any creation; OS Documents may legitimately be redirected.
            for (string? parent = location; parent is not null; parent = Path.GetDirectoryName(parent))
            {
                if (File.Exists(parent)) { why = "The project location contains a file where a folder is needed. Choose another location."; return null; }
            }
            // Read all source bytes before the first destination/parent write; the copy uses this in-memory snapshot.
            var contents = ProjectTemplates.Contents(template, recipes);
            return new(template, location, folder, contents);
        }
        catch (Exception e) when (FileProblem(e))
        { why = $"The template or location can't be used: {e.Message}"; return null; }
    }

    private static bool Within(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string? NameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name is "." or ".." || name.EndsWith('.') || name.Length > 255
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
            return "Use a project name without slashes, special characters, or spaces or dots at the end.";
        var stem = name.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])))
            return "That project name is reserved by Windows. Choose another name.";
        return null;
    }

    private static string AlreadyThere(string name) => $"A folder called {name} is already there. Choose another name.";

    private static bool FileProblem(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static void MakeDirectories(string folder, List<string> created, string? privateRoot = null)
    {
        // The private cleanup list must never acquire shared ancestors, even if staging disappears.
        if (privateRoot is not null && !Within(folder, privateRoot))
            throw new IOException("The temporary project folder changed. Try again.");
        if (Directory.Exists(folder)) return;
        if (privateRoot is not null && Path.GetRelativePath(privateRoot, folder) == ".")
            throw new IOException("The temporary project folder disappeared. Try again.");
        var parent = Path.GetDirectoryName(folder);
        if (parent is not null) MakeDirectories(parent, created, privateRoot);
        // Known preexisting parents are not recorded. Other shared parents are reported on failure, never deleted.
        if (Directory.Exists(folder)) return;
        Directory.CreateDirectory(folder);
        created.Add(folder);
    }

    private static IReadOnlyList<string> RollBack(List<string> files, List<string> directories)
    {
        var left = new List<string>();
        foreach (var file in files.AsEnumerable().Reverse())
        {
            try { File.Delete(file); }
            catch (Exception e) when (FileProblem(e)) { left.Add(file); }
        }
        foreach (var folder in directories.AsEnumerable().Reverse())
        {
            // Non-recursive only: a foreign file in staging or a parent is never removed.
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: false); }
            catch (Exception e) when (FileProblem(e)) { left.Add(folder); }
        }
        return Array.AsReadOnly(left.ToArray());
    }
}
