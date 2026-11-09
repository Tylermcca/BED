using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DpiTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    [InlineData(2.5f)]
    public void Display_scale_multiplies_user_text_size_and_bakes_at_the_effective_size(float scale)
    {
        using var host = new HeadlessHost { Drawing = false };
        EditorWindow<Toy.Board>.Configure(ImGui.GetIO());
        Assert.True(EditorWindow<Toy.Board>.SetTextScale(1.5f));
        var dpi = new DpiScale();
        Assert.Equal(scale != 1, dpi.Apply(scale));
        float font = 0, baked = 0;
        void Draw()
        {
            ImGui.Begin("DPI font proof"u8);
            ImGui.Text("Open project"u8);
            font = ImGui.GetFontSize(); baked = ImGui.GetFontBaked().Size;
            ImGui.End();
        }
        host.Frames(Draw, 4);
        Assert.Equal(1.5f, EditorWindow<Toy.Board>.TextScale);
        Assert.Equal(scale, ImGui.GetStyle().FontScaleDpi);
        Assert.InRange(font, 13 * 1.5f * scale - 1, 13 * 1.5f * scale + 1);
        Assert.InRange(baked, font - 1, font + 1);
    }

    [Fact]
    public void Repeated_monitor_changes_use_original_metrics_and_keep_theme_and_user_settings()
    {
        using var host = new HeadlessHost { Drawing = false };
        var style = ImGui.GetStyle(); var padding = style.FramePadding;
        var dpi = new DpiScale();
        Assert.True(dpi.Apply(1.25f)); var first = style.FramePadding;
        Assert.True(dpi.Apply(2.5f));
        style.Colors[(int)ImGuiCol.Text] = new float4(.1f, .2f, .3f, 1);
        style.FontScaleMain = 1.75f;
        style.Alpha = .9f;
        Assert.True(dpi.Apply(1.25f));
        Assert.Equal(first, style.FramePadding);
        Assert.Equal(new float4(.1f, .2f, .3f, 1), style.Colors[(int)ImGuiCol.Text]);
        Assert.Equal(1.75f, style.FontScaleMain);
        Assert.Equal(.9f, style.Alpha);
        Assert.True(dpi.Apply(1)); Assert.Equal(padding, style.FramePadding);
        Assert.False(dpi.Apply(1));
        Assert.False(dpi.Apply(float.NaN)); Assert.False(dpi.Apply(0)); Assert.False(dpi.Apply(float.PositiveInfinity));
        Assert.Equal(1, dpi.Current);
    }

    [Theory]
    [InlineData(1.25f, false)]
    [InlineData(1.5f, false)]
    [InlineData(2.5f, false)]
    [InlineData(1.25f, true)]
    [InlineData(1.5f, true)]
    [InlineData(2.5f, true)]
    public void Complete_button_glyphs_match_unclipped_reference_not_just_node_bounds(float scale, bool deliberatelyClipped)
    {
        using var host = new HeadlessHost(1280, 800) { Drawing = false };
        var dpi = new DpiScale(); Assert.True(dpi.Apply(scale));
        bool complete = false;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0));
            ImGui.SetNextWindowSize(new float2(1000, 600));
            ImGui.Begin("DPI layout proof"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
            var list = ImGui.GetWindowDrawList();
            var first = list.VtxBuffer.Count;
            ImGui.Text("Open project"u8);
            var reference = Enumerable.Range(first, list.VtxBuffer.Count - first).Select(i => list.VtxBuffer[i]).ToArray();
            var start = list.VtxBuffer.Count;
            Ui.Button("Open project"u8, deliberatelyClipped ? new float2(40 * scale, 0) : null);
            var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
            var clipMin = list.GetClipRectMin(); var clipMax = list.GetClipRectMax();
            complete = false;
            for (var i = start; reference.Length > 0 && i + reference.Length <= list.VtxBuffer.Count; i++)
            {
                var same = true;
                for (var n = 0; n < reference.Length; n++)
                {
                    var vertex = list.VtxBuffer[i + n];
                    if (vertex.uv != reference[n].uv || vertex.pos.X < Math.Max(min.X, clipMin.X) || vertex.pos.X > Math.Min(max.X, clipMax.X)
                        || vertex.pos.Y < Math.Max(min.Y, clipMin.Y) || vertex.pos.Y > Math.Min(max.Y, clipMax.Y)) { same = false; break; }
                }
                if (same) { complete = true; break; }
            }
            ImGui.End();
        }
        host.Frames(Draw, 4);
        Assert.Equal(!deliberatelyClipped, complete);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "Open project" && n.Role == UiRole.Button);
    }
}
