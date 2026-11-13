using Bed.Data;

namespace Bed.Editor.Gum;

/// <summary>One screen listed in a Gum project. <paramref name="File"/> is the full path of its saved screen file;
/// <paramref name="Missing"/> is true when the project names it but the file isn't there.</summary>
public sealed record GumScreen(string Name, string File, bool Missing);

/// <summary>A Gum project as read from disk: its project file, its screens in the project's order, and anything that
/// couldn't be read, as BED's ordinary check findings (an error, a warning, or "worth knowing" for what BED couldn't
/// follow). Reading never changes a file. BORROWED: Gum's saved project layout (GumProjectSave).</summary>
public sealed record GumProject(string ProjectFile, IReadOnlyList<GumScreen> Screens, IReadOnlyList<CheckFinding> Findings)
{
    /// <summary>The folder the project file is in; Gum finds the files it names from here.</summary>
    public string Folder => Path.GetDirectoryName(Path.GetFullPath(ProjectFile))!;
}
