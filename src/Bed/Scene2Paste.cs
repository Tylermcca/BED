using System.Text.Json;
using System.Text.Json.Nodes;
namespace Bed;

// Journal payload contains already allocated identities and accepted snapshots, never live sources.
internal sealed record Scene2Paste(JsonElement Fragment):ICommand<Scene2>
{
    public JsonElement Fragment{get;}=Fragment.Clone();
    public CommandResult<Scene2> Apply(Scene2 scene)=>Scene2Commands.Apply(scene,(json,resolved)=>
    {
        _=Scene2Jcs.Canonicalize(Fragment);
        var reader=new Scene2Read(Fragment,"/fragment");reader.Only("kind","version","space","objects","bases","instances","objectOrder","physics");
        if(reader.Get("kind").Text()!="scene"||reader.Get("version").Value.ValueKind!=JsonValueKind.Number||!reader.Get("version").Value.TryGetInt32(out var version)||version!=2||reader.Get("space").Text()!=json["space"]!.GetValue<string>())Scene2Commands.Refuse("Paste needs the same scene-v2 coordinate space.");
        if(reader.Has("physics"))ScenePhysicsRules.CheckPhysics(ScenePhysicsRules.ReadPhysics(reader.Get("physics")),reader.Get("space").Space(),"/fragment/physics");
        foreach(var name in new[]{"objects","bases","instances","objectOrder"})if(reader.Get(name).Value.ValueKind!=JsonValueKind.Array)Scene2Commands.Refuse("Paste requires explicit "+name+" array.");
        var fragment=JsonNode.Parse(Fragment.GetRawText())!.AsObject();
        if(fragment["objects"]!.AsArray().Count+fragment["instances"]!.AsArray().Count==0)Scene2Commands.Refuse("Paste requires copied objects or instances.");
        static string Text(JsonNode? node,string field)
        {if(node is JsonObject obj&&obj[field] is JsonValue value&&value.TryGetValue<string>(out var text)&&text is not null)return text;throw new ArgumentException("Paste requires an object with a string "+field+".");}
        var used=fragment["instances"]!.AsArray().Select(i=>Text(i,"base")).ToHashSet(StringComparer.Ordinal);var baseIds=new HashSet<string>(StringComparer.Ordinal);
        foreach(var accepted in fragment["bases"]!.AsArray())
        {
            var id=Text(accepted,"id");if(!used.Contains(id)||!baseIds.Add(id))Scene2Commands.Refuse("Paste bases must be distinct and used by copied instances.");
            var existing=Scene2Commands.Array(json,"bases").SingleOrDefault(b=>Scene2Commands.Text(b!,"id")==id);
            if(existing is null)Scene2Commands.Array(json,"bases").Add(accepted!.DeepClone());
            else if(!Scene2Jcs.Canonicalize(existing.ToJsonString()).SequenceEqual(Scene2Jcs.Canonicalize(accepted!.ToJsonString())))Scene2Commands.Refuse("Paste collides with a different accepted base.");
        }
        foreach(var name in new[]{"objects","instances"})foreach(var item in fragment[name]!.AsArray())Scene2Commands.Array(json,name).Add(item?.DeepClone());
        var order=Scene2Commands.Order(resolved);foreach(var id in fragment["objectOrder"]!.AsArray())order.Add(id?.DeepClone());json["objectOrder"]=order;
        // Scene2Commands finishes with the complete strict reader/resolver, including graph/order and expansion limits.
    });
}
