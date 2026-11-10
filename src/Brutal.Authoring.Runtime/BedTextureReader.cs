using System.Buffers.Binary;
namespace Brutal.Authoring.Runtime;

/// <summary>Owned top-left RGBA8 pixels; RGB is sRGB and alpha is linear.</summary>
/// <param name="Width">Positive pixel width.</param>
/// <param name="Height">Positive pixel height.</param>
/// <param name="Rgba">Owned row-major RGBA bytes, four bytes per pixel, top row first.</param>
public sealed record BedTexture(int Width, int Height, byte[] Rgba);
/// <summary>Bounded BEDTEX1 reader. Magic is eight bytes (BEDTEX1 followed by zero), then little-endian width/height.</summary>
public static class BedTextureReader
{
    /// <summary>Maximum image dimension.</summary>
    public const int MaxDimension = 8192;
    /// <summary>Maximum complete artifact size, including its 16-byte header.</summary>
    public const int MaxBytes = 64 * 1024 * 1024;
    /// <summary>Validates dimensions and exact byte length before copying any pixels. No decoding or source file access.</summary>
    /// <param name="bytes">One complete BEDTEX1 artifact, including its 16-byte header.</param>
    /// <returns>A fresh owned pixel array. RGB values remain sRGB; alpha remains linear.</returns>
    /// <exception cref="InvalidDataException">The header, dimensions, byte budget or exact payload length are invalid.</exception>
    public static BedTexture Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16 || bytes.Length > MaxBytes || !bytes[..8].SequenceEqual("BEDTEX1\0"u8)) throw new InvalidDataException("Invalid BEDTEX1 header or size.");
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), height = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension || 16L + (long)width * height * 4 != bytes.Length)
            throw new InvalidDataException("Invalid BEDTEX1 dimensions or pixel byte count.");
        return new((int)width, (int)height, bytes[16..].ToArray());
    }
}
