using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AssetPlacementTests
{
    [Theory]
    [InlineData("deliver")][InlineData("outside")][InlineData("escape")][InlineData("asset-stale")][InlineData("scene-stale")]
    public void Native_payload_delivery_is_one_guarded_add_and_cancel_or_stale_does_not_edit(string mode)
    {
        const string guid="11111111111111111111111111111111";
        var root=Directory.CreateTempSubdirectory("asset-drag-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;
            var path=Path.Combine(data,"room.json");var original=new SceneFormat().Write(Scene.Empty);File.WriteAllText(path,original);
            byte[] bytes=[1,2,3];
            var record=new AssetRecord(guid,"item.bin",Convert.ToHexStringLower(SHA256.HashData(bytes)),"fixture",1,
                JsonSerializer.SerializeToElement(new {}),new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllBytes(Path.Combine(assets,"item.bin"),bytes);File.WriteAllText(Path.Combine(assets,"item.bin")+AssetRecord.Suffix,record.Write());
            using var catalog=new AssetCatalog();
            Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,bytes,[],true,"fixture","initial")],out _));
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));
            Assert.True(workspace.Documents.BindScene("room.json","scene",out _));
            Assert.True(workspace.Documents.BindAssetCatalog(catalog,out _));Assert.True(workspace.Show("room.json"));
            workspace.SceneViewport!.Camera=workspace.SceneViewport.Camera with {Target=new Vector3(2,3,4)};
            var placement=new AssetPlacement(workspace,catalog);
            workspace.SceneViewport.DropTarget=placement.DrawTarget;
            var panel=new AssetsPanel(new AssetBrowser(assets,".bin"),placement.DrawSource);
            using var host=new HeadlessHost(1200,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
            void Draw()
            {
                ImGui.SetNextWindowPos(new float2(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(500,650),ImGuiCond.Always);
                Ui.Begin("Asset source fixture"u8);panel.Draw();Ui.End();
                ImGui.SetNextWindowPos(new float2(510,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(500,650),ImGuiCond.Always);
                Ui.Begin("Target fixture"u8);
                workspace.SceneViewport.Draw();Ui.End();
                placement.FinishFrame();
            }
            host.Frames(Draw,3);
            var bounds=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="item.bin"&&n.Role==UiRole.ListItem).Bounds;
            var viewport=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Viewport"&&n.Role==UiRole.Canvas);
            Assert.True(viewport.Enabled);Assert.False(viewport.Bounds.IsEmpty);
            var target=viewport.Bounds;
            // Keep an off-centre drop: placement must still use the captured view centre, not this pointer position.
            var destination=mode=="outside" ? new float2(target.X+target.Width+50,target.Y+target.Height+50)
                : new float2(target.X+target.Width*.4f,target.Y+target.Height*.35f);
            if(mode=="outside")Assert.True(destination.X>target.X+target.Width&&destination.Y>target.Y+target.Height);
            else Assert.True(destination.X>target.X&&destination.X<target.X+target.Width/2
                &&destination.Y>target.Y&&destination.Y<target.Y+target.Height/2);
            var frame=0;var captured=false;
            void DragFrame()
            {
                frame++;
                if(frame==6)
                {
                    captured=placement.Pending;
                    if(mode=="asset-stale") catalog.BeginRefresh();
                    if(mode=="scene-stale") Assert.True(workspace.SceneWindow!.Editor.Session.Execute(new AddObject(SceneObject.At("other",null,Vector3.Zero))));
                    if(mode=="escape") ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape,true);
                }
                Draw();
            }
            host.Drag(DragFrame,new float2(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2),
                destination,steps:12);
            Assert.True(captured,"The real source must capture before the counterexample changes its inputs.");
            var session=workspace.SceneWindow!.Editor.Session;
            Assert.False(placement.Pending);Assert.Equal(original,File.ReadAllText(path));
            if(mode=="deliver")
            {
                var added=Assert.Single(session.Current.Objects);Assert.Equal(guid,added.Asset);Assert.Equal(new Vector3(2,3,4),added.Position);
                Assert.True(session.Undo());Assert.Empty(session.Current.Objects);Assert.False(session.History.CanUndo);
                Assert.False(placement.Commit());
            }
            else if(mode=="scene-stale")
            {
                Assert.Equal("other",Assert.Single(session.Current.Objects).Id);
                Assert.True(session.Undo());Assert.False(session.History.CanUndo);
            }
            else {Assert.Empty(session.Current.Objects);Assert.False(session.History.CanUndo);}
        }
        finally {Directory.Delete(root,true);}
    }
}
