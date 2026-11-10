using Brutal.ImGuiApi;
using Im=Brutal.ImGuiApi.ImGui;
namespace Brutal.Authoring.Editor.ImGui;

public static partial class Ui
{
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
