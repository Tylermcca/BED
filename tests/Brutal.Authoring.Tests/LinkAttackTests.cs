using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;
using Pointer = Brutal.Authoring.Data.Pointer;

namespace Brutal.Authoring.Tests;

// INVENTED neutral link graphs and mutation schedules. BORROWED: existing map, table Follow/GoTo,
// check snapshot and reverse-query contracts. No game data, OS window or OS input.
[Collection(ImGuiCollection.Name)]
public sealed class LinkAttackTests(ITestOutputHelper output)
{
    private const string SchemaName="loose.schema.json";
    private const string Loose="""{"type":"object","additionalProperties":true}""";
    public static readonly string[] SmallNames=["missing_target_file","missing_target_list","missing_target_key","missing_id","duplicate_target_id","number_ref","empty_ref",
        "slash_id","tilde_id","dot_id","quote_id","slash_key","tilde_key","dot_key","quote_key","nested_arrays","duplicate_map_rule","self_reference","empty_sentinel","bool_ref"];
    public static IEnumerable<object[]> SmallCases=>SmallNames.Select(n=>new object[]{n});
    public static IEnumerable<object[]> Mutations=>new[]{"rename_id","rename_file","delete_file","add_file","reopen_source","remove_source_ref"}.Select(n=>new object[]{n});
    private static JsonElement J<T>(T v)=>JsonSerializer.SerializeToElement(v);
    private sealed class Note(string name)
    {public string Name{get;}=name;public string Phase{get;set;}="starting";public Dictionary<string,object?> Values{get;}=[];public List<string> Faults{get;}=[];}
    private void Write(Note n)
    {
        var json=JsonSerializer.Serialize(n);output.WriteLine(json);
        if(Environment.GetEnvironmentVariable("A205_OBSERVATIONS") is {Length:>0} dir){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,n.Name+".json"),json);}
    }
    private static JsonObject Item(string id,string label)=>new(){["id"]=id,["label"]=label};
    private static JsonObject Root(JsonNode items)=>new(){["$schema"]=SchemaName,["items"]=items};
    private static JsonObject Rule(string file,string at,string target="targets.json",string list="/items",string key="id")=>new()
    {["file"]=file,["at"]=at,["target"]=new JsonObject{["file"]=target,["list"]=list,["key"]=key},["evidence"]="Self-authored hostile link proof."};
    private static string MapText(IEnumerable<JsonObject> rules)=>new JsonObject{["game"]="Neutral link proof",["labels"]=new JsonArray(
        new JsonObject{["file"]="sources.json",["list"]="/items",["field"]="label"},new JsonObject{["file"]="targets.json",["list"]="/items",["field"]="label"}),
        ["references"]=new JsonArray(rules.Select(r=>(JsonNode)r).ToArray())}.ToJsonString();
    private sealed class Fixture:IDisposable
    {
        internal readonly string RootFolder=Path.Combine(Path.GetTempPath(),"link-attack-"+Guid.NewGuid().ToString("N"));
        internal string Data=>Path.Combine(RootFolder,"data");internal string Schemas=>Path.Combine(RootFolder,"schemas");internal string MapFile=>Path.Combine(RootFolder,"reference_map.json");
        internal readonly DataProject Project;internal readonly ReferenceMap Map;internal readonly DataWorkspace Workspace;
        internal string SourceName="sources.json",SourceOwner="/items/0",Field="ref",TargetName="targets.json",TargetPointer="/items/0";
        internal bool ExpectedBad,ExpectedFollow=true;internal int ExpectedReverse=1;
        internal string SourceAt=>SourceOwner+Pointer.Join([Field]);
        internal Fixture(string kind="normal",int large=0)
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);File.WriteAllText(Path.Combine(Schemas,SchemaName),Loose);
            var target=Root(new JsonArray(Item("a","Target A"),Item("b","Target B")));
            var sourceItem=Item("s0","Source zero");sourceItem["ref"]="a";var source=Root(new JsonArray(sourceItem));
            var rule=Rule("sources.json","/items/*/ref");var rules=new List<JsonObject>{rule};
            string? odd=kind.StartsWith("slash_",StringComparison.Ordinal)?"a/b":kind.StartsWith("tilde_",StringComparison.Ordinal)?"a~b":kind.StartsWith("dot_",StringComparison.Ordinal)?"a.b":kind.StartsWith("quote_",StringComparison.Ordinal)?"a\"b":null;
            if(odd is not null)
            {
                sourceItem["ref"]=odd;
                if(kind.EndsWith("_key",StringComparison.Ordinal))
                {target=Root(new JsonObject{[odd]=new JsonObject{["label"]="Odd target"},["other"]=new JsonObject{["label"]="Other"}});rule["target"]!["key"]="$key";TargetPointer="/items"+Pointer.Join([odd]);}
                else target["items"]![0]!["id"]=odd;
            }
            switch(kind)
            {
                case "missing_target_file":case "add_file":ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                case "missing_target_list":rule["target"]!["list"]="/absent";ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                case "missing_target_key":rule["target"]!["key"]="absent";ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                case "missing_id":sourceItem["ref"]="gone";ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                case "duplicate_target_id":target["items"]!.AsArray().Add(Item("a","Second A"));ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                // Owner clarification, 2026-11-05: ids compare as text; schema decides numeric/string types.
                case "number_ref":sourceItem["ref"]=17;target["items"]![0]!["id"]="17";break;
                case "empty_ref":sourceItem["ref"]="";ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                case "bool_ref":sourceItem["ref"]=true;ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                case "empty_sentinel":sourceItem["ref"]="";rule["sentinels"]=new JsonArray("");ExpectedFollow=false;ExpectedReverse=0;break;
                case "duplicate_map_rule":rules.Add(Rule("sources.json","/items/*/ref","decoy.json"));ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=0;break;
                case "nested_arrays":source=new JsonObject{["$schema"]=SchemaName,["groups"]=new JsonArray(new JsonArray(sourceItem.DeepClone()))};rule["at"]="/groups/*/*/ref";SourceOwner="/groups/0/0";break;
                case "self_reference":SourceName="targets.json";Field="id";rules=[Rule("targets.json","/items/*/id")];break;
            }
            WriteFile("sources.json",source.ToJsonString());
            if(kind is not ("missing_target_file" or "add_file"))WriteFile("targets.json",target.ToJsonString());
            WriteFile("decoy.json",Root(new JsonArray(Item("a","Decoy A"))).ToJsonString());
            if(large>0)
            {
                LargeFile("targets.json",large,false);LargeFile("sources.json",large,true);rules=Enumerable.Range(0,5).Select(j=>Rule("sources.json","/items/*/r"+j)).ToList();TargetPointer="/items/"+(large-1);SourceOwner="/items/"+(large-1);Field="r4";ExpectedBad=true;ExpectedFollow=false;ExpectedReverse=4;
            }
            File.WriteAllText(MapFile,MapText(rules));Map=ReferenceMap.Read(File.ReadAllText(MapFile));Project=DataProject.Open(Data,Schemas);
            foreach(var file in Project.Files)Assert.True(file.Open(),file.Name+": "+string.Join("; ",file.Problems.Select(p=>p.Message)));
            Workspace=new DataWorkspace(Project,map:Map);QueryTarget();
        }
        private void LargeFile(string name,int count,bool source)
        {
            using var stream=File.Create(Path.Combine(Data,name));using var w=new Utf8JsonWriter(stream);w.WriteStartObject();w.WriteString("$schema",SchemaName);w.WriteStartArray("items");
            for(var i=0;i<count;i++)
            {w.WriteStartObject();w.WriteString("id",(source?"s":"t")+i);w.WriteString("label",(source?"Source ":"Target ")+i);if(source)for(var j=0;j<5;j++)w.WriteString("r"+j,i==count-1&&j==4?"missing":"t"+((i*5+j)%count));w.WriteEndObject();}
            w.WriteEndArray();w.WriteEndObject();
        }
        internal void WriteFile(string name,string text)=>File.WriteAllText(Path.Combine(Data,name),text,new UTF8Encoding(false));
        internal DataFile Loaded(string name){var f=Project.Find(name)!;Assert.NotNull(f);Assert.True(f.Open());return f;}
        internal ReferenceTarget QueryTarget()
        {var name=Project.Find(TargetName) is null?"decoy.json":TargetName;var at=name==TargetName?TargetPointer:"/items/0";Assert.True(Workspace.GoTo(new(name,at)),Workspace.Said);return new(name,Workspace.Window!.Editor.Primary??at);}
        internal Dictionary<string,byte[]> Disk()=>Directory.EnumerateFiles(Data,"*.json").ToDictionary(p=>Path.GetFileName(p)!,File.ReadAllBytes);
        internal void SameDisk(Dictionary<string,byte[]> before)
        {var after=Disk();Assert.Equal(before.Keys.Order(),after.Keys.Order());foreach(var (name,bytes) in before)Assert.Equal(bytes,after[name]);}
        public void Dispose()
        {var boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;Assert.StartsWith(boundary,Path.GetFullPath(RootFolder),StringComparison.OrdinalIgnoreCase);Directory.Delete(RootFolder,recursive:true);}
    }
    private static string Screen(DataWorkspace w)
    {
        using var host=new HeadlessHost(1600,1200){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(w.Draw,4);
        host.Frame(()=>{ImGui.LogToClipboard();w.Draw();ImGui.LogFinish();});return ImGui.GetClipboardText().ToString();
    }
    private static async Task<CheckRun> Check(Fixture f)
    {
        Assert.True(f.Workspace.RunCheck("links.all"),f.Workspace.Said);var run=f.Workspace.Checks.Last("links.all")!;
        try{await run.Ended.WaitAsync(TimeSpan.FromSeconds(130));await run.Worker.WaitAsync(TimeSpan.FromSeconds(10));}
        catch{run.Cancel();await run.Worker.WaitAsync(TimeSpan.FromSeconds(10));throw;}
        Assert.Equal(CheckState.Completed,run.State);Assert.NotNull(run.Report);return run;
    }
    private static object Findings(CheckReport r)=>r.Findings.Select(v=>new{severity=v.Problem.Severity.ToString(),v.Problem.Message,v.Problem.ObjectId,v.Problem.Path,v.Place,v.Next}).ToArray();
    private static void Places(Fixture f,CheckReport report,Note n)
    {
        var places=new List<object>();foreach(var finding in report.Findings)
        {
            if(finding.Place is not {At:{ } at} p){n.Faults.Add("Finding has no field place.");continue;}
            var doc=f.Loaded(p.File).Session!.Current;if(doc.At(at) is not { } value){n.Faults.Add("Finding place does not resolve: "+p.File+at);continue;}
            if(p.File!=f.SourceName||at!=f.SourceAt)n.Faults.Add("Finding points at a different source item/field.");
            Assert.True(f.Workspace.Reveal(finding),f.Workspace.Said);Assert.Equal(p.File,f.Workspace.Active!.Name);
            var selected=f.Workspace.Window!.Editor.Primary;var owner=Pointer.Join(Pointer.Split(at).SkipLast(1));
            if(selected is null||!(owner==selected||owner.StartsWith(selected+"/",StringComparison.Ordinal)))n.Faults.Add("Finding reveal selected an unrelated item.");
            places.Add(new{p.File,p.Item,p.At,value=value.GetRawText(),selected,said=f.Workspace.Said});
        }
        n.Values["revealedPlaces"]=places;
    }
    private static void Reverse(Fixture f,ReferenceTarget target,int expected,Note n,string prefix="")
    {
        var from=f.Workspace.ReferencedBy();n.Values[prefix+"reverse"]=from.Select(r=>new{r.At,r.Label}).ToArray();n.Values[prefix+"reverseCount"]=from.Count;
        if(expected>=0&&from.Count!=expected)n.Faults.Add("Referenced by count "+from.Count+" differs from expected "+expected+" for "+target.File+target.Pointer);
        foreach(var r in from)
        {
            var file=f.Loaded(r.At.File);var at=file.Session!.Current.At(r.At.Pointer);if(at is null){n.Faults.Add("Reverse place no longer exists.");continue;}
            var fresh=f.Map.Resolve(f.Project,r.At.File,r.At.Pointer,at.Value,ReferenceIntent.Value);
            if(fresh.Outcome!=ReferenceOutcome.Found||fresh.Target is not { } actual||actual.File!=target.File||!(actual.Pointer==target.Pointer||actual.Pointer.StartsWith(target.Pointer+"/",StringComparison.Ordinal)))n.Faults.Add("Reverse entry does not currently resolve to the selected target.");
        }
    }
    private static void Follow(Fixture f,Note n)
    {
        Assert.True(f.Workspace.Show(f.SourceName),f.Workspace.Said);var table=f.Workspace.TableOf(f.SourceName)!;var value=f.Loaded(f.SourceName).Session!.Current.At(f.SourceAt)!.Value;
        var resolved=f.Map.Resolve(f.Project,f.SourceName,f.SourceAt,value);var intent=f.Map.Resolve(f.Project,f.SourceName,f.SourceAt,value,ReferenceIntent.Value);var target=table.LinkOf(f.SourceOwner,f.Field);
        n.Values["resolved"]=new{outcome=resolved.Outcome.ToString(),resolved.Target,resolved.Note};n.Values["explicitIntent"]=new{outcome=intent.Outcome.ToString(),intent.Target,intent.Note};n.Values["tableTarget"]=target;
        var ok=table.Follow(f.SourceOwner,f.Field);n.Values["followed"]=ok;n.Values["tableSaid"]=table.Said;n.Values["afterFollowFile"]=f.Workspace.Active?.Name;n.Values["afterFollowItem"]=f.Workspace.Window?.Editor.Primary;n.Values["workspaceSaid"]=f.Workspace.Said;
        if(ok!=f.ExpectedFollow)n.Faults.Add("Table Follow did not respect the expected unique mapped target.");
        if(ok)
        {
            var selected=f.Workspace.Window?.Editor.Primary;
            if(target!=new ReferenceTarget(f.TargetName,f.TargetPointer)||f.Workspace.Active?.Name!=f.TargetName||selected is null||!(selected==f.TargetPointer||f.TargetPointer.StartsWith(selected+"/",StringComparison.Ordinal)))n.Faults.Add("Table Follow navigated to a different target item.");
            if(selected!=f.TargetPointer&&!f.Workspace.Said.Contains("link points inside",StringComparison.Ordinal))n.Faults.Add("Nearest-owner navigation omitted the exact nested place.");
        }
        else if(f.Workspace.Active?.Name!=f.SourceName)n.Faults.Add("Refused Follow moved to another file.");
    }
    [Theory,MemberData(nameof(SmallCases))]
    public async Task Odd_or_broken_links_are_visible_located_and_never_followed_to_the_wrong_item(string kind)
    {
        var n=new Note(kind);using var f=new Fixture(kind);var disk=f.Disk();n.Values["mapEntries"]=f.Map.Count;n.Phase="check";Write(n);
        var run=await Check(f);n.Values["complete"]=run.Report!.Complete;n.Values["summary"]=run.Report!.Summary;n.Values["findings"]=Findings(run.Report);
        if(f.ExpectedBad&&run.Report.Findings.Count==0)n.Faults.Add("links.all hides a mapped broken or ambiguous link.");
        if(!f.ExpectedBad&&run.Report.Findings.Count!=0)n.Faults.Add("Valid or explicitly sentinel link was reported broken.");
        Places(f,run.Report,n);var desired=new ReferenceTarget(f.TargetName,f.TargetPointer);var query=f.QueryTarget();var ownerOnly=query!=desired&&query.File==desired.File&&desired.Pointer.StartsWith(query.Pointer+"/",StringComparison.Ordinal);
        if(ownerOnly){var exact=f.Map.ReferencesTo(f.Project,desired);n.Values["exactNestedReverse"]=exact;Assert.Single(exact);}
        n.Values["panelQuery"]=query;n.Values["desiredTarget"]=desired;Reverse(f,query,ownerOnly?-1:f.ExpectedReverse,n);var screen=Screen(f.Workspace);n.Values["referencesScreen"]=screen;
        if(ownerOnly&&f.Workspace.ReferencedBy().Count==0&&screen.Contains("removing it breaks no link the map knows",StringComparison.Ordinal))n.Faults.Add("The selected owner contains a known referenced target, but its panel claims removing it breaks no link.");
        Follow(f,n);n.Values["followScreen"]=Screen(f.Workspace);f.SameDisk(disk);n.Values["diskUnchanged"]=true;n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));
    }
    [Theory]
    [InlineData("map_not_json","{")] [InlineData("map_wrong_shape","[]")]
    public void A_bad_map_is_refused_before_any_project_is_published(string kind,string text)
    {
        var n=new Note(kind);using var f=new Fixture();var disk=f.Disk();File.WriteAllText(f.MapFile,text);Exception? caught=null;
        try{ReferenceMap.Read(text);}catch(Exception e)when(e is JsonException or InvalidDocumentException){caught=e;}
        Assert.NotNull(caught);n.Values["mapError"]=caught!.ToString();var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(f.RootFolder,"prefs","recent.json")));
        Assert.False(launcher.Open(new ProjectPaths(f.Data,f.Schemas,f.MapFile)));Assert.Null(launcher.Workspace);Assert.Null(launcher.Current);Assert.Empty(launcher.Recent.Items);n.Values["launcherSaid"]=launcher.Said;
        using(var host=new HeadlessHost(1600,1200){Drawing=false}){EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(launcher.Draw,3);host.Frame(()=>{ImGui.LogToClipboard();launcher.Draw();ImGui.LogFinish();});n.Values["screen"]=ImGui.GetClipboardText().ToString();}
        f.SameDisk(disk);n.Values["diskUnchanged"]=true;n.Phase="complete";Write(n);
    }
    [Theory,MemberData(nameof(Mutations))]
    public async Task Changes_make_the_check_stale_and_reverse_links_follow_the_current_documents(string kind)
    {
        var n=new Note(kind);using var f=new Fixture(kind);var beforeDisk=f.Disk();var old=await Check(f);var target=f.QueryTarget();Reverse(f,target,kind=="add_file"?0:1,n,"before");f.Workspace.ReferencedBy();
        n.Values["beforeFindings"]=Findings(old.Report!);n.Phase="mutation";Write(n);
        switch(kind)
        {
            case "rename_id":Assert.True(f.Loaded("targets.json").Session!.Execute(new SetValue("/items/0/id",J("renamed"))));f.ExpectedBad=true;f.ExpectedFollow=false;f.ExpectedReverse=0;break;
            case "rename_file":Assert.True(f.Workspace.RenameFile("targets.json","moved.json"),f.Workspace.Said);f.TargetName="moved.json";f.ExpectedBad=true;f.ExpectedFollow=false;f.ExpectedReverse=0;target=f.QueryTarget();break;
            case "delete_file":Assert.True(f.Workspace.DeleteFile("targets.json"),f.Workspace.Said);f.ExpectedBad=true;f.ExpectedFollow=false;f.ExpectedReverse=0;target=f.QueryTarget();break;
            case "add_file":f.WriteFile("targets.json",Root(new JsonArray(Item("a","Added A"),Item("b","Added B"))).ToJsonString());f.Workspace.Refresh();f.ExpectedBad=false;f.ExpectedFollow=true;f.ExpectedReverse=1;target=f.QueryTarget();break;
            case "reopen_source":var source=f.Loaded("sources.json");var node=JsonNode.Parse(File.ReadAllText(source.Path))!;node["items"]![0]!["ref"]="b";var stamp=File.GetLastWriteTimeUtc(source.Path);source.Close();f.WriteFile("sources.json",node.ToJsonString());File.SetLastWriteTimeUtc(source.Path,stamp.AddSeconds(1));Assert.True(source.Open());f.ExpectedReverse=0;f.TargetPointer="/items/1";break;
            case "remove_source_ref":Assert.True(f.Loaded("sources.json").Session!.Execute(new SetValue("/items/0/ref",null)));f.ExpectedReverse=0;f.ExpectedFollow=false;break;
        }
        n.Values["mutationSaid"]=f.Workspace.Said;var stale=old.IsStale(f.Project);n.Values["oldResultStale"]=stale;if(!stale)n.Faults.Add("A changed input left the completed check current.");
        foreach(var finding in old.Report!.Findings){if(f.Workspace.Reveal(finding))n.Faults.Add("An old finding was followed after its check became stale.");}
        // Query and draw before any selection change can accidentally flush the cache under test.
        Reverse(f,target,f.ExpectedReverse,n,"after");n.Values["afterMutationScreen"]=Screen(f.Workspace);
        var run=await Check(f);n.Values["afterFindings"]=Findings(run.Report!);n.Values["afterSummary"]=run.Report!.Summary;
        if(f.ExpectedBad&&run.Report.Findings.Count==0)n.Faults.Add("Fresh check hides the newly broken link.");if(!f.ExpectedBad&&run.Report.Findings.Count!=0)n.Faults.Add("Fresh check reports a valid new state as broken.");
        Places(f,run.Report,n);
        if(kind!="remove_source_ref")Follow(f,n);
        if(kind is "rename_id" or "remove_source_ref")f.SameDisk(beforeDisk);
        else
        {
            foreach(var(name,bytes)in beforeDisk)
                if(!(kind=="reopen_source"&&name=="sources.json")&&!(kind is "rename_file" or "delete_file"&&name=="targets.json"))Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(f.Data,name)));
            if(kind=="rename_file"){Assert.False(File.Exists(Path.Combine(f.Data,"targets.json")));Assert.Equal(beforeDisk["targets.json"],File.ReadAllBytes(Path.Combine(f.Data,"moved.json")));}
            if(kind=="delete_file")Assert.False(File.Exists(Path.Combine(f.Data,"targets.json")));if(kind=="add_file")Assert.True(File.Exists(Path.Combine(f.Data,"targets.json")));
        }
        n.Values["unrelatedDiskUnchanged"]=true;n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));
    }
    private void Measure(Note n,string operation,Action action)
    {
        for(var i=0;i<20;i++)action();var ms=new double[150];var allocated=new long[150];
        for(var i=0;i<150;i++){var b=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();action();ms[i]=Stopwatch.GetElapsedTime(t).TotalMilliseconds;allocated[i]=GC.GetAllocatedBytesForCurrentThread()-b;}
        var tms=ms.Order().ToArray();var bs=allocated.Order().ToArray();n.Values[operation]=new{warmups=20,samples=150,medianMs=tms[75],p95Ms=tms[142],maxMs=tms[^1],medianBytes=bs[75],p95Bytes=bs[142],maxBytes=bs[^1],times=ms,allocations=allocated};Write(n);
    }
    [Fact]
    public async Task Large_link_set_checks_250000_references_and_measures_cold_query_and_steady_frames()
    {
        var n=new Note("large");using var f=new Fixture(large:50000);var disk=f.Disk();n.Values["items"]=50000;n.Values["referencesPerItem"]=5;n.Values["expectedLinks"]=250000;n.Values["dataBytes"]=disk.ToDictionary(k=>k.Key,v=>v.Value.Length);
        n.Phase="check";Write(n);var bytes=GC.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();var run=await Check(f);clock.Stop();n.Values["check"]=new{samples=1,elapsedMs=clock.Elapsed.TotalMilliseconds,processAllocatedBytes=GC.GetTotalAllocatedBytes(true)-bytes};
        n.Values["summary"]=run.Report!.Summary;n.Values["findings"]=Findings(run.Report);Assert.True(run.Report.Complete);Assert.Single(run.Report.Findings);Assert.Contains("250000",run.Report!.Summary);Places(f,run.Report,n);
        var query=f.QueryTarget();var b=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();var cold=f.Workspace.ReferencedBy();n.Values["coldReverse"]=new{samples=1,elapsedMs=Stopwatch.GetElapsedTime(t).TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-b,count=cold.Count};Assert.Equal(4,cold.Count);
        Reverse(f,query,4,n);Measure(n,"cachedReverse",()=>Assert.Equal(4,f.Workspace.ReferencedBy().Count));
        using(var host=new HeadlessHost(1600,1200){Drawing=false})
        {
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());foreach(var panel in f.Workspace.Window!.Editor.Panels)f.Workspace.Window.Editor.SetOpen(panel.Id,false);f.Workspace.Window.Editor.SetOpen("project.references",true);host.Frames(f.Workspace.Draw,5);
            Measure(n,"referencesFrame",()=>host.Frame(f.Workspace.Draw));host.Frame(()=>{ImGui.LogToClipboard();f.Workspace.Draw();ImGui.LogFinish();});n.Values["screen"]=ImGui.GetClipboardText().ToString();
        }
        f.SameDisk(disk);n.Values["diskUnchanged"]=true;n.Values["runtime"]=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;n.Values["os"]=System.Runtime.InteropServices.RuntimeInformation.OSDescription;n.Values["processors"]=Environment.ProcessorCount;n.Values["clock"]=Stopwatch.Frequency;n.Phase="complete";Write(n);
    }
}
