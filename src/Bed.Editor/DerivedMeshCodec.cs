using System.Buffers.Binary;
using System.Numerics;

namespace Bed.Editor;

/// <summary>Bounded BEDMESH1 geometry encoding. Does not infer asset identity, material or UV origin.</summary>
public static class DerivedMeshCodec
{
    /// <summary>Maximum vertex count accepted before allocation.</summary>
    public const int MaxVertices=Bed.Runtime.BedMeshReader.MaxVertices;
    /// <summary>Maximum triangle-index count accepted before allocation.</summary>
    public const int MaxIndices=Bed.Runtime.BedMeshReader.MaxIndices;
    /// <summary>Maximum complete encoded size.</summary>
    public const int MaxBytes=Bed.Runtime.BedMeshReader.MaxBytes;
    // BEDMESH1 wire layout: magic[8], flags u32, vertices u32, indices u32, UV origin u32.
    // Attribute order: positions, optional normals, optional UV0, then indices. All little-endian.
    private const int HeaderBytes=24;

    /// <summary>Encodes finite aligned triangle geometry. Refuses material rather than silently dropping it.</summary>
    public static byte[] Encode(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if(mesh.Positions is null||mesh.Indices is null)throw Bad("Missing geometry arrays.");
        if(mesh.Material is not null)throw Bad("BEDMESH1 does not encode materials.");
        var p=mesh.Positions;var n=mesh.Normals;var u=mesh.Uv0;var t=mesh.Indices;
        uint flags=(n is {Length:>0}?1u:0u)|(u is {Length:>0}?2u:0u);
        int length=Length((uint)p.Length,(uint)t.Length,flags,(uint)mesh.Uv0Origin);
        if(n is {Length:>0}&&n.Length!=p.Length||u is {Length:>0}&&u.Length!=p.Length)throw Bad("Attributes must align with vertices.");
        foreach(var v in p)Check(v);
        if(n is not null)foreach(var v in n)Check(v);
        if(u is not null)foreach(var v in u)if(!float.IsFinite(v.X)||!float.IsFinite(v.Y))throw Bad("Nonfinite UV.");
        foreach(var i in t)if(i<0||i>=p.Length)throw Bad("Index outside vertices.");
        var bytes=new byte[length];"BEDMESH1"u8.CopyTo(bytes);
        Write(bytes,8,flags);Write(bytes,12,(uint)p.Length);Write(bytes,16,(uint)t.Length);Write(bytes,20,(uint)mesh.Uv0Origin);
        int at=HeaderBytes;
        void Float(float v){Write(bytes,at,BitConverter.SingleToUInt32Bits(v));at+=4;}
        foreach(var v in p){Float(v.X);Float(v.Y);Float(v.Z);}
        if(n is {Length:>0})foreach(var v in n){Float(v.X);Float(v.Y);Float(v.Z);}
        if(u is {Length:>0})foreach(var v in u){Float(v.X);Float(v.Y);}
        foreach(var i in t){Write(bytes,at,(uint)i);at+=4;}
        return bytes;
    }

    /// <summary>Encodes ordered model parts. Material-free models keep BEDMESH1; material models use BEDMESH2.
    /// GUIDs use RFC/network byte order; geometry scalars remain little-endian. Total old byte/count caps apply.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)] // Preserve legacy Encode(new(positions, indices)) source compatibility.
    public static byte[] Encode(IReadOnlyList<MeshPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0) throw Bad("A model needs at least one part.");
        bool hasMaterials = parts.Any(p => p?.Material is not null);
        long vertices = 0, indices = 0, length = 12;
        foreach (var part in parts)
        {
            if (part?.Geometry is not { } geometry || geometry.Material is not null) throw Bad("Use the part material field, not a hidden geometry material.");
            if (geometry.Positions is null || geometry.Indices is null) throw Bad("Missing geometry arrays.");
            vertices += geometry.Positions.Length; indices += geometry.Indices.Length;
            uint flags = (geometry.Normals is {Length: > 0} ? 1u : 0u) | (geometry.Uv0 is {Length: > 0} ? 2u : 0u);
            length += Length((uint)geometry.Positions.Length, (uint)geometry.Indices.Length, flags, (uint)geometry.Uv0Origin) - 8L + 36;
            if (vertices > MaxVertices || indices > MaxIndices || hasMaterials && length > MaxBytes) throw Bad("Model exceeds total geometry bounds.");
            if (part.Material is { } material)
            {
                try { _ = new SceneMaterial(material.BaseColor); } catch (ArgumentException) { throw Bad("Invalid opaque material factor."); }
                if (material.Ordinal < 0 || material.Texture == Guid.Empty || material.Texture is not null && geometry.Uv0 is not {Length: > 0})
                    throw Bad("Invalid material ordinal, texture GUID or missing texture UV0.");
            }
        }
        if (parts.All(p => p.Material is null)) return Encode(Flatten(parts));
        var bytes = new byte[(int)length]; "BEDMESH2"u8.CopyTo(bytes); Write(bytes, 8, (uint)parts.Count);
        int at = 12;
        foreach (var part in parts)
        {
            var geometry = Encode(part.Geometry); geometry.AsSpan(8).CopyTo(bytes.AsSpan(at)); at += geometry.Length - 8;
            var material = part.Material;
            Write(bytes, at, material is null ? uint.MaxValue : (uint)material.Ordinal);
            var colour = material?.BaseColor ?? Vector4.One;
            Write(bytes, at + 4, BitConverter.SingleToUInt32Bits(colour.X)); Write(bytes, at + 8, BitConverter.SingleToUInt32Bits(colour.Y));
            Write(bytes, at + 12, BitConverter.SingleToUInt32Bits(colour.Z)); Write(bytes, at + 16, BitConverter.SingleToUInt32Bits(colour.W));
            (material?.Texture ?? Guid.Empty).TryWriteBytes(bytes.AsSpan(at + 20, 16), bigEndian: true, out _); at += 36;
        }
        return bytes;
    }

    private static MeshData Flatten(IReadOnlyList<MeshPart> parts)
    {
        if (parts.Count == 1) return parts[0].Geometry;
        var first = parts[0].Geometry;
        bool normals = first.Normals is {Length: > 0}, uv = first.Uv0 is {Length: > 0};
        if (parts.Any(p => (p.Geometry.Normals is {Length: > 0}) != normals || (p.Geometry.Uv0 is {Length: > 0}) != uv || p.Geometry.Uv0Origin != first.Uv0Origin))
            throw Bad("Material-free parts need aligned optional attributes and one UV convention.");
        var positions = new List<Vector3>(); var ns = new List<Vector3>(); var us = new List<Vector2>(); var indices = new List<int>();
        foreach (var part in parts)
        {
            // Keep the same per-part validation as BEDMESH1 before combining arrays.
            _ = Encode(part.Geometry);
            var offset = positions.Count; positions.AddRange(part.Geometry.Positions); indices.AddRange(part.Geometry.Indices.Select(i => checked(i + offset)));
            if (normals) ns.AddRange(part.Geometry.Normals!); if (uv) us.AddRange(part.Geometry.Uv0!);
        }
        return new(positions.ToArray(), indices.ToArray()) { Normals = normals ? ns.ToArray() : null, Uv0 = uv ? us.ToArray() : null, Uv0Origin = first.Uv0Origin };
    }

    /// <summary>Decodes every ordered part through the sole runtime reader, retaining material ordinals and texture GUIDs.</summary>
    public static IReadOnlyList<MeshPart> DecodeParts(ReadOnlySpan<byte> bytes)
        => Array.AsReadOnly(Bed.Runtime.BedMeshReader.Decode(bytes).Parts.Select(p => new MeshPart(
            new(p.Geometry.Positions, p.Geometry.Indices) { Normals = p.Geometry.Normals, Uv0 = p.Geometry.Uv0, Uv0Origin = (MeshUv0Origin)p.Geometry.Uv0Origin },
            p.Material is { } m ? new(m.Ordinal, m.BaseColor, m.Texture) : null)).ToArray());

    /// <summary>Decodes owned arrays after validating size, flags, counts, provenance and every value.
    /// Null and empty optional arrays encode absence; decoded absence is null. Trailing bytes refuse.</summary>
    public static MeshData Decode(ReadOnlySpan<byte> bytes)
    {
        var parts = DecodeParts(bytes);
        if (parts.Count != 1 || parts[0].Material is not null) throw Bad("Use DecodeParts for a material-bearing or multi-part model.");
        return parts[0].Geometry;
    }
    private static int Length(uint vertices,uint indices,uint flags,uint origin)
    {
        if(vertices>MaxVertices||indices>MaxIndices||(flags&~3u)!=0||origin>1||indices%3!=0)throw Bad("Unsupported flags, origin or geometry counts.");
        long length=HeaderBytes+(long)vertices*(12+((flags&1)!=0?12:0)+((flags&2)!=0?8:0))+(long)indices*4;
        if(length>MaxBytes)throw Bad("Geometry exceeds byte limit.");
        return (int)length;
    }
    private static void Check(Vector3 v){if(!float.IsFinite(v.X)||!float.IsFinite(v.Y)||!float.IsFinite(v.Z))throw Bad("Nonfinite attribute.");}
    private static void Write(Span<byte> b,int at,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(at,4),v);
    private static InvalidDataException Bad(string reason)=>new(reason);
}
