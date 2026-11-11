namespace Bed.Editor.App;

// Same admitted PNG/BEDTEX closure bounds; no file acquisition or distribution approval.
internal static class SpriteTextureInput
{
    internal static AssetCatalog.Lease Resolve(AssetCatalog catalog,AssetCatalog.Selection selection)
    {
        if(!catalog.TryResolve(selection,out var lease,out var why))throw new InvalidOperationException(why);
        if(lease!.Texture is not{Format:AssetCatalog.TextureEncoding.Rgba8Srgb} info||info.Width<1||info.Height<1||info.Width>8192||info.Height>8192||4L*info.Width*info.Height!=lease.Length||16L+lease.Length>Bed.Runtime.BedTextureReader.MaxBytes)
            throw new InvalidOperationException("Sprite needs an admitted RGBA8-sRGB image within the saved BEDTEX bound.");
        var input=catalog.CurrentInputs().SingleOrDefault(i=>i.Guid==selection.AssetGuid);
        if(input is null||!string.Equals(Path.GetExtension(input.ProducingRecord.Source),".png",StringComparison.OrdinalIgnoreCase)||input.ProducingRecord.SourceHash!=lease.SourceHash)
            throw new InvalidOperationException("Sprite needs the exact admitted PNG source identity.");
        if(!catalog.TryResolveTexture(selection,lease.SourceHash,out var current,out why))throw new InvalidOperationException(why);
        return current!;
    }
}
