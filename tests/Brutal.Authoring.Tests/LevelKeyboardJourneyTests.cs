using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class LevelKeyboardJourneyTests
{
    [Fact]
    public void SceneStarter_opens_adds_moves_parents_undoes_saves_and_reopens_using_keys()
    {
        var root=Directory.CreateTempSubdirectory("level-keys-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;
            var path=Path.Combine(data,"level.json");
            var original=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SceneStarterReview","scene.json"));
            File.WriteAllText(path,original);
            const string guid="11111111111111111111111111111111";byte[] bytes=[1,2,3];
            var record=new AssetRecord(guid,"piece.bin",Convert.ToHexStringLower(SHA256.HashData(bytes)),"fixture",1,
                JsonSerializer.SerializeToElement(new{}),new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllBytes(Path.Combine(assets,"piece.bin"),bytes);File.WriteAllText(Path.Combine(assets,"piece.bin")+AssetRecord.Suffix,record.Write());
            using var catalog=new AssetCatalog();
            Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,bytes,[],true,"fixture","initial")],out _));
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));
            // Explicitly approved admitted test-host setup. No launcher/catalog acquisition claim.
            Assert.True(workspace.Documents.BindScene("level.json","scene",out _));
            Assert.True(workspace.BindSceneAssets(new AssetBrowser(assets,".bin"),catalog));
            using var host=new HeadlessHost(1600,1000){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
            host.Frames(workspace.Draw,4);
            void Key(ImGuiKey key,bool ctrl=false){host.Press(workspace.Draw,key,ctrl:ctrl);host.Frames(workspace.Draw,2);}
            void Reach(string label,UiRole role,bool contains=false)
            {
                for(var i=0;i<50;i++)
                {
                    if(Ui.Snapshot().Nodes.Any(n=>n.Role==role&&n.Focused&&(contains?n.Label.Contains(label,StringComparison.Ordinal):n.Label==label)))return;
                    Key(ImGuiKey.Tab);
                }
                Assert.Fail("Keys did not reach "+label+"; focus="+string.Join(",",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Label)));
            }
            Assert.Null(workspace.SceneWindow);Reach("level.json",UiRole.ListItem,true);Key(ImGuiKey.Enter);
            var window=workspace.SceneWindow!;Assert.NotNull(window);Assert.Equal(2,window.Editor.Session.Current.Objects.Length);
            Key(ImGuiKey.Insert);Assert.True(window.Editor.IsOpen("scene.assets"));
            Reach("Problems only",UiRole.Checkbox);Key(ImGuiKey.Tab);Key(ImGuiKey.Enter);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="piece.bin"&&n.Role==UiRole.ListItem&&n.Focused);
            Key(ImGuiKey.Enter);Key(ImGuiKey.Escape);Reach("Add selected object",UiRole.Button);Key(ImGuiKey.Enter);
            Assert.False(window.Editor.IsOpen("scene.assets"));
            var added=Assert.Single(window.Editor.Session.Current.Objects,o=>o.Id is not ("root" or "child"));
            Assert.Equal(guid,added.Asset);Assert.Null(added.Parent);Assert.Equal(added.Id,window.Editor.Primary);
            // Ctrl+Tab is actual native keyboard navigation; tree observations select the field, never dispatch actions.
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            try
            {
                var found=false;
                for(var i=0;i<16;i++)
                {
                    Key(ImGuiKey.Tab);var target=ImGui.GetCurrentContext().NavWindowingTarget;
                    var title=target.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)target.Name)??"";
                    if(title.Contains("###inspector",StringComparison.Ordinal)){found=true;break;}
                }
                Assert.True(found,"Ctrl+Tab did not find the Inspector window.");
            }
            finally {io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);host.Frames(workspace.Draw,3);}
            void Edit(string label,string text)
            {Reach(label,UiRole.TextField);Key(ImGuiKey.A,ctrl:true);host.Type(workspace.Draw,text);Key(ImGuiKey.Enter);Key(ImGuiKey.Escape);}
            Edit("Position X","4.5");Assert.Equal(4.5f,window.Editor.Session.Current.Find(added.Id)!.Position.X);
            Edit("Inside","root");Assert.Equal("root",window.Editor.Session.Current.Find(added.Id)!.Parent);
            Key(ImGuiKey.Z,ctrl:true);Assert.Null(window.Editor.Session.Current.Find(added.Id)!.Parent);
            Assert.Equal(4.5f,window.Editor.Session.Current.Find(added.Id)!.Position.X);
            Key(ImGuiKey.Y,ctrl:true);Assert.Equal("root",window.Editor.Session.Current.Find(added.Id)!.Parent);
            Assert.Equal(original,File.ReadAllText(path));
            Key(ImGuiKey.S,ctrl:true);Assert.False(window.Editor.Session.Dirty);var saved=File.ReadAllText(path);Assert.NotEqual(original,saved);
            var expected=window.Editor.Session.Current;
            Key(ImGuiKey.W,ctrl:true);Assert.Null(workspace.SceneWindow);
            Reach("level.json",UiRole.ListItem,true);Key(ImGuiKey.Enter);
            Assert.NotSame(window,workspace.SceneWindow);Assert.Equal(saved,new SceneFormat().Write(workspace.SceneWindow!.Editor.Session.Current));
            Assert.False(workspace.SceneWindow.Editor.Session.History.CanUndo);
            var runtime=SceneTool.RuntimeScene.Load(saved,assetExists:id=>id==guid);
            Assert.True(runtime.AssetsChecked);Assert.Empty(runtime.AssetIssues);
            Assert.Equal(expected.Objects.Length,runtime.Objects.Count);
            foreach(var item in expected.Objects)
            {
                var loaded=Assert.Single(runtime.Objects,o=>o.Id==item.Id);
                Assert.Equal(item.Parent,loaded.Parent?.Id);Assert.Equal(item.Asset,loaded.Asset);
                Assert.Equal(item.Position,loaded.LocalPosition);Assert.Equal(item.Rotation,loaded.LocalRotation);Assert.Equal(item.Scale,loaded.LocalScale);
            }
            Assert.Equal(saved,File.ReadAllText(path));
        }
        finally{Directory.Delete(root,true);}
    }
}
