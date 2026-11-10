using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class BigTablePerf13Tests(ITestOutputHelper output)
{
    [Fact]
    public void Five_thousand_row_table_open_sort_filter_and_edit_keep_exact_order_and_undo()
    {
        var root=Directory.CreateTempSubdirectory("big-table-perf13-").FullName;
        try
        {
            var repo=new DirectoryInfo(AppContext.BaseDirectory);
            while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"fixtures","data-perf","make_data.py")))repo=repo.Parent;
            Assert.NotNull(repo);
            var start=new ProcessStartInfo("python"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-c","import runpy,sys; runpy.run_path(sys.argv[1])['generate'](sys.argv[2],5000)",Path.Combine(repo.FullName,"fixtures","data-perf","make_data.py"),root})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)!)
            {var stdout=process.StandardOutput.ReadToEnd();var stderr=process.StandardError.ReadToEnd();Assert.True(process.WaitForExit(10000));Assert.True(process.ExitCode==0,stdout+stderr);}
            var path=Path.Combine(root,"rows-5000.json");var bytes=File.ReadAllBytes(path);
            var schemas=new JsonSchemas().Add("schema.json",File.ReadAllText(Path.Combine(root,"schema.json")));
            var samples=new List<Dictionary<string,double>>();
            using var ui=new HeadlessHost(1280,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            for(var run=0;run<4;run++)
            {
                var sample=new Dictionary<string,double>();var watch=Stopwatch.StartNew();
                var session=DataKit.Open(path,schemas,"schema.json");
                var editor=new EditorModel<DataDocument>(session,DataEditing.OutlineFor(schemas,"schema.json"));
                var table=new DataTableView(editor,DataEditing.ObjectType(schemas,"schema.json")){List="/rows"};
                sample["open"]=watch.Elapsed.TotalMilliseconds;
                void Draw()
                {
                    ImGui.SetNextWindowPos(new float2(0,0));ImGui.SetNextWindowSize(new float2(1200,740));
                    ImGui.Begin("Big table performance"u8);table.Draw();ImGui.End();
                }
                double Time(Action action){watch.Restart();action();return watch.Elapsed.TotalMilliseconds;}
                sample["firstFrame"]=Time(()=>ui.Frame(Draw));ui.Frames(Draw,2);
                sample["idleFrame"]=Time(()=>ui.Frame(Draw));
                Assert.Equal(5000,table.Items().Count);Assert.Equal(22,table.Columns().Count);
                IReadOnlyList<string>? sorted=null;
                var firstFilter=new DataTableView(editor,table.Type){List="/rows",Filter="needle"};
                sample["firstFilterQuery"]=Time(()=>sorted=firstFilter.Visible());
                Assert.Equal(Enumerable.Range(0,50).Select(i=>$"/rows/{i*100}"),sorted!);
                sample["sortQuery"]=Time(()=>{table.Sort=("n00",false);sorted=table.Visible();});
                sample["sortFrame"]=Time(()=>ui.Frame(Draw));
                Assert.Equal(Enumerable.Range(0,5000).Reverse().Select(i=>$"/rows/{i}"),sorted!);
                sample["filterQuery"]=Time(()=>{table.Sort=null;table.Filter="needle";sorted=table.Visible();});
                sample["filterFrame"]=Time(()=>ui.Frame(Draw));
                Assert.Equal(Enumerable.Range(0,50).Select(i=>$"/rows/{i*100}"),sorted!);
                Assert.False(session.Dirty);Assert.Equal(0,session.Revision);Assert.Empty(session.Journal);
                table.Filter="";table.Sort=null;ui.Frames(Draw,2);
                sample["editCommand"]=Time(()=>Assert.True(table.SetCell("/rows/0","score",-1d),table.Said));
                sample["editFrame"]=Time(()=>ui.Frame(Draw));
                Assert.Equal(-1,session.Current.At("/rows/0/score")!.Value.GetDouble());Assert.True(session.Dirty);
                Assert.Single(session.Journal);Assert.Equal(1,session.History.UndoCount);
                var edited=session.Format.Write(session.Current);Assert.True(session.Undo());Assert.False(session.Dirty);
                Assert.True(session.Redo());Assert.Equal(edited,session.Format.Write(session.Current));
                Assert.Equal(bytes,File.ReadAllBytes(path));
                samples.Add(sample);output.WriteLine(JsonSerializer.Serialize(new{phase="sample",run,rows=5000,columns=22,bytes=bytes.Length,ms=sample}));
            }
            var median=samples[0].Keys.ToDictionary(key=>key,key=>samples.Skip(1).Select(s=>s[key]).Order().ElementAt(1));
            output.WriteLine(JsonSerializer.Serialize(new{phase="median",rows=5000,columns=22,bytes=bytes.Length,ms=median}));
            // Report timing rather than adding a hardware-dependent CI threshold.
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root,true);
        }
    }
}
