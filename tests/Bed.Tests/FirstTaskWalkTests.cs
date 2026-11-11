using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using FirstTaskSample;
using Xunit;

namespace Bed.Tests;

// INVENTED fixture/task; BORROWED existing serial HeadlessHost real-input and palette proof patterns.
// Prepared only, UNRUN. No direct model edit, Run(action), check invocation, Save or Undo bypass in the walk.
[Collection(ImGuiCollection.Name)]
public sealed class FirstTaskWalkTests
{
    private sealed class Rig : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "A334-owned", Guid.NewGuid().ToString("N"));
        internal readonly HeadlessHost Host;
        internal readonly ProjectLauncher Launcher;
        internal DataWorkspace Workspace => Assert.IsType<DataWorkspace>(Launcher.Workspace);
        internal EditorWindow<DataDocument> Window => Assert.IsType<EditorWindow<DataDocument>>(Workspace.Window);
        internal DocumentSession<DataDocument> Session => Window.Editor.Session;
        internal string Disk => Path.Combine(Root, "data", "settings.json");
        internal Rig()
        {
            // Copy only the two published self-authored sample inputs, so fixture/guide cannot drift independently.
            var repo = FindRepo();
            foreach (var relative in new[] { "data/settings.json", "schemas/settings.schema.json" })
            {
                var destination = Path.Combine(Root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(repo, "samples", "FirstTask", relative), destination);
            }
            Host = new HeadlessHost(1600, 1000) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Launcher = new ProjectLauncher(new RecentProjects(Path.Combine(Root, "preferences", "recent.json")))
            { Loaded = [new FirstTaskAdapter()] };
            // Same public setup as the executable: text panel and background-save configuration.
            Bed.Launcher.Program.SetUp(Launcher);
            Host.Frames(Launcher.Draw, 5);
        }
        private static string FindRepo()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "samples", "FirstTask", "FirstTask.csproj"))) return d.FullName;
            throw new InvalidOperationException("Run this source-checkout test with samples/FirstTask present.");
        }
        internal void Key(ImGuiKey key, bool ctrl = false, bool shift = false) => Host.Press(Launcher.Draw, key, ctrl, shift);
        internal void Open()
        {
            // Use the new Open-project route with real keys. Its folder field receives focus on entry;
            // Browse and Use this folder follow it. Recognition still uses the real loaded sample adapter.
            Key(ImGuiKey.O, ctrl: true);
            Key(ImGuiKey.A, ctrl: true);
            Host.Type(Launcher.Draw, Root);
            Key(ImGuiKey.Tab); // Browse for project
            Key(ImGuiKey.Tab); // Use this folder
            Key(ImGuiKey.Enter);
            Assert.NotNull(Launcher.Workspace);
            Assert.IsType<FirstTaskAdapter>(Launcher.Using);
            Assert.Equal("settings.json", Workspace.Active!.Name);
            Assert.Contains(Workspace.Checks.Registered, x => x.Id == FirstTaskCheck.Id);
            Assert.Empty(Session.Journal);
        }
        internal void SelectPreset(string name, string pointer)
        {
            Host.Frames(Launcher.Draw, 3);
            var find = Window.InspectorPlace("", InspectorPart.Find);
            Assert.NotNull(find);
            Host.Click(Launcher.Draw, find.Value.X + 12, find.Value.Y + 7);
            Key(ImGuiKey.A, ctrl: true);
            Host.Type(Launcher.Draw, name);
            Key(ImGuiKey.Enter);
            Assert.NotEmpty(Window.Found);
            Key(ImGuiKey.Tab);
            Key(ImGuiKey.Enter);
            Assert.Equal(pointer, Window.Editor.Primary);
            Host.Frames(Launcher.Draw, 3);
        }
        internal void Rounds(string value)
        {
            var row = Assert.Single(Window.Inspector!.Rows(), x => x.Label == "rounds");
            Assert.Equal(PropertyKind.Integer, row.Kind);
            var place = Window.InspectorPlace(row.Id);
            Assert.NotNull(place);
            Assert.InRange(place.Value.X, 0, Host.Width - 13);
            Assert.InRange(place.Value.Y, 0, Host.Height - 8);
            Host.Click(Launcher.Draw, place.Value.X + 12, place.Value.Y + 7);
            Key(ImGuiKey.A, ctrl: true);
            Host.Type(Launcher.Draw, value);
            Key(ImGuiKey.Enter);
            Host.Frames(Launcher.Draw, 2);
        }
        internal void Palette(string id)
        {
            Key(ImGuiKey.P, ctrl: true, shift: true);
            Assert.True(Window.PaletteOpen);
            Key(ImGuiKey.A, ctrl: true); // Replace the previous query when reopening the palette.
            Host.Type(Launcher.Draw, id);
            Assert.Contains(Window.PaletteEntries(id), e => e.Id == id && e.Unavailable is null);
            Key(ImGuiKey.Enter);
            Assert.False(Window.PaletteOpen);
            Host.Frames(Launcher.Draw, 2);
        }
        internal int RoundsNow => Session.Current.At("/presets/1/rounds")!.Value.GetInt32();
        internal int SavedRounds()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Disk));
            return doc.RootElement.GetProperty("presets")[1].GetProperty("rounds").GetInt32();
        }
        public void Dispose()
        {
            Launcher.Dispose();
            Host.Dispose();
            var full = Path.GetFullPath(Root);
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "A334-owned"));
            if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(full), "N", out _)) throw new InvalidOperationException("Unsafe A334 cleanup boundary.");
            Directory.Delete(full, true);
        }
    }

    [Fact]
    public async Task Newcomer_opens_refuses_corrects_checks_saves_and_undoes_using_real_input()
    {
        using var r = new Rig();
        r.Open();
        r.SelectPreset("Practice", "/presets/1");
        Assert.Equal(60, r.RoundsNow);
        var bytes = File.ReadAllBytes(r.Disk);
        var before = r.Session.Current;
        var revision = r.Session.Revision;
        var undo = r.Session.History.UndoCount;
        var journal = r.Session.Journal.Count;
        r.Rounds("5"); // Valid integer/range, but fails the sample's cross-field rule (reserve is 10).
        Assert.Contains("rounds must be greater than reserve (10)", r.Window.Status);
        Assert.Same(before, r.Session.Current);
        Assert.Equal(revision, r.Session.Revision);
        Assert.Equal(undo, r.Session.History.UndoCount);
        Assert.Equal(journal, r.Session.Journal.Count);
        Assert.False(r.Session.Dirty);
        Assert.Equal(bytes, File.ReadAllBytes(r.Disk));

        r.Rounds("80"); // Positive control after refusal: the real field remains usable.
        Assert.Equal(80, r.RoundsNow);
        Assert.Equal(60, r.Session.Current.At("/presets/0/rounds")!.Value.GetInt32());
        Assert.Equal(undo + 1, r.Session.History.UndoCount);
        Assert.True(r.Session.Dirty);
        Assert.Equal(bytes, File.ReadAllBytes(r.Disk));
        r.Palette(EditorWindow<DataDocument>.PanelCommandAction + "project.checks/run." + FirstTaskCheck.Id);
        var run = Assert.IsType<CheckRun>(r.Workspace.Checks.Last(FirstTaskCheck.Id));
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(5));
        r.Host.Frames(r.Launcher.Draw, 3);
        Assert.True(run.Clean(r.Workspace.Project));
        Assert.True(run.Report!.Complete);
        Assert.Empty(run.Report.Findings);
        Assert.Contains("Both presets checked", run.Report.Summary);
        Assert.Contains("Practice has 80 rounds", run.Report.Summary); // Unsaved snapshot, not the 60-round file on disk.
        Assert.Contains("No simulation", run.Report.Summary);

        r.Key(ImGuiKey.S, ctrl: true); r.Host.Frames(r.Launcher.Draw, 2);
        Assert.False(r.Session.Dirty);
        Assert.Equal(80, r.SavedRounds());
        r.Key(ImGuiKey.Z, ctrl: true); r.Host.Frames(r.Launcher.Draw, 2);
        Assert.Equal(60, r.RoundsNow);
        Assert.True(r.Session.Dirty);
        Assert.Equal(80, r.SavedRounds()); // Undo edits the draft, not the already-saved file.
        Assert.True(run.IsStale(r.Workspace.Project));
        r.Key(ImGuiKey.Y, ctrl: true); r.Host.Frames(r.Launcher.Draw, 2);
        Assert.Equal(80, r.RoundsNow);
        r.Key(ImGuiKey.S, ctrl: true); r.Host.Frames(r.Launcher.Draw, 2);
        Assert.Equal(80, r.SavedRounds());
        Assert.False(r.Session.Dirty);
    }

    [Theory]
    [InlineData("Practice", "/presets/1", "0")]
    [InlineData("Practice", "/presets/1", "101")]
    [InlineData("Practice", "/presets/1", "1.5")]
    [InlineData("Default", "/presets/0", "80")]
    public void Other_bad_inputs_leave_the_document_history_and_disk_unchanged(string name, string pointer, string bad)
    {
        using var r = new Rig();
        r.Open();
        if (name == "Default")
        {
            // Establish a valid real-input positive control before testing the fixed preset.
            r.SelectPreset("Practice", "/presets/1");
            r.Rounds("80");
            Assert.Equal(80, r.RoundsNow);
        }
        r.SelectPreset(name, pointer);
        var before = r.Session.Current;
        var revision = r.Session.Revision;
        var undo = r.Session.History.UndoCount;
        var journal = r.Session.Journal.Count;
        var bytes = File.ReadAllBytes(r.Disk);
        var dirty = r.Session.Dirty;
        r.Rounds(bad);
        Assert.Same(before, r.Session.Current);
        Assert.Equal(revision, r.Session.Revision);
        Assert.Equal(undo, r.Session.History.UndoCount);
        Assert.Equal(journal, r.Session.Journal.Count);
        Assert.Equal(dirty, r.Session.Dirty);
        Assert.Equal(bytes, File.ReadAllBytes(r.Disk));
        Assert.False(string.IsNullOrWhiteSpace(r.Window.Status));
        if (name == "Default") Assert.Contains("Keep Default unchanged", r.Window.Status);
        if (bad == "1.5") Assert.Contains("whole number", r.Window.Status);
        if (name == "Practice")
        {
            r.Rounds("80"); // A different valid value clears the refusal through the actual field.
            Assert.Equal(80, r.RoundsNow);
            Assert.Equal(undo + 1, r.Session.History.UndoCount);
            Assert.Equal(bytes, File.ReadAllBytes(r.Disk));
        }
        else
        {
            Assert.Equal(60, r.Session.Current.At("/presets/0/rounds")!.Value.GetInt32());
            Assert.Equal(80, r.RoundsNow); // Earlier positive edit survives the fixed-preset refusal.
        }
    }

    [Fact]
    public void Palette_Save_Undo_Redo_preserve_draft_disk_and_history()
    {
        using var r = new Rig();
        r.Open();
        r.SelectPreset("Practice", "/presets/1");
        Assert.Equal(60, r.RoundsNow);
        Assert.False(r.Session.History.CanUndo);
        Assert.False(r.Session.History.CanRedo);
        var original = File.ReadAllBytes(r.Disk);
        r.Rounds("80");
        Assert.Equal(80, r.RoundsNow);
        Assert.Equal(60, r.Session.Current.At("/presets/0/rounds")!.Value.GetInt32());
        Assert.True(r.Session.Dirty);
        Assert.Equal(1, r.Session.History.UndoCount);
        Assert.Equal(original, File.ReadAllBytes(r.Disk));
        var edited = r.Session.Current;
        var revision = r.Session.Revision;

        r.Palette("editor.save");
        Assert.Equal("Saved settings.json.", r.Window.Status);
        Assert.Same(edited, r.Session.Current);
        Assert.Equal(revision, r.Session.Revision);
        Assert.Equal(1, r.Session.History.UndoCount);
        Assert.False(r.Session.Dirty);
        Assert.Equal(80, r.SavedRounds());
        var saved = File.ReadAllBytes(r.Disk);

        // Reopening with a different query must replace the old Save query.
        r.Palette("editor.undo");
        Assert.Equal("Undid: Set a value.", r.Window.Status);
        Assert.Equal(60, r.RoundsNow);
        Assert.Equal(0, r.Session.History.UndoCount);
        Assert.False(r.Session.History.CanUndo);
        Assert.True(r.Session.History.CanRedo);
        Assert.True(r.Session.Dirty);
        Assert.Equal(saved, File.ReadAllBytes(r.Disk));

        r.Palette("editor.redo");
        Assert.Equal("Redid: Set a value.", r.Window.Status);
        Assert.Equal(80, r.RoundsNow);
        Assert.Equal(60, r.Session.Current.At("/presets/0/rounds")!.Value.GetInt32());
        Assert.Equal(1, r.Session.History.UndoCount);
        Assert.False(r.Session.History.CanRedo);
        Assert.False(r.Session.Dirty);
        Assert.Equal(saved, File.ReadAllBytes(r.Disk));

        // Repeated invocation must close too; saving must not create another edit.
        revision = r.Session.Revision;
        r.Palette("editor.save");
        Assert.Equal("Saved settings.json.", r.Window.Status);
        Assert.Equal(revision, r.Session.Revision);
        Assert.Equal(1, r.Session.History.UndoCount);
        Assert.False(r.Session.Dirty);
        Assert.Equal(saved, File.ReadAllBytes(r.Disk));
    }
}
