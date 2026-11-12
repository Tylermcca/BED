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
    public void A_project_with_no_readable_screens_says_so_in_plain_words()
    {
        using var host = new HeadlessHost(600, 400);
        using var panel = new GumScreenPanel(Path.Combine(GumFixtures.Folder("Newer"), "Newer.gumj"));
        host.Frames(Window(panel), 2);
        Assert.Null(panel.Picture);
    }
}
