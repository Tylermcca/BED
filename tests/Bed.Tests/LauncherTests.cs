using System.Text.Json;
using Bed.Data;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>The general app (the user, 2026-11-01: "an average person can use the tools"): any project opens with no
/// code, refusals say what to do, recent projects are remembered, and it draws headless.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class LauncherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "launcher-" + Guid.NewGuid().ToString("N"));
    private string Project => Path.Combine(root, "game");
    private string Settings => Path.Combine(root, "settings", "recent.json");

    public LauncherTests()
    {
        Directory.CreateDirectory(Path.Combine(Project, "data"));
        Directory.CreateDirectory(Path.Combine(Project, "schemas"));
        Directory.CreateDirectory(Path.Combine(Project, "empty"));
        File.WriteAllText(Path.Combine(Project, "schemas", "wares.schema.json"), """{"type":"object","properties":{"wares":{"type":"array"}}}""");
        File.WriteAllText(Path.Combine(Project, "data", "wares.json"), """{"wares":[{"id":"ore","price":2}]}""");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private ProjectLauncher Launcher() => new(new RecentProjects(Settings));

    private ProjectPaths Paths(string? map = null) => new(Path.Combine(Project, "data"), Path.Combine(Project, "schemas"), map);

    [Fact]
    public void A_project_opens_with_no_code_showing_its_first_file_and_is_remembered()
    {
        var l = Launcher();
        Assert.True(l.Open(Paths()), l.Said);
        Assert.Contains("Opened game: 1 files", l.Said);   // named after its folder, not "data"
        Assert.Equal("wares.json", l.Workspace!.Active!.Name);
        var again = new RecentProjects(Settings);
        Assert.Equal(Path.Combine(Project, "data"), Assert.Single(again.Items).Data);
    }

    [Fact]
    public void A_folder_laid_out_with_data_and_schemas_opens_in_one_step_with_its_map()
    {
        File.WriteAllText(Path.Combine(Project, "reference_map.json"), """{"game":"G","labels":[],"references":[]}""");
        var paths = ProjectPaths.In(Project)!;
        Assert.EndsWith("reference_map.json", paths.Map);
        var l = Launcher();
        Assert.True(l.OpenFolder(Project), l.Said);
        Assert.NotNull(l.Workspace!.Map);
        Assert.Null(ProjectPaths.In(root));
        Assert.False(Launcher().OpenFolder(root));
        Assert.Contains("doesn't hold a data folder and a rules folder (schemas)", Launcher() is var x && !x.OpenFolder(root) ? x.Said : "");
    }

    [Theory]
    [InlineData("", "schemas", "Choose the data folder")]
    [InlineData("nowhere", "schemas", "There's no data folder at")]
    [InlineData("data", "", "Choose the rules folder")]
    [InlineData("data", "nowhere", "There's no rules folder at")]
    [InlineData("data", "empty", "holds no rules files")]
    public void Each_wrong_folder_is_refused_saying_what_to_do(string data, string schemas, string said)
    {
        var l = Launcher();
        string At(string f) => f.Length == 0 ? "" : Path.Combine(Project, f);
        Assert.False(l.Open(new(At(data), At(schemas))));
        Assert.Contains(said, l.Said);
        Assert.Null(l.Workspace);
        Assert.Empty(new RecentProjects(Settings).Items);   // a failed open isn't remembered
    }

    [Fact]
    public void A_missing_or_broken_link_map_is_refused_and_an_empty_one_is_fine()
    {
        var l = Launcher();
        Assert.False(l.Open(Paths(Path.Combine(Project, "none.json"))));
        Assert.Contains("Leave it empty if the project has none", l.Said);
        File.WriteAllText(Path.Combine(Project, "bad.json"), "{ nope");
        Assert.False(l.Open(Paths(Path.Combine(Project, "bad.json"))));
        Assert.Contains("can't be read", l.Said);
        Assert.True(l.Open(Paths("")));
        Assert.Null(l.Workspace!.Map);
    }

    [Fact]
    public void Closing_with_unsaved_edits_asks_first_and_another_open_waits()
    {
        var l = Launcher();
        Assert.True(l.Open(Paths()));
        Assert.True(l.Workspace!.Window!.Editor.Session.Execute(new SetValue("/wares/0/price", JsonDocument.Parse("3").RootElement)));
        Assert.False(l.Open(Paths()));
        Assert.Contains("unsaved edits", l.Said);
        Assert.False(l.Close());
        Assert.Contains("Don't Save", l.Said);
        Assert.NotNull(l.Workspace);
        Assert.True(l.Workspace.CloseProject!() == false);   // the Files panel's Close says the same
        Assert.True(l.Close(discard: true));
        Assert.Null(l.Workspace);
        Assert.Contains("\"price\":2", File.ReadAllText(Path.Combine(Project, "data", "wares.json")));
    }

    [Fact]
    public void Recent_projects_put_the_latest_first_keep_a_limit_and_survive_a_corrupt_file()
    {
        var recent = new RecentProjects(Settings, limit: 2);
        recent.Add(new("a", "s"));
        recent.Add(new("b", "s"));
        recent.Add(new("a", "s"));   // moved, not repeated
        Assert.Equal(["a", "b"], recent.Items.Select(p => p.Data));
        recent.Add(new("c", "s"));
        Assert.Equal(["c", "a"], recent.Items.Select(p => p.Data));
        recent.Remove(new("c", "s"));
        Assert.Equal(["a"], new RecentProjects(Settings).Items.Select(p => p.Data));
        File.WriteAllText(Settings, "not json");
        Assert.Empty(new RecentProjects(Settings).Items);
    }

    [Fact]
    public void The_start_screen_and_the_project_draw_headless()
    {
        var l = new ProjectLauncher(new RecentProjects(Settings), sample: Paths());
        using var host = new HeadlessHost(1280, 720);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(l.Draw, 3);
        Assert.True(host.Covered(0, 0, 1280, 200) > 2000, "the start screen drew");
        Assert.True(l.Open(l.Sample!));
        host.Frames(l.Draw, 4);
        Assert.True(host.Covered(0, 0, 1280, 720) > 5000, "the workspace drew");
        Assert.Equal("game", ProjectLauncher.Name(Paths()));
    }

    [Fact]
    public void The_program_opens_a_folder_headless_and_exits_with_the_reason_when_it_cant()
    {
        var shot = Path.Combine(root, "shot.png");
        var output = new StringWriter();
        Assert.Equal(0, Bed.Launcher.Program.Run([Project, "--shot", shot], output, TextWriter.Null, Path.Combine(root, "settings")));
        Assert.True(new FileInfo(shot).Length > 1000);
        Assert.Contains("Opened game", output.ToString());
        var errors = new StringWriter();
        Assert.Equal(1, Bed.Launcher.Program.Run([root, "--shot", shot], TextWriter.Null, errors, Path.Combine(root, "settings")));
        Assert.Contains("doesn't hold a data folder", errors.ToString());
        Assert.Equal(2, Bed.Launcher.Program.Run(["--data", "x"], TextWriter.Null, new StringWriter(), Path.Combine(root, "settings")));
    }

    [Fact]
    public void Settings_given_on_the_command_line_keep_the_recent_list_there()
    {
        // Windows' own settings folder ignores APPDATA, so a trial that set it changed the person's list (found by A065).
        var mine = Path.Combine(root, "trial");
        Assert.Equal(0, Bed.Launcher.Program.Run([Project, "--settings", mine, "--shot", Path.Combine(root, "s.png")], TextWriter.Null, TextWriter.Null,
            Path.Combine(root, "not-these")));
        Assert.Single(new RecentProjects(Path.Combine(mine, "recent.json")).Items);
        Assert.False(Directory.Exists(Path.Combine(root, "not-these")));
    }

    [Fact]
    public void The_brutal_logo_shows_before_the_start_screen_then_the_credits_line_is_on_it()
    {
        // The BRUTAL License v1.0: the logo before the program's main function (2.2), the copyright in the credits (2.1).
        var l = new ProjectLauncher(new RecentProjects(Settings)) { Splash = new BrutalSplash() };
        // Only the frame the pixels are read from is painted; the other 181 still run (found by A224: unread frames cost seconds).
        using var host = new HeadlessHost(1280, 720) { Drawing = false };
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(l.Draw, 59);
        host.Drawing = true;
        host.Frame(l.Draw);   // one second in (frame 60): the logo at full strength
        host.Drawing = false;
        var shots = Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS");
        if (shots is { Length: > 0 }) host.Save(Path.Combine(Directory.CreateDirectory(shots).FullName, "40_brutal_splash.png"));
        var white = host.Pixels.Count(p => (p & 0xFF) > 230 && ((p >> 8) & 0xFF) > 230 && ((p >> 16) & 0xFF) > 230);
        Assert.True(white > 5000, $"{white} white pixels: the logo should be drawn large and legible");
        Assert.Equal(0u, host.Pixels[5] & 0x00FFFFFF);   // on black
        Assert.False(l.Splash.Done);
        host.Frames(l.Draw, 120);   // past 2.5 seconds
        Assert.True(l.Splash.Done);
        host.Frames(l.Draw, 2);
        Assert.Contains("Ahwoo Limited", BrutalCredit.Copyright);
        Assert.Contains("BRUTAL License v1.0", BrutalCredit.Notice);
        Assert.Equal((712, 168), BrutalCredit.LogoSize);
    }

    [Fact]
    public void Text_size_is_changed_by_keyboard_through_the_palette_kept_in_bounds_and_remembered()
    {
        var l = Launcher();
        using (var host = new HeadlessHost(1280, 720))
        {
            EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            host.Frames(l.Draw, 2);
            Assert.False(File.Exists(l.SettingsFile));   // nothing written until it changes
            Assert.True(l.Open(Paths()));
            var w = l.Workspace!.Window!;
            Assert.Equal("it's already normal", Assert.Single(w.PaletteEntries("normal text size")).Unavailable);
            Assert.True(w.Run("editor.text_larger"));
            Assert.True(w.Run("editor.text_larger"));
            Assert.Equal(1.5f, EditorWindow<DataDocument>.TextScale);
            for (var i = 0; i < 6; i++) w.Run("editor.text_larger");
            Assert.Equal(EditorWindow<DataDocument>.MaxTextScale, EditorWindow<DataDocument>.TextScale);   // 200% at most (WCAG 1.4.4)
            Assert.Equal("it's as large as it goes", Assert.Single(w.PaletteEntries("larger text")).Unavailable);
            Assert.True(w.Run("editor.text_reset"));
            Assert.True(w.Run("editor.text_larger"));
            host.Frames(l.Draw, 2);   // the launcher keeps it
        }
        Assert.Contains("1.25", File.ReadAllText(l.SettingsFile));
        var next = Launcher();   // a new run starts at the remembered size
        using var again = new HeadlessHost(640, 480);
        again.Frames(next.Draw, 1);
        Assert.Equal(1.25f, EditorWindow<DataDocument>.TextScale);
    }

    [Fact]
    public void The_folder_picker_moves_in_and_up()
    {
        var picker = new FolderPicker();
        picker.Start(Project, chooseFile: false);
        Assert.True(picker.Go("data"));
        Assert.Equal(Path.Combine(Project, "data"), picker.At);
        Assert.True(picker.Go(".."));
        Assert.Equal(Project, picker.At);
        Assert.False(picker.Go("nowhere"));
    }

    // The sample adapter's own built DLL, found under the repository the tests were built from (bin or artifacts).
    private static string StarterAdapterDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Bed.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Directory.EnumerateFiles(dir!.FullName, "StarterAdapter.dll", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains("ref" + Path.DirectorySeparatorChar)
                && !Path.GetRelativePath(dir!.FullName, f).StartsWith("codex" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))   // never Codex's scratch inside this repo, wherever the repo itself sits (found by A190)
            .OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    [Fact]
    public void A_game_adapter_loads_as_an_extra_recognises_its_project_and_adds_its_rules()
    {
        var (loaded, problems) = Adapters.Load([StarterAdapterDll()]);
        Assert.Empty(problems);
        var adapter = Assert.Single(loaded);
        Assert.Equal("Starter (sample game)", adapter.Name);

        File.WriteAllText(Path.Combine(Project, "schemas", "recipes.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(Project, "data", "recipes.json"), """{"recipes":[{"id":"bake","makes":"bread","from":"flour"}]}""");
        File.WriteAllText(Path.Combine(Project, "reference_map.json"), """{"game":"Starter","labels":[],"references":[]}""");
        var l = Launcher();
        l.Loaded = loaded;
        Assert.True(l.OpenFolder(Project), l.Said);
        Assert.Same(adapter, l.Using);   // chosen without being asked
        Assert.Contains("with Starter (sample game)", l.Said);
        Assert.True(l.Workspace!.Show("recipes.json"));
        Assert.False(l.Workspace.Window!.Editor.Session.Execute(new SetValue("/recipes/0/from", JsonDocument.Parse("\"bread\"").RootElement)));
        Assert.Contains("can't use up the ware it makes", string.Join(" ", l.Workspace.Window.Editor.Session.LastIssues.Select(i => i.Message)));

        // The same project opens plainly when no adapter is wanted: adapters are never a requirement.
        var plain = new ProjectLauncher(new RecentProjects(Path.Combine(root, "other.json"))) { Loaded = loaded, NoAdapter = true };
        Assert.True(plain.OpenFolder(Project));
        Assert.Null(plain.Using);
        Assert.True(plain.Workspace!.Show("recipes.json"));
        Assert.True(plain.Workspace.Window!.Editor.Session.Execute(new SetValue("/recipes/0/from", JsonDocument.Parse("\"bread\"").RootElement)));
    }

    private void StarterFiles()
    {
        File.WriteAllText(Path.Combine(Project, "schemas", "recipes.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(Project, "data", "recipes.json"), """{"recipes":[{"id":"bake","makes":"bread","from":"flour"}]}""");
        File.WriteAllText(Path.Combine(Project, "reference_map.json"), """{"game":"Starter","labels":[],"references":[]}""");
    }

    [Fact]
    public void The_ai_command_line_takes_a_game_adapter_and_its_rules_apply_to_an_ai_as_to_a_person()
    {
        // found by A188: the command line opened plain data, so an AI never met the game's own rules
        StarterFiles();
        string[] common = ["--data", Path.Combine(Project, "data"), "--schemas", Path.Combine(Project, "schemas")];
        string[] edit = ["set", "recipes.json", "/recipes/0/from", "\"bread\""];
        var withAdapter = new StringWriter();
        var why = new StringWriter();
        var code = Bed.Agent.Cli.Program.Run([.. common, "--adapter", StarterAdapterDll(), .. edit], TextReader.Null, withAdapter, why);
        Assert.Equal(1, code);   // refused, as any refused edit is
        Assert.Contains("can't use up the ware it makes", withAdapter.ToString());
        Assert.DoesNotContain("\"applied\": true", withAdapter.ToString());
        var plain = new StringWriter();
        Assert.Equal(0, Bed.Agent.Cli.Program.Run([.. common, .. edit], TextReader.Null, plain, TextWriter.Null));
        Assert.Contains("\"applied\": true", plain.ToString());

        var said = new StringWriter();   // read-only with an adapter is refused plainly, not half done
        Assert.Equal(2, Bed.Agent.Cli.Program.Run([.. common, "--adapter", StarterAdapterDll(), "--read-only", "describe"], TextReader.Null, new StringWriter(), said));
        Assert.Contains("isn't supported yet", said.ToString());
    }

    [Fact]
    public void A_hosts_setup_that_throws_costs_that_setup_never_the_project()
    {
        // found by A188: SetUpWorkspace ran outside the adapter guards, so a fault in it escaped Open
        StarterFiles();
        var l = Launcher();
        l.SetUpWorkspace = _ => throw new InvalidOperationException("the host's setup broke");
        Assert.True(l.OpenFolder(Project), l.Said);
        Assert.NotNull(l.Workspace);
        Assert.Contains("the host's setup broke", l.Said);
    }

    [Fact]
    public void A_dll_that_isnt_an_adapter_is_reported_not_thrown()
    {
        File.WriteAllText(Path.Combine(root, "BrokenAdapter.dll"), "not a program");
        var (loaded, problems) = Adapters.Load([Path.Combine(root, "BrokenAdapter.dll"), typeof(DataDocument).Assembly.Location]);   // a library with no adapters (A073's opinion: this test DLL now has some)
        Assert.Empty(loaded);
        Assert.Equal(2, problems.Count);
        Assert.Contains("didn't load", problems[0]);
        Assert.Contains("holds no game adapter", problems[1]);
        Assert.Equal([Path.Combine(root, "BrokenAdapter.dll")], Adapters.In(root));
    }
}
