using System.Numerics;
namespace Bed.Runtime;

/// <summary>Pure full-image sprite geometry and stable painter order, shared by editors and games.</summary>
public static class SpriteGeometry
{
    /// <summary>Maps normalized top-left raster coordinates to local XY metres around a bottom-left normalized pivot.</summary>
    public static Vector3 LocalPoint(Vector2 size,Vector2 pivot,Vector2 raster)
    {
        if(!Finite(size)||size.X<=0||size.Y<=0||!Finite(pivot)||pivot.X<0||pivot.X>1||pivot.Y<0||pivot.Y>1||!Finite(raster))
            throw new ArgumentException("Sprite size must be positive finite and pivot within 0..1; raster coordinates must be finite.");
        var result=new Vector3((raster.X-pivot.X)*size.X,(1-raster.Y-pivot.Y)*size.Y,0);
        if(!Finite(result))throw new ArgumentException("Sprite coordinates overflow.");
        return result;
    }
    /// <summary>Returns owned world corners in top-left, top-right, bottom-right, bottom-left raster order. Mirroring preserves that UV order.</summary>
    public static IReadOnlyList<Vector3> Corners(Vector2 size,Vector2 pivot,Matrix4x4 world)
    {
        float[] elements=[world.M11,world.M12,world.M13,world.M14,world.M21,world.M22,world.M23,world.M24,world.M31,world.M32,world.M33,world.M34,world.M41,world.M42,world.M43,world.M44];
        if(elements.Any(v=>!float.IsFinite(v))||world.M14!=0||world.M24!=0||world.M34!=0||world.M44!=1)
            throw new ArgumentException("Sprite world transform must be finite and affine.");
        Vector2[] raster=[Vector2.Zero,Vector2.UnitX,Vector2.One,Vector2.UnitY];
        var result=raster.Select(p=>Vector3.Transform(LocalPoint(size,pivot,p),world)).ToArray();
        if(result.Any(p=>!Finite(p)))throw new ArgumentException("Sprite world corners must be finite.");
        return Array.AsReadOnly(result);
    }
    /// <summary>Returns input indices sorted by ascending sprite order, then input ordinal; pick by traversing the result backwards.
    /// The caller supplies sprites in effective document order. At most 50,000 entries are accepted.</summary>
    public static IReadOnlyList<int> DrawOrder(IReadOnlyList<int> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if(orders.Count>50000)throw new ArgumentException("Sprite order exceeds 50,000 objects.",nameof(orders));
        return Array.AsReadOnly(Enumerable.Range(0,orders.Count).OrderBy(i=>orders[i]).ThenBy(i=>i).ToArray());
    }
    private static bool Finite(Vector2 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y);
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}
