using System.Text.Json.Nodes;
namespace Bed;

/// <summary>Explicit project capability and one saved authoring-to-generated scene-v2 mapping. This model performs no I/O or admission.</summary>
public sealed class Scene2BuildSettings
{
    /// <summary>Maximum UTF-8 bytes in a project settings document read or produced here.</summary>
    public const int MaxProjectBytes=1024*1024;
    /// <summary>Copies a portable source, separate private generated path and one or both supported spaces.</summary>
    public Scene2BuildSettings(string source,string generated,IEnumerable<SceneSpace> spaces)
    {
        ProjectBuildStamp.Portable(source);ProjectBuildStamp.Portable(generated);
        if(source.StartsWith(".bed/",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The authoring source must be outside .bed.",nameof(source));
        if(!generated.StartsWith(".bed/generated/scenes/",StringComparison.Ordinal)||string.Equals(source,generated,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Generated scenes must have a separate path under .bed/generated/scenes/.",nameof(generated));
        ArgumentNullException.ThrowIfNull(spaces);var owned=spaces.Take(3).ToArray();
        if(owned.Length is <1 or >2||owned.Distinct().Count()!=owned.Length||owned.Any(s=>!Enum.IsDefined(s)))throw new ArgumentException("Declare one or both supported coordinate spaces without duplicates.",nameof(spaces));
        Source=source;Generated=generated;Spaces=Array.AsReadOnly(owned);
    }
    /// <summary>Portable project-relative saved authoring source; a host must separately admit it.</summary>
    public string Source {get;}
    /// <summary>Portable private generated path under .bed/generated/scenes/, never the source.</summary>
    public string Generated {get;}
    /// <summary>Owned explicit supported scene spaces.</summary>
    public IReadOnlyList<SceneSpace> Spaces {get;}
    /// <summary>Explicit authored-body capability; false by default. Parsing never starts simulation or grants a game this capability.</summary>
    public bool SupportsPhysics {get;init;}
    /// <summary>Reads strict sceneBuild settings version1 / sceneVersion2; returns null when absent. Bounds input to one MiB and depth64.</summary>
    public static Scene2BuildSettings? ReadProject(string json)
    {
        ArgumentNullException.ThrowIfNull(json);var root=Scene2Read.Parse(json,MaxProjectBytes);
        if(root.ValueKind!=System.Text.Json.JsonValueKind.Object)throw new DocumentFormatException("$","Project settings must be an object.");
        if(!root.TryGetProperty("sceneBuild",out var value))return null;
        var r=new Scene2Read(value,"/sceneBuild");r.Only("version","sceneVersion","source","generated","spaces","physics");
        if(r.Get("version").Integer()!=1||r.Get("sceneVersion").Integer()!=2)throw new DocumentFormatException("/sceneBuild","Expected settings version1 and sceneVersion2.");
        try{return new(r.Get("source").Text(1,1024),r.Get("generated").Text(1,1024),r.Get("spaces").Array(2,true).Select(s=>s.Space())){SupportsPhysics=r.Has("physics")&&r.Get("physics").Boolean()};}
        catch(ArgumentException e){throw new DocumentFormatException("/sceneBuild",e.Message);}
    }
    /// <summary>Produces updated sceneBuild JSON while preserving unrelated root/play fields. Writes nothing and grants no build permission.</summary>
    public static string WriteProject(string json,Scene2BuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);_=ReadProject(json);var root=JsonNode.Parse(json)!.AsObject();
        root["sceneBuild"]=new JsonObject{{"version",1},{"sceneVersion",2},{"source",settings.Source},{"generated",settings.Generated},
            {"spaces",new JsonArray(settings.Spaces.Select(s=>(JsonNode?)JsonValue.Create(s==SceneSpace.ThreeD?"3d":"2d-xy")).ToArray())}};
        if(settings.SupportsPhysics)root["sceneBuild"]!["physics"]=true;
        var result=root.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
        if(Scene2Read.Utf8.GetByteCount(result)>MaxProjectBytes)throw new DocumentFormatException("$","Project settings exceed one MiB.");
        return result;
    }
}
