using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A frame with more vertices than a 16-bit index can reach draws what it was asked to, as a real renderer does. Found on a
/// card table: past 65,535 the indices wrapped, and triangles joined the wrong points, in huge stray shapes.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class HeadlessLargeFrameTests
{
    [Fact]
    public void A_frame_past_sixty_five_thousand_vertices_draws_only_what_it_asked_for()
    {
        using var host = new HeadlessHost(400, 300) { Background = 0xFF000000 };
        var vertices = 0;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0));
            ImGui.SetNextWindowSize(new float2(400, 300));
            ImGui.Begin("many"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground);
            var list = ImGui.GetWindowDrawList();
            // Two groups at the far sides: a wrapped index joins points from both, right across the empty middle.
            for (var i = 0; i < 9000; i++)
            {
                var x = i % 2 == 0 ? 10 + i % 40 : 350 + i % 40;
                var y = 10 + i / 40;
                list.AddCircleFilled(new float2(x, y), 1.5f, new ImColor8(255, 255, 255, 255), 10);
            }
            vertices = list.VtxBuffer.Count;
            ImGui.End();
        }
        host.Frames(Draw, 3);
        Assert.True(vertices > 0);

        var strays = 0;
        for (var y = 0; y < host.Height; y++)
            for (var x = 100; x < 300; x++)
                if ((host.Pixels[y * host.Width + x] & 0x00FFFFFF) != 0) strays++;
        Assert.Equal(0, strays);
        Assert.True(host.Covered(0, 0, 100, 300) > 0);
        Assert.True(host.Covered(300, 0, 400, 300) > 0);
    }
}
