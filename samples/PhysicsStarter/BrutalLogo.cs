using System.IO.Compression;
namespace PhysicsStarter;
// The official embedded RGBA image; PNG run decoding is shared in approach with BED's existing credit drawing.
internal static class BrutalLogo
{
    internal static readonly (int Width,int Height,List<(int Y,int X0,int X1,uint Rgba)> Runs) Image = Decode();
    private static (int, int, List<(int, int, int, uint)>) Decode()
    {
        using var stream = typeof(BrutalLogo).Assembly.GetManifestResourceStream("LOGO_BRUTAL_WHT.png")!;
        using var file = new MemoryStream();
        stream.CopyTo(file);
        var png = file.ToArray();
        static int Int(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
        int width = 0, height = 0;
        using var data = new MemoryStream();
        for (var i = 8; i < png.Length;)
        {
            var length = Int(png, i);
            var type = System.Text.Encoding.ASCII.GetString(png, i + 4, 4);
            if (type == "IHDR")
            {
                (width, height) = (Int(png, i + 8), Int(png, i + 12));
                if (png[i + 16] != 8 || png[i + 17] != 6 || png[i + 20] != 0) throw new InvalidDataException("the logo must be 8-bit RGBA, not interlaced");
            }
            else if (type == "IDAT") data.Write(png, i + 8, length);
            i += 12 + length;
        }
        data.Position = 0;
        using var z = new ZLibStream(data, CompressionMode.Decompress);
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        z.ReadExactly(raw);
        var prior = new byte[stride];
        var row = new byte[stride];
        var runs = new List<(int, int, int, uint)>();
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, row, 0, stride);
            for (var x = 0; x < stride; x++)   // undo the row's PNG filter
            {
                int left = x >= 4 ? row[x - 4] : 0, up = prior[x], corner = x >= 4 ? prior[x - 4] : 0;
                row[x] = (byte)(row[x] + filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, corner),
                    _ => 0,
                });
            }
            for (var x = 0; x < width;)
            {
                var rgba = BitConverter.ToUInt32(row, x * 4);
                var end = x + 1;
                while (end < width && BitConverter.ToUInt32(row, end * 4) == rgba) end++;
                if ((rgba >> 24) != 0) runs.Add((y, x, end, rgba));
                x = end;
            }
            (prior, row) = (row, prior);
        }
        return (width, height, runs);

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
