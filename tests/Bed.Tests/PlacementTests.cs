using System.Numerics;
using Xunit;

namespace Bed.Tests;

/// <summary>Grid and snapping (AT-112) and origins (AT-115), plain maths with no game in it.</summary>
public sealed class PlacementTests
{
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"expected {expected}, got {actual}");

    [Fact]
    public void SnapsToTheNearestPointOfAnOffsetGrid()
    {
        var grid = Grid.Uniform(1.5f, new Vector3(0.25f, 0, 0));
        Near(new(1.75f, 1.5f, 0), grid.Snap(new(2.2f, 1.1f, 0.4f)));
        Near(new(-1.25f, 0, 0), grid.Snap(new(-1.6f, 0.5f, -0.2f)));
        Assert.True(grid.OnGrid(new(1.75f, 3f, -1.5f)));
        Assert.False(grid.OnGrid(new(1.8f, 3f, 0)));
    }

    [Fact]
    public void AnAxisWithNoSizeDoesntSnap()
    {
        var floor = new Grid(new Vector3(1, 0, 1), Vector3.Zero);   // snap across the floor, not the height
        Near(new(2, 0.37f, -1), floor.Snap(new(2.4f, 0.37f, -0.6f)));
    }

    [Fact]
    public void TheHeldKeyInvertsSnappingAndOffMeansExact()
    {
        var on = Grid.Uniform(1);
        var p = new Vector3(0.4f, 0.6f, 2.2f);
        Assert.Equal(p, on.Snap(p, invert: true));
        Assert.Equal(p, (on with { Enabled = false }).Snap(p));
        Near(new(0, 1, 2), (on with { Enabled = false }).Snap(p, invert: true));
        Assert.Equal(p, Grid.None.Snap(p));
    }

    [Fact]
    public void AnOffGridPositionStaysExactUntilSnapped()
    {
        // Snapping only happens when an edit asks for it: a stored position is never rounded by having a grid.
        var exact = new Vector3(0.7f, 15f, 0f);
        var grid = Grid.Uniform(1.5f);
        Assert.False(grid.OnGrid(exact));
        var snapped = grid.Snap(exact);   // a new value; the stored one is untouched
        Near(new(0f, 15f, 0f), snapped);  // 0.7 is nearer 0 than 1.5
        Assert.Equal(new Vector3(0.7f, 15f, 0f), exact);
    }

    [Fact]
    public void OriginsGiveTheSameBoundsForTheSameObject()
    {
        var size = new Vector3(2, 1, 0.8f);
        var corner = new Origin(OriginKind.MinCorner);
        var centre = new Origin(OriginKind.Centre);
        var standing = new Origin(OriginKind.BottomCentre, Up: 2);

        var at = new Vector3(0.7f, 15, 0);   // a minimum-corner, z-up convention
        var (min, max) = corner.Bounds(at, size);
        Near(at, min);
        Near(new(2.7f, 16, 0.8f), max);

        var c = corner.Convert(at, size, centre);
        Near(new(1.7f, 15.5f, 0.4f), c);
        Near(min, centre.Bounds(c, size).Min);
        Near(new(1.7f, 15.5f, 0), corner.Convert(at, size, standing));
        Near(at, centre.Convert(c, size, corner));   // and back, exactly
    }

    [Fact]
    public void YUpBottomCentreAndCustomPivots()
    {
        var size = new Vector3(1, 2, 1);
        Near(new(0.5f, 0, 0.5f), new Origin(OriginKind.BottomCentre).OffsetIn(size));
        var hinge = new Origin(OriginKind.Pivot, Pivot: new Vector3(0, 0, 0.5f));
        Near(new(3, 0, 4), hinge.Bounds(new(3, 0, 4.5f), size).Min);
    }
}
