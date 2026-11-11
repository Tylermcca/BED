using System.Numerics;
using Bed.Editor;
namespace Bed.Editor.ImGui;

// Authoring-only AABBs, transformed from object-local coordinates. Never fits or publishes a runtime build.
internal sealed class Scene2CollisionPreview
{
    internal sealed record Bounds(Vector3 Center,Vector3 HalfExtents,IReadOnlyList<Vector3> Corners,string Status);
    private sealed record Cached(AssetCatalog? Catalog,AssetCatalog.Selection? Selection,Bounds? Bounds,string Why);
    private Scene2? snapshot;
    private readonly Dictionary<string,Cached> entries=new(StringComparer.Ordinal);
    internal bool TryGet(Scene2 scene,string id,AssetCatalog? catalog,out Bounds? bounds,out string why)
    {
        if(!ReferenceEquals(scene,snapshot)){snapshot=scene;entries.Clear();}
        var projection=Scene2ViewProjection.For(scene);var item=projection.ById[id];var body=item.Body;
        bounds=null;why="No body on "+id+".";if(body is null)return false;
        var mesh=body.Shape is SceneMeshBoundsShape;var selection=mesh&&item.Object.Asset is {} guid?catalog?.Select(guid):null;
        if(entries.TryGetValue(id,out var cached)&&ReferenceEquals(cached.Catalog,catalog)&&cached.Selection==selection)
        {bounds=cached.Bounds;why=cached.Why;return bounds is not null;}
        Vector3 center=default,half=default;
        switch(body.Shape)
        {
            case SceneBoxShape b:center=b.Center;half=b.HalfExtents;break;
            case SceneSphereShape b:center=b.Center;half=new(b.Radius);break;
            case SceneCapsuleYShape b:center=b.Center;half=new(b.Radius,b.HalfHeight+b.Radius,b.Radius);break;
            case SceneMeshBoundsShape:
                if(catalog is null||selection is null){why="Model bounds unavailable: "+(catalog?.Resolve(item.Object.Asset!).Reason??"no available model catalog.");break;}
                if(!catalog.TryResolve(selection,out var lease,out why)||lease!.Texture is not null){why="Model bounds require a current model, not an image.";break;}
                var model=new ModelPreview(catalog);model.Select(selection);
                if(!model.TryParts(out var parts,out why)){why="Model bounds unavailable: "+why;break;}
                var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);var count=0;
                foreach(var part in parts!)foreach(var point in part.Geometry.Positions){min=Vector3.Min(min,point);max=Vector3.Max(max,point);count++;}
                center=(min+max)*.5f;half=(max-min)*.5f;
                if(count==0){why="Model bounds unavailable: empty model.";break;}
                // Recheck after decoding; revoked admission/policy never retains a last-good preview as current.
                if(catalog.Select(item.Object.Asset!)!=selection){why="Model bounds changed during preview.";half=default;break;}
                break;
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
        if(Finite(center)&&Finite(half)&&half.X>0&&half.Y>0&&half.Z>0)
        {
            var world=SceneViewport.World(projection.Flat,id);var points=new Vector3[8];
            for(var i=0;i<8;i++)points[i]=Vector3.Transform(center+half*new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1),world);
            if(points.All(Finite))
            {
                var status=$"{id}: local center [{center.X:g}, {center.Y:g}, {center.Z:g}], half extents [{half.X:g}, {half.Y:g}, {half.Z:g}] m.";
                if(mesh)status+=" Admitted model revision "+selection!.ContentRevision+". Preview only; build captures and checks again.";
                bounds=new(center,half,Array.AsReadOnly(points),status);why="";
            }
            else why="Collision bounds exceed the finite preview range.";
        }
        else if(!mesh||why.Length==0||why.StartsWith("No body",StringComparison.Ordinal))why="Collision bounds require three positive finite dimensions; degenerate models are refused.";
        entries[id]=new(catalog,selection,bounds,why);return bounds is not null;
    }
}
