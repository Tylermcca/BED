namespace SceneStarter.Garden;

/// <summary>The keys held during one step.</summary>
public readonly record struct GardenInput(bool Left,bool Right,bool Jump,bool Restart=false)
{
    public static readonly GardenInput None=default;
}

/// <summary>The garden game's rules as plain code: a fixed 1/60 s step with gravity, box collisions against the solid
/// layer, coins collected on touch and a win at a flag once every coin is in. No window or clock types.</summary>
public sealed class GardenGame
{
    public const float Step=1f/60f;
    public const float RunSpeed=4.5f,GroundAccel=45f,AirAccel=28f,Gravity=32f,JumpSpeed=10.6f,MaxFall=15f;
    private const float CoyoteTime=0.1f,JumpBufferTime=0.12f;
    private readonly Box start;      // player's touch box at the start
    private readonly float pictureDx,pictureDy;  // picture offset from the touch box
    private readonly GardenSprite[] solids,coins,goals;
    private GardenInput previous;
    private float coyote,jumpBuffer;

    public GardenLevel Level {get;}
    public Box Player {get;private set;}
    public float VelocityX {get;private set;}
    public float VelocityY {get;private set;}
    public bool OnGround {get;private set;}
    public bool FacingLeft {get;private set;}
    public bool Won {get;private set;}
    public int Frame {get;private set;}
    /// <summary>Seconds left to show "find every coin first" after touching a flag too early.</summary>
    public float HintTime {get;private set;}
    public bool[] Collected {get;}
    public int CoinCount=>coins.Length;
    public int CoinsCollected=>Collected.Count(c=>c);
    public IReadOnlyList<GardenSprite> Coins=>coins;
    public Box PlayerPicture
    {
        get{var x=Player.X0+pictureDx;var y=Player.Y0+pictureDy;return new(x,y,x+Level.Player.Picture.Width,y+Level.Player.Picture.Height);}
    }

    public GardenGame(GardenLevel level)
    {
        Level=level;start=level.Player.Touch;
        pictureDx=level.Player.Picture.X0-start.X0;pictureDy=level.Player.Picture.Y0-start.Y0;
        solids=level.Sprites.Where(s=>s.Role==GardenRole.Solid).ToArray();
        coins=level.Sprites.Where(s=>s.Role==GardenRole.Coin).ToArray();
        goals=level.Sprites.Where(s=>s.Role==GardenRole.Goal).ToArray();
        Collected=new bool[coins.Length];
        Restart();
    }

    public void Restart()
    {
        Player=start;VelocityX=VelocityY=0;OnGround=false;FacingLeft=false;Won=false;HintTime=0;coyote=jumpBuffer=0;
        Array.Clear(Collected);
        // A start placed slightly into the ground (or a crate) stands on top of it instead.
        for(var i=0;i<8;i++)
        {
            var under=solids.Where(s=>Player.Overlaps(s.Touch,0.0005f)).Select(s=>s.Touch.Y1).DefaultIfEmpty(float.NaN).Max();
            if(float.IsNaN(under)||under-Player.Y0>Player.Height*0.5f)break;
            Player=Player.Offset(0,under-Player.Y0);
        }
        OnGround=Touching(Player.Offset(0,-0.02f));
    }

    public void Update(GardenInput input)
    {
        Frame++;
        if(input.Restart&&!previous.Restart){Restart();previous=input;return;}
        var jumpPressed=input.Jump&&!previous.Jump;previous=input;
        HintTime=MathF.Max(0,HintTime-Step);
        var direction=Won?0:(input.Right?1:0)-(input.Left?1:0);
        if(direction!=0)FacingLeft=direction<0;
        var target=direction*RunSpeed;var accel=(OnGround?GroundAccel:AirAccel)*Step;
        VelocityX=VelocityX<target?MathF.Min(target,VelocityX+accel):MathF.Max(target,VelocityX-accel);

        coyote=OnGround?CoyoteTime:MathF.Max(0,coyote-Step);
        jumpBuffer=jumpPressed&&!Won?JumpBufferTime:MathF.Max(0,jumpBuffer-Step);
        if(jumpBuffer>0&&coyote>0){VelocityY=JumpSpeed;jumpBuffer=coyote=0;OnGround=false;}
        else if(!input.Jump&&VelocityY>JumpSpeed*0.4f)VelocityY=JumpSpeed*0.4f;   // short hop when the key is let go early
        VelocityY=MathF.Max(-MaxFall,VelocityY-Gravity*Step);

        MoveX(VelocityX*Step);
        OnGround=false;MoveY(VelocityY*Step);

        var bounds=Level.Bounds;
        if(Player.X0<bounds.X0){Player=Player.Offset(bounds.X0-Player.X0,0);VelocityX=0;}
        if(Player.X1>bounds.X1){Player=Player.Offset(bounds.X1-Player.X1,0);VelocityX=0;}
        if(Player.Y1>bounds.Y1){Player=Player.Offset(0,bounds.Y1-Player.Y1);VelocityY=MathF.Min(VelocityY,0);}   // the top of the level is a ceiling
        if(Player.Y1<bounds.Y0-1){var coinsKept=(bool[])Collected.Clone();Restart();coinsKept.CopyTo(Collected,0);}   // fell out: back to the start, coins kept

        for(var i=0;i<coins.Length;i++)if(!Collected[i]&&Player.Overlaps(coins[i].Touch))Collected[i]=true;
        if(!Won&&goals.Any(g=>Player.Overlaps(g.Touch)))
        {
            if(CoinsCollected==CoinCount){Won=true;VelocityX=0;}
            else HintTime=2f;
        }
    }

    private bool Touching(Box box)=>solids.Any(s=>box.Overlaps(s.Touch,0.0005f));

    private void MoveX(float dx)
    {
        if(dx==0)return;
        var moved=Player.Offset(dx,0);
        foreach(var s in solids)
            if(moved.Overlaps(s.Touch,0.0005f)&&!Player.Overlaps(s.Touch,0.0005f))   // never snag on something already overlapped
            {
                moved=dx>0?moved.Offset(s.Touch.X0-moved.X1,0):moved.Offset(s.Touch.X1-moved.X0,0);
                VelocityX=0;
            }
        Player=moved;
    }

    private void MoveY(float dy)
    {
        if(dy==0)return;
        var moved=Player.Offset(0,dy);
        foreach(var s in solids)
            if(moved.Overlaps(s.Touch,0.0005f)&&!Player.Overlaps(s.Touch,0.0005f))
            {
                if(dy<0){moved=moved.Offset(0,s.Touch.Y1-moved.Y0);OnGround=true;}
                else moved=moved.Offset(0,s.Touch.Y0-moved.Y1);
                VelocityY=0;
            }
        Player=moved;
    }
}
