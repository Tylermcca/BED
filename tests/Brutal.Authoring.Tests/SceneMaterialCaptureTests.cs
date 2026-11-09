using System.Numerics;
using Brutal.Authoring.Editor;
using Xunit;
namespace Brutal.Authoring.Tests;

public sealed class SceneMaterialCaptureTests
{
    private static SceneRenderRequest Capture(MeshData mesh, uint colour = 0xff804020) =>
        SceneRenderRequest.Capture(new(1, 2, 3, 4, 5, Vector2.Zero, new(800, 600), Vector2.One),
            Camera.Default, [new("object", Matrix4x4.Identity, mesh, colour)]);
    private static MeshData Triangle() => new([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);

    [Fact]
    public void Capture_copies_all_attributes_and_cpu_copies_cannot_mutate_it()
    {
        var mesh = Triangle() with { Normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            Uv0 = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
            Material = new(new(.2f,.3f,.4f,1), new string('a',32), new string('b',64)) };
        var request = Capture(mesh); var item = Assert.Single(request.Items);
        mesh.Positions[0] = new(99); mesh.Normals![0] = Vector3.Zero;
        mesh.Uv0![0] = new(9); mesh.Indices[0] = 2;
        Assert.Equal(Vector3.Zero,item.Payload.Positions[0]);
        Assert.Equal(Vector3.UnitZ,item.Payload.Normals[0]);
        Assert.Equal(Vector2.Zero,item.Payload.Uv0[0]); Assert.Equal(0,item.Indices[0]);
        Assert.Equal(new Vector4(.2f,.3f,.4f,1),item.Payload.Material.BaseColor);
        Assert.Equal(new string('a',32),item.Payload.Material.TextureAssetGuid);
        Assert.Equal(new string('b',64),item.Payload.Material.TextureSourceHash);
        var copy = Assert.Single(request.CopyItems()).Mesh;
        copy.Normals![0] = Vector3.Zero; copy.Uv0![0] = new(7); copy.Positions[0] = new(8);
        Assert.Equal(Vector3.UnitZ,item.Payload.Normals[0]);
        Assert.Equal(Vector2.Zero,item.Payload.Uv0[0]); Assert.Equal(Vector3.Zero,item.Positions[0]);
        Assert.Same(item.Payload.Material,copy.Material);
    }
    [Fact]
    public void Position_only_capture_decodes_srgb_once_and_forces_opaque_material()
    {
        var request = Capture(Triangle(),0x00800aff); var item = Assert.Single(request.Items);
        var color = item.Payload.Material.BaseColor;
        Assert.Equal(1f,color.X); Assert.InRange(color.Y,.003035f,.003036f);
        Assert.InRange(color.Z,.215860f,.215861f); Assert.Equal(1f,color.W);
        Assert.Empty(item.Payload.Normals); Assert.Empty(item.Payload.Uv0);
        Assert.Null(item.Payload.Material.TextureAssetGuid);
        Assert.Equal(0x00800affu,item.Colour);
        Assert.Equal(0x00800affu,Assert.Single(request.Triangles()).Colour);
        var cpu = Assert.Single(request.CopyItems()); Assert.Equal(item.Colour,cpu.Colour);
        Assert.Equal(item.Positions,cpu.Mesh.Positions); Assert.Equal(item.Indices,cpu.Mesh.Indices);
    }
    [Fact]
    public void Capture_refuses_invalid_attributes_and_textures_without_uv()
    {
        Assert.Throws<ArgumentException>(()=>Capture(Triangle() with { Normals = [Vector3.UnitZ] }));
        Assert.Throws<ArgumentException>(()=>Capture(Triangle() with { Uv0 = [new(float.NaN,0),Vector2.Zero,Vector2.One] }));
        Assert.Throws<ArgumentException>(()=>Capture(Triangle() with {
            Material = new(Vector4.One,new string('a',32),new string('b',64)) }));
    }
    [Fact]
    public void Default_top_left_is_preserved_and_unknown_origin_refuses()
    {
        var uv=new[]{new Vector2(.1f,.2f),new Vector2(.3f,.4f),new Vector2(.5f,.6f)};
        var request=Capture(Triangle() with { Uv0=uv });
        Assert.Equal(uv,Assert.Single(request.Items).Payload.Uv0);
        Assert.Equal(MeshUv0Origin.TopLeft,Assert.Single(request.CopyItems()).Mesh.Uv0Origin);
        Assert.Throws<ArgumentException>(()=>Capture(Triangle() with { Uv0Origin=(MeshUv0Origin)99 }));
    }
    [Fact]
    public void Material_does_not_change_cpu_geometry_pick_or_triangle_projection()
    {
        var plain = Capture(MeshData.Cube);
        var explicitMaterial = Capture(MeshData.Cube with { Material = new(new(.9f,.1f,.2f,1)) });
        Assert.Equal(plain.Triangles(),explicitMaterial.Triangles());
        var a=plain.Pick(new(400,300));var b=explicitMaterial.Pick(new(400,300));
        Assert.Equal("object",a.ItemId);Assert.Equal(a.ItemId,b.ItemId);Assert.Equal(a.Distance,b.Distance);
    }
}
