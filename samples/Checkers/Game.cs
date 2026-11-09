using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Brutal.Authoring;

namespace Checkers;

/// <summary>The two sides. Dark starts at the bottom (rank 1) and moves first.</summary>
public enum Side
{
    /// <summary>Dark: men move up the board.</summary>
    Dark,
    /// <summary>Light: men move down the board.</summary>
    Light,
}

/// <summary>
/// A game of checkers as a document: the board, as rows from the top, and whose turn it is. The board is data, so a
/// starting position (or another size) is authored, not coded. Characters: <c>-</c> a light square (never used),
/// <c>.</c> an empty dark square, <c>d</c>/<c>l</c> dark and light men, <c>D</c>/<c>L</c> kings (the same notation
/// as <c>fixtures/checkers</c>). Squares are named file then rank: <c>a1</c> is bottom left and dark.
/// </summary>
/// <param name="Rows">The board, top row first.</param>
/// <param name="ToMove">Whose turn it is.</param>
public sealed record Game(ImmutableArray<string> Rows, Side ToMove)
{
    /// <summary>The board's width and height.</summary>
    public int Size => Rows.Length;

    /// <summary>The standard 8×8 opening: twelve men each, dark to move.</summary>
    public static Game Opening(int size = 8)
    {
        var rows = Enumerable.Range(0, size).Select(r => new string(Enumerable.Range(0, size).Select(c =>
            !Dark(size, r, c) ? '-' : r < size / 2 - 1 ? 'l' : r > size / 2 ? 'd' : '.').ToArray()));
        return new Game([.. rows], Side.Dark);
    }

    /// <summary>Whether the square at row <paramref name="r"/> (from the top) and column <paramref name="c"/> is a dark
    /// (playable) one. The bottom-left square is dark.</summary>
    public static bool Dark(int size, int r, int c) => (size - 1 - r + c) % 2 == 0;

    /// <summary>The piece at row and column: one of <c>. d l D L -</c>.</summary>
    public char At(int r, int c) => Rows[r][c];

    /// <summary>The square's name, such as <c>c3</c>.</summary>
    public string Name(int r, int c) => $"{(char)('a' + c)}{Size - r}";

    /// <summary>The row and column of a square name, or null when it isn't on the board.</summary>
    public (int R, int C)? Parse(string square)
    {
        if (square.Length < 2 || square[0] is < 'a' or > 'z' || !int.TryParse(square[1..], out var rank)) return null;
        var (r, c) = (Size - rank, square[0] - 'a');
        return r >= 0 && r < Size && c >= 0 && c < Size ? (r, c) : null;
    }

    /// <summary>Every dark square's name, top row first.</summary>
    public IEnumerable<string> Squares() =>
        from r in Enumerable.Range(0, Size) from c in Enumerable.Range(0, Size) where Dark(Size, r, c) select Name(r, c);

    internal Game Set(int r, int c, char piece)
    {
        var row = Rows[r].ToCharArray();
        row[c] = piece;
        return this with { Rows = Rows.SetItem(r, new string(row)) };
    }

    /// <summary>Which side a piece belongs to, or null for an empty square.</summary>
    public static Side? Owner(char piece) => piece switch { 'd' or 'D' => Side.Dark, 'l' or 'L' => Side.Light, _ => null };

    /// <summary>A plain name for a piece: "dark man", "light king", or "empty".</summary>
    public static string Describe(char piece) => piece switch
    {
        'd' => "dark man", 'D' => "dark king", 'l' => "light man", 'L' => "light king", _ => "empty",
    };
}

/// <summary>Reads and writes games as canonical JSON: <c>{"kind": "checkers.game", "version": 1, "to_move": …, "board": [...]}</c>.</summary>
public sealed class GameFormat : IDocumentFormat<Game>
{
    /// <inheritdoc />
    public string Kind => "checkers.game";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public Game Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "to_move", "board");
        if (r.String("kind") != Kind) throw new DocumentFormatException("$.kind", $"expected \"{Kind}\"");
        var version = r.Int("version");
        if (version != Version) throw new DocumentFormatException("$.version", $"game version {version} is not supported (this tool reads version {Version})");
        var side = r.String("to_move") switch
        {
            "dark" => Side.Dark,
            "light" => Side.Light,
            var s => throw new DocumentFormatException("$.to_move", $"'{s}' isn't a side; use dark or light"),
        };
        var board = r.Element("board");
        if (board.ValueKind != JsonValueKind.Array) throw new DocumentFormatException("$.board", "expected a list of rows");
        var rows = board.EnumerateArray().Select((e, i) => e.ValueKind == JsonValueKind.String ? e.GetString()!
            : throw new DocumentFormatException($"$.board[{i}]", "expected a row of text")).ToImmutableArray();
        return new Game(rows, side);
    }

    /// <inheritdoc />
    public string Write(Game document)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Canonical.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("kind", Kind);
            w.WriteNumber("version", Version);
            w.WriteString("to_move", document.ToMove == Side.Dark ? "dark" : "light");
            w.WriteStartArray("board");
            foreach (var row in document.Rows) w.WriteStringValue(row);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}

/// <summary>What makes a board valid: square, an even size from 4 to 16, the right characters, pieces only on dark
/// squares, and no man already standing on the row where it would be crowned.</summary>
public sealed class GameValidator : IValidator<Game>
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(Game g)
    {
        var issues = new List<Issue>();
        if (g.Size is < 4 or > 16 || g.Size % 2 != 0)
            return [Issue.Error($"the board has {g.Size} rows; use an even number from 4 to 16", null, "$.board")];
        for (var r = 0; r < g.Size; r++)
        {
            if (g.Rows[r].Length != g.Size)
            {
                issues.Add(Issue.Error($"row {r + 1} has {g.Rows[r].Length} squares; every row needs {g.Size}", null, $"$.board[{r}]"));
                continue;
            }
            for (var c = 0; c < g.Size; c++)
            {
                var p = g.At(r, c);
                var dark = Game.Dark(g.Size, r, c);
                if (!dark && p != '-') issues.Add(Issue.Error($"a piece can't stand on a light square ({(char)('a' + c)}{g.Size - r})", null, $"$.board[{r}]"));
                else if (dark && p is not ('.' or 'd' or 'l' or 'D' or 'L'))
                    issues.Add(Issue.Error($"'{p}' at {g.Name(r, c)} isn't a piece; use . d l D L", g.Name(r, c), $"$.board[{r}]"));
                else if ((p == 'd' && r == 0) || (p == 'l' && r == g.Size - 1))
                    issues.Add(Issue.Error($"the man at {g.Name(r, c)} is on its crowning row; make it a king", g.Name(r, c), $"$.board[{r}]"));
            }
        }
        return issues;
    }
}

/// <summary>
/// The rules of English draughts (BORROWED, as the WCDF states them): men step one square diagonally forward and capture
/// by jumping; kings step and capture in all four diagonal directions, one square at a time (no flying kings); a capture
/// must be taken when one exists, though any capturing sequence may be chosen; a sequence must be finished; a piece may
/// not be jumped twice; a man that reaches the far row is crowned and the move ends there; a side with no move loses.
/// </summary>
public static class Rules
{
    /// <summary>Every legal move for the side to move, each as the squares visited (start, then every landing).</summary>
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

    private static IEnumerable<(int, int)> Directions(char piece) => piece switch
    {
        'd' => [(-1, -1), (-1, 1)],
        'l' => [(1, -1), (1, 1)],
        _ => [(-1, -1), (-1, 1), (1, -1), (1, 1)],
    };

    private static bool On(Game g, int r, int c) => r >= 0 && r < g.Size && c >= 0 && c < g.Size;

    // Extends a capture sequence from (r, c); adds it to found when it can go no further.
    private static void Jumps(Game g, char piece, int r, int c, List<string> path, HashSet<(int, int)> taken,
        List<IReadOnlyList<string>> found, (int R, int C) start)
    {
        var extended = false;
        foreach (var (dr, dc) in Directions(piece))
        {
            int mr = r + dr, mc = c + dc, lr = r + 2 * dr, lc = c + 2 * dc;
            if (!On(g, lr, lc) || taken.Contains((mr, mc))) continue;
            var over = Game.Owner(g.At(mr, mc));
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
    }

    private static bool Crowns(Game g, char piece, int row) => (piece == 'd' && row == 0) || (piece == 'l' && row == g.Size - 1);

    /// <summary>The board after a legal <paramref name="move"/>: captured pieces removed, a man crowned on the far row,
    /// the turn passed. The move isn't checked; use <see cref="LegalMoves"/>.</summary>
    public static Game Apply(Game g, IReadOnlyList<string> move)
    {
        var (r, c) = g.Parse(move[0])!.Value;
        var piece = g.At(r, c);
        var next = g.Set(r, c, '.');
        for (var i = 1; i < move.Count; i++)
        {
            var (tr, tc) = g.Parse(move[i])!.Value;
            if (Math.Abs(tr - r) == 2) next = next.Set((r + tr) / 2, (c + tc) / 2, '.');
            (r, c) = (tr, tc);
        }
        if (Crowns(g, piece, r)) piece = char.ToUpperInvariant(piece);
        return next.Set(r, c, piece) with { ToMove = g.ToMove == Side.Dark ? Side.Light : Side.Dark };
    }

    /// <summary>The winner: the other side, when the side to move has no legal move; otherwise null.</summary>
    public static Side? Winner(Game g) => LegalMoves(g).Count > 0 ? null : g.ToMove == Side.Dark ? Side.Light : Side.Dark;

    /// <summary>How many pieces each side has.</summary>
    public static (int Dark, int Light) Count(Game g) =>
        (g.Rows.Sum(row => row.Count(ch => ch is 'd' or 'D')), g.Rows.Sum(row => row.Count(ch => ch is 'l' or 'L')));
}

/// <summary>Plays a move: the squares visited, start first. Refused, with a reason a player understands, unless it's
/// one of <see cref="Rules.LegalMoves"/>.</summary>
public sealed record MovePiece(IReadOnlyList<string> Path) : ICommand<Game>
{
    /// <inheritdoc />
    public CommandResult<Game> Apply(Game g)
    {
        if (Rules.Winner(g) is { } won) return CommandResult<Game>.Refused(Issue.Error($"the game is over: {won.ToString().ToLowerInvariant()} won"));
        var legal = Rules.LegalMoves(g);
        if (legal.Any(m => m.SequenceEqual(Path))) return CommandResult<Game>.Done(Rules.Apply(g, Path));
        var from = Path.Count > 0 ? Path[0] : "";
        var side = g.ToMove.ToString().ToLowerInvariant();
        if (g.Parse(from) is not { } at) return CommandResult<Game>.Refused(Issue.Error($"'{from}' isn't a square on this board"));
        var piece = g.At(at.R, at.C);
        if (Game.Owner(piece) != g.ToMove)
            return CommandResult<Game>.Refused(Issue.Error(piece is '.' or '-' ? $"there's no piece on {from}" : $"it's {side}'s turn, and {from} is a {Game.Describe(piece)}", from));
        if (Math.Abs(g.Parse(legal[0][1])!.Value.R - g.Parse(legal[0][0])!.Value.R) == 2 && legal.All(m => !m.SequenceEqual(Path)))
        {
            var mine = legal.Where(m => m[0] == from).ToList();
            if (mine.Any(m => m.Take(Path.Count).SequenceEqual(Path)))
                return CommandResult<Game>.Refused(Issue.Error($"keep jumping: the capture from {from} isn't finished", from));
            return CommandResult<Game>.Refused(Issue.Error($"a capture is available, so it must be taken ({string.Join(" or ", legal.Take(3).Select(m => string.Join("-", m)))})", from));
        }
        return CommandResult<Game>.Refused(Issue.Error($"the {Game.Describe(piece)} on {from} can't move there", from));
    }
}

/// <summary>Puts a piece on a square, or clears it with <c>.</c>: setting up a position (authoring, not play).</summary>
public sealed record SetSquare(string Square, char Piece) : ICommand<Game>
{
    /// <inheritdoc />
    public CommandResult<Game> Apply(Game g) => g.Parse(Square) is { } at && Game.Dark(g.Size, at.R, at.C)
        ? CommandResult<Game>.Done(g.Set(at.R, at.C, Piece))
        : CommandResult<Game>.Refused(Issue.Error($"'{Square}' isn't a dark square on this board", Square));
}

/// <summary>Says whose turn it is (authoring a position).</summary>
public sealed record SetTurn(Side Side) : ICommand<Game>
{
    /// <inheritdoc />
    public CommandResult<Game> Apply(Game g) => CommandResult<Game>.Done(g with { ToMove = Side });
}

/// <summary>A simple computer player: a short look-ahead (alpha-beta) counting material, kings worth more, men a little
/// more the further they've come. Deterministic: the same position gives the same move. It plays through the same
/// <see cref="MovePiece"/> command a person does (goal 8: an AI is just another source of commands).</summary>
public static class Computer
{
    /// <summary>The move it would play, or null when there's none.</summary>
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

    private static double Search(Game g, int depth, double alpha, double beta)
    {
        var moves = Rules.LegalMoves(g);
        if (moves.Count == 0) return -1000 - depth;   // the side to move has lost; sooner is worse
        if (depth == 0) return Score(g);
        foreach (var m in moves)
        {
            alpha = Math.Max(alpha, -Search(Rules.Apply(g, m), depth - 1, -beta, -alpha));
            if (alpha >= beta) break;
        }
        return alpha;
    }

    // Material from the side to move's point of view.
    private static double Score(Game g)
    {
        double s = 0;
        for (var r = 0; r < g.Size; r++)
            for (var c = 0; c < g.Size; c++)
            {
                var p = g.At(r, c);
                if (Game.Owner(p) is not { } side) continue;
                var value = char.IsUpper(p) ? 1.6 : 1 + 0.05 * (side == Side.Dark ? g.Size - 1 - r : r);
                s += side == g.ToMove ? value : -value;
            }
        return s;
    }
}
