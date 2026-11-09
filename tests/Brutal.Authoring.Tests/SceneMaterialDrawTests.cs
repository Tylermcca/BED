using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class SceneMaterialDrawTests
{
    private const string Id="11111111111111111111111111111111";
    private static readonly string Hash=Convert.ToHexStringLower(SHA256.HashData(new byte[]{1}));
    private static MeshData Mesh(SceneMaterial? material=null)=>new([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2])
        {Uv0=[new(.1f,.2f),new(.7f,.3f),new(.2f,.9f)],Material=material??new(new(.2f,.4f,.6f,1),Id,Hash)};
    private static SceneRenderRequest Capture(params ViewportItem[] items)=>SceneRenderRequest.Capture(
        new(1,2,3,4,5,Vector2.Zero,new(800,600),Vector2.One),Camera.Default,items);
    private static AssetCatalog.Input Input(AiUse ai=AiUse.Forbidden)=>new(new(Id,"self.tga",Hash,"synthetic",1,JsonSerializer.SerializeToElement(new{}),
        new("self","self-authored","fixture",Declared:AssetOrigin.Ours),ai),[1],[1,2,3,255],[],true,"test","",
        texture:new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb));
    private static void Publish(AssetCatalog catalog,AssetCatalog.Input input)=>Assert.True(catalog.Publish(catalog.BeginRefresh(),[input],out _));
    [Fact]
    public void Packet_is_exactly_the_portable_128_byte_vertex_push_range()
    {
        Assert.Equal(128,Marshal.SizeOf<SceneMaterialPacket>());Assert.Equal(0,Marshal.OffsetOf<SceneMaterialPacket>(nameof(SceneMaterialPacket.A)).ToInt32());
        Assert.Equal(48,Marshal.OffsetOf<SceneMaterialPacket>(nameof(SceneMaterialPacket.ColorA)).ToInt32());
        Assert.Equal(96,Marshal.OffsetOf<SceneMaterialPacket>(nameof(SceneMaterialPacket.UvAB)).ToInt32());
        Assert.Equal(112,Marshal.OffsetOf<SceneMaterialPacket>(nameof(SceneMaterialPacket.UvCMode)).ToInt32());
    }
    [Fact]
    public void CPU_ids_order_transforms_and_clip_positions_are_preserved_without_second_UV_flip()
    {
        var mesh=Mesh();var request=Capture(new ViewportItem("far",Matrix4x4.CreateTranslation(2,0,1),mesh,0xffffffff),new ViewportItem("near",Matrix4x4.Identity,mesh,0xffffffff));
        var draws=SceneMaterialDraws.Build(request);var triangles=request.Triangles();Assert.Equal(2,draws.Count);
        for(var i=0;i<2;i++){Assert.Equal(triangles[i].ItemId,draws[i].ItemId);Assert.Equal(triangles[i].A,draws[i].Packet.A);Assert.Equal(triangles[i].B,draws[i].Packet.B);Assert.Equal(triangles[i].C,draws[i].Packet.C);}
        Assert.Equal(new Vector4(.1f,.2f,.7f,.3f),draws[0].Packet.UvAB);Assert.Equal(new Vector4(.2f,.9f,1,0),draws[0].Packet.UvCMode);
        mesh.Uv0![0]=new(99);Assert.Equal(.1f,draws[0].Packet.UvAB.X);
    }
    [Fact]
    public void Linear_tint_is_not_decoded_again_and_opaque_alpha_ignores_old_packed_display_alpha()
    {
        var material=new SceneMaterial(new(.5f,.25f,.75f,1));var draw=Assert.Single(SceneMaterialDraws.Build(Capture(new ViewportItem("x",Matrix4x4.Identity,Mesh(material),0x000000ff))));
        Assert.Equal(material.BaseColor,draw.Packet.ColorA);Assert.Equal(material.BaseColor,draw.Packet.ColorB);Assert.Equal(material.BaseColor,draw.Packet.ColorC);
        Assert.Equal(0,draw.Packet.UvCMode.Z);Assert.Equal(draw.Packet.ColorA,SceneMaterialDraws.WithAvailability(draw,false).ColorA);
    }
    [Fact]
    public void Unavailable_texture_uses_explicit_placeholder_mode_and_never_changes_captured_geometry_or_identity()
    {
        var draw=Assert.Single(SceneMaterialDraws.Build(Capture(new ViewportItem("x",Matrix4x4.Identity,Mesh(),0xffffffff))));
        var refused=SceneMaterialDraws.WithAvailability(draw,false);Assert.Equal(2,refused.UvCMode.Z);Assert.Equal(draw.Packet.A,refused.A);
        Assert.Equal(1,draw.Packet.UvCMode.Z);Assert.Equal(Id,draw.Material.TextureAssetGuid);Assert.Equal(Hash,draw.Material.TextureSourceHash);
        Assert.Equal(1,SceneMaterialDraws.WithAvailability(draw,true).UvCMode.Z);
    }
    [Fact]
    public void Production_owner_enforces_view_target_budget_before_material_allocation()
    {
        var owner=typeof(EditorApp).Assembly.GetType("Brutal.Authoring.Editor.App.SceneDeviceOwner")!;
        var method=Assert.Single(owner.GetMethods(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance),
            m=>m.Name=="Target"&&m.GetParameters().Length==4);
        var calls=SceneFrameLifecycleTests.Calls(method);
        var target=Assert.Single(calls,c=>c.Method.Name=="Target"&&c.Method.GetParameters().Length==3);
        var material=Assert.Single(calls,c=>c.Method.Name=="Material");Assert.True(target.Offset<material.Offset);
    }
    [Fact]
    public void Catalog_selection_requires_current_exact_hash_metadata_and_refresh_policy_lifetime()
    {
        var material=Mesh().Material!;Assert.False(SceneMaterialDraws.TryTexture(null,material,out _,out var why));Assert.Contains("admitted",why);
        using var catalog=new AssetCatalog();Publish(catalog,Input());Assert.True(SceneMaterialDraws.TryTexture(catalog,material,out var first,out _));
        Assert.False(SceneMaterialDraws.TryTexture(catalog,new(new(1,1,1,1),Id,new string('f',64)),out _,out _));
        catalog.BeginRefresh();Assert.False(SceneMaterialDraws.TryTexture(catalog,material,out _,out _));
        Publish(catalog,Input(AiUse.Unknown));Assert.False(catalog.TryResolve(first!,out _,out _));Assert.True(SceneMaterialDraws.TryTexture(catalog,material,out var changed,out _));
        Assert.NotEqual(first!.PolicyRevision,changed!.PolicyRevision);catalog.Dispose();Assert.False(SceneMaterialDraws.TryTexture(catalog,material,out _,out _));
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Generation_normalizes_self_authored_two_by_two_TGA_rows_to_top_left_without_shader_UV_inversion(bool topOrigin)
    {
        byte[][] colors=[[255,0,0],[0,255,0],[0,0,255],[255,255,255]];
        byte[] tga=new byte[30];tga[2]=2;tga[12]=2;tga[14]=2;tga[16]=24;tga[17]=topOrigin?(byte)32:(byte)0;
        for(var i=0;i<4;i++){var rgb=colors[topOrigin?i:(i<2?i+2:i-2)];tga[18+i*3]=rgb[2];tga[19+i*3]=rgb[1];tga[20+i*3]=rgb[0];}
        var hash=Convert.ToHexStringLower(SHA256.HashData(tga));var record=new AssetRecord(Id,"self.tga",hash,"fixture",1,JsonSerializer.SerializeToElement(new{}),
            new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
        Assert.True(TextureAssetGeneration.TryCreateInput(record,tga,"tga",[],true,"test","",out var input,out var why),why);
        using var catalog=new AssetCatalog();Publish(catalog,input!);Assert.True(catalog.TryResolveTexture(catalog.Select(Id)!,hash,out var lease,out why),why);
        Assert.Equal(2,lease!.Width);Assert.Equal(2,lease.Height);var rgba=new byte[16];lease.CopyTo(rgba);
        Assert.Equal(new byte[]{255,0,0,255,0,255,0,255,0,0,255,255,255,255,255,255},rgba);
    }

    private sealed class ManagedTexture(SceneTextureBudget budget,int bytes):IDisposable
    {
        private readonly SceneTextureBudget.Reservation charge=budget.Reserve(bytes,bytes);
        public int Freed;
        public void UploadComplete()=>charge.ReleaseStaging();
        public void Dispose(){charge.Dispose();Freed++;}
    }
    [Fact]
    public void A_live_view_releases_materials_removed_from_its_latest_request_only_after_sampling_fences_retire()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var budget=new SceneTextureBudget();
        var cache=new SceneMaterialCache<ManagedTexture>(new object(),budget,p=>new(budget,p.ByteCount),_=>{},r=>r.UploadComplete());
        try
        {
            cache.Safe(0);var lease=cache.Acquire("view",catalog,catalog.Select(Id)!,Hash);cache.RetainViews(new HashSet<string>{"view"});cache.Submitted();
            cache.Safe(1);cache.RetainViews(new HashSet<string>{"view"});Assert.Equal(4,budget.ImageBytes);Assert.Equal(0,lease.Resource.Freed);cache.Submitted();
            cache.Safe(0);Assert.Equal(1,lease.Resource.Freed);Assert.Equal(0,budget.ImageBytes);Assert.Equal(0,budget.StagingBytes);
            var replacement=cache.Acquire("view",catalog,catalog.Select(Id)!,Hash);Assert.NotSame(lease.Resource,replacement.Resource);
        }
        finally{cache.Close();}
    }

}
