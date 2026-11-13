namespace Bed;

/// <summary>An immutable preview bound to one authoritative session and revision. Host admission, source freshness and edit guards are checked by the owning host before Commit.</summary>
public sealed class Scene2EditPlan
{
    private readonly DocumentSession<Scene2> owner;
    private readonly Scene2 before;
    private readonly long revision;
    private readonly string? diskBaseline;
    private readonly IReadOnlyList<ICommand<Scene2>> commands;
    private Scene2EditPlan(DocumentSession<Scene2> owner,IReadOnlyList<ICommand<Scene2>> commands,Scene2 candidate)
    {this.owner=owner;before=owner.Current;revision=owner.Revision;diskBaseline=owner.ExpectedOnDisk;this.commands=commands;Candidate=candidate;}
    /// <summary>The validated immutable candidate; capturing it changes no draft, history or file.</summary>
    public Scene2 Candidate {get;}
    /// <summary>The owner revision captured before preview.</summary>
    public long ExpectedRevision=>revision;
    /// <summary>Previews a registered Scene2Kit command without changing the session. Refusals produce no plan.</summary>
    /// <param name="owner">The single scene authority.</param>
    /// <param name="command">A command registered by Scene2Kit.</param>
    /// <param name="plan">The opaque candidate on success.</param>
    /// <param name="why">A plain refusal, or empty on success.</param>
    public static bool Capture(DocumentSession<Scene2> owner,ICommand<Scene2> command,out Scene2EditPlan? plan,out string why)
    {
        plan=null;
        var start=owner.Current;var startRevision=owner.Revision;var startDisk=owner.ExpectedOnDisk;
        ICommand<Scene2> frozen;
        try{frozen=Scene2Kit.Catalog().FromEntry(owner.Catalog.ToEntry(command));}
        catch(Exception e)when(e is DocumentFormatException or InvalidOperationException or ArgumentException)
        {why="Use a registered scene-v2 command: "+e.Message;return false;}
        var result=frozen.Apply(owner.Current);
        if(!result.Ok){why=string.Join(" ",result.Issues.Select(i=>i.Message));return false;}
        var candidate=result.Document!;
        var commands=new List<ICommand<Scene2>>{result.Resolved??frozen};
        if(frozen is Scene2DuplicateInstance or Scene2Compatible { Action: "duplicate" })
        {
            var old=Scene2Resolver.Resolve(start);var next=Scene2Resolver.Resolve(candidate);
            var ids=old.Objects.Select(o=>o.Object.Id).ToHashSet(StringComparer.Ordinal);
            var names=old.Objects.Select(o=>CopyNames.NameOf(start,o.Object)).ToList();
            var sourceMap=frozen is Scene2DuplicateInstance instance?instance.IdMap:((Scene2Compatible)result.Resolved!).Arguments.GetProperty("map");
            var originals=sourceMap.EnumerateObject().ToDictionary(p=>p.Value.GetString()!,p=>p.Name,StringComparer.Ordinal);
            var byId=old.Objects.ToDictionary(o=>o.Object.Id,o=>o.Object,StringComparer.Ordinal);
            foreach(var item in next.Objects.Where(o=>!ids.Contains(o.Object.Id)))
            {
                var label=CopyNames.Next(CopyNames.NameOf(start,byId[originals[item.Object.Id]]),names);names.Add(label);
                var rename=new Scene2Set(item.Object.Id,"/properties/name",System.Text.Json.JsonSerializer.SerializeToElement(label));
                var named=rename.Apply(candidate);
                if(!named.Ok){why=string.Join(" ",named.Issues.Select(i=>i.Message));return false;}
                commands.Add(rename);candidate=named.Document!;
            }
        }
        var issues=owner.Validator.Validate(candidate);
        if(issues.HasErrors()){why=string.Join(" ",issues.Select(i=>i.Message));return false;}
        if(!ReferenceEquals(start,owner.Current)||startRevision!=owner.Revision||startDisk!=owner.ExpectedOnDisk)
        {why="The scene owner changed during preview; preview again.";return false;}
        plan=new(owner,commands,candidate);why="";return true;
    }
    /// <summary>Commits once, as one validated undo/journal step, only if owner identity, revision and saved baseline still match. Never saves a file.</summary>
    /// <param name="target">The original authoritative session.</param>
    /// <param name="why">A plain refusal, or empty on success.</param>
    public bool Commit(DocumentSession<Scene2> target,out string why)
    {
        if(!ReferenceEquals(target,owner)||owner.Revision!=revision||!ReferenceEquals(owner.Current,before))
        {why="The scene owner changed; preview again.";return false;}
        if(owner.ExpectedOnDisk!=diskBaseline||owner.FilePath is not null&&owner.CheckDisk()!=DiskState.Unchanged)
        {why="The saved scene changed; resolve the disk conflict before committing.";return false;}
        var ok=commands.Count==1?owner.Execute(commands[0]):owner.ExecuteBatch(commands);why=ok?"":string.Join(" ",owner.LastIssues.Select(i=>i.Message));return ok;
    }
}
