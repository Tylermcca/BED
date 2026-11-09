using Brutal.ImGuiApi;
using ImCode.Text;

namespace ImCode;

/// <summary>Clipboard through the ImGui platform backend.</summary>
public sealed class ImGuiClipboard : IClipboard
{
    public string GetText() => ImGui.GetClipboardText().ToString() ?? "";
    public void SetText(string text) => ImGui.SetClipboardText(text);
}
