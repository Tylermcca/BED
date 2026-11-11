using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

public sealed class SpriteTextureCacheTests
{
    private const string Id="11111111111111111111111111111111";
    private static AssetCatalog.Input Input(string id=Id,byte pixel=7,string policy="initial",string source="sign.png",bool texture=true)
    {
        byte[] encoded=[pixel];var record=new AssetRecord(id,source,Convert.ToHexStringLower(SHA256.HashData(encoded)),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","synthetic generation",Declared:AssetOrigin.Ours),AiUse.Forbidden);
        return new(record,encoded,[pixel,2,3,127],[],true,"fixture",policy,texture:texture?new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb):null);
    }
    private static void Publish(AssetCatalog c,params AssetCatalog.Input[] inputs)=>Assert.True(c.Publish(c.BeginRefresh(),inputs,out var why),why);
    private sealed class Resource:IDisposable
    {
        private readonly SceneTextureBudget.Reservation charge;
        internal readonly byte[] Pixels;internal readonly SceneSpriteFilter Filter;internal int Submits,Completes,Frees;
        internal Resource(SceneTextureBudget budget,AssetPreview preview,SceneSpriteFilter filter)
        {charge=budget.Reserve(preview.ByteCount,preview.ByteCount);Pixels=new byte[preview.ByteCount];preview.CopyTo(Pixels);Filter=filter;}
        internal void Submitted()=>Submits++;
        internal void Complete(){Completes++;charge.ReleaseStaging();}
        public void Dispose(){if(Frees++==0)charge.Dispose();}
    }
    private sealed class Rig:IDisposable
    {
        internal readonly SceneTextureBudget Budget=new();internal readonly List<Resource> Made=[];internal readonly SpriteTextureCache<Resource> Cache;
        internal Rig()=>Cache=new(new object(),Budget,(pixels,filter)=>{var r=new Resource(Budget,pixels,filter);Made.Add(r);return r;},r=>r.Submitted(),r=>r.Complete());
        public void Dispose()=>Cache.Close();
    }
    [Fact]
    public void Filter_variants_keep_exact_straight_alpha_bytes_and_share_images_only_with_the_same_filter()
    {
        using var c=new AssetCatalog();Publish(c,Input());using var r=new Rig();r.Cache.Safe(0);var selection=c.Select(Id)!;
        var nearest=r.Cache.Acquire("view",c,selection,SceneSpriteFilter.Nearest);var shared=r.Cache.Acquire("other",c,selection,SceneSpriteFilter.Nearest);var linear=r.Cache.Acquire("view",c,selection,SceneSpriteFilter.Linear);
        Assert.Same(nearest.Resource,shared.Resource);Assert.NotSame(nearest.Resource,linear.Resource);Assert.Equal(2,r.Made.Count);
        Assert.Equal(new byte[]{7,2,3,127},nearest.Resource.Pixels);Assert.Equal(nearest.Resource.Pixels,linear.Resource.Pixels);Assert.Equal(SceneSpriteFilter.Nearest,nearest.Resource.Filter);Assert.Equal(SceneSpriteFilter.Linear,linear.Resource.Filter);
        Assert.True(r.Cache.IsCurrent(nearest,SceneSpriteFilter.Nearest));Assert.False(r.Cache.IsCurrent(nearest,SceneSpriteFilter.Linear));Assert.Equal(8,r.Budget.ImageBytes);Assert.Equal(8,r.Budget.StagingBytes);
        r.Cache.RetainViews(new HashSet<string>{"view","other"});r.Cache.Submitted();r.Cache.Safe(1);Assert.Equal(0,nearest.Resource.Completes);Assert.Equal(0,linear.Resource.Frees);
        r.Cache.Acquire("view",c,selection,SceneSpriteFilter.Nearest);r.Cache.RetainViews(new HashSet<string>{"view"});r.Cache.Submitted();r.Cache.Safe(0);
        Assert.Equal(1,nearest.Resource.Completes);Assert.Equal(1,linear.Resource.Completes);Assert.Equal(1,linear.Resource.Frees);Assert.Equal(0,nearest.Resource.Frees);Assert.Equal(4,r.Budget.ImageBytes);Assert.Equal(0,r.Budget.StagingBytes);
    }
    [Theory][InlineData("image")][InlineData("staging")]
    public void Filter_variants_enforce_one_shared_budget_and_failed_allocation_releases_descriptor_charge(string limit)
    {
        using var c=new AssetCatalog();Publish(c,Input());using var r=new Rig();
        using var reserved=r.Budget.Reserve(limit=="image"?SceneTextureBudget.ImageLimit-4:1,limit=="staging"?SceneTextureBudget.StagingLimit-4:0);
        r.Cache.Safe(0);var selected=c.Select(Id)!;r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Nearest);
        var beforeImage=r.Budget.ImageBytes;var beforeStage=r.Budget.StagingBytes;
        Assert.Throws<InvalidOperationException>(()=>r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Linear));Assert.Single(r.Made);Assert.Equal(beforeImage,r.Budget.ImageBytes);Assert.Equal(beforeStage,r.Budget.StagingBytes);
    }
    [Theory][InlineData("source")][InlineData("policy")][InlineData("pending")][InlineData("closed")]
    public void Changed_admission_refuses_a_captured_selection_without_creating_replacement_resources(string change)
    {
        using var c=new AssetCatalog();Publish(c,Input());using var r=new Rig();r.Cache.Safe(0);var selected=c.Select(Id)!;var old=r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Nearest);
        switch(change){case "source":Publish(c,Input(pixel:9));break;case "policy":Publish(c,Input(policy:"changed"));break;case "pending":c.BeginRefresh();break;case "closed":c.Dispose();break;}
        Assert.Throws<InvalidOperationException>(()=>r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Linear));Assert.False(r.Cache.IsCurrent(old,SceneSpriteFilter.Nearest));Assert.Single(r.Made);Assert.Equal(0,old.Resource.Frees);
    }
    [Theory][InlineData("jpg",true)][InlineData("png",false)]
    public void Non_PNG_or_opaque_assets_refuse_before_any_texture_allocation(string extension,bool texture)
    {
        using var c=new AssetCatalog();Publish(c,Input(source:"sign."+extension,texture:texture));using var r=new Rig();r.Cache.Safe(0);
        Assert.Throws<InvalidOperationException>(()=>r.Cache.Acquire("view",c,c.Select(Id)!,SceneSpriteFilter.Nearest));Assert.Empty(r.Made);Assert.Equal(0,r.Budget.ImageBytes);
    }
    [Fact]
    public void Unique_texture_bound_counts_both_filter_variants_in_one_view()
    {
        using var c=new AssetCatalog();var inputs=Enumerable.Range(0,1025).Select(i=>Input(i.ToString("x32"),source:$"sprite_{i}.png")).ToArray();Publish(c,inputs);using var r=new Rig();r.Cache.Safe(0);
        for(var i=0;i<1024;i++)r.Cache.Acquire("view",c,c.Select(inputs[i].Guid)!,i%2==0?SceneSpriteFilter.Nearest:SceneSpriteFilter.Linear);
        var error=Assert.Throws<InvalidOperationException>(()=>r.Cache.Acquire("view",c,c.Select(inputs[1024].Guid)!,SceneSpriteFilter.Linear));Assert.Contains("1,024",error.Message);Assert.Equal(1024,r.Made.Count);
    }
    [Fact]
    public void Both_filters_and_in_flight_replacements_share_the_same_view_descriptor_bound()
    {
        using var c=new AssetCatalog();var inputs=Enumerable.Range(0,1024).Select(i=>Input(i.ToString("x32"),source:$"sprite_{i}.png")).ToArray();Publish(c,inputs);using var r=new Rig();r.Cache.Safe(0);
        foreach(var input in inputs)r.Cache.Acquire("view",c,c.Select(input.Guid)!,SceneSpriteFilter.Nearest);
        var selected=c.Select(inputs[0].Guid)!;
        Assert.Contains("descriptors",Assert.Throws<InvalidOperationException>(()=>r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Linear)).Message);Assert.Equal(1024,r.Made.Count);
        r.Cache.RetainViews(new HashSet<string>{"view"});r.Cache.Submitted();r.Cache.Safe(1);
        // Removing the old nearest bindings still retains their descriptor charges until fence zero completes.
        r.Cache.RetainViews(new HashSet<string>{"view"});
        Assert.Contains("descriptors",Assert.Throws<InvalidOperationException>(()=>r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Linear)).Message);
        r.Cache.Submitted();r.Cache.Safe(0);Assert.NotNull(r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Linear));Assert.Equal(1025,r.Made.Count);
    }
    [Fact]
    public void Frame_thread_filter_and_closed_owner_guards_precede_use()
    {
        using var c=new AssetCatalog();Publish(c,Input());using var r=new Rig();var selected=c.Select(Id)!;
        Assert.Throws<InvalidOperationException>(()=>r.Cache.Acquire("view",c,selected,SceneSpriteFilter.Nearest));r.Cache.Safe(0);
        Assert.Throws<ArgumentException>(()=>r.Cache.Acquire("view",c,selected,(SceneSpriteFilter)99));Assert.Empty(r.Made);
        Exception? other=null;var worker=new Thread(()=>other=Record.Exception(()=>r.Cache.Safe(1))){IsBackground=true};worker.Start();Assert.True(worker.Join(5000));Assert.IsType<InvalidOperationException>(other);r.Cache.Close();Assert.Throws<ObjectDisposedException>(()=>r.Cache.Safe(0));
    }
}
