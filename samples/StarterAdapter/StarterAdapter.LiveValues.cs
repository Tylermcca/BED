using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;

namespace StarterAdapter;

public sealed partial class StarterAdapter
{
    /// <summary>The playable starter shares its own score, winning target and round result.</summary>
    public IEnumerable<LiveValueTarget> LiveValues(ProjectPaths project)
    {
        if(project.Map is not {} map||!File.Exists(map))return [];
        using var json=JsonDocument.Parse(File.ReadAllText(map));
        if(!json.RootElement.TryGetProperty("game"u8,out var game)||game.GetString()!="StarterGame")return [];
        return [new("game","Catch the lights",[new("score","Score"),new("goal","Winning target"),new("won","Round won (1 = yes)")],["start","collect","back","restart"])];
    }
}
