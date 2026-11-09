namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Owned CPU-only RGBA8 preview bytes. No decoder, file, catalog policy, or device resource is implied.</summary>
public sealed class AssetPreview
{
    private readonly byte[] pixels;
    private AssetPreview(int width, int height, byte[] pixels)
    { Width = width; Height = height; this.pixels = pixels; }
    /// <summary>Pixel width.</summary>
    public int Width { get; }
    /// <summary>Pixel height.</summary>
    public int Height { get; }
    /// <summary>Owned RGBA8 payload size, excluding caller buffers and decoder allocations.</summary>
    public int ByteCount => pixels.Length;
    /// <summary>Copies into caller storage without exposing mutable owned bytes.</summary>
    public void CopyTo(Span<byte> destination) => pixels.AsSpan().CopyTo(destination);

    /// <summary>Checks caller-selected budgets before any allocation. This does not validate an encoded image.</summary>
    public static bool TryByteCount(int width, int height, long encodedBytes, long maxEncodedBytes,
        int maxDimension, long maxDecodedBytes, out int byteCount, out string why)
    {
        byteCount = 0;
        if (maxEncodedBytes <= 0 || maxDimension <= 0 || maxDecodedBytes <= 0)
        { why = "Preview budgets must be positive."; return false; }
        if (encodedBytes < 0 || encodedBytes > maxEncodedBytes)
        { why = "Encoded input exceeds the preview budget or has an invalid length."; return false; }
        if (width <= 0 || height <= 0 || width > maxDimension || height > maxDimension)
        { why = "Preview dimensions are invalid or exceed the budget."; return false; }
        // Divide before multiplying: even int.MaxValue squared times four can overflow Int64.
        var pixels = (long)width * height;
        var ceiling = Math.Min(maxDecodedBytes, int.MaxValue);
        if (pixels > ceiling / 4)
        { why = "Decoded preview exceeds the byte budget."; return false; }
        byteCount = (int)(pixels * 4);
        why = "";
        return true;
    }

    /// <summary>Copies already-decoded caller bytes after exact RGBA8 length and budget checks.
    /// Actual decode limits, policy admission, and resource upload remain the host's separate responsibility.</summary>
    public static bool TryCreate(int width, int height, ReadOnlySpan<byte> rgba, long encodedBytes,
        long maxEncodedBytes, int maxDimension, long maxDecodedBytes, out AssetPreview? preview, out string why)
    {
        preview = null;
        if (!TryByteCount(width, height, encodedBytes, maxEncodedBytes, maxDimension, maxDecodedBytes,
            out var count, out why)) return false;
        if (rgba.Length != count)
        { why = "Decoded bytes do not match the declared RGBA8 dimensions."; return false; }
        preview = new(width, height, rgba.ToArray());
        return true;
    }
}

