using System.Numerics;
using Bed.Runtime;
using SceneStarter.Garden;
using Xunit;
namespace Bed.Tests;

/// <summary>The sprite sample's small game as pure code: roles by convention, gravity, box collisions, coins, the win
/// and restart. No window, clock or saved project.</summary>
public sealed class GardenGameTests
{
    private static readonly BedTexture Full=new(4,4,Enumerable.Repeat((byte)255,64).ToArray());
    private static Scene2Object Sprite(string id,float x,float y,float w,float h,int order)
        =>new(SceneObject.At(id,"tex",new Vector3(x,y,0)),new SceneSprite(new Vector2(w,h),new Vector2(0.5f,0),order,SceneSpriteFilter.Linear));
    private static GardenGame Game(float playerX=-4,float crateX=2,params Scene2Object[] extra)
    {
        var objects=new List<Scene2Object>
        {
            Sprite("sky",0,-3,14,8,-20),
            Sprite("ground",0,-3,14,2,0),                  // top at y -1
            Sprite("crate",crateX,-1,1,1,0),               // top at y 0
            Sprite("round_friend",playerX,0,1,1,10),       // starts in the air
            Sprite("coin_a",-2,-1,0.5f,0.5f,10),
            Sprite("post_flag",5,-1,1,2,10),
        };
        objects.AddRange(extra);
        return new GardenGame(GardenLevel.From(objects,new Dictionary<string,BedTexture>{["tex"]=Full}));
    }
    private static void Run(GardenGame game,GardenInput input,int frames){for(var i=0;i<frames;i++)game.Update(input);}

    [Fact]
    public void Roles_follow_the_documented_convention()
    {
        Assert.Equal(GardenRole.Player,GardenLevel.RoleOf("round_friend",10));
        Assert.Equal(GardenRole.Coin,GardenLevel.RoleOf("coin_7",0));
        Assert.Equal(GardenRole.Goal,GardenLevel.RoleOf("post_2_flag",10));
        Assert.Equal(GardenRole.Solid,GardenLevel.RoleOf("anything_else",0));
        Assert.Equal(GardenRole.Scenery,GardenLevel.RoleOf("flower_left",10));
        Assert.Equal(GardenRole.Scenery,GardenLevel.RoleOf("hills",-10));
    }

    [Fact]
    public void Player_falls_onto_the_ground_and_stands_there()
    {
        var game=Game();Run(game,GardenInput.None,60);
        Assert.True(game.OnGround);Assert.Equal(-1,game.Player.Y0,3);Assert.Equal(-4,game.Player.X0+0.5f,3);
    }

    [Fact]
    public void Walking_into_a_crate_stops_at_its_side_and_jumping_lands_on_top()
    {
        var game=Game(playerX:0);Run(game,GardenInput.None,30);
        Run(game,new(false,true,false),90);
        Assert.Equal(1.5f,game.Player.X1,3);Assert.Equal(-1,game.Player.Y0,3);   // crate's left side is x 1.5
        Run(game,new(false,true,true),20);Run(game,new(false,false,true),20);Run(game,GardenInput.None,20);
        Assert.Equal(0,game.Player.Y0,3);Assert.True(game.OnGround);              // on the crate's top
    }

    [Fact]
    public void A_jump_rises_higher_than_one_crate_and_lets_go_early_for_a_short_hop()
    {
        var full=Game();Run(full,GardenInput.None,30);var peak=float.MinValue;
        for(var i=0;i<40;i++){full.Update(new(false,false,true));peak=MathF.Max(peak,full.Player.Y0);}
        Assert.InRange(peak+1,1.3f,2.2f);
        var hop=Game();Run(hop,GardenInput.None,30);hop.Update(new(false,false,true));var hopPeak=float.MinValue;
        for(var i=0;i<40;i++){hop.Update(GardenInput.None);hopPeak=MathF.Max(hopPeak,hop.Player.Y0);}
        Assert.True(hopPeak<peak-0.5f);
    }

    [Fact]
    public void Coins_are_collected_on_touch_and_the_flag_wins_only_after_every_coin()
    {
        var game=Game(playerX:-4,crateX:-6);Run(game,GardenInput.None,30);
        Assert.Equal(0,game.CoinsCollected);Assert.Equal(1,game.CoinCount);
        Run(game,new(false,true,false),30);   // walks over the coin at x -2
        Assert.Equal(1,game.CoinsCollected);Assert.True(game.Collected[0]);
        var early=Game(playerX:3.5f,crateX:-5);Run(early,GardenInput.None,30);Run(early,new(false,true,false),40);
        Assert.False(early.Won);Assert.True(early.HintTime>0);   // touched the flag with the coin still out
        Run(game,new(false,true,false),200);
        Assert.True(game.Won);
        Run(game,new(true,false,true),30);var x=game.Player.X0;Run(game,new(true,false,false),30);
        Assert.Equal(x,game.Player.X0);   // no more moving once won
    }

    [Fact]
    public void Restart_puts_the_player_and_coins_back_and_falling_out_respawns()
    {
        var game=Game(playerX:-4);Run(game,new(false,true,false),40);Assert.Equal(1,game.CoinsCollected);
        game.Update(new(false,false,false,true));
        Assert.Equal(0,game.CoinsCollected);Assert.Equal(-4,game.Player.X0+0.5f,3);Assert.False(game.Won);
        var hole=new GardenGame(GardenLevel.From([Sprite("sky",0,-3,14,8,-20),Sprite("round_friend",0,0,1,1,10)],new Dictionary<string,BedTexture>{["tex"]=Full}));
        Run(hole,GardenInput.None,10);var fellFrom=hole.Player.Y0;Run(hole,GardenInput.None,120);
        Assert.True(hole.Player.Y0>fellFrom-6);   // came back to the start instead of falling forever
    }

    [Fact]
    public void The_game_follows_the_saved_layout_not_fixed_positions()
    {
        var moved=Game(playerX:1,crateX:-3);Run(moved,GardenInput.None,30);
        Assert.Equal(1,moved.Player.X0+0.5f,3);
        Run(moved,new(true,false,false),120);
        Assert.Equal(-2.5f,moved.Player.X0,3);   // stopped by the crate where the level now puts it
    }

    [Fact]
    public void Solid_touch_boxes_skip_thin_tops_and_the_script_parser_reads_keys_and_counts()
    {
        var rgba=new byte[4*4*4];for(var y=1;y<4;y++)for(var x=0;x<4;x++)rgba[(y*4+x)*4+3]=255;rgba[3]=255;   // one tuft pixel on the top row
        var box=GardenLevel.Opaque(new BedTexture(4,4,rgba),0.9f);Assert.Equal(0.75f,box.Y1,3);
        Assert.Equal(1f,GardenLevel.Opaque(new BedTexture(4,4,rgba),0).Y1,3);
        var script=GardenScript.Parse("right*3,right+jump*2,none");
        Assert.Equal(6,script.Length);Assert.True(script[3].Jump&&script[3].Right);Assert.Equal(GardenInput.None,script[5]);
        Assert.Throws<ArgumentException>(()=>GardenScript.Parse("fly*2"));
    }
}
