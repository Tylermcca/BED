using System.Numerics;
using System.Text.Json.Nodes;
using Bed.Editor;
namespace Bed.Editor.ImGui;

internal sealed partial class Scene2InspectorType
{
    private static IEnumerable<Field> BodyFields(SceneBody? body)
    {
        if(body is null)yield break;
        yield return new("body.motion","Motion",PropertyKind.Choice,Motion(body.Motion),"/body/motion",["static","dynamic","kinematic"]);
        yield return new("body.shape","Collision shape",PropertyKind.Choice,ShapeKind(body.Shape),"/body/shape",["box","sphere","capsule-y","mesh-bounds"]);
        if(body.MassKg is {} mass)yield return new("body.mass","Mass (kg)",PropertyKind.Number,(double)mass,"/body/massKg");
        yield return new("body.friction","Friction",PropertyKind.Number,(double)body.Friction,"/body/friction");
        var center=body.Shape switch{SceneBoxShape b=>b.Center,SceneSphereShape b=>b.Center,SceneCapsuleYShape b=>b.Center,_=>Vector3.Zero};
        if(body.Shape is not SceneMeshBoundsShape)
            for(var axis=0;axis<3;axis++)yield return new("body.center."+axis,"Body center "+"XYZ"[axis],PropertyKind.Number,(double)center[axis],"/body/shape");
        if(body.Shape is SceneBoxShape box)
            for(var axis=0;axis<3;axis++)yield return new("body.extent."+axis,"Half extent "+"XYZ"[axis],PropertyKind.Number,(double)box.HalfExtents[axis],"/body/shape");
        if(body.Shape is SceneSphereShape sphere)yield return new("body.radius","Radius (m)",PropertyKind.Number,(double)sphere.Radius,"/body/shape");
        if(body.Shape is SceneCapsuleYShape capsule)
        {yield return new("body.radius","Radius (m)",PropertyKind.Number,(double)capsule.Radius,"/body/shape");yield return new("body.height","Cylinder half height (m)",PropertyKind.Number,(double)capsule.HalfHeight,"/body/shape");}
    }
    private static string Motion(SceneBodyMotion value)=>value switch{SceneBodyMotion.Static=>"static",SceneBodyMotion.Dynamic=>"dynamic",_=>"kinematic"};
    private static string ShapeKind(SceneShape value)=>value switch{SceneBoxShape=>"box",SceneSphereShape=>"sphere",SceneCapsuleYShape=>"capsule-y",_=>"mesh-bounds"};
    private static JsonArray Vec(Vector3 value)=>new(value.X,value.Y,value.Z);
    private static JsonObject ShapeJson(SceneShape value)=>value switch
    {
        SceneBoxShape b=>new(){{"kind","box"},{"halfExtents",Vec(b.HalfExtents)},{"center",Vec(b.Center)}},
        SceneSphereShape b=>new(){{"kind","sphere"},{"radius",b.Radius},{"center",Vec(b.Center)}},
        SceneCapsuleYShape b=>new(){{"kind","capsule-y"},{"radius",b.Radius},{"halfHeight",b.HalfHeight},{"center",Vec(b.Center)}},
        _=>new(){{"kind","mesh-bounds"}},
    };
    internal static JsonObject BodyJson(SceneBody body)
    {
        var result=new JsonObject{{"motion",Motion(body.Motion)},{"shape",ShapeJson(body.Shape)},{"friction",body.Friction}};
        if(body.MassKg is {} mass)result["massKg"]=mass;return result;
    }
    private static IReadOnlyList<ICommand<Scene2>>? EditBody(Scene2 scene,IReadOnlyList<string> ids,string propertyId,object? value,bool reset)
    {
        var resolved=Scene2ViewProjection.For(scene).Resolved;var edits=new JsonArray();
        foreach(var id in ids)
        {
            var item=resolved.Objects.Single(o=>o.Object.Id==id);if(item.Body is not {} body)return null;
            var field=BodyFields(body).SingleOrDefault(f=>f.Id==propertyId);if(field is null)return null;
            void Add(string pointer,JsonNode? destination=null,bool revert=false)
            {var e=new JsonObject{{"id",id},{"field",pointer}};if(revert)e["revert"]=true;else if(destination is not null)e["value"]=destination;edits.Add(e);}
            var own=resolved.Origins[id];
            var whole=own.Kind==Scene2OriginKind.PrefabNode&&scene.Instances.Single(i=>i.Key==own.InstanceKey).Overrides.Any(e=>e.Node==own.NodeKey&&e.Field=="/body");
            var start=edits.Count;
            if(reset&&whole){Add("/body",revert:true);continue;}
            if(reset)
            {
                var origin=resolved.Origins[id];if(origin.Kind!=Scene2OriginKind.PrefabNode)return null;
                var local=scene.Instances.Single(i=>i.Key==origin.InstanceKey).Overrides.Where(e=>e.Node==origin.NodeKey).Select(e=>e.Field).ToHashSet(StringComparer.Ordinal);
                if(local.Contains(field.Pointer))Add(field.Pointer,revert:true);
                if(propertyId=="body.motion"&&local.Contains("/body/massKg"))Add("/body/massKg",revert:true);
                continue;
            }
            if(propertyId=="body.motion")
            {
                var mode=Convert.ToString(value);Add("/body/motion",JsonValue.Create(mode));
                if(mode=="dynamic")Add("/body/massKg",JsonValue.Create(body.MassKg??1f));
                else if(body.MassKg is not null)Add("/body/massKg");
            }
            else if(propertyId=="body.mass"||propertyId=="body.friction")Add(field.Pointer,JsonValue.Create(Convert.ToDouble(value)));
            else
            {
                var shape=ShapeJson(body.Shape);
                if(propertyId=="body.shape")shape=Convert.ToString(value) switch
                {
                    "box"=>ShapeJson(new SceneBoxShape(new(.5f))),"sphere"=>ShapeJson(new SceneSphereShape(.5f)),
                    "capsule-y"=>ShapeJson(new SceneCapsuleYShape(.5f,.5f)),"mesh-bounds"=>ShapeJson(new SceneMeshBoundsShape()),_=>new JsonObject{{"kind",Convert.ToString(value)}},
                };
                else if(propertyId.StartsWith("body.center.",StringComparison.Ordinal))shape["center"]!.AsArray()[int.Parse(propertyId.Split('.')[2])]=Convert.ToDouble(value);
                else if(propertyId.StartsWith("body.extent.",StringComparison.Ordinal))shape["halfExtents"]!.AsArray()[int.Parse(propertyId.Split('.')[2])]=Convert.ToDouble(value);
                else shape[propertyId=="body.radius"?"radius":"halfHeight"]=Convert.ToDouble(value);
                Add("/body/shape",shape);
            }
            if(whole)
            {
                // Editing an existing whole-body local value keeps that ownership instead of introducing overlapping child overrides.
                var replacement=BodyJson(body);
                foreach(var change in edits.Skip(start).ToArray())
                {
                    var key=change!["field"]!.GetValue<string>()[6..];
                    if(change["value"] is {} destination)replacement[key]=destination.DeepClone();else replacement.Remove(key);
                }
                while(edits.Count>start)edits.RemoveAt(edits.Count-1);
                Add("/body",replacement);
            }
        }
        return edits.Count==0?[]:[Scene2Kit.Command("edit_bodies",J(new JsonObject{{"edits",edits}}))];
    }
}
