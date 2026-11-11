namespace SceneStarter.Garden;

/// <summary>Scripted keys for headless runs: comma-separated steps of <c>keys*frames</c>, where keys are <c>left</c>,
/// <c>right</c>, <c>jump</c>, <c>restart</c> or <c>none</c> joined by <c>+</c>. Example: <c>right*40,right+jump*12,none*30</c>.
/// After the script ends no keys are held.</summary>
public static class GardenScript
{
    public const int MaxFrames=36000;
    public static GardenInput[] Parse(string script)
    {
        var frames=new List<GardenInput>();
        foreach(var raw in script.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var parts=raw.Split('*');
            if(parts.Length>2)throw new ArgumentException("Use keys*frames in an input step: "+raw);
            var count=1;
            if(parts.Length==2&&(!int.TryParse(parts[1],out count)||count<1||count>MaxFrames))throw new ArgumentException("Input step frames must be 1 to "+MaxFrames+": "+raw);
            bool left=false,right=false,jump=false,restart=false;
            foreach(var key in parts[0].Split('+',StringSplitOptions.TrimEntries))
                switch(key.ToLowerInvariant())
                {
                    case "left":left=true;break;case "right":right=true;break;case "jump":jump=true;break;
                    case "restart":restart=true;break;case "none":break;
                    default:throw new ArgumentException("Unknown input key '"+key+"'; use left, right, jump, restart or none.");
                }
            for(var i=0;i<count;i++)frames.Add(new(left,right,jump,restart));
            if(frames.Count>MaxFrames)throw new ArgumentException("The input script is longer than "+MaxFrames+" frames.");
        }
        return frames.ToArray();
    }

    /// <summary>Runs the game for <paramref name="frames"/> steps from the script (no keys after it ends).</summary>
    public static void Run(GardenGame game,GardenInput[] script,int frames)
    {
        for(var i=0;i<frames;i++)game.Update(i<script.Length?script[i]:GardenInput.None);
    }

    public static string Describe(GardenGame game)=>
        FormattableString.Invariant($"Garden after {game.Frame} frames: coins {game.CoinsCollected}/{game.CoinCount}, player at ({game.Player.X0+game.Player.Width/2:0.00}, {game.Player.Y0:0.00}), {(game.Won?"won":"playing")}.");
}
