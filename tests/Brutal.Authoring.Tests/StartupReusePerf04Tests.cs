using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Runtime;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

public sealed class StartupReusePerf04Tests(ITestOutputHelper output)
{
    private sealed class Fixture : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("startup-perf04-").FullName;
        public ProjectPaths Paths=>new(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")){Root=Root};
        public Fixture()
        {
            Directory.CreateDirectory(Paths.Data);Directory.CreateDirectory(Paths.Schemas);Directory.CreateDirectory(Path.Combine(Root,"assets"));
            for(var i=0;i<256;i++)
            {
                var path=Path.Combine(Root,"assets",$"mesh-{i:D3}.obj");
                // INVENTED: a distinct, source-owned triangle for each stable identity.
                File.WriteAllText(path,$"v {i} 0 0\nv {i+1} 0 0\nv {i} 1 0\nf 1 2 3\n");
                var record=new AssetRecord((i+1).ToString("x32"),Path.GetFileName(path),AssetRecord.HashOf(path),"obj",1,
                    JsonSerializer.SerializeToElement(new{scale=1}),new("fixture","self-authored","generated",Declared:AssetOrigin.Ours),AiUse.Forbidden);
                File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());
            }
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void Second_open_reuses_all_256_unchanged_inputs_and_the_same_published_generation()
    {
        using var fixture=new Fixture();var provider=new AssetCatalogProvisioner();var elapsed=Stopwatch.StartNew();
        AssetCatalog.Input[] inputs;
        using(var first=provider.Open(fixture.Paths)!)
        {
            output.WriteLine($"First open (256 generated OBJ): {elapsed.Elapsed.TotalMilliseconds:F2} ms");
            Assert.Empty(first.Problems);inputs=first.Catalog.CurrentInputs().ToArray();Assert.Equal(256,inputs.Length);
        }
        var saved=ProjectAssets.Open(fixture.Root);var pointer=Path.Combine(fixture.Root,".bed","cache","current.json");
        var bytes=File.ReadAllBytes(pointer);var stamp=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(pointer,stamp);
        elapsed.Restart();
        using(var second=provider.Open(fixture.Paths)!)
        {
            output.WriteLine($"Second open (256 generated OBJ): {elapsed.Elapsed.TotalMilliseconds:F2} ms");
            Assert.Empty(second.Problems);var reused=second.Catalog.CurrentInputs();Assert.Equal(inputs.Length,reused.Count);
            for(var i=0;i<inputs.Length;i++)Assert.Same(inputs[i],reused[i]);
            output.WriteLine("Derived unchanged inputs on second open: 0 (all 256 producer inputs retained by identity).");
        }
        var again=ProjectAssets.Open(fixture.Root);Assert.Equal(saved.Id,again.Id);Assert.Equal(saved.Generation,again.Generation);
        Assert.Equal(bytes,File.ReadAllBytes(pointer));Assert.Equal(stamp,File.GetLastWriteTimeUtc(pointer));
        Assert.Equal(256,again.ArtifactNames.Count);Assert.Equal(3,again.ReadMesh((256).ToString("x32")).Positions.Length);
    }
    [Fact]
    public void Same_length_same_timestamp_sidecar_change_invalidates_reuse_and_old_generation_remains_readable()
    {
        using var fixture=new Fixture();var provider=new AssetCatalogProvisioner();
        using(var first=provider.Open(fixture.Paths)!)Assert.Empty(first.Problems);
        var previous=ProjectAssets.Open(fixture.Root);var path=Path.Combine(fixture.Root,"assets","mesh-000.obj"+AssetRecord.Suffix);
        var original=File.ReadAllText(path);var stamp=File.GetLastWriteTimeUtc(path);var changed=original.Replace("\"scale\": 1","\"scale\": 3");
        Assert.NotEqual(original,changed);Assert.Equal(original.Length,changed.Length);File.WriteAllText(path,changed);File.SetLastWriteTimeUtc(path,stamp);
        using(var next=provider.Open(fixture.Paths)!)Assert.Empty(next.Problems);
        var current=ProjectAssets.Open(fixture.Root);Assert.NotEqual(previous.Id,current.Id);
        Assert.Contains(current.ReadMesh((1).ToString("x32")).Positions,p=>p.X==3);Assert.Contains(previous.ReadMesh((1).ToString("x32")).Positions,p=>p.X==1);
    }
    [Fact]
    public void Added_asset_invalidates_the_complete_set_even_when_existing_files_are_unchanged()
    {
        using var fixture=new Fixture();var provider=new AssetCatalogProvisioner();
        using(var first=provider.Open(fixture.Paths)!)Assert.Empty(first.Problems);
        var source=Path.Combine(fixture.Root,"assets","mesh-000.obj");var added=Path.Combine(fixture.Root,"assets","added.obj");File.Copy(source,added);
        var record=AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(source))) with{Guid=(257).ToString("x32"),Source="added.obj"};
        File.WriteAllText(AssetRecord.SidecarFor(added),record.Write());
        using(var next=provider.Open(fixture.Paths)!)
        {Assert.Empty(next.Problems);Assert.Equal(257,next.Catalog.CurrentInputs().Count);Assert.NotNull(next.Catalog.Select(record.Guid));}
        Assert.True(ProjectAssets.Open(fixture.Root).ContainsAsset(record.Guid));
    }
    [Fact]
    public void Ambiguous_inputs_are_not_retained_outside_the_valid_derived_byte_budget()
    {
        using var fixture=new Fixture();var provider=new AssetCatalogProvisioner();
        var sidecar=Path.Combine(fixture.Root,"assets","mesh-001.obj"+AssetRecord.Suffix);
        var record=AssetRecord.Read(File.ReadAllText(sidecar)) with{Guid=(1).ToString("x32")};File.WriteAllText(sidecar,record.Write());
        AssetCatalog.Input[] original;
        using(var first=provider.Open(fixture.Paths)!)
        {Assert.Equal(AssetCatalog.State.Ambiguous,first.Catalog.Resolve(record.Guid).State);original=first.Catalog.CurrentInputs().ToArray();}
        using(var second=provider.Open(fixture.Paths)!)
        {
            Assert.Equal(AssetCatalog.State.Ambiguous,second.Catalog.Resolve(record.Guid).State);
            var next=second.Catalog.CurrentInputs();Assert.Equal(original.Length,next.Count);
            for(var i=0;i<original.Length;i++)Assert.NotSame(original[i],next[i]);
        }
    }
    [Fact]
    public void Changed_source_does_not_reuse_stale_content()
    {
        using var fixture=new Fixture();var provider=new AssetCatalogProvisioner();
        using(var first=provider.Open(fixture.Paths)!)Assert.Empty(first.Problems);
        File.AppendAllText(Path.Combine(fixture.Root,"assets","mesh-000.obj"),"# changed after import\n");
        using(var next=provider.Open(fixture.Paths)!)
        {Assert.Contains(next.Problems,p=>p.Contains("changed since import"));Assert.Null(next.Catalog.Select((1).ToString("x32")));}
        Assert.False(ProjectAssets.Open(fixture.Root).ContainsAsset((1).ToString("x32")));
    }
}
