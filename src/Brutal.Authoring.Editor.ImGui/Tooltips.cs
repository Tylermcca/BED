using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Noninteractive native tooltips following ImGui's configured standard hover delay.</summary>
public static class Tooltips
{
    /// <summary>Shows full descriptive text for the last native item after the standard delay, including disabled items; never handles input.</summary>
    public static bool Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        Ui.AnnotateNativeTooltip(text);
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.ForTooltip | ImGuiHoveredFlags.AllowWhenDisabled)) return false;
        ImGui.SetTooltip(text);
        return true;
    }
}
