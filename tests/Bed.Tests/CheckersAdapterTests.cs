using System.Diagnostics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor.ImGui;
using Checkers;
using Xunit;

namespace Bed.Tests;

/// <summary>Checkers in the general app (the user, 2026-11-01: author in the editor, play inside it while making it, and
/// in its own window as the game), loaded the way the app loads it.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class CheckersAdapterTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("checkers-adapter-").FullName;
    private readonly List<ProcessStartInfo> started = [];

    public CheckersAdapterTests()
    {
        var sample = Path.Combine(Repo(), "samples", "CheckersProject");
        foreach (var f in Directory.EnumerateFiles(sample, "*.json", SearchOption.AllDirectories))
        {
            var to = Path.Combine(root, Path.GetRelativePath(sample, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Bed.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    // The adapter's own built DLL, as the app finds it in its adapters folder.
    private static string AdapterDll() =>
        Directory.EnumerateFiles(Repo(), "CheckersAdapter.dll", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains("ref" + Path.DirectorySeparatorChar)
                && !Path.GetRelativePath(Repo(), f).StartsWith("codex" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))   // never Codex's scratch inside this repo, wherever the repo itself sits (found by A190)
            .OrderByDescending(File.GetLastWriteTimeUtc).First();

    private ProjectLauncher Opened(string file = "kings_endgame.json")
    {
        var (loaded, problems) = Adapters.Load([AdapterDll()]);
        Assert.Empty(problems);
        // No window opens: the adapter's launch hook (on the type the app loaded) records what it would start.
        loaded[0].GetType().GetProperty("Launch")!.SetValue(null, new Func<ProcessStartInfo, bool>(info => { started.Add(info); return true; }));
        var l = new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json"))) { Loaded = loaded };
        Assert.True(l.OpenFolder(root), l.Said);
        Assert.Equal("Checkers", l.Using?.Name);   // recognised without being asked
        Assert.True(l.Workspace!.Show(file));
        return l;
    }

    private static dynamic Board(ProjectLauncher l) => l.Workspace!.Window!.Editor.Panels.Single(p => p.Id == "checkers.board").Draw.Target!;

    private static Game Position(ProjectLauncher l) => Checkers.Position.Read(l.Workspace!.Window!.Editor.Session.Current.Root.GetRawText());

    [Fact]
    public void A_position_gets_a_board_panel_and_a_match_to_play_inside_the_editor()
    {
        var l = Opened();
        var editor = l.Workspace!.Window!.Editor;
        Assert.Contains(editor.Panels, p => p.Id == "checkers.board");
        Assert.True(editor.Play("checkers.match"));
        var match = Assert.IsType<Match>(editor.Game);
        Assert.Equal(string.Join("/", Position(l).Rows), string.Join("/", match.Current.Rows));   // ImmutableArray compares by reference
        Assert.StartsWith("Played", match.Play(Assert.IsType<MovePiece>(Computer.Choose(match.Current))));
        Assert.True(editor.Stop());
        Assert.False(editor.Session.Dirty);   // play never edits the position
    }

    [Fact]
    public void Clicking_a_square_changes_its_piece_as_one_undoable_edit()
    {
        var l = Opened("opening.json");
        var board = Board(l);
        var before = Position(l);
        Assert.True((bool)board.Cycle("a5"));   // empty -> dark man
        Assert.Equal('d', Position(l).At(3, 0));
        Assert.True(l.Workspace!.Window!.Run("editor.undo"));
        Assert.Equal(string.Join("/", before.Rows), string.Join("/", Position(l).Rows));
        Assert.False((bool)board.Cycle("b5"));   // a light square holds nothing
        Assert.True((bool)board.SwitchTurn());
        Assert.Equal(Side.Light, Position(l).ToMove);
    }

    [Fact]
    public void A_man_never_stands_where_it_would_be_crowned_the_click_steps_on_to_a_king()
    {
        var l = Opened("opening.json");
        var board = Board(l);
        // h8 is on light's home row: a dark man there would be crowned, so the click goes on to a dark king.
        Assert.True((bool)board.Cycle("a1"));   // dark man -> dark king (a1 starts as a dark man)
        Assert.Equal('D', Position(l).At(7, 0));
    }

    [Fact]
    public void The_games_own_rules_refuse_a_board_the_game_would_refuse()
    {
        var l = Opened("opening.json");
        var session = l.Workspace!.Window!.Editor.Session;
        Assert.False(session.Execute(new SetValue("/board/0", JsonSerializer.SerializeToElement("dl-l-l-l"))));   // a piece on a light square
        Assert.NotEmpty(session.LastIssues);
        Assert.False(session.Dirty);
    }

    [Fact]
    public void Play_in_its_own_window_starts_the_game_on_the_position_being_edited_unsaved_edits_included()
    {
        var l = Opened("opening.json");
        var board = Board(l);
        Assert.True((bool)board.SwitchTurn());   // unsaved
        Assert.True((bool)board.PlayInOwnWindow());
        var start = Assert.Single(started);
        Assert.Contains("play-checkers", string.Join(" ", start.ArgumentList) + start.FileName);
        var copy = start.ArgumentList[^1];
        Assert.Equal(Side.Light, Checkers.Position.Read(File.ReadAllText(copy)).ToMove);
        Assert.Contains("\"dark\"", File.ReadAllText(Path.Combine(root, "data", "opening.json")));   // the file itself is untouched
    }

    [Fact]
    public void Plain_data_is_not_mistaken_for_checkers()
    {
        var (loaded, _) = Adapters.Load([AdapterDll()]);
        Assert.False(loaded[0].Recognises(new ProjectPaths(Path.Combine(Repo(), "samples", "StarterProject", "data"), Path.Combine(Repo(), "samples", "StarterProject", "schemas"))));
    }
}
