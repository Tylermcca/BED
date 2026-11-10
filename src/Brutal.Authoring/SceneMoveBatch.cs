namespace Brutal.Authoring;

// Only the application cost changes: commands, order, journal entries and final validator stay unchanged.
internal static class SceneMoveBatch
{
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
