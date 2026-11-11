using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SpritePresentationTests
{
    private const string Id="11111111111111111111111111111111";
    private static ImTextureRef Texture(int value=91)=>new(){_TexID=new ImTextureID((nint)value)};
    private static void Publish(AssetCatalog c,string policy="initial")
    {
        byte[] source=[1];var record=new AssetRecord(Id,"sign.png",Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","synthetic generation",Declared:AssetOrigin.Ours),AiUse.Forbidden);
        Assert.True(c.Publish(c.BeginRefresh(),[new AssetCatalog.Input(record,source,[10,20,30,127],[],true,"fixture",policy,texture:new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb))],out var why),why);
    }
    [Theory]
    [InlineData("valid")][InlineData("selection")][InlineData("filter")][InlineData("zero")][InlineData("resource")][InlineData("pending")][InlineData("policy")][InlineData("late")][InlineData("closed")][InlineData("context")][InlineData("throw")]
    public void Borrowed_images_require_exact_selection_filter_resource_context_and_current_admission(string mode)
    {
        using var ui=new HeadlessHost{Drawing=false};using var catalog=new AssetCatalog();Publish(catalog);var selection=catalog.Select(Id)!;var calls=0;
        var original=Im.GetCurrentContext();ImGuiContextPtr other=default;
        using var presenter=ScenePresentation.Bind((_,_,_)=>null).WithSprites((_,_,_,_)=>
        {
            calls++;
            switch(mode){case "pending":catalog.BeginRefresh();break;case "policy":Publish(catalog,"new policy");break;case "closed":catalog.Dispose();break;case "context":other=Im.CreateContext();Im.SetCurrentContext(other);break;case "throw":throw new InvalidOperationException("Synthetic upload refusal");}
            return new(mode=="selection"?selection with{Admission=Guid.NewGuid()}:selection,mode=="filter"?SceneSpriteFilter.Linear:SceneSpriteFilter.Nearest,mode=="zero"?default:Texture(),()=>{if(mode=="late")catalog.BeginRefresh();return mode!="resource";},"borrowed");
        });
        try
        {
            presenter.BeginFrame();var ok=presenter.TrySpriteImage("flat",catalog,selection,SceneSpriteFilter.Nearest,out var texture,out var why);
            Assert.Equal(mode=="valid",ok);Assert.Equal(1,calls);
            if(ok){Assert.Equal(Texture()._TexID,texture._TexID);Assert.Equal("borrowed",why);}else{Assert.Equal(default,texture);Assert.NotEmpty(why);}
        }
        finally{if(!other.Equals(default(ImGuiContextPtr))){Im.DestroyContext(other);Im.SetCurrentContext(original);}}
    }
    [Fact]
    public void Foreign_opaque_stale_or_invalid_requests_do_not_call_the_producer_and_closed_or_other_thread_bindings_refuse()
    {
        using var ui=new HeadlessHost{Drawing=false};using var c=new AssetCatalog();using var foreign=new AssetCatalog();Publish(c);Publish(foreign);var selection=c.Select(Id)!;var calls=0;
        using var p=ScenePresentation.Bind((_,_,_)=>null).WithSprites((_,_,s,f)=>{calls++;return new(s,f,Texture(),()=>true);});
        Assert.False(p.TrySpriteImage("flat",foreign,selection,SceneSpriteFilter.Nearest,out _,out _));
        Assert.False(p.TrySpriteImage("flat",c,selection,(SceneSpriteFilter)17,out _,out _));
        Assert.False(p.TrySpriteImage("",c,selection,SceneSpriteFilter.Nearest,out _,out _));
        bool? result=null;var worker=new Thread(()=>result=p.TrySpriteImage("flat",c,selection,SceneSpriteFilter.Nearest,out _,out _)){IsBackground=true};worker.Start();Assert.True(worker.Join(5000));Assert.False(result);
        c.BeginRefresh();Assert.False(p.TrySpriteImage("flat",c,selection,SceneSpriteFilter.Nearest,out _,out _));p.Dispose();Assert.False(p.TrySpriteImage("flat",foreign,foreign.Select(Id)!,SceneSpriteFilter.Nearest,out _,out _));Assert.Equal(0,calls);
    }
    [Fact]
    public void Frame_collection_retains_sprites_and_3D_together_then_retires_absent_views_and_keeps_one_binding()
    {
        using var ui=new HeadlessHost{Drawing=false};using var c=new AssetCatalog();Publish(c);IReadOnlySet<string>? active=null;
        using var p=ScenePresentation.Bind((_,r,_)=>new(r,Texture(),()=>true),views=>active=views);
        Assert.Same(p,p.WithSprites((_,_,s,f)=>new(s,f,Texture(),()=>true)));Assert.Same(p,ScenePresentation.Current);
        Assert.Throws<InvalidOperationException>(()=>p.WithSprites((_,_,_,_)=>null));p.BeginFrame();
        var request=SceneRenderRequest.Capture(new(0,1,2,3,4,Vector2.Zero,new(400,300),Vector2.One),Camera.Default,[]);
        Assert.True(p.TryImage("solid",request,()=>request.Identity,out _,out _));Assert.True(p.TrySpriteImage("flat",c,c.Select(Id)!,SceneSpriteFilter.Linear,out _,out _));p.CompleteFrame();Assert.Equal(new[]{"flat","solid"},active!.Order());
        p.BeginFrame();p.CompleteFrame();Assert.Empty(active!);
    }
    [Fact]
    public void Missing_sprite_producer_keeps_3D_binding_and_names_the_bounds_fallback()
    {
        using var ui=new HeadlessHost{Drawing=false};using var c=new AssetCatalog();Publish(c);
        using var p=ScenePresentation.Bind((_,r,_)=>new(r,Texture(),()=>true));
        Assert.False(p.TrySpriteImage("flat",c,c.Select(Id)!,SceneSpriteFilter.Nearest,out var texture,out var why));Assert.Equal(default,texture);Assert.Contains("showing sprite bounds",why);
    }
    [Fact]
    public void Approved_seam_is_public_documented_and_connected_only_to_the_Vulkan_host()
    {
        Assert.True(typeof(SpriteImage).IsVisible);var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(typeof(ScenePresentation).Assembly.Location,".xml"));
        Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="T:"+typeof(SpriteImage).FullName);
        foreach(var method in new[]{"WithSprites","TrySpriteImage"})
        {Assert.NotNull(typeof(ScenePresentation).GetMethod(method));Assert.Contains(xml.Descendants("member"),m=>((string?)m.Attribute("name"))?.StartsWith("M:"+typeof(ScenePresentation).FullName+"."+method+"(",StringComparison.Ordinal)==true);}
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var host=typeof(Bed.Editor.App.EditorApp);Assert.Single(SceneFrameLifecycleTests.Calls(host.GetMethod("Loop",flags)!),c=>c.Method.DeclaringType==typeof(ScenePresentation)&&c.Method.Name=="WithSprites");
        Assert.DoesNotContain(SceneFrameLifecycleTests.Calls(host.GetMethod("RunOpenGl",flags)!),c=>c.Method.DeclaringType==typeof(ScenePresentation)&&c.Method.Name=="WithSprites");
    }
    private sealed record Image(nint Id,Vector2[] Positions,Vector2[] Uvs,uint[] Colours);
    private static List<Image> Images()
    {
        var result=new List<Image>();var data=Im.GetDrawData();
        for(var l=0;l<data.CmdListsCount;l++)
        {
            var list=data.CmdLists[l];
            foreach(var command in list.CmdBuffer)
            {
                var id=command.GetTexID().Value;if(id is not (91 or 92))continue;
                var indices=Enumerable.Range((int)command.IdxOffset,(int)command.ElemCount).Select(i=>(int)list.IdxBuffer[i]+(int)command.VtxOffset).Distinct().ToArray();
                result.Add(new(id,indices.Select(i=>new Vector2(list.VtxBuffer[i].pos.X,list.VtxBuffer[i].pos.Y)).ToArray(),indices.Select(i=>new Vector2(list.VtxBuffer[i].uv.X,list.VtxBuffer[i].uv.Y)).ToArray(),indices.Select(i=>list.VtxBuffer[i].col).ToArray()));
            }
        }
        return result;
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Actual_flat_view_records_ordered_raster_UVs_for_reflected_sprites_and_stale_images_fall_back_to_bounds(bool stale)
    {
        using var ui=new HeadlessHost(1000,800){Drawing=false};using var c=new AssetCatalog();Publish(c);
        Scene2Object Sprite(string id,int order,SceneSpriteFilter filter,Vector3 scale)=>new(SceneObject.At(id,Id,Vector3.Zero)with{Scale=scale},new(new(2,1),new(.25f,.75f),order,filter));
        var document=new Scene2(SceneSpace.TwoDXY,[Sprite("top",7,SceneSpriteFilter.Linear,new(-1,1,1)),Sprite("under",-1,SceneSpriteFilter.Nearest,Vector3.One)],[],[],null);
        var editor=new EditorModel<Scene2>(new(new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog(),document),Scene2Editing.Outline);
        var type=typeof(ScenePresentation).Assembly.GetType("Bed.Editor.ImGui.Scene2FlatView",true)!;
        var view=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[editor,(Func<string?>)(()=>null),(Func<AssetCatalog?>)(()=>c)],null)!;
        var draw=type.GetMethod("Draw",BindingFlags.Instance|BindingFlags.NonPublic)!;var seen=new List<SceneSpriteFilter>();
        using var p=ScenePresentation.Bind((_,_,_)=>null).WithSprites((_,_,s,f)=>{seen.Add(f);return new(s,f,Texture(f==SceneSpriteFilter.Nearest?91:92),()=>!stale);});
        void Draw(){seen.Clear();p.BeginFrame();Im.SetNextWindowPos(new(0,0));Im.SetNextWindowSize(new(950,750));Ui.Begin("Flat image protocol"u8);draw.Invoke(view,null);Ui.End();p.CompleteFrame();}
        ui.Frames(Draw,3);Assert.Equal(new[]{SceneSpriteFilter.Nearest,SceneSpriteFilter.Linear},seen);
        var images=Images();Assert.Empty(editor.Session.Journal);Assert.False(editor.Session.Dirty);
        if(stale){Assert.Empty(images);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("out of date"));return;}
        Assert.Equal(new nint[]{91,92},images.Select(i=>i.Id));
        foreach(var image in images){Assert.Equal(new Vector2[]{new(0,0),new(1,0),new(1,1),new(0,1)},image.Uvs);Assert.All(image.Colours,col=>Assert.Equal(0xffffffffu,col));Assert.Equal(128,MathF.Abs(image.Positions[1].X-image.Positions[0].X));Assert.Equal(64,MathF.Abs(image.Positions[3].Y-image.Positions[0].Y));}
        Assert.True(images[0].Positions[1].X>images[0].Positions[0].X);Assert.True(images[1].Positions[1].X<images[1].Positions[0].X);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.Contains("showing sprite bounds"));
    }
}
