using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed;

// Reuses v1's operation policy, but persists changes to v2 ownership and keeps every sprite/base/order field.
internal sealed record Scene2Compatible(string Action,JsonElement Arguments):ICommand<Scene2>
{
    public JsonElement Arguments {get;}=Arguments.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)
    {
        ICommand<Scene2>? resolvedCommand=null;
        var result=Scene2Commands.Apply(scene,(json,resolved)=>
        {
            var catalog=SceneKit.Catalog();var command=catalog.FromEntry(JournalEntry.Do("scene."+Action,1,Arguments));
            var flat=new Scene([..resolved.Objects.Select(o=>o.Object)]);var changed=command.Apply(flat);
            if(!changed.Ok)Scene2Commands.Refuse(string.Join(" ",changed.Issues.Select(i=>i.Message)));
            var validation=new SceneValidator().Validate(changed.Document!);if(validation.HasErrors())Scene2Commands.Refuse(string.Join(" ",validation.Select(i=>i.Message)));
            if(command is RenameObject rename){Rename(json,resolved,rename);return;}
            if(command is AddObject add)
            {
                json["objects"]!.AsArray().Add(Scene2Commands.ObjectJson(new(add.Object,null)));
                if(json["objectOrder"] is JsonArray order)order.Add(add.Object.Id);return;
            }
            if(command is DuplicateGroup duplicate)
            {
                if(duplicate.Ids.Any(id=>resolved.Origins[id].Kind!=Scene2OriginKind.Ordinary))Scene2Commands.Refuse("Duplicate the whole instance, or Break Link before copying individual linked objects.");
                if(changed.Resolved is not DuplicateGroup {Map:{} map})Scene2Commands.Refuse("Duplicate did not allocate an explicit id map.");
                var recorded=(DuplicateGroup)changed.Resolved!;var copied=changed.Document!.Objects.Skip(flat.Objects.Length).ToArray();
                foreach(var copy in copied)
                {
                    var original=recorded.Map!.Single(p=>p.Value==copy.Id).Key;var sprite=resolved.Objects.Single(o=>o.Object.Id==original).Sprite;
                    json["objects"]!.AsArray().Add(Scene2Commands.ObjectJson(new Scene2Object(copy,sprite){Body=resolved.Objects.Single(o=>o.Object.Id==original).Body}));
                    if(json["objectOrder"] is JsonArray order)order.Add(copy.Id);
                }
                resolvedCommand=new Scene2Compatible(Action,catalog.ToEntry(recorded).Args!.Value);return;
            }
            if(command is RemoveObject remove)
            {
                if(resolved.Origins[remove.Id].Kind!=Scene2OriginKind.Ordinary)Scene2Commands.Refuse("Delete the whole instance, or Break Link before deleting its parts.");
                var retained=changed.Document!.Objects.Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
                if(resolved.Objects.Any(o=>!retained.Contains(o.Object.Id)&&resolved.Origins[o.Object.Id].Kind!=Scene2OriginKind.Ordinary))Scene2Commands.Refuse("Deletion would remove part of an instance; Break Link first.");
                var objects=json["objects"]!.AsArray();foreach(var item in objects.ToArray())if(!retained.Contains(Scene2Commands.Text(item!,"id")))objects.Remove(item);
                if(json["objectOrder"] is JsonArray order)foreach(var item in order.ToArray())if(!retained.Contains(item!.GetValue<string>()))order.Remove(item);return;
            }
            // Only these typed v1 operations are translated; unknown future commands cannot flatten an instance.
            var fields=command switch
            {
                MoveObject o=>new[]{(o.Id,"/position",(JsonElement?)Vector(o.Position.X,o.Position.Y,o.Position.Z))},
                RotateObject o=>new[]{(o.Id,"/rotation",(JsonElement?)Vector(o.Rotation.X,o.Rotation.Y,o.Rotation.Z,o.Rotation.W))},
                ScaleObject o=>new[]{(o.Id,"/scale",(JsonElement?)Vector(o.Scale.X,o.Scale.Y,o.Scale.Z))},
                SetAsset o=>new[]{(o.Id,"/asset",o.Asset is null?null:(JsonElement?)JsonSerializer.SerializeToElement(o.Asset))},
                SetSceneProperty o=>new[]{(o.Id,"/properties/"+Escape(o.Name),o.Value)},
                SetReference o=>new[]{(o.Id,"/references/"+Escape(o.Name),o.Target is null?null:(JsonElement?)JsonSerializer.SerializeToElement(new{ @object=o.Target}))},
                SetParent=>[],
                _=>throw new ArgumentException("Unsupported compatible action.")
            };
            if(command is SetParent parent)
            {
                var origin=resolved.Origins[parent.Id];if(origin.Kind==Scene2OriginKind.PrefabNode)Scene2Commands.Refuse("Break Link before changing an internal parent.");
                var target=origin.Kind==Scene2OriginKind.Placement?Scene2Commands.Instance(json,origin.InstanceKey!)["placement"]!
                    :json["objects"]!.AsArray().Single(o=>Scene2Commands.Text(o!,"id")==parent.Id)!;
                if(parent.Parent is null)target.AsObject().Remove("parent");else target["parent"]=parent.Parent;
            }
            foreach(var field in fields)Scene2Commands.Set(json,resolved,field.Item1,field.Item2,field.Item3);
        });
        return result.Ok&&resolvedCommand is not null?result with{Resolved=resolvedCommand}:result;
    }
    private static JsonElement Vector(params float[] values)=>JsonSerializer.SerializeToElement(values);
    private static string Escape(string name)=>name.Replace("~","~0").Replace("/","~1");
    private static void Rename(JsonObject json,Scene2Resolution resolved,RenameObject rename)
    {
        if(resolved.Origins[rename.Id].Kind==Scene2OriginKind.PrefabNode)Scene2Commands.Refuse("Break Link before renaming an internal node.");
        string In(string id)=>id==rename.Id?rename.NewId:id;
        foreach(var item in json["objects"]!.AsArray())
        {
            item!["id"]=In(Scene2Commands.Text(item,"id"));if(item["parent"] is {} parent)item["parent"]=In(parent.GetValue<string>());
            foreach(var pair in item["references"]!.AsObject().ToArray())item["references"]![pair.Key]=In(pair.Value!.GetValue<string>());
        }
        if(json["instances"] is JsonArray instances)foreach(var item in instances)
        {
            var placement=item!["placement"]!;placement["id"]=In(Scene2Commands.Text(placement,"id"));if(placement["parent"] is {} parent)placement["parent"]=In(parent.GetValue<string>());
            foreach(var pair in item["bindings"]!.AsObject().ToArray())item["bindings"]![pair.Key]=In(pair.Value!.GetValue<string>());
            foreach(var edit in item["overrides"]!.AsArray())if(Scene2Commands.Text(edit!,"field").StartsWith("/references/",StringComparison.Ordinal)&&edit!["value"]?["object"] is {} target)edit["value"]!["object"]=In(target.GetValue<string>());
        }
        if(json["objectOrder"] is JsonArray order)for(var i=0;i<order.Count;i++)order[i]=In(order[i]!.GetValue<string>());
    }
}
