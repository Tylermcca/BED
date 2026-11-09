using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;

/// <summary>
/// Starts a frame the way BRUTAL's own demo does (<c>samples/Demo/DemoImGui.cs</c>). Text that isn't a <c>u8</c> literal
/// is copied into <see cref="ImString.SharedStorage"/>, a fixed 8 MiB buffer that only the program can empty. Without
/// this, each frame's text adds to the last until the buffer is full and drawing throws, so a program would crash after
/// running for a while. Every frame loop starts here. BORROWED: BRUTAL's demo.
/// </summary>
public static class ImGuiFrame
{
    /// <summary>Calls <c>ImGui.NewFrame</c> and recycles the last frame's text.</summary>
    public static void Begin()
    {
        Brutal.ImGuiApi.ImGui.NewFrame();
        ImString.SharedStorage.Reset();
        Ui.BeginFrame();
    }
}
