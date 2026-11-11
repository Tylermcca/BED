using System.Numerics;

namespace Bed.Import;

/// <summary>
/// A project's coordinate frame, as a basis change from glTF's (right-handed, +Y up, +Z front, asset-right −X; worksheet
/// AT-021). Every project names its own; the importer converts once, at the boundary. A frame with a negative determinant
/// is a mirror: triangle winding flips once to stay outward.
/// </summary>
/// <param name="Name">A plain name, for reports.</param>
/// <param name="FromGltf">The basis change, in System.Numerics' row-vector convention (<c>v' = v · M</c>).</param>
public sealed record ProjectFrame(string Name, Matrix4x4 FromGltf)
{
    /// <summary>glTF's own frame: no conversion.</summary>
    public static readonly ProjectFrame Gltf = new("gltf", Matrix4x4.Identity);

    /// <summary>Y up, left-handed, +Z forward, +X right: <c>(x, y, z) → (−x, y, z)</c>, determinant −1.</summary>
    public static readonly ProjectFrame YUpLeftHanded = new("y_up_left_handed", new Matrix4x4(
        -1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1));

    /// <summary>Z up, right-handed, +Y forward, +X right: <c>(x, y, z) → (−x, z, y)</c>, determinant +1.</summary>
    public static readonly ProjectFrame ZUpRightHanded = new("z_up_right_handed", new Matrix4x4(
        -1, 0, 0, 0,
        0, 0, 1, 0,
        0, 1, 0, 0,
        0, 0, 0, 1));

    /// <summary>The basis change's determinant: −1 for a mirror, +1 otherwise.</summary>
    public float Determinant => FromGltf.GetDeterminant();

    /// <summary>A point in this frame.</summary>
    public Vector3 Point(Vector3 gltf) => Vector3.Transform(gltf, FromGltf);

    /// <summary>A direction in this frame.</summary>
    public Vector3 Direction(Vector3 gltf) => Vector3.TransformNormal(gltf, FromGltf);
}
