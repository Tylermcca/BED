using System.Numerics;
using System.Text.Json;
using Bed.Editor;
namespace Bed.Editor.ImGui;

// Read-only projections plus persisted scene2 commands; never a second mutable flattened session.
internal sealed partial class Scene2InspectorType:IObjectType<Scene2>
{
    public string Id=>"scene2.object";
    public string Label=>"Object";
    public long? RowsVersion=>0;
    internal static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value);
    internal static ICommand<Scene2> Set(string id,string field,object? value)=>value is null?Scene2Kit.Command("set",J(new{id,field})):Scene2Kit.Command("set",J(new{id,field,value}));
    private static string Escape(string s)=>s.Replace("~","~0").Replace("/","~1");
    /// <summary>Plain words for the three standard physics motion types, shown beside the Motion field.</summary>
    internal const string MotionHelp="Static: never moves. Dynamic: moved by physics. Kinematic: moved by the game, pushes others. ";
    private sealed record Field(string Id,string Label,PropertyKind Kind,object? Value,string Pointer,IReadOnlyList<string>? Choices=null);
    private static IEnumerable<Field> Fields(Scene2 scene,Scene2Object item)
    {
        var o=item.Object;yield return new("id","Id",PropertyKind.Text,o.Id,"/id");
        yield return new("asset","Asset",PropertyKind.Text,o.Asset??"","/asset");
        yield return new("parent","Inside",PropertyKind.Text,o.Parent??"","/parent");
        for(var axis=0;axis<(scene.Space==SceneSpace.TwoDXY?2:3);axis++)
        {
            var a=axis==0?"X":axis==1?"Y":"Z";
            yield return new("position."+axis,"Position "+a,PropertyKind.Number,(double)o.Position[axis],"/position");
            yield return new("scale."+axis,"Scale "+a,PropertyKind.Number,(double)o.Scale[axis],"/scale");
        }
        if(scene.Space==SceneSpace.TwoDXY)yield return new("angle","Rotation Z",PropertyKind.Number,2*Math.Atan2(o.Rotation.Z,o.Rotation.W)*180/Math.PI,"/rotation");
        else
        {
            var q=o.Rotation;yield return new("rotation.0","Quaternion X",PropertyKind.Number,(double)q.X,"/rotation");yield return new("rotation.1","Quaternion Y",PropertyKind.Number,(double)q.Y,"/rotation");
            yield return new("rotation.2","Quaternion Z",PropertyKind.Number,(double)q.Z,"/rotation");yield return new("rotation.3","Quaternion W",PropertyKind.Number,(double)q.W,"/rotation");
        }
        if(item.Sprite is {} s)
        {
            yield return new("size.0","Sprite width",PropertyKind.Number,(double)s.Size.X,"/sprite/size");yield return new("size.1","Sprite height",PropertyKind.Number,(double)s.Size.Y,"/sprite/size");
            yield return new("pivot.0","Pivot X",PropertyKind.Number,(double)s.Pivot.X,"/sprite/pivot");yield return new("pivot.1","Pivot Y",PropertyKind.Number,(double)s.Pivot.Y,"/sprite/pivot");
            yield return new("order","Draw order",PropertyKind.Integer,s.Order,"/sprite/order");yield return new("filter","Filter",PropertyKind.Choice,s.Filter==SceneSpriteFilter.Nearest?"nearest":"linear","/sprite/filter",["nearest","linear"]);
        }
        foreach(var field in BodyFields(item.Body))yield return field;
        foreach(var p in o.Properties)
        {
            var kind=p.Value.ValueKind switch{JsonValueKind.String=>PropertyKind.Text,JsonValueKind.True or JsonValueKind.False=>PropertyKind.Toggle,_=>PropertyKind.Number};
            object? value=p.Value.ValueKind switch{JsonValueKind.String=>p.Value.GetString(),JsonValueKind.True=>true,JsonValueKind.False=>false,_=>p.Value.GetDouble()};
            yield return new("property."+p.Key,p.Key,kind,value,"/properties/"+Escape(p.Key));
        }
        foreach(var p in o.References)yield return new("reference."+p.Key,"Reference "+p.Key,PropertyKind.Text,p.Value,"/references/"+Escape(p.Key));
    }
    public IReadOnlyList<InspectorRow> Rows(Scene2 scene,IReadOnlyList<string> ids)
    {
        if(ids.Count==0)return [];var projection=Scene2ViewProjection.For(scene);var resolved=projection.Resolved;var byId=projection.ById;
        if(ids.Any(id=>!byId.ContainsKey(id)))return [];
        var fields=ids.Select(id=>Fields(scene,byId[id]).ToDictionary(f=>f.Id,StringComparer.Ordinal)).ToArray();var rows=new List<InspectorRow>();
        foreach(var field in fields[0].Values)
        {
            if(fields.Any(f=>!f.TryGetValue(field.Id,out var other)||other.Kind!=field.Kind))continue;
            var same=fields.All(f=>Equals(field.Value,f[field.Id].Value));
            var origins=ids.Select(id=>resolved.Origins[id]).ToArray();var inherited=origins.All(o=>o.Kind==Scene2OriginKind.PrefabNode);
            var local=origins.Any(o=>o.Kind==Scene2OriginKind.PrefabNode&&scene.Instances.Single(i=>i.Key==o.InstanceKey).Overrides.Any(e=>e.Node==o.NodeKey&&(e.Field==field.Pointer||field.Pointer.StartsWith(e.Field+"/",StringComparison.Ordinal))));
            var editable=field.Id!="id"&&origins.All(o=>field.Id=="parent"?o.Kind!=Scene2OriginKind.PrefabNode:o.Kind!=Scene2OriginKind.Placement||field.Pointer is "/position" or "/rotation" or "/scale");
            rows.Add(new(field.Id,field.Label,field.Kind,same?field.Value:null,!same,editable,local,inherited,field.Choices??[],(field.Id=="body.motion"?MotionHelp:"")+(inherited?(local?"Changed in this copy. Revert puts back the prefab's value.":"Comes from the prefab."):"Owned by this object.")));
        }
        return rows;
    }
    public IReadOnlyList<ICommand<Scene2>>? EditCommands(Scene2 scene,IReadOnlyList<string> ids,string propertyId,object? value,bool reset)
    {
        if(propertyId.StartsWith("body.",StringComparison.Ordinal))return EditBody(scene,ids,propertyId,value,reset);
        var resolved=Scene2ViewProjection.For(scene).Resolved;var commands=new List<ICommand<Scene2>>();
        foreach(var id in ids)
        {
            var item=resolved.Objects.Single(o=>o.Object.Id==id);var field=Fields(scene,item).SingleOrDefault(f=>f.Id==propertyId);if(field is null||field.Id=="id")return null;
            var origin=resolved.Origins[id];
            if(field.Id=="parent")
            {if(reset)return null;commands.Add(Scene2Kit.Command("object.set_parent",J(new{id,parent=string.IsNullOrWhiteSpace(value as string)?null:value})));continue;}
            if(reset)
            {
                if(origin.Kind!=Scene2OriginKind.PrefabNode)return null;
                // Reset only explicit local values; inherited siblings already have the desired value.
                if(scene.Instances.Single(i=>i.Key==origin.InstanceKey).Overrides.Any(e=>e.Node==origin.NodeKey&&e.Field==field.Pointer))
                    commands.Add(Scene2Kit.Command("revert",J(new{instance=origin.InstanceKey,node=origin.NodeKey,field=field.Pointer})));
                continue;
            }
            object? destination=value;
            if(propertyId.StartsWith("position.",StringComparison.Ordinal)||propertyId.StartsWith("scale.",StringComparison.Ordinal))
            {var v=field.Pointer=="/position"?item.Object.Position:item.Object.Scale;v[int.Parse(propertyId.Split('.')[1])]=Convert.ToSingle(value);destination=new[]{v.X,v.Y,v.Z};}
            else if(propertyId=="angle"){var q=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,Convert.ToSingle(value)*MathF.PI/180);destination=new[]{q.X,q.Y,q.Z,q.W};}
            else if(propertyId.StartsWith("rotation.",StringComparison.Ordinal))
            {var q=item.Object.Rotation;var a=new[]{q.X,q.Y,q.Z,q.W};a[int.Parse(propertyId.Split('.')[1])]=Convert.ToSingle(value);destination=a;}
            else if(propertyId.StartsWith("size.",StringComparison.Ordinal)||propertyId.StartsWith("pivot.",StringComparison.Ordinal))
            {var v=field.Pointer=="/sprite/size"?item.Sprite!.Size:item.Sprite!.Pivot;v[int.Parse(propertyId.Split('.')[1])]=Convert.ToSingle(value);destination=new[]{v.X,v.Y};}
            else if(propertyId=="order")destination=Convert.ToInt32(value);
            else if(propertyId=="asset")destination=string.IsNullOrWhiteSpace(value as string)?null:value;
            else if(propertyId.StartsWith("reference.",StringComparison.Ordinal))destination=new{@object=value};
            commands.Add(Set(id,field.Pointer,destination));
        }
        return commands;
    }
}
