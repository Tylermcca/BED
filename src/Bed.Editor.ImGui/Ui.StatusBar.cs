using Brutal.ImGuiApi;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Editor.ImGui;

public static partial class Ui
{
    internal static bool BeginPlaybackBar(float height)
    {
        var shown=Im.Internal.BeginViewportSideBar("Playback###workspace.playback"u8,Im.GetMainViewport(),ImGuiDir.Up,height,
            ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoDocking|
            ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoFocusOnAppearing|ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse);
        Window(UiRole.Window,"Playback###workspace.playback"u8);return shown;
    }
    // Keep the existing Begin/End and semantic-parent contract for this internal host-side sidebar.
    internal static bool BeginWorkspaceStatusBar(float height)
    {
        var shown=Im.Internal.BeginViewportSideBar("Status bar###workspace.status"u8,Im.GetMainViewport(),ImGuiDir.Down,height,
            ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoDocking|
            ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoFocusOnAppearing|ImGuiWindowFlags.NoNavFocus|
            ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse);
        Window(UiRole.Window,"Status bar###workspace.status"u8);return shown;
    }
}
