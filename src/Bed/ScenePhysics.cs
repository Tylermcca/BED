using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed;

/// <summary>Explicit world gravity in metres/second squared and fixed step in seconds; no engine defaults.</summary>
/// <param name="Gravity">Finite world acceleration; Z must be zero for XY levels.</param>
/// <param name="FixedStepSeconds">Finite step from 0.0001 through 0.1 seconds.</param>
public sealed record ScenePhysics(Vector3 Gravity,float FixedStepSeconds);
/// <summary>Authored motion responsibility; does not create a native body.</summary>
public enum SceneBodyMotion
{
    /// <summary>Fixed initial pose.</summary>
    Static,
    /// <summary>Simulation owns world motion.</summary>
    Dynamic,
    /// <summary>The game supplies explicit physics-boundary targets.</summary>
    Kinematic,
}
/// <summary>Closed scene-v2 collision shape vocabulary; custom derived types refuse validation.</summary>
public abstract record SceneShape;
/// <summary>Local metre box dimensions and geometric offset; world scale is applied once by the game.</summary>
/// <param name="HalfExtents">Three positive local half-extents.</param>
/// <param name="Center">Finite local geometric offset, default zero.</param>
public sealed record SceneBoxShape(Vector3 HalfExtents,Vector3 Center=default):SceneShape;
/// <summary>A local metre sphere.</summary>
/// <param name="Radius">Positive finite radius.</param>
/// <param name="Center">Finite local geometric offset, default zero.</param>
public sealed record SceneSphereShape(float Radius,Vector3 Center=default):SceneShape;
/// <summary>A local Y-axis capsule with a positive cylindrical segment.</summary>
/// <param name="Radius">Positive finite radius.</param>
/// <param name="HalfHeight">Positive half-length of the cylinder, excluding hemispheres.</param>
/// <param name="Center">Finite local geometric offset, default zero.</param>
public sealed record SceneCapsuleYShape(float Radius,float HalfHeight,Vector3 Center=default):SceneShape;
/// <summary>Request to fit a local box to this object's captured model bounds at build time; never an asset lookup in core.</summary>
public sealed record SceneMeshBoundsShape:SceneShape;
/// <summary>Immutable authored body description, validated as part of a complete scene.</summary>
/// <param name="Motion">Static, dynamic or kinematic responsibility.</param>
/// <param name="Shape">One supported local collision shape.</param>
/// <param name="Friction">Finite dimensionless value in 0..1.</param>
/// <param name="MassKg">Positive finite total kilograms for dynamic only; absent for other modes.</param>
public sealed record SceneBody(SceneBodyMotion Motion,SceneShape Shape,float Friction,float? MassKg=null);

internal static class ScenePhysicsRules
{
    internal static float Number(Scene2Read r)
    {if(r.Value.ValueKind!=JsonValueKind.Number||!r.Value.TryGetSingle(out var n)||!float.IsFinite(n))throw Scene2Read.Bad(r.Path,"expected a finite float32");return n;}
    internal static ScenePhysics ReadPhysics(Scene2Read r)
    {r.Only("gravity","fixedStepSeconds");return new(r.Get("gravity").Vec3(),Number(r.Get("fixedStepSeconds")));}
    internal static SceneBodyMotion Motion(Scene2Read r)=>r.Text() switch{"static"=>SceneBodyMotion.Static,"dynamic"=>SceneBodyMotion.Dynamic,"kinematic"=>SceneBodyMotion.Kinematic,_=>throw Scene2Read.Bad(r.Path,"unknown body motion")};
    internal static SceneShape ReadShape(Scene2Read r)
    {
        var kind=r.Get("kind").Text();var center=r.Has("center")?r.Get("center").Vec3():Vector3.Zero;
        SceneShape result;
        switch(kind)
        {
            case "box":r.Only("kind","halfExtents","center");result=new SceneBoxShape(r.Get("halfExtents").Vec3(),center);break;
            case "sphere":r.Only("kind","radius","center");result=new SceneSphereShape(Number(r.Get("radius")),center);break;
            case "capsule-y":r.Only("kind","radius","halfHeight","center");result=new SceneCapsuleYShape(Number(r.Get("radius")),Number(r.Get("halfHeight")),center);break;
            case "mesh-bounds":r.Only("kind");result=new SceneMeshBoundsShape();break;
            default:throw Scene2Read.Bad(r.Path,"unsupported body shape");
        }
        CheckShape(result,r.Path);return result;
    }
    internal static SceneBody ReadBody(Scene2Read r)
    {r.Only("motion","shape","massKg","friction");var b=new SceneBody(Motion(r.Get("motion")),ReadShape(r.Get("shape")),Number(r.Get("friction")),r.Has("massKg")?Number(r.Get("massKg")):null);CheckBody(b,r.Path);return b;}
    internal static void CheckPhysics(ScenePhysics? value,SceneSpace space,string at="/physics")
    {
        if(value is null)return;
        if(!Scene2Numeric.Finite(value.Gravity)||!float.IsFinite(value.FixedStepSeconds)||value.FixedStepSeconds is <.0001f or >.1f)throw Scene2Read.Bad(at,"gravity must be finite and fixed step must be 0.0001..0.1 seconds");
        if(space==SceneSpace.TwoDXY&&value.Gravity.Z!=0)throw Scene2Read.Bad(at+"/gravity","XY gravity must have zero Z");
    }
    internal static void CheckBody(SceneBody b,string at)
    {
        if(!Enum.IsDefined(b.Motion)||!float.IsFinite(b.Friction)||b.Friction is <0 or >1)throw Scene2Read.Bad(at,"unsupported body motion or friction outside 0..1");
        if(b.Motion==SceneBodyMotion.Dynamic)
        {if(b.MassKg is not {} mass||!float.IsFinite(mass)||mass<=0)throw Scene2Read.Bad(at+"/massKg","a dynamic body requires positive finite kilograms");}
        else if(b.MassKg is not null)throw Scene2Read.Bad(at+"/massKg","mass is allowed only for a dynamic body");
        CheckShape(b.Shape,at+"/shape");
    }
    private static bool Positive(float n)=>float.IsFinite(n)&&n>0;
    private static void CheckShape(SceneShape shape,string at)
    {
        var valid=shape switch
        {
            SceneBoxShape s=>Scene2Numeric.Finite(s.Center)&&Positive(s.HalfExtents.X)&&Positive(s.HalfExtents.Y)&&Positive(s.HalfExtents.Z),
            SceneSphereShape s=>Scene2Numeric.Finite(s.Center)&&Positive(s.Radius),
            SceneCapsuleYShape s=>Scene2Numeric.Finite(s.Center)&&Positive(s.Radius)&&Positive(s.HalfHeight),
            SceneMeshBoundsShape=>true,_=>false,
        };
        if(!valid)throw Scene2Read.Bad(at,"use one supported shape with positive finite dimensions and finite center");
    }
    private static JsonArray Vector(Vector3 v)=>new(v.X,v.Y,v.Z);
    internal static JsonObject Physics(ScenePhysics p)=>new(){{"gravity",Vector(p.Gravity)},{"fixedStepSeconds",p.FixedStepSeconds}};
    internal static JsonObject Shape(SceneShape s)=>s switch
    {
        SceneBoxShape b=>new(){{"kind","box"},{"halfExtents",Vector(b.HalfExtents)},{"center",Vector(b.Center)}},
        SceneSphereShape b=>new(){{"kind","sphere"},{"radius",b.Radius},{"center",Vector(b.Center)}},
        SceneCapsuleYShape b=>new(){{"kind","capsule-y"},{"radius",b.Radius},{"halfHeight",b.HalfHeight},{"center",Vector(b.Center)}},
        SceneMeshBoundsShape=>new(){{"kind","mesh-bounds"}},_=>throw Scene2Read.Bad("/body/shape","unsupported body shape"),
    };
    internal static JsonObject Body(SceneBody b)
    {var json=new JsonObject{{"motion",b.Motion switch{SceneBodyMotion.Static=>"static",SceneBodyMotion.Dynamic=>"dynamic",SceneBodyMotion.Kinematic=>"kinematic",_=>throw Scene2Read.Bad("/body/motion","unsupported motion")}},{"shape",Shape(b.Shape)},{"friction",b.Friction}};if(b.MassKg is {} mass)json.Add("massKg",mass);return json;}
}
