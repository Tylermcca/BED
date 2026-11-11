using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneAccessibilityTests
{
    [Theory]
    [InlineData(1024,1f)]
    [InlineData(1024,2f)]
    public void Narrow_typed_workspace_preserves_semantics_numeric_TRS_and_keyboard_history(int width,float scale)
    {
        var root=Directory.CreateTempSubdirectory("scene-access-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var path=Path.Combine(data,"room.json");
            var original=new SceneFormat().Write(new Scene([SceneObject.At("item",null,Vector3.Zero)]));
            File.WriteAllText(path,original);
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));
            Assert.True(workspace.Documents.BindScene("room.json","scene",out _));
            Assert.True(workspace.Show("room.json"));
            var window=workspace.SceneWindow!;Assert.True(window.Editor.Select("item"));
            using var host=new HeadlessHost(width,900){Drawing=false};
            EditorWindow<Scene>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;
            host.Frames(workspace.Draw,4);
            var nodes=Ui.Snapshot().Nodes;
            Assert.Contains(nodes,n=>n.Role==UiRole.Menu && n.Label=="Help");
            Assert.Contains(nodes,n=>n.Label=="Position X");
            Assert.Contains(nodes,n=>n.Label=="Yaw");
            Assert.Contains(nodes,n=>n.Label=="Scale X");
            Assert.True(workspace.SceneViewport!.LastRect.Size.X>0);
            Assert.True(workspace.SceneViewport.LastRect.Size.Y>0);
            // Inspector APIs commit typed numeric commands; semantic presence above is not a glyph/contrast claim.
            Assert.True(window.Inspector!.Set("position.x",2.5));
            Assert.True(window.Inspector.Set("rotation.yaw",90.0));
            Assert.True(window.Inspector.Set("scale.x",2.0));
            var item=window.Editor.Session.Current.Find("item")!;
            Assert.Equal(new Vector3(2.5f,0,0),item.Position);Assert.Equal(new Vector3(2,1,1),item.Scale);
            Assert.True(Math.Abs(Quaternion.Dot(Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),item.Rotation))>0.99999f);
            // Duplicate now has a shipped default and places the copy beside the original.
            Assert.Equal("Ctrl+D",window.Shortcuts.For("scene.duplicate")!.Value.ToString());
            Assert.True(window.HandleKey(Chord.Parse("Ctrl+D")),window.Status);
            Assert.Equal(2,window.Editor.Session.Current.Objects.Length);
            var copy=Assert.Single(window.Editor.Session.Current.Objects,o=>o.Id!="item");
            Assert.Equal(item.Position+Vector3.UnitX,copy.Position);Assert.Equal(item.Rotation,copy.Rotation);Assert.Equal(item.Scale,copy.Scale);
            Assert.True(window.HandleKey(Chord.Parse("Ctrl+Z")));Assert.Single(window.Editor.Session.Current.Objects);
            Assert.True(window.HandleKey(Chord.Parse("Ctrl+Z")));Assert.Equal(Vector3.One,window.Editor.Session.Current.Find("item")!.Scale);
            Assert.True(window.HandleKey(Chord.Parse("Ctrl+Z")));Assert.Equal(Quaternion.Identity,window.Editor.Session.Current.Find("item")!.Rotation);
            Assert.True(window.HandleKey(Chord.Parse("Ctrl+Z")));Assert.Equal(Vector3.Zero,window.Editor.Session.Current.Find("item")!.Position);
            Assert.False(window.Editor.Session.History.CanUndo);
            Assert.Equal(original,File.ReadAllText(path));
            // Duplicate selected its copies; undo removes them. Select the original again for the save exercise.
            Assert.True(window.Editor.Select("item"));
            // Continue the actual host journey only through its typed Save route, never Session.Save.
            Assert.True(window.Inspector.Set("position.x",3.0));
            var checks=0;workspace.Documents.SceneSaveCheck=e=>{Assert.Same(window.Editor,e.Editor);checks++;return null;};
            Assert.True(window.Run("editor.save"),window.Status);Assert.Equal(1,checks);
            Assert.False(window.Editor.Session.Dirty);Assert.True(window.Editor.Session.History.CanUndo);
            Assert.False(window.SaveAs(Path.Combine(data,"other.json")));Assert.False(File.Exists(Path.Combine(data,"other.json")));
            var saved=File.ReadAllText(path);Assert.NotEqual(original,saved);
            var runtime=SceneTool.RuntimeScene.Load(saved);
            Assert.Equal(new Vector3(3,0,0),Assert.Single(runtime.Objects).LocalPosition);
            Assert.True(workspace.Documents.CloseScene("room.json",SceneCloseDecision.Save,out var why),why);
            Assert.Null(workspace.SceneWindow);Assert.True(workspace.Show("room.json"));
            Assert.NotSame(window,workspace.SceneWindow);Assert.False(workspace.SceneWindow!.Editor.Session.History.CanUndo);
            Assert.Equal(new Vector3(3,0,0),workspace.SceneWindow.Editor.Session.Current.Find("item")!.Position);
            Assert.True(workspace.SceneWindow.Editor.Select("item"));
            Assert.True(workspace.SceneWindow.Inspector!.Set("position.x",4.0));
            Assert.False(window.Run("editor.save")); // An obsolete window must not save the replacement entry.
            Assert.True(workspace.SceneWindow.Editor.Session.Dirty);
            Assert.Equal(saved,File.ReadAllText(path));
        }
        finally { Directory.Delete(root,true); }
    }
}
