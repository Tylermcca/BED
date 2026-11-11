namespace Bed;

/// <summary>An immutable preview bound to one authoritative session and revision. Host admission, source freshness and edit guards are checked by the owning host before Commit.</summary>
public sealed class Scene2EditPlan
{
    private readonly DocumentSession<Scene2> owner;
    private readonly Scene2 before;
    private readonly long revision;
    private readonly string? diskBaseline;
    private readonly ICommand<Scene2> command;
    private Scene2EditPlan(DocumentSession<Scene2> owner,ICommand<Scene2> command,Scene2 candidate)
    {this.owner=owner;before=owner.Current;revision=owner.Revision;diskBaseline=owner.ExpectedOnDisk;this.command=command;Candidate=candidate;}
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
        var issues=owner.Validator.Validate(result.Document!);
        if(issues.HasErrors()){why=string.Join(" ",issues.Select(i=>i.Message));return false;}
        if(!ReferenceEquals(start,owner.Current)||startRevision!=owner.Revision||startDisk!=owner.ExpectedOnDisk)
        {why="The scene owner changed during preview; preview again.";return false;}
        plan=new(owner,result.Resolved??frozen,result.Document!);why="";return true;
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
        var ok=owner.Execute(command);why=ok?"":string.Join(" ",owner.LastIssues.Select(i=>i.Message));return ok;
    }
}
