using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;
using ImGui=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DataOpenPerf21Tests(ITestOutputHelper output)
{
    [Fact]
    public void Hundred_files_and_a_five_thousand_row_table_open_usable_without_source_changes()
    {
        var root=Directory.CreateTempSubdirectory("data-open-perf21-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"entry.schema.json"),"""{"type":"object","required":["rows"],"properties":{"rows":{"type":"array","items":{"type":"object","required":["id","name","price","count","enabled"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"price":{"type":"number","minimum":0},"count":{"type":"integer","minimum":0},"enabled":{"type":"boolean"}},"additionalProperties":false}}},"additionalProperties":false}""");
            for(var file=0;file<100;file++)File.WriteAllText(Path.Combine(data,$"entry-{file:D3}.json"),JsonSerializer.Serialize(new{rows=Enumerable.Range(0,file==0?5000:64).Select(i=>new{id=$"item_{file:D3}_{i:D4}",name=$"Fixture item {file}:{i}",price=i*1.25,count=i,enabled=i%2==0})}));
            var original=Directory.GetFiles(root,"*.json",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
            using var host=new HeadlessHost(1600,1000){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            if(Environment.GetEnvironmentVariable("PERF21_GATE") is {} gate)
            {
                File.WriteAllText(gate+".pid",Environment.ProcessId.ToString());
                Assert.True(SpinWait.SpinUntil(()=>File.Exists(gate+".go"),TimeSpan.FromSeconds(60)),"Profiler did not release workload gate");
            }
            var measurements=new List<object>();
            for(var run=0;run<3;run++)
            {
                using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,$"local-{run}/recent.json"))){Loaded=[new Adapter()]};
                var start=Stopwatch.GetTimestamp();Assert.True(launcher.OpenFolder(root),launcher.Said);var opened=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var workspace=launcher.Workspace!;var table=workspace.TableOf("entry-000.json")!;
                var frames=0;
                bool Usable()=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.TextField&&n.Label.Contains("Fixture item 0:0",StringComparison.Ordinal)&&n.Bounds.Width>0&&n.Bounds.Height>0);
                do{host.Frame(launcher.Draw);frames++;}while(!Usable()&&frames<5);
                var usable=Stopwatch.GetElapsedTime(start).TotalMilliseconds;Assert.True(Usable(),string.Join(" | ",Ui.Snapshot().Nodes.Take(100).Select(n=>$"{n.Role}:{n.Label}")));
                Assert.Equal(100,workspace.Project.Files.Count);Assert.Equal(5000,table.Items().Count);Assert.Equal(5,table.Columns().Count);
                Assert.All(workspace.Project.Files,f=>{Assert.Equal(DataFileState.Open,f.State);Assert.False(f.Dirty);Assert.DoesNotContain(f.Session!.LastIssues,i=>i.Severity==Severity.Error);});
                Assert.Equal("entry-000.json",workspace.Active!.Name);
                var session=workspace.Active.Session!;Assert.True(table.SetCell("/rows/0","price",42.5));Assert.True(session.Dirty);Assert.True(session.Undo());Assert.False(session.Dirty);
                measurements.Add(new{run,openMs=opened,firstUsableMs=usable,frames});
            }
            foreach(var pair in original)Assert.Equal(pair.Value,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(pair.Key))));
            var result=JsonSerializer.Serialize(new{files=100,largeRows=5000,otherRows=64,columns=5,inputBytes=original.Keys.Sum(p=>new FileInfo(p).Length),headless=true,cpuRaster=false,measurements},new JsonSerializerOptions{WriteIndented=true});
            output.WriteLine(result);if(Environment.GetEnvironmentVariable("PERF21_RECEIPT") is {} receipt)File.WriteAllText(receipt,result);
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData("malformed")][InlineData("schema")][InlineData("rule")][InlineData("throw")][InlineData("changed")]
    public void Adapter_selected_schema_still_uses_current_strict_read_and_full_validation(string mode)
    {
        var root=Directory.CreateTempSubdirectory("data-open-guards-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;var path=Path.Combine(data,"entry.json");
            File.WriteAllText(Path.Combine(schemas,"entry.schema.json"),"""{"type":"object","required":["price"],"properties":{"price":{"type":"number","minimum":0}}}""");
            File.WriteAllText(path,mode=="malformed"?"{ bad JSON":mode=="schema"?"{\"price\":-1}":"{\"price\":1}");
            var calls=0;var project=DataProject.Open(data,schemas,rules:_=>mode=="rule"?new RefusingRule():null);
            project.SchemaFor=_=>{calls++;if(mode=="throw")throw new InvalidOperationException("fixture schema choice failed");if(mode=="changed")File.WriteAllText(path,"{\"price\":7}");return "entry.schema.json";};
            var file=project.Find("entry.json")!;Assert.Equal(mode=="changed",file.Open());Assert.Equal(1,calls);
            if(mode=="changed")
            {
                Assert.Equal(7,file.Session!.Current.Root.GetProperty("price").GetInt32());Assert.Equal(DiskState.Unchanged,file.Session.CheckDisk());Assert.False(file.Dirty);
                Assert.True(file.Open());Assert.Equal(1,calls);
            }
            else{Assert.Null(file.Session);Assert.NotEmpty(file.Problems);}
            if(mode=="malformed")Assert.Contains("isn't valid JSON",file.Problems[0].Message);
            if(mode=="rule")Assert.Contains("fixture rule refused",file.Problems[0].Message);
            if(mode=="throw")Assert.Contains("fixture schema choice failed",file.Problems[0].Message);
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    private sealed class RefusingRule:IValidator<DataDocument>
    {public IReadOnlyList<Issue> Validate(DataDocument doc)=>[Issue.Error("fixture rule refused")];}

    private sealed class Adapter:IProjectAdapter
    {
        public string Name=>"Data open fixture";
        public bool Recognises(ProjectPaths paths)=>true;
        public DataProject Open(ProjectPaths paths){var p=DataProject.Open(paths.Data,paths.Schemas);p.SchemaFor=_=>"entry.schema.json";return p;}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table)=>table.List="/rows";
    }
}
