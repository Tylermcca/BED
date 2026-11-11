using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

// Tree assertions deliberately distinguish submitted semantics and widget geometry from rendered text.
// FullyShown uses native full-label text layout evidence; it does not claim inter-window occlusion or glyph rasterization.
internal sealed class UiTreeAssertionsA448(UiSnapshot snapshot)
{
    public UiNode Find(UiRole role, string label) =>
        Assert.Single(snapshot.Nodes, n => n.Role == role && string.Equals(n.Label, label, StringComparison.Ordinal));

    public static void Enabled(UiNode node) => Assert.True(node.Enabled, node.DisabledReason ?? $"{node.Label} is disabled.");
    public static void Focused(UiNode node) => Assert.True(node.Focused, $"{node.Label} is not focused.");

    // Visibility here is geometric intersection with an explicit native clip, not inter-window occlusion.
    public static void Visible(UiNode node, UiBounds clip)
    {
        var b = node.Bounds;
        Assert.False(b.IsEmpty); Assert.False(clip.IsEmpty);
        Assert.True(b.X < clip.X + clip.Width && b.X + b.Width > clip.X
            && b.Y < clip.Y + clip.Height && b.Y + b.Height > clip.Y);
    }

    public static void FullyShown(UiNode node, string expected)
    {
        Assert.Equal(expected, node.Label);
        Assert.True(node.TextEvidence.HasValue, $"No native text evidence for '{expected}'.");
        var text = node.TextEvidence.Value;
        Assert.False(text.Truncated, $"Full text '{expected}' extends beyond its native clip: {text}.");
        Assert.False(text.FullBounds.IsEmpty);
        Assert.False(text.Clip.IsEmpty);
        var b = text.FullBounds; var c = text.Clip;
        Assert.True(b.X >= c.X && b.Y >= c.Y && b.X + b.Width <= c.X + c.Width && b.Y + b.Height <= c.Y + c.Height);
    }

    // The caller must supply the actual native clip for this item. A window's outer rect is NOT that clip.
    // This proves only widget containment; even a fully contained widget can clip or ellipsize its label.
    public static void InsideClip(UiNode node, UiBounds clip)
    {
        Assert.False(node.Bounds.IsEmpty, $"{node.Label} has no item rectangle.");
        Assert.False(clip.IsEmpty, "The supplied clip is empty.");
        var b = node.Bounds;
        Assert.True(b.X >= clip.X && b.Y >= clip.Y && b.X + b.Width <= clip.X + clip.Width
            && b.Y + b.Height <= clip.Y + clip.Height,
            $"Widget '{node.Label}' bounds {b} extend outside supplied clip {clip}.");
    }
}
