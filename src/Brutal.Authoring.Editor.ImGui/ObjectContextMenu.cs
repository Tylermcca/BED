using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Objects-row context menu using the existing selection and inspector removal authority.</summary>
public static class ObjectContextMenu
{
    /// <summary>Draw after the object's selectable. Returns an action result for the host status line, or null.</summary>
    public static string? Draw<TDoc>(EditorWindow<TDoc> window, string id) where TDoc : class
    {
        var editor = window.Editor;
        var io = ImGui.GetIO();
        var requested = ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)
            || ImGui.IsItemFocused() && !io.WantTextInput && !io.KeyCtrl && !io.KeyAlt && !io.KeySuper
                && (ImGui.IsKeyPressed(ImGuiKey.Menu, false) || io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.F10, false));
        var popup = "Object actions##" + id;
        if (requested) { editor.Select(id); ImGui.OpenPopup(popup); }
        if (!Ui.BeginPopup(popup)) return null;
        string? result = null;
        var missing = editor.Outline.Label(editor.Session.Current, id) is null ? "This object no longer exists." : null;
        var revealReason = missing ?? (window.RevealInTable is null ? "This object view has no table-reveal action." : null);
        if (Ui.MenuItem("Show in table"u8, ""u8, false, revealReason is null, revealReason))
            result = window.RevealInTable!(id) ? "Shown in table." : "This object could not be shown in the table.";
        var copyReason = missing ?? (window.CopyObject is null ? "This object view has no object-copy action; table cell selection is separate." : null);
        if (Ui.MenuItem("Copy"u8, ""u8, false, copyReason is null, copyReason))
            result = window.CopyObject!(id) ? "Copied object row." : "This object has no copyable table row.";
        var why = editor.Outline.Label(editor.Session.Current, id) is null ? "This object no longer exists."
            : editor.Playing is not null ? "Stop Play before deleting an object."
            : !editor.InFocus(id) ? "This object is outside the current focus."
            : editor.Selection.Count != 1 || editor.Primary != id ? "Select this object again before deleting it."
            : window.Inspector?.CanRemoveHere != true ? "This object has no supported remove action." : null;
        if (Ui.MenuItem("Delete"u8, ""u8, false, why is null, why))
            result = window.Inspector!.RemoveHere() ? "Deleted object; Undo restores it."
                : string.Join(" ", window.Inspector.LastIssues.Select(issue => issue.Message));
        Ui.EndPopup();
        return result;
    }
}
