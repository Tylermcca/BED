using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
// Personal view preference only; never changes document commands or global user shortcut overrides.
internal static class ViewportControls
{
    internal static bool Wer {get;set;}
    internal static bool Changed {get;set;}
    private static void Choose(bool wer){Wer=wer;Changed=true;}
    internal static string Note {get;set;} = "";
    internal static void DrawChoice()
    {
        if(Ui.BeginCombo("Viewport keymap"u8,Wer?"W/E/R":"BED defaults"))
        {
            if(Ui.Selectable("BED defaults"u8,!Wer))Choose(false);
            if(!Wer)Brutal.ImGuiApi.ImGui.SetItemDefaultFocus();
            if(Ui.Selectable("W/E/R"u8,Wer))Choose(true);
            if(Wer)Brutal.ImGuiApi.ImGui.SetItemDefaultFocus();
            Ui.EndCombo();
        }
        if(Note.Length>0)Ui.TextWrapped(Note);
        Ui.TextWrapped(Wer?"W moves, E rotates, R scales. Hold right mouse for camera flight.":"3D: G moves, R rotates, S scales. Flat: W moves, R rotates, S scales.");
    }
}
