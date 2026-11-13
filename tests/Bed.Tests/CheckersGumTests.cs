using Bed.Editor.Gum;
using Checkers;
using CheckersPlay;
using Brutal.ImGuiApi.Internal;
using Xunit;

namespace Bed.Tests;

// Checkers' screens made in Gum (samples/CheckersProject/ui): Gum's own check passes them, BED's Gum check finds nothing,
// and the game draws them with live values. The copy next to the test program is the one the game uses.
[Collection(ImGuiCollection.Name)]   // Gum's drawing service is one per process
public sealed class CheckersGumTests
{
    private static string Project => CheckersScreen.Project() ?? throw new InvalidOperationException("The checkers Gum project wasn't copied next to the program.");

    [Fact]
    public void BEDs_Gum_check_finds_nothing_in_the_checkers_screens_for_a_game_that_draws_with_Skia()
    {
        var (project, check) = GumDependencyCheck.ReadAndCheck(Project, new HashSet<GumTextRoute> { GumTextRoute.Skia });
        Assert.Equal(["MatchPanel", "Title"], project.Screens.Select(s => s.Name).Order());
        Assert.NotNull(check);
        Assert.True(check.Project.Complete);
        Assert.Empty(check.Project.Findings);
        foreach (var (screen, report) in check.Screens)
        {
            Assert.True(report.Complete, screen);
            Assert.Empty(report.Findings);
        }
    }

    [Fact]
    public void The_match_strip_shows_what_the_game_sets_and_lists_its_parts_with_their_words()
    {
        using var picture = new GumScreenPicture(Project, 720, 200);
        Assert.True(picture.Show("MatchPanel"), picture.Problem);
        Assert.Equal("", picture.Problem);
        picture.Frame(0);
        var before = picture.Pixels.ToArray();
        Assert.True(picture.Set("TurnText.Text", "Light won in 40 moves."));
        Assert.True(picture.Frame(0.05), "setting the text changed nothing");
        Assert.False(before.AsSpan().SequenceEqual(picture.Pixels));
        var button = Assert.Single(picture.Parts(), p => p.Name == "NewGameButton");
        Assert.Equal(("New game", 200f, 52f), (button.Words, button.Width, button.Height));
        Assert.Equal("Light won in 40 moves.", Assert.Single(picture.Parts(), p => p.Name == "TurnText").Words);
        // The screen's own parts, including the ones it puts inside its own containers; not the label inside the button.
        Assert.Equal(["Bar", "Strip", "Row", "Info", "TurnText", "CountText", "NewGameButton"], picture.Parts().Select(p => p.Name));
        Assert.False(picture.Set("NoSuchPart.Text", "x"));
        // Its own calls don't make the picture load the screen again: the value is still there.
        Assert.False(picture.Frame(0.1));
    }

    [Fact]
    public void Started_on_its_own_the_game_shows_the_title_whose_button_the_keyboard_presses_and_from_a_position_it_goes_straight_to_the_match()
    {
        using (var titled = new Play(Game.Opening(), title: true))
        {
            Assert.True(titled.OnTitle);
            using var host = new Bed.Editor.ImGui.HeadlessHost(720, 800) { Drawing = false };
            Bed.Editor.ImGui.EditorWindow<Bed.Data.DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            host.Frames(titled.Draw, 3);
            // The Gum button is a button to the keyboard too: Tab reaches it by its words, and Space presses it.
            var id = Brutal.ImGuiApi.ImGui.Internal.FindWindowByName("Checkers###game").GetID("New game").Value;
            for (var i = 0; i < 20 && Brutal.ImGuiApi.ImGui.GetCurrentContext().NavId.Value != id; i++) host.Press(titled.Draw, Brutal.ImGuiApi.ImGuiKey.Tab);
            host.Press(titled.Draw, Brutal.ImGuiApi.ImGuiKey.Space);
            Assert.False(titled.OnTitle);
        }
        using var straight = new Play(Game.Opening());
        Assert.False(straight.OnTitle);
    }

    // The game in its window at a text size, after a few frames.
    private sealed class Window : IDisposable
    {
        public readonly Bed.Editor.ImGui.HeadlessHost Host = new(720, 800) { Drawing = false };
        public readonly Play Play;
        public Window(float scale, bool title = false)
        {
            Bed.Editor.ImGui.EditorWindow<Bed.Data.DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            Brutal.ImGuiApi.ImGui.GetStyle().FontScaleMain = scale;
            Play = new Play(Game.Opening(), title);
            Host.Frames(Play.Draw, 4);
        }
        public CheckersScreen Screen => (Play.OnTitle ? Play.TitleScreen : Play.Strip) ?? throw new InvalidOperationException("No Gum screen drawn.");
        public GumPart Part(string name) => Assert.Single(Screen.Parts(), p => p.Name == name);
        // Tabs to the button named by its words and presses it with Space.
        public void Press(string words)
        {
            var id = Brutal.ImGuiApi.ImGui.Internal.FindWindowByName("Checkers###game").GetID(words).Value;
            for (var i = 0; i < 40 && Brutal.ImGuiApi.ImGui.GetCurrentContext().NavId.Value != id; i++) Host.Press(Play.Draw, Brutal.ImGuiApi.ImGuiKey.Tab);
            Assert.Equal(id, Brutal.ImGuiApi.ImGui.GetCurrentContext().NavId.Value);
            Host.Press(Play.Draw, Brutal.ImGuiApi.ImGuiKey.Space);
        }
        public void Dispose() { Brutal.ImGuiApi.ImGui.GetStyle().FontScaleMain = 1; Play.Dispose(); Host.Dispose(); }
    }

    // The contrast between the commonest colour in a part (its background) and the colour that stands out most from it
    // that covers at least a few pixels (its words), measured from the picture Gum drew, as WCAG works it out.
    private static double Contrast(GumScreenPicture picture, GumPart part)
    {
        var counts = new Dictionary<(byte, byte, byte), int>();
        var pixels = picture.Pixels;
        for (var y = (int)part.Y; y < (int)(part.Y + part.Height) && y < picture.Height; y++)
            for (var x = (int)part.X; x < (int)(part.X + part.Width) && x < picture.Width; x++)
            {
                var i = (y * picture.Width + x) * 4;
                var colour = (pixels[i], pixels[i + 1], pixels[i + 2]);
                counts[colour] = counts.GetValueOrDefault(colour) + 1;
            }
        static double Light((byte R, byte G, byte B) c)
        {
            static double Channel(byte v) { var s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
        static double Ratio(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        var background = Light(counts.MaxBy(c => c.Value).Key);
        return counts.Where(c => c.Value >= 12).Max(c => Ratio(background, Light(c.Key)));
    }

    [Theory] [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void At_every_text_size_the_strip_grows_with_the_text_fits_the_window_and_its_words_stand_out(float scale)
    {
        using var w = new Window(scale);
        Assert.Equal(scale, w.Screen.Zoom);
        Assert.False(w.Play.Match.ShowsStatus);   // the strip says whose move it is; the board doesn't say it again
        var button = w.Part("NewGameButton"); var turn = w.Part("TurnText"); var count = w.Part("CountText");
        // Drawn as much bigger as the text is: the button is 52 high in the design.
        Assert.InRange(button.Height, 52 * scale - 1, 52 * scale + 1);
        foreach (var part in new[] { button, turn, count })
        {
            Assert.True(part.X >= 0 && part.X + part.Width <= 720, $"{part.Name} goes past the window's side at {scale}x: {part}");
            Assert.True(part.Y + part.Height <= w.Part("Bar").Y + w.Part("Bar").Height, $"{part.Name} is outside the strip at {scale}x");
        }
        Assert.True(button.Y >= turn.Y + turn.Height || button.X >= turn.X + turn.Width, $"the button covers the words at {scale}x");
        Assert.InRange(Contrast(w.Screen.Picture, button), 4.5, 21);
        Assert.InRange(Contrast(w.Screen.Picture, turn), 4.5, 21);
        Assert.InRange(Contrast(w.Screen.Picture, count), 4.5, 21);
        // The keyboard reaches the button by its words and presses it.
        Host(w).Frames(w.Play.Draw, 1);
        var match = w.Play.Match;
        w.Press("New game");
        Assert.NotSame(match, w.Play.Match);
    }

    [Theory] [InlineData(1f)] [InlineData(2f)]
    public void At_every_text_size_the_title_grows_with_the_text_fits_the_window_and_its_button_is_pressed_by_keyboard(float scale)
    {
        using var w = new Window(scale, title: true);
        Assert.True(w.Play.OnTitle);
        var button = w.Part("NewGameButton"); var heading = w.Part("Heading");
        Assert.InRange(button.Height, 52 * scale - 1, 52 * scale + 1);
        foreach (var part in w.Screen.Parts().Where(p => p.Name != "Backdrop"))
            Assert.True(part.X >= 0 && part.X + part.Width <= 720 && part.Y >= 0 && part.Y + part.Height <= w.Screen.Picture.Height, $"{part.Name} is cut off at {scale}x: {part}");
        Assert.InRange(Contrast(w.Screen.Picture, button), 4.5, 21);
        Assert.InRange(Contrast(w.Screen.Picture, heading), 4.5, 21);
        w.Press("New game");
        Assert.False(w.Play.OnTitle);
    }

    private static Bed.Editor.ImGui.HeadlessHost Host(Window w) => w.Host;

    [Fact]
    public void BED_tells_how_a_game_draws_Gum_text_from_the_built_game_when_there_is_no_code_beside_it()
    {
        var folder = Directory.CreateTempSubdirectory("gum-route-").FullName;
        try
        {
            Assert.Empty(GumTextRoutes.Detect(folder));
            // A program built as one file names the libraries inside it; play-checkers carries Gum's Skia drawing.
            var game = Path.Combine(AppContext.BaseDirectory, "SkiaGum.Standalone.dll");
            Assert.True(File.Exists(game), "the test program should have Gum's Skia library beside it");
            File.WriteAllBytes(Path.Combine(folder, "play.exe"), [.. "MZ junk\0"u8, .. "SkiaGum.Standalone.dll\0"u8, .. "more"u8]);
            Assert.Equal([GumTextRoute.Skia], GumTextRoutes.Detect(folder));
            File.Delete(Path.Combine(folder, "play.exe"));
            // Libraries beside a game count too.
            Directory.CreateDirectory(Path.Combine(folder, "build"));
            File.WriteAllText(Path.Combine(folder, "build", "MonoGameGum.dll"), "");
            Assert.Equal([GumTextRoute.FontCache], GumTextRoutes.Detect(folder));
            // A name that only starts the same way isn't a Gum library.
            File.Delete(Path.Combine(folder, "build", "MonoGameGum.dll"));
            File.WriteAllBytes(Path.Combine(folder, "other.exe"), [.. "SkiaGumbo.txt\0SkiaGum.\0"u8]);
            Assert.Empty(GumTextRoutes.Detect(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void After_a_save_in_Gum_the_strip_still_lists_its_parts_so_its_size_and_button_survive()
    {
        var folder = Directory.CreateTempSubdirectory("checkers-ui-").FullName;
        try
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(Project)!, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(folder, Path.GetRelativePath(Path.GetDirectoryName(Project)!, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file, to);
            }
            using var picture = new GumScreenPicture(Path.Combine(folder, "CheckersUi.gumj"), 720, 400);
            Assert.True(picture.Show("MatchPanel"), picture.Problem);
            picture.Frame(0);
            Assert.Contains(picture.Parts(), p => p.Name == "NewGameButton");
            // Gum saves the strip in another colour (found in a real window: the parts went missing after Gum's reload).
            var screen = Path.Combine(folder, "Screens", "MatchPanel.gusj");
            var saves = picture.Saves;
            File.WriteAllText(screen, File.ReadAllText(screen).Replace("\"ValueAsInt\": 46", "\"ValueAsInt\": 30"));
            var clock = System.Diagnostics.Stopwatch.StartNew(); var t = 0.0;
            while (picture.Saves == saves && clock.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(50); picture.Frame(t += 0.05); }
            Assert.True(picture.Saves > saves, "Gum didn't notice the save");
            picture.Frame(t + 0.05);
            Assert.Equal(["Bar", "Strip", "Row", "Info", "TurnText", "CountText", "NewGameButton"], picture.Parts().Select(p => p.Name));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
