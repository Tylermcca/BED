using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Agent;
using Brutal.Authoring.Editor;
namespace Brutal.Authoring.Agent.Cli;

// Typed scene edits use the document host's validation/conflict save path. No frame or window is opened.
internal static class SceneCommandCall
{
    private static JsonObject Refused(string why)=>new(){["error"]=why,["next"]="Resolve the refusal before trying the scene command again; nothing was saved."};
    internal static JsonObject Run(AgentSession agent,IReadOnlyList<string> args,bool preview,bool save)
    {
        if(args.Count!=4)throw new ArgumentException("scene needs a file, an action and one JSON argument object.");
        var name=args[1];var action=args[2];
        if(action is not("add" or "move" or "duplicate" or "delete" or "rename" or "group"))
            throw new ArgumentException("Scene actions are add, move, duplicate, delete, rename and group.");
        if(agent.Project.Find(name) is null)return Refused("Choose an admitted scene file in the data folder.");
        if(agent.Project.ReadOnlyReason(name) is {} readOnly)return Refused(readOnly);
        var host=DocumentHost.For(agent.Project);
        if(!host.BindScene(name,"scene",out var why)||!host.OpenScene(name,out var entry,out why))return Refused(why);
        using var json=Parse(args[3]);var editor=entry!.Editor;bool ok;
        if(action is "duplicate" or "group")
        {
            var ids=Ids(json.RootElement);
            if(ids.Any(id=>editor.Session.Current.Find(id) is null))return Refused("Every selected ID must name an existing scene object.");
            editor.Select(ids);
            if(action=="group")
            {
                var grouped=SceneObjectOperations.Group(editor);ok=grouped.Ok;why=grouped.Message;
            }
            else ok=SceneObjectOperations.Duplicate(editor,new DuplicateGroup(ids),out why);
            if(!ok)return Refused(why);
        }
        else
        {
            var commandId=action=="delete"?"scene.remove":"scene."+action;
            ICommand<Scene> command;
            try{command=editor.Session.Catalog.FromEntry(JournalEntry.Do(commandId,1,json.RootElement.Clone()));}
            catch(DocumentFormatException error){throw new ArgumentException("Invalid scene arguments: "+error.Message,error);}
            ok=editor.Execute(command);
            if(!ok)return Refused(string.Join(" ",(editor.Refused.Count>0?editor.Refused:editor.Session.LastIssues).Select(i=>i.Message)));
        }
        var shouldSave=save&&!preview;
        if(shouldSave&&!host.SaveScene(name,out why))return Refused(why);
        return new()
        {
            ["file"]=name,["action"]=action,["preview"]=!shouldSave,["saved"]=shouldSave,
            ["candidate"]=JsonNode.Parse(editor.Session.TextToSave),
            ["selection"]=new JsonArray(editor.Selection.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray()),
            ["undoSteps"]=editor.Session.History.UndoCount,["journal"]=JsonNode.Parse(JournalFile.Write(editor.Session.Journal)),
            ["next"]=shouldSave?"Saved through the scene's checked save path.":"Nothing was written; repeat with --save to keep this candidate."
        };
    }
    private static JsonDocument Parse(string text)
    {
        try
        {
            var parsed=JsonDocument.Parse(text,new(){MaxDepth=64});
            if(parsed.RootElement.ValueKind!=JsonValueKind.Object){parsed.Dispose();throw new ArgumentException("Scene arguments must be a JSON object.");}
            return parsed;
        }
        catch(JsonException error){throw new ArgumentException("Scene arguments are not valid JSON: "+error.Message,error);}
    }
    private static string[] Ids(JsonElement args)
    {
        var properties=args.EnumerateObject().ToArray();
        if(properties.Length!=1||properties[0].Name!="ids"||properties[0].Value.ValueKind!=JsonValueKind.Array)
            throw new ArgumentException("Use exactly {\"ids\":[\"object_id\",...]} for duplicate or group.");
        var elements=properties[0].Value.EnumerateArray().ToArray();
        if(elements.Length==0||elements.Any(id=>id.ValueKind!=JsonValueKind.String||string.IsNullOrEmpty(id.GetString())))
            throw new ArgumentException("ids must contain one or more nonempty object IDs.");
        var result=elements.Select(id=>id.GetString()!).ToArray();
        if(result.Distinct(StringComparer.Ordinal).Count()!=result.Length)throw new ArgumentException("ids must not repeat an object ID.");
        return result;
    }
}
