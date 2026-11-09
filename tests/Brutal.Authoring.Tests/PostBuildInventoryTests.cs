using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Xunit;
namespace Brutal.Authoring.Tests;

public sealed class PostBuildInventoryTests
{
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static readonly GameContentMapping Output=new("program","bin/result.bin","synthetic");
    private static readonly GameContentNode Notice=new(new("notice","NOTICE.txt","text"),[7,8],Hash([7,8]),[],true,ExportSource.GameOwned);
    private static (GameContentManifest Source,ExportOriginReceipt Origin,ExportCheckReceipt Check) Admission(byte value=1,bool licensed=false)
    {
        var mapping=new GameContentMapping("source","input.txt","text");
        const string guid="11111111111111111111111111111111";var sourceHash=new string('a',64);
        var source=GameContentManifest.Create(GameBuildProfile.Create("sample","Sample.csproj",GameBuildTarget.WindowsX64,[mapping]),"synthetic","saved-1",true,
            [new(mapping,[value],Hash([value]),[],true,licensed?ExportSource.Catalog(guid,sourceHash):ExportSource.GameOwned)]);
        AssetRecord[] records=licensed?[new(guid,"synthetic.txt",sourceHash,"text",1,JsonSerializer.SerializeToElement(new{}),
            new("Fixture author","MIT","self-authored fixture",Declared:AssetOrigin.Licensed),AiUse.Forbidden)]:[];
        return(source,ExportOriginReview.Evaluate(source,records),new(source.Hash,"required",ExportCheckVerdict.Passed,DateTimeOffset.UtcNow,TimeSpan.Zero));
    }
    private static BuildOutputDeclaration Declaration(byte sourceValue=1,bool licensed=false)
    {
        var a=Admission(sourceValue,licensed);
        return new(a.Source,["required"],[a.Check],a.Origin,
            GameBuildProfile.Create("sample","Sample.csproj",GameBuildTarget.WindowsX64,[Output]),
            [new(Output,[])],[new(Notice,a.Origin.Notices)]);
    }
    [Fact] public void Post_build_bytes_are_newly_pinned_and_source_hash_is_part_of_identity()
    {
        var d=Declaration();var bytes=new byte[]{9,10,11};var id=Guid.NewGuid();var prior=new string('b',64);
        var result=d.Pin(id,new Dictionary<string,byte[]>{{"bin/result.bin",bytes},{"NOTICE.txt",[7,8]}},prior);
        Assert.Equal(d.Source.Hash,result.Manifest.SourceManifestHash);
        var node=Assert.Single(result.Manifest.Nodes);Assert.Equal(Hash(bytes),node.Sha256);Assert.Equal(3,node.Length);
        Assert.True(node.Source!.IsGameOwned);Assert.Equal(d.Source.Hash,node.Source.SourceHash);
        Assert.Equal(ExportStagingState.Ready,result.Staging.State);Assert.Equal(prior,result.Staging.PriorGoodManifestHash);
        bytes[0]=0;Assert.False(result.Staging.Record(id,"bin/result.bin",bytes,out _));
        byte[] copy=new byte[3];node.CopyTo(copy);Assert.Equal(new byte[]{9,10,11},copy);
        var other=Declaration(2).Pin(Guid.NewGuid(),new Dictionary<string,byte[]>{{"bin/result.bin",copy},{"NOTICE.txt",[7,8]}});
        Assert.NotEqual(result.Manifest.Hash,other.Manifest.Hash);Assert.NotEqual(result.Manifest.SourceManifestHash,other.Manifest.SourceManifestHash);
    }
    [Theory][InlineData("missing-check")][InlineData("stale-check")][InlineData("failed-check")][InlineData("stale-origin")]
    public void Source_receipts_must_admit_the_exact_source(string mode)
    {
        var a=Admission();var receipts=mode=="missing-check"?Array.Empty<ExportCheckReceipt>():new[]{mode=="stale-check"?a.Check with {ManifestHash=new string('c',64)}:mode=="failed-check"?a.Check with {Verdict=ExportCheckVerdict.Failed}:a.Check};
        Assert.Throws<ArgumentException>(()=>new BuildOutputDeclaration(a.Source,["required"],receipts,mode=="stale-origin"?Admission(2).Origin:a.Origin,
            GameBuildProfile.Create("sample","Sample.csproj",GameBuildTarget.WindowsX64,[Output]),[new(Output,[])],[]));
    }
    [Fact] public void Required_origin_notice_coverage_and_notice_bytes_cannot_be_omitted_or_changed()
    {
        var a=Admission(licensed:true);Assert.Single(a.Origin.Notices);Assert.True(a.Origin.Accepted);
        Assert.Throws<ArgumentException>(()=>new BuildOutputDeclaration(a.Source,["required"],[a.Check],a.Origin,
            GameBuildProfile.Create("sample","Sample.csproj",GameBuildTarget.WindowsX64,[Output]),[new(Output,[])],[]));
        var d=Declaration(licensed:true);
        Assert.Throws<ArgumentException>(()=>d.Pin(Guid.NewGuid(),new Dictionary<string,byte[]>{{"bin/result.bin",[9]},{"NOTICE.txt",[0]}}));
        var result=d.Pin(Guid.NewGuid(),new Dictionary<string,byte[]>{{"bin/result.bin",[9]},{"NOTICE.txt",[7,8]}});
        Assert.True(Assert.Single(result.Staging.Outputs.Where(o=>o.Path=="NOTICE.txt")).IsNotice);
    }
    [Fact] public void Declaration_reuses_graph_and_collision_refusals_before_build()
    {
        var a=Admission();var p=GameBuildProfile.Create("sample","Sample.csproj",GameBuildTarget.WindowsX64,[Output]);
        Assert.Throws<ArgumentException>(()=>new BuildOutputDeclaration(a.Source,["required"],[a.Check],a.Origin,p,[new(Output,["missing"])],[]));
        Assert.Throws<ArgumentException>(()=>new BuildOutputDeclaration(a.Source,["required"],[a.Check],a.Origin,p,[new(Output,["program"])],[]));
        var collision=new GameContentNode(new("notice","BIN/result.bin","text"),[1],Hash([1]),[],true);
        Assert.Throws<ArgumentException>(()=>new BuildOutputDeclaration(a.Source,["required"],[a.Check],a.Origin,p,[new(Output,[])],[new(collision,[])]));
    }
    [Theory][InlineData("valid")][InlineData("missing")][InlineData("extra")][InlineData("directory")][InlineData("notice")][InlineData("canceled")]
    public void Fresh_folder_is_exact_and_never_modified_by_inventory(string mode)
    {
        var root=Path.Combine(Path.GetTempPath(),"bed-postbuild-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"bin"));
        try
        {
            File.WriteAllBytes(Path.Combine(root,"bin","result.bin"),[9,10]);File.WriteAllBytes(Path.Combine(root,"NOTICE.txt"),mode=="notice"?[0]:[7,8]);
            if(mode=="missing")File.Delete(Path.Combine(root,"bin","result.bin"));
            if(mode=="extra")File.WriteAllText(Path.Combine(root,"unexpected.txt"),"keep");
            if(mode=="directory")Directory.CreateDirectory(Path.Combine(root,"unexpected"));
            var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
            var d=Declaration();var id=Guid.NewGuid();var prior=new string('d',64);
            if(mode=="valid")
            {
                var pinned=BuildOutputInventory.Capture(root,d,id,prior);
                Assert.Equal(d.Source.Hash,pinned.Manifest.SourceManifestHash);Assert.Equal(ExportStagingState.Ready,pinned.Staging.State);
                Assert.Equal(prior,pinned.Staging.PriorGoodManifestHash);Assert.Equal(2,pinned.Staging.Outputs.Length);
            }
            else if(mode=="canceled")Assert.Throws<OperationCanceledException>(()=>BuildOutputInventory.Capture(root,d,id,prior,new CancellationToken(true)));
            else if(mode is "missing" or "notice")Assert.Throws<ArgumentException>(()=>BuildOutputInventory.Capture(root,d,id,prior));
            else Assert.Throws<IOException>(()=>BuildOutputInventory.Capture(root,d,id,prior));
            Assert.Equal(before.Keys.Order(),Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order());
            foreach(var row in before)Assert.Equal(row.Value,File.ReadAllBytes(row.Key));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact] public void Host_refuses_unsaved_or_wrong_saved_revision_without_starting_any_process()
    {
        var spec=new GameBuildSpec(@"C:\invented\builder.exe",@"C:\invented",["{output}"]);var d=Declaration();
        Assert.Throws<InvalidOperationException>(()=>new GameBuildHost(spec,@"C:\invented","saved-1",true,d,100,10,TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(()=>new GameBuildHost(spec,@"C:\invented","different",false,d,100,10,TimeSpan.FromSeconds(1)));
        using var host=new GameBuildHost(spec,@"C:\invented","saved-1",false,d,100,10,TimeSpan.FromSeconds(1));
        host.Cancel();Assert.Null(host.Completion);Assert.Null(host.OutputManifest);Assert.Throws<InvalidOperationException>(host.Start);
    }
}
