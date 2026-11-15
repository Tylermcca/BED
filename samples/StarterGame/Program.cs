using System.Security.Cryptography;
using System.Text.Json;

namespace StarterGame;

public static class Program
{
    public static int Main(string[] args)
    {
        var output=Console.Out;
        try
        {
            string root=Directory.GetCurrentDirectory();bool headless=Environment.GetEnvironmentVariable("BED_GAME_HEADLESS")=="1",observe=false;string? shot=null;string script="";
            for(var i=0;i<args.Length;i++)switch(args[i])
            {
                case "--project":root=Path.GetFullPath(args[++i]);break;
                case "--headless":headless=true;break;
                case "--observe":headless=observe=true;break;
                case "--shot":shot=Path.GetFullPath(args[++i]);headless=true;break;
                case "--script":script=args[++i];headless=true;break;
                default:throw new ArgumentException("Use --project <folder>, --headless, --script start,collect,back, --shot <png>, or --observe.");
            }
            var game=new Game();game.Load(Path.Combine(root,"data","settings.json"));
            Console.SetOut(Console.Error); // Gum diagnostics never become observation replies.
            void Apply(string action)
            {
                switch(action){case "start" or "restart":game.Start();break;case "collect":game.Collect();break;case "back":game.Back();break;case "frame":break;default:throw new ArgumentException("Choose start, collect, back, restart, frame or stop.");}
                game.Load(Path.Combine(root,"data","settings.json"));
            }
            if(Bed.Runtime.LiveValuesHost.Target is not null)
            {
                long frame=0;
                return Bed.Runtime.LiveValuesHost.Run("game",()=>frame,()=>[game.Score,game.Goal,game.Won?1:0],
                    (_,_)=>throw new ArgumentException("These live values are read-only; edit the saved winning target in BED."),
                    (key,seconds)=>
                    {
                        if(key is not null&&key is not("start" or "collect" or "back" or "restart"))throw new ArgumentException("Choose start, collect, back or restart.");
                        if(key is null)game.Load(Path.Combine(root,"data","settings.json"));else Apply(key);
                        frame+=checked((long)Math.Ceiling(seconds*60));
                    },Console.In,output,Console.Error);
            }
            using var screen=new GameScreen(root);screen.Frame(game);
            if(Environment.GetEnvironmentVariable("BED_AI_OBSERVE")=="1")return Pictures(game,screen,Console.In,output,Console.Error);
            void Action(string action){Apply(action);screen.Frame(game);}
            void Reply()=>output.WriteLine(JsonSerializer.Serialize(new{screen=game.Playing?"Play":"Menu",game.Title,game.Score,game.Goal,game.Won,screen.Reloads,picture=Convert.ToHexStringLower(SHA256.HashData(screen.Pixels))}));
            if(observe)
            {
                Reply();string? line;
                while((line=Console.ReadLine()) is not null)
                {
                    if(line.Length>1024)throw new ArgumentException("Keep a live-value request within 1024 characters.");
                    using var request=JsonDocument.Parse(line);var action=request.RootElement.GetProperty("action").GetString()!;if(action=="stop")break;Action(action);Reply();
                }
            }
            else if(headless)
            {
                var actions=script.Split(',',StringSplitOptions.RemoveEmptyEntries);if(actions.Length>100)throw new ArgumentException("Use at most 100 scripted actions.");
                foreach(var action in actions)Action(action);if(shot is not null)screen.Save(shot);Reply();
            }
            else if(OperatingSystem.IsWindows())GameWindow.Play(game,screen,Path.Combine(root,"data","settings.json"));
            else throw new PlatformNotSupportedException("This starter's window runs on Windows; use --headless to check its screens and rules elsewhere.");
            return 0;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or PlatformNotSupportedException)
        {Console.Error.WriteLine("The game couldn't start or continue: "+e.Message);return 1;}
        finally{Console.SetOut(output);}
    }
    // The same game-owned PNG as --shot, on BED's bounded private picture connection.
    private static int Pictures(Game game,GameScreen screen,TextReader input,TextWriter output,TextWriter error)
    {
        try
        {
            void Reply(long id,byte[]? png=null)
            {
                var description=$"{game.Title}. {(game.Playing?"Play":"Menu")}. Score {game.Score} of {game.Goal}.";
                output.WriteLine(JsonSerializer.Serialize(new{id,frame=0,description,png,error=(string?)null,mode="headless; simulation pauses between requests"}));output.Flush();
            }
            Reply(0);long last=0;
            while(ReadPictureRequest(input) is {} line)
            {
                using var json=JsonDocument.Parse(line,new(){MaxDepth=4});var request=json.RootElement;
                var fields=request.EnumerateObject().Select(p=>p.Name).ToArray();
                if(fields.Length!=4||fields.Distinct().Count()!=4||fields.Except(new[]{"id","op","key","seconds"}).Any())throw new InvalidDataException("The picture request contains missing or extra information.");
                var id=request.GetProperty("id").GetInt64();if(last==long.MaxValue||id!=last+1)throw new InvalidDataException("The picture request is out of date.");last=id;
                var op=request.GetProperty("op").GetString();
                if(request.GetProperty("key").ValueKind!=JsonValueKind.Null||request.GetProperty("seconds").GetDouble()!=0)throw new InvalidDataException("Pictures do not advance the game or hold a key.");
                if(op=="stop"){Reply(id);return 0;}
                if(op!="shot")throw new InvalidDataException("Use shot or stop for a picture run.");
                var png=screen.Png();if(png.Length>1024*1024)throw new InvalidDataException("The game picture exceeds one MiB.");
                Reply(id,png);
            }
            return 0;
        }
        catch(Exception e)when(e is not OutOfMemoryException){error.WriteLine("Picture run stopped: "+e.Message);return 3;}
    }
    private static string? ReadPictureRequest(TextReader input)
    {
        var text=new System.Text.StringBuilder();while(true)
        {
            var value=input.Read();if(value<0)return text.Length==0?null:throw new InvalidDataException("Incomplete picture request.");
            if(value=='\n')return text.ToString().TrimEnd('\r');
            if(text.Length>=512)throw new InvalidDataException("Keep a picture request within 512 characters.");text.Append((char)value);
        }
    }
}
