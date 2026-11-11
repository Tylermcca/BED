using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// The keys the map and the 3D viewport share while they have the keyboard focus (found by A217: both were mouse-only).
/// Read once a frame by a focused canvas. INVENTED.
/// </summary>
internal static class CanvasKeys
{
    /// <summary>The arrow pressed this frame (with repeat), as a step: right and up are positive; (0, 0) for none.</summary>
    public static (int X, int Y) Arrow() =>
        ImGui.IsKeyPressed(ImGuiKey.LeftArrow) ? (-1, 0) : ImGui.IsKeyPressed(ImGuiKey.RightArrow) ? (1, 0)
        : ImGui.IsKeyPressed(ImGuiKey.UpArrow) ? (0, 1) : ImGui.IsKeyPressed(ImGuiKey.DownArrow) ? (0, -1) : (0, 0);

    /// <summary>+1 for + (zoom in), -1 for - (zoom out), 0 for neither; the main keys or the keypad's.</summary>
    public static int Zoom() =>
        ImGui.IsKeyPressed(ImGuiKey.Equal) || ImGui.IsKeyPressed(ImGuiKey.KeypadAdd) ? 1
        : ImGui.IsKeyPressed(ImGuiKey.Minus) || ImGui.IsKeyPressed(ImGuiKey.KeypadSubtract) ? -1 : 0;

    /// <summary>Map-only: +1 for Enter (next marker), -1 for Shift+Enter (previous), 0 for neither.</summary>
    public static int Pick() =>
        ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false) ? (ImGui.GetIO().KeyShift ? -1 : 1) : 0;

    /// <summary>The index <paramref name="step"/> on from <paramref name="at"/> among <paramref name="count"/>, round the
    /// ends; from nothing (<paramref name="at"/> below 0), the first going forward and the last going back.</summary>
    public static int Next(int at, int step, int count) =>
        at < 0 ? (step > 0 ? 0 : count - 1) : ((at + step) % count + count) % count;
}
