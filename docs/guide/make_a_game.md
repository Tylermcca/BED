# Make a small game with the tools: checkers

Follow the checkers sample to build a small game from editable data, legal moves and a play session. Its data, rules, commands, inspector and panel live in samples/Checkers. The library supplies general document editing and play-session support; it does not contain checkers rules.

The rules follow English draughts as defined by the WCDF. The sample, UI and simple opponent are self-authored and use the existing document, command, plug-in and Play/Stop APIs. No game art or workbook content is used.

The examples below quote the live sample and tests, including the new Board, Match and PlayMode support. They are excerpts inside existing types/methods, not standalone replacement files. Each citation gives the source file and line range. Start with [the API map](api_overview.md) and [the document walkthrough](your_first_document_type.md) if the core types are new to you.

## Why a game is a document

A saved position is just an immutable board and whose turn it is. Keeping that state separate from UI makes validation, save/reload, undo and replay possible without a display.

<!-- excerpt E01 -->
Source: [Game.cs:25](../../samples/Checkers/Game.cs#L25), lines 25-29.

~~~csharp
public sealed record Game(ImmutableArray<string> Rows, Side ToMove)
{
    /// <summary>The board's width and height.</summary>
    public int Size => Rows.Length;

~~~

The sample's session uses that record and the same generic format/validator/catalogue boundary as other documents. Its outline exposes dark squares as the inspector's objects.

<!-- excerpt E11 -->
Source: [CheckersPlugin.cs:24](../../samples/Checkers/CheckersPlugin.cs#L24), lines 24-29.

~~~csharp
    /// <summary>A session over <paramref name="game"/> with the checkers format, validator and core commands.</summary>
    public static DocumentSession<Game> Session(Game game) => new(new GameFormat(), new GameValidator(), new CommandCatalog<Game>(), game);

    /// <summary>The outline: every dark square, labelled with what stands on it.</summary>
    public static IDocumentOutline<Game> Outline { get; } = new FlatOutline<Game>(g =>
        g.Squares().Select(s => (s, $"{s}: {Game.Describe(g.At(g.Parse(s)!.Value.R, g.Parse(s)!.Value.C))}")));
~~~

This is an authoring tool plus a sample game, not a new engine hidden inside the core.

## The board is data

Rows run from the top of the board downwards. a1 is the bottom-left dark square. "-" is unplayable, "." empty, d/l men and D/L kings. The opening helper authors a starter; it does not lock the document to that position.

<!-- excerpt E02 -->
Source: [Game.cs:31](../../samples/Checkers/Game.cs#L31), lines 31-37.

~~~csharp
    public static Game Opening(int size = 8)
    {
        var rows = Enumerable.Range(0, size).Select(r => new string(Enumerable.Range(0, size).Select(c =>
            !Dark(size, r, c) ? '-' : r < size / 2 - 1 ? 'l' : r > size / 2 ? 'd' : '.').ToArray()));
        return new Game([.. rows], Side.Dark);
    }

~~~

GameFormat owns the versioned checkers.game JSON format:

<!-- excerpt E03 -->
Source: [Game.cs:78](../../samples/Checkers/Game.cs#L78), lines 78-84.

~~~csharp
public sealed class GameFormat : IDocumentFormat<Game>
{
    /// <inheritdoc />
    public string Kind => "checkers.game";

    /// <inheritdoc />
    public int Version => 1;
~~~

GameValidator accepts square even boards from 4 to 16, with legal characters/parity and no uncrowned man on its crowning row:

<!-- excerpt E04 -->
Source: [Game.cs:128](../../samples/Checkers/Game.cs#L128), lines 128-135.

~~~csharp
public sealed class GameValidator : IValidator<Game>
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(Game g)
    {
        var issues = new List<Issue>();
        if (g.Size is < 4 or > 16 || g.Size % 2 != 0)
            return [Issue.Error($"the board has {g.Size} rows; use an even number from 4 to 16", null, "$.board")];
~~~

That size range is the sample's data capability. The official English draughts test corpus is 8×8; changing dimensions does not by itself define an official rule variant.

## Rules: structure first, legal commands next

The validator checks whether a document is structurally sound. Rules.LegalMoves supplies the current side's complete move paths. It gathers captures separately and returns those whenever any exist.

<!-- excerpt E05 -->
Source: [Game.cs:167](../../samples/Checkers/Game.cs#L167), lines 167-180.

~~~csharp
    public static IReadOnlyList<IReadOnlyList<string>> LegalMoves(Game g)
    {
        var captures = new List<IReadOnlyList<string>>();
        var steps = new List<IReadOnlyList<string>>();
        for (var r = 0; r < g.Size; r++)
            for (var c = 0; c < g.Size; c++)
            {
                if (Game.Owner(g.At(r, c)) != g.ToMove) continue;
                Jumps(g, g.At(r, c), r, c, [g.Name(r, c)], [], captures, (r, c));
                foreach (var (dr, dc) in Directions(g.At(r, c)))
                    if (On(g, r + dr, c + dc) && g.At(r + dr, c + dc) == '.') steps.Add([g.Name(r, c), g.Name(r + dr, c + dc)]);
            }
        return captures.Count > 0 ? captures : steps;
    }
~~~

A capture branch ends at crowning, or is extended until no further jump can be made:

<!-- excerpt E06 -->
Source: [Game.cs:201](../../samples/Checkers/Game.cs#L201), lines 201-211.

~~~csharp
            var landing = g.At(lr, lc) == '.' || (lr, lc) == start;   // the moving piece has left its start
            if (over is null || over == Game.Owner(piece) || !landing) continue;
            extended = true;
            path.Add(g.Name(lr, lc));
            taken.Add((mr, mc));
            if (Crowns(g, piece, lr)) found.Add([.. path]);   // crowned: the move ends here
            else Jumps(g, piece, lr, lc, path, taken, found, start);
            taken.Remove((mr, mc));
            path.RemoveAt(path.Count - 1);
        }
        if (!extended && path.Count > 1) found.Add([.. path]);
~~~

Having no legal move gives the other side the win:

<!-- excerpt E07 -->
Source: [Game.cs:234](../../samples/Checkers/Game.cs#L234), lines 234-234.

~~~csharp
    public static Side? Winner(Game g) => LegalMoves(g).Count > 0 ? null : g.ToMove == Side.Dark ? Side.Light : Side.Dark;
~~~

MovePiece is the supported edit. It checks the proposed whole path against Rules.LegalMoves before applying the effects.

<!-- excerpt E08 -->
Source: [Game.cs:243](../../samples/Checkers/Game.cs#L243), lines 243-250.

~~~csharp
public sealed record MovePiece(IReadOnlyList<string> Path) : ICommand<Game>
{
    /// <inheritdoc />
    public CommandResult<Game> Apply(Game g)
    {
        if (Rules.Winner(g) is { } won) return CommandResult<Game>.Refused(Issue.Error($"the game is over: {won.ToString().ToLowerInvariant()} won"));
        var legal = Rules.LegalMoves(g);
        if (legal.Any(m => m.SequenceEqual(Path))) return CommandResult<Game>.Done(Rules.Apply(g, Path));
~~~

Rules.Apply itself assumes a legal move; it is not a replacement for the command boundary. The independent hand-derived positions in fixtures/checkers cover forced captures, unequal choices, branches, crowning, backwards kings and repeated-victim exclusions. The structural fixture checker does not generate those expected sets.

## Refusals that teach

A refusal should explain what the player can do next. The sample distinguishes moving the wrong side, stopping a capture early and ignoring a forced capture, using the relevant square as the issue target.

<!-- excerpt E09 -->
Source: [Game.cs:255](../../samples/Checkers/Game.cs#L255), lines 255-263.

~~~csharp
        if (Game.Owner(piece) != g.ToMove)
            return CommandResult<Game>.Refused(Issue.Error(piece is '.' or '-' ? $"there's no piece on {from}" : $"it's {side}'s turn, and {from} is a {Game.Describe(piece)}", from));
        if (Math.Abs(g.Parse(legal[0][1])!.Value.R - g.Parse(legal[0][0])!.Value.R) == 2 && legal.All(m => !m.SequenceEqual(Path)))
        {
            var mine = legal.Where(m => m[0] == from).ToList();
            if (mine.Any(m => m.Take(Path.Count).SequenceEqual(Path)))
                return CommandResult<Game>.Refused(Issue.Error($"keep jumping: the capture from {from} isn't finished", from));
            return CommandResult<Game>.Refused(Issue.Error($"a capture is available, so it must be taken ({string.Join(" or ", legal.Take(3).Select(m => string.Join("-", m)))})", from));
        }
~~~

CheckersTests also checks the exact messages for an empty square, an impossible move and a finished game. A refused document command leaves the session unchanged; it is not a partially played move.

## Set up a position in the inspector

SetSquare authors data. It can place or clear a piece without pretending that this is a legal turn of play; the session's validator still checks the resulting board.

<!-- excerpt E10 -->
Source: [Game.cs:269](../../samples/Checkers/Game.cs#L269), lines 269-275.

~~~csharp
public sealed record SetSquare(string Square, char Piece) : ICommand<Game>
{
    /// <inheritdoc />
    public CommandResult<Game> Apply(Game g) => g.Parse(Square) is { } at && Game.Dark(g.Size, at.R, at.C)
        ? CommandResult<Game>.Done(g.Set(at.R, at.C, Piece))
        : CommandResult<Game>.Refused(Issue.Error($"'{Square}' isn't a dark square on this board", Square));
}
~~~

The plug-in exposes a Piece choice row that creates SetSquare, not a direct write into the board:

<!-- excerpt E13 -->
Source: [CheckersPlugin.cs:46](../../samples/Checkers/CheckersPlugin.cs#L46), lines 46-52.

~~~csharp
    private static ObjectType<Game, Square> SquareType() => new("checkers.square", "Square",
        (g, id) => g.Parse(id) is { } at && Game.Dark(g.Size, at.R, at.C) ? new Square(id, g.At(at.R, at.C)) : null,
        [
            new("piece", "Piece", PropertyKind.Choice, s => Game.Describe(s.Piece),
                (s, v) => new SetSquare(s.Name, v switch { "dark man" => 'd', "dark king" => 'D', "light man" => 'l', "light king" => 'L', _ => '.' }),
                Choices: Pieces, Help: "What stands here. Setting up a position doesn't play a move."),
        ]);
~~~

Select d4 and choose dark king to author a different starting position. The play test does exactly that and snapshots the document's file text, journal and undo availability before Play.

<!-- excerpt E22 -->
Source: [CheckersTests.cs:318](../../tests/Brutal.Authoring.Tests/CheckersTests.cs#L318), lines 318-321.

~~~csharp
        Assert.True(inspector.Set("piece", "dark king"));   // author a position: an extra dark king
        var file = new GameFormat().Write(editor.Session.Current);
        var journal = JournalFile.Write(editor.Session.Journal);
        var canUndo = editor.Session.History.CanUndo;
~~~

## Register the plug-in

The manifest describes the sample. Its licence is supplied here; under the current tools policy licence information is optional and known compatibility concerns are warnings.

<!-- excerpt E24 -->
Source: [plugin.json:1](../../samples/Checkers/plugin.json#L1), lines 1-8.

~~~json
{
  "id": "checkers",
  "name": "Checkers",
  "version": "1.0.0",
  "api": "0.1",
  "license": "MIT",
  "description": "English draughts made with the authoring tools: set up a board, play it by clicking, let the computer move, replay a match."
}
~~~

The sample embeds plugin.json through its project file and hands a CheckersPlugin object to PluginHost. There is no plug-in-folder assembly loader. Register groups commands, the square type, workspace, palette maker, shortcut, Board panel and Match play mode:

<!-- excerpt E12 -->
Source: [CheckersPlugin.cs:34](../../samples/Checkers/CheckersPlugin.cs#L34), lines 34-43.

~~~csharp
    public void Register(PluginRegistrar<Game> add) => add
        .Command<MovePiece>("checkers.move", 1, "Play a move")
        .Command<SetSquare>("checkers.set", 1, "Set a square")
        .Command<SetTurn>("checkers.turn", 1, "Set whose turn it is")
        .ObjectType(SquareType())
        .Workspace(new Workspace("checkers.play", "Play", ["checkers.move"]))
        .Maker("checkers.move", e => Computer.Choose(e.Session.Current))
        .Shortcut("checkers.move", "Alt+M")
        .Panel(new Panel("checkers.board", "Board", () => DrawBoard(add.Editor), Width: 440, Height: 520))
        .PlayMode(new PlayMode<Game>("checkers.match", "Match", game => new Match(game)));
~~~

The host checks and stages registrations together. The Board panel supports the original document-editing path, useful for demonstrating commands, undo and a persistent journal. Play Match is the separate disposable-play workflow below. Keep their state ownership clear.

## The computer is another command source

Computer.Choose returns a MovePiece. It uses deterministic short look-ahead with a material/advancement score; this is a simple sample opponent, not a claim of strong play.

<!-- excerpt E14 -->
Source: [Game.cs:290](../../samples/Checkers/Game.cs#L290), lines 290-300.

~~~csharp
    public static MovePiece? Choose(Game g, int depth = 4)
    {
        IReadOnlyList<string>? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var m in Rules.LegalMoves(g))
        {
            var score = -Search(Rules.Apply(g, m), depth - 1, double.NegativeInfinity, double.PositiveInfinity);
            if (score > bestScore) (best, bestScore) = (m, score);
        }
        return best is null ? null : new MovePiece(best);
    }
~~~

Outside Play, the maker runs that command through the editor's document session. Inside Match, the same MovePiece rule logic is applied to the match's own board:

<!-- excerpt E15 -->
Source: [CheckersPlugin.cs:196](../../samples/Checkers/CheckersPlugin.cs#L196), lines 196-201.

~~~csharp
    public string Play(MovePiece move)
    {
        var result = move.Apply(Current);
        if (result.Document is not { } next) return string.Join(" ", result.Issues.Select(i => i.Message));
        history.Add(next);
        return $"Played {string.Join("-", move.Path)}.";
~~~

The AI does not need a second rule implementation or a privileged mutation path. The rules decide whether the proposed move is allowed.

## Play, then return to editing

PlayMode creates a Match from the authored position. Match owns its current board and local history; it does not edit the document session.

<!-- excerpt E16 -->
Source: [CheckersPlugin.cs:159](../../samples/Checkers/CheckersPlugin.cs#L159), lines 159-165.

~~~csharp
public sealed class Match(Game start) : IPlaySession
{
    private readonly List<Game> history = [start];
    private readonly List<string> picked = [];

    /// <summary>The position now.</summary>
    public Game Current => history[^1];
~~~

The real test starts the mode, verifies that the authored king reached the match and checks that document edits, inspector changes and undo are locked while playing.

<!-- excerpt E20 -->
Source: [CheckersTests.cs:331](../../tests/Brutal.Authoring.Tests/CheckersTests.cs#L331), lines 331-341.

~~~csharp
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
~~~

A match can take back its own last move using its local history. Stop discards the match. The test then compares document text, journal and undo availability to their pre-Play values and verifies editing works again.

<!-- excerpt E21 -->
Source: [CheckersTests.cs:361](../../tests/Brutal.Authoring.Tests/CheckersTests.cs#L361), lines 361-367.

~~~csharp
        // Stop: back to editing, exactly as before Play.
        Assert.True(window.Run("editor.stop"));
        Assert.Null(editor.Playing);
        Assert.Equal(file, new GameFormat().Write(editor.Session.Current));
        Assert.Equal(journal, JournalFile.Write(editor.Session.Journal));
        Assert.Equal(canUndo, editor.Session.History.CanUndo);
        Assert.True(inspector.Set("piece", "empty"));   // editing works again
~~~

The window also has a test for a throwing play mode: it stops the failed mode, reports the reason and keeps editing available. This is error recovery for trusted in-process code, not a security boundary.

## Save, replay and undo

Document-session moves and setup edits are journaled. The whole-game test replays those entries onto the same opening and checks canonical text equality, then undoes the last edit.

<!-- excerpt E17 -->
Source: [CheckersTests.cs:174](../../tests/Brutal.Authoring.Tests/CheckersTests.cs#L174), lines 174-177.

~~~csharp
        var replay = DocumentSession<Game>.Replay(Game.Opening(), s.Journal, new GameFormat(), new GameValidator(), s.Catalog);
        Assert.Equal(new GameFormat().Write(s.Current), new GameFormat().Write(replay.Current));
        Assert.True(s.Undo());
        Assert.Null(Rules.Winner(s.Current));   // undo takes the last move back
~~~

Another test saves via the actual window's Save As field, opens the file again and continues from the same side to move:

<!-- excerpt E18 -->
Source: [CheckersTests.cs:288](../../tests/Brutal.Authoring.Tests/CheckersTests.cs#L288), lines 288-291.

~~~csharp
            var reopened = DocumentSession<Game>.Open(file, new GameFormat(), new GameValidator(), editor.Session.Catalog);
            Assert.Equal(new GameFormat().Write(editor.Session.Current), new GameFormat().Write(reopened.Current));
            Assert.Equal(Side.Dark, reopened.Current.ToMove);
            Assert.True(reopened.Execute(Computer.Choose(reopened.Current)!));
~~~

These are the persistent document workflow. The disposable Match currently keeps its own take-back history, not the document's journal. Do not expect Stop to export a played match into saved authoring data.

## Test it without a screen

The tests use HeadlessHost: real ImGui input and CPU-rendered pixels, with no OS window, GPU renderer or focus change. ImGui tests share the serial collection, so only one host exists at a time.

A square's screen centre is measured from the actual board layout, then clicked through the host:

<!-- excerpt E19 -->
Source: [CheckersTests.cs:218](../../tests/Brutal.Authoring.Tests/CheckersTests.cs#L218), lines 218-225.

~~~csharp
        {
            foreach (var sq in squares)
            {
                var at = plugin.ScreenCentre(editor.Session.Current, sq);
                host.Click(Draw, at.X, at.Y);
            }
        }
        Play("c3", "d4");
~~~

The shared Board draws each dark square's hit target in that same layout:

<!-- excerpt E23 -->
Source: [CheckersPlugin.cs:137](../../samples/Checkers/CheckersPlugin.cs#L137), lines 137-140.

~~~csharp
                }
                if (!Game.Dark(g.Size, r, c)) continue;
                ImGui.SetCursorScreenPos(a);
                if (ImGui.InvisibleButton($"##sq{square}", new float2(cell, cell))) clicked(square);
~~~

Long scripted games turn Drawing off while input/state still run, then restore it and render settled frames before saving. They assert results and replay/state preservation; screenshots help inspect the UI but do not replace those assertions.

Run the following from the tools repository root in **Windows PowerShell 5.1**. Pick any empty folder of your own for the pictures. No images are committed.

~~~powershell
$env:BRUTAL_AUTHORING_SHOTS = "$env:TEMP\checkers-shots"
dotnet test tests\Brutal.Authoring.Tests\Brutal.Authoring.Tests.csproj --filter "FullyQualifiedName~CheckersTests" --nologo -v:q
[Environment]::SetEnvironmentVariable('BRUTAL_AUTHORING_SHOTS', $null, 'Process')
~~~

Actual result, executed against an isolated current tools copy in Windows PowerShell 5.1:

~~~text
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 6 s
~~~

The four generated 1400×860 frames were opened and checked:

| Frame | What it shows |
|---|---|
| 10_checkers_first_move.png | The document Board after c3-d4, Light to move, twelve pieces each, and a Played c3-d4 message. |
| 11_checkers_end.png | The document Board at Light's win, remaining light pieces/kings and no dark pieces; the outliner reflects that document state. |
| 12_checkers_saved.png | match.json in the title, no unsaved star, Dark to move after two document moves, and Saved to match.json in the status line. |
| 13_checkers_play.png | A separate Match (playing) panel at Light's win, a Stop Match toolbar action, locked authoring controls and the original authored dark king still visible in the inspector/outliner. |

The last frame records the match before Stop; the test's assertions prove the return to unchanged editing afterward. The first three frames exercise persistent document editing. Different tests put the computer on different sides, so these images are not a general prediction of who wins.

## Your turn: change one rule

For a separate exercise, define a flying-king variant before changing code. This variant is not implemented by the sample. Decide how far a king may slide, which square beyond a captured piece may be chosen and when a multi-capture must stop.

That would affect quiet-move enumeration and Jumps in Rules.LegalMoves, captured-piece removal in Rules.Apply (which currently recognises a two-row jump), and the forced-capture explanations in MovePiece. The board and click path representation can still carry a start plus landings, but longer diagonals need new independent tests.

Keep the English fixture set and its existing expectations for English mode. Add hand-derived flying-king positions for slides, blockers, landing choices, direction changes and repeated victims. Update the computer's expectations because it consumes legal moves, and exercise both document replay and disposable Play/Stop. Changing the board data alone cannot change capture rules.

## Check excerpts against your checkout

Use each excerpt's source link and line range to compare it with your checkout before adapting it. Run the focused command above when you can execute the sample; a matching excerpt alone does not show that a build or play session succeeds.
