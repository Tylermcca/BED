using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class Scene2SpriteDragTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [Theory][InlineData("deliver")][InlineData("outside")][InlineData("escape")][InlineData("asset-stale")][InlineData("scene-stale")][InlineData("read-only")][InlineData("closed")]
    public void Actual_native_asset_token_delivers_one_sprite_at_the_cursor_and_stale_cancelled_or_closed_gestures_never_edit(string mode)
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json",open:false);File.WriteAllText(r.Disk,new Scene2Format().Write(new(SceneSpace.TwoDXY,[],[],[],null)));var original=File.ReadAllText(r.Disk);
        const string id="55555555555555555555555555555555";var assets=Directory.CreateDirectory(Path.Combine(r.Root,"assets")).FullName;byte[] source=[1,2,3];
        var record=new AssetRecord(id,"sign.png",Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,new SpriteAssetDefaults(10,new(.25f,.75f),SceneSpriteFilter.Linear).WithSettings(JsonSerializer.SerializeToElement(new{})),new("Fixture","self-authored","synthetic decoded-image generation",Declared:AssetOrigin.Ours),AiUse.Forbidden);
        File.WriteAllBytes(Path.Combine(assets,"sign.png"),source);File.WriteAllText(Path.Combine(assets,"sign.png")+AssetRecord.Suffix,record.Write());using var catalog=new AssetCatalog();
        Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,source,new byte[2*3*4],[],true,"fixture","initial",texture:new(2,3,AssetCatalog.TextureEncoding.Rgba8Srgb))],out _));
        using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.BindSceneAssets(new AssetBrowser(assets,".png"),catalog));Assert.True(workspace.Show("level.json"));var entry=r.Entry;
        var picker=(AssetsPanel)typeof(DataWorkspace).GetField("sceneAssetPicker",Private)!.GetValue(workspace)!;var placement=(AssetPlacement)typeof(DataWorkspace).GetField("scenePlacement",Private)!.GetValue(workspace)!;
        var windows=(IDictionary)typeof(DataWorkspace).GetField("scene2Windows",Private)!.GetValue(workspace)!;var native=windows["level.json"]!;var flat=native.GetType().GetField("flatView",Private)!.GetValue(native)!;var drawFlat=flat.GetType().GetMethod("Draw",Private)!;
        using var ui=new HeadlessHost(1200,800){Drawing=false};
        void Draw()
        {
            ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(500,650));Ui.Begin("Sprite source"u8);picker.Draw();Ui.End();
            ImGui.SetNextWindowPos(new(510,0));ImGui.SetNextWindowSize(new(500,650));Ui.Begin("Sprite target"u8);drawFlat.Invoke(flat,null);Ui.End();placement.FinishFrame();
        }
        ui.Frames(Draw,3);var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="sign.png").Bounds;var canvas=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas&&n.Label=="Flat XY").Bounds;
        var destination=mode=="outside"?new float2(1150,750):new float2(canvas.X+canvas.Width*.4f,canvas.Y+canvas.Height*.35f);var frame=0;var captured=false;
        void Drag()
        {
            if(++frame==6)
            {
                captured=placement.Pending;
                if(mode=="asset-stale")catalog.BeginRefresh();
                if(mode=="scene-stale")Assert.True(entry.Session.Execute(Scene2Kit.Command("object.add",JsonSerializer.SerializeToElement(new{@object=new{id="other"}}))));
                if(mode=="escape")ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape,true);
                if(mode=="read-only")r.Blocked=true;
                if(mode=="closed")Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));
            }
            Draw();
        }
        ui.Drag(Drag,new(item.X+item.Width/2,item.Y+item.Height/2),destination,steps:12);Assert.True(captured);Assert.False(placement.Pending);Assert.False(placement.Commit());Assert.Equal(original,File.ReadAllText(r.Disk));
        if(mode=="deliver")
        {
            var added=Assert.Single(entry.Session.Current.Objects);Assert.Equal(id,added.Object.Asset);Assert.Equal(new Vector2(.2f,.3f),added.Sprite!.Size);Assert.Equal(new Vector2(.25f,.75f),added.Sprite.Pivot);Assert.Equal(SceneSpriteFilter.Linear,added.Sprite.Filter);
            // ImGui rounds native mouse coordinates to raster pixels; compare with the delivered cursor, not the requested fractional test coordinate.
            var mouse=ImGui.GetIO().MousePos;var expected=new Vector2((mouse.X-canvas.X-canvas.Width/2)/64,(canvas.Y+canvas.Height/2-mouse.Y)/64);
            Assert.InRange(added.Object.Position.X,expected.X-.001f,expected.X+.001f);Assert.InRange(added.Object.Position.Y,expected.Y-.001f,expected.Y+.001f);Assert.Equal(0,added.Object.Position.Z);Assert.Single(entry.Session.Journal);Assert.True(entry.Session.Undo());Assert.Empty(entry.Session.Current.Objects);
        }
        else if(mode=="scene-stale"){Assert.Equal("other",Assert.Single(entry.Session.Current.Objects).Object.Id);Assert.Single(entry.Session.Journal);}
        else {Assert.Empty(entry.Session.Current.Objects);Assert.Empty(entry.Session.Journal);}
    }
}
