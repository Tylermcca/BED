using System.Collections.Immutable;
using System.Numerics;
using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Checkers;
using Xunit;

namespace Brutal.Authoring.Tests
{
    // Expectations come from public XML comments and goal 14, not existing play tests.
    // MIXED: Unity/Godot/Unreal's Play-return workflow, the library's immutable snapshot seam.
    // INVENTED: probe games, scene fixtures, and hand-derived checkers positions.
    [Collection(ImGuiCollection.Name)]
    public sealed class PlayAcceptanceTests
    {
        [Fact]
        public void InitiallyEditingHasNoGameOrMode()
        {
            using var f = new Fixture();
            Assert.Null(f.Editor.Game);
            Assert.Null(f.Editor.Playing);
            Assert.False(f.Editor.Stop());
        }

        [Fact]
        public void PlayReturnsTrueAndExposesModeAndGame()
        {
            using var f = new Fixture();
            var game = new Probe(f.Session.Current);
            var mode = new PlayMode<Scene>("probe.play", "Probe", _ => game);
            f.Editor.Add(mode);
            Assert.True(f.Editor.Play(mode.Id));
            Assert.Same(mode, f.Editor.Playing);
            Assert.Same(game, f.Editor.Game);
        }

        [Fact]
        public void StopReturnsTrueAndReturnsToEditing()
        {
            using var f = new Fixture();
            f.Start();
            Assert.True(f.Editor.Stop());
            Assert.Null(f.Editor.Game);
            Assert.Null(f.Editor.Playing);
            Assert.False(f.Editor.Stop());
        }

        [Fact]
        public void AlreadyPlayingRefusesWithoutStartingAnotherGame()
        {
            using var f = new Fixture();
            var running = f.Start();
            var starts = 0;
            f.Editor.Add(new PlayMode<Scene>("other.play", "Other", d => { starts++; return new Probe(d); }));
            Assert.False(f.Editor.Play("other.play"));
            Assert.NotEmpty(f.Editor.Refused);
            Assert.Same(running, f.Editor.Game);
            Assert.Equal(0, starts);
        }

        [Fact]
        public void UnknownPlayModeReturnsFalseWithAReason()
        {
            using var f = new Fixture();
            Assert.False(f.Editor.Play("missing.mode"));
            Assert.NotEmpty(f.Editor.Refused);
            Assert.Null(f.Editor.Game);
            Assert.Null(f.Editor.Playing);
        }

        [Fact]
        public void ThrowingStartIsReportedAndLeavesEditingUsable()
        {
            using var f = new Fixture();
            f.Editor.Add(new PlayMode<Scene>("probe.bad", "Broken start", _ => throw new InvalidOperationException("start sentinel")));
            Assert.False(f.Editor.Play("probe.bad"));
            Assert.Contains(f.Editor.Refused, i => i.Message.Contains("start sentinel", StringComparison.Ordinal));
            Assert.Null(f.Editor.Game);
            Assert.Null(f.Editor.Playing);
            Assert.True(f.Editor.Execute(new MoveObject("piece", new Vector3(9, 0, 0)), "piece"));
        }

        [Fact]
        public void StartReceivesTheCurrentAuthoredPosition()
        {
            using var f = new Fixture();
            Assert.True(f.Editor.Execute(new MoveObject("piece", new Vector3(12, 2, 3)), "piece"));
            var game = f.Start();
            Assert.Equal(new Vector3(12, 2, 3), game.Snapshot.Find("piece")!.Position);
            Assert.Equal(f.Session.Format.Write(f.Session.Current), f.Session.Format.Write(game.Snapshot));
        }

        [Fact]
        public void PastSnapshotDoesNotFollowEditsAfterStop()
        {
            using var f = new Fixture();
            var game = f.Start();
            var old = f.Session.Format.Write(game.Snapshot);
            Assert.True(f.Editor.Stop());
            Assert.True(f.Editor.Execute(new MoveObject("piece", new Vector3(22, 0, 0)), "piece"));
            Assert.Equal(old, f.Session.Format.Write(game.Snapshot));
            Assert.NotEqual(old, f.Session.Format.Write(f.Session.Current));
        }

        [Fact]
        public void RestartCreatesFreshGameFromLatestDocument()
        {
            using var f = new Fixture();
            var starts = new List<Probe>();
            f.Editor.Add(new PlayMode<Scene>("probe.repeat", "Repeat", d => { var g = new Probe(d); starts.Add(g); return g; }));
            Assert.True(f.Editor.Play("probe.repeat"));
            Assert.True(f.Editor.Stop());
            Assert.True(f.Editor.Execute(new MoveObject("piece", new Vector3(18, 0, 0)), "piece"));
            Assert.True(f.Editor.Play("probe.repeat"));
            Assert.Equal(2, starts.Count);
            Assert.NotSame(starts[0], starts[1]);
            Assert.Equal(3, starts[0].Snapshot.Find("piece")!.Position.X);
            Assert.Equal(18, starts[1].Snapshot.Find("piece")!.Position.X);
        }

        [Fact]
        public void ExecuteIsLockedWithAnEditorReason()
        {
            using var f = new Fixture();
            var before = f.Session.Format.Write(f.Session.Current);
            f.Start();
            Assert.False(f.Editor.Execute(new MoveObject("piece", new Vector3(40, 0, 0)), "piece"));
            AssertPlayReason(f.Editor.Refused.Select(i => i.Message));
            Assert.Equal(before, f.Session.Format.Write(f.Session.Current));
        }

        [Fact]
        public void InspectorSetIsLockedWithAnIssue()
        {
            using var f = new Fixture();
            f.Editor.Select("piece");
            f.Start();
            Assert.False(f.Inspector.Set("x", 40f));
            AssertPlayReason(f.Inspector.LastIssues.Select(i => i.Message));
            Assert.Equal(3, f.Session.Current.Find("piece")!.Position.X);
        }

        [Fact]
        public void InspectorResetIsLockedWithAnIssue()
        {
            using var f = new Fixture();
            f.Editor.Select("piece");
            Assert.True(Assert.Single(f.Inspector.Rows()).Overridden);
            f.Start();
            Assert.False(f.Inspector.Reset("x"));
            AssertPlayReason(f.Inspector.LastIssues.Select(i => i.Message));
            Assert.Equal(3, f.Session.Current.Find("piece")!.Position.X);
        }

        [Theory]
        [InlineData("editor.undo")]
        [InlineData("editor.redo")]
        [InlineData("editor.save")]
        [InlineData("editor.save_as")]
        public void WindowAuthoringActionsAreLockedWithAStatus(string id)
        {
            using var f = new Fixture();
            f.PrepareHistoryAndFile();
            var before = f.Capture();
            f.Start();
            Assert.False(f.Window.Run(id));
            AssertPlayReason([f.Window.Status]);
            Assert.False(f.Window.SaveAsOpen);
            Assert.True(f.Editor.Stop());
            f.AssertUnchanged(before);
        }

        [Fact]   // un-skipped by Claude on accept: SaveAs now refuses while playing
        public void SaveAsCannotWriteOrChangeFilePathWhilePlaying()
        {
            using var f = new Fixture();
            f.PrepareHistoryAndFile();
            var before = f.Capture();
            f.Start();
            var other = f.Path + ".other";
            Assert.False(f.Window.SaveAs(other), $"File exists: {File.Exists(other)}; session file: {f.Session.FilePath}");
            AssertPlayReason([f.Window.Status]);
            Assert.False(File.Exists(other));
            Assert.Equal(f.Path, f.Session.FilePath);
            Assert.True(f.Editor.Stop());
            f.AssertUnchanged(before);
        }

        [Fact]
        public void StopPreservesDirtyDocumentJournalUndoRedoAndDisk()
        {
            using var f = new Fixture();
            f.PrepareHistoryAndFile();
            var before = f.Capture();
            f.Start();
            Assert.True(f.Editor.Stop());
            f.AssertUnchanged(before);
            Assert.True(f.Session.Redo());
            Assert.Equal(5, f.Session.Current.Find("piece")!.Position.X);
            Assert.True(f.Session.Undo());
            Assert.Equal(4, f.Session.Current.Find("piece")!.Position.X);
            Assert.True(f.Session.Undo());
            Assert.Equal(3, f.Session.Current.Find("piece")!.Position.X);
        }

        [Fact]
        public void StopPreservesCleanSavedDocument()
        {
            using var f = new Fixture();
            f.Session.Save(f.Path);
            Assert.False(f.Session.Dirty);
            var before = f.Capture();
            f.Start();
            Assert.True(f.Editor.Stop());
            f.AssertUnchanged(before);
        }

        [Fact]
        public void PlayAndStopDoNotRaiseDocumentChanged()
        {
            using var f = new Fixture();
            var changes = 0;
            f.Session.Changed += () => changes++;
            f.Start();
            Assert.True(f.Editor.Stop());
            Assert.Equal(0, changes);
            Assert.Empty(f.Session.Journal);
        }

        [Fact]
        public void PanelCanBeClosedAndOpenedWhilePlaying()
        {
            using var f = new Fixture();
            f.Editor.Add(new Panel("probe.panel", "Probe panel", () => { }));
            f.Start();
            Assert.True(f.Editor.IsOpen("probe.panel"));
            Assert.True(f.Window.Run(EditorWindow<Scene>.PanelAction + "probe.panel"));
            Assert.False(f.Editor.IsOpen("probe.panel"));
            Assert.True(f.Window.Run(EditorWindow<Scene>.PanelAction + "probe.panel"));
            Assert.True(f.Editor.IsOpen("probe.panel"));
            Assert.Empty(f.Session.Journal);
        }

        [Fact]
        public void PaletteStillOpensWhilePlaying()
        {
            using var f = new Fixture();
            f.Start();
            Assert.True(f.Window.Run("editor.palette"));
            Assert.True(f.Window.PaletteOpen);
            Assert.Contains(f.Window.PaletteEntries("Stop"), e => e.Id == "editor.stop");
        }

        [Fact]
        public void PlayPaletteEntryUsesThePublicPrefixAndModeTitle()
        {
            using var f = new Fixture();
            f.Editor.Add(new PlayMode<Scene>("probe.play", "My preview", d => new Probe(d)));
            var entry = Assert.Single(f.Window.PaletteEntries("My preview"));
            Assert.Equal(EditorWindow<Scene>.PlayAction + "probe.play", entry.Id);
            Assert.Contains("My preview", entry.Label);
            Assert.Null(entry.Unavailable);
            Assert.True(f.Window.Run(entry.Id));
            Assert.NotNull(f.Editor.Game);
        }

        [Fact]
        public void StopPaletteEntryStopsTheRunningGame()
        {
            using var f = new Fixture();
            f.Start();
            var stop = Assert.Single(f.Window.PaletteEntries("Stop"));
            Assert.Equal("editor.stop", stop.Id);
            Assert.Null(stop.Unavailable);
            Assert.True(f.Window.Run(stop.Id));
            Assert.Null(f.Editor.Game);
        }

        [Fact]
        public void StopCallsDisposeExactlyOnce()
        {
            using var f = new Fixture();
            var game = f.Start();
            Assert.True(f.Editor.Stop());
            Assert.Equal(1, game.Disposals);
            Assert.False(f.Editor.Stop());
            Assert.Equal(1, game.Disposals);
        }

        [Fact]
        public void WindowDrawRunsGameFrameHeadlessly()
        {
            using var f = new Fixture();
            var game = f.Start();
            using var host = new HeadlessHost();
            host.Frames(f.Window.Draw);
            Assert.True(game.Draws > 0);
            Assert.True(host.Covered() > 0);
        }

        [Fact]
        public void ThrowingDrawStopsDisposesAndReportsGame()
        {
            using var f = new Fixture();
            var game = new Probe(f.Session.Current) { ThrowOnDraw = true };
            f.Editor.Add(new PlayMode<Scene>("probe.throw", "Throw preview", _ => game));
            Assert.True(f.Editor.Play("probe.throw"));
            using var host = new HeadlessHost();
            host.Frames(f.Window.Draw);
            Assert.Null(f.Editor.Game);
            Assert.Null(f.Editor.Playing);
            Assert.Equal(1, game.Disposals);
            Assert.Contains("draw sentinel", f.Window.Status);
            Assert.True(f.Editor.Execute(new MoveObject("piece", new Vector3(2, 0, 0)), "piece"));
        }

        [Fact]
        public void PluginRegistersOwnedPlayMode()
        {
            using var f = new Fixture();
            Assert.True(f.Plugins.Add(new TestPlugin("probe", a => a.PlayMode(Mode("probe.play")))));
            Assert.Equal("probe.play", Assert.Single(f.Editor.PlayModes).Id);
            Assert.True(f.Editor.Play("probe.play"));
        }

        [Fact]
        public void PluginCannotRegisterAnotherOwnersMode()
        {
            using var f = new Fixture();
            Assert.False(f.Plugins.Add(new TestPlugin("probe", a => a.PlayMode(Mode("other.play")))));
            Assert.Empty(f.Editor.PlayModes);
            Assert.NotEmpty(Assert.Single(f.Plugins.Plugins).Issues);
        }

        [Fact]
        public void ExistingModeCollisionRefusesEveryStagedAddition()
        {
            using var f = new Fixture();
            var original = Mode("probe.play");
            f.Editor.Add(original);
            Assert.False(f.Plugins.Add(new TestPlugin("probe", a => a
                .Panel(new Panel("probe.panel", "Never registered", () => { }))
                .PlayMode(Mode("probe.play")))));
            Assert.Same(original, Assert.Single(f.Editor.PlayModes));
            Assert.DoesNotContain(f.Editor.Panels, p => p.Id != BedAbout.Panel.Id && p.Id != KeysPanel.Id && p.Id != GettingStartedPanel.Id);
            Assert.NotEmpty(Assert.Single(f.Plugins.Plugins).Issues);
        }

        [Fact]
        public void DuplicateStagedModesRefuseAllRegistration()
        {
            using var f = new Fixture();
            Assert.False(f.Plugins.Add(new TestPlugin("probe", a => a
                .PlayMode(Mode("probe.same")).PlayMode(Mode("probe.same")))));
            Assert.Empty(f.Editor.PlayModes);
            Assert.False(Assert.Single(f.Plugins.Plugins).Enabled);
        }

        [Fact]
        public void InvalidPanelRollsBackStagedPlayMode()
        {
            using var f = new Fixture();
            Assert.False(f.Plugins.Add(new TestPlugin("probe", a => a
                .PlayMode(Mode("probe.good")).Panel(new Panel("other.bad", "Wrong owner", () => { })))));
            Assert.Empty(f.Editor.PlayModes);
            Assert.DoesNotContain(f.Editor.Panels, p => p.Id != BedAbout.Panel.Id && p.Id != KeysPanel.Id && p.Id != GettingStartedPanel.Id);
        }

        [Fact]
        public void ThrowingRegistrationDoesNotLeakPlayMode()
        {
            using var f = new Fixture();
            Assert.False(f.Plugins.Add(new TestPlugin("probe", a =>
            {
                a.PlayMode(Mode("probe.good"));
                throw new InvalidOperationException("registration sentinel");
            })));
            Assert.Empty(f.Editor.PlayModes);
            Assert.Contains(Assert.Single(f.Plugins.Plugins).Issues, i => i.Message.Contains("registration sentinel"));
        }

        [Fact]
        public void PlayModesKeepRegistrationOrder()
        {
            using var f = new Fixture();
            Assert.True(f.Plugins.Add(new TestPlugin("probe", a => a
                .PlayMode(Mode("probe.zebra")).PlayMode(Mode("probe.ant")))));
            Assert.Equal(new[] { "probe.zebra", "probe.ant" }, f.Editor.PlayModes.Select(m => m.Id));
        }

        [Fact]
        public void MatchStartsFromAuthoredBoardAndTurn()
        {
            var start = Position(8, Side.Light, ("c3", 'd'), ("h8", 'l'));
            using var match = new Match(start);
            Assert.Equal(new GameFormat().Write(start), new GameFormat().Write(match.Current));
            Assert.Contains("light", match.Status, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ClicksPlayOneLegalMoveAndPassTurn()
        {
            using var match = new Match(Position(8, Side.Dark, ("c3", 'd'), ("h8", 'l')));
            match.Click("c3");
            match.Click("b4");
            Assert.Equal('.', At(match.Current, "c3"));
            Assert.Equal('d', At(match.Current, "b4"));
            Assert.Equal(Side.Light, match.Current.ToMove);
        }

        [Fact]
        public void PartialCaptureSaysKeepJumpingWithoutCommittingPartialBoard()
        {
            var start = Position(8, Side.Dark, ("c3", 'd'), ("d4", 'l'), ("f6", 'l'));
            using var match = new Match(start);
            match.Click("c3");
            var said = match.Click("e5");
            Assert.Contains("jump", said, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new GameFormat().Write(start), new GameFormat().Write(match.Current));
            match.Click("g7");
            Assert.Equal('d', At(match.Current, "g7"));
            Assert.Equal('.', At(match.Current, "c3"));
            Assert.Equal('.', At(match.Current, "d4"));
            Assert.Equal('.', At(match.Current, "f6"));
            Assert.Equal(Side.Light, match.Current.ToMove);
        }

        [Fact]   // un-skipped by Claude on accept: Play now updates Said
        public void RuleRefusalIsPropagatedWithoutChangingMatch()
        {
            var start = Position(8, Side.Dark, ("c3", 'd'), ("d4", 'l'), ("h8", 'l'));
            var illegal = new MovePiece(new[] { "c3", "b4" }); // capture exists at e5: a quiet move is forbidden.
            var expected = illegal.Apply(start);
            Assert.False(expected.Ok);
            using var match = new Match(start);
            var said = match.Play(illegal);
            Assert.Contains(expected.Issues[0].Message, said);
            Assert.Equal(said, match.Said);
            Assert.Equal(new GameFormat().Write(start), new GameFormat().Write(match.Current));
        }

        [Fact]
        public void ClickingWrongSideRefusesWithAReason()
        {
            var start = Position(8, Side.Dark, ("c3", 'd'), ("h8", 'l'));
            using var match = new Match(start);
            Assert.False(string.IsNullOrWhiteSpace(match.Click("h8")));
            Assert.Equal(new GameFormat().Write(start), new GameFormat().Write(match.Current));
            match.Click("c3");
            match.Click("b4");
            Assert.Equal('d', At(match.Current, "b4"));
        }

        [Fact]
        public void WholeMatchByHeadlessBoardClicksReachesWinnerAndLeavesAuthoringUntouched()
        {
            var start = Position(4, Side.Dark, ("a1", 'd'), ("b2", 'l'));
            var session = CheckersPlugin.Session(start);
            var editor = new EditorModel<Game>(session, CheckersPlugin.Outline);
            editor.Add(new PlayMode<Game>("probe.match", "Acceptance match", d => new Match(d)));
            var window = new EditorWindow<Game>(editor);
            var before = session.Format.Write(session.Current);
            Assert.True(window.Run(EditorWindow<Game>.PlayAction + "probe.match"));
            var match = Assert.IsType<Match>(editor.Game);
            using var host = new HeadlessHost();
            host.Frames(window.Draw);
            ClickSquare(host, window.Draw, match, "a1");
            ClickSquare(host, window.Draw, match, "c3");
            Assert.Equal('d', At(match.Current, "c3"));
            Assert.Equal('.', At(match.Current, "b2"));
            Assert.Contains("dark", match.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("won", match.Status, StringComparison.OrdinalIgnoreCase);
            Assert.True(window.Run("editor.stop"));
            Assert.Equal(before, session.Format.Write(session.Current));
            Assert.Empty(session.Journal);
            Assert.False(session.History.CanUndo);
            Assert.False(session.Dirty);
        }

        [Fact]
        public void TakeBackRestoresPreviousMatchPositionOnly()
        {
            var start = Position(8, Side.Dark, ("c3", 'd'), ("h8", 'l'));
            using var match = new Match(start);
            match.Click("c3");
            match.Click("b4");
            Assert.Equal(Side.Light, match.Current.ToMove);
            using var host = new HeadlessHost();
            void Draw()
            {
                ImGui.SetNextWindowSize(new Brutal.Numerics.float2(600, 720));
                ImGui.Begin("Take back acceptance");
                match.Draw();
                ImGui.End();
            }
            host.Frames(Draw);
            // The headless frame locates Take back in the button row below the board.
            host.Click(Draw, match.LastBoard.Origin.X + 140, match.LastBoard.Origin.Y + match.Current.Size * match.LastBoard.Cell + 12);
            Assert.Equal(new GameFormat().Write(start), new GameFormat().Write(match.Current));
            Assert.Equal(Side.Dark, match.Current.ToMove);
        }

        [Fact]
        public void BoardCentreAndDrawRouteRealClickToSquareName()
        {
            var g = Position(4, Side.Dark, ("a1", 'd'), ("b2", 'l'));
            string? clicked = null;
            var at = default((Brutal.Numerics.float2 Origin, float Cell));
            using var host = new HeadlessHost();
            void Draw()
            {
                ImGui.SetNextWindowSize(new Brutal.Numerics.float2(600, 600));
                ImGui.Begin("Board acceptance");
                at = Board.Draw(g, [], s => clicked = s);
                ImGui.Dummy(new Brutal.Numerics.float2(1, 1));
                ImGui.End();
            }
            host.Frames(Draw);
            var centre = Board.Centre(g, at, "b2");
            host.Click(Draw, centre.X, centre.Y);
            Assert.Equal("b2", clicked);
        }

        [Fact]
        public void FullOpeningMatchThroughHeadlessClicksReachesWinner()
        {
            var start = Game.Opening();
            Assert.Equal(8, start.Size);
            Assert.Equal(12, start.Rows.Sum(row => row.Count(c => c == 'd')));
            Assert.Equal(12, start.Rows.Sum(row => row.Count(c => c == 'l')));
            var session = CheckersPlugin.Session(start);
            var editor = new EditorModel<Game>(session, CheckersPlugin.Outline);
            editor.Add(new PlayMode<Game>("probe.full", "Full acceptance match", d => new Match(d)));
            var window = new EditorWindow<Game>(editor);
            var before = session.Format.Write(session.Current);
            Assert.True(window.Run(EditorWindow<Game>.PlayAction + "probe.full"));
            var match = Assert.IsType<Match>(editor.Game);
            using var host = new HeadlessHost(1600, 1200);
            host.Frames(window.Draw);
            host.Drawing = false;
            var turns = 0;
            var choices = new Random(14); // INVENTED repeatable player; not the sample computer.
            while (Rules.Winner(match.Current) is null && turns < 300)
            {
                var legal = Rules.LegalMoves(match.Current);
                Assert.NotEmpty(legal);
                var path = legal[choices.Next(legal.Count)];
                var expected = Rules.Apply(match.Current, path);
                foreach (var square in path) ClickSquare(host, window.Draw, match, square);
                Assert.Equal(new GameFormat().Write(expected), new GameFormat().Write(match.Current));
                turns++;
            }
            Assert.True(turns > 1, "A full opening match must exercise multiple turns.");
            Assert.True(Rules.Winner(match.Current) is not null, $"No winner after {turns} moves.");
            Assert.Contains(Rules.Winner(match.Current)!.Value.ToString(), match.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("won", match.Status, StringComparison.OrdinalIgnoreCase);
            host.Drawing = true;
            host.Frame(window.Draw);
            Assert.True(host.Covered() > 0);
            Assert.True(window.Run("editor.stop"));
            Assert.Equal(before, session.Format.Write(session.Current));
            Assert.Empty(session.Journal);
            Assert.False(session.History.CanUndo);
            Assert.False(session.History.CanRedo);
            Assert.False(session.Dirty);
        }

        [Fact]
        public void ClickedIllegalMoveDisplaysRulesReason()
        {
            var start = Position(8, Side.Dark, ("c3", 'd'), ("d4", 'l'), ("h8", 'l'));
            var expected = new MovePiece(new[] { "c3", "b4" }).Apply(start);
            Assert.False(expected.Ok);
            using var match = new Match(start);
            match.Click("c3");
            var said = match.Click("b4");
            Assert.Contains(expected.Issues[0].Message, said);
            Assert.Equal(said, match.Said);
            Assert.Equal(new GameFormat().Write(start), new GameFormat().Write(match.Current));
        }

        [Fact]
        public void GuideHeadlessPlayWorkflow()
        {
            using var f = new Fixture();
            var manifest = PluginManifest.Read("""
                {"id":"memos","name":"Memos","version":"1.0.0","api":"0.1","license":"MIT","permissions":[]}
                """);
            Assert.True(f.Plugins.Add(new PlayGuideExample.PreviewPlugin(manifest)));
            var before = f.Session.Format.Write(f.Session.Current);
            Assert.True(f.Window.Run(EditorWindow<Scene>.PlayAction + "memos.preview"));
            using var host = new HeadlessHost();
            host.Frames(f.Window.Draw);
            Assert.Equal("Previewing 1 object(s)", f.Editor.Game!.Status);
            Assert.True(host.Covered() > 0);
            Assert.True(f.Window.Run("editor.stop"));
            Assert.Equal(before, f.Session.Format.Write(f.Session.Current));
            Assert.Empty(f.Session.Journal);
        }

        private static void ClickSquare(HeadlessHost host, Action draw, Match match, string square)
        {
            var p = Board.Centre(match.Current, match.LastBoard, square);
            host.Click(draw, p.X, p.Y);
        }

        private static void AssertPlayReason(IEnumerable<string> messages)
        {
            var text = string.Join(" ", messages);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(text.Contains("play", StringComparison.OrdinalIgnoreCase)
                || text.Contains("stop", StringComparison.OrdinalIgnoreCase), text);
        }

        private static PlayMode<Scene> Mode(string id) => new(id, "Acceptance preview", d => new Probe(d));

        // Construct square data independently from bottom-left=a1=dark, top row first.
        private static Game Position(int size, Side turn, params (string Square, char Piece)[] pieces)
        {
            var rows = Enumerable.Range(0, size).Select(r => Enumerable.Range(0, size)
                .Select(c => ((size - 1 - r + c) % 2 == 0) ? '.' : '-').ToArray()).ToArray();
            foreach (var (square, piece) in pieces)
                rows[size - int.Parse(square[1..])][square[0] - 'a'] = piece;
            return new Game(rows.Select(r => new string(r)).ToImmutableArray(), turn);
        }

        private static char At(Game g, string square) => g.Rows[g.Size - int.Parse(square[1..])][square[0] - 'a'];

        private sealed class Probe(Scene snapshot) : IPlaySession
        {
            public Scene Snapshot { get; } = snapshot;
            public int Draws { get; private set; }
            public int Disposals { get; private set; }
            public bool ThrowOnDraw { get; init; }
            public string Status => "Probe is running";
            public void Draw()
            {
                Draws++;
                if (ThrowOnDraw) throw new InvalidOperationException("draw sentinel");
                ImGui.Text("Self-authored probe");
            }
            public void Dispose() => Disposals++;
        }

        private sealed class TestPlugin(string id, Action<PluginRegistrar<Scene>> register) : IEditorPlugin<Scene>
        {
            public PluginManifest Manifest { get; } = PluginManifest.Read(
                "{\"id\":\"" + id + "\",\"name\":\"Acceptance probe\",\"version\":\"1.0.0\",\"api\":\"0.1\",\"license\":\"MIT\",\"permissions\":[]}");
            public void Register(PluginRegistrar<Scene> add) => register(add);
        }

        private sealed record Baseline(string Text, JournalEntry[] Journal, int UndoCount, bool CanRedo, bool Dirty,
            long Revision, byte[]? Disk);

        private sealed class Fixture : IDisposable
        {
            public Fixture()
            {
                Session = new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(),
                    new Scene([SceneObject.At("piece", null, new Vector3(3, 0, 0))]));
                Editor = new EditorModel<Scene>(Session, new FlatOutline<Scene>(d => d.Objects.Select(o => (o.Id, o.Id))));
                Inspector = new Inspector<Scene>(Editor).Add(new ObjectType<Scene, SceneObject>("probe.object", "Probe object",
                    (d, id) => d.Find(id), [new("x", "X", PropertyKind.Number, o => o.Position.X,
                        (o, v) => new MoveObject(o.Id, new Vector3(Convert.ToSingle(v), o.Position.Y, o.Position.Z)),
                        (_, _) => 0f)]));
                Window = new EditorWindow<Scene>(Editor, Inspector);
                Plugins = new PluginHost<Scene>(Editor, Inspector, Window.Shortcuts);
            }
            public DocumentSession<Scene> Session { get; }
            public EditorModel<Scene> Editor { get; }
            public Inspector<Scene> Inspector { get; }
            public EditorWindow<Scene> Window { get; }
            public PluginHost<Scene> Plugins { get; }
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "a022-" + Guid.NewGuid().ToString("N") + ".json");
            public Probe Start()
            {
                Probe? game = null;
                Editor.Add(new PlayMode<Scene>("probe.play", "Probe", d => game = new Probe(d)));
                Assert.True(Editor.Play("probe.play"));
                return Assert.IsType<Probe>(game);
            }
            public void PrepareHistoryAndFile()
            {
                Session.Save(Path);
                Assert.True(Editor.Execute(new MoveObject("piece", new Vector3(4, 0, 0)), "piece"));
                Assert.True(Editor.Execute(new MoveObject("piece", new Vector3(5, 0, 0)), "piece"));
                Assert.True(Session.Undo());
                Assert.True(Session.History.CanUndo);
                Assert.True(Session.History.CanRedo);
                Assert.True(Session.Dirty);
            }
            public Baseline Capture() => new(Session.Format.Write(Session.Current), Session.Journal.ToArray(),
                Session.History.UndoCount, Session.History.CanRedo, Session.Dirty, Session.Revision,
                File.Exists(Path) ? File.ReadAllBytes(Path) : null);
            public void AssertUnchanged(Baseline before)
            {
                Assert.Equal(before.Text, Session.Format.Write(Session.Current));
                Assert.Equal(before.Journal, Session.Journal);
                Assert.Equal(before.UndoCount, Session.History.UndoCount);
                Assert.Equal(before.CanRedo, Session.History.CanRedo);
                Assert.Equal(before.Dirty, Session.Dirty);
                Assert.Equal(before.Revision, Session.Revision);
                Assert.Equal(before.Disk, File.Exists(Path) ? File.ReadAllBytes(Path) : null);
            }
            public void Dispose()
            {
                Editor.Stop();
                if (File.Exists(Path)) File.Delete(Path);
            }
        }
    }
}

namespace PlayGuideExample
{
    /// <summary>INVENTED read-only scene preview with its own play lifetime.</summary>
    public sealed class PreviewGame(Scene snapshot) : IPlaySession
    {
        /// <inheritdoc />
        public string Status => $"Previewing {snapshot.Objects.Length} object(s)";
        /// <inheritdoc />
        public void Draw() => ImGui.Text(Status);
        /// <inheritdoc />
        public void Dispose() { }
    }

    /// <summary>Adds a preview through the existing plug-in seam.</summary>
    public sealed class PreviewPlugin(PluginManifest manifest) : IEditorPlugin<Scene>
    {
        /// <inheritdoc />
        public PluginManifest Manifest => manifest;
        /// <inheritdoc />
        public void Register(PluginRegistrar<Scene> add) => add.PlayMode(
            new PlayMode<Scene>("memos.preview", "Memo preview", d => new PreviewGame(d)));
    }
}