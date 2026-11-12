using Gum.DataTypes;
using Gum.Managers;
using Gum.ProjectServices;

namespace Bed.Editor.Gum;

// GUM-LIST/GUM-FILES: Gum's own project loader (Gum.ProjectServices.ProjectLoader, the one behind gumcli check) does the
// reading, and keeps its load errors. Gum keeps tool-wide shared state (ObjectFinder.Self, StandardElementsManager.Self,
// FileManager), so every BED call into Gum runs under one lock and clears ObjectFinder's project afterwards.
// BORROWED: the loader and its errors; INVENTED: the lock.

/// <summary>The one way into Gum's library: loads a project read-only, one call at a time.</summary>
internal static class GumLibrary
{
    private static readonly object Gate = new();

    /// <summary>Runs <paramref name="work"/> while no other Gum call is running.</summary>
    public static T Run<T>(Func<T> work)
    {
        lock (Gate) { Uses++; return work(); }
    }

    /// <summary>How many Gum calls have run, counting the one running now. A long-lived Gum user (the Gum screen picture)
    /// compares it with its own last call to tell whether another call has changed Gum's shared state since. Read it
    /// inside <see cref="Run{T}"/>.</summary>
    internal static long Uses { get; private set; }

    /// <summary>Loads <paramref name="projectFile"/> with Gum's own loader and points Gum's shared lookup at it. Call
    /// inside <see cref="Run{T}"/>. Never writes. A file BED isn't allowed to open gives a result with an error message
    /// rather than an exception.</summary>
    public static ProjectLoadResult Load(string projectFile)
    {
        // ProjectLoader does what BED did by hand before: StandardElementsManager.Self.Initialize() (ProjectLoader.cs:36) and
        // project.Initialize() (ProjectLoader.cs:58). Its Initialize doesn't tolerate a missing default state, as gumcli check
        // doesn't; such a project fails to load and is reported as one BED couldn't read.
        ProjectLoadResult result;
        try { result = new ProjectLoader().Load(projectFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // ProjectLoader reads the project file once before its own error handling starts.
            return new ProjectLoadResult { ErrorMessage = NotAllowed };
        }
        catch (Exception)
        {
            // Gum's readers throw several kinds of error for a damaged file; each means BED couldn't read it.
            return new ProjectLoadResult { ErrorMessage = Unreadable };
        }
        if (result.Project is { } project) ObjectFinder.Self.GumProjectSave = project;
        return result;
    }

    /// <summary>The reason given when Windows refused the file.</summary>
    public const string NotAllowed = "BED wasn't allowed to open the file, or another program is holding it";

    /// <summary>The reason given when Gum's loader stopped on a damaged file.</summary>
    public const string Unreadable = "it isn't laid out the way Gum saves a project";

    /// <summary>Clears Gum's shared lookup so no project stays held after a call.</summary>
    public static void Release() => ObjectFinder.Self.GumProjectSave = null;

    /// <summary>True for a project saved by a newer Gum than the library BED uses, or one with no version Gum knows.
    /// Gum's own tool refuses these too (ProjectManager), so BED does the same rather than guess.</summary>
    public static bool Unsupported(GumProjectSave project) =>
        project.Version > GumProjectSave.NativeVersion || project.Version < (int)GumProjectSave.GumxVersions.InitialVersion;
}
