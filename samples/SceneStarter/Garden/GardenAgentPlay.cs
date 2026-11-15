using System.Text.Json;
namespace SceneStarter.Garden;

// Optional, parent-owned stdio observation mode. It never reads desktop input or opens a window.
internal static class GardenAgentPlay
{
    private const int MaxRequest=512,MaxPng=1024*1024;
    internal static int Run(string project,TextReader input,TextWriter output,TextWriter error)
    {
        try
        {
            var saved=SavedScene.Open(project);
            if(!GardenLevel.CanPlay(saved))throw new InvalidOperationException("This saved game cannot share pictures of its 2D level.");
            var game=new GardenGame(GardenLevel.From(saved));
            if(Bed.Runtime.LiveValuesHost.Target is {} target)
            {
                if(target!="round_friend")throw new InvalidDataException("The garden doesn't share that object.");
                return Bed.Runtime.LiveValuesHost.Run(target,()=>game.Frame,()=>new[]{game.Player.X0,game.Player.Y0,game.VelocityX,game.VelocityY,game.JumpStrength},
                    (field,value)=>{if(field!="jumpStrength")throw new ArgumentException("Unknown garden value.");game.TuneJump(value);},
                    (key,seconds)=>{if(key is not(null or "left" or "right" or "jump" or "restart"))throw new ArgumentException("Unknown garden input.");for(var i=0;i<(int)Math.Ceiling(seconds*60-1e-9);i++)game.Update(new(key=="left",key=="right",key=="jump",key=="restart"));game.ReleaseKeys();},input,output,error);
            }
            var (w,h)=GardenRenderer.Fit(game.Level.Bounds,1280,720);var renderer=new GardenRenderer(game.Level,w,h);
            void Reply(long id,byte[]? png=null,string? refusal=null)=>output.WriteLine(JsonSerializer.Serialize(new { id,frame=game.Frame,description=GardenScript.Describe(game),png,error=refusal,mode="headless; simulation pauses between requests" }));
            Reply(0);output.Flush();long last=0;
            while(Read(input) is {} line)
            {
                using var json=JsonDocument.Parse(line,new(){MaxDepth=4});var root=json.RootElement;
                var names=root.EnumerateObject().Select(p=>p.Name).ToArray();
                if(names.Length!=4||names.Distinct().Count()!=4||names.Except(new[]{"id","op","key","seconds"}).Any())throw new InvalidDataException("The picture request contains missing or extra information.");
                var id=root.GetProperty("id").GetInt64();if(id!=last+1)throw new InvalidDataException("The picture request is out of date.");last=id;
                var op=root.GetProperty("op").GetString();var key=root.GetProperty("key").ValueKind==JsonValueKind.Null?null:root.GetProperty("key").GetString();
                var seconds=root.GetProperty("seconds").GetDouble();
                if(op=="stop"&&key is null&&seconds==0){Reply(id);output.Flush();return 0;}
                if(op=="shot"&&key is null&&seconds==0) { }
                else if(op=="hold"&&(key is "left" or "right" or "jump" or "restart")&&double.IsFinite(seconds)&&seconds>0&&seconds<=5)
                {
                    // Fixed simulation ticks, not wall-clock timing. No key remains held after this request.
                    var frames=(int)Math.Ceiling(seconds*60-1e-9);var keys=GardenScript.Parse(key+"*"+frames);
                    GardenScript.Run(game,keys,frames);game.ReleaseKeys();
                }
                else {Reply(id,refusal:"Take a picture, or hold left/right/jump/restart for more than zero and at most five seconds of play.");output.Flush();continue;}
                renderer.Render(game);var png=PngWriter.Encode(renderer.Pixels,w,h);
                if(png.Length>MaxPng)throw new InvalidDataException("The game picture is larger than the allowed size.");
                Reply(id,png);output.Flush();
            }
            return 0;
        }
        catch(Exception e)when(e is not OutOfMemoryException){error.WriteLine("The picture run stopped: "+e.Message);return 3;}
    }
    private static string? Read(TextReader input)
    {
        var line=new System.Text.StringBuilder();
        while(true)
        {
            var c=input.Read();if(c<0)return line.Length==0?null:throw new InvalidDataException("The picture request is incomplete.");
            if(c=='\n')return line.ToString().TrimEnd('\r');
            if(line.Length>=MaxRequest)throw new InvalidDataException("The picture request is too long.");line.Append((char)c);
        }
    }
}
