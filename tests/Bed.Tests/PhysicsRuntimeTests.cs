using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Bed;
using PhysicsStarter;
using Xunit;
namespace Bed.Tests;

[Collection(PhysicsWorldCollection.Name)]
public sealed class PhysicsRuntimeTests
{
    private static Scene2Object Object(string id,Vector3 p,SceneBodyMotion motion,SceneShape shape,float friction=.5f,float mass=3,float scale=1)=>new(SceneObject.At(id,null,p) with{Scale=new(scale)},null){Body=new(motion,shape,friction,motion==SceneBodyMotion.Dynamic?mass:null)};
    private static Scene2 Scene(Vector3 gravity,params Scene2Object[] objects)=>new(SceneSpace.ThreeD,objects.ToImmutableArray(),[],[],null){Physics=new(gravity,.01f)};
    [Fact]
    public void Saved_gravity_step_mass_and_scaled_offset_survive_actual_collision_and_owned_cleanup()
    {
        var scene=Scene(new(0,-10,0),Object("floor",new(0,-.5f,0),SceneBodyMotion.Static,new SceneBoxShape(new(20,.5f,20))),Object("falling",new(0,4,0),SceneBodyMotion.Dynamic,new SceneSphereShape(.5f),scale:2));
        using(var world=new PhysicsWorld(scene)){Assert.Equal(.01f,world.StepSeconds);Assert.InRange(world.Mass("falling"),2.999f,3.001f);for(var i=0;i<200;i++)world.Tick();Assert.InRange(world.Pose("falling").Position.Y,.95f,1.05f);Assert.Equal(new Vector3(0,-.5f,0),world.Pose("floor").Position);Assert.Throws<InvalidOperationException>(()=>new PhysicsWorld(scene));}
        using var next=new PhysicsWorld(scene);Assert.Equal(2,next.Count);
    }
    [Fact]
    public void Saved_friction_changes_actual_sliding_and_kinematic_controller_pushes_a_dynamic_body()
    {
        var scene=Scene(new(0,-10,0),Object("floor",new(0,-.5f,0),SceneBodyMotion.Static,new SceneBoxShape(new(50,.5f,50)),1),Object("rough",new(-4,.5f,0),SceneBodyMotion.Dynamic,new SceneBoxShape(new(.5f)),.9f),Object("smooth",new(4,.5f,0),SceneBodyMotion.Dynamic,new SceneBoxShape(new(.5f)),0));
        using(var world=new PhysicsWorld(scene)){world.Velocity("rough",new(2,0,0),Vector3.Zero);world.Velocity("smooth",new(2,0,0),Vector3.Zero);for(var i=0;i<100;i++)world.Tick();var high=world.Pose("rough").Position.X+4;var low=world.Pose("smooth").Position.X-4;Assert.True(low>1.8f);Assert.True(high<low*.5f);}
        scene=Scene(Vector3.Zero,Object("pusher",new(-2,0,0),SceneBodyMotion.Kinematic,new SceneBoxShape(new(.5f))),Object("crate",Vector3.Zero,SceneBodyMotion.Dynamic,new SceneBoxShape(new(.5f))));
        using(var world=new PhysicsWorld(scene)){for(var i=1;i<=100;i++){world.MoveKinematic("pusher",new(-2+i*.02f,0,0),Quaternion.Identity);world.Tick();}world.Velocity("pusher",Vector3.Zero,Vector3.Zero);Assert.InRange(world.Pose("pusher").Position.X,-.001f,.001f);Assert.True(world.Pose("crate").Position.X>.5f);}
    }
    [Fact]
    public void XY_locks_zero_gravity_and_invalid_kinematic_targets_have_no_hidden_axis_workaround()
    {
        var scene=Scene(Vector3.Zero,Object("plane",Vector3.Zero,SceneBodyMotion.Dynamic,new SceneSphereShape(.5f)),Object("kinematic",new(5,0,0),SceneBodyMotion.Kinematic,new SceneSphereShape(.5f))) with{Space=SceneSpace.TwoDXY};
        using var world=new PhysicsWorld(scene);world.Velocity("plane",new(1,2,0),new(0,0,1));for(var i=0;i<50;i++)world.Tick();var pose=world.Pose("plane");Assert.InRange(pose.Position.X,.49f,.51f);Assert.InRange(pose.Position.Y,.99f,1.01f);Assert.Equal(0,pose.Position.Z);Assert.Equal(0,pose.Rotation.X);Assert.Equal(0,pose.Rotation.Y);Assert.Throws<ArgumentException>(()=>world.Velocity("plane",Vector3.UnitZ,Vector3.Zero));Assert.Throws<ArgumentException>(()=>world.MoveKinematic("kinematic",new(5,0,1),Quaternion.Identity));
    }
    [Theory][InlineData("box")][InlineData("sphere")][InlineData("capsule")]
    public void Every_primitive_density_route_retains_authored_mass_and_nonzero_center_origin(string kind)
    {
        SceneShape shape=kind switch{"box"=>new SceneBoxShape(new(1),new(2,.3f,-.2f)),"sphere"=>new SceneSphereShape(.5f,new(2,.3f,-.2f)),_=>new SceneCapsuleYShape(.5f,1,new(2,.3f,-.2f))};
        var scene=Scene(Vector3.Zero,Object("body",new(3,4,5),SceneBodyMotion.Dynamic,shape,mass:12));using var world=new PhysicsWorld(scene);Assert.InRange(world.Mass("body"),11.99f,12.01f);Assert.Equal(new Vector3(3,4,5),world.Pose("body").Position);world.Tick();Assert.Equal(new Vector3(3,4,5),world.Pose("body").Position);
    }
    [Fact]
    public void Late_native_density_refusal_unwinds_previously_created_bodies_and_global_factory()
    {
        var scene=Scene(Vector3.Zero,Object("first",Vector3.Zero,SceneBodyMotion.Static,new SceneSphereShape(1)),Object("huge_density",new(10,0,0),SceneBodyMotion.Dynamic,new SceneSphereShape(.00001f),mass:float.MaxValue));Assert.Throws<InvalidDataException>(()=>new PhysicsWorld(scene));using var next=new PhysicsWorld(Scene(Vector3.Zero,Object("one",Vector3.Zero,SceneBodyMotion.Dynamic,new SceneSphereShape(1))));Assert.Equal(1,next.Count);next.Tick();
    }
    [Fact]
    public void Capability_defaults_absent_and_false_are_compatible_and_true_is_strictly_explicit()
    {
        var settings=new Scene2BuildSettings("data/level.json",".bed/generated/scenes/level.json",[SceneSpace.ThreeD]);var old=Scene2BuildSettings.WriteProject("{}",settings);Assert.False(Scene2BuildSettings.ReadProject(old)!.SupportsPhysics);Assert.Null(JsonNode.Parse(old)!["sceneBuild"]!["physics"]);
        var enabled=Scene2BuildSettings.WriteProject(old,new(settings.Source,settings.Generated,settings.Spaces){SupportsPhysics=true});Assert.True(Scene2BuildSettings.ReadProject(enabled)!.SupportsPhysics);Assert.Equal(old,Scene2BuildSettings.WriteProject(enabled,settings));var json=JsonNode.Parse(enabled)!;json["sceneBuild"]!["physics"]="yes";Assert.Throws<DocumentFormatException>(()=>Scene2BuildSettings.ReadProject(json.ToJsonString()));
    }
    [Theory][InlineData("inertia")][InlineData("center")][InlineData("inverse")]
    public void Extreme_finite_authored_inputs_refuse_before_native_mass_arithmetic(string kind)
    {
        var shape=kind=="inertia"?new SceneSphereShape(1e10f):new SceneSphereShape(1,kind=="center"?new(1e30f,0,0):Vector3.Zero);var scene=Scene(Vector3.Zero,Object("large",Vector3.Zero,SceneBodyMotion.Dynamic,shape,mass:kind=="inverse"?1e38f:1e20f));Assert.Throws<InvalidDataException>(()=>new PhysicsWorld(scene));using var next=new PhysicsWorld(Scene(Vector3.Zero,Object("ok",Vector3.Zero,SceneBodyMotion.Dynamic,new SceneSphereShape(1))));Assert.Equal(1,next.Count);
    }

}
