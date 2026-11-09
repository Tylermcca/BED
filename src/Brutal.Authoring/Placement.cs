using System.Numerics;

namespace Brutal.Authoring;

/// <summary>
/// A grid to snap to (worksheet AT-112): its cell size and origin, and whether snapping is on. A setting of a document
/// or workspace (a 1.5 m tactical grid, a 0.25 m prop grid); positions stay exact numbers and are only snapped when an
/// edit asks, never rounded on save.
/// </summary>
/// <param name="Size">The cell size on each axis, in metres. An axis with size 0 doesn't snap.</param>
/// <param name="Origin">A point the grid passes through.</param>
/// <param name="Enabled">Whether snapping is on by default (a held key can override it for one drag).</param>
public sealed record Grid(Vector3 Size, Vector3 Origin, bool Enabled = true)
{
    /// <summary>No grid: nothing snaps.</summary>
    public static readonly Grid None = new(Vector3.Zero, Vector3.Zero, Enabled: false);

    /// <summary>A grid with the same cell size on every axis.</summary>
    public static Grid Uniform(float size, Vector3 origin = default, bool enabled = true) => new(new Vector3(size), origin, enabled);

    /// <summary>The grid point nearest <paramref name="p"/> when snapping applies, else <paramref name="p"/> unchanged.
    /// <paramref name="invert"/> is the held-key override: it snaps when off and frees when on.</summary>
    public Vector3 Snap(Vector3 p, bool invert = false)
    {
        if (Enabled == invert) return p;
        return new Vector3(Axis(p.X, Size.X, Origin.X), Axis(p.Y, Size.Y, Origin.Y), Axis(p.Z, Size.Z, Origin.Z));
    }

    private static float Axis(float v, float size, float origin) =>
        size <= 0 ? v : origin + MathF.Round((v - origin) / size, MidpointRounding.AwayFromZero) * size;

    /// <summary>True when <paramref name="p"/> sits on a grid point, within <paramref name="tolerance"/>.</summary>
    public bool OnGrid(Vector3 p, float tolerance = 1e-4f) => Vector3.Distance(new Grid(Size, Origin).Snap(p), p) <= tolerance;
}

/// <summary>Where an object's position sits relative to its shape (worksheet AT-115).</summary>
public enum OriginKind
{
    /// <summary>The position is the centre of the object's bounds.</summary>
    Centre,
    /// <summary>The position is the minimum corner of the bounds (for example the south-west floor corner).</summary>
    MinCorner,
    /// <summary>The position is the bottom centre: centred across, at the bottom of the up axis.</summary>
    BottomCentre,
    /// <summary>The position is the object's own pivot, a point in its local space.</summary>
    Pivot,
}

/// <summary>
/// An object type's placement convention: where its position sits, and which axis is up. A game's convention is honoured
/// exactly; a renderer's convenient centre never rewrites an authored position.
/// </summary>
/// <param name="Kind">Where the position sits.</param>
/// <param name="Up">The up axis, for <see cref="OriginKind.BottomCentre"/>: 1 for Y up, 2 for Z up.</param>
/// <param name="Pivot">For <see cref="OriginKind.Pivot"/>: the pivot in local space, measured from the bounds' minimum corner.</param>
public sealed record Origin(OriginKind Kind, int Up = 1, Vector3 Pivot = default)
{
    /// <summary>The offset from the local bounds' minimum corner to where the position sits.</summary>
    public Vector3 OffsetIn(Vector3 size) => Kind switch
    {
        OriginKind.Centre => size / 2,
        OriginKind.MinCorner => Vector3.Zero,
        OriginKind.BottomCentre => Up == 2 ? new Vector3(size.X / 2, size.Y / 2, 0) : new Vector3(size.X / 2, 0, size.Z / 2),
        OriginKind.Pivot => Pivot,
        _ => throw new InvalidOperationException($"unknown origin {Kind}"),
    };

    /// <summary>The bounds (minimum and maximum corners, unrotated) of an object of <paramref name="size"/> at <paramref name="position"/>.</summary>
    public (Vector3 Min, Vector3 Max) Bounds(Vector3 position, Vector3 size)
    {
        var min = position - OffsetIn(size);
        return (min, min + size);
    }

    /// <summary>The position that puts an object of <paramref name="size"/> with its minimum corner at <paramref name="min"/>.</summary>
    public Vector3 PositionFor(Vector3 min, Vector3 size) => min + OffsetIn(size);

    /// <summary>The same object's position under another convention: what a viewport or importer may compute for display,
    /// never what is saved.</summary>
    public Vector3 Convert(Vector3 position, Vector3 size, Origin to) => to.PositionFor(Bounds(position, size).Min, size);
}
