using System.Reflection;
using Checkers;
using CheckersPlay;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class CheckersPlayerRuntimeTests
{
    [Fact]
    public void Player_assembly_graph_has_no_editor_assembly()
    {
        var pending=new Stack<Assembly>();pending.Push(typeof(CheckersPlay.Program).Assembly);var seen=new HashSet<string>();
        while(pending.TryPop(out var assembly))foreach(var reference in assembly.GetReferencedAssemblies())
        {
            Assert.False(reference.Name!.StartsWith("Bed.Editor",StringComparison.Ordinal),assembly.FullName+" -> "+reference.FullName);
            if(seen.Add(reference.Name)&&(reference.Name.StartsWith("Checkers",StringComparison.Ordinal)||reference.Name=="Bed"))pending.Push(Assembly.Load(reference));
        }
        Assert.Equal("Checkers.Rules",typeof(Game).Assembly.GetName().Name);
    }
    [Fact]
    public void Mouse_plays_a_legal_move_and_undo_and_new_game_restore_the_saved_position()
    {
        using var play=new Play(Game.Opening());play.Draw();var start=play.Match.Current;var path=Rules.LegalMoves(start)[0];
        void Square(string square)
        {var at=start.Parse(square)!.Value;var b=play.Screen.Board;play.Screen.Click(play,b.X+(at.C+.5f)*b.Cell,b.Y+(at.R+.5f)*b.Cell);play.Draw();}
        foreach(var square in path)Square(square);
        Assert.NotEqual(start,play.Match.Current);Assert.Contains("Played",play.Match.Said);Assert.True(play.Match.CanTakeBack);
        var back=Assert.Single(play.Screen.Parts(),p=>p.Name=="TakeBack");play.Screen.Click(play,back.X+back.Width/2,back.Y+back.Height/2);play.Draw();
        Assert.Equal(start,play.Match.Current);Assert.False(play.Match.CanTakeBack);
        play.Key("C");var newer=play.Match;play.Key("N");Assert.NotSame(newer,play.Match);Assert.Equal(start,play.Match.Current);
    }
    [Fact]
    public void Arrow_and_enter_route_picks_and_plays_the_same_move_as_a_mouse()
    {
        using var play=new Play(Game.Opening());play.Draw();var game=play.Match.Current;var path=Rules.LegalMoves(game)[0];
        foreach(var square in path)
        {
            var at=game.Parse(square)!.Value;var target=at.R*game.Size+at.C;
            while(play.Screen.Cursor>target)play.Key("Left");while(play.Screen.Cursor<target)play.Key("Right");
            play.Key("Enter");
        }
        Assert.Equal(Side.Light,play.Match.Current.ToMove);Assert.StartsWith("Played",play.Match.Said);
    }
    [Fact]
    public void Plain_fallback_new_game_is_still_clickable()
    {
        using var play=new Play(Game.Opening(),plain:true);play.Match.ComputerMove();var before=play.Match;play.Draw();
        var button=Assert.Single(play.Screen.Parts(),p=>p.Name=="NewGameButton");play.Screen.Click(play,button.X+10,button.Y+10);
        Assert.NotSame(before,play.Match);Assert.Equal(Side.Dark,play.Match.Current.ToMove);
    }
    [Fact]
    public void Arrow_input_returns_focus_from_a_button_to_the_board()
    {
        using var play=new Play(Game.Opening());play.Draw();play.Key("Tab");Assert.Equal("New game",play.Focus);
        play.Key("Right");Assert.Equal("Board",play.Focus);
    }
    [Fact]
    public void Broken_gum_during_a_save_shows_a_plain_fallback_and_recovers_after_the_next_save()
    {
        var project=GameScreen.Project()!;var original=File.ReadAllBytes(project);
        using var play=new Play(Game.Opening());
        try
        {
            File.WriteAllText(project,"{broken");play.Draw();Assert.NotEmpty(play.Screen.Problem);Assert.Contains(play.Screen.Parts(),p=>p.Name=="NewGameButton");
            File.WriteAllBytes(project,original);play.Draw();Assert.Empty(play.Screen.Problem);Assert.Contains(play.Screen.Parts(),p=>p.Name=="TurnText");
        }
        finally{File.WriteAllBytes(project,original);}
    }
    [Theory][InlineData(6)][InlineData(8)][InlineData(10)]
    public void Every_authored_board_size_fits_the_runtime_picture_and_refused_clicks_keep_the_board(int size)
    {
        var game=Game.Opening(size);using var play=new Play(game);play.Draw();var board=play.Screen.Board;
        Assert.True(board.Cell>0);Assert.InRange(board.X+size*board.Cell,0,play.Screen.Width);Assert.InRange(board.Y+size*board.Cell,0,play.Screen.Height);
        play.Screen.Click(play,-10,-10);Assert.Equal(game,play.Match.Current);
    }
}
