using System.Collections.ObjectModel;
namespace Bed.Editor;

/// <summary>An owned bounded capture of a resolved scene's exact admitted sprite textures. Not a licence/export approval or filesystem freshness claim.</summary>
public sealed class SpriteAssetClosure
{
    private readonly AssetCatalog owner;
    private readonly AssetCatalog.Selection[] selections;
    private SpriteAssetClosure(AssetCatalog owner,AssetCatalog.Selection[] selections,Dictionary<string,AssetCatalog.Lease> textures,AssetCatalog.Input[] inputs)
    {this.owner=owner;this.selections=selections;Textures=new ReadOnlyDictionary<string,AssetCatalog.Lease>(textures);Inputs=Array.AsReadOnly(inputs);}
    /// <summary>Unique exact image GUIDs and their pinned RGBA8-sRGB/top-left leases. Retaining this closure retains the bytes.</summary>
    public IReadOnlyDictionary<string,AssetCatalog.Lease> Textures {get;}
    /// <summary>Deep-owned producing records (including provenance/settings/source hashes) and source metadata for the captured textures. Existing export policy must still judge them.</summary>
    public IReadOnlyList<AssetCatalog.Input> Inputs {get;}
    /// <summary>Captures all effective sprites after prefab resolution: at most 1,024 unique images, 256 MiB total RGBA, 64 MiB per complete BEDTEX artifact.
    /// Missing, ambiguous, stale, non-PNG or non-texture assets refuse the complete closure; paths are never resolved.</summary>
    public static bool Capture(Scene2 scene,AssetCatalog catalog,out SpriteAssetClosure? closure,out string why)
    {
        ArgumentNullException.ThrowIfNull(scene);ArgumentNullException.ThrowIfNull(catalog);closure=null;
        try
        {
            var ids=Scene2Resolver.Resolve(scene).Objects.Where(o=>o.Sprite is not null).Select(o=>o.Object.Asset!).Distinct(StringComparer.Ordinal).ToArray();
            if(ids.Length>1024){why="Sprite closure exceeds 1,024 textures.";return false;}
            var textures=new Dictionary<string,AssetCatalog.Lease>(StringComparer.Ordinal);var inputs=new List<AssetCatalog.Input>();var selections=new List<AssetCatalog.Selection>();long total=0;
            foreach(var id in ids)
            {
                var selection=catalog.Select(id);
                if(selection is null){why=$"Sprite image {id}: {catalog.Resolve(id).Reason}";return false;}
                if(!SpriteAssetCapture.Resolve(catalog,selection,out var lease,out var input,out why)){why=$"Sprite image {id}: {why}";return false;}
                if((total+=lease!.Length)>256L*1024*1024){why="Sprite closure exceeds 256 MiB RGBA.";return false;}
                textures.Add(id,lease);inputs.Add(input!);selections.Add(selection);
            }
            var captured=new SpriteAssetClosure(catalog,selections.ToArray(),textures,inputs.ToArray());
            if(!captured.Revalidate(catalog,out why))return false;
            closure=captured;return true;
        }
        catch(Exception e)when(e is DocumentFormatException or InvalidOperationException or ArgumentException)
        {why="Sprite closure refused: "+e.Message;return false;}
    }
    /// <summary>Rechecks original admission and content/policy tokens. Previously captured bytes remain usable as labelled retained content after refusal.</summary>
    public bool Revalidate(AssetCatalog catalog,out string why)
    {
        if(!ReferenceEquals(catalog,owner)){why="Sprite closure belongs to another asset admission.";return false;}
        // Even an empty closure must not validate against a closed/pending admission.
        if(catalog.Resolve("").State==AssetCatalog.State.Stale)
        {why="Sprite catalog is closed or refreshing.";return false;}
        foreach(var selection in selections)if(!catalog.TryResolve(selection,out _,out why))return false;
        why="";return true;
    }
}

internal static class SpriteAssetCapture
{
    internal static bool Resolve(AssetCatalog catalog,AssetCatalog.Selection selection,out AssetCatalog.Lease? lease,out AssetCatalog.Input? input,out string why)
    {
        input=null;
        if(!catalog.TryResolve(selection,out lease,out why))return false;
        if(lease!.Texture is not{Format:AssetCatalog.TextureEncoding.Rgba8Srgb} texture||texture.Width<1||texture.Height<1||texture.Width>8192||texture.Height>8192||16L+lease.Length>64L*1024*1024||4L*texture.Width*texture.Height!=lease.Length)
        {why="Select an imported image that isn't too big; models and other assets can't be sprites.";return false;}
        input=catalog.CurrentInputs().SingleOrDefault(i=>i.Guid==selection.AssetGuid);
        if(input is null||!string.Equals(Path.GetExtension(input.ProducingRecord.Source),".png",StringComparison.OrdinalIgnoreCase))
        {why="Sprites can only use imported PNG images for now.";return false;}
        if(input.ProducingRecord.SourceHash!=lease.SourceHash){why="Image generation changed; capture again.";return false;}
        return catalog.TryResolve(selection,out lease,out why);
    }
}
