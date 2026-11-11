using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed.Editor;

/// <summary>Image import defaults copied once into a new sprite; changing them never edits a placed object.</summary>
/// <param name="PixelsPerMetre">Positive finite image density.</param>
/// <param name="Pivot">Bottom-left normalized pivot.</param>
/// <param name="Filter">Sampling to copy into the new placement.</param>
public sealed record SpriteAssetDefaults(double PixelsPerMetre,Vector2 Pivot,SceneSpriteFilter Filter)
{
    /// <summary>Reads Settings.spriteDefaults version 1 strictly; absence means 100 pixels/metre, centre and nearest.
    /// Other importer settings remain owned by their importer. Explicit null, unknown default fields and duplicate settings refuse.</summary>
    public static SpriteAssetDefaults Read(JsonElement settings)
    {
        CheckSettings(settings);
        if(!settings.TryGetProperty("spriteDefaults",out var d))return new(100,new(.5f,.5f),SceneSpriteFilter.Nearest);
        if(d.ValueKind!=JsonValueKind.Object)throw new ArgumentException("spriteDefaults must be an object.");
        var fields=d.EnumerateObject().ToArray();
        if(fields.Length!=4||fields.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=4||fields.Any(p=>p.Name is not("version" or "pixelsPerMetre" or "pivot" or "filter")))
            throw new ArgumentException("spriteDefaults requires exactly version, pixelsPerMetre, pivot and filter.");
        if(!d.GetProperty("version").TryGetInt32(out var version)||version!=1)throw new ArgumentException("Unsupported spriteDefaults version.");
        var pivot=d.GetProperty("pivot");
        if(pivot.ValueKind!=JsonValueKind.Array||pivot.GetArrayLength()!=2)throw new ArgumentException("Sprite pivot needs two components.");
        var filter=d.GetProperty("filter").GetString() switch{"nearest"=>SceneSpriteFilter.Nearest,"linear"=>SceneSpriteFilter.Linear,_=>throw new ArgumentException("Unknown sprite filter.")};
        var value=new SpriteAssetDefaults(d.GetProperty("pixelsPerMetre").GetDouble(),new(pivot[0].GetSingle(),pivot[1].GetSingle()),filter);
        value.Validate();return value;
    }
    /// <summary>Reports malformed defaults without changing metadata or files.</summary>
    public static bool TryRead(JsonElement settings,out SpriteAssetDefaults? defaults,out string why)
    {
        defaults=null;
        try{defaults=Read(settings);why="";return true;}
        catch(Exception e)when(e is ArgumentException or InvalidOperationException or FormatException or JsonException or OverflowException)
        {why="Image sprite defaults refused: "+e.Message;return false;}
    }
    /// <summary>Returns detached settings with just spriteDefaults replaced, preserving unrelated settings. Writes no sidecar or catalog.</summary>
    public JsonElement WithSettings(JsonElement settings)
    {
        Validate();CheckSettings(settings);var node=JsonNode.Parse(settings.GetRawText())!.AsObject();
        node["spriteDefaults"]=JsonSerializer.SerializeToNode(new{version=1,pixelsPerMetre=PixelsPerMetre,pivot=new[]{Pivot.X,Pivot.Y},filter=Filter==SceneSpriteFilter.Nearest?"nearest":"linear"});
        return JsonSerializer.SerializeToElement(node);
    }
    /// <summary>Creates explicit metre dimensions from the captured decoded image. Reopening/reimporting never calls this for existing sprites.</summary>
    public SceneSprite CreateSprite(int width,int height,int order=0)
    {
        Validate();if(width<1||height<1||width>8192||height>8192||16L+4L*width*height>64L*1024*1024)throw new ArgumentException("Image exceeds bounded saved texture dimensions.");
        var size=new Vector2((float)(width/PixelsPerMetre),(float)(height/PixelsPerMetre));
        if(!float.IsFinite(size.X)||!float.IsFinite(size.Y)||size.X<=0||size.Y<=0)throw new ArgumentException("Sprite density produces nonfinite or zero metre dimensions.");
        return new(size,Pivot,order,Filter);
    }
    private void Validate()
    {
        if(!double.IsFinite(PixelsPerMetre)||PixelsPerMetre<=0||!float.IsFinite(Pivot.X)||!float.IsFinite(Pivot.Y)||Pivot.X<0||Pivot.X>1||Pivot.Y<0||Pivot.Y>1||!Enum.IsDefined(Filter))
            throw new ArgumentException("Sprite density must be positive finite; pivot must be within 0..1 and filter supported.");
    }
    private static void CheckSettings(JsonElement settings)
    {
        if(settings.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Image settings must be an object.");
        var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var p in settings.EnumerateObject())if(!keys.Add(p.Name))throw new ArgumentException("Duplicate image setting.");
    }
}
