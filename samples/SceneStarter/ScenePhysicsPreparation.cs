using System.Collections.Immutable;
using System.Numerics;
using Bed;
using Bed.Runtime;
namespace SceneStarter;

internal static class ScenePhysicsPreparation
{
    internal static Scene2 Prepare(Scene2 source,ProjectAssets assets,bool supported)
    {
        var flat=Scene2Kit.Flatten(source);if(flat.Physics is null)return flat;
        if(!supported)throw new InvalidOperationException("This game has not enabled authored physics.");
        var fitted=new Dictionary<string,SceneBoxShape>(StringComparer.Ordinal);
        var objects=flat.Objects.Select(item=>
        {
            if(item.Body?.Shape is not SceneMeshBoundsShape)return item;
            var id=item.Object.Asset??throw new InvalidDataException("Mesh bounds require a model asset.");
            if(!fitted.TryGetValue(id,out var shape))
            {
                var mesh=assets.ReadMesh(id);var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);var count=0;
                foreach(var part in mesh.Parts)foreach(var point in part.Geometry.Positions){min=Vector3.Min(min,point);max=Vector3.Max(max,point);count++;}
                Vector3 Average(bool difference)=>new((float)(((double)max.X+(difference?-min.X:min.X))*.5),(float)(((double)max.Y+(difference?-min.Y:min.Y))*.5),(float)(((double)max.Z+(difference?-min.Z:min.Z))*.5));
                var center=Average(false);var half=Average(true);
                if(count==0||!Finite(center)||!Finite(half)||half.X<=0||half.Y<=0||half.Z<=0)throw new InvalidDataException("Mesh bounds require a nondegenerate finite model: "+id);
                fitted.Add(id,shape=new(half,center));
            }
            return item with{Body=item.Body with{Shape=shape}};
        }).ToImmutableArray();
        var result=flat with{Objects=objects};_=new Scene2Format().Read(new Scene2Format().Write(result));return result;
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}
