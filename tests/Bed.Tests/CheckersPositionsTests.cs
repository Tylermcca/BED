using System.Collections.Immutable;
using System.Text.Json;
using Checkers;
using Xunit;

namespace Bed.Tests;

/// <summary>Goal 13's independent rule check: every position in Codex's A018 set (English draughts, worked out by hand
/// from the WCDF rules) gives exactly its listed legal moves, and every "after" board matches.</summary>
public sealed class CheckersPositionsTests
{
    private static readonly JsonElement File = Load();

    private static JsonElement Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "fixtures", "checkers", "positions.json");
            if (System.IO.File.Exists(f)) return JsonDocument.Parse(System.IO.File.ReadAllText(f)).RootElement.Clone();
        }
        throw new InvalidOperationException("can't find fixtures/checkers/positions.json");
    }

    public static TheoryData<string> Ids()
    {
        var ids = new TheoryData<string>();
        foreach (var p in File.GetProperty("positions").EnumerateArray()) ids.Add(p.GetProperty("id").GetString()!);
        return ids;
    }

    private static Game Read(JsonElement board, string toMove) =>
        new([.. board.EnumerateArray().Select(r => r.GetString()!)], toMove == "dark" ? Side.Dark : Side.Light);

    private static string Moves(IEnumerable<IEnumerable<string>> moves) =>
        string.Join(" ", moves.Select(m => string.Join("-", m)).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Ids))]
    public void ThePositionGivesExactlyItsLegalMoves(string id)
    {
        var p = File.GetProperty("positions").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        var g = Read(p.GetProperty("board"), p.GetProperty("to_move").GetString()!);
        Assert.Empty(new GameValidator().Validate(g));
        var expected = Moves(p.GetProperty("legal").EnumerateArray().Select(m => m.EnumerateArray().Select(s => s.GetString()!)));
        Assert.Equal(expected, Moves(Rules.LegalMoves(g)));
        if (expected.Length == 0) Assert.NotNull(Rules.Winner(g));   // no move: the side to move has lost

        if (!p.TryGetProperty("after", out var after)) return;
        var move = after.GetProperty("move").EnumerateArray().Select(s => s.GetString()!).ToImmutableArray();
        var want = Read(after.GetProperty("board"), after.GetProperty("to_move").GetString()!);
        var got = Rules.Apply(g, move);
        Assert.Equal(string.Join("/", want.Rows), string.Join("/", got.Rows));   // ImmutableArray.Equals compares references
        Assert.Equal(want.ToMove, got.ToMove);
    }

    [Fact]
    public void TheSetIsTheOneCodexHandedIn()
    {
        Assert.Equal("checkers-positions", File.GetProperty("kind").GetString());
        Assert.Equal("english-draughts", File.GetProperty("rules").GetString());
        Assert.True(File.GetProperty("positions").GetArrayLength() >= 40);
    }
}
