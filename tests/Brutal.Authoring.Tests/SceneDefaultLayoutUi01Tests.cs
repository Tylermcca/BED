using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneDefaultLayoutUi01Tests
{
    [Fact]
    public void Default_scene_files_outliner_inspector_and_viewport_have_disjoint_tree_bounds()
    {
        var root=Directory.CreateTempSubdirectory("scene-layout-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var path=Path.Combine(data,"level.json");
            var original=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SceneStarterReview","scene.json"));
            File.WriteAllText(path,original);
            var firstCloses=0;var secondCloses=0;
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas)){CloseProject=()=>{firstCloses++;return true;}};
            Assert.True(workspace.Documents.BindScene("level.json","scene",out _));
            Assert.True(workspace.Show("level.json"));
            using var host=new HeadlessHost(1600,900){Drawing=false};
            EditorWindow<Scene>.Configure(ImGui.GetIO());host.Frames(workspace.Draw,5);
            var windows=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Window).ToArray();
            var panels=new[]{"Files","Outliner","Inspector","Viewport"}.Select(label=>Assert.Single(windows,n=>n.Label==label)).ToArray();
            foreach(var panel in panels)
            {
                Assert.False(panel.Bounds.IsEmpty);
                Assert.True(panel.Bounds.X>=0&&panel.Bounds.Y>=0&&panel.Bounds.X+panel.Bounds.Width<=1600&&panel.Bounds.Y+panel.Bounds.Height<=900,$"Outside viewport: {panel.Label} {panel.Bounds}");
            }
            for(var i=0;i<panels.Length;i++)for(var j=i+1;j<panels.Length;j++)
            {
                var a=panels[i].Bounds;var b=panels[j].Bounds;
                Assert.True(a.X+a.Width<=b.X||b.X+b.Width<=a.X||a.Y+a.Height<=b.Y||b.Y+b.Height<=a.Y,$"Overlapping panels: {panels[i].Label} {a}; {panels[j].Label} {b}");
            }
            Assert.True(panels[0].Bounds.Y+panels[0].Bounds.Height<=panels[1].Bounds.Y);
            Assert.Equal(original,File.ReadAllText(path));
            Assert.False(workspace.SceneWindow!.Editor.Session.Dirty);
            using var second=new DataWorkspace(workspace.Project){CloseProject=()=>{secondCloses++;return true;}};
            Assert.True(second.Show("level.json"));Assert.True(second.Show("level.json"));
            Assert.Same(workspace.SceneWindow.Editor,second.SceneWindow!.Editor);
            var shared=Assert.Single(second.SceneWindow.Editor.Panels,p=>p.Id=="project.files");
            host.Frames(second.Draw,3);
            Assert.False(shared.Commands!.Single(c=>c.Id=="close_project").Run());
            Assert.StartsWith("Closing a project with typed documents",second.Said);
            Assert.StartsWith("Showing",workspace.Said);
            Assert.Equal(0,firstCloses);Assert.Equal(0,secondCloses);
            host.Frames(workspace.Draw,3);
            Assert.False(shared.Commands!.Single(c=>c.Id=="close_project").Run());
            Assert.StartsWith("Closing a project with typed documents",workspace.Said);
            Assert.Equal(0,firstCloses);Assert.Equal(0,secondCloses);
            Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Files");
        }
        finally{Directory.Delete(root,true);}
    }
}
