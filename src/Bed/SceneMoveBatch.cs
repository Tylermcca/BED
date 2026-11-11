using System.Text.Json;

namespace Bed;

// Only the application cost changes: commands, order, journal entries and final validator stay unchanged.
internal static class SceneMoveBatch
{
    // Byte-identical to CommandCatalog's camel-case record/vector serializer; no new command or journal shape.
    internal static JsonElement Arguments(MoveObject move)
    {
        if(move.Id is null)throw new JsonException("MoveObject.Id is required.");
        if(!float.IsFinite(move.Position.X)||!float.IsFinite(move.Position.Y)||!float.IsFinite(move.Position.Z))
            throw new ArgumentException("a vector component isn't a finite number, so the command can't be recorded");
        using var bytes=new MemoryStream(128);
        using(var writer=new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();writer.WriteString("id"u8,move.Id);writer.WriteStartArray("position"u8);
            writer.WriteNumberValue(move.Position.X);writer.WriteNumberValue(move.Position.Y);writer.WriteNumberValue(move.Position.Z);
            writer.WriteEndArray();writer.WriteEndObject();
        }
        using var json=JsonDocument.Parse(bytes.GetBuffer().AsMemory(0,(int)bytes.Length));
        return json.RootElement.Clone();
    }

    internal static bool TryApply(Scene scene,IReadOnlyList<MoveObject> moves,out CommandResult<Scene>? result)
    {
        result=null;
        var indices=new Dictionary<string,int>(scene.Objects.Length,StringComparer.Ordinal);
        for(var i=0;i<scene.Objects.Length;i++)
            // Repair-mode scenes with duplicate/null identities retain the original Replace semantics.
            if(scene.Objects[i].Id is not {} id||!indices.TryAdd(id,i))return false;
        if(moves.Any(move=>move.Id is null))return false;
        var changed=scene.Objects.ToBuilder();
        foreach(var move in moves)
        {
            if(!indices.TryGetValue(move.Id,out var index))
            {result=CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{move.Id}'",move.Id));return true;}
            changed[index]=changed[index] with{Position=move.Position};
        }
        result=CommandResult<Scene>.Done(new(changed.MoveToImmutable()));return true;
    }
}
