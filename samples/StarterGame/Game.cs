using System.Text.Json;

namespace StarterGame;

// INVENTED: a small original click-and-collect game; saved JSON owns its title and target score.
public sealed class Game
{
    public string Title {get;private set;}="Catch the lights";
    public int Goal {get;private set;}=5;
    public int Score {get;private set;}
    public bool Playing {get;private set;}
    public bool Won=>Score>=Goal;
    public void Load(string path)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(path));var n=json.RootElement;
        var title=n.GetProperty("title").GetString()!;var goal=n.GetProperty("goal").GetInt32();
        if(string.IsNullOrWhiteSpace(title)||title.Length>64||goal<1||goal>50)throw new InvalidDataException("Use a title of 1 to 64 letters and a goal from 1 to 50.");
        Title=title;Goal=goal;
    }
    public void Start(){Score=0;Playing=true;}
    public void Back()=>Playing=false;
    public void Collect(){if(Playing&&!Won)Score++;}
    public (float X,float Y) Target=>((Score*137)%560+80,(Score*83)%220+170);
}
