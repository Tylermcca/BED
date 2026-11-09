using System.Buffers.Binary;
using System.Numerics;

namespace Brutal.Authoring.Editor;

/// <summary>Bounded BEDMESH1 geometry encoding. Does not infer asset identity, material or UV origin.</summary>
public static class DerivedMeshCodec
{
    /// <summary>Maximum vertex count accepted before allocation.</summary>
    public const int MaxVertices=1_000_000;
    /// <summary>Maximum triangle-index count accepted before allocation.</summary>
    public const int MaxIndices=3_000_000;
    /// <summary>Maximum complete encoded size.</summary>
    public const int MaxBytes=64*1024*1024;
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

    /// <summary>Decodes owned arrays after validating size, flags, counts, provenance and every value.
    /// Null and empty optional arrays encode absence; decoded absence is null. Trailing bytes refuse.</summary>
    public static MeshData Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<HeaderBytes||bytes.Length>MaxBytes||!bytes[..8].SequenceEqual("BEDMESH1"u8))throw Bad("Invalid BEDMESH1 header or size.");
        uint flags=Read(bytes,8),vertices=Read(bytes,12),indices=Read(bytes,16),origin=Read(bytes,20);
        if(bytes.Length!=Length(vertices,indices,flags,origin))throw Bad("Truncated or trailing geometry bytes.");
        // Validate the entire payload before allocating geometry arrays.
        int floatCount=checked((int)vertices*(3+((flags&1)!=0?3:0)+((flags&2)!=0?2:0)));
        int at=HeaderBytes;
        for(int i=0;i<floatCount;i++,at+=4)if(!float.IsFinite(BitConverter.UInt32BitsToSingle(Read(bytes,at))))throw Bad("Nonfinite attribute.");
        for(int i=0;i<(int)indices;i++,at+=4)if(Read(bytes,at)>=vertices)throw Bad("Index outside vertices.");
        var p=new Vector3[(int)vertices];var n=(flags&1)!=0?new Vector3[(int)vertices]:null;var u=(flags&2)!=0?new Vector2[(int)vertices]:null;var t=new int[(int)indices];
        at=HeaderBytes;
        for(int i=0;i<p.Length;i++)p[i]=Vector(bytes,ref at);
        if(n is not null)for(int i=0;i<n.Length;i++)n[i]=Vector(bytes,ref at);
        if(u is not null)for(int i=0;i<u.Length;i++){u[i]=new(Float(bytes,at),Float(bytes,at+4));at+=8;}
        for(int i=0;i<t.Length;i++,at+=4)t[i]=(int)Read(bytes,at);
        return new(p,t){Normals=n,Uv0=u,Uv0Origin=(MeshUv0Origin)origin};
    }
    private static int Length(uint vertices,uint indices,uint flags,uint origin)
    {
        if(vertices>MaxVertices||indices>MaxIndices||(flags&~3u)!=0||origin>1||indices%3!=0)throw Bad("Unsupported flags, origin or geometry counts.");
        long length=HeaderBytes+(long)vertices*(12+((flags&1)!=0?12:0)+((flags&2)!=0?8:0))+(long)indices*4;
        if(length>MaxBytes)throw Bad("Geometry exceeds byte limit.");
        return (int)length;
    }
    private static void Check(Vector3 v){if(!float.IsFinite(v.X)||!float.IsFinite(v.Y)||!float.IsFinite(v.Z))throw Bad("Nonfinite attribute.");}
    private static Vector3 Vector(ReadOnlySpan<byte> b,ref int at){var v=new Vector3(Float(b,at),Float(b,at+4),Float(b,at+8));at+=12;return v;}
    private static float Float(ReadOnlySpan<byte> b,int at)=>BitConverter.UInt32BitsToSingle(Read(b,at));
    private static uint Read(ReadOnlySpan<byte> b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at,4));
    private static void Write(Span<byte> b,int at,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(at,4),v);
    private static InvalidDataException Bad(string reason)=>new(reason);
}
