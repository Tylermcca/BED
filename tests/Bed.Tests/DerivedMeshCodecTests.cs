using System.Buffers.Binary;
using System.Numerics;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;
public sealed class DerivedMeshCodecTests
{
    private static MeshData Triangle()=>new([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]);
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public void Roundtrip_copies_optional_attributes_and_preserves_raw_obj_uv(bool normals,bool uv)
    {
        var mesh=Triangle() with {Normals=normals?[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ]:null,
            Uv0=uv?[new(-2,.2f),new(1,.4f),new(3,2)]:null,Uv0Origin=MeshUv0Origin.ObjBottomLeft};
        var bytes=DerivedMeshCodec.Encode(mesh);var copy=DerivedMeshCodec.Decode(bytes);
        Assert.Equal(mesh.Positions,copy.Positions);Assert.Equal(mesh.Indices,copy.Indices);
        Assert.Equal(mesh.Normals,copy.Normals);Assert.Equal(mesh.Uv0,copy.Uv0);Assert.Equal(mesh.Uv0Origin,copy.Uv0Origin);
        Assert.Equal(bytes,DerivedMeshCodec.Encode(copy));
        mesh.Positions[0]=Vector3.One;Array.Fill(bytes,(byte)0);Assert.Equal(Vector3.Zero,copy.Positions[0]);
        Assert.NotSame(mesh.Indices,copy.Indices);
    }
    [Fact] public void Header_counts_floats_and_indices_are_explicit_little_endian()
    {
        var b=DerivedMeshCodec.Encode(Triangle());Assert.Equal("BEDMESH1"u8.ToArray(),b[..8]);
        Assert.Equal(new byte[]{0,0,0,0,3,0,0,0,3,0,0,0,0,0,0,0},b[8..24]);
        Assert.Equal(new byte[]{0,0,128,63},b[36..40]);Assert.Equal(new byte[]{0,0,0,0,1,0,0,0,2,0,0,0},b[^12..]);
    }
    [Fact] public void Every_truncation_and_trailing_data_refuses()
    {
        var b=DerivedMeshCodec.Encode(Triangle());for(int n=0;n<b.Length;n++)Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Decode(b.AsSpan(0,n)));
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Decode([..b,0]));b[0]^=1;Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Decode(b));
    }
    [Theory][InlineData(8,4u)][InlineData(12,uint.MaxValue)][InlineData(16,uint.MaxValue)][InlineData(20,2u)][InlineData(16,2u)]
    public void Invalid_counts_flags_or_provenance_refuse_before_payload_allocation(int offset,uint value)
    {
        var b=DerivedMeshCodec.Encode(Triangle());BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset,4),value);
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Decode(b));
    }
    [Fact] public void Nonfinite_attributes_and_out_of_range_indices_refuse()
    {
        var b=DerivedMeshCodec.Encode(Triangle());BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24,4),0x7fc00000);
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Decode(b));b=DerivedMeshCodec.Encode(Triangle());
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(b.Length-4),3);Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Decode(b));
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Encode(Triangle() with {Normals=[Vector3.UnitZ]}));
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Encode(Triangle() with {Uv0=[new(float.NaN,0),Vector2.Zero,Vector2.Zero]}));
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Encode(Triangle() with {Indices=[0,-1,2]}));
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Encode(Triangle() with {Positions=[new(float.PositiveInfinity,0,0),Vector3.Zero,Vector3.One]}));
    }
    [Fact] public void Material_is_not_silently_lost_and_empty_geometry_is_supported()
    {
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Encode(Triangle() with {Material=new(Vector4.One)}));
        Assert.Throws<InvalidDataException>(()=>DerivedMeshCodec.Encode(Triangle() with {Uv0Origin=(MeshUv0Origin)42}));
        var empty=DerivedMeshCodec.Decode(DerivedMeshCodec.Encode(new([],[])));Assert.Empty(empty.Positions);Assert.Empty(empty.Indices);Assert.Null(empty.Material);
    }
}
