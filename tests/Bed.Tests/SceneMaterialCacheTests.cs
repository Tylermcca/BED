using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor.App;
using Xunit;

namespace Bed.Tests;

public sealed class SceneMaterialCacheTests
{
    private const string Id="11111111111111111111111111111111";
    private static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
    private static AssetRecord Record(string id,byte[] source)=>new(id,"synthetic.tga",Hash(source),"fixture",1,JsonSerializer.SerializeToElement(new {}),
        new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
    private static AssetCatalog.Input Input(byte value=1,string id=Id,AssetCatalog.TextureInfo? info=null,bool metadata=true)
    {
        byte[] source=[value];return new(Record(id,source),source,[value,2,3,255],[],true,"fixture","",
            texture:metadata?info??new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb):null);
    }
    private static void Publish(AssetCatalog catalog,params AssetCatalog.Input[] inputs)=>Assert.True(catalog.Publish(catalog.BeginRefresh(),inputs,out var why),why);
    private sealed class Resource(SceneTextureBudget budget,int bytes):IDisposable
    {
        private readonly SceneTextureBudget.Reservation charge=budget.Reserve(bytes,bytes);
        public int Submitted,Completed,Frees;
        public void Complete(){Completed++;charge.ReleaseStaging();}
        public void Dispose(){if(Frees++==0)charge.Dispose();}
    }
    private sealed class Rig:IDisposable
    {
        public readonly SceneTextureBudget Budget=new();
        public readonly List<Resource> Made=[];
        public readonly SceneMaterialCache<Resource> Cache;
        public Action? DuringCreate;
        public Rig()=>Cache=new(new object(),Budget,p=>{var r=new Resource(Budget,p.ByteCount);Made.Add(r);DuringCreate?.Invoke();return r;},r=>r.Submitted++,r=>r.Complete());
        public void Dispose()=>Cache.Close();
    }

    [Fact]
    public void Texture_generation_decodes_a_pinned_self_authored_TGA_and_publishes_explicit_srgb_metadata()
    {
        byte[] tga=new byte[21];tga[2]=2;tga[12]=1;tga[14]=1;tga[16]=24;tga[17]=32;tga[18]=3;tga[19]=2;tga[20]=1;
        Assert.True(TextureAssetGeneration.TryCreateInput(Record(Id,tga),tga,"tga",[],true,"texture-v1","",out var input,out var why),why);
        using var catalog=new AssetCatalog();Publish(catalog,input!);var selection=catalog.Select(Id)!;
        Assert.True(catalog.TryResolveTexture(selection,Hash(tga),out var lease,out why),why);
        Assert.Equal(Hash(tga),lease!.SourceHash);Assert.Equal(AssetCatalog.TextureEncoding.Rgba8Srgb,lease.TextureFormat);
        Assert.Equal(1,lease.Width);Assert.Equal(1,lease.Height);var bytes=new byte[4];lease.CopyTo(bytes);Assert.Equal(new byte[]{1,2,3,255},bytes);
        Assert.False(TextureAssetGeneration.TryCreateInput(Record(Id,[7]),tga,"tga",[],true,"texture-v1","",out _,out _));
    }

    [Fact]
    public void Ordinary_leases_remain_readable_but_material_resolution_refuses_missing_invalid_or_mismatched_metadata()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input(metadata:false));var selection=catalog.Select(Id)!;
        Assert.True(catalog.TryResolve(selection,out var ordinary,out _));Assert.Equal(Hash([1]),ordinary!.SourceHash);Assert.Null(ordinary.TextureFormat);
        Assert.False(catalog.TryResolveTexture(selection,Hash([1]),out _,out var why));Assert.Contains("declared",why);
        Publish(catalog,Input());selection=catalog.Select(Id)!;Assert.False(catalog.TryResolveTexture(selection,Hash([2]),out _,out _));
        Publish(catalog,Input(info:new(2,1,AssetCatalog.TextureEncoding.Rgba8Srgb)));Assert.Equal(AssetCatalog.State.Failed,catalog.Resolve(Id).State);
        Publish(catalog,Input(info:new(1,1,(AssetCatalog.TextureEncoding)99)));Assert.Null(catalog.Select(Id));
    }

    [Fact]
    public void Texture_bytes_and_dimensions_participate_in_revision_and_are_owned_copies()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var selected=catalog.Select(Id)!;
        byte[] source=[1],rgba=[9,8,7,255];var input=new AssetCatalog.Input(Record(Id,source),source,rgba,[],true,"fixture","",texture:new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb));
        rgba[0]=0;Publish(catalog,input);Assert.False(catalog.TryResolve(selected,out _,out _));
        Assert.True(catalog.TryResolveTexture(catalog.Select(Id)!,Hash(source),out var lease,out _));var pixels=new byte[4];lease!.CopyTo(pixels);Assert.Equal(9,pixels[0]);
        Publish(catalog,new AssetCatalog.Input(Record(Id,source),source,[1,2,3,255],[],true,"fixture","",texture:new(2,1,AssetCatalog.TextureEncoding.Rgba8Srgb)));
        Assert.Null(catalog.Select(Id));
    }

    [Fact]
    public void Shared_image_is_uploaded_once_and_staging_releases_only_at_its_submitted_fence()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var selected=catalog.Select(Id)!;using var rig=new Rig();
        rig.Cache.Safe(0);var a=rig.Cache.Acquire("a",catalog,selected,Hash([1]));var b=rig.Cache.Acquire("b",catalog,selected,Hash([1]));
        Assert.Same(a.Resource,b.Resource);Assert.Single(rig.Made);Assert.Equal(4,rig.Budget.ImageBytes);Assert.Equal(4,rig.Budget.StagingBytes);
        rig.Cache.Submitted();Assert.Equal(1,a.Resource.Submitted);
        rig.Cache.Safe(1);Assert.False(rig.Cache.IsCurrent(a));rig.Cache.Acquire("b",catalog,selected,Hash([1]));rig.Cache.Submitted();Assert.Equal(0,a.Resource.Completed);
        rig.Cache.Safe(0);Assert.Equal(1,a.Resource.Completed);Assert.Equal(0,rig.Budget.StagingBytes);Assert.Equal(1,a.Resource.Submitted);
    }

    [Fact]
    public void Replaced_content_holds_overlap_until_all_old_sampling_slots_are_safe()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());using var rig=new Rig();
        rig.Cache.Safe(0);var old=rig.Cache.Acquire("a",catalog,catalog.Select(Id)!,Hash([1]));rig.Cache.Submitted();
        rig.Cache.Safe(1);rig.Cache.Acquire("a",catalog,catalog.Select(Id)!,Hash([1]));rig.Cache.Submitted();
        Publish(catalog,Input(2));rig.Cache.Safe(0);var next=rig.Cache.Acquire("a",catalog,catalog.Select(Id)!,Hash([2]));
        Assert.NotSame(old.Resource,next.Resource);Assert.Equal(8,rig.Budget.ImageBytes);Assert.Equal(0,old.Resource.Frees);rig.Cache.Submitted();
        rig.Cache.Safe(1);Assert.Equal(1,old.Resource.Frees);Assert.Equal(4,rig.Budget.ImageBytes);
    }

    [Fact]
    public void Refresh_foreign_hash_close_and_allocation_races_never_expose_an_old_resource_as_current()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());using var rig=new Rig();rig.Cache.Safe(0);
        var lease=rig.Cache.Acquire("a",catalog,catalog.Select(Id)!,Hash([1]));
        rig.Cache.RetainViews(new HashSet<string>());Assert.False(rig.Cache.IsCurrent(lease));
        rig.Cache.Submitted();rig.Cache.Safe(1);rig.Cache.Submitted();rig.Cache.Safe(0);Assert.Equal(1,lease.Resource.Frees);
        rig.DuringCreate=()=>catalog.BeginRefresh();Assert.Throws<InvalidOperationException>(()=>rig.Cache.Acquire("new",catalog,catalog.Select(Id)!,Hash([1])));
        Assert.Equal(0,rig.Budget.ImageBytes);Assert.Equal(0,rig.Budget.StagingBytes);Assert.Equal(1,rig.Made[^1].Frees);
    }

    [Fact]
    public void Per_view_descriptor_limit_refuses_without_losing_existing_bindings_or_partial_image_charge()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Enumerable.Range(1,1025).Select(i=>Input(id:i.ToString("x32"))).ToArray());
        using var rig=new Rig();rig.Cache.Safe(0);
        for(var i=1;i<=1024;i++)rig.Cache.Acquire("a",catalog,catalog.Select(i.ToString("x32"))!,Hash([1]));
        Assert.Equal(4096,rig.Budget.ImageBytes);Assert.Throws<InvalidOperationException>(()=>rig.Cache.Acquire("a",catalog,catalog.Select(1025.ToString("x32"))!,Hash([1])));
        Assert.Equal(4096,rig.Budget.ImageBytes);Assert.Equal(1024,rig.Made.Count);
    }
}
