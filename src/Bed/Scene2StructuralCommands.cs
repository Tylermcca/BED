using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed;

internal sealed record Scene2RemoveInstance(string Instance):ICommand<Scene2>
{
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        var instance=Scene2Commands.Instance(json,Instance);var removed=resolved.Origins.Where(p=>p.Value.InstanceKey==Instance).Select(p=>p.Key).ToHashSet(StringComparer.Ordinal);
        foreach(var item in resolved.Objects.Where(o=>!removed.Contains(o.Object.Id)))
            if(item.Object.Parent is {} parent&&removed.Contains(parent)||item.Object.References.Values.Any(removed.Contains))Scene2Commands.Refuse("Another object depends on this instance; remove that dependency first.");
        json["instances"]!.AsArray().Remove(instance);
        if(json["objectOrder"] is JsonArray order)foreach(var item in order.ToArray())if(removed.Contains(item!.GetValue<string>()))order.Remove(item);
    });
}
internal sealed record Scene2Group(string Id,IReadOnlyList<string> Ids):ICommand<Scene2>
{
    public IReadOnlyList<string> Ids {get;}=Ids.ToImmutableArray();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        if(Ids.Count==0||Ids.Distinct(StringComparer.Ordinal).Count()!=Ids.Count||Ids.Any(id=>!resolved.Origins.ContainsKey(id)))Scene2Commands.Refuse("Select distinct existing objects to group.");
        if(Ids.Any(id=>resolved.Origins[id].Kind==Scene2OriginKind.PrefabNode))Scene2Commands.Refuse("Group the whole instance, or Break Link before grouping internal nodes.");
        if(resolved.Origins.ContainsKey(Id))Scene2Commands.Refuse("The group id is already in use.");
        var objects=resolved.Objects.ToDictionary(o=>o.Object.Id,o=>o.Object,StringComparer.Ordinal);var selected=Ids.ToHashSet(StringComparer.Ordinal);
        var worlds=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
        Matrix4x4 World(string id)
        {
            if(worlds.TryGetValue(id,out var found))return found;
            var chain=new Stack<SceneObject>();string? current=id;
            while(current is not null&&!worlds.ContainsKey(current)){var item=objects[current];chain.Push(item);current=item.Parent;}
            var world=current is null?Matrix4x4.Identity:worlds[current];
            while(chain.TryPop(out var item)){world=Local(item)*world;worlds[item.Id]=world;}
            return world;
        }
        bool HasSelectedAncestor(string id)
        {for(var parent=objects[id].Parent;parent is not null;parent=objects[parent].Parent)if(selected.Contains(parent))return true;return false;}
        var roots=Ids.Where(id=>!HasSelectedAncestor(id)).ToArray();var parents=roots.Select(id=>objects[id].Parent).Distinct(StringComparer.Ordinal).ToArray();var parentId=parents.Length==1?parents[0]:null;
        double x=0,y=0,z=0;foreach(var id in Ids){var t=World(id).Translation;x+=t.X;y+=t.Y;z+=t.Z;}
        var center=new Vector3((float)(x/Ids.Count),(float)(y/Ids.Count),(float)(z/Ids.Count));
        var parentWorld=parentId is null?Matrix4x4.Identity:World(parentId);
        if(!Matrix4x4.Invert(parentWorld,out var inverseParent))Scene2Commands.Refuse("The parent transform is singular.");
        var group=SceneObject.At(Id,null,Vector3.Transform(center,inverseParent)) with{Parent=parentId};var groupWorld=Local(group)*parentWorld;
        if(!Matrix4x4.Invert(groupWorld,out var inverseGroup))Scene2Commands.Refuse("The group transform is singular.");
        json["objects"]!.AsArray().Add(Scene2Commands.ObjectJson(new(group,null)));
        foreach(var id in roots)
        {
            var matrix=World(id)*inverseGroup;
            if(!Matrix4x4.Decompose(matrix,out var scale,out var rotation,out var position))Scene2Commands.Refuse("This transform cannot be represented as position, rotation and scale.");
            // A translated group under the same parent leaves original scale/rotation exact, including planar mirrors.
            if(objects[id].Parent==parentId){position=objects[id].Position-group.Position;rotation=objects[id].Rotation;scale=objects[id].Scale;}
            var rebuilt=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(position);
            if(!Scene2Numeric.Close(matrix,rebuilt))Scene2Commands.Refuse("Grouping would introduce shear; keep the existing hierarchy.");
            var origin=resolved.Origins[id];var target=origin.Kind==Scene2OriginKind.Placement?Scene2Commands.Instance(json,origin.InstanceKey!)["placement"]!
                :json["objects"]!.AsArray().Single(o=>Scene2Commands.Text(o!,"id")==id)!;
            target["parent"]=Id;
            Scene2Commands.Set(json,resolved,id,"/position",JsonSerializer.SerializeToElement(new[]{position.X,position.Y,position.Z}));
            Scene2Commands.Set(json,resolved,id,"/rotation",JsonSerializer.SerializeToElement(new[]{rotation.X,rotation.Y,rotation.Z,rotation.W}));
            Scene2Commands.Set(json,resolved,id,"/scale",JsonSerializer.SerializeToElement(new[]{scale.X,scale.Y,scale.Z}));
        }
        // Existing ordinals remain stable; the empty wrapper is appended and never changes sprite tie order.
        var order=Scene2Commands.Order(resolved);order.Add(Id);json["objectOrder"]=order;
    });
    private static Matrix4x4 Local(SceneObject o)=>Matrix4x4.CreateScale(o.Scale)*Matrix4x4.CreateFromQuaternion(o.Rotation)*Matrix4x4.CreateTranslation(o.Position);

}

internal sealed record Scene2Reorder(IReadOnlyList<string> Ids):ICommand<Scene2>
{
    public IReadOnlyList<string> Ids {get;}=Ids.ToImmutableArray();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        if(Ids.Count!=resolved.Objects.Length||Ids.Distinct(StringComparer.Ordinal).Count()!=Ids.Count||Ids.Any(id=>!resolved.Origins.ContainsKey(id)))Scene2Commands.Refuse("Order must contain every expanded id exactly once.");
        json["objectOrder"]=new JsonArray(Ids.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray());
    });
}
