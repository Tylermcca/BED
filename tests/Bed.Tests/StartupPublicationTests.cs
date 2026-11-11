using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Xunit;
namespace Bed.Tests;
public sealed class StartupPublicationTests
{
    private const string Id="12345678901234567890123456789012";
    private sealed class Fixture : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("startup-publication-").FullName;
        public string Assets=>Path.Combine(Root,"assets");public string Source=>Path.Combine(Assets,"mesh.obj");
        public string Pointer=>Path.Combine(Root,".bed","cache","current.json");
        public ProjectPaths Paths=>new(Path.Combine(Root,"data"),Path.Combine(Root,"schemas"));
        public Fixture(){Directory.CreateDirectory(Paths.Data);Directory.CreateDirectory(Paths.Schemas);}
        public void Model()
        {
            Directory.CreateDirectory(Assets);File.WriteAllText(Source,"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            var r=new AssetRecord(Id,"mesh.obj",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Source))),"obj",1,JsonSerializer.SerializeToElement(new{scale=1}),new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllText(AssetRecord.SidecarFor(Source),r.Write());
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void Startup_publishes_real_mesh_for_runtime_and_reuses_unchanged_generation_without_pointer_rewrite()
    {
        using var f=new Fixture();f.Model();var source=File.ReadAllBytes(f.Source);var sidecar=File.ReadAllBytes(AssetRecord.SidecarFor(f.Source));
        using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)!){Assert.Empty(admitted.Problems);Assert.NotNull(admitted.Catalog.Select(Id));}
        var runtime=ProjectAssets.Open(f.Root);Assert.True(runtime.ContainsAsset(Id));Assert.Equal(3,runtime.ReadMesh(Id).Positions.Length);Assert.Null(runtime.ReadMesh(Id).Uv0);
        var pointer=File.ReadAllBytes(f.Pointer);var stamp=new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc);File.SetLastWriteTimeUtc(f.Pointer,stamp);
        using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)!){Assert.Empty(admitted.Problems);}
        var again=ProjectAssets.Open(f.Root);Assert.Equal(runtime.Id,again.Id);Assert.Equal(runtime.Generation,again.Generation);Assert.Equal(pointer,File.ReadAllBytes(f.Pointer));Assert.Equal(stamp,File.GetLastWriteTimeUtc(f.Pointer));
        Assert.Equal(source,File.ReadAllBytes(f.Source));Assert.Equal(sidecar,File.ReadAllBytes(AssetRecord.SidecarFor(f.Source)));Assert.Single(Directory.GetDirectories(Path.Combine(f.Root,".bed","cache","generations")));
    }
    [Theory]
    [InlineData(true,true)][InlineData(false,true)]
    [InlineData(true,false)][InlineData(false,false)]
    public void Nested_data_uses_explicit_root_then_folder_for_assets_and_runtime_cache(bool explicitRoot,bool hasAssets)
    {
        using var f=new Fixture();if(hasAssets)f.Model();
        var data=Directory.CreateDirectory(Path.Combine(f.Root,"nested","data")).FullName;
        var paths=new ProjectPaths(data,f.Paths.Schemas)
            {Root=explicitRoot?f.Root:null,Folder=explicitRoot?Path.Combine(f.Root,"unused-folder"):f.Root};
        var before=Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,File.ReadAllBytes);
        using(var admitted=new AssetCatalogProvisioner().Open(paths))
        {
            if(hasAssets){Assert.NotNull(admitted);Assert.Empty(admitted.Problems);Assert.NotNull(admitted.Catalog.Select(Id));}
            else Assert.Null(admitted);
        }
        Assert.True(File.Exists(f.Pointer));
        var runtime=ProjectAssets.Open(f.Root);
        if(hasAssets){Assert.True(runtime.ContainsAsset(Id));Assert.Equal(3,runtime.ReadMesh(Id).Positions.Length);}
        else{Assert.Empty(runtime.ArtifactNames);Assert.False(Directory.Exists(f.Assets));}
        Assert.False(Directory.Exists(Path.Combine(f.Root,"nested",".bed")));
        Assert.False(Directory.Exists(Path.Combine(data,".bed")));
        Assert.False(Directory.Exists(Path.Combine(f.Root,"unused-folder")));
        foreach(var (path,bytes) in before)Assert.Equal(bytes,File.ReadAllBytes(path));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Empty_projects_publish_empty_generation_without_creating_assets_or_rewriting(bool folder)
    {
        using var f=new Fixture();if(folder)Directory.CreateDirectory(f.Assets);
        using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)){if(folder)Assert.NotNull(admitted);else Assert.Null(admitted);}
        var first=ProjectAssets.Open(f.Root);Assert.Empty(first.ArtifactNames);Assert.Equal(folder,Directory.Exists(f.Assets));
        using(var again=new AssetCatalogProvisioner().Open(f.Paths)){}
        Assert.Equal(first.Id,ProjectAssets.Open(f.Root).Id);Assert.Equal(folder,Directory.Exists(f.Assets));
    }
    [Fact]
    public void Changed_settings_publish_new_derived_content_and_keep_previous_generation_readable()
    {
        using var f=new Fixture();f.Model();using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)){}
        var first=ProjectAssets.Open(f.Root);var previous=first.ReadAsset(Id);
        var path=AssetRecord.SidecarFor(f.Source);var record=AssetRecord.Read(File.ReadAllText(path)) with{Settings=JsonSerializer.SerializeToElement(new{scale=3})};File.WriteAllText(path,record.Write());var sidecar=File.ReadAllText(path);
        using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)){}
        var next=ProjectAssets.Open(f.Root);Assert.Equal(first.Generation+1,next.Generation);Assert.NotEqual(first.Id,next.Id);Assert.Contains(next.ReadMesh(Id).Positions,p=>p.X==3);
        Assert.Equal(previous,first.ReadAsset(Id));Assert.Equal(sidecar,File.ReadAllText(path));Assert.Equal(2,Directory.GetDirectories(Path.Combine(f.Root,".bed","cache","generations")).Length);
    }
    [Fact]
    public void Publisher_lock_refuses_startup_without_changing_pointer_or_sources()
    {
        using var f=new Fixture();f.Model();using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)){}
        var pointer=File.ReadAllBytes(f.Pointer);var sidecar=File.ReadAllBytes(AssetRecord.SidecarFor(f.Source));
        using(var owner=new AssetCatalogPublisher(f.Root))Assert.Throws<IOException>(()=>new AssetCatalogProvisioner().Open(f.Paths));
        Assert.Equal(pointer,File.ReadAllBytes(f.Pointer));Assert.Equal(sidecar,File.ReadAllBytes(AssetRecord.SidecarFor(f.Source)));Assert.True(ProjectAssets.Open(f.Root).ContainsAsset(Id));
    }
    [Fact]
    public void Failed_asset_is_not_silently_kept_from_an_older_generation()
    {
        using var f=new Fixture();f.Model();using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)){}
        File.AppendAllText(f.Source,"# changed source\n");var changed=File.ReadAllText(f.Source);var sidecar=File.ReadAllText(AssetRecord.SidecarFor(f.Source));
        using(var admitted=new AssetCatalogProvisioner().Open(f.Paths)!){Assert.Contains(admitted.Problems,p=>p.Contains("changed since import"));Assert.Null(admitted.Catalog.Select(Id));}
        Assert.False(ProjectAssets.Open(f.Root).ContainsAsset(Id));Assert.Equal(changed,File.ReadAllText(f.Source));Assert.Equal(sidecar,File.ReadAllText(AssetRecord.SidecarFor(f.Source)));
    }
}
