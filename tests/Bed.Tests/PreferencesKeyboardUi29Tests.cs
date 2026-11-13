using System.Numerics;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PreferencesKeyboardUi29Tests
{
    [Theory]
    [InlineData("start", false)] [InlineData("data", false)] [InlineData("scene", false)]
    [InlineData("data", true)] [InlineData("scene", true)]
    public void Native_shortcut_and_menu_open_live_preferences_and_restart_preserves_them(string surface, bool menu)
    {
        var root = Directory.CreateTempSubdirectory("preferences-ui29-").FullName;
        var original = EditorTheme.Preference;
        try
        {
            var local = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
            var settings = Path.Combine(local, "settings.json");
            File.WriteAllText(settings, """{"textScale":1,"theme":"Light","future":{"keep":true}}""");
            var path = Path.Combine(data, "draft.json");
            File.WriteAllText(path, surface == "scene" ? new SceneFormat().Write(new Scene([SceneObject.At("box", null, Vector3.Zero)])) : """{"rows":[{"id":"one","value":1}]}""");
            File.WriteAllText(Path.Combine(schemas, "draft.schema.json"), """{"type":"object"}""");
            var bytes = File.ReadAllBytes(path);
            using (var host = new HeadlessHost(1600, 1000) { Drawing = false })
            using (var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(local, "recent.json"))))
            {
                EditorWindow<DataDocument>.Configure(Im.GetIO());
                if (surface == "scene") launcher.SetUpWorkspace = w => Assert.True(w.Documents.BindScene("draft.json", "scene", out _));
                host.Frames(launcher.Draw, 4);
                if (surface != "start")
                {
                    Assert.True(launcher.Open(new ProjectPaths(data, schemas)), launcher.Said);
                    Assert.True(launcher.Workspace!.Show("draft.json")); host.Frames(launcher.Draw, 4);
                }
                void Key(ImGuiKey key, bool ctrl = false, bool alt = false) { host.Press(launcher.Draw, key, ctrl: ctrl, alt: alt); host.Frames(launcher.Draw, 2); }
                void Reach(string label)
                {
                    for (var i = 0; i < 60; i++)
                    {
                        if (Ui.Snapshot().Nodes.Any(n => n.Focused && n.Label == label))
                        { Assert.True(Im.GetCurrentContext().NavCursorVisible); return; }
                        Key(ImGuiKey.Tab);
                    }
                    Assert.Fail("Keyboard could not reach " + label + "; focused: " + string.Join(",", Ui.Snapshot().Nodes.Where(n => n.Focused).Select(n => n.Label)));
                }
                void Choose(string label, int index)
                {
                    Reach(label); Key(ImGuiKey.Space); Key(ImGuiKey.Home);
                    for (var i = 0; i < index; i++) Key(ImGuiKey.DownArrow);
                    Key(ImGuiKey.Enter); host.Frames(launcher.Draw, 2);
                }
                if (menu)
                {
                    void Access(string label, UiRole role)
                    {
                        var item = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == role && n.Label == label);
                        if (item.AccessKey is { } letter) Key(Enum.Parse<ImGuiKey>(letter), alt: true);
                        else
                        {
                            for (var i = 0; i < 40 && !Ui.Snapshot().Nodes.Any(n => n.Role == role && n.Label == label && n.Focused); i++) Key(ImGuiKey.DownArrow);
                            Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == role && n.Label == label && n.Focused);
                            Key(role == UiRole.Menu ? ImGuiKey.RightArrow : ImGuiKey.Enter);
                        }
                    }
                    Key(ImGuiKey.E, alt: true); Access("Options", UiRole.Menu);
                    Access("Open options...", UiRole.MenuItem);
                }
                else Key(ImGuiKey.Comma, ctrl: true);
                host.Frames(launcher.Draw, 3);
                Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo && n.Label == "Theme");
                Choose("Theme", 3); Choose("UI scale", 2);
                Assert.Equal(EditorThemePreference.HighContrast, EditorTheme.Preference);
                Assert.Equal(2f, EditorWindow<DataDocument>.TextScale);
                Assert.Equal(2f, Im.GetStyle().FontScaleMain);
                Assert.Equal(1f, Im.GetStyle().Colors[(int)ImGuiCol.Text].X);
                var saved = JsonNode.Parse(File.ReadAllText(settings))!;
                Assert.Equal("HighContrast", saved["theme"]!.GetValue<string>());
                Assert.Equal(2f, saved["textScale"]!.GetValue<float>());
                Assert.True(saved["future"]!["keep"]!.GetValue<bool>());
                Assert.Equal(bytes, File.ReadAllBytes(path));
                Key(ImGuiKey.Escape); host.Frames(launcher.Draw, 2);
                Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo && n.Label == "Theme");
                if (launcher.Workspace is { } workspace)
                {
                    Assert.Empty(workspace.Project.Dirty);
                    if (surface == "scene") Assert.False(workspace.SceneWindow!.Editor.Session.Dirty);
                }
            }
            var stored = File.ReadAllBytes(settings);
            using (var host = new HeadlessHost(1600, 1000) { Drawing = false })
            using (var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(local, "recent.json"))))
            {
                EditorWindow<DataDocument>.Configure(Im.GetIO()); host.Frames(launcher.Draw, 3);
                Assert.Equal(EditorThemePreference.HighContrast, EditorTheme.Preference);
                Assert.Equal(2f, EditorWindow<DataDocument>.TextScale);
                Assert.Equal(stored, File.ReadAllBytes(settings));
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
        }
        finally { EditorTheme.Preference = original; Directory.Delete(root, true); }
    }
}
