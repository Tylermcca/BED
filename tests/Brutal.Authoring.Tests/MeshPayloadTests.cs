using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Import;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class MeshPayloadTests
{
    private static Vector3[] Positions()=>[Vector3.Zero,Vector3.UnitX,Vector3.UnitY];
    [Fact] public void Payload_copies_attributes_and_indices_without_normalizing_them()
    {
        var p=Positions();Vector3[] n=[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ];Vector2[] uv=[new(0,.2f),new(1,.4f),new(.2f,1)];int[] indices=[0,1,2];
        var material=new SceneMaterial(Vector4.One,new string('1',32),new string('a',64));var payload=new MeshPayload(p,n,uv,indices,material);
        p[0]=Vector3.One;n[0]=Vector3.Zero;uv[0]=Vector2.One;indices[0]=2;
        Assert.Equal(Vector3.Zero,payload.Positions[0]);Assert.Equal(Vector3.UnitZ,payload.Normals[0]);Assert.Equal(new Vector2(0,.2f),payload.Uv0[0]);Assert.Equal(0,payload.Indices[0]);Assert.Same(material,payload.Material);
        Assert.Throws<NotSupportedException>(()=>((IList<Vector2>)payload.Uv0)[0]=Vector2.Zero);
    }
    [Fact] public void Position_only_mesh_and_two_value_deconstruction_remain_compatible()
    {
        var mesh=new MeshData(Positions(),[0,1,2]);var (p,i)=mesh;Assert.Null(mesh.Uv0);Assert.Same(mesh.Positions,p);Assert.Same(mesh.Indices,i);
        var payload=new MeshPayload(p,[],[],i,new(Vector4.One));Assert.Empty(payload.Normals);Assert.Empty(payload.Uv0);
    }
    [Fact] public void Misaligned_nonfinite_or_textured_without_uv_attributes_refuse()
    {
        var material=new SceneMaterial(Vector4.One);Assert.Throws<ArgumentException>(()=>new MeshPayload(Positions(),[Vector3.UnitZ],[],[0,1,2],material));
        Assert.Throws<ArgumentException>(()=>new MeshPayload(Positions(),[],[new(float.NaN,0),Vector2.Zero,Vector2.One],[0,1,2],material));
        Assert.Throws<ArgumentException>(()=>new MeshPayload(Positions(),[],[],[0,1,3],material));
        Assert.Throws<ArgumentException>(()=>new MeshPayload(Positions(),[],[],[0,1,2],new(Vector4.One,new string('1',32),new string('a',64))));
    }
    [Fact] public void Material_requires_opaque_linear_color_and_complete_texture_identity()
    {
        Assert.Throws<ArgumentException>(()=>new SceneMaterial(new(1,1,1,.5f)));Assert.Throws<ArgumentException>(()=>new SceneMaterial(new(-1,0,0,1)));
        Assert.Throws<ArgumentException>(()=>new SceneMaterial(Vector4.One,new string('1',32)));Assert.Throws<ArgumentException>(()=>new SceneMaterial(Vector4.One,"name",new string('a',64)));
    }
    [Fact] public void Synthetic_obj_v_stays_raw_until_explicit_capture_and_is_flipped_once()
    {
        var file=Path.Combine(Path.GetTempPath(),"bed-uv-"+Guid.NewGuid().ToString("N")+".obj");
        try
        {
            File.WriteAllText(file,"v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0.2\nvt 1 0.4\nvt 0 1\nf 1/1 2/2 3/3\n");
            foreach(var frame in new[]{ProjectFrame.Gltf,ProjectFrame.YUpLeftHanded})
            {
                var mesh=Assert.Single(ObjImporter.Import(file,frame).Asset!.Meshes);Assert.Equal(new Vector2(0,.2f),mesh.Uv0[0]);Assert.Equal(new Vector2(1,.4f),mesh.Uv0[1]);
                // This caller knows it invoked ObjImporter; no basis/path/name heuristic establishes provenance.
                var data=new MeshData(mesh.Positions,mesh.Triangles)
                { Normals=mesh.Normals,Uv0=mesh.Uv0,Uv0Origin=MeshUv0Origin.ObjBottomLeft };
                var identity=new SceneRenderIdentity(1,2,3,4,5,Vector2.Zero,new(800,600),Vector2.One);
                var request=SceneRenderRequest.Capture(identity,Camera.Default,[new("object",Matrix4x4.Identity,data,0xffffffff)]);
                var payload=Assert.Single(request.Items).Payload;
                Assert.Equal(new Vector2(0,.8f),payload.Uv0[0]);Assert.Equal(new Vector2(1,.6f),payload.Uv0[1]);
                Assert.Equal(new Vector2(0,.2f),mesh.Uv0[0]);
                var copy=Assert.Single(request.CopyItems());Assert.Equal(MeshUv0Origin.TopLeft,copy.Mesh.Uv0Origin);
                var recaptured=SceneRenderRequest.Capture(identity,Camera.Default,[copy]);
                Assert.Equal(payload.Uv0,Assert.Single(recaptured.Items).Payload.Uv0);
            }
        }
        finally{File.Delete(file);}
    }
}
