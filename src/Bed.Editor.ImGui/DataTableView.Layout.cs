using System.Security.Cryptography;
using System.Text;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

public sealed partial class DataTableView
{
    private readonly string unsavedIdentity = Guid.NewGuid().ToString("N");
    private string? layoutFile, layoutList, layoutId;
    private string[] layoutColumns = [];
    private bool restoreTableFocus;

    // Reuse the app's bounded per-user layout.ini store; never write project data for view state.
    // The schema key deliberately starts afresh when columns change, so native index-based settings cannot drift.
    private string TableIdentity(string list, IReadOnlyList<InspectorRow> columns)
    {
        var file = Editor.Session.FilePath ?? unsavedIdentity;
        if (OperatingSystem.IsWindows()) file = file.ToUpperInvariant();
        if (layoutId is not null && file == layoutFile && list == layoutList && columns.Count == layoutColumns.Length
            && columns.Select(c => c.Id).SequenceEqual(layoutColumns)) return layoutId;
        if (layoutId is not null)
        {
            // A different exact layout key owns its own native sort; do not carry the last list's model sort into it.
            Sort = null;
            var previous = ImGui.Internal.TableFindByID(ImGui.GetID(layoutId));
            restoreTableFocus = !previous.Equals(default(Brutal.ImGuiApi.Internal.ImGuiTablePtr))
                && ImGui.GetCurrentContext().NavWindow.Equals(previous.InnerWindow);
        }
        layoutFile = file; layoutList = list; layoutColumns = columns.Select(c => c.Id).ToArray();
        var key = System.Text.Json.JsonSerializer.Serialize(new[] { file, list }.Concat(layoutColumns));
        layoutId = "##data-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        sortSeen = false;
        return layoutId;
    }

    private void RestoreTableFocus()
    {
        if (!restoreTableFocus) return;
        restoreTableFocus = false;
        // Switching lists creates a distinct native scrolling child. Carry its keyboard ownership across,
        // so Escape can still leave the table and reach the existing Back/filter controls.
        ImGui.Internal.FocusWindow(ImGui.Internal.GetCurrentTable().InnerWindow);
    }

    private void TableHeaders(IReadOnlyList<InspectorRow> columns)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var i = 0; i <= columns.Count; i++)
        {
            if (!ImGui.TableSetColumnIndex(i)) continue;
            ImGui.PushID(i);
            if (i == 0) ImGui.TableHeader("Item"u8);
            else ImGui.TableHeader(columns[i - 1].Label);
            var io = ImGui.GetIO();
            if (ImGui.IsItemFocused() && !io.WantTextInput && !io.KeyCtrl && !io.KeyAlt && !io.KeySuper
                && (ImGui.IsKeyPressed(ImGuiKey.Menu, false) || io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.F10, false)))
                ImGui.Internal.TableOpenContextMenu(i);
            ImGui.PopID();
        }
        var table = ImGui.Internal.GetCurrentTable();
        // Append to ImGui's own header menu using its existing ID scope.
        ImGui.Internal.PushOverrideID(table.ID);
        if (Ui.BeginPopup("##ContextMenu"u8))
        {
            var scope = Ui.Tree.Count - 1;
            Ui.Tree.Update(scope, Ui.Tree.At(scope) with { Label = "Table column actions" });
            if (Ui.MenuItem("Reset table layout"u8))
            {
                table.IsResetAllRequest = true;
                Sort = null; sortSeen = false;
                Said = "Reset table widths, column order and sort.";
            }
            Ui.EndPopup();
        }
        ImGui.PopID();
    }
}
