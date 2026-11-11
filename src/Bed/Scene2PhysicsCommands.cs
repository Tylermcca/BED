using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace Bed;

// The missing property is distinct from explicit null, including during journal replay.
internal sealed record Scene2SetPhysics(JsonElement Physics=default):ICommand<Scene2>
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Physics {get;}=Physics.ValueKind==JsonValueKind.Undefined?default:Physics.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        if(Physics.ValueKind==JsonValueKind.Undefined)json.Remove("physics");
        else {_=Scene2Jcs.Canonicalize(Physics);_=ScenePhysicsRules.ReadPhysics(new(Physics,"/physics"));json["physics"]=JsonNode.Parse(Physics.GetRawText());}
    });
}
internal sealed record Scene2EditBodies(JsonElement Edits):ICommand<Scene2>
{
    public JsonElement Edits {get;}=Edits.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        _=Scene2Jcs.Canonicalize(Edits);
        if(Edits.ValueKind!=JsonValueKind.Array||Edits.GetArrayLength() is <1 or >20480)Scene2Commands.Refuse("Use 1..20480 body edits, at most five addresses per object.");
        var targets=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        foreach(var value in Edits.EnumerateArray())
        {
            var reader=new Scene2Read(value,"/edits");reader.Only("id","field","value","revert");
            var id=reader.Get("id").Text();var field=reader.Get("field").Text();
            if(field is not("/body" or "/body/motion" or "/body/shape" or "/body/massKg" or "/body/friction"))Scene2Commands.Refuse("Use an approved body address.");
            if(!targets.TryGetValue(id,out var fields)){fields=new(StringComparer.Ordinal);targets.Add(id,fields);}
            if(targets.Count>4096||fields.Count>=5||!fields.Add(field)||fields.Count>1&&(field=="/body"||fields.Contains("/body")))Scene2Commands.Refuse("Body edits repeat or overlap an address, or exceed 4096 objects / five fields.");
            if(!resolved.Origins.TryGetValue(id,out var origin))Scene2Commands.Refuse("Unknown object "+id+".");
            if(reader.Has("revert"))
            {
                if(reader.Get("revert").Value.ValueKind!=JsonValueKind.True||reader.Has("value"))Scene2Commands.Refuse("Revert requires true and no value.");
                if(origin!.Kind!=Scene2OriginKind.PrefabNode)Scene2Commands.Refuse("Only inherited prefab bodies can Revert.");
                var overrides=Scene2Commands.Instance(json,origin.InstanceKey!)["overrides"]!.AsArray();
                var old=overrides.SingleOrDefault(e=>Scene2Commands.Text(e!,"node")==origin.NodeKey&&Scene2Commands.Text(e!,"field")==field);
                if(old is null)Scene2Commands.Refuse("No local body value at that address.");overrides.Remove(old);
            }
            else
            {
                if(origin!.Kind==Scene2OriginKind.PrefabNode)
                {
                    var prior=Scene2Commands.Instance(json,origin.InstanceKey!)["overrides"]!.AsArray().Where(e=>Scene2Commands.Text(e!,"node")==origin.NodeKey).Select(e=>Scene2Commands.Text(e!,"field"));
                    if(prior.Any(address=>address!=field&&(field=="/body"&&address.StartsWith("/body/",StringComparison.Ordinal)||field.StartsWith("/body/",StringComparison.Ordinal)&&address=="/body")))
                        Scene2Commands.Refuse("Body overrides overlap. Revert body changes before switching between whole-body and child-field ownership.");
                }
                Scene2Commands.Set(json,resolved,id,field,reader.Has("value")?reader.Get("value").Value:null);
            }
        }
        // Scene2Commands.Apply parses and validates only this complete result, never an intermediate motion/mass state.
    });
}
