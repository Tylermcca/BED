using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>The explicit answer to one pending project transition.</summary>
public enum SaveChangesChoice
{
    /// <summary>Save all data drafts before continuing.</summary>
    Save,
    /// <summary>Continue without saving the current drafts.</summary>
    DontSave,
    /// <summary>Keep the current project and cancel the transition.</summary>
    Cancel
}

public sealed partial class ProjectLauncher
{
    private Func<bool>? pendingTransition;
    private bool decisionExecuting, focusSave, exitAccepted;

    private bool ConfirmBefore(Func<bool> continuation)
    {
        if (decisionExecuting) return true;
        if (Workspace?.Documents.HasSceneBindings == true)
            return Say(false, "Typed scenes need their own save/discard decision before replacing this project.");
        if (Workspace is not { } open || open.Project.Dirty.Count == 0) return true;
        if (pendingTransition is null) { pendingTransition=continuation;focusSave=true; }
        closeAnyway=true;
        return Say(false, $"{open.Project.Dirty.Count} file(s) have unsaved edits. Choose Save, Don't Save or Cancel.");
    }

    /// <summary>Whether the shared save decision is waiting for an answer.</summary>
    public bool SaveChangesPending => pendingTransition is not null;

    /// <summary>Resolves the pending transition. A failed save retains the project, draft and pending decision.</summary>
    public bool AnswerSaveChanges(SaveChangesChoice choice)
    {
        if (pendingTransition is not { } next) return Say(false,"No save decision is pending.");
        if (choice==SaveChangesChoice.Cancel)
        { pendingTransition=null;closeAnyway=false;return Say(false,"Cancelled; the project is still open."); }
        if (choice is not (SaveChangesChoice.Save or SaveChangesChoice.DontSave)) return Say(false,"Choose Save, Don't Save or Cancel.");
        if (Workspace?.Documents.HasSceneBindings == true) return Say(false,"Typed scenes need their own save/discard decision.");
        if (choice==SaveChangesChoice.Save && Workspace is { } open && !open.SaveAll())
            return Say(false,open.Said);
        pendingTransition=null;closeAnyway=false;
        decisionExecuting=true;
        try { return next(); }
        finally { decisionExecuting=false; }
    }

    /// <summary>EditorApp's cleared OS-close callback. Pending or cancelled decisions never authorize exit.</summary>
    public bool RequestWindowClose(bool requested)
    {
        if (exitAccepted) return true;
        if (!requested) return false;
        bool Exit()
        {
            if (Workspace is not null && !Close()) return false;
            return exitAccepted=true;
        }
        return ConfirmBefore(Exit) && Exit();
    }

    private void DrawCloseAnyway()
    {
        if (ImGui.Begin("Save changes?###launcher.close"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextWrapped($"Save changes to {(Current is { } current ? Name(current) : "this project") }?");
            if (Workspace is { } open)
                foreach (var file in open.Project.Dirty) ImGui.Text(file.Name);
            ImGui.TextWrapped(Said);
            if (focusSave) { ImGui.SetKeyboardFocusHere();focusSave=false; }
            if (Ui.Button("Save"u8)) AnswerSaveChanges(SaveChangesChoice.Save);
            ImGui.SameLine();
            if (Ui.Button("Don't Save"u8)) AnswerSaveChanges(SaveChangesChoice.DontSave);
            ImGui.SameLine();
            if (Ui.Button("Cancel"u8)) AnswerSaveChanges(SaveChangesChoice.Cancel);
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape,false))
                AnswerSaveChanges(SaveChangesChoice.Cancel);
        }
        ImGui.End();
    }
}
