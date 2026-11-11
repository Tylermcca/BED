using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class BigSceneFramePerf20Tests(ITestOutputHelper output)
{
    [Fact]
    public void Five_thousand_cube_selection_move_preview_and_Outliner_scroll_keep_exact_scene_semantics()
    {
        var root=Directory.CreateTempSubdirectory("big-scene-perf20-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var scene=new Scene(Enumerable.Range(0,5000).Select(i=>SceneObject.At($"cube_{i:D4}",null,new((i%100-50)*2,0,(i/100-25)*2))).ToImmutableArray());
            var disk=Path.Combine(data,"level.json");var original=new SceneFormat().Write(scene);File.WriteAllText(disk,original);
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.Documents.BindScene("level.json","scene",out _));Assert.True(workspace.Show("level.json"));
            var editor=workspace.SceneWindow!.Editor;scene=editor.Session.Current;var view=workspace.SceneViewport!;view.Camera=new(Vector3.Zero,.3f,.65f,300);
            using var host=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());host.Frames(workspace.Draw,4);
            var times=new List<object>();
            void Frame(string phase,Action? input=null)
            {
                var start=Stopwatch.GetTimestamp();input?.Invoke();var inputMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                start=Stopwatch.GetTimestamp();host.Frame(workspace.Draw);var frameMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                times.Add(new{phase,inputMs,frameMs,totalMs=inputMs+frameMs});
            }
            bool Inside(UiSnapshot tree,UiNode node,string name)
            {for(var at=node.Parent;at>=0;at=tree.Nodes[at].Parent)if(tree.Nodes[at].Role==UiRole.Window&&tree.Nodes[at].Label==name)return true;return false;}
            var initialTree=Ui.Snapshot();
            var row=initialTree.Nodes.First(n=>n.Role==UiRole.ListItem&&Inside(initialTree,n,"Outliner"));
            ImGui.GetIO().AddMousePosEvent(row.Bounds.X+20,row.Bounds.Y+row.Bounds.Height/2);Frame("selection-hover");
            ImGui.GetIO().AddMouseButtonEvent(0,true);Frame("selection-press");ImGui.GetIO().AddMouseButtonEvent(0,false);Frame("selection-release");
            Assert.Single(editor.Selection);
            var ids=scene.Objects.Where(o=>o.Id!="cube_2550").Select(o=>o.Id).Append("cube_2550").ToArray();
            Frame("select-all",()=>Assert.True(editor.Select(ids)));Assert.Equal(5000,editor.Selection.Count);
            host.Frames(workspace.Draw,2);
            var handle=view.MoveTargets.Single(h=>h.Kind==MoveHandleKind.X);var from=(handle.Start+handle.End)/2+view.LastRect.Origin;
            var direction=Vector2.Normalize(handle.End-handle.Start);
            ImGui.GetIO().AddMousePosEvent(from.X,from.Y);Frame("move-hover");
            ImGui.GetIO().AddMouseButtonEvent(0,true);Frame("move-capture");Frame("move-captured");
            for(var i=1;i<=3;i++)
            {
                var target=from+direction*(35+i*12);ImGui.GetIO().AddMousePosEvent(target.X,target.Y);Frame("move-preview");
                Assert.Same(scene,editor.Session.Current);Assert.Empty(editor.Session.Journal);
                Assert.Equal(5000,view.RenderRequest!.Items.Count);
            }
            ImGui.GetIO().AddMouseButtonEvent(0,false);Frame("move-release");Frame("move-committed");
            Assert.Single(editor.Session.Journal);Assert.Equal(1,editor.Session.History.UndoCount);
            var moved=editor.Session.Current;var delta=moved.Find("cube_2550")!.Position-scene.Find("cube_2550")!.Position;
            Assert.True(delta.X>0);Assert.Equal(0,delta.Y);Assert.Equal(0,delta.Z);
            foreach(var item in scene.Objects)Assert.Equal(item.Position+delta,moved.Find(item.Id)!.Position);
            var outliner=Ui.Snapshot().Nodes.Single(n=>n.Role==UiRole.Window&&n.Label=="Outliner");
            ImGui.GetIO().AddMousePosEvent(outliner.Bounds.X+40,outliner.Bounds.Y+outliner.Bounds.Height/2);Frame("scroll-hover");
            var beforeTree=Ui.Snapshot();
            var before=beforeTree.Nodes.Where(n=>n.Role==UiRole.ListItem&&Inside(beforeTree,n,"Outliner")).Select(n=>n.Label).ToArray();
            for(var i=0;i<3;i++){ImGui.GetIO().AddMouseWheelEvent(0,-20);Frame("outliner-scroll");}
            var afterTree=Ui.Snapshot();
            var after=afterTree.Nodes.Where(n=>n.Role==UiRole.ListItem&&Inside(afterTree,n,"Outliner")).Select(n=>n.Label).ToArray();
            Assert.NotEqual(before,after);Assert.Equal(ids,editor.Selection);Assert.Single(editor.Session.Journal);
            Assert.Equal(original,File.ReadAllText(disk));Assert.True(editor.Session.Undo());Assert.Same(scene,editor.Session.Current);
            var json=JsonSerializer.Serialize(new{objects=5000,width=1400,height=1000,cpuRaster=false,geometry="5000 default cube meshes, 60000 triangles",times},new JsonSerializerOptions{WriteIndented=true});
            output.WriteLine(json);if(Environment.GetEnvironmentVariable("PERF20_RECEIPT") is {} receipt)File.WriteAllText(receipt,json);
        }
        finally{Directory.Delete(root,true);}
    }
}
