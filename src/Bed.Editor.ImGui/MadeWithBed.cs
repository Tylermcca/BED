using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// A "Made with BED" launch screen any game made with BED can show, usually straight after <see cref="BrutalSplash"/>:
/// the same timing, fade and skip (a click or a key after the first second), on a white page that comes up out of
/// black and goes back to it, with BED's mark, "MADE WITH"
/// over "BED" in white on a rounded blue box. The mark is drawn in code from strokes and rounded shapes, with no image or
/// font files, so it looks the same in every game, whatever fonts the game loads, and in the headless host. Showing it is
/// the game's choice; nothing requires it. Draw it each frame until <see cref="Done"/>. INVENTED for this library.
/// </summary>
public sealed class MadeWithBedSplash
{
    private double? started;

    /// <summary>How long it shows, in seconds: as long as the BRUTAL logo.</summary>
    public const double Seconds = BrutalSplash.Seconds;

    /// <summary>How long before a click or key can skip it, in seconds: as for the BRUTAL logo.</summary>
    public const double SkippableAfter = BrutalSplash.SkippableAfter;

    /// <summary>Whether it has finished (or was skipped).</summary>
    public bool Done { get; private set; }

    /// <summary>Where the mark sits on this frame, for a game drawing with it: the mark's blue box (all its words are inside it),
    /// the big word's box (the bold letters alone), how long the screen has shown in seconds, and its fade (0 to 1), all in
    /// window pixels.</summary>
    public readonly record struct Place(float2 MarkMin, float2 MarkMax, float2 WordMin, float2 WordMax, float Seconds, float Fade);

    /// <summary>Optional: drawn on the white page before the mark each frame, so whatever it draws sits behind the box (the box
    /// always draws over it). Draw into <c>ImGui.GetWindowDrawList()</c>. A game's own touch; nothing requires it.</summary>
    public Action<Place>? Behind { get; init; }

    /// <summary>Optional: drawn after the mark each frame, over it. Keep it off the mark's words.</summary>
    public Action<Place>? InFront { get; init; }

    /// <summary>Draws a frame of the screen, filling the window. Returns false once it's done.</summary>
    public bool Draw()
    {
        if (Done) return false;
        if (BrutalSplash.Page(ref started, "###bed.madewith"u8, (pos, size, fade) =>
        {
            fade = Math.Clamp(fade, 0, 1);
            if (Behind is not null || InFront is not null)
            {
                var (markBox, wordBox) = MadeWithBedMark.Placed(pos, size);
                var place = new Place(markBox.Min, markBox.Max, wordBox.Min, wordBox.Max, (float)(ImGui.GetTime() - (started ?? 0)), fade);
                Behind?.Invoke(place);
                MadeWithBedMark.Draw(pos, size);
                InFront?.Invoke(place);
            }
            else MadeWithBedMark.Draw(pos, size);
            // The whole screen, and anything a game drew on it, comes up out of black and goes back to it: one veil over all of it,
            // so nothing fades on its own and overlapping strokes never show their seams.
            if (fade < 1)
                ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size, Veil(fade));
        }, new float4(1, 1, 1, 1))) return true;
        Done = true;
        return false;
    }

    // The veil over the screen as it fades: black, so it comes up out of the BRUTAL logo's black page into white with no flash,
    // and goes back down to black as that did.
    private static ImColor8 Veil(float fade) => new(0, 0, 0, (byte)MathF.Round((1 - fade) * 255));

    /// <summary>Ends it now (for a headless picture of what comes after).</summary>
    public void Skip() => Done = true;
}

/// <summary>BED's "Made with BED" mark, built from strokes in letter-height units and drawn to fit a window.</summary>
internal static class MadeWithBedMark
{
    private const uint White = 0xFFFFFF;
    private const uint Pale = 0xCFE3F6;       // "MADE WITH": a pale blue on the box, quieter than the white word
    private const uint DeepBlue = 0x1F6FB2;   // the editor's accent in its light theme: white letters read on it

    private abstract record Shape(uint Rgb);
    private sealed record Stroke(float2[] Points, float Width, uint Rgb) : Shape(Rgb);
    private sealed record Box(float2 Min, float2 Max, float Round, uint Rgb) : Shape(Rgb);

    /// <summary>Draws the mark centred in the window at <paramref name="at"/> of <paramref name="size"/>, as big as the
    /// BRUTAL logo (half the width, at most half the height).</summary>
    internal static void Draw(float2 at, float2 size)
    {
        var shapes = Build(out _, out _);
        var (origin, scale) = Fit(shapes, at, size);
        var draw = ImGui.GetWindowDrawList();
        foreach (var shape in shapes)
        {
            var c = shape.Rgb;
            var colour = new ImColor8((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF), 255);
            switch (shape)
            {
                case Box b:
                    draw.AddRectFilled(origin + b.Min * scale, origin + b.Max * scale, colour, b.Round * scale);
                    break;
                case Stroke s:
                    var width = s.Width * scale;
                    for (var i = 0; i < s.Points.Length; i++)
                    {
                        var p = origin + s.Points[i] * scale;
                        draw.AddCircleFilled(p, width / 2, colour, 0);   // round ends and joins
                        if (i > 0) draw.AddLine(origin + s.Points[i - 1] * scale, p, colour, width);
                    }
                    break;
            }
        }
    }

    // Where the mark's letter-height units land in the window: centred, as big as the BRUTAL logo.
    private static (float2 Origin, float Scale) Fit(List<Shape> shapes, float2 at, float2 size)
    {
        var (min, max) = Bounds(shapes);
        var extent = max - min;
        var scale = MathF.Min(size.X * 0.5f / extent.X, size.Y * 0.5f / extent.Y);
        return (at + (size - extent * scale) / 2 - min * scale, scale);
    }

    /// <summary>The boxes the whole mark (its blue box) and its big word take in the window, as <see cref="Draw"/> places them.</summary>
    internal static ((float2 Min, float2 Max) Mark, (float2 Min, float2 Max) Word) Placed(float2 at, float2 size)
    {
        var shapes = Build(out var wordMin, out var wordMax);
        var (origin, scale) = Fit(shapes, at, size);
        var (min, max) = Bounds(shapes);
        return ((origin + min * scale, origin + max * scale), (origin + wordMin * scale, origin + wordMax * scale));
    }

    // The mark in letter-height units: "MADE WITH" in small open capitals over "BED" in bold rounded strokes, both on a
    // rounded blue box.
    private static List<Shape> Build(out float2 wordMin, out float2 wordMax)
    {
        var shapes = new List<Shape>();
        const float bold = 0.25f;
        float x = 0;
        foreach (var letter in "BED")
        {
            var (strokes, width) = BoldLetter(letter);
            foreach (var s in strokes) shapes.Add(new Stroke(Shift(s, x, 0), bold, White));
            x += width + bold + 0.17f;
        }
        var end = x - bold - 0.17f;
        var wordLeft = -bold / 2;
        var wordRight = end + bold / 2;
        (wordMin, wordMax) = (new(wordLeft, -bold / 2), new(wordRight, 1 + bold / 2));

        // "MADE WITH", small, spaced out, centred over the word.
        const float small = 0.3f, thin = 0.055f, track = 0.55f, gap = 0.3f;
        var text = new List<(float2[] Points, float X)>();
        float tx = 0;
        foreach (var letter in "MADE WITH")
        {
            var (strokes, width) = SmallLetter(letter);
            foreach (var s in strokes) text.Add((s, tx));
            tx += width + (letter == ' ' ? 0 : track);
        }
        tx -= track;
        var middle = (wordLeft + wordRight) / 2;
        var top = -bold / 2 - gap - small;
        var lx = middle - tx * small / 2;
        foreach (var (points, ox) in text)
            shapes.Add(new Stroke(points.Select(p => new float2(lx + (ox + p.X) * small, top + p.Y * small)).ToArray(), thin, Pale));

        // The box round both, with the same margin all round.
        const float pad = 0.34f;
        var half = MathF.Max(wordRight - wordLeft, tx * small + thin) / 2 + pad;
        shapes.Insert(0, new Box(new(middle - half, top - thin / 2 - pad), new(middle + half, 1 + bold / 2 + pad), 0.26f, DeepBlue));
        return shapes;
    }

    private static (float2 Min, float2 Max) Bounds(List<Shape> shapes)
    {
        float2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        void Take(float2 a, float2 b) { min = new(MathF.Min(min.X, a.X), MathF.Min(min.Y, a.Y)); max = new(MathF.Max(max.X, b.X), MathF.Max(max.Y, b.Y)); }
        foreach (var shape in shapes)
            if (shape is Box b) Take(b.Min, b.Max);
            else if (shape is Stroke s) foreach (var p in s.Points) Take(p - new float2(s.Width / 2, s.Width / 2), p + new float2(s.Width / 2, s.Width / 2));
        return (min, max);
    }

    private static float2[] Shift(float2[] points, float x, float y) => points.Select(p => new float2(p.X + x, p.Y + y)).ToArray();

    // Points round an arc about (cx, cy), from one angle to another in degrees (screen angles: 90 points down).
    private static IEnumerable<float2> Arc(float cx, float cy, float r, float from, float to, int steps = 24)
    {
        for (var i = 0; i <= steps; i++)
        {
            var a = (from + (to - from) * i / steps) * MathF.PI / 180;
            yield return new(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }
    }

    private static float2[] Path(params IEnumerable<float2>[] parts) => parts.SelectMany(p => p).ToArray();

    private static float2 P(float x, float y) => new(x, y);

    // B, E and D for the word: wide, round bowls; letters one unit high, drawn on the stroke's centre line.
    private static (float2[][] Strokes, float Width) BoldLetter(char letter) => letter switch
    {
        'B' => ([
            Path([P(0.36f, 0.47f), P(0, 0.47f), P(0, 0), P(0.34f, 0)], Arc(0.34f, 0.235f, 0.235f, -90, 90)),
            Path([P(0, 0.47f), P(0, 1), P(0.38f, 1)], Arc(0.38f, 0.735f, 0.265f, 90, -90), [P(0.36f, 0.47f)]),
        ], 0.645f),
        'E' => ([[P(0.55f, 0), P(0, 0), P(0, 1), P(0.55f, 1)], [P(0, 0.5f), P(0.45f, 0.5f)]], 0.55f),
        'D' => ([Path([P(0, 0), P(0, 1), P(0.22f, 1)], Arc(0.22f, 0.5f, 0.5f, 90, -90, 36), [P(0, 0)])], 0.72f),
        _ => throw new ArgumentOutOfRangeException(nameof(letter)),
    };

    // The few capitals "MADE WITH" needs, in thin open strokes.
    private static (float2[][] Strokes, float Width) SmallLetter(char letter) => letter switch
    {
        'M' => ([[P(0, 1), P(0, 0), P(0.4f, 0.62f), P(0.8f, 0), P(0.8f, 1)]], 0.8f),
        'A' => ([[P(0, 1), P(0.36f, 0), P(0.72f, 1)], [P(0.14f, 0.62f), P(0.58f, 0.62f)]], 0.72f),
        'D' => ([Path([P(0, 0), P(0, 1), P(0.2f, 1)], Arc(0.2f, 0.5f, 0.5f, 90, -90), [P(0, 0)])], 0.7f),
        'E' => ([[P(0.55f, 0), P(0, 0), P(0, 1), P(0.55f, 1)], [P(0, 0.5f), P(0.45f, 0.5f)]], 0.55f),
        'W' => ([[P(0, 0), P(0.22f, 1), P(0.48f, 0.38f), P(0.74f, 1), P(0.96f, 0)]], 0.96f),
        'I' => ([[P(0, 0), P(0, 1)]], 0),
        'T' => ([[P(0, 0), P(0.64f, 0)], [P(0.32f, 0), P(0.32f, 1)]], 0.64f),
        'H' => ([[P(0, 0), P(0, 1)], [P(0.62f, 0), P(0.62f, 1)], [P(0, 0.5f), P(0.62f, 0.5f)]], 0.62f),
        ' ' => ([], 0.6f),
        _ => throw new ArgumentOutOfRangeException(nameof(letter)),
    };
}
