using Bed.Editor;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.ImGui;

internal static class ChoiceLabelUi
{
    internal static bool Selectable(InspectorRow row, string value, string current)
    {
        if (row.ChoiceLabels is null) return Ui.Selectable(value, value == current);
        // Different values may have identical labels. Keep native and accessibility identities tied to the value.
        Im.PushID(value);
        try { return Ui.Selectable(row.ChoiceLabel(value) + "###choice", value == current); }
        finally { Im.PopID(); }
    }
}