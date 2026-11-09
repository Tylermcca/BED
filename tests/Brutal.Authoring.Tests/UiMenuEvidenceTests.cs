using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
using Xunit.Sdk;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiMenuEvidenceTests
{
    [Theory]
    [InlineData(1f, false)] [InlineData(1.5f, false)] [InlineData(2f, false)]
    [InlineData(1f, true)] [InlineData(1.5f, true)] [InlineData(2f, true)]
    public void Menu_label_layout_matches_native_glyph_submission_and_parent_clip(float scale, bool horizontal)
    {
        using var host = new HeadlessHost(1000, 700) { Drawing = false };
        Im.GetStyle().FontScaleMain = scale;
        UiBounds clip = default; float2 size = default; bool sameGlyphs = false;
        void Draw()
        {
            Im.SetNextWindowPos(new float2(20, 20)); Im.SetNextWindowSize(new float2(900, 600));
            Ui.Begin("Menu evidence", ImGuiWindowFlags.MenuBar);
            var list = Im.GetWindowDrawList(); var start = list.VtxBuffer.Count;
            Im.Text("Help"u8);
            var reference = Enumerable.Range(start, list.VtxBuffer.Count - start).Select(i => list.VtxBuffer[i]).ToArray();
            if (horizontal) Assert.True(Ui.BeginMenuBar());
            var a = list.GetClipRectMin(); var b = list.GetClipRectMax(); clip = new(a.X, a.Y, b.X-a.X, b.Y-a.Y);
            size = Im.CalcTextSize("Help"u8); start = list.VtxBuffer.Count;
            Assert.False(Ui.BeginMenu("Help##target", false));
            sameGlyphs = false;
            for (var i = start; reference.Length > 0 && i + reference.Length <= list.VtxBuffer.Count; i++)
            {
                var delta = list.VtxBuffer[i].pos - reference[0].pos;
                var same = true;
                for (var n = 0; n < reference.Length; n++)
                    if (list.VtxBuffer[i+n].uv != reference[n].uv
                        || MathF.Abs(list.VtxBuffer[i+n].pos.X - reference[n].pos.X - delta.X) > 0.01f
                        || MathF.Abs(list.VtxBuffer[i+n].pos.Y - reference[n].pos.Y - delta.Y) > 0.01f) { same = false; break; }
                if (same) { sameGlyphs = true; break; }
            }
            if (horizontal) Ui.EndMenuBar();
            Ui.End();
        }
        host.Frames(Draw, 4);
        var menu = new UiTreeAssertionsA448(Ui.Snapshot()).Find(UiRole.Menu, "Help");
        Assert.False(menu.Enabled); Assert.False(menu.Expanded);
        UiTreeAssertionsA448.FullyShown(menu, "Help");
        var evidence = menu.TextEvidence!.Value;
        Assert.Equal(clip, evidence.Clip); Assert.Equal(-1, evidence.WrapWidth);
        Assert.Equal(size.X, evidence.FullBounds.Width); Assert.Equal(size.Y, evidence.FullBounds.Height);
        Assert.True(sameGlyphs, "The target must submit the complete reference glyph sequence, with unchanged relative positions.");
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Narrow_actual_draw_clip_rejects_complete_menu_label(float scale)
    {
        using var host = new HeadlessHost(900, 650) { Drawing = false };
        Im.GetStyle().FontScaleMain = scale;
        void Draw()
        {
            Im.SetNextWindowPos(new float2(0, 0)); Im.SetNextWindowSize(new float2(800, 600));
            Ui.Begin("Clipped menu"); var pos = Im.GetCursorScreenPos();
            Im.PushClipRect(pos, pos + new float2(12, 100), true);
            Assert.False(Ui.BeginMenu("A complete label##clip", false));
            Im.PopClipRect(); Ui.End();
        }
        host.Frames(Draw, 4);
        var menu = new UiTreeAssertionsA448(Ui.Snapshot()).Find(UiRole.Menu, "A complete label");
        Assert.Equal(12, menu.TextEvidence!.Value.Clip.Width);
        Assert.True(menu.TextEvidence.Value.Truncated);
        Assert.True(menu.TextEvidence.Value.FullBounds.Width > 12);
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.FullyShown(menu, "A complete label"));
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Opening_a_popup_keeps_header_text_in_its_original_clip(float scale)
    {
        using var host = new HeadlessHost(1000, 700) { Drawing = false };
        Im.GetStyle().FontScaleMain = scale;
        UiBounds parentClip = default; UiBounds popupClip = default;
        void Draw()
        {
            Ui.Begin("Open menu", ImGuiWindowFlags.MenuBar);
            if (Ui.BeginMenuBar())
            {
                var a = Im.GetWindowDrawList().GetClipRectMin(); var b = Im.GetWindowDrawList().GetClipRectMax();
                parentClip = new(a.X, a.Y, b.X-a.X, b.Y-a.Y);
                if (Ui.BeginMenu("Help"))
                {
                    a = Im.GetWindowDrawList().GetClipRectMin(); b = Im.GetWindowDrawList().GetClipRectMax();
                    popupClip = new(a.X, a.Y, b.X-a.X, b.Y-a.Y);
                    Ui.Text("Popup body", "body"); Ui.EndMenu();
                }
                Ui.EndMenuBar();
            }
            Ui.End();
        }
        host.Frames(Draw, 4);
        var target = new UiTreeAssertionsA448(Ui.Snapshot()).Find(UiRole.Menu, "Help").Bounds;
        host.Click(Draw, target.X + target.Width/2, target.Y + target.Height/2);
        host.Frames(Draw, 3);
        var query = new UiTreeAssertionsA448(Ui.Snapshot()); var menu = query.Find(UiRole.Menu, "Help");
        Assert.True(menu.Expanded); UiTreeAssertionsA448.FullyShown(menu, "Help");
        Assert.Equal(parentClip, menu.TextEvidence!.Value.Clip);
        Assert.Equal(popupClip, query.Find(UiRole.Text, "Popup body").TextEvidence!.Value.Clip);
        Assert.NotEqual(parentClip, popupClip);
    }

    [Fact]
    public void Repeated_menu_append_does_not_claim_another_painted_label()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Ui.Begin("Repeated menu");
            Assert.False(Ui.BeginMenu("Help", false));
            Assert.False(Ui.BeginMenu("Help", false));
            Ui.End();
        }
        host.Frames(Draw, 3);
        var menus = Ui.Snapshot().Nodes.Where(n => n.Role == UiRole.Menu && n.Label == "Help").ToArray();
        Assert.Equal(2, menus.Length);
        Assert.NotNull(menus[0].TextEvidence); Assert.Null(menus[1].TextEvidence);
    }
}
