using System.Collections.Immutable;
using System.Security.Cryptography;
namespace Brutal.Authoring.Editor;

/// <summary>In-memory staging specification state; Ready never means published or distribution-approved.</summary>
public enum ExportStagingState
{
    /// <summary>Awaiting every declared output's exact bytes.</summary>
    Collecting,
    /// <summary>Every allowlisted artifact and notice has matched its pinned hash and length.</summary>
    Ready,
    /// <summary>Cancellation prevents any further staging receipt.</summary>
    Canceled,
    /// <summary>A failure prevents any further staging receipt.</summary>
    Failed
}
/// <summary>Expected immutable output identity; receipt checks verify bytes, not legal adequacy of notice text.</summary>
public sealed record ExportStagedOutput(string Path,string Sha256,int Length,bool IsNotice);

/// <summary>Pure owned-staging/rollback specification. No directory, write, deletion, rename, process or publication
/// API exists here. A later filesystem adapter must establish containment, exclusive ownership and durable replacement
/// independently; a cache publisher is not implicitly an export publisher. Prior-good identity never changes here.</summary>
public sealed class ExportStaging
{
    private readonly object gate=new();
    private readonly ImmutableDictionary<string,ExportStagedOutput> expected;
    private readonly HashSet<string> received=new(StringComparer.Ordinal);
    private ExportStagingState state;
    private string reason="";
    private ExportStaging(Guid id,GameContentManifest manifest,string? prior,ImmutableArray<ExportStagedOutput> outputs)
    {Id=id;ManifestHash=manifest.Hash;PriorGoodManifestHash=prior;Outputs=outputs;expected=outputs.ToImmutableDictionary(o=>o.Path,StringComparer.Ordinal);state=outputs.Length==0?ExportStagingState.Ready:ExportStagingState.Collecting;}
    /// <summary>Fresh owner-chosen attempt identity, never a filesystem path or authority to clean another attempt.</summary>
    public Guid Id {get;}
    /// <summary>Exact pinned content manifest for this attempt.</summary>
    public string ManifestHash {get;}
    /// <summary>Previously published identity supplied by the host; preserved on every outcome, not read or verified here.</summary>
    public string? PriorGoodManifestHash {get;}
    /// <summary>Complete output allowlist, including separately declared required notices.</summary>
    public ImmutableArray<ExportStagedOutput> Outputs {get;}
    /// <summary>Current in-memory phase.</summary>
    public ExportStagingState State {get{lock(gate)return state;}}
    /// <summary>Terminal failure/cancellation explanation.</summary>
    public string Reason {get{lock(gate)return reason;}}
    /// <summary>Creates a bounded plan from pinned artifacts and caller-declared required notices. Notice coverage
    /// must be explicitly declared; BED verifies bytes only, never infers all legal obligations were identified.</summary>
    public static ExportStaging Create(Guid attempt,GameContentManifest manifest,string? priorGoodManifestHash,
        IEnumerable<GameContentNode> requiredNotices,bool noticeCoverageDeclared)
    {
        ArgumentNullException.ThrowIfNull(manifest);ArgumentNullException.ThrowIfNull(requiredNotices);
        if(attempt==Guid.Empty)throw new ArgumentException("A staging attempt needs its own identity.",nameof(attempt));
        if(!noticeCoverageDeclared)throw new ArgumentException("Required notice coverage has not been declared.");
        if(priorGoodManifestHash is not null && !Hash(priorGoodManifestHash))throw new ArgumentException("Prior-good identity must be a SHA-256 hash.");
        var notices=requiredNotices.Take(65).ToArray();
        if(notices.Length>64||notices.Any(n=>n is null||!n.DeclaredComplete||n.Dependencies.Length!=0||!n.HashMatches()))throw new ArgumentException("Required notices must be complete, pinned, hash-matching leaf artifacts (at most 64).");
        if(notices.Sum(n=>(long)n.Length)>64L*1024*1024)throw new ArgumentException("Notice payload budget exceeded.");
        var all=manifest.Nodes.Concat(notices).ToArray();
        _=GameBuildProfile.Create(manifest.Profile.Id,manifest.Profile.Project,manifest.Profile.Target,all.Select(n=>n.Output));
        var outputs=manifest.Nodes.Select(n=>new ExportStagedOutput(n.Output.Destination,n.Sha256,n.Length,false))
            .Concat(notices.Select(n=>new ExportStagedOutput(n.Output.Destination,n.Sha256,n.Length,true)))
            .OrderBy(o=>o.Path,StringComparer.Ordinal).ToImmutableArray();
        return new(attempt,manifest,priorGoodManifestHash,outputs);
    }
    /// <summary>Checks supplied staged bytes against the exact allowlist; stores only receipt identity, never bytes.
    /// A wrong owner, extra output, changed bytes or terminal attempt refuses. Duplicate matching receipts are idempotent.</summary>
    public bool Record(Guid attempt,string path,ReadOnlySpan<byte> bytes,out string why)
    {
        lock(gate)
        {
            if(attempt!=Id){why="This receipt belongs to another staging attempt.";return false;}
            if(state is ExportStagingState.Canceled or ExportStagingState.Failed){why=reason;return false;}
            if(!expected.TryGetValue(path,out var output)){return Refuse("An output is outside the exact allowlist.",out why);}
            if(bytes.Length!=output.Length||Convert.ToHexStringLower(SHA256.HashData(bytes))!=output.Sha256)return Refuse("Staged bytes differ from the pinned output.",out why);
            received.Add(path);if(received.Count==expected.Count)state=ExportStagingState.Ready;why="";return true;
        }
    }
    /// <summary>Stops this attempt only; prior-good identity and any real files remain untouched.</summary>
    public void Cancel(Guid attempt){lock(gate){if(attempt!=Id)throw new ArgumentException("Wrong staging owner.",nameof(attempt));if(state==ExportStagingState.Failed)return;state=ExportStagingState.Canceled;reason="Staging was canceled; prior good export is unchanged.";}}
    /// <summary>Records an external staging failure without cleanup or publication.</summary>
    public void Fail(Guid attempt,string message){lock(gate){if(attempt!=Id)throw new ArgumentException("Wrong staging owner.",nameof(attempt));if(state==ExportStagingState.Canceled)return;if(string.IsNullOrWhiteSpace(message))throw new ArgumentException("A failure needs a reason.");state=ExportStagingState.Failed;reason=message;}}
    private bool Refuse(string message,out string why){state=ExportStagingState.Failed;why=reason=message;return false;}
    private static bool Hash(string value)=>value.Length==64&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
