using System.Text;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class AssetCatalogPublisherTests : IDisposable
{
    private readonly string root=Directory.CreateTempSubdirectory("catalog-publish-").FullName;
    private string Cache=>Path.Combine(root,".bed","cache");
    private static Dictionary<string,byte[]> Content(string value)=>new() { ["model.bin"]=Encoding.UTF8.GetBytes(value) };
    public void Dispose()=>Directory.Delete(root,true);

    [Fact]
    public void Complete_snapshot_publishes_once_and_reopens_without_editor_or_source_writes()
    {
        Directory.CreateDirectory(Path.Combine(root,"data"));File.WriteAllText(Path.Combine(root,"data","keep.txt"),"unchanged");
        string id;
        using(var p=new AssetCatalogPublisher(root))
        {
            var source=Content("first");var stage=p.Prepare("revision-1",source);source["model.bin"][0]=0;
            Assert.Null(p.Current);Assert.True(p.Publish(stage,out _));Assert.Equal(1,p.Current!.Generation);id=p.Current.Id;
            Assert.False(p.Publish(stage,out _));Assert.False(p.Cancel(stage));
            Assert.Equal("first",File.ReadAllText(Path.Combine(Cache,"generations",id,"model.bin")));
        }
        using(var reopened=new AssetCatalogPublisher(root)){Assert.Equal(id,reopened.Current!.Id);Assert.Equal("revision-1",reopened.Current.Revision);}
        Assert.Equal("unchanged",File.ReadAllText(Path.Combine(root,"data","keep.txt")));
    }
    [Fact]
    public void Cancel_and_stale_predecessor_keep_last_published_pointer()
    {
        using var p=new AssetCatalogPublisher(root);var first=p.Prepare("r1",Content("one"));Assert.True(p.Publish(first,out _));
        var pointer=File.ReadAllBytes(Path.Combine(Cache,"current.json"));
        var canceled=p.Prepare("r2",Content("two"));Assert.True(p.Cancel(canceled));Assert.False(p.Publish(canceled,out _));
        Assert.Equal(pointer,File.ReadAllBytes(Path.Combine(Cache,"current.json")));
        var a=p.Prepare("r3",Content("three"));var b=p.Prepare("r4",Content("four"));Assert.True(p.Publish(a,out _));
        Assert.False(p.Publish(b,out var why));Assert.Contains("predecessor",why);Assert.Equal("r3",p.Current!.Revision);
    }
    [Fact]
    public void Missing_or_changed_artifact_refuses_without_replacing_pointer()
    {
        using var p=new AssetCatalogPublisher(root);var good=p.Prepare("r1",Content("one"));p.Publish(good,out _);
        var pointer=File.ReadAllBytes(Path.Combine(Cache,"current.json"));var bad=p.Prepare("r2",Content("two"));
        var stage=Assert.Single(Directory.GetDirectories(Path.Combine(Cache,"staging")));
        File.WriteAllText(Path.Combine(stage,"model.bin"),"different");
        Assert.Throws<InvalidDataException>(()=>p.Publish(bad,out _));Assert.Equal(pointer,File.ReadAllBytes(Path.Combine(Cache,"current.json")));
    }
    [Fact]
    public void Recovery_removes_only_owned_unpublished_staging_and_retains_published_generations()
    {
        string published;
        using(var p=new AssetCatalogPublisher(root)){var s=p.Prepare("r1",Content("one"));p.Publish(s,out _);published=p.Current!.Id;p.Prepare("abandoned",Content("two"));}
        var unrelated=Path.Combine(Cache,"staging",new string('a',32));Directory.CreateDirectory(unrelated);File.WriteAllText(Path.Combine(unrelated,"user.txt"),"keep");
        using(var p=new AssetCatalogPublisher(root))
        { Assert.Equal(published,p.Current!.Id);Assert.Equal(new[]{unrelated},Directory.GetDirectories(Path.Combine(Cache,"staging")));Assert.True(Directory.Exists(Path.Combine(Cache,"generations",published))); }
        Assert.Equal("keep",File.ReadAllText(Path.Combine(unrelated,"user.txt")));
    }
    [Theory]
    [InlineData("../escape")][InlineData("C:\\escape")][InlineData("folder/file")][InlineData("manifest.json")][InlineData("OWNER.TXT")][InlineData("COM1.bin")][InlineData("AUX")]
    public void Artifact_paths_are_refused_before_staging(string name)
    {
        using var p=new AssetCatalogPublisher(root);
        Assert.Throws<ArgumentException>(()=>p.Prepare("r1",new Dictionary<string,byte[]>{{name,[1]}}));
        Assert.Empty(Directory.GetDirectories(Path.Combine(Cache,"staging")));Assert.Null(p.Current);
    }
    [Fact]
    public void Another_owner_or_foreign_stage_cannot_publish()
    {
        using var p=new AssetCatalogPublisher(root);Assert.Throws<IOException>(()=>new AssetCatalogPublisher(root));
        var other=Path.Combine(root,"other");Directory.CreateDirectory(other);
        using var q=new AssetCatalogPublisher(other);var foreign=q.Prepare("r1",Content("other"));
        Assert.False(p.Publish(foreign,out _));Assert.Null(p.Current);
        q.Dispose();Assert.False(q.Publish(foreign,out _));
    }
    [Fact]
    public void Corrupt_published_pointer_refuses_recovery_before_deleting_staging()
    {
        string stage;
        using(var p=new AssetCatalogPublisher(root)){var s=p.Prepare("r1",Content("one"));p.Publish(s,out _);p.Prepare("pending",Content("two"));stage=Assert.Single(Directory.GetDirectories(Path.Combine(Cache,"staging")));}
        File.WriteAllText(Path.Combine(Cache,"current.json"),"{\"Id\":\"../outside\",\"ManifestHash\":\"x\"}");
        Assert.Throws<InvalidDataException>(()=>new AssetCatalogPublisher(root));Assert.True(Directory.Exists(stage));
    }
}
