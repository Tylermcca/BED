using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ThemePreferencesA454Tests
{
    private sealed class Rig : IDisposable
    {
        private readonly EditorThemePreference original = EditorTheme.Preference;
        public string Root { get; } = Directory.CreateTempSubdirectory("theme-prefs-").FullName;
        public string Folder => Path.Combine(Root, "settings");
        public string Settings => Path.Combine(Folder, "settings.json");
        public ProjectLauncher Launcher { get; }
        public HeadlessHost Host { get; } = new(1600, 1000) { Drawing = false };
        public Rig(string? settings = null)
        {
            if (settings is not null) { Directory.CreateDirectory(Folder); File.WriteAllText(Settings, settings); }
            Launcher = new(new RecentProjects(Path.Combine(Folder, "recent.json")));
            EditorWindow<DataDocument>.Configure(Im.GetIO());
        }
        public JsonObject Read() => JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        public void Reach(string label)
        {
            var window = Im.Internal.FindWindowByName("BED###launcher"u8);
            Assert.False(window.IsNull()); var id = window.GetID(label).Value;
            for (var i = 0; i < 40 && Im.GetCurrentContext().NavId.Value != id; i++) Host.Press(Launcher.Draw, ImGuiKey.Tab);
            Assert.Equal(id, Im.GetCurrentContext().NavId.Value);
        }
        public void Preferences()
        {
            Host.Frames(Launcher.Draw, 4); Reach("Preferences"); Host.Press(Launcher.Draw, ImGuiKey.Enter);
            Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo && n.Label == "Theme");
        }
        public void Choose(EditorThemePreference preference)
        {
            Reach("Theme"); Host.Press(Launcher.Draw, ImGuiKey.Space);
            Host.Press(Launcher.Draw, ImGuiKey.Home);
            for (var i = 0; i < (int)preference; i++) Host.Press(Launcher.Draw, ImGuiKey.DownArrow);
            Host.Press(Launcher.Draw, ImGuiKey.Enter); Host.Frames(Launcher.Draw, 2);
            Assert.Equal(preference, EditorTheme.Preference);
        }
        public void Dispose()
        {
            Launcher.Dispose(); Host.Dispose(); EditorTheme.Preference = original;
            var full = Path.GetFullPath(Root); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("theme-prefs-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected preferences fixture cleanup path.");
            Directory.Delete(full, true);
        }
    }

    [Theory]
    [InlineData(EditorThemePreference.System)] [InlineData(EditorThemePreference.Light)] [InlineData(EditorThemePreference.Dark)]
    public void Remembered_theme_and_size_load_without_rewriting_any_settings(EditorThemePreference theme)
    {
        var settings = "{\"textScale\":1.5,\"theme\":\"" + theme + "\",\"future\":{\"values\":[1,true,null]}}";
        using var r = new Rig(settings); r.Host.Frames(r.Launcher.Draw, 3);
        Assert.Equal(theme, EditorTheme.Preference); Assert.Equal(1.5f, EditorWindow<DataDocument>.TextScale);
        Assert.Equal(settings, File.ReadAllText(r.Settings)); Assert.False(File.Exists(r.Launcher.Recent.File));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"theme\":\"Unknown\"}")]
    [InlineData("{\"theme\":999}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Invalid_or_missing_theme_uses_System_without_rewriting_the_input(string settings)
    {
        using var r = new Rig(settings); r.Host.Frames(r.Launcher.Draw, 3);
        Assert.Equal(EditorThemePreference.System, EditorTheme.Preference);
        Assert.Equal(settings, File.ReadAllText(r.Settings));
    }

    [Fact]
    public void Preferences_keyboard_choice_preserves_size_unknown_fields_and_later_disk_keys()
    {
        using var r = new Rig("""{"textScale":1.5,"theme":"System","future":{"keep":[1,true,null]}}""");
        r.Preferences(); var latest = r.Read(); latest["late"] = "added after opening"; File.WriteAllText(r.Settings, latest.ToJsonString());
        r.Choose(EditorThemePreference.Dark); var dark = r.Read();
        Assert.Equal("Dark", dark["theme"]!.GetValue<string>()); Assert.Equal(1.5f, dark["textScale"]!.GetValue<float>());
        Assert.True(JsonNode.DeepEquals(latest["future"], dark["future"])); Assert.Equal("added after opening", dark["late"]!.GetValue<string>());
        r.Choose(EditorThemePreference.Light); Assert.Equal("Light", r.Read()["theme"]!.GetValue<string>());
        r.Choose(EditorThemePreference.System); Assert.Equal("System", r.Read()["theme"]!.GetValue<string>());
        r.Reach("Larger"); r.Host.Press(r.Launcher.Draw, ImGuiKey.Space); r.Host.Frames(r.Launcher.Draw, 2);
        var final = r.Read(); Assert.Equal(1.75f, final["textScale"]!.GetValue<float>());
        Assert.Equal("System", final["theme"]!.GetValue<string>()); Assert.True(JsonNode.DeepEquals(latest["future"], final["future"]));
        Assert.Null(r.Launcher.Workspace); Assert.Empty(r.Launcher.Recent.Items);
    }

    [Fact]
    public void Idle_first_start_does_not_create_preferences_but_an_explicit_theme_change_does()
    {
        using var r = new Rig(); r.Host.Frames(r.Launcher.Draw, 3);
        Assert.False(Directory.Exists(r.Folder)); EditorTheme.Preference = EditorThemePreference.Light; r.Host.Frame(r.Launcher.Draw);
        Assert.Equal("Light", r.Read()["theme"]!.GetValue<string>()); Assert.False(File.Exists(r.Launcher.Recent.File));
    }

    [Fact]
    public void Unwritable_settings_keeps_the_theme_choice_and_ui_running()
    {
        using var r = new Rig(); Directory.CreateDirectory(r.Settings); r.Host.Frames(r.Launcher.Draw, 3);
        EditorTheme.Preference = EditorThemePreference.Dark; var error = Record.Exception(() => r.Host.Frame(r.Launcher.Draw));
        Assert.Null(error); Assert.Equal(EditorThemePreference.Dark, EditorTheme.Preference);
        Assert.True(Directory.Exists(r.Settings)); Assert.Null(r.Launcher.Workspace);
    }
}
