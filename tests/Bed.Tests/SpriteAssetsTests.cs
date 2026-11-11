using System.Numerics;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Runtime;
using Xunit;
namespace Bed.Tests;

public sealed class SpriteAssetsTests
{
    private const string Id="11111111111111111111111111111111";
    private static JsonElement J(string text)=>JsonSerializer.Deserialize<JsonElement>(text);
    private static Scene2 Empty=>new(SceneSpace.TwoDXY,[],[],[],null);
    private static AssetCatalog.Input Image(int width=64,int height=96,double density=100,string policy="policy",string source="sign.png")
    {
        byte[] encoded=[1,2,3];var pixels=new byte[width*height*4];pixels[0]=47;pixels[3]=127;
        var settings=new SpriteAssetDefaults(density,new(.5f,0),SceneSpriteFilter.Linear).WithSettings(J("{\"imageNote\":\"keep\"}"));
        var record=new AssetRecord(Id,source,Convert.ToHexStringLower(SHA256.HashData(encoded)),"png",1,settings,
            new("Fixture","self-authored","synthetic image",Declared:AssetOrigin.Ours),AiUse.Forbidden);
        return new(record,encoded,pixels,[],true,"BEDTEX1",policy,texture:new(width,height,AssetCatalog.TextureEncoding.Rgba8Srgb));
    }
    private static void Publish(AssetCatalog c,params AssetCatalog.Input[] inputs)=>Assert.True(c.Publish(c.BeginRefresh(),inputs,out var why),why);
    private static Scene2 Placed(string id="sign",string asset=Id,int order=0)=>new(SceneSpace.TwoDXY,[new(SceneObject.At(id,asset,Vector3.Zero),new(new(.64f,.96f),new(.5f,0),order,SceneSpriteFilter.Linear))],[],[],null);
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("sprite-host-").FullName;
        internal readonly DocumentHost Host;internal readonly Scene2DocumentEntry Entry;internal readonly AssetCatalog Catalog=new();internal bool Blocked;
        internal string Disk=>Path.Combine(Root,"data/level.json");
        internal Rig(Scene2? scene=null)
        {
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(Disk,new Scene2Format().Write(scene??Empty));
            var project=DataProject.Open(Path.Combine(Root,"data"),Path.Combine(Root,"schemas"),readOnlyFile:_=>Blocked?"fixture is read-only":null);
            Host=DocumentHost.For(project);Assert.True(Host.BindScene("level.json","scene",out _));Assert.True(Host.OpenScene2("level.json",out var entry,out var why),why);Entry=entry!;
            Assert.True(Host.BindAssetCatalog(Catalog,out _));Publish(Catalog,Image());
        }
        internal SceneSpritePlacement Capture(){Assert.True(Host.CaptureSpritePlacement("level.json",Catalog.Select(Id)!,out var p,out var why),why);return p!;}
        public void Dispose(){Catalog.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Defaults_preserve_unrelated_settings_and_all_asset_identity_and_rights_metadata()
    {
        var original=Image().ProducingRecord;
        var defaults=new SpriteAssetDefaults(200,new(.25f,.75f),SceneSpriteFilter.Nearest);
        var updated=original with{Settings=defaults.WithSettings(original.Settings)};
        Assert.Equal("keep",updated.Settings.GetProperty("imageNote").GetString());Assert.Equal(original.Guid,updated.Guid);Assert.Equal(original.Provenance,updated.Provenance);
        Assert.Equal(original.AiUse,updated.AiUse);Assert.Equal(original.SourceHash,updated.SourceHash);Assert.Equal(original.ImporterVersion,updated.ImporterVersion);
        Assert.Equal(defaults,SpriteAssetDefaults.Read(updated.Settings));Assert.Equal(new Vector2(.32f,.48f),defaults.CreateSprite(64,96).Size);
        Assert.Equal(new SpriteAssetDefaults(100,new(.5f,.5f),SceneSpriteFilter.Nearest),SpriteAssetDefaults.Read(J("{}")));
        Assert.Equal(1,AssetRecord.Version);
    }
    [Theory]
    [InlineData("null")][InlineData("{}")]
    [InlineData("{\"version\":2,\"pixelsPerMetre\":100,\"pivot\":[0,0],\"filter\":\"nearest\"}")]
    [InlineData("{\"version\":1,\"pixelsPerMetre\":0,\"pivot\":[0,0],\"filter\":\"nearest\"}")]
    [InlineData("{\"version\":1,\"pixelsPerMetre\":100,\"pivot\":[1.1,0],\"filter\":\"nearest\"}")]
    [InlineData("{\"version\":1,\"pixelsPerMetre\":100,\"pivot\":[0,0],\"filter\":\"cubic\"}")]
    [InlineData("{\"version\":1,\"version\":1,\"pixelsPerMetre\":100,\"pivot\":[0,0],\"filter\":\"nearest\"}")]
    public void Malformed_defaults_refuse_without_silently_falling_back(string defaults)
    {Assert.False(SpriteAssetDefaults.TryRead(J("{\"spriteDefaults\":"+defaults+"}"),out var value,out var why));Assert.Null(value);Assert.NotEmpty(why);}
    [Fact]
    public void Defaults_refuse_duplicate_roots_invalid_density_size_underflow_and_complete_artifact_overflow()
    {
        Assert.False(SpriteAssetDefaults.TryRead(J("{\"x\":1,\"x\":2}"),out _,out _));
        foreach(var d in new[]{double.NaN,double.PositiveInfinity,0,-1,double.Epsilon,double.MaxValue})
            Assert.Throws<ArgumentException>(()=>new SpriteAssetDefaults(d,Vector2.Zero,SceneSpriteFilter.Nearest).CreateSprite(64,96));
        Assert.Throws<ArgumentException>(()=>SpriteAssetDefaults.Read(J("{}")).CreateSprite(4096,4096));
        Assert.Throws<ArgumentException>(()=>new SpriteAssetDefaults(100,Vector2.Zero,(SceneSpriteFilter)88).WithSettings(J("{}")));
    }
    [Fact]
    public void Raster_origin_pivot_parent_rotation_and_mirroring_produce_expected_world_corners()
    {
        var size=new Vector2(.64f,.96f);var pivot=new Vector2(.5f,0);
        Assert.Equal(new Vector3(-.32f,.96f,0),SpriteGeometry.LocalPoint(size,pivot,Vector2.Zero));
        Assert.Equal(new Vector3(.32f,0,0),SpriteGeometry.LocalPoint(size,pivot,Vector2.One));
        var world=Matrix4x4.CreateScale(-2,3,1)*Matrix4x4.CreateRotationZ(MathF.PI/2)*Matrix4x4.CreateTranslation(12,3,0);
        var corners=SpriteGeometry.Corners(size,pivot,world);
        Vector3[] expected=[new(9.12f,3.64f,0),new(9.12f,2.36f,0),new(12,2.36f,0),new(12,3.64f,0)];
        for(var i=0;i<4;i++)Assert.True(Vector3.Distance(expected[i],corners[i])<.00001f,$"corner {i}: {corners[i]}");
        var odd=SpriteAssetDefaults.Read(J("{}")).CreateSprite(63,95);Assert.Equal(new Vector2(.63f,.95f),odd.Size);
        Assert.Throws<ArgumentException>(()=>SpriteGeometry.Corners(Vector2.Zero,pivot,world));
        Assert.Throws<ArgumentException>(()=>SpriteGeometry.LocalPoint(size,new(-1,0),Vector2.Zero));
        Assert.Throws<ArgumentException>(()=>SpriteGeometry.Corners(size,pivot,Matrix4x4.CreateTranslation(float.NaN,0,0)));
        Assert.Equal(new[]{3,1,2,0},SpriteGeometry.DrawOrder(new[]{int.MaxValue,2,2,int.MinValue}));
        Assert.Equal(new[]{0,2,1,3},SpriteGeometry.DrawOrder(new[]{int.MaxValue,2,2,int.MinValue}).Reverse());
        Assert.Throws<ArgumentException>(()=>SpriteGeometry.DrawOrder(new int[50001]));
    }
    [Fact]
    public void Placement_is_one_checked_undo_step_and_defaults_and_reimport_never_resize_existing_sprites()
    {
        using var r=new Rig();var disk=File.ReadAllText(r.Disk);var p=r.Capture();Assert.Empty(r.Entry.Session.Journal);
        Assert.True(r.Host.PlaceSprite(p,"sign",new(2,3),out var why),why);var original=r.Entry.Session.Current.Objects.Single();
        Assert.Equal(new Vector2(.64f,.96f),original.Sprite!.Size);Assert.Equal(new(.5f,0),original.Sprite.Pivot);Assert.Equal(new Vector3(2,3,0),original.Object.Position);Assert.Equal(Id,original.Object.Asset);
        Assert.Equal(disk,File.ReadAllText(r.Disk));Assert.Single(r.Entry.Session.Journal);Assert.Equal("sign",r.Entry.Editor.Primary);
        var placedText=r.Entry.Session.TextToSave;
        Publish(r.Catalog,Image(128,192,400));Assert.Equal(placedText,r.Entry.Session.TextToSave);Assert.Same(original.Sprite,r.Entry.Session.Current.Objects.Single().Sprite);
        Assert.True(r.Entry.Session.Undo());Assert.Empty(r.Entry.Session.Current.Objects);Assert.True(r.Entry.Session.Redo());Assert.Equal(placedText,r.Entry.Session.TextToSave);
        var next=r.Capture();Assert.True(r.Host.PlaceSprite(next,"next",Vector2.Zero,out why),why);Assert.Equal(new Vector2(.32f,.48f),r.Entry.Session.Current.Objects.Last().Sprite!.Size);
        Assert.Equal(original.Sprite,r.Entry.Session.Current.Objects.First().Sprite);
        var replay=DocumentSession<Scene2>.ReplayFrom(r.Entry.Session.JournalBase,r.Entry.Session.Journal,new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(r.Entry.Session.TextToSave,replay.TextToSave);
        Assert.True(r.Host.SaveScene2("level.json",out why),why);Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out why),why);
        Assert.True(r.Host.OpenScene2("level.json",out var reopened,out why),why);Assert.Equal(original.Sprite,reopened!.Session.Current.Objects.First().Sprite);

    }
    [Theory]
    [InlineData("content")][InlineData("policy")][InlineData("defaults")][InlineData("pending")][InlineData("missing")][InlineData("closed")][InlineData("revision")][InlineData("disk")][InlineData("reopen")][InlineData("readonly")][InlineData("editor-policy")]
    public void Changed_capture_or_scene_guard_refuses_atomically(string change)
    {
        using var r=new Rig();var p=r.Capture();var original=r.Entry.Session.Current;
        switch(change)
        {
            case "content":Publish(r.Catalog,Image(128,96));break;
            case "policy":Publish(r.Catalog,Image(policy:"changed"));break;
            case "defaults":Publish(r.Catalog,Image(density:200));break;
            case "pending":r.Catalog.BeginRefresh();break;
            case "missing":Publish(r.Catalog);break;
            case "closed":r.Catalog.Dispose();break;
            case "revision":Assert.True(r.Entry.Session.Execute(Scene2Kit.Command("object.add",J("{\"object\":{\"id\":\"marker\"}}"))));break;
            case "disk":File.AppendAllText(r.Disk," ");break;
            case "reopen":Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(r.Host.OpenScene2("level.json",out _,out _));break;
            case "readonly":r.Blocked=true;break;
            case "editor-policy":r.Entry.Editor.EditRefusal=(_,_,_)=>"adapter refused";break;
        }
        var count=r.Entry.Session.Journal.Count;Assert.False(r.Host.PlaceSprite(p,"sign",Vector2.Zero,out var why));Assert.NotEmpty(why);Assert.Equal(count,r.Entry.Session.Journal.Count);
        if(change!="revision")Assert.Same(original,r.Entry.Session.Current);
    }
    [Fact]
    public void Foreign_model_nonplanar_and_invalid_position_refuse_without_scene_changes()
    {
        using var r=new Rig();using var other=new Rig();var p=r.Capture();Assert.False(other.Host.PlaceSprite(p,"sign",Vector2.Zero,out _));
        Assert.False(r.Host.PlaceSprite(p,"bad",new(float.NaN,0),out _));Assert.Empty(r.Entry.Session.Journal);
        Publish(r.Catalog,new AssetCatalog.Input(Image().ProducingRecord,[1,2,3],[1],[],true,"model","policy"));
        Assert.False(r.Host.CaptureSpritePlacement("level.json",r.Catalog.Select(Id)!,out _,out var why));Assert.Contains("models",why);
        using var threeD=new Rig(new(SceneSpace.ThreeD,[],[],[],null));Assert.False(threeD.Host.CaptureSpritePlacement("level.json",threeD.Catalog.Select(Id)!,out _,out why));Assert.Contains("unsupported",why);
    }
    [Fact]
    public void Closure_deduplicates_exact_GUID_retains_source_policy_alpha_and_refuses_after_refresh()
    {
        using var c=new AssetCatalog();var input=Image();Publish(c,input);
        var scene=Placed() with{Objects=[..Placed().Objects,..Placed("second").Objects]};
        Assert.True(SpriteAssetClosure.Capture(scene,c,out var closure,out var why),why);Assert.Single(closure!.Textures);Assert.Single(closure.Inputs);
        Assert.Equal(input.ProducingRecord.Write(),closure.Inputs[0].ProducingRecord.Write());var lease=closure.Textures[Id];Assert.Equal(input.ProducingRecord.SourceHash,lease.SourceHash);
        var rgba=new byte[lease.Length];lease.CopyTo(rgba);Assert.Equal(47,rgba[0]);Assert.Equal(127,rgba[3]);
        Assert.Equal(rgba,BedTextureReader.Decode(TextureAssetGeneration.EncodeArtifact(lease)).Rgba);
        Publish(c,Image(policy:"new policy"));Assert.False(closure.Revalidate(c,out _));var retained=new byte[lease.Length];lease.CopyTo(retained);Assert.Equal(rgba,retained);
        using var foreign=new AssetCatalog();Assert.False(closure.Revalidate(foreign,out _));
    }
    [Theory][InlineData("missing")][InlineData("ambiguous")][InlineData("opaque")][InlineData("jpg")][InlineData("pending")]
    public void Closure_refuses_whole_capture_for_unsupported_or_unready_images(string mode)
    {
        using var c=new AssetCatalog();
        switch(mode)
        {
            case "missing":Publish(c);break;
            case "ambiguous":Publish(c,Image(),Image());break;
            case "opaque":Publish(c,new AssetCatalog.Input(Image().ProducingRecord,[1,2,3],[1],[],true,"model","policy"));break;
            case "jpg":Publish(c,Image(source:"sign.jpg"));break;
            case "pending":Publish(c,Image());c.BeginRefresh();break;
        }
        Assert.False(SpriteAssetClosure.Capture(Placed(),c,out var closure,out var why));Assert.Null(closure);Assert.NotEmpty(why);
    }
    [Fact]
    public void Closure_refuses_texture_count_before_any_byte_capture_and_empty_closed_admission()
    {
        var objects=Enumerable.Range(0,1025).Select(i=>new Scene2Object(SceneObject.At("sprite_"+i,i.ToString("x32"),Vector3.Zero),new(Vector2.One,Vector2.Zero,0,SceneSpriteFilter.Nearest))).ToImmutableArray();
        using var c=new AssetCatalog();Publish(c);Assert.False(SpriteAssetClosure.Capture(new(SceneSpace.TwoDXY,objects,[],[],null),c,out _,out var why));Assert.Contains("1,024",why);
        Assert.True(SpriteAssetClosure.Capture(Empty,c,out var empty,out why),why);c.Dispose();Assert.False(empty!.Revalidate(c,out _));
    }
    [Theory][InlineData("owner")][InlineData("focus")][InlineData("policy")]
    public void Reentrant_editor_policy_cannot_change_owner_focus_or_policy_and_then_commit_old_capture(string mode)
    {
        var scene=Empty with{Objects=[new(SceneObject.At("group",null,Vector3.Zero),null)]};using var r=new Rig(scene);var p=r.Capture();
        r.Entry.Editor.EditRefusal=(_,_,_)=>
        {
            if(mode=="owner"){Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(r.Host.OpenScene2("level.json",out _,out _));}
            else if(mode=="focus")Assert.True(r.Entry.Editor.FocusOn("group"));
            else r.Entry.Editor.EditRefusal=(_,_,_)=>"new refusal";
            return null;
        };
        Assert.False(r.Host.PlaceSprite(p,"sign",Vector2.Zero,out var why));Assert.NotEmpty(why);Assert.Empty(r.Entry.Session.Journal);
        Assert.True(r.Host.TryGetOpenScene2("level.json",out var current));Assert.Empty(current!.Session.Journal);
    }
    [Fact]
    public void Explicit_parent_placement_obeys_focus_and_copies_local_XY_without_altering_parent()
    {
        var scene=Empty with{Objects=[new(SceneObject.At("group",null,new(12,3,0)),null)]};using var r=new Rig(scene);
        Assert.True(r.Entry.Editor.FocusOn("group"));Assert.False(r.Host.CaptureSpritePlacement("level.json",r.Catalog.Select(Id)!,out _,out _));
        Assert.True(r.Host.CaptureSpritePlacement("level.json",r.Catalog.Select(Id)!,out var p,out var why,"group"),why);
        Assert.True(r.Host.PlaceSprite(p!,"child",new(2,4),out why),why);
        var objects=r.Entry.Session.Current.Objects;Assert.Equal("group",objects[1].Object.Parent);Assert.Equal(new Vector3(2,4,0),objects[1].Object.Position);Assert.Equal(new Vector3(12,3,0),objects[0].Object.Position);
    }
    [Fact]
    public void Prefab_snapshot_closure_and_stable_order_survive_Break_Link_without_reading_original_source()
    {
        using var c=new AssetCatalog();Publish(c,Image());
        var args=J("""
        {"key":"22222222222222222222222222222222","baseId":"accepted","definition":
          {"kind":"prefab-definition","version":1,"space":"2d-xy","guid":"33333333333333333333333333333333","root":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
           "nodes":[{"key":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","label":"Artwork","asset":"11111111111111111111111111111111","sprite":{"size":[2,3],"pivot":[0,1],"order":0,"filter":"nearest"}}],"slots":[]},
         "placement":{"id":"piece","position":[3,4,0]},"nodeIds":{"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa":"piece_art"},"bindings":{}}
        """);
        var added=Scene2Kit.Command("instantiate",args).Apply(Placed());Assert.True(added.Ok,string.Join(" ",added.Issues.Select(i=>i.Message)));var scene=added.Document!;
        Assert.True(SpriteAssetClosure.Capture(scene,c,out var closure,out var why),why);Assert.Single(closure!.Textures);
        var before=Scene2Resolver.Resolve(scene).Objects.Where(o=>o.Sprite is not null).ToArray();
        var detached=Scene2Kit.Command("break_link",J("{\"instance\":\"22222222222222222222222222222222\"}")).Apply(scene);Assert.True(detached.Ok);
        var after=Scene2Resolver.Resolve(detached.Document!).Objects.Where(o=>o.Sprite is not null).ToArray();
        string[] Order(Scene2Object[] items)=>SpriteGeometry.DrawOrder(items.Select(o=>o.Sprite!.Order).ToArray()).Select(i=>items[i].Object.Id).ToArray();
        Assert.Equal(new[]{"sign","piece_art"},Order(before));Assert.Equal(Order(before),Order(after));Assert.Equal(before.Select(o=>o.Sprite),after.Select(o=>o.Sprite));
        Assert.Equal(new Vector2(2,3),after[1].Sprite!.Size);Assert.True(SpriteAssetClosure.Capture(detached.Document!,c,out var second,out why),why);Assert.Equal(closure.Textures.Keys,second!.Textures.Keys);
    }

}
