using Bed;
using Checkers;
namespace CheckersPlay;

public static class Program
{
    private const string Usage="usage: play-checkers [position.json] [--shot <file.png>]\n  No position starts at the title; --shot draws without a window. Keys: arrows select, Enter/Space choose, N new game, C computer, U take back, Escape quit.\n\nBRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited. See NOTICE.txt.\n";
    public static int Main(string[] args)=>Run(args,Console.Out,Console.Error);
    public static int Run(string[] args,TextWriter output,TextWriter errors)
    {
        string? file=null,shot=null;
        for(var i=0;i<args.Length;i++)switch(args[i])
        {
            case "--shot" when i+1<args.Length:shot=args[++i];break;
            case "-h" or "--help":output.Write(Usage);return 0;
            case var a when !a.StartsWith('-')&&file is null:file=a;break;
            default:errors.Write(Usage);return 2;
        }
        Game start;
        try{start=file is null?Game.Opening():Position.Read(File.ReadAllText(file));}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or DocumentFormatException or InvalidDocumentException or System.Text.Json.JsonException)
        {errors.WriteLine($"{file} isn't a checkers position: {e.Message}");return 1;}
        using var play=new Play(start,title:file is null);
        if(shot is not null){play.Draw();play.Screen.Save(shot);output.WriteLine($"Drew the game into {Path.GetFullPath(shot)}.");return 0;}
        if(!OperatingSystem.IsWindows()){errors.WriteLine("This sample's game window needs Windows; --shot works without a window.");return 1;}
        GameWindow.Play(play,play.Screen);return 0;
    }
}

// The player owns its presentation. Authoring's Match/Board and editor services stay in the authoring project.
public sealed class Play : IDisposable
{
    private readonly Game start;
    public PlayerMatch Match {get;private set;}
    public GameScreen Screen {get;}
    public bool OnTitle {get;private set;}
    public float Scale {get;set;}=1;
    public string Focus {get;internal set;}="Board";
    public Play(Game start,bool title=false,bool plain=false)
    {this.start=start;Match=new(start);Screen=new(plain);OnTitle=title&&!plain&&GameScreen.Project() is not null;}
    public void NewGame(){OnTitle=false;Match=new(start);}
    public void Draw()=>Screen.Frame(this);
    public void Key(string key)
    {
        if(key=="Tab")
        {
            var buttons=Screen.Parts().Where(p=>p.Name.EndsWith("Button",StringComparison.Ordinal)||p.Name is "ComputerMove" or "TakeBack").Select(p=>p.Words!);
            var controls=(OnTitle?buttons:buttons.Append("Board")).ToArray();
            if(controls.Length==0)return;
            Focus=controls[(Array.IndexOf(controls,Focus)+1)%controls.Length];return;
        }
        if(!OnTitle&&(key is "Enter" or "Space")&&Focus!="Board")
        {
            if(Focus=="New game")NewGame();else if(Focus=="Computer move")Match.ComputerMove();else if(Focus=="Take back")Match.TakeBack();return;
        }
        if(key is "New game" or "N"||(OnTitle&&key is "Enter" or "Space")){NewGame();return;}
        if(OnTitle)return;
        if(key=="C")Match.ComputerMove();else if(key=="U")Match.TakeBack();
        else if(key is "Enter" or "Space")Match.Click(Match.Current.Name(Screen.Cursor/Match.Current.Size,Screen.Cursor%Match.Current.Size));
        else {if(key is "Left" or "Right" or "Up" or "Down")Focus="Board";var n=Match.Current.Size;Screen.Cursor=key switch{"Left"=>Math.Max(0,Screen.Cursor-1),"Right"=>Math.Min(n*n-1,Screen.Cursor+1),"Up"=>Math.Max(0,Screen.Cursor-n),"Down"=>Math.Min(n*n-1,Screen.Cursor+n),_=>Screen.Cursor};}
    }
    public void Dispose()=>Screen.Dispose();
}

public sealed class PlayerMatch(Game start)
{
    private readonly List<Game> history=[start];
    private readonly List<string> picked=[];
    public Game Current=>history[^1];
    public IReadOnlyList<string> Picked=>picked;
    public string Said {get;private set;}="";
    public bool CanTakeBack=>history.Count>1;
    public string Status=>Rules.Winner(Current) is {} winner?$"{winner} won in {history.Count-1} moves.":$"Move {history.Count}: {Current.ToMove} to play.";
    public string Click(string square)
    {
        var game=Current;var mine=game.Parse(square) is {} at&&Game.Owner(game.At(at.R,at.C))==game.ToMove;
        if(picked.Count==1&&picked[0]==square){picked.Clear();return Said=$"Let go of {square}.";}
        if(picked.Count<=1&&mine){picked.Clear();picked.Add(square);return Said=$"Picked {square}.";}
        if(picked.Count==0)return Said=$"Pick one of {game.ToMove.ToString().ToLowerInvariant()}'s pieces.";
        picked.Add(square);var path=picked.ToList();var legal=Rules.LegalMoves(game);
        if(!legal.Any(m=>m.SequenceEqual(path))&&legal.Any(m=>m.Count>path.Count&&m.Take(path.Count).SequenceEqual(path)))return Said=$"{string.Join("-",path)}: keep jumping.";
        picked.Clear();return Play(new MovePiece(path));
    }
    public string Play(MovePiece move)
    {
        var result=move.Apply(Current);if(result.Document is not {} next)return Said=string.Join(" ",result.Issues.Select(i=>i.Message));
        history.Add(next);return Said=$"Played {string.Join("-",move.Path)}.";
    }
    public void ComputerMove()=>Said=Computer.Choose(Current) is {} move?"The computer: "+Play(move):"No move to play.";
    public void TakeBack(){if(!CanTakeBack)return;history.RemoveAt(history.Count-1);picked.Clear();Said="Took back the last move.";}
}
