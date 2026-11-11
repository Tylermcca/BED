using System.Numerics;
using Xunit;
namespace Bed.Tests;

public sealed class Scene2BodyTransformTests
{
    private static void Check(params SceneObject[] items)
        =>Scene2BodyTransforms.Check(items.ToDictionary(o=>o.Id),new HashSet<string>{"body"},(id,field)=>"/"+id+field);
    [Fact]
    public void Body_uses_the_complete_initial_world_basis_and_allows_positive_uniform_scale()
    {
        var parent=SceneObject.At("parent",null,new(3,4,5)) with{Scale=new(2),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.7f)};
        var body=SceneObject.At("body",null,new(1,0,0)) with{Parent="parent",Scale=new(.5f),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitX,.2f)};
        Check(parent,body);Check(body with{Parent=null,Scale=new(3)});
    }
    [Theory][InlineData(-1,1,1)][InlineData(-1,-1,1)][InlineData(0,1,1)][InlineData(1,2,1)][InlineData(0.000001f,0.000002f,0.000001f)]
    public void Signed_zero_nonuniform_and_tiny_nonuniform_scales_refuse(float x,float y,float z)
        =>Assert.Throws<DocumentFormatException>(()=>Check(SceneObject.At("body",null,Vector3.Zero) with{Scale=new(x,y,z)}));
    [Fact]
    public void Body_unit_rotation_is_explicit_even_when_a_zero_quaternion_would_make_an_identity_matrix()
        =>Assert.Throws<DocumentFormatException>(()=>Check(SceneObject.At("body",null,Vector3.Zero) with{Rotation=default}));
    [Fact]
    public void Body_ancestry_through_nonbody_markers_refuses()
    {
        var parent=SceneObject.At("ancestor",null,Vector3.Zero);var marker=SceneObject.At("marker",null,Vector3.Zero) with{Parent="ancestor"};var body=SceneObject.At("body",null,Vector3.Zero) with{Parent="marker"};
        Assert.Throws<DocumentFormatException>(()=>Scene2BodyTransforms.Check(new[]{parent,marker,body}.ToDictionary(o=>o.Id),new HashSet<string>{"ancestor","body"},(id,field)=>"/"+id+field));
    }
    [Fact]
    public void Nonbody_parent_shear_or_reflection_cannot_be_hidden_from_body_world_validation()
    {
        var parent=SceneObject.At("parent",null,Vector3.Zero) with{Scale=new(2,1,1)};var body=SceneObject.At("body",null,Vector3.Zero) with{Parent="parent",Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.7f)};
        Assert.Throws<DocumentFormatException>(()=>Check(parent,body));Assert.Throws<DocumentFormatException>(()=>Check(parent with{Scale=new(-1,1,1)},body with{Rotation=Quaternion.Identity}));
    }
}
