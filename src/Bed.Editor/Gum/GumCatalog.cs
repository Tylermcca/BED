using Bed.Data;
using Gum.DataTypes;
using Gum.ProjectServices;

namespace Bed.Editor.Gum;

// GUM-LIST: a read-only list of a Gum project's screens. MIXED: reading is Gum's own (Gum.ProjectServices.ProjectLoader,
// with its load errors and codes; ElementReference's file names); the findings are BED's own check findings
// (Bed.Data.CheckFinding) in plain words (GumWords). Refusing a version newer than the Gum library knows matches Gum's own tool.

/// <summary>Finds Gum projects in a folder and lists their screens, without changing any file.</summary>
public static class GumCatalog
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".bed" };

    /// <summary>The Gum project files (<c>*.gumx</c>, <c>*.gumj</c>) in <paramref name="projectRoot"/> and its immediate
    /// subfolders, sorted by full path. Build output, version-control and BED's own folders are skipped. A folder that
    /// can't be listed is left out rather than stopping the search.</summary>
    /// <param name="projectRoot">The folder to look in.</param>
    public static IReadOnlyList<string> FindProjects(string projectRoot)
    {
        var found = new List<string>();
        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) return found;
        Add(root, found);
        foreach (var folder in Safe(() => Directory.GetDirectories(root)))
            if (!Skipped.Contains(Path.GetFileName(folder))) Add(folder, found);
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    private static void Add(string folder, List<string> found)
    {
        foreach (var file in Safe(() => Directory.GetFiles(folder)))
        {
            if (GumProjectSave.IsProjectFile(file)) found.Add(file);
        }
    }

    private static string[] Safe(Func<string[]> list)
    {
        try { return list(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Reads <paramref name="projectFile"/> with Gum's own loader (the one behind <c>gumcli check</c>) and lists
    /// its screens in the project's own order, each with the full path of its screen file and whether Gum found that
    /// file. What Gum's loader reports (a screen file it couldn't read, unfinished merge marks, a file out of date) comes
    /// back as plain-words findings with Gum's code. A project BED can't read, or one saved by a newer Gum than the Gum
    /// library BED uses, gives no screens and a plain-words finding; nothing is guessed and no file is written.</summary>
    /// <param name="projectFile">A <c>.gumx</c> or <c>.gumj</c> file.</param>
    public static GumProject Read(string projectFile)
    {
        var full = Path.GetFullPath(projectFile);
        return GumLibrary.Run(() =>
        {
            try { return FromLoaded(full, GumLibrary.Load(full)); }
            finally { GumLibrary.Release(); }
        });
    }

    internal static GumProject FromLoaded(string full, ProjectLoadResult loaded)
    {
        var name = Path.GetFileNameWithoutExtension(full);
        if (loaded.Project is not { } project || loaded.ErrorMessage is not null)
            return new GumProject(full, [], [Finding(Severity.Error, full, null,
                $"BED couldn't read the Gum project {name}, because {Why(full, loaded.ErrorMessage)}.", "Open the project in Gum to check it, then try again.")]);
        if (GumLibrary.Unsupported(project))
            return new GumProject(full, [], [Finding(Severity.Error, full, null,
                $"The Gum project {name} was saved by a newer Gum than BED can read, so BED can't list its screens.",
                "Open it in Gum to see it.")]);

        var folder = Path.GetDirectoryName(full)!;
        var words = new GumWords(project, folder, null);
        var screens = new List<GumScreen>();
        var findings = new List<CheckFinding>();
        foreach (var error in loaded.LoadErrors)
        {
            // A file the project lists but Gum didn't find: Gum's checker says it once, as GUM0004 (below for screens).
            if (error.Code is null && project.AllElements.Any(e => e.IsSourceFileMissing && string.Equals(e.Name, error.ElementName, StringComparison.OrdinalIgnoreCase))) continue;
            findings.Add(words.Finding(error, full));
        }
        foreach (var listed in project.ScreenReferences)
        {
            if (string.IsNullOrEmpty(listed.Name)) continue;
            var file = ElementFile(folder, listed, ElementType.Screen, GumProjectSave.IsJsonFormat(full));
            var loadedScreen = project.Screens.FirstOrDefault(s => string.Equals(s.Name, listed.Name, StringComparison.OrdinalIgnoreCase));
            // A screen file Gum couldn't read is there but not loaded; its load error above says why.
            var missing = loadedScreen is null ? !File.Exists(file) : loadedScreen.IsSourceFileMissing;
            screens.Add(new GumScreen(listed.Name, file, missing));
            if (missing)
                findings.Add(words.Finding(new ErrorResult { Code = "GUM0004", ElementName = listed.Name, Severity = ErrorSeverity.Error }, full));
        }
        return new GumProject(full, screens, findings);
    }

    /// <summary>Why Gum's loader couldn't read a project, in words for a sentence ("because ...").</summary>
    internal static string Why(string full, string? error) =>
        !File.Exists(full) ? "the file isn't there"
        : error == GumLibrary.NotAllowed ? GumLibrary.NotAllowed
        : GumMessages.IsConflictMarkers(error) ? "it still has unfinished merge marks (<<<<<<<) from version control"
        : GumLibrary.Unreadable;

    /// <summary>The full path of the file Gum reads the screen, component or built-in type <paramref name="name"/> of
    /// <paramref name="project"/> from, or null when the project doesn't list it.</summary>
    internal static string? ElementFile(string folder, GumProjectSave project, string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var json = GumProjectSave.IsJsonFormat(project.FullFileName ?? "");
        foreach (var (references, type) in new[] { (project.ScreenReferences, ElementType.Screen), (project.ComponentReferences, ElementType.Component), (project.StandardElementReferences, ElementType.Standard) })
            foreach (var reference in references)
                if (string.Equals(reference.Name, name, StringComparison.OrdinalIgnoreCase)) return ElementFile(folder, reference, type, json);
        return null;
    }

    /// <summary>Where Gum looks for a listed screen, component or built-in type: its linked file when that exists (Gum
    /// prefers it), otherwise <c>Screens/Name.gusx</c> and the like (BORROWED: ElementReference.ToElementSave, with Gum's
    /// own Subfolder and GetExtension worked out on a copy, so Gum's loaded project isn't changed).</summary>
    internal static string ElementFile(string folder, ElementReference listed, ElementType type, bool json)
    {
        if (!string.IsNullOrWhiteSpace(listed.Link))
        {
            var linked = Path.GetFullPath(Path.Combine(folder, listed.Link.Replace('\\', '/')));
            if (File.Exists(linked)) return linked;
        }
        var typed = new ElementReference { Name = listed.Name, ElementType = type };
        return Path.GetFullPath(Path.Combine(folder, typed.Subfolder, listed.Name + "." + typed.GetExtension(json)));
    }

    internal static string Relative(string folder, string file) => Path.GetRelativePath(folder, file).Replace('\\', '/');

    internal static CheckFinding Finding(Severity severity, string file, string? item, string message, string next) =>
        new(new Issue(severity, message, item), new CheckPlace(file, item), next);
}
