using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class TooltipTests
{
    [Theory]
    [InlineData(false, "Save", "Save (Ctrl+S)")]
    [InlineData(true, "Save", "Nothing selected")]
    [InlineData(false, "->", "Follow reference")]
    public void Native_standard_delay_applies_and_tooltip_does_not_steal_clicks(bool disabled, string label, string tooltip)
    {
        using var host = new HeadlessHost { Drawing = false };
        var shown = false; var clicks = 0; var center = float2.Zero;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(50, 50)); ImGui.SetNextWindowSize(new float2(400, 180));
            Ui.Begin("Tooltip proof"u8);
            Ui.BeginDisabled(disabled, "Nothing selected");
            if (Ui.Button(label)) clicks++;
            center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            var id = ImGui.GetItemID(); var bounds = ImGui.GetItemRectMin();
            shown = Tooltips.Show(tooltip);
            Assert.Equal(id, ImGui.GetItemID()); Assert.Equal(bounds, ImGui.GetItemRectMin());
            Ui.EndDisabled(); Ui.End();
        }
        host.Frames(Draw, 4);
        host.MoveMouse(Draw, center.X, center.Y);
        Assert.False(shown);
        var wait = (int)Math.Ceiling((ImGui.GetStyle().HoverDelayNormal + ImGui.GetStyle().HoverStationaryDelay + .2f) / ImGui.GetIO().DeltaTime);
        host.Frames(Draw, wait);
        Assert.True(shown);
        host.Click(Draw, center.X, center.Y);
        Assert.Equal(disabled ? 0 : 1, clicks);
        host.MoveMouse(Draw, 1000, 700); host.Frames(Draw, 2);
        Assert.False(shown);
    }
}
