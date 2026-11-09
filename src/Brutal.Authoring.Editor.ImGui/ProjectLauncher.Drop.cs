using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;

public sealed partial class ProjectLauncher
{
    /// <summary>Handles a folder drop resolved by the host to the empty start window.
    /// The host must use the last completed window bounds to choose that target.
    /// Returns true only when the existing Open project route opened a workspace;
    /// an unrecognised folder instead shows the same editable manual-choice step.
    /// Refuses any existing workspace, including clean ones: replacement needs the normal save decision.
    /// No source file is copied, moved or deleted, and no folder dialog is opened.</summary>
    public bool DropOnStart(IReadOnlyList<string> paths)
    {
        if (Workspace is not null)
            return Say(false, "A project is already open. Close it before dropping a folder on the start window.");
        if (paths is null || paths.Count != 1 || string.IsNullOrWhiteSpace(paths[0]))
            return Say(false, "Drop one project folder on the start window.");
        var folder = paths[0];
        if (!Directory.Exists(folder))
            return Say(false, File.Exists(folder)
                ? "The start window opens folders, not files. Drop one project folder."
                : "That project folder wasn't found. Drop an existing folder.");
        return UseChosenProjectFolder(folder);
    }

    // The native folder picker and an empty-start drop both enter exactly the existing recognition/manual flow.
    private bool UseChosenProjectFolder(string chosen)
    {
        startPage = StartPage.Open;
        focusOpenFolder = true;
        projectFolder = new ImInputString(2048, chosen);
        if (OpenFolder(chosen)) { startPage = StartPage.Home; return true; }
        if (ProjectPaths.In(chosen) is null) ChooseDataFolder(chosen);
        return false;
    }
}
