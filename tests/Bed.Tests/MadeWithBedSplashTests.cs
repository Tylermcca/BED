using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

// The "Made with BED" launch screen: a white page with the mark, white letters on a blue box, centred, coming up out of black
// and going back to it with the BRUTAL logo's timing, finished by its time, and skippable by a click or key only after the first second.
// BED_MADEWITH_PNG=<folder> saves a picture of it.
[Collection(ImGuiCollection.Name)]
public sealed class MadeWithBedSplashTests
{
    private const uint PageWhite = 0xffffffff;

    private static void At(HeadlessHost h, MadeWithBedSplash s, float delta) { ImGui.GetIO().DeltaTime = delta; h.Frame(() => s.Draw()); }

    // The pixels that aren't the white page.
    private static (int X, int Y)[] Marked(HeadlessHost h) =>
        h.Pixels.Select((p, i) => (p, i)).Where(v => (v.p & 0xffffff) != 0xffffff).Select(v => (X: v.i % h.Width, Y: v.i / h.Width)).ToArray();

    [Fact]
    public void Draws_the_blue_box_centred_on_white()
    {
        using var h = new HeadlessHost(1280, 800) { Background = 0xff000000 };
        var s = new MadeWithBedSplash();
        At(h, s, 1f / 60);
        Assert.All(h.Pixels, p => Assert.Equal(0xff000000u, p));   // the first frame is black, as the BRUTAL logo left it: it fades in
        At(h, s, 1f);
        var lit = Marked(h);
        Assert.True(lit.Length > 50000);
        Assert.InRange((lit.Min(v => v.X) + lit.Max(v => v.X)) / 2f, 620, 660);
        Assert.InRange((lit.Min(v => v.Y) + lit.Max(v => v.Y)) / 2f, 380, 420);
        Assert.True(lit.Max(v => v.X) - lit.Min(v => v.X) >= 1280 / 3, "The mark is prominent, sized by the BRUTAL logo's rule.");
        Assert.Equal(PageWhite, h.Pixels[0]);
        var middle = h.Pixels[400 * 1280 + (lit.Min(v => v.X) + 12)] & 0xffffff;
        Assert.Equal(0xB26F1Fu, middle);   // the box's own blue (ABGR) inside its left edge
        Assert.False(s.Done);
    }

    [Fact]
    public void Finishes_by_its_time_like_the_BRUTAL_logo()
    {
        Assert.Equal(BrutalSplash.Seconds, MadeWithBedSplash.Seconds);
        Assert.Equal(BrutalSplash.SkippableAfter, MadeWithBedSplash.SkippableAfter);
        using var h = new HeadlessHost(720, 800) { Drawing = false };
        ImGui.GetIO().DeltaTime = .1f;
        var s = new MadeWithBedSplash();
        var drawing = false;
        void Draw() => drawing = s.Draw();
        h.Frames(Draw, 23); Assert.True(drawing); Assert.False(s.Done);
        h.Frames(Draw, 5); Assert.False(drawing); Assert.True(s.Done);
    }

    [Theory] [InlineData(ImGuiKey.Escape)] [InlineData(ImGuiKey.Space)]
    public void A_key_skips_it_after_the_first_second_only(ImGuiKey key)
    {
        using var h = new HeadlessHost(720, 800) { Drawing = false };
        var s = new MadeWithBedSplash();
        At(h, s, .01f);
        h.Press(() => s.Draw(), key); Assert.False(s.Done);
        At(h, s, 1.01f);
        ImGui.GetIO().DeltaTime = .01f; h.Press(() => s.Draw(), key); Assert.True(s.Done);
    }

    [Fact]
    public void A_click_skips_it_after_the_first_second()
    {
        using var h = new HeadlessHost(720, 800) { Drawing = false };
        var s = new MadeWithBedSplash();
        At(h, s, .01f); At(h, s, 1.01f);
        ImGui.GetIO().DeltaTime = .01f; h.Click(() => s.Draw(), 100, 100); Assert.True(s.Done);
    }

    [Fact]
    public void It_draws_whole_and_inside_a_small_window()
    {
        var folder = Environment.GetEnvironmentVariable("BED_MADEWITH_PNG");
        foreach (var (w, hgt) in new[] { (1280, 720), (320, 240) })
        {
            using var h = new HeadlessHost(w, hgt) { Background = 0xff000000 };
            var s = new MadeWithBedSplash();
            At(h, s, 1f / 60); At(h, s, 1f);
            var lit = Marked(h);
            Assert.True(lit.Length > w * hgt / 20, $"Too little is drawn at {w}x{hgt}.");
            Assert.True(lit.Min(v => v.X) > 0 && lit.Min(v => v.Y) > 0 && lit.Max(v => v.X) < w - 1 && lit.Max(v => v.Y) < hgt - 1, $"It's clipped at {w}x{hgt}.");
            if (folder is not null && w == 1280) { Directory.CreateDirectory(folder); h.Save(Path.Combine(folder, "madewith.png")); }
        }
    }

    [Fact]
    public void A_game_can_draw_behind_and_over_the_box_and_the_box_stays_on_top_of_what_is_behind()
    {
        using var h = new HeadlessHost(1280, 800) { Background = 0xff000000 };
        var calls = new List<(string Layer, MadeWithBedSplash.Place Place)>();
        var s = new MadeWithBedSplash
        {
            // Behind: a red block a little bigger than the whole box; in front: nothing drawn, only noted.
            Behind = p => { calls.Add(("behind", p)); ImGui.GetWindowDrawList().AddRectFilled(p.MarkMin - new Brutal.Numerics.float2(20, 20), p.MarkMax + new Brutal.Numerics.float2(20, 20), new ImColor8(255, 0, 0, 255)); },
            InFront = p => calls.Add(("front", p)),
        };
        At(h, s, 1f / 60); calls.Clear();
        At(h, s, 1f);
        Assert.Equal(["behind", "front"], calls.Select(c => c.Layer));
        var place = calls[0].Place;
        Assert.InRange(place.Seconds, 0.9f, 1.1f);
        Assert.Equal(1f, place.Fade);
        Assert.True(place.WordMin.X > place.MarkMin.X + 10 && place.WordMax.X < place.MarkMax.X - 10 && place.WordMin.Y > place.MarkMin.Y + 10 && place.WordMax.Y < place.MarkMax.Y - 10,
            "The word sits inside the box.");
        // The box covers what's behind it: inside it there's no red, only its blue and its letters; round it the red shows.
        int red = 0, outside = 0;
        for (var y = (int)place.MarkMin.Y + 12; y < (int)place.MarkMax.Y - 12; y++)
            for (var x = (int)place.MarkMin.X + 12; x < (int)place.MarkMax.X - 12; x++)
                if ((h.Pixels[y * h.Width + x] & 0xffffff) == 0x0000ff) red++;
        Assert.Equal(0, red);
        for (var x = (int)place.MarkMin.X - 15; x < (int)place.MarkMax.X + 15; x++)
            if ((h.Pixels[((int)place.MarkMin.Y - 10) * h.Width + x] & 0xffffff) == 0x0000ff) outside++;
        Assert.True(outside > 100, "What's behind shows round the box.");
        int white = 0;
        for (var y = (int)place.WordMin.Y + 2; y < (int)place.WordMax.Y - 2; y++)
            for (var x = (int)place.WordMin.X + 2; x < (int)place.WordMax.X - 2; x++)
                if ((h.Pixels[y * h.Width + x] & 0xffffff) == 0xffffff) white++;
        Assert.True(white > 2000, $"The word's white letters show on the box ({white} white pixels).");
    }
}
