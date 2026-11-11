using System.Security.Cryptography;

namespace Bed.Editor.App;

/// <summary>Generation-side decode of caller-pinned texture bytes through the existing bounded Stb decoder.
/// This does not read a path, admit a project, approve content or publish a catalog.</summary>
public static class TextureAssetGeneration
{
    /// <summary>Uses the saved-artifact pixel bound before decode, independently of the larger general preview decoder budget.</summary>
    public static bool TryCreateSavedInput(AssetRecord record, ReadOnlySpan<byte> source, string encodedFormat,
        IEnumerable<string> dependencies, bool dependenciesComplete, string profile, string policyInputs,
        out AssetCatalog.Input? input, out string why)
    {
        input=null;
        if(!AssetImageDecoder.Inspect(source,encodedFormat,out var width,out var height,out why))return false;
        if(16L+(long)width*height*4>Bed.Runtime.BedTextureReader.MaxBytes)
        {why="Texture exceeds the complete 64 MiB BEDTEX1 artifact bound.";return false;}
        return TryCreateInput(record,source,encodedFormat,dependencies,dependenciesComplete,profile,policyInputs,out input,out why);
    }

    /// <summary>Copies an ordinary artifact, or wraps an explicitly decoded texture for the saved runtime generation.</summary>
    public static byte[] StoredArtifact(AssetCatalog.Lease lease)
    {
        if(lease.Texture is not null)return EncodeArtifact(lease);
        var bytes=new byte[lease.Length];lease.CopyTo(bytes);return bytes;
    }

    /// <summary>Writes a complete BEDTEX1 artifact from an explicitly decoded catalog lease. Does not save or publish it.
    /// The 64 MiB cap is for the complete saved artifact, independently of the decoder's larger general preview budget.</summary>
    public static byte[] EncodeArtifact(AssetCatalog.Lease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.Texture is not { Format: AssetCatalog.TextureEncoding.Rgba8Srgb } info || info.Width < 1 || info.Height < 1
            || info.Width > Bed.Runtime.BedTextureReader.MaxDimension || info.Height > Bed.Runtime.BedTextureReader.MaxDimension
            || 16L + lease.Length > Bed.Runtime.BedTextureReader.MaxBytes || (long)info.Width * info.Height * 4 != lease.Length)
            throw new InvalidDataException("Texture cannot fit the bounded BEDTEX1 artifact.");
        var bytes = new byte[16 + lease.Length]; "BEDTEX1\0"u8.CopyTo(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)info.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)info.Height);
        lease.CopyTo(bytes.AsSpan(16)); return bytes;
    }

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
