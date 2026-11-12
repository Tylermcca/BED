using Bed;
using Bed.Editor;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Checkers;

/// <summary>
/// Checkers in the editor, as a plug-in (goal 13): the move, square and turn commands; an inspector type for squares, so
/// a position is set up like any other data; a palette maker that lets the computer play a move; and a Board panel where
/// a person plays by clicking a piece, then each square it lands on. Nothing here is in the library.
/// </summary>
public sealed class CheckersPlugin : IEditorPlugin<Game>
{
    private readonly List<string> picked = [];   // the squares clicked so far in the move being made

    /// <inheritdoc />
    public PluginManifest Manifest { get; } = PluginManifest.Read(
        new StreamReader(typeof(CheckersPlugin).Assembly.GetManifestResourceStream("plugin.json")!).ReadToEnd());

    /// <summary>The squares clicked so far in the move being made (start first).</summary>
    public IReadOnlyList<string> Picked => picked;

    /// <summary>A session over <paramref name="game"/> with the checkers format, validator and core commands.</summary>
    public static DocumentSession<Game> Session(Game game) => new(new GameFormat(), new GameValidator(), new CommandCatalog<Game>(), game);

    /// <summary>The outline: every dark square, labelled with what stands on it.</summary>
    public static IDocumentOutline<Game> Outline { get; } = new FlatOutline<Game>(g =>
        g.Squares().Select(s => (s, $"{s}: {Game.Describe(g.At(g.Parse(s)!.Value.R, g.Parse(s)!.Value.C))}")));

    private static readonly string[] Pieces = ["empty", "dark man", "dark king", "light man", "light king"];

    /// <inheritdoc />
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

    // Squares are the inspector's objects, so a position is set up like any other data.
    private static ObjectType<Game, Square> SquareType() => new("checkers.square", "Square",
        (g, id) => g.Parse(id) is { } at && Game.Dark(g.Size, at.R, at.C) ? new Square(id, g.At(at.R, at.C)) : null,
        [
            new("piece", "Piece", PropertyKind.Choice, s => Game.Describe(s.Piece),
                (s, v) => new SetSquare(s.Name, v switch { "dark man" => 'd', "dark king" => 'D', "light man" => 'l', "light king" => 'L', _ => '.' }),
                Choices: Pieces, Help: "What stands here. Setting up a position doesn't play a move."),
        ]);

    /// <summary>One dark square and what stands on it, as the inspector sees it.</summary>
    public sealed record Square(string Name, char Piece);

    /// <summary>Handles a click on <paramref name="square"/> in the board panel: the first click picks a piece of the side
    /// to move, each later click adds a landing square, and the move is played as soon as the clicks make a whole legal
    /// move. Clicking the picked piece again lets it go. Returns what happened, for the panel's line of text.</summary>
    public string Click(EditorModel<Game> editor, string square)
    {
        var g = editor.Session.Current;
        var mine = g.Parse(square) is { } at && Game.Owner(g.At(at.R, at.C)) == g.ToMove;
        if (picked.Count == 1 && picked[0] == square)
        {
            picked.Clear();
            return $"Let go of {square}.";
        }
        if (picked.Count <= 1 && mine)   // pick, or pick another of your own
        {
            picked.Clear();
            picked.Add(square);
            return $"Picked {square}.";
        }
        if (picked.Count == 0) return $"Pick one of {g.ToMove.ToString().ToLowerInvariant()}'s pieces.";
        picked.Add(square);
        var path = picked.ToList();
        var legal = Rules.LegalMoves(g);
        if (!legal.Any(m => m.SequenceEqual(path)) && legal.Any(m => m.Count > path.Count && m.Take(path.Count).SequenceEqual(path)))
            return $"{string.Join("-", path)}: keep jumping.";
        picked.Clear();
        return editor.Execute(new MovePiece(path)) ? $"Played {string.Join("-", path)}."
            : string.Join(" ", editor.Session.LastIssues.Select(i => i.Message));   // the rules say why not
    }

    private string said = "";

    /// <summary>Where the board panel last drew square <c>a{size}</c>'s top-left corner, in screen pixels, and how big a
    /// square was: so a test (or a tool) can click a square by name.</summary>
    public (float2 Origin, float Cell) LastBoard { get; private set; }

    /// <summary>The centre of <paramref name="square"/> as last drawn, in screen pixels.</summary>
    public float2 ScreenCentre(Game g, string square) => Board.Centre(g, LastBoard, square);

    /// <summary>What the board panel last said (the result of the last click or button).</summary>
    public string Said => said;

    private void DrawBoard(EditorModel<Game> editor)
    {
        var g = editor.Session.Current;
        LastBoard = Board.Draw(g, picked, square => said = Click(editor, square));
        if (ImGui.Button("Computer move"u8))
            said = Computer.Choose(g) is { } m && editor.Execute(m) ? $"The computer played {string.Join("-", m.Path)}." : "No move to play.";
        ImGui.SameLine();
        ImGui.TextDisabled(said);
    }
}

/// <summary>Draws a board with ImGui: squares, pieces, crowns, the picked squares highlighted, and a click target on every
/// dark square. Shared by the authoring panel and the match.</summary>
public static class Board
{
    // The pieces' rim and kings' crowns: 6:1 against the dark square, 3.5:1 against a light piece, 13:1 against a dark one.
    private static readonly ImColor8 Cream = new(245, 235, 210, 255);

    /// <summary>Draws <paramref name="g"/> into the current window and calls <paramref name="clicked"/> with a square's name
    /// when it's clicked. Returns where square a{size} was drawn and how big a square was.</summary>
    /// <param name="below">Height to leave under the board for what's drawn after it (a host's credits line), so it stays in
    /// the window at any text size (found by A067).</param>
    public static (float2 Origin, float Cell) Draw(Game g, IReadOnlyCollection<string> picked, Action<string> clicked, float below = 0, bool status = true)
    {
        var (dark, light) = Rules.Count(g);
        if (status) ImGui.Text(Rules.Winner(g) is { } w ? $"{w} wins!" : $"{g.ToMove} to move.   Dark {dark}, light {light}.");
        var size = MathF.Min(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2 - below);
        var cell = MathF.Max(12, size / g.Size);
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var r = 0; r < g.Size; r++)
            for (var c = 0; c < g.Size; c++)
            {
                var a = origin + new float2(c * cell, r * cell);
                var square = g.Name(r, c);
                draw.AddRectFilled(a, a + new float2(cell, cell), Game.Dark(g.Size, r, c)
                    ? picked.Contains(square) ? new ImColor8(90, 120, 70, 255) : new ImColor8(110, 75, 50, 255) : new ImColor8(225, 205, 170, 255));
                var p = g.At(r, c);
                if (Game.Owner(p) is { } side)
                {
                    var centre = a + new float2(cell / 2, cell / 2);
                    // A cream rim around every piece, so its edge stands out from the dark square by at least 3:1 (WCAG
                    // 1.4.11; found by A067: the black outline was 2.7:1), and a cream crown that stands out from either body.
                    draw.AddCircleFilled(centre, cell * 0.41f, Cream);
                    draw.AddCircleFilled(centre, cell * 0.36f, side == Side.Dark ? new ImColor8(30, 30, 30, 255) : new ImColor8(235, 70, 55, 255));
                    if (char.IsUpper(p)) draw.AddCircle(centre, cell * 0.2f, Cream, 0, MathF.Max(2.5f, cell * 0.05f));   // a king's crown
                }
                if (!Game.Dark(g.Size, r, c)) continue;
                ImGui.SetCursorScreenPos(a);
                // In keyboard navigation too (found by A067: InvisibleButton leaves it out unless asked), so a square is
                // reached with the arrow keys and chosen with Enter or Space, like any button.
                if (ImGui.InvisibleButton($"##sq{square}", new float2(cell, cell), ImGuiButtonFlags.EnableNav)) clicked(square);
            }
        ImGui.SetCursorScreenPos(origin + new float2(0, cell * g.Size + 4));
        ImGui.Dummy(new float2(0, 0));   // an item after moving the cursor, so the window grows to hold the board (ImGui asks for one)
        return (origin, cell);
    }

    /// <summary>The centre of <paramref name="square"/> on a board last drawn at <paramref name="at"/>, in screen pixels.</summary>
    public static float2 Centre(Game g, (float2 Origin, float Cell) at, string square)
    {
        var (r, c) = g.Parse(square) ?? throw new ArgumentException($"'{square}' isn't on the board", nameof(square));
        return at.Origin + new float2((c + 0.5f) * at.Cell, (r + 0.5f) * at.Cell);
    }
}

/// <summary>
/// A match, played from the authored position (goal 14): the game's own code, with its own copy of the board. A person
/// clicks a piece, then each square it lands on; "Computer move" lets the computer play the side to move; "Take back"
/// undoes the last move of the match. Nothing here touches the document: Stop throws the match away.
/// </summary>
public sealed class Match(Game start) : IPlaySession
{
    private readonly List<Game> history = [start];
    private readonly List<string> picked = [];

    /// <summary>The position now.</summary>
    public Game Current => history[^1];

    /// <summary>What the match last said.</summary>
    public string Said { get; private set; } = "";

    /// <summary>Where the board was last drawn, for clicking a square by name.</summary>
    public (float2 Origin, float Cell) LastBoard { get; private set; }

    /// <inheritdoc />
    public string Status => Rules.Winner(Current) is { } w ? $"{w} won in {history.Count - 1} moves." : $"Move {history.Count}: {Current.ToMove} to play.";

    /// <summary>Handles a click on a square, as the board panel does: pick a piece of the side to move, then each landing
    /// square; the move is played when the clicks make a whole legal one, and refused with the rules' reason otherwise.</summary>
    public string Click(string square)
    {
        var g = Current;
        var mine = g.Parse(square) is { } at && Game.Owner(g.At(at.R, at.C)) == g.ToMove;
        if (picked.Count == 1 && picked[0] == square) { picked.Clear(); return Said = $"Let go of {square}."; }
        if (picked.Count <= 1 && mine) { picked.Clear(); picked.Add(square); return Said = $"Picked {square}."; }
        if (picked.Count == 0) return Said = $"Pick one of {g.ToMove.ToString().ToLowerInvariant()}'s pieces.";
        picked.Add(square);
        var path = picked.ToList();
        var legal = Rules.LegalMoves(g);
        if (!legal.Any(m => m.SequenceEqual(path)) && legal.Any(m => m.Count > path.Count && m.Take(path.Count).SequenceEqual(path)))
            return Said = $"{string.Join("-", path)}: keep jumping.";
        picked.Clear();
        return Said = Play(new MovePiece(path));
    }

    /// <summary>Plays a move through the rules (the same <see cref="MovePiece"/> a document edit uses, applied to the
    /// match's own board). Returns what happened, which <see cref="Said"/> then shows.</summary>
    public string Play(MovePiece move)
    {
        var result = move.Apply(Current);
        if (result.Document is not { } next) return Said = string.Join(" ", result.Issues.Select(i => i.Message));
        history.Add(next);
        return Said = $"Played {string.Join("-", move.Path)}.";
    }

    /// <summary>Whether the line above the board says whose move it is and the pieces left. A game that shows them
    /// itself (checkers' Gum strip) turns it off.</summary>
    public bool ShowsStatus { get; set; } = true;

    /// <inheritdoc />
    public void Draw() => Draw(0);

    /// <summary>Draws the match, leaving <paramref name="below"/> pixels under it for the host's own lines.</summary>
    public void Draw(float below)
    {
        LastBoard = Board.Draw(Current, picked, square => Click(square), below, ShowsStatus);
        if (ImGui.Button("Computer move"u8))
            Said = Computer.Choose(Current) is { } m ? "The computer: " + Play(m) : "No move to play.";
        ImGui.SameLine();
        ImGui.BeginDisabled(history.Count == 1);
        if (ImGui.Button("Take back"u8)) { history.RemoveAt(history.Count - 1); picked.Clear(); Said = "Took back the last move."; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled(Said);
    }

    /// <inheritdoc />
    public void Dispose() { }
}
