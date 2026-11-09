using System.Numerics;
namespace Brutal.Authoring.Editor;

/// <summary>Opaque unlit linear base color and optional explicitly identified base-color texture.
/// This declaration grants no catalog admission, decoding or GPU ownership.</summary>
public sealed class SceneMaterial
{
    /// <summary>Validates an opaque linear color and either both texture identity fields or neither.</summary>
    public SceneMaterial(Vector4 baseColor,string? textureAssetGuid=null,string? textureSourceHash=null)
    {
        if(!Finite(baseColor.X)||!Finite(baseColor.Y)||!Finite(baseColor.Z)||baseColor.W!=1f)
            throw new ArgumentException("Material base color must be finite linear RGB in [0,1] with opaque alpha.",nameof(baseColor));
        if((textureAssetGuid is null)!=(textureSourceHash is null)||textureAssetGuid is not null&&(!Hex(textureAssetGuid,32)||!Hex(textureSourceHash,64)))
            throw new ArgumentException("A texture needs its lowercase asset GUID and source SHA-256 together.");
        BaseColor=baseColor;TextureAssetGuid=textureAssetGuid;TextureSourceHash=textureSourceHash;
    }
    /// <summary>Linear RGBA; alpha is one in this first opaque material path.</summary>
    public Vector4 BaseColor {get;}
    /// <summary>Explicit catalog identity, never inferred from a material name or path.</summary>
    public string? TextureAssetGuid {get;}
    /// <summary>Exact source hash associated with the producing asset record.</summary>
    public string? TextureSourceHash {get;}
    private static bool Finite(float x)=>float.IsFinite(x)&&x>=0&&x<=1;
    private static bool Hex(string? text,int n)=>text?.Length==n&&text.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
/// <summary>Owned immutable CPU geometry/material capture. No importer, native resource or renderer is invoked.</summary>
public sealed class MeshPayload
{
    /// <summary>Copies all arrays before validation. Normals and UV0 may be absent; otherwise they align with positions.
    /// A textured material requires UV0. Authored UV coordinates are preserved, never clamped or flipped here.</summary>
    public MeshPayload(IEnumerable<Vector3> positions,IEnumerable<Vector3> normals,IEnumerable<Vector2> uv0,IEnumerable<int> indices,SceneMaterial material)
    {
        ArgumentNullException.ThrowIfNull(positions);ArgumentNullException.ThrowIfNull(normals);ArgumentNullException.ThrowIfNull(uv0);ArgumentNullException.ThrowIfNull(indices);ArgumentNullException.ThrowIfNull(material);
        var p=positions.ToArray();var n=normals.ToArray();var u=uv0.ToArray();var t=indices.ToArray();
        if(p.Any(v=>!Finite(v))||n.Any(v=>!Finite(v))||u.Any(v=>!float.IsFinite(v.X)||!float.IsFinite(v.Y))
            ||n.Length!=0&&n.Length!=p.Length||u.Length!=0&&u.Length!=p.Length||t.Length%3!=0||t.Any(i=>i<0||i>=p.Length))
            throw new ArgumentException("Mesh payload needs finite, vertex-aligned attributes and complete in-range triangles.");
        if(material.TextureAssetGuid is not null&&(u.Length==0||u.Length!=p.Length))throw new ArgumentException("Textured material requires vertex-aligned UV0.");
        Positions=Array.AsReadOnly(p);Normals=Array.AsReadOnly(n);Uv0=Array.AsReadOnly(u);Indices=Array.AsReadOnly(t);Material=material;
    }
    /// <summary>Copied model-space positions.</summary>
    public IReadOnlyList<Vector3> Positions {get;}
    /// <summary>Copied normals, empty when absent; no lighting normals are fabricated.</summary>
    public IReadOnlyList<Vector3> Normals {get;}
    /// <summary>Copied top-left-origin UV0, empty when absent.</summary>
    public IReadOnlyList<Vector2> Uv0 {get;}
    /// <summary>Copied source-order triangle indices.</summary>
    public IReadOnlyList<int> Indices {get;}
    /// <summary>Immutable explicit material declaration.</summary>
    public SceneMaterial Material {get;}
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}
