using System.Collections.Immutable;
namespace Brutal.Authoring.Editor;

/// <summary>Export-specific terminal outcome, not an editor CheckRun verdict or inferred permission.</summary>
public enum ExportCheckVerdict
{
    /// <summary>No terminal result was established.</summary>
    Unknown,
    /// <summary>The declared required check completed and accepted these exact export inputs.</summary>
    Passed,
    /// <summary>The check rejected the inputs.</summary>
    Failed,
    /// <summary>The check did not cover its declared inputs completely.</summary>
    Incomplete,
    /// <summary>The run was canceled.</summary>
    Canceled,
    /// <summary>The run exceeded its deadline.</summary>
    TimedOut
}
/// <summary>Receipt supplied by an export-check producer, bound to exact immutable export inputs.
/// FinishedAt and Duration record execution time; neither grants freshness. The hash is not a credential.
/// Producers must issue Passed only after their complete check, never by relabelling an ordinary CheckRun.</summary>
public sealed record ExportCheckReceipt(string ManifestHash,string CheckId,ExportCheckVerdict Verdict,DateTimeOffset FinishedAt,TimeSpan Duration);
/// <summary>Pure check-gate result. Passing checks is not distribution/origin approval or permission to publish.</summary>
public sealed record ExportGateResult(bool ChecksSatisfied,ImmutableArray<string> Reasons)
{
    internal bool OriginSatisfied {get;init;}
    /// <summary>Both exact-manifest gates passed; this does not launch or publish anything.</summary>
    public bool CompleteExport=>ChecksSatisfied && OriginSatisfied;
    /// <summary>The explicit unresolved prerequisite, even when all declared checks pass.</summary>
    public string DistributionStatus=>OriginSatisfied ? "Origin review accepted for this manifest." : "Distribution and origin review is not established for this manifest.";
}
/// <summary>Pure required-check admission over export-specific receipts. Performs no checks, disk reads, save,
/// process launch or publication. The game/host declares required IDs; BED does not invent sufficient coverage.</summary>
public static class ExportGate
{
    /// <summary>Requires exactly one current Passed receipt for each declared check. Duplicate receipts are ambiguous,
    /// not resolved by wall-clock ordering. Other-manifest receipts are stale regardless of their timestamp.
    /// Empty required-check declarations are supported but still cannot authorize complete export.</summary>
    public static ExportGateResult Evaluate(GameContentManifest manifest,IEnumerable<string> requiredChecks,IEnumerable<ExportCheckReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(manifest);ArgumentNullException.ThrowIfNull(requiredChecks);ArgumentNullException.ThrowIfNull(receipts);
        var required=requiredChecks.Take(257).ToArray();var rows=receipts.Take(1025).ToArray();
        var reasons=ImmutableArray.CreateBuilder<string>();
        if(required.Length>256||required.Any(id=>!Id(id))||required.Distinct(StringComparer.Ordinal).Count()!=required.Length)
            reasons.Add("Required checks must be a unique bounded list of valid IDs (at most 256).");
        if(rows.Length>1024)reasons.Add("There are more than 1024 check receipts.");
        if(rows.Any(r=>r is null||!Id(r.CheckId)||!Hash(r.ManifestHash)||!Enum.IsDefined(r.Verdict)||r.FinishedAt==default||r.Duration<TimeSpan.Zero))
            reasons.Add("A check receipt is missing valid identity, verdict or execution-time fields.");
        if(reasons.Count>0)return new(false,reasons.ToImmutable());
        foreach(var id in required.Order(StringComparer.Ordinal))
        {
            var matches=rows.Where(r=>r.CheckId==id).ToArray();
            if(matches.Length==0){reasons.Add($"Required check '{id}' has no receipt.");continue;}
            if(matches.Length!=1){reasons.Add($"Required check '{id}' has ambiguous duplicate receipts.");continue;}
            var receipt=matches[0];
            if(receipt.ManifestHash!=manifest.Hash){reasons.Add($"Required check '{id}' is out of date for these export inputs.");continue;}
            if(receipt.Verdict!=ExportCheckVerdict.Passed)reasons.Add($"Required check '{id}' did not pass ({receipt.Verdict}).");
        }
        return new(reasons.Count==0,reasons.ToImmutable());
    }
    /// <summary>Combines the required-check gate with an internally produced origin receipt for the same manifest.</summary>
    public static ExportGateResult Evaluate(GameContentManifest manifest,IEnumerable<string> requiredChecks,IEnumerable<ExportCheckReceipt> receipts,ExportOriginReceipt origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var check=Evaluate(manifest,requiredChecks,receipts);
        var accepted=origin.ManifestHash==manifest.Hash && origin.Accepted;
        var reasons=check.Reasons.ToBuilder();
        if(origin.ManifestHash!=manifest.Hash)reasons.Add("Origin review is out of date for these export inputs.");
        else reasons.AddRange(origin.Refusals);
        return new(check.ChecksSatisfied,reasons.ToImmutable()){OriginSatisfied=accepted};
    }
    private static bool Id(string? value)=>value is {Length:>0 and <=128}&&!string.IsNullOrWhiteSpace(value)&&!value.Any(char.IsControl);
    private static bool Hash(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
