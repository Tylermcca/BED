using Bed;
using Bed.Runtime;
using SceneStarter;
namespace PhysicsStarter;
internal static class PhysicsLiveValues
{
    internal static int Run(TextWriter output,TextWriter error)
    {
        var target=LiveValuesHost.Target??throw new InvalidOperationException("Select an object in BED before starting live values.");
        var saved=Scene2RuntimeInput.Open(Environment.GetEnvironmentVariable("BED_PROJECT")??"",true);
        using var toy=new CrateToy(saved.Level.Scene);var world=toy.World;
        if(!world.Named(SceneBodyMotion.Dynamic).Contains(target,StringComparer.Ordinal))throw new ArgumentException("Choose an island object that can move.");
        return LiveValuesHost.Run(target,()=>toy.Ticks,()=>new[]{world.Pose(target).Position.Y,world.LinearVelocity(target).Y,world.GravityY,world.Mass(target)},
            (field,value)=>{if(field=="gravityY")world.TryGravity(value);else if(field=="massKg")world.TryMass(target,value);else throw new ArgumentException("Unknown island value.");},
            (key,seconds)=>{if(key is not(null or "jump"))throw new ArgumentException("Use jump for the selected island body.");if(key=="jump")toy.Jump(target);for(var i=0;i<(int)Math.Ceiling(seconds/world.StepSeconds-1e-6);i++)toy.Step();},Console.In,output,error);
    }
}
