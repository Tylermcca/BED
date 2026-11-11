using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class LauncherPresentationA441Tests
{
    private sealed class Connection : IProjectAdapter
    {
        public string Name => "Example rules";
        public int Opened;
        public bool Recognises(ProjectPaths paths) => true;
        public DataProject Open(ProjectPaths paths) { Opened++; return DataProject.Open(paths.Data, paths.Schemas); }
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    }
    private sealed class Rig : IDisposable
    {
        private readonly EditorThemePreference original = EditorTheme.Preference;
        public string Root { get; } = Directory.CreateTempSubdirectory("launcher-words-").FullName;
        public string FilePath => Path.Combine(Root, "data", "sample.json");
        public ProjectPaths Paths => new(Path.Combine(Root, "data"), Path.Combine(Root, "schemas"));
        public ProjectLauncher Launcher { get; }
        public HeadlessHost Host { get; } = new(1600, 1000) { Drawing = false };
        public Rig()
        {
            Directory.CreateDirectory(Paths.Data); Directory.CreateDirectory(Paths.Schemas);
            File.WriteAllText(FilePath, """{"items":[{"value":1}]}""");
            File.WriteAllText(Path.Combine(Paths.Schemas, "sample.schema.json"), """{"type":"object"}""");
            Launcher = new(new RecentProjects(Path.Combine(Root, "settings", "recent.json")));
            EditorWindow<DataDocument>.Configure(Im.GetIO());
        }
        public string Text()
        {
            Host.Frame(() => { Im.LogToClipboard(); Launcher.Draw(); Im.LogFinish(); });
            return Im.GetClipboardText().ToString() ?? "";
        }
        public void Reach(string label)
        {
            var window = Im.Internal.FindWindowByName("BED###launcher"u8); Assert.False(window.IsNull());
            var expected = window.GetID(label).Value;
            for (var i = 0; i < 45 && Im.GetCurrentContext().NavId.Value != expected; i++) Host.Press(Launcher.Draw, ImGuiKey.Tab);
            Assert.Equal(expected, Im.GetCurrentContext().NavId.Value);
        }
        public void Dispose()
        {
            Launcher.Dispose(); Host.Dispose(); EditorTheme.Preference = original;
            var full = Path.GetFullPath(Root); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("launcher-words-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected wording fixture cleanup path.");
            Directory.Delete(full, true);
        }
    }

    [Fact]
    public void Home_lists_connections_and_does_not_restore_a_preproject_AI_route()
    {
        using var r = new Rig(); r.Launcher.Loaded = [new Connection()]; var before = File.ReadAllBytes(r.FilePath);
        r.Host.Frames(r.Launcher.Draw, 4); var text = r.Text();
        Assert.Contains("Available game connections: Example rules", text);
        Assert.DoesNotContain("Available game adapters", text);
        Assert.DoesNotContain("Help: Use with an AI assistant", text); Assert.DoesNotContain("Copy bed-cli command", text);
        Assert.Null(r.Launcher.Workspace); Assert.Empty(r.Launcher.Recent.Items);
        Assert.Equal(before, File.ReadAllBytes(r.FilePath));
    }

    [Fact]
    public void Manual_Data_only_choice_keeps_the_existing_hidden_combo_and_selection_behavior()
    {
        using var r = new Rig(); var connection = new Connection(); r.Launcher.Loaded = [connection]; r.Launcher.Adapter = connection;
        r.Host.Frames(r.Launcher.Draw, 4); r.Host.Press(r.Launcher.Draw, ImGuiKey.O, ctrl: true);
        var text = r.Text(); Assert.Contains("Game connection", text);
        Assert.Contains("Game connections are programs; load only ones you trust.", text);
        Assert.DoesNotContain("None (plain data)", text);
        var before = File.ReadAllBytes(r.FilePath);
        r.Reach("##adapter"); r.Host.Press(r.Launcher.Draw, ImGuiKey.Space);
        r.Host.Press(r.Launcher.Draw, ImGuiKey.Home); r.Host.Press(r.Launcher.Draw, ImGuiKey.DownArrow);
        r.Host.Press(r.Launcher.Draw, ImGuiKey.Enter); r.Host.Frames(r.Launcher.Draw, 2);
        Assert.True(r.Launcher.NoAdapter); Assert.Null(r.Launcher.Adapter); Assert.Contains("Data only", r.Text());
        Assert.Equal(0, connection.Opened); Assert.Null(r.Launcher.Workspace); Assert.Empty(r.Launcher.Recent.Items);
        Assert.Equal(before, File.ReadAllBytes(r.FilePath));
    }

    [Fact]
    public void Missing_saved_connection_uses_plain_fallback_and_keeps_the_exact_remembered_identity()
    {
        using var r = new Rig(); var global = new Connection(); r.Launcher.Loaded = [global];
        var missing = Path.Combine(r.Root, "missing-connection.dll");
        var remembered = r.Paths with { AdapterPath = missing, AdapterType = "Invented.Connection" };
        var before = File.ReadAllBytes(r.FilePath);
        Assert.True(r.Launcher.OpenRecent(remembered), r.Launcher.Said);
        Assert.Contains("The saved game connection at " + missing + " is missing.", r.Launcher.Said);
        Assert.Contains("opened without that game connection", r.Launcher.Said);
        Assert.Null(r.Launcher.Using); Assert.Equal(0, global.Opened);
        var recent = Assert.Single(r.Launcher.Recent.Items);
        Assert.Equal(missing, recent.AdapterPath); Assert.Equal("Invented.Connection", recent.AdapterType);
        Assert.Equal(before, File.ReadAllBytes(r.FilePath)); Assert.Empty(r.Launcher.Workspace!.Window!.Editor.Session.Journal);
        Assert.Single(r.Launcher.Workspace.Window.Editor.Panels, p => p.Id == AiSetupPanel.Id);
    }
}
