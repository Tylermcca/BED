using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The editor's style meets WCAG 2.2 AA where A058's audit measured it falling short: text on every button, header
/// and tab state at 4.5:1 (1.4.3), and frame boundaries at 3:1 (1.4.11), measured from the style and from a render.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class EditorStyleTests
{
    // WCAG 2.2's relative luminance and contrast ratio.
    private static double Luminance(float4 c)
    {
        static double Channel(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(c.X) + 0.7152 * Channel(c.Y) + 0.0722 * Channel(c.Z);
    }

    private static double Contrast(float4 a, float4 b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // A colour as drawn over the opaque background behind it.
    private static float4 Over(float4 c, float4 back) =>
        new(c.X * c.W + back.X * (1 - c.W), c.Y * c.W + back.Y * (1 - c.W), c.Z * c.W + back.Z * (1 - c.W), 1);

    private static float4 Colour(ImGuiCol col) => ImGui.GetStyle().Colors[(int)col];

    [Theory]
    [InlineData(ImGuiCol.Button)]
    [InlineData(ImGuiCol.ButtonHovered)]
    [InlineData(ImGuiCol.ButtonActive)]
    [InlineData(ImGuiCol.Header)]
    [InlineData(ImGuiCol.HeaderHovered)]
    [InlineData(ImGuiCol.HeaderActive)]
    [InlineData(ImGuiCol.Tab)]
    [InlineData(ImGuiCol.TabHovered)]
    [InlineData(ImGuiCol.TabSelected)]
    [InlineData(ImGuiCol.FrameBg)]
    [InlineData(ImGuiCol.FrameBgHovered)]
    [InlineData(ImGuiCol.FrameBgActive)]
    public void TextOnEveryControlStateIsAtLeastFourAndAHalfToOne(ImGuiCol fill)
    {
        using var host = new HeadlessHost(64, 64);
        var back = Over(Colour(ImGuiCol.WindowBg), new float4(0, 0, 0, 1));
        var ratio = Contrast(Colour(ImGuiCol.Text), Over(Colour(fill), back));
        Assert.True(ratio >= 4.5, $"{fill}: {ratio:F2}:1");
    }

    [Theory]
    [InlineData(ImGuiCol.WindowBg)]
    [InlineData(ImGuiCol.FrameBg)]
    public void FrameBoundariesAreAtLeastThreeToOne(ImGuiCol against)
    {
        using var host = new HeadlessHost(64, 64);
        var window = Over(Colour(ImGuiCol.WindowBg), new float4(0, 0, 0, 1));
        var back = against == ImGuiCol.WindowBg ? window : Over(Colour(against), window);
        Assert.True(ImGui.GetStyle().FrameBorderSize >= 1, "frames need a drawn boundary");
        var ratio = Contrast(Over(Colour(ImGuiCol.Border), window), back);
        Assert.True(ratio >= 3, $"border against {against}: {ratio:F2}:1");
    }

    [Fact]
    public void AnUncheckedBoxShowsItsBoundaryInTheRender()
    {
        using var host = new HeadlessHost(200, 100);
        var (min, max) = (default(float2), default(float2));
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0));
            ImGui.SetNextWindowSize(new float2(200, 100));
            ImGui.Begin("style");
            var off = false;
            ImGui.Checkbox("Unchecked", ref off);
            min = ImGui.GetItemRectMin();
            ImGui.End();
        }
        host.Frames(Draw);
        var border = Colour(ImGuiCol.Border);
        var side = (int)ImGui.GetFrameHeight();
        bool Near(uint p, float v, int shift) => Math.Abs((int)((p >> shift) & 0xFF) - v * 255) <= 24;
        var hits = 0;
        for (var y = (int)min.Y; y < (int)min.Y + side; y++)
            for (var x = (int)min.X; x < (int)min.X + side; x++)
            {
                var p = host.Pixels[y * host.Width + x];
                if (Near(p, border.X, 0) && Near(p, border.Y, 8) && Near(p, border.Z, 16)) hits++;
            }
        // A drawn square outline is about four sides' worth of pixels; without one there are none (A058 measured 1.41:1).
        Assert.True(hits >= 2 * side, $"{hits} boundary pixels around a {side}px box");
    }
}
