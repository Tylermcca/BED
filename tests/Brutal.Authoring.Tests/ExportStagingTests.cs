using System.Security.Cryptography;
using Brutal.Authoring.Editor;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class ExportStagingTests
{
    private static GameContentNode Node(string id,string path,byte b)=>new(new(id,path,"test-v1"),[b],Convert.ToHexStringLower(SHA256.HashData(new[]{b})),[],true);
    private static GameContentManifest Manifest(){var n=Node("root","content/root.bin",1);var p=GameBuildProfile.Create("desktop","Game.csproj",GameBuildTarget.WindowsX64,[n.Output]);return GameContentManifest.Create(p,"game","rev",true,[n]);}
    private static ExportStaging Stage()=>ExportStaging.Create(Guid.NewGuid(),Manifest(),new string('a',64),[Node("notice","licenses/NOTICE.txt",2)],true);
    [Fact] public void Ready_requires_artifact_and_notice_bytes_without_changing_prior_good()
    {
        var s=Stage();Assert.True(s.Record(s.Id,"content/root.bin",[1],out _));Assert.Equal(ExportStagingState.Collecting,s.State);
        Assert.True(s.Record(s.Id,"licenses/NOTICE.txt",[2],out _));Assert.Equal(ExportStagingState.Ready,s.State);
        Assert.True(s.Record(s.Id,"licenses/NOTICE.txt",[2],out _));Assert.Equal(new string('a',64),s.PriorGoodManifestHash);
    }
    [Fact] public void Cancel_even_after_ready_preserves_previous_export_and_rejects_late_completion()
    {var s=Stage();s.Record(s.Id,"content/root.bin",[1],out _);s.Record(s.Id,"licenses/NOTICE.txt",[2],out _);s.Cancel(s.Id);Assert.False(s.Record(s.Id,"content/root.bin",[1],out _));Assert.Equal(ExportStagingState.Canceled,s.State);Assert.Equal(new string('a',64),s.PriorGoodManifestHash);}
    [Theory][InlineData("../escape")][InlineData("content/extra.bin")][InlineData("CONTENT/root.bin")]
    public void Nonallowlisted_path_fails_attempt(string path)
    {var s=Stage();Assert.False(s.Record(s.Id,path,[1],out _));Assert.Equal(ExportStagingState.Failed,s.State);Assert.Equal(new string('a',64),s.PriorGoodManifestHash);}
    [Fact] public void Changed_bytes_fail_and_other_owner_cannot_mutate_attempt()
    {var s=Stage();Assert.False(s.Record(Guid.NewGuid(),"content/root.bin",[1],out _));Assert.Equal(ExportStagingState.Collecting,s.State);Assert.Throws<ArgumentException>(()=>s.Cancel(Guid.NewGuid()));Assert.False(s.Record(s.Id,"content/root.bin",[9],out _));Assert.Equal(ExportStagingState.Failed,s.State);}
    [Fact] public void Missing_notice_coverage_collision_and_bad_notice_hash_refuse()
    {
        Assert.Throws<ArgumentException>(()=>ExportStaging.Create(Guid.NewGuid(),Manifest(),null,[],false));
        Assert.Throws<ArgumentException>(()=>ExportStaging.Create(Guid.NewGuid(),Manifest(),null,[Node("notice","CONTENT/root.bin",2)],true));
        var bad=new GameContentNode(new("notice","NOTICE.txt","text"),[2],new string('0',64),[],true);
        Assert.Throws<ArgumentException>(()=>ExportStaging.Create(Guid.NewGuid(),Manifest(),null,[bad],true));
    }
}
