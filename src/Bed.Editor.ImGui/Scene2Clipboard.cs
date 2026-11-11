using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
namespace Bed.Editor.ImGui;

// One workspace's immutable source snapshot. No operating-system clipboard or source discovery.
internal sealed class Scene2Clipboard
{
    private Scene2? snapshot;private string[] included=[];
    internal bool HasObjects=>snapshot is not null&&included.Length>0;
    internal void Clear(){snapshot=null;included=[];}
    internal (bool Ok,string Message) Copy(EditorModel<Scene2> editor)
    {
        var source=editor.Session.Current;var resolved=Scene2ViewProjection.For(source).Resolved;var byId=resolved.Objects.ToDictionary(o=>o.Object.Id,StringComparer.Ordinal);
        if(editor.Selection.Count==0||editor.Selection.Any(id=>!byId.ContainsKey(id)))return(false,"Select existing objects to copy first.");
        var children=resolved.Objects.Where(o=>o.Object.Parent is not null).GroupBy(o=>o.Object.Parent!,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Select(o=>o.Object.Id).ToArray(),StringComparer.Ordinal);
        var instances=resolved.Origins.Where(p=>p.Value.InstanceKey is not null).GroupBy(p=>p.Value.InstanceKey!,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Select(p=>p.Key).ToArray(),StringComparer.Ordinal);
        var expandedInstances=new HashSet<string>(StringComparer.Ordinal);var chosen=new HashSet<string>(StringComparer.Ordinal);var queue=new Queue<string>(editor.Selection);
        while(queue.TryDequeue(out var id))
        {
            if(!chosen.Add(id))continue;
            if(resolved.Origins[id].InstanceKey is {} instance&&expandedInstances.Add(instance))foreach(var part in instances[instance])if(!chosen.Contains(part))queue.Enqueue(part);
            if(children.TryGetValue(id,out var descendants))foreach(var child in descendants)if(!chosen.Contains(child))queue.Enqueue(child);
        }
        snapshot=source;included=resolved.Objects.Select(o=>o.Object.Id).Where(chosen.Contains).ToArray();return(true,$"Copied {included.Length} effective object(s), retaining whole linked pieces and descendants inside this project.");
    }
    internal (bool Ok,string Message) Paste(EditorModel<Scene2> editor)
    {
        if(snapshot is not {} source||included.Length==0)return(false,"Copy scene-v2 objects in this project first.");
        if(editor.FocusedOn is not null)return(false,"Step out of focus before pasting objects.");
        if(editor.Playing is not null)return(false,"Stop Play before pasting objects.");
        var destination=editor.Session.Current;var revision=editor.Session.Revision;
        if(source.Space!=destination.Space)return(false,"Cannot paste between 3D and flat coordinate spaces.");
        try
        {
            var sourceView=Scene2ViewProjection.For(source);var chosen=included.ToHashSet(StringComparer.Ordinal);var reserved=sourceView.ById.Keys.Concat(Scene2ViewProjection.For(destination).ById.Keys).ToHashSet(StringComparer.Ordinal);var map=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var id in included){var prefix=id[..Math.Min(100,id.Length)]+"_copy_";var number=1;while(!reserved.Add(prefix+number))number++;map.Add(id,prefix+number);}
            string Remap(string id)=>map.GetValueOrDefault(id,id);
            var fragment=JsonNode.Parse(new Scene2Format().Write(source))!.AsObject();
            foreach(var field in new[]{"objects","bases","instances"})if(fragment[field] is null)fragment[field]=new JsonArray();
            void Object(JsonNode item)
            {
                item["id"]=Remap(item["id"]!.GetValue<string>());
                if(item["parent"] is {} parent)item["parent"]=Remap(parent.GetValue<string>());
                if(item["references"] is JsonObject refs)foreach(var pair in refs.ToArray())refs[pair.Key]=Remap(pair.Value!.GetValue<string>());
            }
            var objects=fragment["objects"]!.AsArray();foreach(var item in objects.ToArray())if(!chosen.Contains(item!["id"]!.GetValue<string>()))objects.Remove(item);else Object(item!);
            var instances=fragment["instances"]!.AsArray();var used=new HashSet<string>(StringComparer.Ordinal);
            foreach(var instance in instances.ToArray())
            {
                if(!chosen.Contains(instance!["placement"]!["id"]!.GetValue<string>())){instances.Remove(instance);continue;}
                used.Add(instance["base"]!.GetValue<string>());instance["key"]=Guid.NewGuid().ToString("N");Object(instance["placement"]!);
                foreach(var field in new[]{"nodeIds","bindings"})foreach(var pair in instance[field]!.AsObject().ToArray())instance[field]![pair.Key]=Remap(pair.Value!.GetValue<string>());
                foreach(var edit in instance["overrides"]!.AsArray())if(edit!["field"]!.GetValue<string>().StartsWith("/references/",StringComparison.Ordinal)&&edit["value"]?["object"] is {} target)edit["value"]!["object"]=Remap(target.GetValue<string>());
            }
            var bases=fragment["bases"]!.AsArray();var baseMap=new Dictionary<string,string>(StringComparer.Ordinal);var emitted=new HashSet<string>(StringComparer.Ordinal);
            foreach(var accepted in bases.ToArray())
            {
                var id=accepted!["id"]!.GetValue<string>();if(!used.Contains(id)){bases.Remove(accepted);continue;}
                var from=source.Bases.Single(b=>b.Id==id);var same=destination.Bases.FirstOrDefault(b=>b.Revision==from.Revision&&b.Definition.Guid==from.Definition.Guid);
                var copy=same?.Id??"clipboard_"+Guid.NewGuid().ToString("N");baseMap.Add(id,copy);accepted["id"]=copy;if(!emitted.Add(copy))bases.Remove(accepted);
            }
            foreach(var instance in instances)instance!["base"]=baseMap[instance["base"]!.GetValue<string>()];
            var copied=included.Select(Remap).ToArray();fragment["objectOrder"]=new JsonArray(copied.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray());
            var paste=Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment}));var result=paste.Apply(destination);if(!result.Ok)return(false,string.Join(" ",result.Issues.Select(i=>i.Message)));
            var move=SceneTranslation.World(Scene2ViewProjection.For(result.Document!).Flat,copied,Vector3.UnitX);if(!move.Valid)return(false,string.Join(" ",move.Issues.Select(i=>i.Message)));
            // Bake the shared planner's final root positions into the fresh fragment. Replaying one move
            // per root would repeatedly serialize the complete scene for large clipboard selections.
            var authored=objects.Concat(instances.Select(i=>i!["placement"])).ToDictionary(i=>i!["id"]!.GetValue<string>(),i=>i!,StringComparer.Ordinal);
            foreach(var command in move.Commands)
            {
                if(command is not MoveObject shift||!authored.TryGetValue(shift.Id,out var item))return(false,"Clipboard translation did not identify an authored copied root.");
                item["position"]=new JsonArray(shift.Position.X,shift.Position.Y,shift.Position.Z);
            }
            paste=Scene2Kit.Command("paste",JsonSerializer.SerializeToElement(new{fragment}));
            if(!editor.ExecuteAt(revision,[paste]))return(false,string.Join(" ",(editor.Refused.Count>0?editor.Refused:editor.Session.LastIssues).Select(i=>i.Message)));
            editor.Select(copied);
            return(true,$"Pasted {copied.Length} effective object(s), moved 1 metre along world X.");
        }
        catch(Exception e)when(e is DocumentFormatException or JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {return(false,"Paste refused: "+e.Message);}
    }
}
