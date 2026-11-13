using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

// Uses the existing local preference state; never edits a project document.
internal sealed class PreferencesWindow
{
    private bool open, focus, popupOpen;
    internal bool HasKeyboardFocus { get; private set; }
    internal void Open() { open = true; focus = true; }
    internal void Draw()
    {
        if (!open) { HasKeyboardFocus = false; return; }
        var vp=ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.Pos+vp.Size*.5f,ImGuiCond.FirstUseEver,new float2(.5f,.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(vp.Size.X-32,ImGui.GetFontSize()*30),MathF.Min(vp.Size.Y-64,ImGui.GetFontSize()*24)),ImGuiCond.FirstUseEver);
        if (focus) ImGui.SetNextWindowFocus();
        HasKeyboardFocus = false;
        if (Ui.Begin("Options###bed.preferences"u8, ref open, ImGuiWindowFlags.NoDocking))
        {
            if (focus) { ImGui.SetKeyboardFocusHere(); focus = false; }
            if (Ui.BeginCombo("Theme"u8, EditorTheme.Label(EditorTheme.Preference)))
            {
                foreach (var choice in Enum.GetValues<EditorThemePreference>())
                {
                    var selected = EditorTheme.Preference == choice;
                    if (Ui.Selectable(EditorTheme.Label(choice), selected)) EditorTheme.Preference = choice;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                Ui.EndCombo();
            }
            if (Ui.BeginCombo("UI scale"u8, $"{EditorWindow<DataDocument>.TextScale * 100:0}%"))
            {
                foreach (var percent in new[] { 100, 150, 200 })
                {
                    var selected = EditorWindow<DataDocument>.TextScale == percent / 100f;
                    if (Ui.Selectable($"{percent}%", selected)) EditorWindow<DataDocument>.SetTextScale(percent / 100f);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                Ui.EndCombo();
            }
            DrawCopyNames();
            ViewportControls.DrawChoice();
            Locations.Draw();
            HasKeyboardFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            if (Ui.Button("Close options"u8)) open = false;
            if (HasKeyboardFocus && !popupOpen && ImGui.IsKeyPressed(ImGuiKey.Escape, false)
                && !ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)) open = false;
        }
        popupOpen = ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);
        Ui.End();
    }
    internal static void DrawCopyNames()
    {
            if(Ui.BeginCombo("Names for copies"u8,CopyNames.Example(CopyNames.Style)))
            {
                foreach(var style in Enum.GetValues<CopyNameStyle>())
                    if(Ui.Selectable(CopyNames.Example(style),CopyNames.Style==style))CopyNames.Style=style;
                Ui.EndCombo();
            }
            Ui.TextWrapped($"Example: Cube becomes {CopyNames.Next("Cube",["Cube"])}");
    }

}
