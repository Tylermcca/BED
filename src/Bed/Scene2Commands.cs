using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed;

// Shared deterministic authority operations. Never edits a flat shadow.
internal static class Scene2Commands
{
    internal static CommandCatalog<Scene2> Catalog() => new CommandCatalog<Scene2>()
        .Register<Scene2SetPhysics>("scene2.set_physics",1,"Set physics")
        .Register<Scene2EditBodies>("scene2.edit_bodies",1,"Edit bodies")
        .Register<Scene2Paste>("scene2.paste",1,"Paste objects")
        .Register<Scene2Reorder>("scene2.reorder",1,"Reorder objects")
        .Register<Scene2Group>("scene2.group",1,"Group objects")
        .Register<Scene2RemoveInstance>("scene2.remove_instance",1,"Remove instance")
        .Register<Scene2AddSprite>("scene2.add_sprite",1,"Add sprite")
        .Register<Scene2SetSprite>("scene2.set_sprite",1,"Set sprite")
        .Register<Scene2Compatible>("scene2.compatible",1,"Edit object")
        .Register<Scene2Set>("scene2.set",1,"Set field")
        .Register<Scene2Revert>("scene2.revert",1,"Revert field")
        .Register<Scene2BreakLink>("scene2.break_link",1,"Break Link")
        .Register<Scene2Instantiate>("scene2.instantiate",1,"Instantiate")
        .Register<Scene2DuplicateInstance>("scene2.duplicate",1,"Duplicate instance")
        .Register<Scene2Update>("scene2.update",1,"Accept source update");

    internal static CommandResult<Scene2> Apply(Scene2 scene, Action<JsonObject,Scene2Resolution> change)
    {
        try
        {
            var resolved=Scene2Resolver.Resolve(scene);
            var json=Scene2Write.Scene(scene).AsObject();
            change(json,resolved);
            Prune(json);
            return CommandResult<Scene2>.Done(new Scene2Format().Read(json.ToJsonString()));
        }
        catch(Exception e) when(e is DocumentFormatException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {return CommandResult<Scene2>.Refused(Issue.Error(e.Message));}
    }
    internal static void Refuse(string why)=>throw new ArgumentException(why);
    internal static JsonObject Instance(JsonObject json,string key) => json["instances"]?.AsArray().OfType<JsonObject>().SingleOrDefault(i=>Text(i,"key")==key)
        ?? throw new ArgumentException("No instance has key "+key+".");
    internal static string Text(JsonNode node,string field)=>node[field]!.GetValue<string>();
    internal static JsonArray Array(JsonObject node,string key)
    {if(node[key] is null)node[key]=new JsonArray();return node[key]!.AsArray();}
    internal static JsonArray Order(Scene2Resolution resolved)=>new(resolved.Objects.Select(o=>(JsonNode?)JsonValue.Create(o.Object.Id)).ToArray());
    internal static void Prune(JsonObject json)
    {
        var used=json["instances"]?.AsArray().Select(i=>Text(i!,"base")).ToHashSet(StringComparer.Ordinal)??[];
        if(json["bases"] is JsonArray bases)foreach(var b in bases.ToArray())if(!used.Contains(Text(b!,"id")))bases.Remove(b);
    }
    internal static JsonObject ObjectJson(Scene2Object item)
    {
        var only=new Scene2(SceneSpace.ThreeD,[item],[],[],null);
        // Shape writer only: validity is checked on the complete destination scene, with its real profile and links.
        return Scene2Write.Scene(only)["objects"]![0]!.AsObject().DeepClone().AsObject();
    }
    internal static void Base(JsonObject json,string id,JsonElement definition)
    {
        var parsed=new PrefabDefinitionFormat().Read(definition.GetRawText());
        var revision="sha256:"+Convert.ToHexStringLower(SHA256.HashData(Scene2Jcs.Canonicalize(parsed.Original)));
        var bases=Array(json,"bases");
        if(bases.OfType<JsonObject>().SingleOrDefault(b=>Text(b,"id")==id) is {} old)
        {if(Text(old,"revision")!=revision||!Scene2Jcs.Canonicalize(old["definition"]!.ToJsonString()).AsSpan().SequenceEqual(Scene2Jcs.Canonicalize(parsed.Original)))Refuse("Base id already names different accepted content.");return;}
        bases.Add(new JsonObject{{"id",id},{"revision",revision},{"definition",Scene2Write.Definition(parsed)}});
    }
    internal static void Set(JsonObject json,Scene2Resolution resolved,string id,string field,JsonElement? value)
    {
        if(!resolved.Origins.TryGetValue(id,out var origin))Refuse("Unknown object "+id+".");
        ValidateField(field,value);
        if(origin!.Kind==Scene2OriginKind.PrefabNode)
        {
            var instance=Instance(json,origin.InstanceKey!);var edits=instance["overrides"]!.AsArray();
            foreach(var old in edits.ToArray())if(Text(old!,"node")==origin.NodeKey&&Text(old!,"field")==field)edits.Remove(old);
            var edit=new JsonObject{{"node",origin.NodeKey},{"field",field},{"op",value is null?"unset":"set"}};
            if(value is {} v)edit["value"]=JsonNode.Parse(v.GetRawText());edits.Add(edit);return;
        }
        var target=origin.Kind==Scene2OriginKind.Placement?Instance(json,origin.InstanceKey!)["placement"]!.AsObject()
            :json["objects"]!.AsArray().OfType<JsonObject>().Single(o=>Text(o,"id")==id);
        if(origin.Kind==Scene2OriginKind.Placement&&field is not("/position" or "/rotation" or "/scale"))Refuse("An instance placement owns only its transform; Break Link to edit content.");
        var segments=field.Split('/').Skip(1).Select(Unescape).ToArray();JsonObject parent=target;
        foreach(var segment in segments[..^1])
        {if(parent[segment] is null)Refuse("Field requires existing "+segment+" metadata.");parent=parent[segment]!.AsObject();}
        if(value is null)parent.Remove(segments[^1]);
        else if(segments[0]=="references")parent[segments[^1]]=value.Value.GetProperty("object").GetString();
        else parent[segments[^1]]=JsonNode.Parse(value.Value.GetRawText());
    }
    internal static string Unescape(string part)
    {for(var i=0;i<part.Length;i++)if(part[i]=='~'&&(++i>=part.Length||part[i] is not('0' or '1')))Refuse("Invalid JSON Pointer escape.");return part.Replace("~1","/").Replace("~0","~");}
    private static void ValidateField(string field,JsonElement? value)
    {
        var parts=field.Split('/');
        var scalar=field is "/position" or "/rotation" or "/scale" or "/asset";
        var named=parts.Length==3&&parts[0]==""&&parts[1] is "properties" or "references";
        var sprite=field is "/sprite/size" or "/sprite/pivot" or "/sprite/order" or "/sprite/filter";
        var body=field is "/body" or "/body/motion" or "/body/shape" or "/body/massKg" or "/body/friction";
        if(!scalar&&!named&&!sprite&&!body)Refuse("Unsupported field. Structural instance edits require Break Link.");
        foreach(var part in parts.Skip(1))_=Unescape(part);
        if(value is null&&field is not("/asset" or "/body" or "/body/massKg")&&!named)Refuse("This field cannot be unset.");
        if(value is {} v&&v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)Refuse("Explicit null is not an unset operation.");
        if(named&&parts[1]=="references"&&value is {} reference)
        {if(reference.ValueKind!=JsonValueKind.Object||reference.EnumerateObject().Count()!=1||!reference.TryGetProperty("object",out var target)||target.ValueKind!=JsonValueKind.String)Refuse("A reference set requires exactly {object:sceneId}.");}
    }
}

internal sealed record Scene2Set(string Id,string Field,JsonElement? Value):ICommand<Scene2>
{
    public JsonElement? Value {get;}=Value?.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(j,r)=>Scene2Commands.Set(j,r,Id,Field,Value));
}
internal sealed record Scene2Revert(string Instance,string Node,string Field):ICommand<Scene2>
{
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(j,r)=>
    {
        var instance=Scene2Commands.Instance(j,Instance);
        if(instance["nodeIds"]![Node] is null)Scene2Commands.Refuse("Unknown accepted node.");
        var edits=instance["overrides"]!.AsArray();var old=edits.SingleOrDefault(e=>Scene2Commands.Text(e!,"node")==Node&&Scene2Commands.Text(e!,"field")==Field);
        if(old is null)Scene2Commands.Refuse("No explicit override at that field.");edits.Remove(old);
    });
}
internal sealed record Scene2BreakLink(string Instance):ICommand<Scene2>
{
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(j,r)=>
    {
        var instance=Scene2Commands.Instance(j,Instance);j["objectOrder"]=Scene2Commands.Order(r);
        foreach(var item in r.Objects.Where(o=>r.Origins[o.Object.Id].InstanceKey==Instance))j["objects"]!.AsArray().Add(Scene2Commands.ObjectJson(item));
        j["instances"]!.AsArray().Remove(instance);
    });
}
internal sealed record Scene2Instantiate(string Key,string BaseId,JsonElement Definition,JsonElement Placement,JsonElement NodeIds,JsonElement Bindings):ICommand<Scene2>
{
    public JsonElement Definition {get;}=Definition.Clone();public JsonElement Placement {get;}=Placement.Clone();
    public JsonElement NodeIds {get;}=NodeIds.Clone();public JsonElement Bindings {get;}=Bindings.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(j,r)=>
    {
        Scene2Commands.Base(j,BaseId,Definition);
        var definition=new PrefabDefinitionFormat().Read(Definition.GetRawText());
        var instance=new JsonObject{{"key",Key},{"source",definition.Guid},{"base",BaseId},{"placement",JsonNode.Parse(Placement.GetRawText())},
            {"nodeIds",JsonNode.Parse(NodeIds.GetRawText())},{"bindings",JsonNode.Parse(Bindings.GetRawText())},{"overrides",new JsonArray()}};
        Scene2Commands.Array(j,"instances").Add(instance);
        if(j["objectOrder"] is JsonArray order)
        {order.Add(Scene2Commands.Text(instance["placement"]!,"id"));foreach(var node in definition.Nodes)order.Add(instance["nodeIds"]![node.Key]!.GetValue<string>());}
    });
}
internal sealed record Scene2DuplicateInstance(string Instance,string NewKey,JsonElement IdMap):ICommand<Scene2>
{
    public JsonElement IdMap {get;}=IdMap.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(j,r)=>
    {
        var source=Scene2Commands.Instance(j,Instance);var ids=r.Objects.Where(o=>r.Origins[o.Object.Id].InstanceKey==Instance).Select(o=>o.Object.Id).ToArray();
        if(IdMap.ValueKind!=JsonValueKind.Object)Scene2Commands.Refuse("Expected a complete id map.");
        var pairs=IdMap.EnumerateObject().ToArray();
        if(pairs.Length!=ids.Length||pairs.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=pairs.Length||pairs.Any(p=>p.Value.ValueKind!=JsonValueKind.String)||ids.Any(id=>!IdMap.TryGetProperty(id,out _)))Scene2Commands.Refuse("Map must exactly cover the whole instance.");
        var map=pairs.ToDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal);
        if(map.Values.Distinct(StringComparer.Ordinal).Count()!=map.Count||map.Values.Any(r.Origins.ContainsKey))Scene2Commands.Refuse("Copied ids must be distinct and unused.");
        string Remap(string id)=>map.GetValueOrDefault(id,id);
        var copy=source.DeepClone().AsObject();copy["key"]=NewKey;copy["placement"]!["id"]=Remap(Scene2Commands.Text(source["placement"]!,"id"));
        if(copy["placement"]!["parent"] is {} parent)copy["placement"]!["parent"]=Remap(parent.GetValue<string>());
        foreach(var pair in copy["nodeIds"]!.AsObject().ToArray())copy["nodeIds"]![pair.Key]=Remap(pair.Value!.GetValue<string>());
        foreach(var pair in copy["bindings"]!.AsObject().ToArray())copy["bindings"]![pair.Key]=Remap(pair.Value!.GetValue<string>());
        // Accepted internal node references resolve through the copied map. Remap explicit scene-id overrides only.
        foreach(var edit in copy["overrides"]!.AsArray())if(Scene2Commands.Text(edit!,"field").StartsWith("/references/",StringComparison.Ordinal)&&edit!["value"]?["object"] is {} target)edit["value"]!["object"]=Remap(target.GetValue<string>());
        var instances=j["instances"]!.AsArray();instances.Insert(instances.IndexOf(source)+1,copy);
        var order=Scene2Commands.Order(r);var positions=r.Objects.Select((item,index)=>(item.Object.Id,index)).ToDictionary(p=>p.Id,p=>p.index,StringComparer.Ordinal);var last=ids.Max(id=>positions[id]);
        foreach(var id in r.Objects.Select(o=>o.Object.Id).Where(map.ContainsKey))order.Insert(++last,JsonValue.Create(map[id]));j["objectOrder"]=order;
    });
}
internal sealed record Scene2Update(string Instance,string BaseId,JsonElement Definition,JsonElement NewNodeIds):ICommand<Scene2>
{
    public JsonElement Definition {get;}=Definition.Clone();public JsonElement NewNodeIds {get;}=NewNodeIds.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(j,r)=>
    {
        var instance=Scene2Commands.Instance(j,Instance);var old=scene.Bases.Single(b=>b.Id==Scene2Commands.Text(instance,"base")).Definition;
        var next=new PrefabDefinitionFormat().Read(Definition.GetRawText());
        if(next.Guid!=old.Guid||next.Space!=old.Space)Scene2Commands.Refuse("Source identity or space changed.");
        var oldKeys=old.Nodes.Select(n=>n.Key).ToHashSet(StringComparer.Ordinal);var nextKeys=next.Nodes.Select(n=>n.Key).ToHashSet(StringComparer.Ordinal);
        var oldSlots=old.Slots.Select(s=>s.Key).ToHashSet(StringComparer.Ordinal);var nextSlots=next.Slots.Select(s=>s.Key).ToHashSet(StringComparer.Ordinal);
        foreach(var slot in oldSlots.Except(nextSlots))if(instance["bindings"]![slot] is not null||old.Slots.Single(s=>s.Key==slot).Required)Scene2Commands.Refuse("Source deleted a bound or required slot.");
        foreach(var edit in instance["overrides"]!.AsArray())
        {
            var node=Scene2Commands.Text(edit!,"node");if(!nextKeys.Contains(node))Scene2Commands.Refuse("Source deleted an overridden node.");
            var field=Scene2Commands.Text(edit!,"field");
            if(field.StartsWith("/properties/",StringComparison.Ordinal)&&Scene2Commands.Text(edit!,"op")=="set")
            {
                var name=Scene2Commands.Unescape(field[12..]);var target=next.Nodes.Single(n=>n.Key==node);
                if(target.Properties.TryGetValue(name,out var inherited)&&!Compatible(inherited.ValueKind,edit!["value"]!.GetValueKind()))Scene2Commands.Refuse("Local property type conflicts with the updated source.");
            }
        }
        if(NewNodeIds.ValueKind!=JsonValueKind.Object)Scene2Commands.Refuse("Expected new node id map.");
        var added=nextKeys.Except(oldKeys).ToHashSet(StringComparer.Ordinal);var supplied=NewNodeIds.EnumerateObject().ToArray();
        if(supplied.Length!=added.Count||supplied.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=supplied.Length||supplied.Any(p=>!added.Contains(p.Name)||p.Value.ValueKind!=JsonValueKind.String))Scene2Commands.Refuse("Map must name exactly the new source nodes.");
        if(supplied.Select(p=>p.Value.GetString()).Distinct(StringComparer.Ordinal).Count()!=supplied.Length||supplied.Any(p=>r.Origins.ContainsKey(p.Value.GetString()!)))Scene2Commands.Refuse("New node ids must be distinct and unused, including ids of deleted nodes.");
        var nodeIds=instance["nodeIds"]!.AsObject();var removed=oldKeys.Except(nextKeys).Select(k=>nodeIds[k]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        // External parenting, references, bindings and retained override targets must not be silently dropped.
        foreach(var item in r.Objects.Where(o=>!removed.Contains(o.Object.Id)))
            if(item.Object.Parent is {} parent&&removed.Contains(parent)||item.Object.References.Values.Any(removed.Contains))Scene2Commands.Refuse("Source deleted a referenced or parent node; clean up or Break Link first.");
        foreach(var key in oldKeys.Except(nextKeys))nodeIds.Remove(key);
        foreach(var p in supplied)nodeIds.Add(p.Name,p.Value.GetString());
        Scene2Commands.Base(j,BaseId,Definition);instance["base"]=BaseId;
        var order=new LinkedList<string>(r.Objects.Select(o=>o.Object.Id).Where(id=>!removed.Contains(id)));
        var positions=new Dictionary<string,LinkedListNode<string>>(StringComparer.Ordinal);
        for(var cursor=order.First;cursor is not null;cursor=cursor.Next)positions.Add(cursor.Value,cursor);
        var predecessor=positions[Scene2Commands.Text(instance["placement"]!,"id")];
        foreach(var node in next.Nodes)
        {
            var id=nodeIds[node.Key]!.GetValue<string>();
            if(added.Contains(node.Key))positions.Add(id,order.AddAfter(predecessor,id));
            predecessor=positions[id];
        }
        j["objectOrder"]=new JsonArray(order.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray());
    });
    private static bool Compatible(JsonValueKind a,JsonValueKind b)=>a==b||a is JsonValueKind.True or JsonValueKind.False&&b is JsonValueKind.True or JsonValueKind.False;
}
