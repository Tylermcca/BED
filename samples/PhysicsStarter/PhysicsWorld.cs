using System.Numerics;
using Bed;
using Brutal.JoltApi;
using Brutal.Numerics;
namespace PhysicsStarter;

// One game/owner thread. No editor callbacks, authoring writes or borrowed handles escape.
internal sealed class PhysicsWorld:IDisposable
{
    private static readonly object Global=new();
    private static bool active;
    private readonly int thread=Environment.CurrentManagedThreadId;
    private bool held,factory,types,hasTemp,hasJobs,hasBroad,hasPairs,hasFilter,hasSystem,disposed;
    private TempAllocatorImpl temp;private JobSystemThreadPool jobs;private BroadPhaseLayerInterfaceTable broad;
    private ObjectLayerPairFilterTable pairs;private ObjectVsBroadPhaseLayerFilterTable filter;private PhysicsSystem system;private BodyInterface bodies;
    private readonly Dictionary<string,(uint Id,SceneBodyMotion Motion)> owned=new(StringComparer.Ordinal);
    private readonly SceneSpace space;
    internal float StepSeconds{get;}
    internal int Count=>owned.Count;
    private readonly Dictionary<string,(Vector3 Position,Quaternion Rotation)> saved=new(StringComparer.Ordinal);
    /// <summary>Body names with this motion, in saved order.</summary>
    internal IReadOnlyList<string> Named(SceneBodyMotion motion)=>owned.Where(p=>p.Value.Motion==motion).Select(p=>p.Key).ToArray();
    /// <summary>The saved world pose a body started from.</summary>
    internal (Vector3 Position,Quaternion Rotation) Saved(string name)=>saved[name];
    internal PhysicsWorld(Scene2 scene)
    {
        scene=new Scene2Format().Read(new Scene2Format().Write(scene));
        if(scene.Bases.Length!=0||scene.Instances.Length!=0||scene.Physics is null||scene.Objects.Any(o=>o.Body?.Shape is SceneMeshBoundsShape))throw new InvalidDataException("Load a prepared primitive-only physics scene.");
        space=scene.Space;StepSeconds=scene.Physics.FixedStepSeconds;
        lock(Global){if(active)throw new InvalidOperationException("Another game-owned physics world is active.");active=true;held=true;}
        try
        {
            Jolt.RegisterDefaultAllocator();SceneStarter.PhysicsRuntimeInventory.CheckLoadedNative();Factory.Instance=Factory.Create();factory=true;Jolt.RegisterTypes();types=true;
            temp=TempAllocatorImpl.Create(16*1024*1024);hasTemp=true;jobs=JobSystemThreadPool.Create(2048,8,1);hasJobs=true;
            broad=BroadPhaseLayerInterfaceTable.Create(2,2);hasBroad=true;broad.MapObjectToBroadPhaseLayer(0,0);broad.MapObjectToBroadPhaseLayer(1,1);
            pairs=ObjectLayerPairFilterTable.Create(2);hasPairs=true;pairs.EnableCollision(0,1);pairs.EnableCollision(1,1);
            filter=ObjectVsBroadPhaseLayerFilterTable.Create(broad,2,pairs,2);hasFilter=true;system=PhysicsSystem.Create();hasSystem=true;system.Init(4096,0,16384,16384,broad,filter,pairs);
            system.SetGravity(F(scene.Physics.Gravity));bodies=system.GetBodyInterface();
            var objects=scene.Objects.ToDictionary(o=>o.Object.Id,o=>o.Object,StringComparer.Ordinal);var worlds=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
            Matrix4x4 World(string id)
            {
                var chain=new Stack<SceneObject>();string? at=id;while(at is not null&&!worlds.ContainsKey(at)){var item=objects[at];chain.Push(item);at=item.Parent;}
                var world=at is null?Matrix4x4.Identity:worlds[at];while(chain.TryPop(out var item)){world=Matrix4x4.CreateScale(item.Scale)*Matrix4x4.CreateFromQuaternion(item.Rotation)*Matrix4x4.CreateTranslation(item.Position)*world;worlds[item.Id]=world;}return worlds[id];
            }
            foreach(var item in scene.Objects.Where(o=>o.Body is not null))
            {
                if(!Matrix4x4.Decompose(World(item.Object.Id),out var scale,out var rotation,out var position))throw new InvalidDataException("Cannot decompose body world pose.");
                saved.Add(item.Object.Id,(position,rotation));Add(item.Object.Id,item.Body!,scale.X,position,rotation);
            }
        }
        catch{try{Dispose();}catch{/* Every resource is attempted by Dispose; preserve the creation refusal. */}throw;}
    }
    private static float3 F(Vector3 v)=>new(v.X,v.Y,v.Z);
    private static floatQuat Q(Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
    private void Check(){if(disposed)throw new ObjectDisposedException(nameof(PhysicsWorld));if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Physics belongs to its game thread.");}
    private void Add(string name,SceneBody body,float scale,Vector3 position,Quaternion rotation)
    {
        var shape=body.Shape;var center=shape switch{SceneBoxShape b=>b.Center,SceneSphereShape b=>b.Center,SceneCapsuleYShape b=>b.Center,_=>throw new InvalidDataException("Prepare primitive bodies first.")};
        var dimensions=shape switch{SceneBoxShape b=>b.HalfExtents,SceneSphereShape b=>new(b.Radius),SceneCapsuleYShape b=>new(b.Radius,b.HalfHeight,b.Radius),_=>Vector3.Zero};dimensions*=scale;center*=scale;
        if(!Finite(dimensions)||!Finite(center)||dimensions.X<=0||dimensions.Y<=0||dimensions.Z<=0)throw new InvalidDataException("Scaled shape exceeds native finite dimensions.");
        double x=dimensions.X,y=dimensions.Y,z=dimensions.Z;var volume=shape switch{SceneBoxShape=>8*x*y*z,SceneSphereShape=>4*Math.PI*x*x*x/3,SceneCapsuleYShape=>Math.PI*x*x*(2*y)+4*Math.PI*x*x*x/3,_=>0};
        var density=body.Motion==SceneBodyMotion.Dynamic?(float)(body.MassKg!.Value/volume):1f;
        if(!double.IsFinite(volume)||volume<=0||!float.IsFinite(density)||density<=0)throw new InvalidDataException("Mass / scaled volume exceeds the native density range.");
        var nativeVolume=(float)volume;var mass=body.Motion==SceneBodyMotion.Dynamic?(double)body.MassKg!.Value:volume;
        if(!float.IsFinite(nativeVolume)||nativeVolume<1.17549435e-38f||density<1.17549435e-38f||mass<1.17549435e-38f||!float.IsFinite((float)(1/mass))||(float)(1/mass)<1.17549435e-38f||!Finite(position+Vector3.Transform(center,rotation)))throw new InvalidDataException("Shape volume, mass or center exceeds the native finite range.");
        var cylinder=mass*(Math.PI*x*x*(2*y))/volume;var caps=mass-cylinder;
        var moments=shape switch
        {
            SceneBoxShape=>new[]{mass*(y*y+z*z)/3,mass*(x*x+z*z)/3,mass*(x*x+y*y)/3},
            SceneSphereShape=>new[]{.4*mass*x*x},
            _=>new[]{.5*cylinder*x*x+.4*caps*x*x,cylinder*(x*x/4+y*y/3)+caps*(.4*x*x+y*y+.75*y*x)},
        };
        var offsetMoment=mass*((double)center.X*center.X+(double)center.Y*center.Y+(double)center.Z*center.Z);
        if(!double.IsFinite(offsetMoment)||offsetMoment>float.MaxValue||moments.Any(moment=>!float.IsFinite((float)moment)||moment<1.17549435e-38f||!float.IsFinite((float)(1/moment))||(float)(1/moment)<1.17549435e-38f))throw new InvalidDataException("Shape inertia exceeds the native finite range.");
        Shape native;
        switch(shape)
        {
            case SceneBoxShape:
                using(var settings=BoxShapeSettings.Create(F(dimensions),MathF.Min(.05f,MathF.Min(dimensions.X,MathF.Min(dimensions.Y,dimensions.Z))*.1f),default)){settings.SetDensity(density);native=settings.CreateShape();}break;
            case SceneSphereShape:
                using(var settings=SphereShapeSettings.Create(dimensions.X,default)){settings.SetDensity(density);native=settings.CreateShape();}break;
            case SceneCapsuleYShape:
                using(var settings=CapsuleShapeSettings.Create(dimensions.Y,dimensions.X,default)){settings.SetDensity(density);native=settings.CreateShape();}break;
            default:throw new InvalidDataException("Unsupported native shape.");
        }
        if(native.Equals(default(Shape)))throw new InvalidOperationException("Native shape creation refused.");
        using(native)
        {
            if(center!=Vector3.Zero)
            {using var translated=RotatedTranslatedShapeSettings.Create(F(center),floatQuat.Identity,native);using var offset=translated.CreateShape();Create(offset);}
            else Create(native);
        }
        void Create(Shape solid)
        {
            var motion=body.Motion switch{SceneBodyMotion.Static=>MotionType.Static,SceneBodyMotion.Dynamic=>MotionType.Dynamic,_=>MotionType.Kinematic};
            using var settings=BodyCreationSettings.Create(solid,F(position),Q(rotation),motion,body.Motion==SceneBodyMotion.Static?(ushort)0:(ushort)1);
            settings.SetLinearDamping(0);settings.SetAngularDamping(0);settings.SetAllowSleeping(false);settings.SetFriction(body.Friction);settings.SetAllowedDOFs(space==SceneSpace.TwoDXY?AllowedDOFs.Plane2D:AllowedDOFs.All);
            var id=bodies.CreateAndAddBody(settings,body.Motion==SceneBodyMotion.Static?Activation.DontActivate:Activation.Activate);
            if(id==uint.MaxValue)throw new InvalidOperationException("Native body capacity exhausted.");owned.Add(name,(id,body.Motion));
            if(MathF.Abs(bodies.GetFriction(id)-body.Friction)>1e-6f)throw new InvalidOperationException("Native friction differs from Saved input.");
            if(body.Motion==SceneBodyMotion.Dynamic)
            {
                var actual=Mass(name);if(MathF.Abs(actual-body.MassKg!.Value)>MathF.Max(.001f,body.MassKg.Value*.001f))throw new InvalidOperationException("Native mass differs from Saved input.");
                var inertia=system.GetBodyLockInterfaceNoLock().TryGetBody(id).GetMotionProperties().GetInverseInertiaDiagonal();
                if(!float.IsFinite(inertia.X)||!float.IsFinite(inertia.Y)||!float.IsFinite(inertia.Z)||inertia.Z<=0||space==SceneSpace.ThreeD&&(inertia.X<=0||inertia.Y<=0))throw new InvalidOperationException("Native inertia exceeds its finite range.");
            }
        }
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    internal float Mass(string name){Check();var entry=owned[name];if(entry.Motion!=SceneBodyMotion.Dynamic)throw new InvalidOperationException("Only dynamic bodies own authored mass.");var body=system.GetBodyLockInterfaceNoLock().TryGetBody(entry.Id);var mass=1/body.GetMotionProperties().GetInverseMass();if(!float.IsFinite(mass)||mass<=0)throw new InvalidOperationException("Native mass is invalid.");return mass;}
    internal (Vector3 Position,Quaternion Rotation) Pose(string name){Check();bodies.GetPositionAndRotation(owned[name].Id,out var p,out var q);return(new(p.X,p.Y,p.Z),new(q.X,q.Y,q.Z,q.W));}
    internal void Velocity(string name,Vector3 linear,Vector3 angular)
    {Check();if(!Finite(linear)||!Finite(angular)||space==SceneSpace.TwoDXY&&(linear.Z!=0||angular.X!=0||angular.Y!=0))throw new ArgumentException("Use finite permitted-axis velocities.");bodies.SetLinearAndAngularVelocity(owned[name].Id,F(linear),F(angular));}
    internal void MoveKinematic(string name,Vector3 position,Quaternion rotation)
    {Check();if(owned[name].Motion!=SceneBodyMotion.Kinematic||!Finite(position)||!float.IsFinite(rotation.LengthSquared())||MathF.Abs(rotation.LengthSquared()-1)>1e-6f||space==SceneSpace.TwoDXY&&(position.Z!=0||rotation.X!=0||rotation.Y!=0))throw new ArgumentException("Use a valid kinematic target in the declared space.");bodies.MoveKinematic(owned[name].Id,F(position),Q(rotation),StepSeconds);}
    internal void Tick(){Check();var result=system.Update(StepSeconds,1,temp,jobs);if((int)result!=0)throw new InvalidOperationException("Native physics step refused: "+result);}
    public void Dispose()
    {
        if(disposed)return;Check();disposed=true;var errors=new List<Exception>();void Try(Action action){try{action();}catch(Exception e){errors.Add(e);}}
        if(hasSystem){foreach(var item in owned.Values){Try(()=>bodies.RemoveBody(item.Id));Try(()=>bodies.DestroyBody(item.Id));}owned.Clear();Try(()=>system.Dispose());}
        if(hasFilter)Try(()=>filter.Dispose());if(hasPairs)Try(()=>pairs.Dispose());if(hasBroad)Try(()=>broad.Dispose());if(hasJobs)Try(()=>jobs.Dispose());if(hasTemp)Try(()=>temp.Dispose());if(types)Try(Jolt.UnregisterTypes);if(factory)Try(Factory.DestroyInstance);if(held){held=false;lock(Global)active=false;}
        if(errors.Count>0)throw new AggregateException("Physics cleanup refused.",errors);
    }
}
