using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

public sealed partial class ProjectLauncher
{
    private string? replacementFolder;
    private DataWorkspace? replacementOwner;

    /// <summary>Stages an existing folder for confirmation; never opens or modifies it yet.</summary>
    public bool DropOnEditor(IReadOnlyList<string> paths)
    {
        if (Workspace is null) return Say(false,"Open a project before dropping a replacement folder on the editor.");
        if (SaveChangesPending || replacementFolder is not null)
            return Say(false,"Finish the open decision before dropping another folder.");
        if (paths is null || paths.Count!=1 || string.IsNullOrWhiteSpace(paths[0]) || !Directory.Exists(paths[0]))
            return Say(false,"Drop one existing project folder on the editor. Drop JSON files on Files instead.");
        replacementFolder=paths[0];replacementOwner=Workspace;
        return Say(true,$"Open {replacementFolder} instead?");
    }

    /// <summary>Answers folder confirmation; acceptance enters the existing save/replacement route.</summary>
    public bool AnswerFolderReplacement(bool open)
    {
        if (replacementFolder is not { } folder) return Say(false,"No replacement folder is pending.");
        var owner=replacementOwner;replacementFolder=null;replacementOwner=null;
        if (!open) return Say(false,"Cancelled; the project is still open.");
        if (!ReferenceEquals(owner,Workspace)) return Say(false,"The project changed. Drop the folder again.");
        return OpenFolder(folder);
    }

    private void DrawFolderReplacement()
    {
        if (replacementFolder is not { } folder) return;
        if (!ReferenceEquals(replacementOwner,Workspace)) { replacementFolder=null;replacementOwner=null;return; }
        if (Ui.Begin("Open folder instead?###launcher.folder-drop"u8,ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            Ui.TextWrapped($"Open {folder} instead?");
            if (Ui.Button("Open instead"u8)) AnswerFolderReplacement(true);
            ImGui.SameLine();
            if (Ui.Button("Cancel"u8)) AnswerFolderReplacement(false);
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape,false))
                AnswerFolderReplacement(false);
        }
        Ui.End();
    }
}
