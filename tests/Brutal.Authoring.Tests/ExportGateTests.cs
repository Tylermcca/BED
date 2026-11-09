using System.Security.Cryptography;
using Brutal.Authoring.Editor;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class ExportGateTests
{
    private static GameContentManifest Manifest(byte value=1)
    {
        var mapping=new GameContentMapping("root","content/root.bin","test-v1");
        var profile=GameBuildProfile.Create("desktop","Game.csproj",GameBuildTarget.WindowsX64,[mapping]);
        return GameContentManifest.Create(profile,"game","revision",true,[new(mapping,[value],Convert.ToHexStringLower(SHA256.HashData(new[]{value})),[],true)]);
    }
    private static ExportCheckReceipt Receipt(GameContentManifest m,string id="required",ExportCheckVerdict verdict=ExportCheckVerdict.Passed)=>new(m.Hash,id,verdict,new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero),TimeSpan.FromSeconds(1));
    [Fact] public void Exact_passes_satisfy_checks_but_never_approve_distribution()
    {
        var m=Manifest();var result=ExportGate.Evaluate(m,["required"],[Receipt(m)]);Assert.True(result.ChecksSatisfied);Assert.Empty(result.Reasons);
        Assert.False(result.CompleteExport);Assert.Contains("not established",result.DistributionStatus);
    }
    [Theory]
    [InlineData(ExportCheckVerdict.Unknown)][InlineData(ExportCheckVerdict.Failed)][InlineData(ExportCheckVerdict.Incomplete)]
    [InlineData(ExportCheckVerdict.Canceled)][InlineData(ExportCheckVerdict.TimedOut)]
    public void Nonpassing_outcome_fails_closed(ExportCheckVerdict verdict)
    {var m=Manifest();Assert.False(ExportGate.Evaluate(m,["required"],[Receipt(m,verdict:verdict)]).ChecksSatisfied);}
    [Fact] public void Missing_stale_and_duplicate_receipts_refuse_even_with_recent_clock_time()
    {
        var m=Manifest();Assert.False(ExportGate.Evaluate(m,["required"],[]).ChecksSatisfied);
        var newer=Receipt(Manifest(2)) with {FinishedAt=DateTimeOffset.MaxValue};Assert.False(ExportGate.Evaluate(m,["required"],[newer]).ChecksSatisfied);
        Assert.False(ExportGate.Evaluate(m,["required"],[Receipt(m),Receipt(m)]).ChecksSatisfied);
    }
    [Fact] public void Receipt_cannot_be_reused_for_different_required_check()
    {var m=Manifest();Assert.False(ExportGate.Evaluate(m,["another"],[Receipt(m)]).ChecksSatisfied);}
    [Fact] public void Invalid_timing_hash_and_unknown_verdict_refuse()
    {
        var m=Manifest();foreach(var r in new[]{Receipt(m) with {FinishedAt=default},Receipt(m) with {Duration=TimeSpan.FromTicks(-1)},Receipt(m) with {Verdict=(ExportCheckVerdict)99},Receipt(m) with {ManifestHash="invalid"}})
            Assert.False(ExportGate.Evaluate(m,["required"],[r]).ChecksSatisfied);
    }
    [Fact] public void Empty_requirements_do_not_imply_complete_export_and_input_lists_are_bounded()
    {
        var m=Manifest();Assert.False(ExportGate.Evaluate(m,[],[]).CompleteExport);
        Assert.False(ExportGate.Evaluate(m,["required","required"],[Receipt(m)]).ChecksSatisfied);
        var seen=0;IEnumerable<ExportCheckReceipt> Infinite(){while(true){seen++;yield return Receipt(m);}}
        Assert.False(ExportGate.Evaluate(m,["required"],Infinite()).ChecksSatisfied);Assert.Equal(1025,seen);
    }
}
