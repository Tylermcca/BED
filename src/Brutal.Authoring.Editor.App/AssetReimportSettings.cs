using System.Numerics;
using System.Text.Json;
using Brutal.Authoring.Import;
namespace Brutal.Authoring.Editor.App;

/// <summary>The approved bounded postimport scale/up-axis settings, independent of any importer security policy.</summary>
/// <param name="Scale">Approved positive uniform postimport scale.</param>
/// <param name="UpAxis">Approved positive project up axis, Y or Z.</param>
public sealed record AssetReimportSettings(float Scale,string UpAxis)
{
    /// <summary>Schema consumed by AssetSettingsSession; absent keys use only the explicitly approved defaults.</summary>
    public const string Schema="""{"type":"object","additionalProperties":false,"properties":{"scale":{"type":"number","minimum":0.001,"maximum":1000},"upAxis":{"type":"string","enum":["Y","Z"]}}}""";
    /// <summary>Refuses unknown keys/duplicates, invalid scale and unsupported project up axes.</summary>
    public static AssetReimportSettings Parse(JsonElement settings,ProjectFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if(settings.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Import settings must be an object.");
        var keys=new HashSet<string>(StringComparer.Ordinal);var scale=1f;string? axis=null;
        foreach(var p in settings.EnumerateObject())
        {
            if(!keys.Add(p.Name))throw new ArgumentException("Duplicate import setting.");
            if(p.Name=="scale")
            {if(p.Value.ValueKind!=JsonValueKind.Number||!p.Value.TryGetSingle(out scale)||!float.IsFinite(scale)||scale<.001f||scale>1000)throw new ArgumentException("Scale must be from 0.001 to 1000.");}
            else if(p.Name=="upAxis")
            {if(p.Value.ValueKind!=JsonValueKind.String||(axis=p.Value.GetString()) is not ("Y" or "Z"))throw new ArgumentException("upAxis must be Y or Z.");}
            else throw new ArgumentException("Unknown import setting: "+p.Name);
        }
        return new(scale,axis??ProjectUp(frame));
    }
    /// <summary>The rotation applied after the existing importer frame conversion; positive uniform scale never changes winding.</summary>
    public Matrix4x4 Transform(ProjectFrame frame)
    {
        if(!float.IsFinite(Scale)||Scale<.001f||Scale>1000||UpAxis is not ("Y" or "Z"))throw new ArgumentException("Invalid postimport settings.");
        var original=ProjectUp(frame);
        var rotation=original==UpAxis?Matrix4x4.Identity:Matrix4x4.CreateRotationX(original=="Y"?MathF.PI/2:-MathF.PI/2);
        return Matrix4x4.CreateScale(Scale)*rotation;
    }
    private static string ProjectUp(ProjectFrame frame)
    {
        var up=Vector3.Normalize(frame.Direction(Vector3.UnitY));
        if(Vector3.Distance(up,Vector3.UnitY)<.00001f)return "Y";
        if(Vector3.Distance(up,Vector3.UnitZ)<.00001f)return "Z";
        throw new ArgumentException("This import controller requires a positive Y-up or Z-up project frame.");
    }
}
