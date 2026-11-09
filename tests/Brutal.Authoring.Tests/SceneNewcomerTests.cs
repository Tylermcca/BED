using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneNewcomerTests
{
    [Fact]
    public void Admitted_host_keyboard_creation_parent_history_save_reopen_and_core_load_keep_the_same_scene()
    {
        const string guid="11111111111111111111111111111111";
        var root=Directory.CreateTempSubdirectory("scene-newcomer-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;
            var path=Path.Combine(data,"room.json");
            var initial=new SceneFormat().Write(Scene.Empty);File.WriteAllText(path,initial);
            byte[] bytes=[1,2,3];
            var record=new AssetRecord(guid,"item.bin",Convert.ToHexStringLower(SHA256.HashData(bytes)),"fixture",1,
                JsonSerializer.SerializeToElement(new {}),new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllBytes(Path.Combine(assets,"item.bin"),bytes);
            File.WriteAllText(Path.Combine(assets,"item.bin")+AssetRecord.Suffix,record.Write());
            using var catalog=new AssetCatalog();
            Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,bytes,[],true,"fixture","initial")],out _));
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));
            Assert.True(workspace.Documents.BindScene("room.json","scene",out _));
            // Explicit admitted test-host binding, not evidence of a launcher filesystem admission producer.
            Assert.True(workspace.BindSceneAssets(new AssetBrowser(assets,".bin"),catalog));
            Assert.True(workspace.Show("room.json"));
            using var host=new HeadlessHost(1600,1000){Drawing=false};
            EditorWindow<Scene>.Configure(ImGui.GetIO());
            host.Frames(workspace.Draw,4);
            var window=workspace.SceneWindow!;
            workspace.SceneViewport!.Camera=workspace.SceneViewport.Camera with {Target=new Vector3(2,3,4)};
            void Press(ImGuiKey key,bool ctrl=false,bool shift=false)
            {host.Press(workspace.Draw,key,ctrl:ctrl,shift:shift);host.Frames(workspace.Draw,2);}
            void TabTo(string label,UiRole role)
            {
                for(var i=0;i<24;i++)
                {
                    if(Ui.Snapshot().Nodes.Any(n=>n.Label==label&&n.Role==role&&n.Focused))return;
                    Press(ImGuiKey.Tab);
                }
                Assert.Fail("Keyboard focus did not reach "+label+"; focused: "+string.Join(",",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Label)));
            }
            void CreateThroughPicker()
            {
                Press(ImGuiKey.Insert);
                Assert.True(window.Editor.IsOpen("scene.assets"));
                TabTo("Problems only",UiRole.Checkbox);
                Press(ImGuiKey.Tab); // A child list is a native navigation stop; Enter moves into it.
                Press(ImGuiKey.Enter);
                Assert.True(Ui.Snapshot().Nodes.Any(n=>n.Label=="item.bin"&&n.Role==UiRole.ListItem&&n.Focused),
                    "List focus: "+string.Join(",",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Label)));
                Press(ImGuiKey.Enter);
                Press(ImGuiKey.Escape); // Leave the child list before continuing through the parent panel.
                TabTo("Add selected object",UiRole.Button);Press(ImGuiKey.Enter);
                Assert.False(window.Editor.IsOpen("scene.assets"));
            }
            CreateThroughPicker();
            var parent=Assert.Single(window.Editor.Session.Current.Objects);
            Assert.Null(parent.Parent);Assert.Equal(new Vector3(2,3,4),parent.Position);
            Assert.Equal(parent.Id,window.Editor.Primary);
            CreateThroughPicker();
            var child=Assert.Single(window.Editor.Session.Current.Objects,o=>o.Parent==parent.Id);
            Assert.Equal(Vector3.Zero,child.Position);Assert.Equal(guid,child.Asset);
            Assert.Equal(child.Id,window.Editor.Primary);
            Press(ImGuiKey.P,ctrl:true,shift:true);Press(ImGuiKey.A,ctrl:true);
            host.Type(workspace.Draw,"scene.duplicate");Press(ImGuiKey.Enter);
            Assert.Equal(3,window.Editor.Session.Current.Objects.Length);
            var copy=Assert.Single(window.Editor.Session.Current.Objects,o=>o.Id!=parent.Id&&o.Id!=child.Id);
            Assert.Equal(child.Parent,copy.Parent);Assert.Equal(child.Asset,copy.Asset);
            Assert.Equal(child.Position,copy.Position);Assert.Equal(child.Rotation,copy.Rotation);Assert.Equal(child.Scale,copy.Scale);
            Press(ImGuiKey.Z,ctrl:true);Assert.Equal(new[]{parent,child},window.Editor.Session.Current.Objects);
            Assert.Equal(initial,File.ReadAllText(path));
            var calls=0;workspace.Documents.SceneSaveCheck=e=>{Assert.Same(window.Editor,e.Editor);calls++;return null;};
            Press(ImGuiKey.S,ctrl:true);Assert.Equal(1,calls);Assert.False(window.Editor.Session.Dirty);
            var saved=File.ReadAllText(path);Assert.NotEqual(initial,saved);
            // Existing typed lifecycle seam: no document-tab close UI is invented for this journey.
            Assert.True(workspace.Documents.CloseScene("room.json",SceneCloseDecision.Save,out var why),why);
            Assert.True(workspace.Show("room.json"));Assert.NotSame(window,workspace.SceneWindow);
            var reopened=workspace.SceneWindow!.Editor.Session;
            Assert.Equal(saved,new SceneFormat().Write(reopened.Current));Assert.False(reopened.History.CanUndo);
            var runtime=SceneTool.RuntimeScene.Load(saved,assetExists:id=>id==guid);
            Assert.True(runtime.AssetsChecked);Assert.Empty(runtime.AssetIssues);Assert.Equal(2,runtime.Objects.Count);
            foreach(var source in reopened.Current.Objects)
            {
                var actual=Assert.Single(runtime.Objects,o=>o.Id==source.Id);
                Assert.Equal(source.Asset,actual.Asset);Assert.Equal(source.Position,actual.LocalPosition);
                Assert.Equal(source.Rotation,actual.LocalRotation);Assert.Equal(source.Scale,actual.LocalScale);
                Assert.Equal(source.Parent,actual.Parent?.Id);
            }
            Assert.Same(runtime.Objects.Single(o=>o.Id==parent.Id),runtime.Objects.Single(o=>o.Id==child.Id).Parent);
            Assert.Equal(saved,File.ReadAllText(path));
        }
        finally {Directory.Delete(root,true);}
    }
}
