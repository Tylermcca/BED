using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class AssetSourceFilesTests
{
    private const string Id="11111111111111111111111111111111";
    private static string Hash(byte b)=>Convert.ToHexStringLower(SHA256.HashData(new byte[]{b}));
    private static AssetRecord Record()=>new(Id,"self.gltf",Hash(1),"gltf",1,JsonSerializer.SerializeToElement(new{}),new("self","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
    private static AssetCatalog.Input Input(IEnumerable<AssetCatalog.SourceFile>? files)=>new(Record(),[1],[9],[],true,"BEDMESH1","same-policy",sourceFiles:files);
    private static void Publish(AssetCatalog c,AssetCatalog.Input value)=>Assert.True(c.Publish(c.BeginRefresh(),[value],out _));
    [Fact]
    public void External_source_metadata_is_copied_sorted_and_read_only_without_inventing_GUIDs()
    {
        var files=new List<AssetCatalog.SourceFile>{new("b.bin",Hash(2)),new("a.bin",Hash(3))};var input=Input(files);files[0]=new("other.bin",Hash(4));
        Assert.Equal(new[]{"a.bin","b.bin"},input.SourceFiles.Select(f=>f.RelativePath));Assert.Equal(Hash(2),input.SourceFiles[1].SourceHash);
        Assert.Throws<NotSupportedException>(()=>((IList<AssetCatalog.SourceFile>)input.SourceFiles).Clear());
        using var c=new AssetCatalog();Publish(c,input);Assert.Same(input,c.CurrentInputs()[0]);Assert.Equal(Hash(3),c.CurrentInputs()[0].SourceFiles[0].SourceHash);
    }
    [Fact]
    public void File_hashes_invalidate_content_with_unchanged_primary_bytes_settings_policy_and_stable_canonical_order()
    {
        using var c=new AssetCatalog();var files=new[]{new AssetCatalog.SourceFile("b.bin",Hash(2)),new AssetCatalog.SourceFile("a.bin",Hash(3))};Publish(c,Input(files));var selected=c.Select(Id)!;
        Assert.True(c.TryResolve(selected,out var first,out _));Publish(c,Input(files.Reverse()));Assert.True(c.TryResolve(selected,out var same,out _));Assert.Equal(first!.ContentRevision,same!.ContentRevision);
        Publish(c,Input([new("a.bin",Hash(3)),new("b.bin",Hash(4))]));Assert.False(c.TryResolve(selected,out _,out _));var changed=c.Resolve(Id).Current!;
        Assert.Equal(first.SourceHash,changed.SourceHash);Assert.Equal(first.PolicyRevision,changed.PolicyRevision);Assert.NotEqual(first.ContentRevision,changed.ContentRevision);
    }
    [Fact]
    public void Reimport_copy_preserves_other_inputs_and_replaces_only_its_explicit_read_receipt()
    {
        var input=Input([new("a.bin",Hash(2))]);var preserved=input.WithReimportedContent(Record(),[1],[8],"BEDMESH1");
        Assert.Equal(input.SourceFiles,preserved.SourceFiles);var cleared=input.WithReimportedContent(Record(),[1],[8],"BEDMESH1",[]);Assert.Empty(cleared.SourceFiles);Assert.Single(input.SourceFiles);
        Assert.Throws<ArgumentException>(()=>Input([new("a.bin",Hash(2)),new("a.bin",Hash(3))]));
        Assert.Throws<ArgumentException>(()=>Input([new("/absolute.bin",Hash(2))]));Assert.Throws<ArgumentException>(()=>Input([new("a.bin","not-a-hash")]));
    }
}
