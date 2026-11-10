using Brutal.Authoring.Runtime;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Editor.ImGui;

// Selected texture only: at most 64 x 64 samples and 128 x 128 screen pixels.
// Like the credit logo, this draws coloured runs in either host without a native texture allocation.
internal sealed class AssetThumbnail(int width, int height, int columns, int rows, byte[] samples)
{
    internal int Width => width;
    internal int Height => height;
    internal static AssetThumbnail? Create(AssetCatalog.Lease lease, out string why)
    {
        why = "";
        if (lease.Length > BedTextureReader.MaxBytes)
        { why = "The texture exceeds the preview byte limit."; return null; }
        try
        {
            int width, height;
            byte[] rgba;
            if (lease.Texture is { } info)
            {
                width = info.Width; height = info.Height;
                if (info.Format != AssetCatalog.TextureEncoding.Rgba8Srgb || width < 1 || height < 1
                    || width > BedTextureReader.MaxDimension || height > BedTextureReader.MaxDimension
                    || 16L + lease.Length > BedTextureReader.MaxBytes || (long)width * height * 4 != lease.Length)
                { why = "The texture has invalid dimensions or pixel bytes."; return null; }
                // The catalog owns the decoded pixels that are written to its BEDTEX1 artifact.
                rgba = new byte[lease.Length]; lease.CopyTo(rgba);
            }
            else
            {
                var bytes = new byte[lease.Length]; lease.CopyTo(bytes);
                var image = BedTextureReader.Decode(bytes);
                width = image.Width; height = image.Height; rgba = image.Rgba;
            }
            var scale = Math.Min(1d, 64d / Math.Max(width, height));
            var columns = Math.Max(1, (int)Math.Round(width * scale));
            var rows = Math.Max(1, (int)Math.Round(height * scale));
            var samples = new byte[columns * rows * 4];
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < columns; x++)
                {
                    var sourceX = Math.Min(width - 1, (int)((x + .5) * width / columns));
                    var sourceY = Math.Min(height - 1, (int)((y + .5) * height / rows));
                    rgba.AsSpan((sourceY * width + sourceX) * 4, 4).CopyTo(samples.AsSpan((y * columns + x) * 4, 4));
                }
            return new(width, height, columns, rows, samples);
        }
        catch (InvalidDataException error) { why = error.Message; return null; }
    }

    internal void Draw(string description)
    {
        var maxSide = MathF.Min(128, MathF.Max(1, Im.GetContentRegionAvail().X));
        var scale = maxSide / Math.Max(width, height);
        var size = new float2(MathF.Max(1, width * scale), MathF.Max(1, height * scale));
        var at = Im.GetCursorScreenPos();
        Im.InvisibleButton("##asset-texture-thumbnail"u8, size);
        Ui.RecordCanvas(description);
        if (!Im.IsItemVisible()) return;
        var draw = Im.GetWindowDrawList();
        for (var y = 0f; y < size.Y; y += 8)
            for (var x = 0f; x < size.X; x += 8)
            {
                var shade = ((int)(x / 8) + (int)(y / 8)) % 2 == 0 ? (byte)72 : (byte)110;
                draw.AddRectFilled(at + new float2(x, y), at + new float2(MathF.Min(x + 8, size.X), MathF.Min(y + 8, size.Y)), new ImColor8(shade, shade, shade, 255));
            }
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns;)
            {
                var start = x;
                var pixel = samples.AsSpan((y * columns + x) * 4, 4);
                while (++x < columns && pixel.SequenceEqual(samples.AsSpan((y * columns + x) * 4, 4))) { }
                if (pixel[3] == 0) continue;
                draw.AddRectFilled(at + new float2(start * size.X / columns, y * size.Y / rows),
                    at + new float2(x * size.X / columns, (y + 1) * size.Y / rows), new ImColor8(pixel[0], pixel[1], pixel[2], pixel[3]));
            }
    }
}
