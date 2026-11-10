using System.Diagnostics;
using System.Collections.Immutable;
using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;
namespace Brutal.Authoring.Tests;

public sealed class LargeLevelQa14Tests(ITestOutputHelper output)
{
    [Fact]
    public void Five_thousand_object_open_select_move_undo_and_real_save_preserve_every_transform_and_one_undo_step()
    {
        var root=Directory.CreateTempSubdirectory("large-level-qa14-").FullName;DataWorkspace? workspace=null;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var path=Path.Combine(data,"level.json");var format=new SceneFormat();
            var scene=new Scene(Enumerable.Range(0,5000).Select(i=>SceneObject.At("object_"+i,null,new Vector3(i%100,i/100,0))).ToImmutableArray());
            File.WriteAllText(path,format.Write(scene));var original=File.ReadAllBytes(path);var timings=new Dictionary<string,double>();
            void Time(string action,Action run){var watch=Stopwatch.StartNew();run();watch.Stop();timings.Add(action,watch.Elapsed.TotalMilliseconds);}
            Time("open",()=>
            {
                workspace=new(DataProject.Open(data,schemas));Assert.True(workspace.Documents.BindScene("level.json","scene",out var why),why);
                Assert.True(workspace.Show("level.json"),workspace.Said);
            });
            var editor=workspace!.SceneWindow!.Editor;var session=editor.Session;Assert.Equal(5000,session.Current.Objects.Length);
            Time("select-all",()=>Assert.True(editor.Select(session.Current.Objects.Select(o=>o.Id).ToArray())));Assert.Equal(5000,editor.Selection.Count);
            var before=session.Current;var revision=session.Revision;var delta=new Vector3(2,0,-3);
            Time("move",()=>
            {
                var phase=Stopwatch.StartNew();var plan=SceneTranslation.World(before,editor.Selection,delta);timings.Add("move planning",phase.Elapsed.TotalMilliseconds);Assert.True(plan.Valid,string.Join("; ",plan.Issues));
                Assert.Equal(5000,plan.Roots.Count);phase.Restart();Assert.True(editor.ExecuteAt(revision,plan.Commands,plan.Roots.ToArray()));timings.Add("move commit",phase.Elapsed.TotalMilliseconds);
            });
            Assert.Equal(1,session.History.UndoCount);Assert.Equal(revision+1,session.Revision);
            for(var i=0;i<5000;i++)Assert.Equal(scene.Objects[i].Position+delta,session.Current.Objects[i].Position);
            var moved=session.Current;
            Time("undo",()=>Assert.True(session.Undo()));Assert.Same(before,session.Current);Assert.Equal(5000,editor.Selection.Count);
            Assert.Equal(original,File.ReadAllBytes(path));Assert.True(session.Redo());Assert.Same(moved,session.Current);Assert.True(session.Dirty);
            Time("save",()=>Assert.True(workspace.Documents.SaveScene("level.json",out var why),why));
            Assert.False(session.Dirty);var saved=format.Read(File.ReadAllText(path));Assert.Equal(5000,saved.Objects.Length);
            for(var i=0;i<5000;i++){Assert.Equal(scene.Objects[i].Id,saved.Objects[i].Id);Assert.Equal(scene.Objects[i].Position+delta,saved.Objects[i].Position);}
            Assert.Equal(1,session.History.UndoCount);Assert.Empty(Directory.GetFiles(data,"*.tmp"));
            output.WriteLine("5,000 generated flat objects; cold typed-workspace open; no rendering/assets. Move includes planning, refusal guards, atomic batch/journal and validation. Redo restores moved state outside timing before a real atomic Save.");
            foreach(var (action,ms) in timings)output.WriteLine($"{action}: {ms:F3} ms");
        }
        finally{workspace?.Dispose();Directory.Delete(root,true);}
    }
}
