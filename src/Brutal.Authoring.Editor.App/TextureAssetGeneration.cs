using System.Security.Cryptography;

namespace Brutal.Authoring.Editor.App;

/// <summary>Generation-side decode of caller-pinned texture bytes through the existing bounded Stb decoder.
/// This does not read a path, admit a project, approve content or publish a catalog.</summary>
public static class TextureAssetGeneration
{
    /// <summary>Produces an explicitly decoded RGBA8-sRGB catalog input for the exact verified source record.
    /// The caller owns generation cancellation/dependency publication and must revalidate its complete read set.</summary>
    public static bool TryCreateInput(AssetRecord record, ReadOnlySpan<byte> source, string encodedFormat,
        IEnumerable<string> dependencies, bool dependenciesComplete, string profile, string policyInputs,
        out AssetCatalog.Input? input, out string why)
    {
        input=null;var pinned=source.ToArray();
        if(Convert.ToHexStringLower(SHA256.HashData(pinned))!=record.SourceHash)
        {why="Texture source bytes differ from the pinned record.";return false;}
        if(!AssetImageDecoder.TryDecode(pinned,encodedFormat,out var image,out why))return false;
        input=new(record,pinned,image!.Rgba,dependencies,dependenciesComplete,profile,policyInputs,
            texture:new(image.Width,image.Height,AssetCatalog.TextureEncoding.Rgba8Srgb));
        why="";return true;
    }
}
