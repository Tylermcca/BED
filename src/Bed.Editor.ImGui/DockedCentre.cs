using Brutal.Numerics;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

/// <summary>The docked editor's centre view this frame: the area left over by the side and bottom docks.
/// Floating helper boxes (the welcome and tour boxes) sit inside it so they never cover Files, Used by or the other docks.</summary>
internal static class DockedCentre
{
    private static int frame=-1;
    private static float2 pos,size;
    internal static void Record(float2 at,float2 extent){frame=ImGui.GetFrameCount();pos=at;size=extent;}
    /// <summary>The centre area, when a docked editor drew one this frame.</summary>
    internal static bool TryGet(out float2 at,out float2 extent)
    {
        at=pos;extent=size;
        return frame==ImGui.GetFrameCount()&&extent.X>0&&extent.Y>0;
    }
}
