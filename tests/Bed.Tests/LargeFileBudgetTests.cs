using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

/// <summary>INVENTED neutral generated data and allocation/CPU growth guards. No wall-clock pass/fail limits.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class LargeFileBudgetTests(ITestOutputHelper output)
{
    // Four times the rows: linear work has ample room, a quadratic allocation regression approaches sixteen times.
    // CPU guard uses process CPU time to exclude descheduling; an additive 200ms absorbs timer/JIT/GC noise.
    [Theory]
    [InlineData("validate")]
    [InlineData("write")]
    [InlineData("rows")]
    [InlineData("agent-list")]
    [InlineData("agent-set")]
    [InlineData("snapshot")]
    public void Four_times_rows_has_linear_allocation_and_subquadratic_cpu_budget(string op)
    {
        using var small=new Rig(1000,"wide");using var big=new Rig(4000,"wide");
        var a=Budget(small,op);var b=Budget(big,op);
        output.WriteLine($"{op}: 1000 alloc={a.Alloc} cpu={a.Cpu:F2}; 4000 alloc={b.Alloc} cpu={b.Cpu:F2}");
        Assert.True(b.Alloc < a.Alloc*7+1_000_000,$"{op}: allocations grew superlinearly: {a.Alloc} -> {b.Alloc}");
        Assert.True(b.Cpu < a.Cpu*10+200,$"{op}: CPU cost grew beyond generous growth ceiling: {a.Cpu} -> {b.Cpu}");
    }
    private static (long Alloc,double Cpu) Budget(Rig r,string op)
    {
        var allocations=new List<long>();var cpu=new List<double>();
        for(int i=0;i<6;i++){
            r.Prepare(op);var bytes=GC.GetTotalAllocatedBytes(true);var before=Process.GetCurrentProcess().TotalProcessorTime;
            r.Act(op);var used=Process.GetCurrentProcess().TotalProcessorTime-before;bytes=GC.GetTotalAllocatedBytes(true)-bytes;
            r.Verify(op);r.Cleanup(op);if(i>0){allocations.Add(bytes);cpu.Add(used.TotalMilliseconds);}
        }
        allocations.Sort();cpu.Sort();return(allocations[2],cpu[2]);
    }
    [Fact]
    public void Expanding_ten_thousand_wide_rows_does_not_exhaust_one_frames_text_storage()
    {
        using var r=new Rig(10000,"wide");r.Ui();
        var error=Record.Exception(r.Expand);
        Assert.Null(error);
        Assert.Equal(10000,r.File.Session!.Current.At("/catalog/entries")!.Value.GetArrayLength());
        Assert.Equal(0,r.File.Session.Revision);
    }
    [Fact]
    public void Expanded_lists_submit_a_bounded_page_instead_of_all_logical_items()
    {
        foreach(var n in new[]{1000,4000}){
            using var r=new Rig(n,"wide");r.Ui();r.Expand();
            var submitted=Enumerable.Range(0,n).Count(i=>r.Window!.InspectorPlace("\u001f/entries/"+i)!=null);
            output.WriteLine($"{n} rows: {submitted} item headers submitted");
            Assert.InRange(submitted,1,100); // twice the current page, independent of machine load
            Assert.Equal(0,r.File.Session!.Revision);
        }
    }
    [Fact]
    public void One_thousand_settled_folded_frames_do_not_retain_unbounded_memory_or_edits()
    {
        using var r=new Rig(100,"nested");r.Ui();r.Host!.Frames(r.Draw,20);
        var before=GC.GetTotalMemory(true);for(int i=0;i<1000;i++)r.Host.Frame(r.Draw);
        var after=GC.GetTotalMemory(true);output.WriteLine($"1000 frames retained delta={after-before}");
        Assert.True(after-before<32*1024*1024,$"retained {after-before} bytes after 1000 unchanged frames");
        Assert.Equal(0,r.File.Session!.Revision);Assert.Equal(0,r.File.Session.History.UndoCount);Assert.False(r.File.Dirty);
    }

    /// <summary>Scratch runner entry: Release, warmup then 30 actual samples, same fixture and operation as guards.</summary>
    internal static void Profile(string[] args)
    {
        int n=int.Parse(args[0]);string shape=args[1],op=args[2];int count=args.Length>3?int.Parse(args[3]):30;
        using var r=new Rig(n,shape);
        Console.WriteLine(JsonSerializer.Serialize(new{phase="ready",n,shape,op,bytes=r.Bytes,cores=Environment.ProcessorCount,build=
#if DEBUG
        "Debug"
#else
        "Release"
#endif
        }));
        try{
            if(op is "folded" or "expanded" or "end" or "typed" or "chrome"){r.Ui(op!="chrome");if(op is "expanded" or "end")r.Expand();if(op=="end")r.ScrollEnd();}
            for(int i=-5;i<count;i++){
                r.Prepare(op);var alloc=GC.GetTotalAllocatedBytes(true);var sw=Stopwatch.StartNew();r.Act(op);sw.Stop();alloc=GC.GetTotalAllocatedBytes(true)-alloc;
                r.Verify(op);r.Cleanup(op);
                Console.WriteLine(JsonSerializer.Serialize(new{phase=i<0?"warmup":"sample",n,shape,op,i,ms=sw.Elapsed.TotalMilliseconds,alloc}));
            }
        }catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{phase="failure",n,shape,op,error=e.ToString()}));throw;}
    }

    internal sealed class CountCheck : ICheck
    {
        public CheckDefinition Definition {get;}=new("rows.count","Count rows","Counts generated rows",TimeSpan.FromMinutes(5));
        public Task<CheckReport> RunAsync(CheckSnapshot s,IProgress<CheckProgress> p,CancellationToken c){
            using var d=JsonDocument.Parse(s.Text("data/rows.json")!);var n=d.RootElement.GetProperty("catalog").GetProperty("entries").GetArrayLength();
            c.ThrowIfCancellationRequested();return Task.FromResult(new CheckReport(true,[],n.ToString()));
        }
    }
    internal sealed class Rig : IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"large-file-budget-"+Guid.NewGuid().ToString("N"));
        internal readonly int N;internal readonly long Bytes;internal readonly DataProject Project;internal DataFile File=>Project.Find("rows.json")!;
        internal readonly EditorModel<DataDocument> Editor;internal readonly Inspector<DataDocument> Inspector;internal readonly AgentSession Agent;
        internal HeadlessHost? Host;internal EditorWindow<DataDocument>? Window;internal void Draw()=>Window!.Draw();
        private object? answer;private bool success;private readonly Checks checks=new Checks().Add(new CountCheck());
        private string Folder=>Path.Combine(root,"data");
        internal Rig(int n,string shape)
        {
            N=n;Directory.CreateDirectory(Folder);Directory.CreateDirectory(Path.Combine(root,"schemas"));
            var props=new JsonObject{["id"]=new JsonObject{["type"]="string"}};
            for(int k=0;k<32;k++)props["v"+k]=new JsonObject{["type"]="integer"};
            if(shape=="nested")props["detail"]=JsonNode.Parse("""{"type":"object","properties":{"name":{"type":"string"},"children":{"type":"array","items":{"type":"object","properties":{"x":{"type":"integer"}}}}}}""");
            var schema=JsonNode.Parse("""{"type":"object","properties":{"catalog":{"type":"object","properties":{"title":{"type":"string"},"entries":{"type":"array","items":{"type":"object"}}}}}}""")!;
            schema["properties"]!["catalog"]!["properties"]!["entries"]!["items"]!["properties"]=props;
            System.IO.File.WriteAllText(Path.Combine(root,"schemas","rows.schema.json"),schema.ToJsonString());
            using(var stream=System.IO.File.Create(Path.Combine(Folder,"rows.json")))
            using(var writer=new Utf8JsonWriter(stream)){
                writer.WriteStartObject();writer.WriteStartObject("catalog");writer.WriteString("title","seed");writer.WriteStartArray("entries");
                for(int i=0;i<n;i++){writer.WriteStartObject();writer.WriteString("id","row-"+i);for(int k=0;k<32;k++)writer.WriteNumber("v"+k,i+k);
                    if(shape=="nested"){writer.WritePropertyName("detail");writer.WriteRawValue("""{"name":"leaf","children":[{"x":1},{"x":2}]}""");}writer.WriteEndObject();}
                writer.WriteEndArray();writer.WriteEndObject();writer.WriteEndObject();
            }
            Bytes=new FileInfo(Path.Combine(Folder,"rows.json")).Length;
            Project=DataProject.Open(Folder,Path.Combine(root,"schemas"));Assert.True(File.Open());
            Editor=new(File.Session!,DataEditing.Outline);Assert.True(Editor.Select("/catalog"));
            Inspector=new Inspector<DataDocument>(Editor).Add(DataEditing.ObjectType(Project.Schemas,"rows.schema.json"));
            Agent=new(Project);
        }
        internal void Ui(bool withInspector=true){Host=new HeadlessHost(){Drawing=false};Window=new(Editor,withInspector?Inspector:null);Host.Frames(Draw);}
        internal void Expand(){
            var p=Window!.InspectorPlace("entries")!.Value;
            Host!.Click(Draw,p.X-12,p.Y+8);
            Assert.NotNull(Window.InspectorPlace("\u001f/entries/0"));
        }
        internal void ScrollEnd(){
            // Prove the real Later control, then use Add/undo as bounded setup for a 2,000-page list.
            // No page dictionary or scroll state is injected; the original document is restored before timing.
            var original=File.Session!.Current.Root.GetRawText();
            var later=Window!.InspectorPlace("entries",InspectorPart.Later)!.Value;
            Host!.Click(Draw,later.X+8,later.Y+6);
            Assert.NotNull(Window.InspectorPlace("\u001f/entries/50"));
            if(N>100){
                WheelToBottom();
                var add=Window.InspectorPlace("entries",InspectorPart.Add)!.Value;
                Assert.InRange(add.Y,0,Host.Height-12);
                Host.Click(Draw,add.X+8,add.Y+6);
                Assert.Equal(N+1,File.Session.Current.At("/catalog/entries")!.Value.GetArrayLength());
                Host.Press(Draw,ImGuiKey.Z,ctrl:true);
                Assert.Equal(N,File.Session.Current.At("/catalog/entries")!.Value.GetArrayLength());
            }
            WheelToBottom();
            Assert.Equal(original,File.Session.Current.Root.GetRawText());
            Assert.NotNull(Window.InspectorPlace("\u001f/entries/"+(N-1)+"/v31"));
            Console.WriteLine(JsonSerializer.Serialize(new{phase="last-page-proof",n=N,laterUsed=true,addUndo=N>100}));
        }
        private void WheelToBottom(){
            // The inspector's screen position is unchanged by its content scrolling.
            Host!.MoveMouse(Draw,600,350);
            for(int i=0;i<3;i++){ImGui.GetIO().AddMouseWheelEvent(0,-1000000);Host.Frame(Draw);}
            var pane=ImGui.GetCurrentContext().HoveredWindow;
            Assert.False(pane.IsNull());Assert.True(pane.ScrollMax.Y>0);
            Assert.InRange(pane.Scroll.Y,pane.ScrollMax.Y-1,pane.ScrollMax.Y+1);
            Console.WriteLine(JsonSerializer.Serialize(new{phase="scroll-proof",scroll=pane.Scroll.Y,maximum=pane.ScrollMax.Y}));
        }
        internal void Prepare(string op){
            if(op=="open")File.Close();
            if(op is "save" or "undo")Assert.True(File.Session!.Execute(new SetValue("/catalog/title",JsonSerializer.SerializeToElement("changed"))));
            if(op=="typed"){
                var p=Window!.InspectorPlace("title")!.Value;Host!.Click(Draw,p.X+10,p.Y+8);Assert.True(ImGui.GetIO().WantTextInput);
                Host.Press(Draw,ImGuiKey.A,ctrl:true);
            }
        }
        internal void Act(string op){
            var s=File.Session;
            switch(op){
                case "open":success=File.Open();break;
                case "folded":case "expanded":case "end":case "chrome":Host!.Frame(Draw);break;
                case "typed":Host!.Type(Draw,"changed");Host.Press(Draw,ImGuiKey.Enter);break;
                case "undo":success=s!.Undo();break;
                case "save":success=File.Save();break;
                case "agent-list":answer=Agent.Find("rows.json","/catalog/entries");break;
                case "search":answer=Agent.Find("rows.json","/catalog/entries","row-"+(N-1));break;
                case "agent-set":answer=Agent.Set("rows.json","/catalog/title","\"changed\"");break;
                case "snapshot":answer=CheckSnapshot.Capture(Project);break;
                case "check":var run=checks.Start("rows.count",Project,out var why);Assert.NotNull(run);run.Ended.GetAwaiter().GetResult();Assert.True(run.Clean(Project),why);answer=run;break;
                case "rows":answer=Inspector.Nested(Inspector.Rows().Single(x=>x.Id=="entries"));break;
                case "write":answer=new DataFormat().Write(s!.Current);break;
                case "validate":answer=s!.Validator.Validate(s.Current);break;
                default:throw new ArgumentException(op);
            }
        }
        internal void Verify(string op){
            if(op=="open")Assert.True(success);
            if(op=="rows")Assert.Equal(N,((IReadOnlyList<InspectorRow>)answer!).Count);
            if(op=="validate")Assert.Empty((IReadOnlyList<Issue>)answer!);
            if(op=="write"){
                var serialized=Assert.IsType<string>(answer);var roundTrip=new DataFormat().Read(serialized);
                Assert.True(JsonElement.DeepEquals(File.Session!.Current.Root,roundTrip.Root));
            }
            if(op=="snapshot"){
                var snapshot=Assert.IsType<CheckSnapshot>(answer);
                using var captured=JsonDocument.Parse(snapshot.Text("data/rows.json")!);
                Assert.True(JsonElement.DeepEquals(File.Session!.Current.Root,captured.RootElement));
                Assert.NotNull(snapshot.Text("schemas/rows.schema.json"));
            }
            if(op=="agent-list")Assert.Equal(N,((JsonObject)answer!)["matches"]!.GetValue<int>());
            if(op=="search")Assert.Equal(1,((JsonObject)answer!)["matches"]!.GetValue<int>());
            if(op=="agent-set")Assert.True(((JsonObject)answer!)["applied"]!.GetValue<bool>());
            if(op=="typed"){Assert.Equal("changed",File.Session!.Current.At("/catalog/title")!.Value.GetString());Assert.True(File.Session.History.UndoCount>0);}
            if(op=="undo"){Assert.True(success);Assert.Equal("seed",File.Session!.Current.At("/catalog/title")!.Value.GetString());}
            if(op=="save"){Assert.True(success);Assert.Contains("changed",System.IO.File.ReadAllText(File.Path));}
        }
        internal void Cleanup(string op){
            if(op is "agent-set" or "typed")Assert.True(File.Session!.Undo());
            if(op=="save"){Assert.True(File.Session!.Execute(new SetValue("/catalog/title",JsonSerializer.SerializeToElement("seed"))));Assert.True(File.Save());}
        }
        public void Dispose(){
            Host?.Dispose();var resolved=Path.GetFullPath(root);var parent=Path.GetFullPath(Path.GetTempPath());
            if(!resolved.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("large-file-budget-",StringComparison.Ordinal))throw new Exception("cleanup target");
            Directory.Delete(resolved,true);
        }
    }
}
