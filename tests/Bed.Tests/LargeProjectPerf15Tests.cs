using System.Diagnostics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class LargeProjectPerf15Tests(ITestOutputHelper output)
{
    [Fact]
    public void Recursive_project_measures_first_usable_and_idle_frames_without_changing_any_input()
    {
        var root=Directory.CreateTempSubdirectory("large-project-perf15-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"entry.schema.json"),"""{"type":"object","required":["rows"],"properties":{"rows":{"type":"array","items":{"type":"object","required":["id","text","value"],"properties":{"id":{"type":"string"},"text":{"type":"string"},"value":{"type":"number","minimum":0}},"additionalProperties":false}}},"additionalProperties":false}""");
            for(var file=0;file<170;file++)
            {
                var name=file<50?$"root-{file:000}.json":$"stories/region-{file%12:00}/chapter-{file%7:00}/entry-{file:000}.json";
                var path=Path.Combine(data,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path,JsonSerializer.Serialize(new {rows=Enumerable.Range(0,128).Select(row=>new{id=$"item-{file}-{row}",text=$"The receiving clerk records crate {row} at dock {file}. "+new string('a',256),value=row})}));
            }
            var inputs=Directory.GetFiles(root,"*.json",SearchOption.AllDirectories).ToDictionary(path=>path,File.ReadAllBytes);
            using var host=new HeadlessHost(1600,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            for(var run=0;run<3;run++)
            {
                var adapter=new FixtureAdapter();
                using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,$"settings-{run}","recent.json"))){Loaded=[adapter]};
                var watch=Stopwatch.StartNew();Assert.True(launcher.OpenFolder(root),launcher.Said);var open=watch.Elapsed.TotalMilliseconds;
                var workspace=launcher.Workspace!;Assert.Equal(170,workspace.Project.Files.Count);
                Assert.All(workspace.Project.Files,file=>{Assert.Equal(DataFileState.Open,file.State);Assert.NotNull(file.Session);Assert.False(file.Dirty);});
                watch.Restart();host.Frame(launcher.Draw);var first=watch.Elapsed.TotalMilliseconds;
                bool Usable()=>Ui.Snapshot().Nodes.Any(node=>node.Role==UiRole.ListItem&&node.Label.Trim()=="root-000.json"&&node.Bounds.Width>0&&node.Bounds.Height>0);
                var usableFrames=1;while(!Usable()&&usableFrames<4){host.Frame(launcher.Draw);usableFrames++;}
                var usable=watch.Elapsed.TotalMilliseconds;Assert.True(Usable());
                host.Frames(launcher.Draw,3);var idle=new List<double>();var allocations=GC.GetAllocatedBytesForCurrentThread();
                for(var frame=0;frame<60;frame++){watch.Restart();host.Frame(launcher.Draw);idle.Add(watch.Elapsed.TotalMilliseconds);}
                var bytesPerFrame=(GC.GetAllocatedBytesForCurrentThread()-allocations)/60;
                watch.Restart();foreach(var file in workspace.Project.Files){_ = file.ReadOnlyReason;_ = file.ReadOnlyReason;}var pathSweep=watch.Elapsed.TotalMilliseconds;
                var editor=workspace.Window!.Editor;editor.SetOpen("project.files",false);host.Frames(launcher.Draw,2);
                watch.Restart();host.Frame(launcher.Draw);var withoutFiles=watch.Elapsed.TotalMilliseconds;editor.SetOpen("project.files",true);
                Assert.False(workspace.Show("stories/region-02/chapter-03/entry-170.json"));
                Assert.True(workspace.Show(workspace.Project.Files.Last().Name),workspace.Said);host.Frames(launcher.Draw,3);
                var active=new List<double>();for(var frame=0;frame<30;frame++){watch.Restart();host.Frame(launcher.Draw);active.Add(watch.Elapsed.TotalMilliseconds);}
                output.WriteLine(JsonSerializer.Serialize(new{run,files=170,rows=170*128,inputBytes=inputs.Sum(pair=>pair.Value.Length),openMs=open,firstFrameMs=first,openToFrameMs=open+first,openToUsableMs=open+usable,usableFrames,idleMedianMs=idle.Order().ElementAt(30),idleP95Ms=idle.Order().ElementAt(56),idleMaxMs=idle.Max(),idleBytesPerFrame=bytesPerFrame,pathSweepMs=pathSweep,withoutFilesMs=withoutFiles,activeMedianMs=active.Order().ElementAt(15),activeP95Ms=active.Order().ElementAt(28)}));
            }
            foreach(var (path,bytes) in inputs)Assert.Equal(bytes,File.ReadAllBytes(path));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Clipped_files_keep_keyboard_navigation_repair_and_fresh_read_only_status()
    {
        var root=Directory.CreateTempSubdirectory("clipped-files-perf15-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"entry.schema.json"),"""{"type":"object","properties":{"value":{"type":"integer","minimum":0}}}""");
            for(var index=0;index<250;index++)File.WriteAllText(Path.Combine(data,$"entry-{index:000}.json"),index==249?"""{"value":-1}""":"""{"value":0}""");
            var adapter=new FixtureAdapter();
            using var host=new HeadlessHost(1200,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local","recent.json"))){Loaded=[adapter]};
            Assert.True(launcher.OpenFolder(root),launcher.Said);host.Frames(launcher.Draw,5);var workspace=launcher.Workspace!;
            adapter.Reads=0;host.Frame(launcher.Draw);Assert.InRange(adapter.Reads,1,60);
            UiNode Row(string text)=>Assert.Single(Ui.Snapshot().Nodes,node=>node.Role==UiRole.ListItem&&node.Label.Contains(text,StringComparison.Ordinal));
            adapter.Locked="entry-000.json";host.Frame(launcher.Draw);Assert.Contains("read-only",Row("entry-000.json").Label);
            adapter.Locked=null;host.Frame(launcher.Draw);Assert.DoesNotContain("read-only",Row("entry-000.json").Label);
            File.Delete(Path.Combine(data,"entry-001.json"));host.Frame(launcher.Draw);Assert.Contains("gone from the folder",Row("entry-001.json").Label);
            File.WriteAllText(Path.Combine(data,"entry-001.json"),"""{"value":0}""");host.Frame(launcher.Draw);Assert.DoesNotContain("gone from the folder",Row("entry-001.json").Label);
            var bounds=Row("entry-000.json").Bounds;host.Click(launcher.Draw,bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);
            host.Press(launcher.Draw,Brutal.ImGuiApi.ImGuiKey.End);host.Frames(launcher.Draw,3);
            Assert.Contains("can't open",Row("entry-249.json").Label);
            var repair=Assert.Single(Ui.Snapshot().Nodes,node=>node.Role==UiRole.Button&&node.Label=="Repair").Bounds;
            host.Click(launcher.Draw,repair.X+repair.Width/2,repair.Y+repair.Height/2);
            Assert.Equal("entry-249.json",workspace.Active!.Name);Assert.True(workspace.Active.Session!.Repairing);
            Assert.Equal("""{"value":-1}""",File.ReadAllText(Path.Combine(data,"entry-249.json")));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    private sealed class FixtureAdapter:IProjectAdapter
    {
        public int Reads;
        public string? Locked;
        public string Name=>"Recursive fixture";
        public bool Recognises(ProjectPaths paths)=>true;
        public DataProject Open(ProjectPaths paths){var project=DataProject.Open(paths.Data,paths.Schemas,readOnlyFile:name=>{Reads++;return name==Locked?"Locked by fixture policy.":null;},recursive:true);project.SchemaFor=_=>"entry.schema.json";return project;}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
}
