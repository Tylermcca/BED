using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SaveLevelPerf10Tests(ITestOutputHelper output)
{
    [Fact]
    public void Real_CtrlS_saves_five_and_fifty_thousand_objects_without_changing_drafts_or_history()
    {
        var results=new List<(int Count,long Bytes,double Save,double Key)>();
        foreach(var count in new[]{5000,50000})
        {
            var root=Directory.CreateTempSubdirectory("save-level-perf10-").FullName;
            try
            {
                var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
                var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
                var path=Path.Combine(data,"level.json");var format=new SceneFormat();
                var scene=new Scene(Enumerable.Range(0,count).Select(i=>SceneObject.At("object_"+i,null,new Vector3(i%100,i/100,0))).ToImmutableArray());
                File.WriteAllText(path,format.Write(scene));
                using var workspace=new DataWorkspace(DataProject.Open(data,schemas));
                Assert.True(workspace.Documents.BindScene("level.json","scene",out var why),why);
                Assert.True(workspace.Show("level.json"),workspace.Said);
                var window=workspace.SceneWindow!;var session=window.Editor.Session;
                foreach(var panel in window.Editor.Panels)window.Editor.SetOpen(panel.Id,false);
                // Isolate Save from the separately owned large Outliner/viewport drawing cost.
                // Draw still calls production Keys before Begin; real native Ctrl+S is unchanged.
                void DrawSaveWindow(){ImGui.SetNextWindowCollapsed(true,ImGuiCond.Always);window.Draw();}
                var realSave=window.SaveAction!;var saves=new List<double>();
                window.SaveAction=()=>
                {
                    var clock=Stopwatch.StartNew();
                    var result=realSave();
                    clock.Stop();saves.Add(clock.Elapsed.TotalMilliseconds);return result;
                };
                using var host=new HeadlessHost(1400,900){Drawing=false};
                EditorWindow<Scene>.Configure(ImGui.GetIO());host.Frames(DrawSaveWindow,3);
                var keyTimes=new List<double>();
                // First actual save warms the same production route; retain its number separately.
                for(var run=0;run<4;run++)
                {
                    Assert.True(session.Execute(new MoveObject("object_0",new Vector3(run+1,2,3))));
                    var draft=session.Current;var revision=session.Revision;var journal=session.Journal.ToArray();
                    var undo=session.History.UndoCount;var saveCalls=saves.Count;
                    var clock=Stopwatch.StartNew();host.Press(DrawSaveWindow,ImGuiKey.S,ctrl:true);clock.Stop();
                    Assert.Equal(saveCalls+1,saves.Count);Assert.Equal("Saved level.json.",window.Status);
                    Assert.Same(draft,session.Current);Assert.Equal(revision,session.Revision);
                    Assert.Equal(journal,session.Journal);Assert.Equal(undo,session.History.UndoCount);Assert.False(session.Dirty);
                    var saved=File.ReadAllText(path);Assert.Equal(format.Write(draft),saved);
                    var reopened=format.Read(saved);Assert.Equal(count,reopened.Objects.Length);
                    Assert.Equal(format.Write(draft),format.Write(reopened));Assert.Empty(Directory.GetFiles(data,"*.tmp"));
                    if(run==0)output.WriteLine($"{count:N0} warm-up save: {saves[^1]:F3} ms; Ctrl+S frames: {clock.Elapsed.TotalMilliseconds:F3} ms");
                    else keyTimes.Add(clock.Elapsed.TotalMilliseconds);
                }
                var timed=saves.Skip(1).ToArray();var bytes=new FileInfo(path).Length;
                double Median(IEnumerable<double> values)=>values.Order().ElementAt(1);
                var save=Median(timed);var key=Median(keyTimes);results.Add((count,bytes,save,key));
                output.WriteLine($"{count:N0} objects, {bytes:N0} UTF-8 bytes: save samples [{string.Join(", ",timed.Select(ms=>ms.ToString("F3")))}] ms; median {save:F3} ms; Ctrl+S frames median {key:F3} ms.");
                Assert.True(session.Undo());Assert.True(session.Dirty);Assert.True(session.Redo());Assert.False(session.Dirty);
                Assert.Equal(4,session.History.UndoCount);
            }
            finally{Directory.Delete(root,true);}
        }
        output.WriteLine($"50k/5k ratios: objects 10.000; bytes {(double)results[1].Bytes/results[0].Bytes:F3}; save {results[1].Save/results[0].Save:F3}; Ctrl+S frames {results[1].Key/results[0].Key:F3}.");
        output.WriteLine("Save timing wraps the unchanged real workspace SaveAction: validation, policy and conflict checks, serialization, atomic file-group write and MarkSaved. Ctrl+S timing includes real native keyboard press/release and UI frames, with CPU rasterization disabled, optional panels closed and the editor collapsed to isolate Save from Outliner/viewport rendering. Generation, open, edits and exact reopen checks are outside timing. No assets or visible windows. Timing is evidence, not a flaky CI threshold.");
    }
}
