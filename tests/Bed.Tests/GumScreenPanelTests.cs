using Bed.Editor.Gum;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// GUM-PICTURE: the Gum screen panel, drawn by the headless host (no window). The host rasterises ImGui's own texture
// requests the way a renderer would, so a colour from the Gum screen showing on the host's pixels proves the picture went
// through ImGui's texture list.
[Collection(ImGuiCollection.Name)]
public sealed class GumScreenPanelTests
{
    private static int CountColour(HeadlessHost host, byte r, byte g, byte b)
    {
        var wanted = 0xFF000000u | (uint)b << 16 | (uint)g << 8 | r;
        return host.Pixels.Count(p => p == wanted);
    }

    private static Action Window(GumScreenPanel panel) => () =>
    {
        ImGui.SetNextWindowPos(new float2(0, 0));
        ImGui.SetNextWindowSize(new float2(900, 700));
        if (Ui.Begin("Gum screen###gum-screen-test"u8)) panel.Draw();
        Ui.End();
    };

    [Fact]
    public void The_panel_shows_the_screen_as_Gum_draws_it_and_shows_a_new_look_when_chosen()
    {
        var folder = Directory.CreateTempSubdirectory("gum-panel-").FullName;
        try
        {
            var from = GumFixtures.Folder("Original");
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(folder, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            var screen = Path.Combine(folder, "Screens", "Preview.gusj");
            File.WriteAllText(screen, File.ReadAllText(screen).Replace("\"Categories\": []",
                """
                "Categories": [ { "Name": "Mood", "States": [
                  { "Name": "Warm", "Variables": [ { "Name": "Panel.Red", "Type": "int", "SetsValue": true, "ValueAsInt": 250 } ], "VariableLists": [] } ] } ]
                """));

            using var host = new HeadlessHost(900, 700);
            using var panel = new GumScreenPanel(Path.Combine(folder, "Original.gumj"));
            var draw = Window(panel);
            host.Frames(draw, 3);
            Assert.Equal("Preview", panel.Picture!.Screen);
            Assert.Equal("", panel.Picture.Problem);
            Assert.Equal(ImTextureStatus.OK, panel.Texture.State.Status);
            Assert.NotEqual(0L, panel.Texture.State.Id);
            Assert.True(CountColour(host, 20, 60, 90) > 1000, "The Gum screen's panel colour isn't on screen.");

            Assert.True(panel.Choose("Preview", new GumLook("Mood", "Warm")));
            host.Frames(draw, 3);
            Assert.True(CountColour(host, 250, 60, 90) > 1000, "The chosen look isn't on screen.");
            Assert.Equal(0, CountColour(host, 20, 60, 90));

            // The panel's words, as the screen-reader tree records them.
            var tree = Ui.Snapshot().Nodes.Select(n => n.Label).ToList();
            Assert.Contains(tree, l => l.Contains("Picture of the Gum screen Preview, look Mood: Warm"));
            Assert.DoesNotContain(tree, l => l.Contains("render", StringComparison.OrdinalIgnoreCase)
                || l.Contains("canvas", StringComparison.OrdinalIgnoreCase) || l.Contains("texture", StringComparison.OrdinalIgnoreCase));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void Picking_in_a_scaled_fake_picture_highlights_the_smallest_part_and_follows_its_name_after_a_save()
    {
        using var host = new HeadlessHost(900, 700);
        using var panel = new GumScreenPanel("unused.gumj");
        IReadOnlyList<GumPart> parts = [new("Background", 0, 0, 800, 600, null), new("NewGameButton", 100, 120, 200, 60, "New game")];
        var origin = new float2(90, 150); var scale = .75f;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(900, 700));
            if (Ui.Begin("Fake picture"u8))
            {
                panel.DrawPartChoices(parts);
                panel.DrawPartPicture(parts, origin, new float2(600, 450), scale,
                    () => ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new float2(600, 450), 0xff101010));
            }
            Ui.End();
        }
        host.Frames(Draw, 3);
        var before = host.Pixels[(int)(origin.Y + 120 * scale) * host.Width + (int)(origin.X + 150 * scale)];
        host.Click(Draw, origin.X + 200 * scale, origin.Y + 140 * scale); host.Frames(Draw, 2);
        Assert.Equal("NewGameButton", panel.SelectedPart);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "NewGameButton: 200 × 60 pixels");
        Assert.NotEqual(before, host.Pixels[(int)(origin.Y + 120 * scale) * host.Width + (int)(origin.X + 150 * scale)]);
        parts = [new("NewGameButton", 200, 160, 240, 70, "Play")]; host.Frames(Draw, 2);
        Assert.Equal("NewGameButton", panel.SelectedPart);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "NewGameButton: 240 × 70 pixels");
        parts = []; host.Frames(Draw, 2); Assert.Null(panel.SelectedPart);
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Label.Contains("NewGameButton"));
    }

    [Fact]
    public void The_fake_picture_part_list_supports_keyboard_and_screen_reader_selection()
    {
        using var host = new HeadlessHost(900, 700);
        EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());
        using var panel = new GumScreenPanel("unused.gumj");
        GumPart[] parts = [new("NewGameButton", 20, 30, 100, 40, "New game")];
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(900, 700));
            if (Ui.Begin("Fake picture"u8)) panel.DrawPartChoices(parts);
            Ui.End();
        }
        host.Frames(Draw, 3);
        var combo = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo && n.Label == "Part");
        Ui.RequestAction(combo.Id, UiActions.Focus); host.Frame(Draw);
        host.Press(Draw, ImGuiKey.Space); host.Frames(Draw, 2);
        var item = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "NewGameButton");
        Assert.True(item.Actions.HasFlag(UiActions.Select));
        var request = Ui.RequestAction(item.Id, UiActions.Select); host.Frame(Draw);
        Assert.Equal(UiActionOutcome.Applied, request.Outcome);
        Assert.Equal("NewGameButton", panel.SelectedPart);
        host.Press(Draw, ImGuiKey.Escape); host.Frames(Draw, 2);
        Ui.RequestAction(combo.Id, UiActions.Focus); host.Frame(Draw); host.Press(Draw, ImGuiKey.Space);
        host.Press(Draw, ImGuiKey.Home); host.Press(Draw, ImGuiKey.Enter); host.Frames(Draw, 2);
        Assert.Null(panel.SelectedPart); // The first list item is No part selected.
    }

    [Fact]
    public void A_project_with_no_readable_screens_says_so_in_plain_words()
    {
        using var host = new HeadlessHost(600, 400);
        using var panel = new GumScreenPanel(Path.Combine(GumFixtures.Folder("Newer"), "Newer.gumj"));
        host.Frames(Window(panel), 2);
        Assert.Null(panel.Picture);
    }
}
