using Checkers;
using CheckersPlay;
using Xunit;

namespace Bed.Tests;

/// <summary>Checkers as a game in its own window (the user, 2026-11-01): it starts from a saved position or the opening,
/// refuses a position the rules don't allow, and draws headless.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class CheckersGameTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("checkers-game-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void It_starts_from_a_position_the_editor_saved_with_a_schema_field_allowed()
    {
        var text = new GameFormat().Write(Game.Opening() with { ToMove = Side.Light }).Replace("{", "{\"$schema\":\"board.schema.json\",", StringComparison.Ordinal);
        var game = Position.Read(text);
        Assert.Equal(Side.Light, game.ToMove);
        Assert.Equal(string.Join("/", Game.Opening().Rows), string.Join("/", game.Rows));
    }

    [Fact]
    public void A_position_the_rules_refuse_is_said_and_not_played()
    {
        var bad = Path.Combine(root, "bad.json");
        File.WriteAllText(bad, """{"kind":"checkers.game","version":1,"to_move":"dark","board":["d."]}""");
        var errors = new StringWriter();
        Assert.Equal(1, CheckersPlay.Program.Run([bad], TextWriter.Null, errors));
        Assert.Contains("isn't a checkers position", errors.ToString());
        Assert.Equal(2, CheckersPlay.Program.Run(["--nope"], TextWriter.Null, new StringWriter()));
    }

    [Fact]
    public void It_draws_headless_and_a_match_plays_from_the_given_position()
    {
        var file = Path.Combine(root, "start.json");
        File.WriteAllText(file, new GameFormat().Write(Game.Opening()));
        var shot = Path.Combine(root, "game.png");
        Assert.Equal(0, CheckersPlay.Program.Run([file, "--shot", shot], TextWriter.Null, TextWriter.Null));
        Assert.True(new FileInfo(shot).Length > 1000);
        var play = new Play(Game.Opening());
        Assert.StartsWith("Played", play.Match.Play(Assert.IsType<MovePiece>(Computer.Choose(play.Match.Current))));
        Assert.Equal(Side.Light, play.Match.Current.ToMove);
    }
}
