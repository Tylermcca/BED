using System.Collections.Immutable;
using System.Numerics;
using Bed;
using PhysicsStarter;
using Xunit;
namespace Bed.Tests;

/// <summary>Only one game-owned physics world may be active per process, so tests that make one share this collection.</summary>
[CollectionDefinition(Name)]
public sealed class PhysicsWorldCollection{internal const string Name="Physics world";}

/// <summary>The physics sample's interactive rules, without a window: input mapping, the orbit camera, dropping crates
/// and resetting to the saved level.</summary>
[Collection(PhysicsWorldCollection.Name)]
public sealed class PhysicsToyTests
{
    private static Scene2Object Body(string id,Vector3 p,SceneBodyMotion motion,SceneShape shape,string? asset=null,string? parent=null,float mass=1)=>
        new(SceneObject.At(id,asset,p) with{Parent=parent},null){Body=new(motion,shape,.6f,motion==SceneBodyMotion.Dynamic?mass:null)};
    // The sample's shape: a 8 x 8 m island, a kinematic paddle, a crate whose body sits on a child of an empty root, and scenery.
    private static Scene2 Island()=>new(SceneSpace.ThreeD,[
        Body("floor",new(0,-.25f,0),SceneBodyMotion.Static,new SceneBoxShape(new(4,.25f,4)),"f01"),
        Body("paddle",new(2.5f,.75f,0),SceneBodyMotion.Kinematic,new SceneBoxShape(new(1,.125f,.25f)),"d01"),
        new(SceneObject.At("tree","e01",new(-2.7f,0,2.4f)),null),
        new(SceneObject.At("crate",null,new(0,.6f,0)),null),
        Body("crate_box",Vector3.Zero,SceneBodyMotion.Dynamic,new SceneBoxShape(new(.5f)),"c01","crate"),
    ],[],[],null){Physics=new(new(0,-9.8f,0),.01f)};

    [Fact]
    public void A_left_click_drops_a_drag_does_not_and_keys_orbit_reset_and_quit()
    {
        var camera=new OrbitCamera();var controls=new ToyControls(camera);var yaw=camera.Yaw;
        controls.Cursor(400,300);controls.Button(ToyButton.Left,true);controls.Cursor(402,301);controls.Button(ToyButton.Left,false);
        var click=Assert.Single(controls.Update(0));Assert.Equal(ToyCommand.DropCrate,click.Command);Assert.Equal(new Vector2(402,301),click.At);
        controls.Button(ToyButton.Left,true);controls.Cursor(500,300);controls.Button(ToyButton.Left,false);Assert.Empty(controls.Update(0));
        Assert.Equal(yaw,camera.Yaw);
        controls.Button(ToyButton.Right,true);controls.Cursor(600,300);controls.Button(ToyButton.Right,false);Assert.NotEqual(yaw,camera.Yaw);Assert.Empty(controls.Update(0));
        yaw=camera.Yaw;controls.Key(ToyKey.A,true);controls.Update(.5f);controls.Key(ToyKey.A,false);controls.Update(.5f);
        Assert.Equal((yaw+.5f*ToyControls.KeyRadiansPerSecond)%MathF.Tau,camera.Yaw,4);
        yaw=camera.Yaw;controls.Key(ToyKey.D,true);controls.Update(.25f);controls.Key(ToyKey.D,false);Assert.Equal(yaw-.25f*ToyControls.KeyRadiansPerSecond,camera.Yaw,4);
        controls.Key(ToyKey.R,true);controls.Key(ToyKey.R,false);controls.Key(ToyKey.Escape,true);
        Assert.Equal([ToyCommand.Reset,ToyCommand.Quit],controls.Update(0).Select(c=>c.Command));
    }

    [Fact]
    public void The_wheel_zooms_within_limits_and_pitch_stays_above_the_ground()
    {
        var camera=new OrbitCamera();var controls=new ToyControls(camera);var start=camera.Distance;
        controls.Scroll(1);Assert.True(camera.Distance<start);
        for(var i=0;i<100;i++)controls.Scroll(1);Assert.Equal(OrbitCamera.MinDistance,camera.Distance);
        for(var i=0;i<100;i++)controls.Scroll(-1);Assert.Equal(OrbitCamera.MaxDistance,camera.Distance);
        controls.Button(ToyButton.Right,true);controls.Cursor(0,100000);Assert.Equal(OrbitCamera.MaxPitch,camera.Pitch);controls.Cursor(0,-100000);Assert.Equal(OrbitCamera.MinPitch,camera.Pitch);
    }

    [Fact]
    public void The_ray_through_the_view_centre_points_at_the_target()
    {
        var camera=new OrbitCamera();var (origin,direction)=camera.Ray(640,360,1280,720);
        Assert.True(Vector3.Distance(Vector3.Normalize(camera.Target-camera.Eye),direction)<1e-3f);Assert.True(Vector3.Distance(origin,camera.Eye)<.2f);
    }

    [Fact]
    public void Dropping_adds_a_falling_copy_of_the_prefab_crate_that_lands_on_the_island()
    {
        using var toy=new CrateToy(Island());
        Assert.Equal(3,toy.SavedBodies);Assert.Equal("crate_box",toy.Template!.Value.Id);Assert.Equal("c01",toy.Template.Value.Asset);
        Assert.Equal((new Vector2(-4,-4),new Vector2(4,4),0f),toy.Island);
        var before=toy.Drawables().Count();var name=toy.Drop(new(1,-1))!;
        Assert.Equal(4,toy.World.Count);Assert.Equal(before+1,toy.Drawables().Count());Assert.Contains(name,toy.World.Named(SceneBodyMotion.Dynamic));
        Assert.Equal(toy.DropHeight,toy.World.Pose(name).Position.Y);Assert.InRange(toy.World.Mass(name),.999f,1.001f);
        for(var i=0;i<300;i++)toy.Step();
        var rest=toy.World.Pose(name).Position;Assert.InRange(rest.Y,.4f,.6f);Assert.InRange(rest.X,.5f,1.5f);Assert.InRange(rest.Z,-1.5f,-.5f);
        // The crate model follows its body: the drawn crate's origin is at the body's pose.
        Assert.Contains(toy.Drawables(),d=>d.Asset=="c01"&&Vector3.Distance(d.World.Translation,rest)<1e-4f);
    }

    [Fact]
    public void Drop_points_use_the_island_hit_or_fall_back_to_its_centre_and_old_crates_make_room()
    {
        using var toy=new CrateToy(Island());
        Assert.Equal(new Vector2(1,2),toy.DropPoint(new(1,10,2),-Vector3.UnitY));
        Assert.Equal(Vector2.Zero,toy.DropPoint(new(30,10,0),-Vector3.UnitY));      // off the island
        Assert.Equal(Vector2.Zero,toy.DropPoint(new(1,10,2),Vector3.UnitY));        // looking at the sky
        for(var i=0;i<CrateToy.MaxDropped+3;i++)toy.Drop(new(i%5-2,i%3-1));
        Assert.Equal(CrateToy.MaxDropped,toy.Dropped.Count);Assert.Equal(3+CrateToy.MaxDropped,toy.World.Count);Assert.DoesNotContain("dropped_crate_1",toy.Dropped);
    }

    [Fact]
    public void Reset_returns_to_the_saved_level()
    {
        using var toy=new CrateToy(Island());var saved=toy.Drawables().Select(d=>d.World).ToArray();
        toy.Drop(new(0,2));toy.Drop(new(-2,0));for(var i=0;i<25;i++)toy.Step();
        Assert.NotEqual(saved,toy.Drawables().Take(saved.Length).Select(d=>d.World));
        toy.Reset();
        Assert.Equal(0,toy.Ticks);Assert.Empty(toy.Dropped);Assert.Equal(3,toy.World.Count);Assert.Equal(saved,toy.Drawables().Select(d=>d.World));
    }

    [Fact]
    public void A_long_pause_steps_at_most_eight_times_and_counts_the_dropped_time()
    {
        using var toy=new CrateToy(Island());Assert.Equal(8,toy.Advance(1));Assert.Equal(8,toy.Ticks);Assert.InRange(toy.DroppedSeconds,.91,.93);
    }
}
