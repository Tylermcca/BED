using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Checkers;
using Xunit;

namespace Bed.Tests;

/// <summary>Goal 13, the progress milestone: checkers made with the tools. The rules on hand-made positions, refusals
/// that say why, a whole game, a replayed match, and play by clicking in the editor window with no window.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class CheckersTests
{
    private static readonly string? Shots = Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS");

    // Builds a position from rows (top first), "." for any empty square; light squares are filled in. A piece written on a
    // light square fails the test rather than vanishing.
    private static Game Position(Side toMove, params string[] rows)
    {
        var n = rows.Length;
        return new Game([.. rows.Select((row, r) => new string(row.Select((ch, c) =>
        {
            if (Game.Dark(n, r, c)) return ch;
            Assert.True(ch == '.', $"the test put '{ch}' on a light square (row {r}, column {c})");
            return '-';
        }).ToArray()))], toMove);
    }

    private static string Moves(Game g) => string.Join(" ", Rules.LegalMoves(g).Select(m => string.Join("-", m)).Order(StringComparer.Ordinal));

    [Fact]
    public void TheOpeningHasSevenMovesForDarkAndRoundTrips()
    {
        var g = Game.Opening();
        Assert.Empty(new GameValidator().Validate(g));
        Assert.Equal("a3-b4 c3-b4 c3-d4 e3-d4 e3-f4 g3-f4 g3-h4", Moves(g));
        var f = new GameFormat();
        Assert.Equal(f.Write(g), f.Write(f.Read(f.Write(g))));
        Assert.Equal((12, 12), Rules.Count(g));
    }

    [Fact]
    public void ABadBoardIsRefusedSayingWhy()
    {
        var g = Game.Opening();
        var light = new Game(g.Rows.SetItem(7, g.Rows[7][..1] + "d" + g.Rows[7][2..]), Side.Dark);
        Assert.Contains(new GameValidator().Validate(light), i => i.Message.StartsWith("a piece can't stand on a light square"));
        var crowned = Position(Side.Dark, ".d......", "........", "........", "........", "........", "........", "........", "........");
        Assert.Contains(new GameValidator().Validate(crowned), i => i.Message.Contains("on its crowning row; make it a king"));
    }

    [Fact]
    public void ACaptureMustBeTakenAndAnyCaptureMayBeChosen()
    {
        var g = Position(Side.Dark,
            "........",
            "........",
            "........",
            "........",
            "...l.l..",
            "....d...",
            "........",
            "........");
        Assert.Equal("e3-c5 e3-g5", Moves(g));   // two captures; the plain steps are gone
    }

    [Fact]
    public void AMultiJumpMustBeFinishedAndBranches()
    {
        var g = Position(Side.Dark,
            "........",
            "........",
            "...l.l..",
            "........",
            "...l....",
            "..d.....",
            "........",
            "........");
        Assert.Equal("c3-e5-c7 c3-e5-g7", Moves(g));
        var s = CheckersPlugin.Session(g);
        foreach (var (id, v, label) in new (string, int, string)[] { ("checkers.move", 1, "Play") }) s.Catalog.Register<MovePiece>(id, v, label);
        Assert.False(s.Execute(new MovePiece(["c3", "e5"])));
        Assert.Equal("keep jumping: the capture from c3 isn't finished", s.LastIssues[0].Message);
        Assert.True(s.Execute(new MovePiece(["c3", "e5", "g7"])));
        Assert.Equal((1, 1), Rules.Count(s.Current));
    }

    [Fact]
    public void AManCrownedByACaptureStopsThere()
    {
        var g = Position(Side.Dark,
            "........",
            "..l.l...",
            "...d....",
            "........",
            "........",
            "........",
            "........",
            "........");
        // d6 jumps c7 to b8 and is crowned: it may not go on (it could otherwise take e7 as a king later).
        Assert.Contains("d6-b8", Moves(g).Split(' '));
        var after = Rules.Apply(g, ["d6", "b8"]);
        Assert.Equal('D', after.At(0, 1));
        Assert.Equal(Side.Light, after.ToMove);
    }

    [Fact]
    public void KingsMoveAndCaptureBackwardsOneSquareAtATime()
    {
        var g = Position(Side.Dark,
            "........",
            "........",
            "........",
            "....D...",
            "...l....",
            "........",
            "........",
            "........");
        Assert.Equal("e5-c3", Moves(g));   // capture forced, backwards
        var free = Position(Side.Dark, "........", "........", "........", "....D...", "........", "........", "........", "........");
        Assert.Equal("e5-d4 e5-d6 e5-f4 e5-f6", Moves(free));   // one square at a time: no flying kings
    }

    [Fact]
    public void ASideWithNoMoveLoses()
    {
        // Light's only man, on a3, is blocked by b2, and c1 behind it stops the jump.
        var blocked = Position(Side.Light,
            "........",
            "........",
            "........",
            "........",
            "........",
            "l.......",
            ".d......",
            "..d.....");
        Assert.Empty(Rules.LegalMoves(blocked));
        Assert.Equal(Side.Dark, Rules.Winner(blocked));
        var s = Session(blocked);
        Assert.False(s.Execute(new MovePiece(["a3", "b2"])));
        Assert.Equal("the game is over: dark won", s.LastIssues[0].Message);
    }

    [Fact]
    public void RefusalsSayWhyInAPlayersWords()
    {
        var s = Session(Game.Opening());
        Assert.False(s.Execute(new MovePiece(["b6", "a5"])));
        Assert.Equal("it's dark's turn, and b6 is a light man", s.LastIssues[0].Message);
        Assert.False(s.Execute(new MovePiece(["d4", "e5"])));
        Assert.Equal("there's no piece on d4", s.LastIssues[0].Message);
        Assert.False(s.Execute(new MovePiece(["c3", "c4"])));
        Assert.Equal("the dark man on c3 can't move there", s.LastIssues[0].Message);
        Assert.False(s.Dirty);
    }

    private static DocumentSession<Game> Session(Game g)
    {
        var s = CheckersPlugin.Session(g);
        s.Catalog.Register<MovePiece>("checkers.move", 1, "Play a move");
        return s;
    }

    [Fact]
    public void TheComputerBeatsAPlayerWhoAlwaysPlaysTheFirstMoveAndTheMatchReplays()
    {
        var s = Session(Game.Opening());
        for (var ply = 0; ply < 300 && Rules.Winner(s.Current) is null; ply++)
        {
            var move = s.Current.ToMove == Side.Dark ? Computer.Choose(s.Current)! : new MovePiece(Rules.LegalMoves(s.Current)[0]);
            Assert.True(s.Execute(move), string.Join("; ", s.LastIssues));
        }
        Assert.Equal(Side.Dark, Rules.Winner(s.Current));
        var replay = DocumentSession<Game>.Replay(Game.Opening(), s.Journal, new GameFormat(), new GameValidator(), s.Catalog);
        Assert.Equal(new GameFormat().Write(s.Current), new GameFormat().Write(replay.Current));
        Assert.True(s.Undo());
        Assert.Null(Rules.Winner(s.Current));   // undo takes the last move back
    }

    [Fact]
    public void APositionIsSetUpInTheInspectorLikeAnyData()
    {
        var (editor, inspector, _, _) = Editor(Game.Opening());
        editor.Select("d4");
        Assert.True(inspector.Set("piece", "dark king"));
        Assert.Equal('D', editor.Session.Current.At(4, 3));
        Assert.Equal("dark king", inspector.Rows().Single(r => r.Id == "piece").Value);
        Assert.True(editor.Session.Undo());
        Assert.Equal('.', editor.Session.Current.At(4, 3));
    }

    private static (EditorModel<Game> Editor, Inspector<Game> Inspector, EditorWindow<Game> Window, CheckersPlugin Plugin) Editor(Game g)
    {
        var editor = new EditorModel<Game>(CheckersPlugin.Session(g), CheckersPlugin.Outline);
        var inspector = new Inspector<Game>(editor);
        var shortcuts = new Shortcuts();
        var plugin = new CheckersPlugin();
        Assert.True(new PluginHost<Game>(editor, inspector, shortcuts).Add(plugin));
        return (editor, inspector, new EditorWindow<Game>(editor, inspector, shortcuts), plugin);
    }

    [Fact]
    public void AGameIsPlayedByClickingInTheEditorWindowAgainstTheComputer()
    {
        var (editor, _, window, plugin) = Editor(Game.Opening());
        using var host = new HeadlessHost(1400, 860);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(700, 560), ImGuiCond.Always);
            window.Draw();
        }
        host.Frames(Draw);
        Assert.True(plugin.LastBoard.Cell > 12);

        // A person plays c3-d4 by clicking; the computer answers from the palette (its maker); the person plays on.
        void Play(params string[] squares)
        {
            foreach (var sq in squares)
            {
                var at = plugin.ScreenCentre(editor.Session.Current, sq);
                host.Click(Draw, at.X, at.Y);
            }
        }
        Play("c3", "d4");
        Assert.Equal("Played c3-d4.", plugin.Said);
        Assert.Equal(Side.Light, editor.Session.Current.ToMove);
        if (Shots is { Length: > 0 } dir) host.Save(Path.Combine(Directory.CreateDirectory(dir).FullName, "10_checkers_first_move.png"));
        Assert.True(window.Run("checkers.move"), window.Status);   // the computer, through the palette
        Assert.Equal(Side.Dark, editor.Session.Current.ToMove);

        // A wrong click is refused with the rules' reason and changes nothing.
        var before = new GameFormat().Write(editor.Session.Current);
        Play("a3", "a4");
        Assert.Equal(before, new GameFormat().Write(editor.Session.Current));

        // Then the person plays the first legal move each turn by clicking, the computer answers, to the end.
        host.Drawing = false;
        for (var turn = 0; turn < 150 && Rules.Winner(editor.Session.Current) is null; turn++)
        {
            if (editor.Session.Current.ToMove == Side.Dark) Play([.. Rules.LegalMoves(editor.Session.Current)[0]]);
            else Assert.True(window.Run("checkers.move"), window.Status);
        }
        host.Drawing = true;
        host.Frames(Draw, 2);
        if (Shots is { Length: > 0 } d2) host.Save(Path.Combine(Directory.CreateDirectory(d2).FullName, "11_checkers_end.png"));
        Assert.NotNull(Rules.Winner(editor.Session.Current));
        var replay = DocumentSession<Game>.Replay(Game.Opening(), editor.Session.Journal, new GameFormat(), new GameValidator(), editor.Session.Catalog);
        Assert.Equal(new GameFormat().Write(editor.Session.Current), new GameFormat().Write(replay.Current));
    }

    [Fact]
    public void AGameIsSavedFromTheWindowAndPlaysOnAfterReloading()
    {
        var dir = Directory.CreateTempSubdirectory("bed-checkers-").FullName;
        try
        {
            var (editor, _, window, plugin) = Editor(Game.Opening());
            using var host = new HeadlessHost(1400, 860);
            void Draw()
            {
                ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Brutal.Numerics.float2(700, 560), ImGuiCond.Always);
                window.Draw();
            }
            host.Frames(Draw);
            foreach (var sq in new[] { "e3", "f4" })
            {
                var at = plugin.ScreenCentre(editor.Session.Current, sq);
                host.Click(Draw, at.X, at.Y);
            }
            Assert.True(window.Run("checkers.move"), window.Status);   // the computer answers

            // Save with no file yet: the window asks where. Type a path over the suggested one and press Enter.
            Assert.False(window.Run("editor.save"));
            Assert.True(window.SaveAsOpen);
            host.Frames(Draw, 2);
            host.Press(Draw, ImGuiKey.A, ctrl: true);
            var file = Path.Combine(dir, "match.json");
            host.Type(Draw, file);
            host.Press(Draw, ImGuiKey.Enter);
            if (Shots is { Length: > 0 } d) host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "12_checkers_saved.png"));
            Assert.Equal("Saved to match.json.", window.Status);
            Assert.False(window.SaveAsOpen);
            Assert.False(editor.Session.Dirty);

            // Reload it and play on from the same position.
            var reopened = DocumentSession<Game>.Open(file, new GameFormat(), new GameValidator(), editor.Session.Catalog);
            Assert.Equal(new GameFormat().Write(editor.Session.Current), new GameFormat().Write(reopened.Current));
            Assert.Equal(Side.Dark, reopened.Current.ToMove);
            Assert.True(reopened.Execute(Computer.Choose(reopened.Current)!));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SaveAsSaysWhyWhenItCantAndEscapeCancels()
    {
        var (editor, _, window, _) = Editor(Game.Opening());
        Assert.True(window.Run("editor.file_save_as"));
        Assert.True(window.SaveAsOpen);
        Assert.False(window.SaveAs("   "));
        Assert.Equal("Type a file name to save to.", window.Status);
        Assert.False(window.SaveAs(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"), "x.json")));
        Assert.StartsWith("Couldn't save to", window.Status);
        Assert.True(window.HandleKey(Chord.Parse("Escape")));
        Assert.False(window.SaveAsOpen);
        Assert.Null(editor.Session.FilePath);
    }

    // ---- goal 14: play from the editor ----

    [Fact]
    public void PlayRunsAMatchFromTheAuthoredPositionAndStopLeavesTheDocumentUntouched()
    {
        var (editor, inspector, window, _) = Editor(Game.Opening());
        editor.Select("d4");
        Assert.True(inspector.Set("piece", "dark king"));   // author a position: an extra dark king
        var file = new GameFormat().Write(editor.Session.Current);
        var journal = JournalFile.Write(editor.Session.Journal);
        var canUndo = editor.Session.History.CanUndo;

        using var host = new HeadlessHost(1400, 860);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(700, 560), ImGuiCond.Always);
            window.Draw();
        }
        host.Frames(Draw);
        Assert.True(window.Run(EditorWindow<Game>.PlayAction + "checkers.match"), window.Status);
        host.Frames(Draw, 2);
        var match = Assert.IsType<Match>(editor.Game);
        Assert.Equal('D', match.Current.At(4, 3));   // the match starts from the authored board

        // While playing, edits are locked everywhere: commands, the inspector, undo and save.
        Assert.False(editor.Execute(new SetSquare("a3", '.')));
        Assert.Contains("is playing; stop it to edit", editor.Refused[0].Message);
        Assert.False(inspector.Set("piece", "empty"));
        Assert.False(window.Run("editor.undo"));
        Assert.Equal("'Match' is playing; stop it first.", window.Status);

        // Play by clicking against the computer to the end.
        host.Drawing = false;
        for (var turn = 0; turn < 200 && Rules.Winner(match.Current) is null; turn++)
        {
            if (match.Current.ToMove == Side.Dark)
                foreach (var sq in Rules.LegalMoves(match.Current)[0])
                {
                    var at = Board.Centre(match.Current, match.LastBoard, sq);
                    host.Click(Draw, at.X, at.Y);
                }
            else match.Play(Computer.Choose(match.Current)!);
        }
        host.Drawing = true;
        host.Frames(Draw, 2);
        if (Shots is { Length: > 0 } d) host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "13_checkers_play.png"));
        Assert.NotNull(Rules.Winner(match.Current));
        Assert.StartsWith(Rules.Winner(match.Current).ToString(), match.Status);

        // Stop: back to editing, exactly as before Play.
        Assert.True(window.Run("editor.stop"));
        Assert.Null(editor.Playing);
        Assert.Equal(file, new GameFormat().Write(editor.Session.Current));
        Assert.Equal(journal, JournalFile.Write(editor.Session.Journal));
        Assert.Equal(canUndo, editor.Session.History.CanUndo);
        Assert.True(inspector.Set("piece", "empty"));   // editing works again
    }

    private sealed class Broken : IPlaySession
    {
        public string Status => "about to fail";
        public void Draw() => throw new InvalidOperationException("the board fell off the table");
        public void Dispose() { }
    }

    [Fact]
    public void AGameThatFailsIsStoppedAndReportedAndEditingCarriesOn()
    {
        var (editor, _, window, _) = Editor(Game.Opening());
        editor.Add(new PlayMode<Game>("test.broken", "Broken game", _ => new Broken()));
        Assert.True(window.Run(EditorWindow<Game>.PlayAction + "test.broken"));
        EditorWindowTests.Frames(window.Draw);
        Assert.Null(editor.Playing);
        Assert.Equal("Broken game failed and was stopped: the board fell off the table", window.Status);
        Assert.False(editor.Play("test.missing"));
        Assert.Equal("there's no play mode 'test.missing'", editor.Refused[0].Message);
        Assert.True(editor.Execute(new SetSquare("d4", 'd')));
    }

    [Fact]
    public void ThePaletteOffersPlayThenStopAndClicksPlayTheMatch()
    {
        var (editor, _, window, _) = Editor(Game.Opening());
        Assert.Contains(window.PaletteEntries("play"), e => e.Id == "editor.play.checkers.match" && e.Label == "Play Match");
        Assert.True(window.Run("editor.play.checkers.match"));
        Assert.Contains(window.PaletteEntries("stop"), e => e.Id == "editor.stop" && e.Label == "Stop playing Match");
        var match = (Match)editor.Game!;
        Assert.Equal("Picked c3.", match.Click("c3"));
        Assert.Equal("Played c3-d4.", match.Click("d4"));
        Assert.Equal("Pick one of light's pieces.", match.Click("e3"));
        Assert.False(editor.Play("checkers.match"));   // already playing
        Assert.True(editor.Stop());
        Assert.False(editor.Stop());
    }
}
