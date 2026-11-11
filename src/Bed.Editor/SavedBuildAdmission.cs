using System.Collections.Immutable;
namespace Bed.Editor;

/// <summary>Saved-only export admission produced by the existing closure, check and origin gates.
/// It invokes explicitly supplied game producers; it performs no save, disk scan, process launch or publication.</summary>
public sealed class SavedBuildAdmission
{
    private SavedBuildAdmission(PlaySnapshot saved,GameContentManifest source,ImmutableArray<string> required,
        ImmutableArray<ExportCheckReceipt> checks,ExportOriginReceipt origin,ImmutableArray<AssetRecord> records)
    {SavedInput=saved;SnapshotHash=saved.Hash;Source=source;RequiredChecks=required;Checks=checks;Origin=origin;SourceRecords=records;}
    /// <summary>The immutable exact Saved payload passed to the admitted producers and checks.</summary>
    public PlaySnapshot SavedInput {get;}
    /// <summary>Exact immutable Saved input from which the declared producer captured the content graph.</summary>
    public string SnapshotHash {get;}
    /// <summary>Complete verified EX02 source closure.</summary>
    public GameContentManifest Source {get;}
    /// <summary>Caller-declared required checks, never invented sufficient coverage.</summary>
    public ImmutableArray<string> RequiredChecks {get;}
    /// <summary>Actual check-producer receipts admitted for this exact manifest.</summary>
    public ImmutableArray<ExportCheckReceipt> Checks {get;}
    /// <summary>Copied producing metadata supplied by the admitted game, not a live sidecar scan.</summary>
    public ImmutableArray<AssetRecord> SourceRecords {get;}
    /// <summary>Existing pure origin review result for the producing records.</summary>
    public ExportOriginReceipt Origin {get;}
    /// <summary>Captures only Saved input, proves declared graph/hash closure, runs the supplied export checks,
    /// then applies ExportOriginReview. Any missing/stale/failed gate refuses the admission.</summary>
    public static SavedBuildAdmission Prepare(PlaySnapshot saved,GameBuildProfile sourceProfile,string inventoryOwner,bool declaredComplete,
        Func<PlaySnapshot,IEnumerable<GameContentNode>> capture,IEnumerable<string> requiredChecks,
        Func<GameContentManifest,IEnumerable<ExportCheckReceipt>> runChecks,IEnumerable<AssetRecord> sourceRecords,
        IReadOnlyCollection<string>? ownNames=null,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(saved);ArgumentNullException.ThrowIfNull(capture);ArgumentNullException.ThrowIfNull(runChecks);
        ArgumentNullException.ThrowIfNull(requiredChecks);ArgumentNullException.ThrowIfNull(sourceRecords);
        if(saved.Choice!=PlayInputChoice.Saved)throw new InvalidOperationException("Export builds require the admitted Saved input.");
        cancellation.ThrowIfCancellationRequested();
        var source=GameContentManifest.Create(sourceProfile,inventoryOwner,saved.Hash,declaredComplete,capture(saved));
        var required=requiredChecks.Take(257).ToImmutableArray();cancellation.ThrowIfCancellationRequested();
        var checks=runChecks(source).Take(1025).ToImmutableArray();
        var checkGate=ExportGate.Evaluate(source,required,checks);
        if(!checkGate.ChecksSatisfied)throw new InvalidOperationException(string.Join(" ",checkGate.Reasons));
        cancellation.ThrowIfCancellationRequested();
        var records=sourceRecords.Take(1025).Select(r=>r is null?throw new ArgumentException("Missing producing record."):r with
            {Settings=r.Settings.Clone(),RightsFound=r.RightsFound is null?null:Array.AsReadOnly(r.RightsFound.ToArray())}).ToImmutableArray();
        var origin=ExportOriginReview.Evaluate(source,records,ownNames);
        var gate=ExportGate.Evaluate(source,required,checks,origin);
        if(!gate.CompleteExport)throw new InvalidOperationException(string.Join(" ",gate.Reasons));
        cancellation.ThrowIfCancellationRequested();return new(saved,source,required,checks,origin,records);
    }
    /// <summary>Combines this admitted Saved source with explicit output mappings and required notice coverage.
    /// Output binaries remain unpinned until the existing post-build inventory succeeds.</summary>
    public BuildOutputDeclaration DeclareOutputs(GameBuildProfile profile,IEnumerable<BuildOutputItem> outputs,IEnumerable<BuildOutputNotice> requiredNotices)=>
        new(Source,RequiredChecks,Checks,Origin,profile,outputs,requiredNotices);
}
