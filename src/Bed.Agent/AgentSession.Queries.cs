using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;

namespace Bed.Agent;

public sealed partial class AgentSession
{
    // A missing input is an unanswered query, never evidence of zero results.
    internal static JsonObject Query(Func<JsonObject> read)
    {
        JsonObject result;
        try { result=read(); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { result=Fail(e.Message,"Supply the missing input and try the query again."); }
        if(result["error"] is JsonValue value && value.TryGetValue<string>(out var why) && !why.StartsWith("couldn't check:",StringComparison.Ordinal))
            result["error"]="couldn't check: "+why;
        return result;
    }
    private static bool CannotResolve(ReferenceOutcome outcome)=>outcome is ReferenceOutcome.NotLoaded or ReferenceOutcome.NeedsScope or ReferenceOutcome.Ambiguous;
    private JsonObject? ReferenceInputs()
    {
        using var map=JsonDocument.Parse(Map!.Text);
        IEnumerable<string> Files(JsonElement node)
        {
            if(node.ValueKind==JsonValueKind.Object)
                foreach(var property in node.EnumerateObject())
                    if(property.Name=="file"&&property.Value.ValueKind==JsonValueKind.String)yield return property.Value.GetString()!;
                    else foreach(var name in Files(property.Value))yield return name;
            else if(node.ValueKind==JsonValueKind.Array)
                foreach(var item in node.EnumerateArray())foreach(var name in Files(item))yield return name;
        }
        foreach(var name in Files(map.RootElement.GetProperty("references")).Distinct(StringComparer.Ordinal))
            if(Open(name,out _) is {} failure)return failure;
        foreach(var link in Map.Links(Project))
            if(CannotResolve(link.Result.Outcome))return Fail($"{link.At.File} at {link.At.Pointer}: {link.Result.Note ?? link.Result.Outcome.ToString()}","Resolve the missing or ambiguous reference input before asking for a complete reference list.");
        return null;
    }
}
