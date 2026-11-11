using System.Security.Cryptography;
using System.Text;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;
public sealed class SavedBuildAdmissionTests
{
    private static GameContentMapping Mapping=>new("document","saved.txt","utf8");
    private static GameBuildProfile Profile=>GameBuildProfile.Create("saved","Synthetic.csproj",GameBuildTarget.WindowsX64,[Mapping]);
    private static PlaySnapshot Snapshot(PlayInputChoice choice=PlayInputChoice.Saved)=>PlaySnapshot.Create(new("scene",4,"saved","dirty draft"),choice,d=>new("fixture",Encoding.UTF8.GetBytes(d.Text))).Snapshot!;
    private static GameContentNode Node(PlaySnapshot snapshot,params string[] dependencies)
    {var bytes=snapshot.CopyPayload();return new(Mapping,bytes,Convert.ToHexStringLower(SHA256.HashData(bytes)),dependencies,true,ExportSource.GameOwned);}
    private static ExportCheckReceipt Check(GameContentManifest manifest,ExportCheckVerdict verdict=ExportCheckVerdict.Passed,string? hash=null)=>new(hash??manifest.Hash,"structure",verdict,DateTimeOffset.UtcNow,TimeSpan.Zero);
    [Fact]
    public void Saved_closure_then_real_check_then_origin_yield_exact_source_and_unpinned_output_declaration()
    {
        var saved=Snapshot();var order=new List<string>();var admission=SavedBuildAdmission.Prepare(saved,Profile,"fixture",true,s=>{order.Add("capture");Assert.Same(saved,s);return [Node(s)];},["structure"],m=>
        {
            order.Add("checks");var node=Assert.Single(m.Nodes);var bytes=new byte[node.Length];node.CopyTo(bytes);
            var valid=Encoding.UTF8.GetString(bytes)=="saved";return [Check(m,valid?ExportCheckVerdict.Passed:ExportCheckVerdict.Failed)];
        },[]);
        Assert.Equal(new[]{"capture","checks"},order);Assert.Equal(saved.Hash,admission.Source.InventoryRevision);Assert.Equal(saved.Hash,admission.SnapshotHash);
        Assert.True(admission.Origin.Accepted);Assert.Equal(admission.Source.Hash,Assert.Single(admission.Checks).ManifestHash);
        var output=new GameContentMapping("runtime","runtime.bin","binary");var profile=GameBuildProfile.Create("build","Synthetic.csproj",GameBuildTarget.WindowsX64,[output]);
        var declaration=admission.DeclareOutputs(profile,[new(output,[])],[]);Assert.Same(admission.Source,declaration.Source);Assert.Equal(new[]{"runtime.bin"},declaration.Paths);
    }
    [Fact]
    public void Draft_and_canceled_inputs_refuse_before_either_producer_runs()
    {
        var calls=0;IEnumerable<GameContentNode> Capture(PlaySnapshot s){calls++;return [Node(s)];}
        Assert.Throws<InvalidOperationException>(()=>SavedBuildAdmission.Prepare(Snapshot(PlayInputChoice.Draft),Profile,"fixture",true,Capture,["structure"],m=>{calls++;return [Check(m)];},[]));
        Assert.Throws<OperationCanceledException>(()=>SavedBuildAdmission.Prepare(Snapshot(),Profile,"fixture",true,Capture,["structure"],m=>{calls++;return [Check(m)];},[],cancellation:new CancellationToken(true)));
        Assert.Equal(0,calls);
    }
    [Fact]
    public void Missing_content_closure_refuses_before_check_execution()
    {
        var checks=0;Assert.Throws<ArgumentException>(()=>SavedBuildAdmission.Prepare(Snapshot(),Profile,"fixture",true,s=>[Node(s,"missing")],["structure"],m=>{checks++;return [Check(m)];},[]));Assert.Equal(0,checks);
    }
    [Theory]
    [InlineData("failed")][InlineData("stale")][InlineData("missing")]
    public void Existing_check_gate_is_never_relabelled_as_passed(string mode)
    {
        Assert.Throws<InvalidOperationException>(()=>SavedBuildAdmission.Prepare(Snapshot(),Profile,"fixture",true,s=>[Node(s)],["structure"],m=>
            mode=="missing"?[]:[Check(m,mode=="failed"?ExportCheckVerdict.Failed:ExportCheckVerdict.Passed,mode=="stale"?new string('a',64):null)],[]));
    }
    [Fact]
    public void Producing_metadata_is_detached_from_caller_json_lifetime_and_record_array()
    {
        using var settings=System.Text.Json.JsonDocument.Parse("""{"scale":2}""");
        var record=new AssetRecord(new string('a',32),"synthetic.obj",new string('b',64),"fixture",1,settings.RootElement,
            new Provenance("Fixture author","self-authored","script",Declared:AssetOrigin.Ours),AiUse.Unknown);
        AssetRecord[] records=[record];var saved=Snapshot();
        var admission=SavedBuildAdmission.Prepare(saved,Profile,"fixture",true,s=>[Node(s)],["structure"],m=>[Check(m)],records);
        records[0]=record with {Source="changed.obj"};settings.Dispose();
        var pinned=Assert.Single(admission.SourceRecords);Assert.Equal("synthetic.obj",pinned.Source);
        Assert.Equal(2,pinned.Settings.GetProperty("scale"u8).GetInt32());Assert.Equal(record.SourceHash,pinned.SourceHash);
        Assert.NotSame(record,pinned);Assert.True(admission.Origin.Accepted);
    }
}
