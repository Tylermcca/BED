using System.Buffers.Binary;
using System.Numerics;
namespace Bed.Runtime;

/// <summary>Producer-declared UV convention; never inferred from paths or spatial axes.</summary>
public enum BedMeshUv0Origin
{
    /// <summary>Top-left UVs, including glTF.</summary>
    TopLeft,
    /// <summary>Raw OBJ bottom-left UVs, unchanged by decoding.</summary>
    ObjBottomLeft
}
/// <summary>Owned decoded model-space geometry. No renderer, identity or material is inferred.</summary>
/// <param name="Positions">Finite local vertex positions.</param>
/// <param name="Indices">Validated triangle indices.</param>
public sealed record BedMeshGeometry(Vector3[] Positions,int[] Indices)
{
    /// <summary>Optional owned, vertex-aligned normals; null means absent.</summary>
    public Vector3[]? Normals {get;init;}
    /// <summary>Optional owned UV0; raw values and out-of-range coordinates are preserved.</summary>
    public Vector2[]? Uv0 {get;init;}
    /// <summary>Exact producer convention; decoding does not flip UVs.</summary>
    public BedMeshUv0Origin Uv0Origin {get;init;}
}
/// <summary>Opaque linear base colour and optional texture GUID; texture content is resolved from the same saved generation.</summary>
/// <param name="Ordinal">The producer's material slot within its model.</param>
/// <param name="BaseColor">Finite opaque linear RGBA, with components between zero and one.</param>
/// <param name="Texture">Optional exact texture asset GUID; read it through the model's <see cref="ProjectAssets"/> snapshot.</param>
public sealed record BedMeshMaterial(int Ordinal, Vector4 BaseColor, Guid? Texture);
/// <summary>One geometry/material part, in source order.</summary>
/// <param name="Geometry">Owned model-local streams and triangle indices for this part.</param>
/// <param name="Material">The decoded material; null for legacy BEDMESH1 geometry.</param>
public sealed record BedMeshPart(BedMeshGeometry Geometry, BedMeshMaterial? Material);
/// <summary>A decoded model with all material parts. Legacy geometry access requires exactly one part.</summary>
/// <param name="parts">Parts whose membership is copied; decoded geometry arrays remain caller-owned and mutable.</param>
public sealed class BedMesh(IReadOnlyList<BedMeshPart> parts)
{
    /// <summary>Owned decoded parts in source order; membership cannot be changed by the caller.</summary>
    public IReadOnlyList<BedMeshPart> Parts { get; } = Array.AsReadOnly(parts.ToArray());
    private BedMeshGeometry Single => Parts.Count == 1 ? Parts[0].Geometry : throw new InvalidOperationException("Use Parts to consume a multi-part model.");
    /// <summary>Single-part compatibility geometry; never silently drops other parts.</summary>
    public Vector3[] Positions => Single.Positions;
    /// <summary>Single-part compatibility triangle indices.</summary>
    public int[] Indices => Single.Indices;
    /// <summary>Single-part compatibility normal stream.</summary>
    public Vector3[]? Normals => Single.Normals;
    /// <summary>Single-part compatibility UV stream.</summary>
    public Vector2[]? Uv0 => Single.Uv0;
    /// <summary>Single-part compatibility UV convention.</summary>
    public BedMeshUv0Origin Uv0Origin => Single.Uv0Origin;
}
/// <summary>The single bounded BEDMESH1/2 decoder, shared by editor and standalone games.</summary>
public static class BedMeshReader
{
    /// <summary>Maximum validated vertex count before allocation.</summary>
    public const int MaxVertices=1_000_000;
    /// <summary>Maximum validated triangle-index count before allocation.</summary>
    public const int MaxIndices=3_000_000;
    /// <summary>Maximum complete artifact size.</summary>
    public const int MaxBytes=64*1024*1024;
    private const int HeaderBytes=24;
    /// <summary>Validates the complete BEDMESH1 or BEDMESH2 payload before allocating owned geometry.
    /// Rejects unknown flags/origin, nonfinite attributes, bad indices, truncation and trailing bytes.</summary>
    /// <param name="bytes">One complete derived artifact, including its eight-byte magic header.</param>
    /// <returns>One legacy part or every BEDMESH2 part in source order, with unmodified UV conventions.</returns>
    /// <remarks>Use Parts for multi-part models. Positions/Indices compatibility properties throw when more than one part exists.
    /// Decoding neither resolves textures nor imports source files; material texture GUIDs belong to the same published generation.</remarks>
    /// <exception cref="InvalidDataException">The header, version, flags, streams, material, indices or size are invalid.</exception>
    public static BedMesh Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || bytes.Length > MaxBytes) throw Bad("Invalid model header or size.");
        if (bytes[..8].SequenceEqual("BEDMESH1"u8)) return new([new(DecodeOne(bytes), null)]);
        if (bytes.Length < 12 || !bytes[..8].SequenceEqual("BEDMESH2"u8)) throw Bad("Invalid BEDMESH header.");
        var count = Read(bytes, 8);
        // Every part needs 16 geometry-header and 36 material bytes, even with no vertices.
        if (count == 0 || count > (bytes.Length - 12) / 52) throw Bad("Invalid model part count.");
        var descriptors = new List<(int Start, int Length, BedMeshMaterial? Material)>((int)count);
        int at = 12; long totalVertices = 0, totalIndices = 0;
        for (var part = 0; part < count; part++)
        {
            if (bytes.Length - at < 16) throw Bad("Truncated part header.");
            var flags = Read(bytes, at); var vertices = Read(bytes, at + 4); var indices = Read(bytes, at + 8); var origin = Read(bytes, at + 12);
            var length = Length(vertices, indices, flags, origin) - 8;
            totalVertices += vertices; totalIndices += indices;
            if (totalVertices > MaxVertices || totalIndices > MaxIndices || (long)at + length + 36 > bytes.Length) throw Bad("Model exceeds total geometry bounds or is truncated.");
            ValidateGeometry(bytes.Slice(at + 16, length - 16), flags, vertices, indices);
            var materialAt = at + length;
            var ordinal = Read(bytes, materialAt);
            var colour = new Vector4(Float(bytes, materialAt + 4), Float(bytes, materialAt + 8), Float(bytes, materialAt + 12), Float(bytes, materialAt + 16));
            var guid = new Guid(bytes.Slice(materialAt + 20, 16), bigEndian: true);
            BedMeshMaterial? material = null;
            if (ordinal == uint.MaxValue)
            { if (colour != Vector4.One || guid != Guid.Empty) throw Bad("Invalid absent material sentinel."); }
            else
            {
                if (ordinal > int.MaxValue || !Colour(colour) || guid != Guid.Empty && (flags & 2) == 0) throw Bad("Invalid opaque material or missing texture UV0.");
                material = new((int)ordinal, colour, guid == Guid.Empty ? null : guid);
            }
            descriptors.Add((at, length, material)); at = materialAt + 36;
        }
        if (at != bytes.Length) throw Bad("Trailing model bytes.");
        // No geometry arrays are allocated until every part, total and material has passed validation.
        var parts = new BedMeshPart[descriptors.Count];
        for (var i = 0; i < parts.Length; i++)
        { var d = descriptors[i]; parts[i] = new(ReadGeometry(bytes.Slice(d.Start, d.Length)), d.Material); }
        return new(parts);
    }
    private static BedMeshGeometry DecodeOne(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<HeaderBytes||bytes.Length>MaxBytes||!bytes[..8].SequenceEqual("BEDMESH1"u8))throw Bad("Invalid BEDMESH1 header or size.");
        uint flags=Read(bytes,8),vertices=Read(bytes,12),indices=Read(bytes,16),origin=Read(bytes,20);
        if(bytes.Length!=Length(vertices,indices,flags,origin))throw Bad("Truncated or trailing geometry bytes.");
        ValidateGeometry(bytes[HeaderBytes..], flags, vertices, indices);
        return ReadGeometry(bytes[8..]);
    }
    private static bool Colour(Vector4 c) => float.IsFinite(c.X) && float.IsFinite(c.Y) && float.IsFinite(c.Z)
        && c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1 && c.W == 1;
    private static void ValidateGeometry(ReadOnlySpan<byte> bytes, uint flags, uint vertices, uint indices)
    {
        int floatCount=checked((int)vertices*(3+((flags&1)!=0?3:0)+((flags&2)!=0?2:0)));
        int at=0;
        for(int i=0;i<floatCount;i++,at+=4)if(!float.IsFinite(BitConverter.UInt32BitsToSingle(Read(bytes,at))))throw Bad("Nonfinite attribute.");
        for(int i=0;i<(int)indices;i++,at+=4)if(Read(bytes,at)>=vertices)throw Bad("Index outside vertices.");
    }
    private static BedMeshGeometry ReadGeometry(ReadOnlySpan<byte> bytes)
    {
        var flags=Read(bytes,0);var vertices=Read(bytes,4);var indices=Read(bytes,8);var origin=Read(bytes,12);
        var p=new Vector3[(int)vertices];var n=(flags&1)!=0?new Vector3[(int)vertices]:null;var u=(flags&2)!=0?new Vector2[(int)vertices]:null;var t=new int[(int)indices];
        int at=16;
        for(int i=0;i<p.Length;i++)p[i]=Vector(bytes,ref at);
        if(n is not null)for(int i=0;i<n.Length;i++)n[i]=Vector(bytes,ref at);
        if(u is not null)for(int i=0;i<u.Length;i++){u[i]=new(Float(bytes,at),Float(bytes,at+4));at+=8;}
        for(int i=0;i<t.Length;i++,at+=4)t[i]=(int)Read(bytes,at);
        return new(p,t){Normals=n,Uv0=u,Uv0Origin=(BedMeshUv0Origin)origin};
    }
    private static int Length(uint vertices,uint indices,uint flags,uint origin)
    {
        if(vertices>MaxVertices||indices>MaxIndices||(flags&~3u)!=0||origin>1||indices%3!=0)throw Bad("Unsupported flags, origin or geometry counts.");
        long length=HeaderBytes+(long)vertices*(12+((flags&1)!=0?12:0)+((flags&2)!=0?8:0))+(long)indices*4;
        if(length>MaxBytes)throw Bad("Geometry exceeds byte limit.");
        return (int)length;
    }
    private static Vector3 Vector(ReadOnlySpan<byte> b,ref int at){var v=new Vector3(Float(b,at),Float(b,at+4),Float(b,at+8));at+=12;return v;}
    private static float Float(ReadOnlySpan<byte> b,int at)=>BitConverter.UInt32BitsToSingle(Read(b,at));
    private static uint Read(ReadOnlySpan<byte> b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at,4));
    private static InvalidDataException Bad(string reason)=>new(reason);
}
