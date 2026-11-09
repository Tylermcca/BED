using System.IO.Compression;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// What the BRUTAL License v1.0 asks of a program built with BRUTAL that's given to people: its copyright notice in the
/// credits (2.1), its logo shown, unaltered except for scaling, before the program's main function (2.2), and its
/// NOTICE.txt beside the program (2.3, copied by the build). The tools, the checkers game and anything a game builds on
/// this window host use it. BORROWED: the terms are Ahwoo Limited's; the logo and notice are theirs, from the BRUTAL
/// repository's <c>docs/branding</c> and <c>NOTICE.txt</c>.
/// </summary>
public static class BrutalCredit
{
    /// <summary>The credit line the licence requires, word for word.</summary>
    public const string Copyright = "BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.";

    /// <summary>BRUTAL's NOTICE.txt, as shipped beside the program.</summary>
    public static string Notice { get; } = new StreamReader(typeof(BrutalCredit).Assembly.GetManifestResourceStream("NOTICE.txt")!).ReadToEnd();

    private static (int Width, int Height, List<(int Y, int X0, int X1, uint Rgba)> Runs)? logo;

    /// <summary>The logo's size in pixels.</summary>
    public static (int Width, int Height) LogoSize => (Logo.Width, Logo.Height);

    // The white logo, decoded once into runs of identical pixels per row, so it draws exactly with no GPU texture: the
    // same in the window and the headless host.
    private static (int Width, int Height, List<(int Y, int X0, int X1, uint Rgba)> Runs) Logo => logo ??= Decode();

    /// <summary>Draws the logo with its top-left at <paramref name="at"/>, each logo pixel <paramref name="scale"/> screen
    /// pixels across, at <paramref name="alpha"/> opacity (for a fade).</summary>
    public static void DrawLogo(float2 at, float scale, float alpha = 1)
    {
        var draw = ImGui.GetWindowDrawList();
        foreach (var (y, x0, x1, rgba) in Logo.Runs)
        {
            var a = (uint)MathF.Round(((rgba >> 24) & 0xFF) * Math.Clamp(alpha, 0, 1));
            if (a == 0) continue;
            draw.AddRectFilled(at + new float2(x0 * scale, y * scale), at + new float2(x1 * scale, (y + 1) * scale), new ImColor8((byte)(rgba & 0xFF), (byte)((rgba >> 8) & 0xFF), (byte)((rgba >> 16) & 0xFF), (byte)a));
        }
    }

    // A PNG of 8-bit RGBA, not interlaced (what BRUTAL's logo files are), to rows of pixels and then to runs.
    private static (int, int, List<(int, int, int, uint)>) Decode()
    {
        using var stream = typeof(BrutalCredit).Assembly.GetManifestResourceStream("LOGO_BRUTAL_WHT.png")!;
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

/// <summary>
/// The BRUTAL logo before a program's main function (the licence's 2.2): on black, centred, legible, fading in and out
/// over 2.5 seconds, and skippable by a click or a key after the first second. Draw it each frame until <see cref="Done"/>.
/// </summary>
public sealed class BrutalSplash
{
    private double? started;

    /// <summary>How long it shows, in seconds.</summary>
    public const double Seconds = 2.5;

    /// <summary>How long before a click or key can skip it, in seconds.</summary>
    public const double SkippableAfter = 1;

    /// <summary>Whether it has finished (or was skipped).</summary>
    public bool Done { get; private set; }

    /// <summary>Draws a frame of the splash, filling the window. Returns false once it's done.</summary>
    public bool Draw()
    {
        if (Done) return false;
        var now = ImGui.GetTime();
        started ??= now;
        var t = now - started.Value;
        var skip = t >= SkippableAfter && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.Space));
        if (t >= Seconds || skip) { Done = true; return false; }
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new float4(0, 0, 0, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);   // all black, no frame
        if (Ui.Begin("###brutal.splash"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav))
        {
            var (w, h) = BrutalCredit.LogoSize;
            // Legible and prominent: half the window's width, never past its height, the whole logo scaled as one.
            var scale = MathF.Min(viewport.Size.X * 0.5f / w, viewport.Size.Y * 0.5f / h);
            var at = viewport.Pos + (viewport.Size - new float2(w * scale, h * scale)) / 2;
            var fade = (float)Math.Min(1, Math.Min(t / 0.4, (Seconds - t) / 0.4));
            BrutalCredit.DrawLogo(at, scale, fade);
        }
        Ui.End();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        return true;
    }

    /// <summary>Ends it now (for a headless picture of what comes after).</summary>
    public void Skip() => Done = true;
}
