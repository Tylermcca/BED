using System.Numerics;
using Bed;
namespace PhysicsStarter;

// The interactive toy's rules, free of window and GPU types so tests drive them directly.

/// <summary>What the player asked for this frame.</summary>
internal enum ToyCommand{DropCrate,Reset,Quit}
/// <summary>The keys the toy listens to, mapped from the window library by the window layer.</summary>
internal enum ToyKey{Other,A,D,Left,Right,R,Escape}
internal enum ToyButton{Left,Right,Other}

/// <summary>An orbit camera around the island: yaw and pitch in radians, distance in metres.</summary>
internal sealed class OrbitCamera
{
    internal const float MinPitch=.17f,MaxPitch=1.25f,MinDistance=5,MaxDistance=24;
    internal Vector3 Target{get;set;}=new(0,1f,0);
    internal float Yaw{get;private set;}=.62f;
    internal float Pitch{get;private set;}=.4f;
    internal float Distance{get;private set;}=14f;
    internal const float FieldOfView=.75f;
    internal void Orbit(float yaw,float pitch){Yaw=(Yaw+yaw)%MathF.Tau;Pitch=Math.Clamp(Pitch+pitch,MinPitch,MaxPitch);}
    /// <summary>Wheel notches: positive zooms in, each notch by about a tenth.</summary>
    internal void Zoom(float notches)=>Distance=Math.Clamp(Distance*MathF.Pow(.9f,notches),MinDistance,MaxDistance);
    internal Vector3 Eye=>Target+Distance*new Vector3(MathF.Cos(Pitch)*MathF.Sin(Yaw),MathF.Sin(Pitch),MathF.Cos(Pitch)*MathF.Cos(Yaw));
    internal Matrix4x4 View=>Matrix4x4.CreateLookAt(Eye,Target,Vector3.UnitY);
    internal static Matrix4x4 Projection(float aspect)=>Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView,aspect,.1f,200);
    /// <summary>The world-space ray under a point of a width x height view, in the same units the point uses.</summary>
    internal (Vector3 Origin,Vector3 Direction) Ray(float x,float y,float width,float height)
    {
        Matrix4x4.Invert(View*Projection(width/height),out var inverse);
        var ndc=new Vector2(2*x/width-1,1-2*y/height);
        var near=Vector4.Transform(new Vector4(ndc,0,1),inverse);var far=Vector4.Transform(new Vector4(ndc,1,1),inverse);
        var a=new Vector3(near.X,near.Y,near.Z)/near.W;var b=new Vector3(far.X,far.Y,far.Z)/far.W;
        return(a,Vector3.Normalize(b-a));
    }
}

/// <summary>Turns window events into camera moves and commands. Left click drops a crate, right-drag or A/D (or the
/// arrow keys) orbits, the wheel zooms, R resets and Esc quits. A left press that moves more than a few pixels is a
/// drag, not a click.</summary>
internal sealed class ToyControls(OrbitCamera camera)
{
    internal const float DragRadiansPerPixel=.008f,KeyRadiansPerSecond=1.6f,ClickSlop=6;
    private readonly HashSet<ToyKey> held=[];
    private readonly List<(ToyCommand Command,Vector2 At)> pending=[];
    private Vector2 cursor,leftDown;private bool left,right,leftMoved;
    internal OrbitCamera Camera=>camera;
    internal void Key(ToyKey key,bool down)
    {
        if(!down){held.Remove(key);return;}
        held.Add(key);
        if(key==ToyKey.R)pending.Add((ToyCommand.Reset,cursor));
        else if(key==ToyKey.Escape)pending.Add((ToyCommand.Quit,cursor));
    }
    internal void Button(ToyButton button,bool down)
    {
        if(button==ToyButton.Right)right=down;
        else if(button==ToyButton.Left)
        {
            if(down){left=true;leftMoved=false;leftDown=cursor;}
            else if(left){left=false;if(!leftMoved)pending.Add((ToyCommand.DropCrate,cursor));}
        }
    }
    internal void Cursor(float x,float y)
    {
        var next=new Vector2(x,y);var delta=next-cursor;cursor=next;
        if(right)camera.Orbit(-delta.X*DragRadiansPerPixel,delta.Y*DragRadiansPerPixel);
        if(left&&Vector2.Distance(cursor,leftDown)>ClickSlop)leftMoved=true;
    }
    internal void Scroll(float notches)=>camera.Zoom(notches);
    /// <summary>Applies held keys for this many seconds and returns the commands since the last call.</summary>
    internal IReadOnlyList<(ToyCommand Command,Vector2 At)> Update(float seconds)
    {
        var turn=(held.Contains(ToyKey.A)||held.Contains(ToyKey.Left)?1:0)-(held.Contains(ToyKey.D)||held.Contains(ToyKey.Right)?1:0);
        if(turn!=0)camera.Orbit(turn*KeyRadiansPerSecond*seconds,0);
        var result=pending.ToArray();pending.Clear();return result;
    }
}

/// <summary>The live game: the saved bodies in Jolt, the kinematic rule, and crates the player drops. Reset rebuilds
/// everything from the saved scene; nothing here writes the level.</summary>
internal sealed class CrateToy:IDisposable
{
    internal const int MaxDropped=40;
    internal const float FallenY=-12;
    private readonly Scene2 scene;
    private PhysicsWorld world;private KinematicMotion motion;
    private readonly List<string> dropped=[];
    private int serial;private double accumulator;
    /// <summary>The prefab copy a dropped crate repeats: the first dynamic body in saved order, its model and world scale.</summary>
    internal (string Id,SceneBody Body,string? Asset,float Scale)? Template{get;}
    /// <summary>The island's walkable rectangle in XZ, from the static boxes, and the height of its top.</summary>
    internal (Vector2 Min,Vector2 Max,float Top) Island{get;}
    internal float DropHeight{get;}
    internal int SavedBodies{get;}
    internal int Ticks{get;private set;}
    internal double DroppedSeconds{get;private set;}
    internal IReadOnlyList<string> Dropped=>dropped;
    internal PhysicsWorld World=>world;
    internal KinematicMotion Motion=>motion;
    internal CrateToy(Scene2 scene)
    {
        this.scene=scene;world=new PhysicsWorld(scene);motion=new KinematicMotion(world);SavedBodies=world.Count;
        var worlds=SceneWorld.Matrices(scene);
        if(scene.Objects.FirstOrDefault(o=>o.Body?.Motion==SceneBodyMotion.Dynamic) is {} first)
        {Matrix4x4.Decompose(worlds[first.Object.Id],out var scale,out _,out _);Template=(first.Object.Id,first.Body!,first.Object.Asset,scale.X);}
        var boxes=scene.Objects.Where(o=>o.Body is {Motion:SceneBodyMotion.Static,Shape:SceneBoxShape}).Select(o=>
        {Matrix4x4.Decompose(worlds[o.Object.Id],out var s,out _,out var p);var b=(SceneBoxShape)o.Body!.Shape;var c=p+b.Center*s.X;var h=b.HalfExtents*s.X;return(Min:c-h,Max:c+h);}).ToArray();
        Island=boxes.Length==0?(new(-4,-4),new(4,4),0):(new(boxes.Min(b=>b.Min.X),boxes.Min(b=>b.Min.Z)),new(boxes.Max(b=>b.Max.X),boxes.Max(b=>b.Max.Z)),boxes.Max(b=>b.Max.Y));
        var highest=world.Named(SceneBodyMotion.Dynamic).Select(n=>world.Saved(n).Position.Y).DefaultIfEmpty(Island.Top).Max();
        DropHeight=MathF.Max(highest,Island.Top)+2.5f;
    }
    /// <summary>Where a crate lands for a ground ray: the hit on the island's top, or the island centre when the ray
    /// misses the island.</summary>
    internal Vector2 DropPoint(Vector3 origin,Vector3 direction)
    {
        var centre=(Island.Min+Island.Max)/2;
        if(MathF.Abs(direction.Y)<1e-5f)return centre;
        var t=(Island.Top-origin.Y)/direction.Y;if(t<=0)return centre;
        var hit=origin+t*direction;var point=new Vector2(hit.X,hit.Z);var margin=new Vector2(.5f);
        return Vector2.Clamp(point,Island.Min+margin,Island.Max-margin)==point?point:centre;
    }
    /// <summary>Drops a copy of the template crate from above this XZ point; the oldest dropped crate goes when there are too many.</summary>
    internal string? Drop(Vector2 at)
    {
        if(Template is not {} template)return null;
        if(dropped.Count>=MaxDropped){world.Remove(dropped[0]);dropped.RemoveAt(0);}
        var name=$"dropped_crate_{++serial}";
        // A small repeatable turn per crate so a pile looks tumbled, not stacked.
        var rotation=Quaternion.CreateFromYawPitchRoll(serial*.9f,(serial%3-1)*.12f,(serial%2-.5f)*.16f);
        world.Spawn(name,template.Body,new(at.X,DropHeight,at.Y),rotation,template.Scale);dropped.Add(name);return name;
    }
    /// <summary>Back to the saved level: a fresh world from the saved scene, no dropped crates, time zero.</summary>
    internal void Reset()
    {
        world.Dispose();dropped.Clear();Ticks=0;accumulator=0;
        world=new PhysicsWorld(scene);motion=new KinematicMotion(world);
    }
    /// <summary>One fixed step, then dropped crates that fell off the island are cleared away.</summary>
    internal void Step()
    {
        motion.Step(Ticks);world.Tick();Ticks++;
        for(var i=dropped.Count-1;i>=0;i--)if(world.Pose(dropped[i]).Position.Y<FallenY){world.Remove(dropped[i]);dropped.RemoveAt(i);}
    }
    /// <summary>Real time in, at most eight fixed steps out; whole steps beyond that are dropped and counted.</summary>
    internal int Advance(double seconds)
    {
        accumulator+=seconds;var steps=0;
        while(accumulator>=world.StepSeconds&&steps<8){Step();steps++;accumulator-=world.StepSeconds;}
        if(accumulator>=world.StepSeconds){var skip=Math.Floor(accumulator/world.StepSeconds)*world.StepSeconds;DroppedSeconds+=skip;accumulator-=skip;}
        return steps;
    }
    /// <summary>Every model to draw this frame with its world matrix: saved objects follow their bodies' live poses
    /// (children move with them), and each dropped crate draws the template's model.</summary>
    internal IEnumerable<(string Asset,Matrix4x4 World)> Drawables()
    {
        var live=world.Named(SceneBodyMotion.Static).Concat(world.Named(SceneBodyMotion.Kinematic)).Concat(world.Named(SceneBodyMotion.Dynamic)).Where(n=>!dropped.Contains(n))
            .ToDictionary(n=>n,n=>world.Pose(n),StringComparer.Ordinal);
        foreach(var (id,matrix) in SceneWorld.Matrices(scene,live))
            if(scene.Objects.First(o=>o.Object.Id==id).Object.Asset is {} asset)yield return(asset,matrix);
        if(Template is {Asset:{} crate} template)
            foreach(var name in dropped){var(p,q)=world.Pose(name);yield return(crate,Matrix4x4.CreateScale(template.Scale)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(p));}
    }
    public void Dispose()=>world.Dispose();
}

/// <summary>World matrices for a scene's object tree. A body's live pose replaces its object's world rotation and
/// position (its world scale stays), and its children follow it.</summary>
internal static class SceneWorld
{
    internal static Dictionary<string,Matrix4x4> Matrices(Scene2 scene,IReadOnlyDictionary<string,(Vector3 Position,Quaternion Rotation)>? poses=null)
    {
        var objects=scene.Objects.ToDictionary(o=>o.Object.Id,o=>o.Object,StringComparer.Ordinal);var worlds=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
        Matrix4x4 World(string id)
        {
            if(worlds.TryGetValue(id,out var known))return known;
            var item=objects[id];var parent=item.Parent is null?Matrix4x4.Identity:World(item.Parent);
            var world=Matrix4x4.CreateScale(item.Scale)*Matrix4x4.CreateFromQuaternion(item.Rotation)*Matrix4x4.CreateTranslation(item.Position)*parent;
            if(poses is not null&&poses.TryGetValue(id,out var pose)&&Matrix4x4.Decompose(world,out var scale,out _,out _))
                world=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(pose.Rotation)*Matrix4x4.CreateTranslation(pose.Position);
            worlds[id]=world;return world;
        }
        foreach(var item in scene.Objects)World(item.Object.Id);
        return worlds;
    }
}
