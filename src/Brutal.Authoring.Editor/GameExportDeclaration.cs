namespace Brutal.Authoring.Editor;

/// <summary>An admitted Saved source plus explicit game-owned output declarations. Does not pin built binaries or run a command.</summary>
public sealed class GameExportPlan
{
    /// <summary>Requires output provenance to be bound to this exact Saved-source admission.</summary>
    public GameExportPlan(SavedBuildAdmission admission,BuildOutputDeclaration outputs,string? savedPayloadOutput=null)
    {
        ArgumentNullException.ThrowIfNull(admission);ArgumentNullException.ThrowIfNull(outputs);
        if(outputs.Source.Hash!=admission.Source.Hash||outputs.Source.InventoryRevision!=admission.SnapshotHash)
            throw new ArgumentException("Build outputs belong to another Saved source admission.");
        if(savedPayloadOutput is not null&&!outputs.Paths.Contains(savedPayloadOutput,StringComparer.Ordinal))throw new ArgumentException("The copied Saved payload must be a declared output.",nameof(savedPayloadOutput));
        Admission=admission;Outputs=outputs;SavedPayloadOutput=savedPayloadOutput;
    }
    /// <summary>Optional legacy Saved-envelope output. Saved-file exports instead retain every admitted source path.</summary>
    public string? SavedPayloadOutput {get;}
    /// <summary>EX02/EX03/origin admission, profiles, producing records and exact receipts.</summary>
    public SavedBuildAdmission Admission {get;}
    /// <summary>Game-declared output graph, runtime formats and complete notice coverage.</summary>
    public BuildOutputDeclaration Outputs {get;}
}
/// <summary>Optional game-owned Saved export producer and unpacked smoke command. No tools-side format/ID inference.</summary>
public sealed class GameExportDeclaration
{
    private readonly Func<PlaySnapshot,GameExportPlan> prepare;
    private readonly Func<string,GameContentManifest,GameLaunchSpec> smoke;
    /// <summary>Declares the Saved converter, admitted source/output producer and explicit smoke-spec producer.</summary>
    public GameExportDeclaration(PlaySnapshotConverter convertSaved,Func<PlaySnapshot,GameExportPlan> prepare,
        Func<string,GameContentManifest,GameLaunchSpec> smoke)
    {ArgumentNullException.ThrowIfNull(convertSaved);ArgumentNullException.ThrowIfNull(prepare);ArgumentNullException.ThrowIfNull(smoke);ConvertSaved=convertSaved;this.prepare=prepare;this.smoke=smoke;}
    /// <summary>Only the explicitly selected immutable Saved text is supplied to game conversion.</summary>
    public PlaySnapshotConverter ConvertSaved {get;}
    /// <summary>Runs game declarations only after Saved capture, requiring their source identity to match it exactly.</summary>
    public GameExportPlan Prepare(PlaySnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if(saved.Choice!=PlayInputChoice.Saved)throw new InvalidOperationException("Export requires Saved input.");
        var plan=prepare(saved)??throw new InvalidOperationException("The adapter did not produce an export plan.");
        if(plan.Admission.SnapshotHash!=saved.Hash)throw new InvalidOperationException("The export plan belongs to another Saved snapshot.");
        return plan;
    }
    /// <summary>Creates the declared smoke command for actual pinned post-build outputs. Does not start or discover an executable.</summary>
    public GameLaunchSpec Smoke(string outputDirectory,GameContentManifest outputs)
    {
        if(!Path.IsPathFullyQualified(outputDirectory))throw new ArgumentException("Use the owned absolute output directory.",nameof(outputDirectory));
        ArgumentNullException.ThrowIfNull(outputs);return smoke(outputDirectory,outputs)??throw new InvalidOperationException("No smoke command was declared.");
    }
}
