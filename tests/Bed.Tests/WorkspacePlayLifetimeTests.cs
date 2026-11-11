using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>B4's generic half: Play belongs to the workspace. At most one file's game runs; it stops before another file is
/// shown, before its file is renamed or deleted, and before the project is closed, replaced or the host shuts down; a cancelled
/// close leaves it running; a play mode can read the whole project's drafts through the workspace it was configured with. The
/// games here are fakes that count their disposal: no game code runs. INVENTED fixtures.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class WorkspacePlayLifetimeTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("playlifetime-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private string Settings => Path.Combine(root, "settings", "recent.json");

    public WorkspacePlayLifetimeTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "wares.schema.json"), """{"type":"object","properties":{"wares":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"price":{"type":"number"}}}}}}""");
        File.WriteAllText(Path.Combine(Schemas, "crew.schema.json"), """{"type":"object","properties":{"crew":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}}}}}}""");
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"$schema":"wares.schema.json","wares":[{"name":"Rope","price":2}]}""");
        File.WriteAllText(Path.Combine(Data, "crew.json"), """{"crew":[{"name":"Ana"}]}""");
        File.WriteAllText(Path.Combine(Schemas, "tools.schema.json"), """{"type":"object","properties":{"tools":{"type":"array","items":{"type":"string"}}}}""");
        File.WriteAllText(Path.Combine(Data, "tools.json"), """{"tools":[5]}""");   // doesn't fit its schema: opens only for repair
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    /// <summary>A game that only counts: how often it was disposed, and what it was started with.</summary>
    private sealed class FakeGame(string seen, bool throws = false, Func<bool>? fileThere = null) : IPlaySession
    {
        public int Disposed;
        public bool? FileThereAtStop;   // whether its file was still on disk, under its name, when it was stopped
        public string Seen => seen;
        public void Draw() { }
        public string Status => "playing " + seen;
        public void Dispose()
        {
            Disposed++;
            FileThereAtStop ??= fileThere?.Invoke();
            if (throws) throw new InvalidOperationException("the fake game broke while stopping");
        }
    }

    /// <summary>An adapter whose play mode, on any file, starts a fake game from both files' current drafts, read through the
    /// workspace and file it was configured with.</summary>
    private sealed class TwoFileAdapter : IProjectAdapter
    {
        public readonly List<FakeGame> Games = [];
        public readonly List<(DataWorkspace Workspace, string File)> Configured = [];
        public bool Throws;
        public string Name => "Two files";
        public DataProject Open(ProjectPaths project) => DataProject.Open(project.Data, project.Schemas);
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) => throw new InvalidOperationException("the general app calls the workspace overload");

        public void Configure(DataWorkspace workspace, DataFile file, EditorModel<DataDocument> editor, DataTableView table)
        {
            Configured.Add((workspace, file.Name));
            editor.Add(new PlayMode<DataDocument>("two.files", "Both files", _ =>
            {
                var wares = workspace.Project.Find("wares.json")?.Session?.Current.At("/wares/0/name")?.GetString();
                var crew = workspace.Project.Find("crew.json")?.Session?.Current.At("/crew/0/name")?.GetString();
                var path = file.Path;
                var game = new FakeGame($"{wares}/{crew}", Throws, () => File.Exists(path));
                Games.Add(game);
                return game;
            }));
        }
    }

    private (ProjectLauncher Launcher, TwoFileAdapter Adapter) Launch()
    {
        var adapter = new TwoFileAdapter();
        var launcher = new ProjectLauncher(new RecentProjects(Settings)) { Adapter = adapter };
        Assert.True(launcher.Open(new ProjectPaths(Data, Schemas)), launcher.Said);
        return (launcher, adapter);
    }

    private static FakeGame Play(DataWorkspace w, string file)
    {
        Assert.True(w.Show(file), w.Said);
        Assert.True(w.Window!.Run(EditorWindow<DataDocument>.PlayAction + "two.files"), w.Window.Status);
        return (FakeGame)w.Window.Editor.Game!;
    }

    [Fact]
    public void The_general_app_configures_each_file_with_its_workspace_and_play_reads_the_other_files_unsaved_draft()
    {
        var (l, a) = Launch();
        var w = l.Workspace!;
        Assert.True(w.Show("crew.json"));
        Assert.True(w.Window!.Editor.Execute(new SetValue("/crew/0/name", Json("\"Bea\""))));
        var game = Play(w, "wares.json");   // showing wares.json stopped nothing: nothing was playing
        Assert.Equal("Rope/Bea", game.Seen);   // crew.json's unsaved name, from the workspace, not the disk
        Assert.Contains("\"Ana\"", File.ReadAllText(Path.Combine(Data, "crew.json")));
        Assert.All(a.Configured, c => Assert.Same(w, c.Workspace));
        Assert.Equal(["crew.json", "wares.json"], a.Configured.Select(c => c.File).Order());
        Assert.Same(w.Window.Editor, w.Playing);
    }

    [Fact]
    public void Showing_another_file_stops_the_game_first_and_keeps_every_draft()
    {
        var (l, _) = Launch();
        var w = l.Workspace!;
        Assert.True(w.Show("wares.json"));
        Assert.True(w.Window!.Editor.Execute(new SetValue("/wares/0/price", Json("5"))));
        var game = Play(w, "wares.json");
        Assert.True(w.Show("wares.json"));   // the same file again: it plays on
        Assert.Equal(0, game.Disposed);
        Assert.True(w.Show("crew.json"));
        Assert.Equal(1, game.Disposed);
        Assert.Null(w.Playing);
        Assert.True(w.Show("wares.json"));
        Assert.Null(w.Window!.Editor.Playing);
        Assert.Equal(5, w.Window.Editor.Session.Current.At("/wares/0/price")!.Value.GetDouble());
        Assert.True(w.Window.Editor.Session.Dirty);
        Assert.False(w.StopPlay());   // nothing left to stop
        Assert.Equal(1, game.Disposed);
    }

    [Fact]
    public void Going_to_a_link_in_another_file_stops_the_game_too()
    {
        var (l, _) = Launch();
        var w = l.Workspace!;
        var game = Play(w, "wares.json");
        Assert.True(w.GoTo(new ReferenceTarget("crew.json", "/crew/0")), w.Said);
        Assert.Equal(1, game.Disposed);
    }

    [Fact]
    public void A_second_file_cant_start_a_game_while_one_plays_and_says_which()
    {
        var (l, a) = Launch();
        var w = l.Workspace!;
        Assert.True(w.Show("crew.json"));
        var crew = w.Window!;
        var game = Play(w, "wares.json");
        // Reached directly, not through Show (which would stop the first): refused, naming what plays where.
        Assert.False(crew.Editor.Play("two.files"));
        Assert.Contains("'Both files' is playing in wares.json; stop it first", crew.Editor.Refused.Single().Message);
        Assert.Single(a.Games);
        Assert.Equal(0, game.Disposed);
    }

    [Fact]
    public void Renaming_or_deleting_the_playing_file_stops_its_game_first()
    {
        var (l, _) = Launch();
        var w = l.Workspace!;
        var game = Play(w, "wares.json");
        Assert.True(w.RenameFile("wares.json", "goods.json"), w.Said);
        Assert.Equal(1, game.Disposed);
        Assert.True(game.FileThereAtStop);   // stopped before the rename moved the file
        Assert.False(File.Exists(Path.Combine(Data, "wares.json")));
        Assert.True(w.Show("crew.json"));
        var again = Play(w, "crew.json");
        Assert.True(w.DeleteFile("crew.json"), w.Said);
        Assert.Equal(1, again.Disposed);
        Assert.True(again.FileThereAtStop);   // stopped before the delete removed it
        Assert.False(File.Exists(Path.Combine(Data, "crew.json")));
        Assert.Null(w.Playing);
    }

    [Fact]
    public void Repairing_another_file_stops_the_game_first()
    {
        var (l, _) = Launch();
        var w = l.Workspace!;
        var game = Play(w, "wares.json");
        Assert.True(w.Repair("tools.json"), w.Said);
        Assert.Equal(1, game.Disposed);
        Assert.Null(w.Playing);
    }

    [Fact]
    public void A_failure_while_stopping_is_kept_when_going_to_a_link_or_back()   // found by A236
    {
        const string failed = "failed while stopping";
        var (l, a) = Launch();
        a.Throws = true;
        var w = l.Workspace!;
        var game = Play(w, "wares.json");
        Assert.True(w.GoTo(new ReferenceTarget("crew.json", "/crew/0")), w.Said);
        Assert.StartsWith("Went to", w.Said);
        Assert.Contains(failed, w.Said);
        Assert.Equal(1, game.Disposed);
        Assert.Null(w.Playing);

        game = Play(w, "crew.json");
        Assert.True(w.Back(), w.Said);
        Assert.StartsWith("Back to wares.json.", w.Said);
        Assert.Contains(failed, w.Said);
        Assert.Equal(1, game.Disposed);
        Assert.Null(w.Playing);

        Assert.True(w.GoTo(new ReferenceTarget("crew.json", "/crew/0")), w.Said);
        Assert.DoesNotContain(failed, w.Said);   // nothing was playing this time, so nothing is said about stopping
    }

    [Fact]
    public void A_failure_while_stopping_is_said_on_rename_delete_close_and_opening_over_it()
    {
        const string failed = "failed while stopping";
        var (l, a) = Launch();
        a.Throws = true;
        var w = l.Workspace!;
        Play(w, "wares.json");
        Assert.True(w.RenameFile("wares.json", "goods.json"), w.Said);
        Assert.Contains(failed, w.Said);
        Assert.True(w.Show("crew.json"));
        Play(w, "crew.json");
        Assert.True(w.DeleteFile("crew.json"), w.Said);
        Assert.Contains(failed, w.Said);
        Assert.Null(w.Playing);

        Play(w, "goods.json");
        Assert.True(l.Open(new ProjectPaths(Data, Schemas)), l.Said);
        Assert.Contains(failed, l.Said);
        Assert.Contains(failed, l.Workspace!.Said);   // the new workspace, what the person sees next
        Assert.Null(w.Playing);

        Play(l.Workspace!, "goods.json");
        Assert.True(l.Close(discard: true));
        Assert.Contains(failed, l.Said);
        Assert.StartsWith("Closed the project.", l.Said);
    }

    [Fact]
    public void Closing_the_project_stops_the_game_but_a_refused_close_leaves_it_playing()
    {
        var (l, _) = Launch();
        var w = l.Workspace!;
        Assert.True(w.Show("crew.json"));
        Assert.True(w.Window!.Editor.Execute(new SetValue("/crew/0/name", Json("\"Bea\""))));
        var game = Play(w, "wares.json");
        Assert.False(l.Close());   // unsaved edits: the close waits for an answer
        Assert.Equal(0, game.Disposed);
        Assert.Same(w, l.Workspace);
        Assert.NotNull(w.Playing);
        Assert.True(l.Close(discard: true));
        Assert.Equal(1, game.Disposed);
        Assert.Contains("\"Ana\"", File.ReadAllText(Path.Combine(Data, "crew.json")));   // nothing was saved on the way out
    }

    [Fact]
    public void Opening_another_project_over_it_or_shutting_down_stops_the_game()
    {
        var (l, _) = Launch();
        var game = Play(l.Workspace!, "wares.json");
        Assert.True(l.Open(new ProjectPaths(Data, Schemas)), l.Said);
        Assert.Equal(1, game.Disposed);
        var next = Play(l.Workspace!, "crew.json");
        l.Dispose();   // the host's window closed
        Assert.Equal(1, next.Disposed);
        Assert.NotNull(l.Workspace);   // shutting down stops the game; it doesn't close or save the project
        l.Dispose();
        Assert.Equal(1, next.Disposed);
    }

    [Fact]
    public void A_game_that_throws_while_stopping_is_still_stopped_and_the_workspace_says_why()
    {
        var (l, a) = Launch();
        a.Throws = true;
        var w = l.Workspace!;
        var game = Play(w, "wares.json");
        Assert.True(w.Show("crew.json"));   // the switch isn't blocked by the broken game
        Assert.Equal(1, game.Disposed);
        Assert.Null(w.Playing);
        Assert.Contains("failed while stopping", w.Said);
    }

    [Fact]
    public void An_adapter_with_only_the_old_configure_still_gets_it()
    {
        var calls = new List<string>();
        var launcher = new ProjectLauncher(new RecentProjects(Settings)) { Adapter = new OldAdapter(calls) };
        Assert.True(launcher.Open(new ProjectPaths(Data, Schemas)), launcher.Said);
        Assert.Single(calls);   // the first file shown
        Assert.Null(launcher.Workspace!.AdapterProblem);
    }

    private sealed class OldAdapter(List<string> calls) : IProjectAdapter
    {
        public string Name => "Old";
        public DataProject Open(ProjectPaths project) => DataProject.Open(project.Data, project.Schemas);
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) => calls.Add(editor.Session.FilePath!);
    }
}
