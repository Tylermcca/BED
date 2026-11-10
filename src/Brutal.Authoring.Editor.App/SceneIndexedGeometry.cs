using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Brutal.Authoring.Editor;
namespace Brutal.Authoring.Editor.App;

/// <summary>Immutable model-space vertex/index upload. Its content key excludes camera and object transforms.</summary>
public sealed class SceneIndexedGeometry
{
    /// <summary>Position plus top-left UV; absent UV is zero and never enables a texture.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct Vertex(Vector3 Position,Vector2 Uv);
    private readonly byte[] vertices,indices;
    /// <summary>Packs the captured mesh without changing its CPU geometry or source-order indices.</summary>
    public SceneIndexedGeometry(MeshPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var v=new Vertex[payload.Positions.Count];for(var i=0;i<v.Length;i++)v[i]=new(payload.Positions[i],payload.Uv0.Count==0?Vector2.Zero:payload.Uv0[i]);
        vertices=MemoryMarshal.AsBytes(v.AsSpan()).ToArray();indices=MemoryMarshal.AsBytes(payload.Indices.ToArray().AsSpan()).ToArray();
        IndexCount=payload.Indices.Count;
        if((long)vertices.Length+indices.Length>64L*1024*1024)throw new InvalidOperationException("Mesh upload exceeds 64 MiB.");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(BitConverter.GetBytes(vertices.Length));hash.AppendData(vertices);hash.AppendData(indices);Key=Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    /// <summary>Exact vertex/index content identity, not an asset admission or policy grant.</summary>
    public string Key {get;}
    /// <summary>Source-order index count.</summary>
    public int IndexCount {get;}
    /// <summary>Total immutable upload bytes.</summary>
    public long ByteCount=>vertices.LongLength+indices.LongLength;
    internal ReadOnlySpan<byte> VertexBytes=>vertices;
    internal ReadOnlySpan<byte> IndexBytes=>indices;
    /// <summary>Owned vertex-buffer copy for diagnostics or tests.</summary>
    public byte[] CopyVertices()=>vertices.ToArray();
    /// <summary>Owned index-buffer copy for diagnostics or tests.</summary>
    public byte[] CopyIndices()=>indices.ToArray();
}
