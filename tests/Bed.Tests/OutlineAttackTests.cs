using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

// INVENTED fixtures/scenarios; BORROWED A184 measurement protocol and public outline/session contracts.
[Collection(ImGuiCollection.Name)]
public sealed class OutlineAttackTests(ITestOutputHelper output)
{
    const string Schema="""{"type":"object","properties":{"stamp":{"type":"integer"},"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"amount":{"type":"integer"}},"required":["id","name","amount"]}}}}""";
    public sealed class LongTheoryAttribute:TheoryAttribute
    {public LongTheoryAttribute(){if(Environment.GetEnvironmentVariable("A211_LONG")!="1")Skip="Set A211_LONG=1 for 100000 items, 500 files, 1000-item batches and 20/150 frame profiling; no long-case or cost claim otherwise.";}}
    static JsonElement J(string text)=>JsonDocument.Parse(text).RootElement.Clone();
    static string Name(int i,string shape)=>shape switch
    {
        "empty"=>"", "duplicate"=>"Same", "longlabel"=>new string('L',10240)+i,
        "unicode"=>"\u202e\u05e9\u05dc\u05d5\u05dd\u202c e\u0301\u0323\u2067\u0645\u0631\u062d\u0628\u0627\u2069 "+i,
        _=>"Item "+i.ToString("D6",CultureInfo.InvariantCulture)
    };
    static string Text(int count,string shape="normal")=>JsonSerializer.Serialize(new{stamp=0,items=Enumerable.Range(0,count).Select(i=>new{id="id"+i,name=Name(i,shape),amount=i})});
    static string DeepText(int depth)
    {
        JsonNode? child=null;
        for(int i=depth-1;i>=0;i--)child=new JsonObject{["name"]="node"+i,["child"]=child};
        return new JsonObject{["root"]=child}.ToJsonString();
    }
    sealed class NestedOutline(int depth):IDocumentOutline<DataDocument>
    {
        public readonly string[] Ids=Enumerable.Range(0,depth).Select(i=>"/root"+string.Concat(Enumerable.Repeat("/child",i))).ToArray();
        public IEnumerable<string> ObjectIds(DataDocument d)=>Ids.Where(p=>d.At(p) is {ValueKind:JsonValueKind.Object});
        public string? Label(DataDocument d,string id)=>d.At(id) is {ValueKind:JsonValueKind.Object} item?item.GetProperty("name").GetString():null;
        public string? Parent(DataDocument d,string id)=>Array.IndexOf(Ids,id) is var i&&i>0?Ids[i-1]:null;
        public long? Version=>0;
    }
    sealed class Rig:IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"a211-"+Guid.NewGuid().ToString("N"));
        public string Data=>Path.Combine(Root,"data");public string FilePath=>Path.Combine(Data,"rows.json");
        public readonly HeadlessHost Host=new(1600,1200){Drawing=false};
        public readonly DataProject Project;public readonly DataWorkspace Workspace;public readonly EditorWindow<DataDocument> Window;
        readonly bool custom;
        public EditorModel<DataDocument> Editor=>Window.Editor;public DocumentSession<DataDocument> Session=>Editor.Session;
        public Action Draw=>custom?Window.DrawDocked:Workspace.Draw;
        public Rig(int count=64,string shape="normal",int files=1,bool deep=false)
        {
            custom=deep;
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            var text=deep?DeepText(20):Text(count,shape);
            File.WriteAllText(FilePath,text,new UTF8Encoding(false));
            for(int i=1;i<files;i++)File.WriteAllText(Path.Combine(Data,$"file{i:D3}.json"),Text(1),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Root,"schemas","rows.schema.json"),deep?"{\"type\":\"object\"}":Schema,new UTF8Encoding(false));
            Project=DataProject.Open(Data,Path.Combine(Root,"schemas"));Project.SchemaFor=_=>"rows.schema.json";
            Workspace=new(Project);Assert.True(Workspace.Show("rows.json"),Workspace.Said);
            if(deep)
            {
                var e=new EditorModel<DataDocument>(Workspace.Window!.Editor.Session,new NestedOutline(20));
                Window=new(e,new Inspector<DataDocument>(e).Add(DataEditing.ObjectType(Project.Schemas,"rows.schema.json"))){Searcher=(d,q)=>DataSearch.Find(d,q)};
            }
            else{Window=Workspace.Window!;foreach(var p in Editor.Panels)Editor.SetOpen(p.Id,p.Id=="project.files");}
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Host.Frames(Draw,5);
        }
        public string IdAt(int index)=>Session.Current.At($"/items/{index}/id")!.Value.GetString()!;
        public string Ui()
        {
            var p=Path.Combine(Root,"ui.txt");Host.Frame(()=>{ImGui.LogToFile(0,p);Draw();ImGui.LogFinish();});return File.ReadAllText(p);
        }
        public void Bottom(string window="Outliner###outliner")
        {
            var w=ImGui.Internal.FindWindowByName(window);Assert.False(w.IsNull());
            Host.MoveMouse(Draw,w.Pos.X+Math.Min(100,w.Size.X/2),w.Pos.Y+w.Size.Y/2);
            ImGui.GetIO().AddMouseWheelEvent(0,-100000);Host.Frames(Draw,20);
            Assert.True(w.ScrollMax.Y==0||w.Scroll.Y>=w.ScrollMax.Y-1,$"Scroll didn't reach bottom {w.Scroll.Y}/{w.ScrollMax.Y}");
        }
        public void ClickLast(string window="Outliner###outliner")
        {
            var w=ImGui.Internal.FindWindowByName(window);var c=w.InnerClipRect;
            Host.Click(Draw,c.Min.X+80,c.Max.Y-8);
        }
        public void Dispose()
        {
            Host.Dispose();var boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(Root).StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(Root).StartsWith("a211-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture path");
            Directory.Delete(Root,true);
        }
    }
    void Evidence(string name,object value)
    {
        var json=JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true});output.WriteLine(json);
        if(Environment.GetEnvironmentVariable("A211_EVIDENCE") is {Length:>0} dir){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,name+".json"),json);}
    }
    static void ActivateFind(Rig r)
    {
        var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
        for(int i=0;i<12;i++)
        {
            r.Host.Press(r.Draw,ImGuiKey.Tab);var w=ImGui.GetCurrentContext().NavWindowingTarget;
            if(!w.IsNull()&&(Marshal.PtrToStringUTF8((IntPtr)w.Name)??"").Contains("###inspector",StringComparison.Ordinal))break;
        }
        io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);r.Host.Frames(r.Draw,3);r.Host.Press(r.Draw,ImGuiKey.Enter);
        Assert.True(io.WantTextInput,"Native keyboard traversal did not activate the Find input");
    }
    [Theory][InlineData("normal")][InlineData("empty")][InlineData("duplicate")][InlineData("longlabel")][InlineData("unicode")]
    public void Labels_do_not_merge_outline_identity_or_select_a_different_row(string shape)
    {
        using var r=new Rig(80,shape);var before=r.Session.Current;
        var ids=r.Editor.Outline.ObjectIds(before).ToArray();Assert.Equal(82,ids.Length);Assert.Equal(82,ids.Distinct().Count());
        r.Bottom();r.ClickLast();Evidence("labels-"+shape,new{primary=r.Editor.Primary,id=r.Editor.Primary is {} p?r.Session.Current.At(p+"/id")?.GetString():null,count=ids.Length});
        Assert.Equal("/items/79",r.Editor.Primary);Assert.Equal("id79",r.IdAt(79));Assert.Same(before,r.Session.Current);Assert.Empty(r.Session.Journal);
    }
    [Fact]public void Selection_survives_field_edit_undo_redo_and_validated_reload()
    {
        using var r=new Rig();Assert.True(r.Editor.Select("/items/47"));var old=r.Session.Current;
        Assert.True(r.Session.Execute(new SetValue("/items/47/name",J("\"Changed\""))));r.Host.Frames(r.Draw);Assert.Equal("/items/47",r.Editor.Primary);Assert.Equal("id47",r.IdAt(47));
        Assert.True(r.Session.Undo());Assert.Same(old,r.Session.Current);Assert.Equal("/items/47",r.Editor.Primary);Assert.True(r.Session.Redo());Assert.Equal("/items/47",r.Editor.Primary);
        File.WriteAllText(r.FilePath,Text(64).Replace("Item 000047","Reloaded",StringComparison.Ordinal));
        Assert.True(r.Session.Revert());r.Host.Frames(r.Draw);Assert.Equal("/items/47",r.Editor.Primary);Assert.Equal("Reloaded",r.Editor.Outline.Label(r.Session.Current,"/items/47"));
        Evidence("lifecycle",new{r.Editor.Primary,id=r.IdAt(47),name=r.Session.Current.At("/items/47/name")!.Value.GetString(),r.Session.Dirty});
    }
    [Fact]public void Search_and_check_place_reveal_the_named_item_without_a_document_edit()
    {
        using var r=new Rig(300);var before=r.Session.Current;Assert.Equal(1,r.Window.Find("Item 000247"));var hit=Assert.Single(r.Window.Found);
        Assert.True(r.Window.Reveal(hit.Pointer),r.Window.Status);r.Host.Frames(r.Draw);Assert.Equal("/items/247",r.Editor.Primary);
        Assert.True(r.Workspace.Reveal(new CheckFinding(Issue.Warning("Look here"),new("rows.json","/items/283","/items/283/name"),"Inspect it")),r.Workspace.Said);
        r.Host.Frames(r.Draw);Assert.Equal("/items/283",r.Editor.Primary);Assert.Same(before,r.Session.Current);Assert.Empty(r.Session.Journal);
        Evidence("search-check",new{hit=hit.Pointer,r.Editor.Primary,id=r.IdAt(283)});
    }
    [Fact]public void Folder_refresh_shows_external_add_remove_and_keeps_the_selected_missing_draft()
    {
        using var r=new Rig();Assert.True(r.Editor.Select("/items/12"));var session=r.Session;
        Assert.True(session.Execute(new SetValue("/items/12/name",J("\"Unsaved survivor\""))));
        File.Move(r.FilePath,Path.Combine(r.Data,"renamed.json"));r.Workspace.Refresh();r.Host.Frames(r.Draw);
        Assert.Equal(DataFileState.Missing,r.Project.Find("rows.json")!.State);Assert.NotNull(r.Project.Find("renamed.json"));Assert.Same(session,r.Workspace.Window!.Editor.Session);Assert.Equal("/items/12",r.Editor.Primary);
        var ui=r.Ui();Assert.Contains("renamed.json",ui);Assert.Contains("gone from the folder",ui);
        File.WriteAllText(Path.Combine(r.Data,"added.json"),Text(2));r.Workspace.Refresh();r.Host.Frames(r.Draw);Assert.NotNull(r.Project.Find("added.json"));
        File.Delete(Path.Combine(r.Data,"added.json"));r.Workspace.Refresh();r.Host.Frames(r.Draw);Assert.Equal(DataFileState.Missing,r.Project.Find("added.json")!.State);
        Assert.True(r.Session.Dirty);Assert.Equal("Unsaved survivor",r.Session.Current.At("/items/12/name")!.Value.GetString());
        Evidence("external-files",new{r.Editor.Primary,oldState=r.Project.Find("rows.json")!.State.ToString(),newState=r.Project.Find("renamed.json")!.State.ToString(),files=r.Project.Files.Select(x=>new{x.Name,state=x.State.ToString()})});
    }
    [Fact]public void Generic_outline_reaches_twenty_nested_items_and_reveals_the_deepest_name()
    {
        using var r=new Rig(deep:true);var ids=r.Editor.Outline.ObjectIds(r.Session.Current).ToArray();Assert.Equal(20,ids.Length);
        Assert.True(r.Window.Reveal(ids[^1]+"/name"),r.Window.Status);r.Host.Frames(r.Draw);Assert.Equal(ids[^1],r.Editor.Primary);
        Assert.Equal("node19",r.Editor.Outline.Label(r.Session.Current,r.Editor.Primary!));Assert.True(r.Session.Execute(new SetValue(ids[^1]+"/name",J("\"Deep edit\""))));
        r.Host.Frames(r.Draw);Assert.Equal(ids[^1],r.Editor.Primary);Assert.True(r.Session.Undo());Assert.Equal(ids[^1],r.Editor.Primary);
        Evidence("nested20",new{count=ids.Length,depth=19,r.Editor.Primary});
    }
    [LongTheory][InlineData("many")][InlineData("nested")][InlineData("files")][InlineData("empty")][InlineData("duplicate")][InlineData("longlabel")][InlineData("unicode")]
    public void Scale_routes_preserve_identity_without_making_timing_claims(string shape)
    {
        using var r=new Rig(shape=="many"?100000:shape is "empty" or "duplicate" or "longlabel" or "unicode"?128:80,shape,files:shape=="files"?500:1,deep:shape=="nested");
        var before=r.Session.Current;
        var ids=r.Editor.Outline.ObjectIds(before).ToArray();Assert.Equal(ids.Length,ids.Distinct(StringComparer.Ordinal).Count());
        var selected=shape=="nested"?ids[^1]:shape=="many"?"/items/99999":shape=="files"?"/items/79":"/items/127";
        Assert.True(r.Editor.Select(selected));r.Host.Frames(r.Draw);Assert.Equal(selected,r.Editor.Primary);
        var query=shape=="nested"?"node19":"id"+selected.Split('/')[2];
        Assert.Equal(1,r.Window.Find(query));var hit=Assert.Single(r.Window.Found);Assert.True(r.Window.Reveal(hit.Pointer),r.Window.Status);
        r.Host.Frames(r.Draw);Assert.Equal(selected,r.Editor.Primary);
        if(shape=="nested")Assert.Equal("node19",r.Editor.Outline.Label(r.Session.Current,selected));
        else
        {
            Assert.Equal(query,r.Session.Current.At(selected+"/id")!.Value.GetString());
            Assert.True(r.Workspace.Reveal(new CheckFinding(Issue.Warning("Scale finding"),new("rows.json",selected,selected+"/name"),"Inspect this invented item")),r.Workspace.Said);
            r.Host.Frames(r.Draw);Assert.Equal(selected,r.Editor.Primary);
        }
        if(shape=="many")
        {
            Assert.Equal(100002,ids.Length);Assert.True(r.Editor.Select("/items/0"));r.Bottom();r.ClickLast();
            Assert.Equal("/items/99999",r.Editor.Primary);Assert.Equal("id99999",r.IdAt(99999));
        }
        if(shape=="files")
        {
            Assert.Equal(500,r.Project.Files.Count);Assert.True(r.Workspace.Show("file001.json"));r.Host.Frames(r.Draw,5);
            Assert.Equal("file001.json",r.Workspace.Active!.Name);r.Bottom("Files###panel.project.files");r.ClickLast("Files###panel.project.files");
            Assert.Equal("rows.json",r.Workspace.Active!.Name);Assert.Same(r.Window,r.Workspace.Window);Assert.Equal(selected,r.Editor.Primary);
            File.WriteAllText(Path.Combine(r.Data,"external.json"),Text(1));r.Workspace.Refresh();r.Host.Frames(r.Draw);Assert.Equal(501,r.Project.Files.Count);
            File.Delete(Path.Combine(r.Data,"external.json"));r.Workspace.Refresh();r.Host.Frames(r.Draw);Assert.Equal(DataFileState.Missing,r.Project.Find("external.json")!.State);
            Assert.Equal(selected,r.Editor.Primary);
        }
        ActivateFind(r);r.Host.Type(r.Draw,"q");r.Host.Press(r.Draw,ImGuiKey.Enter);
        Assert.Contains("Nothing holds 'q'",r.Window.Status,StringComparison.Ordinal);Assert.Equal(selected,r.Editor.Primary);
        Assert.Same(before,r.Session.Current);Assert.Empty(r.Session.Journal);
        Evidence("scale-functional-"+shape,new{shape,outlineCount=ids.Length,fileCount=r.Project.Files.Count,hit=hit.Pointer,selection=r.Editor.Primary,identity=shape=="nested"?"node19":query,nativeSearch=r.Window.Status,timingClaim=false});
    }
    [LongTheory][InlineData("many")][InlineData("nested")][InlineData("files")][InlineData("empty")][InlineData("duplicate")][InlineData("longlabel")][InlineData("unicode")]
    public void Scale_actions_and_frame_costs(string shape)
    {
        using var r=new Rig(shape=="many"?100000:shape is "empty" or "duplicate" or "longlabel" or "unicode"?128:80,shape,files:shape=="files"?500:1,deep:shape=="nested");
        var costs=new Dictionary<string,object>();
        object Measure(Action act){var b=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();act();return new{ms=Stopwatch.GetElapsedTime(t).TotalMilliseconds,bytes=GC.GetAllocatedBytesForCurrentThread()-b};}
        object Idle()
        {
            r.Host.Frames(r.Draw,20);var samples=new List<(double Ms,long Bytes)>();
            for(int i=0;i<150;i++){var b=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();r.Host.Frame(r.Draw);samples.Add((Stopwatch.GetElapsedTime(t).TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-b));}
            var time=samples.Select(x=>x.Ms).Order().ToArray();var alloc=samples.Select(x=>x.Bytes).Order().ToArray();return new{n=150,medianMs=time[75],p95Ms=time[142],maxMs=time[^1],medianBytes=alloc[75],p95Bytes=alloc[142],samples=samples.Select(s=>new{ms=s.Ms,bytes=s.Bytes}).ToArray()};
        }
        costs["idle"]=Idle();Evidence("cost-"+shape,new{shape,phase="idle",costs});
        var scrollWindow=shape=="files"?"Files###panel.project.files":"Outliner###outliner";
        var outline=ImGui.Internal.FindWindowByName(scrollWindow);r.Host.MoveMouse(r.Draw,outline.Pos.X+80,outline.Pos.Y+outline.Size.Y/2);
        costs["scrollFrame"]=Measure(()=>{ImGui.GetIO().AddMouseWheelEvent(0,-100000);r.Host.Frame(r.Draw);});r.Host.Frames(r.Draw,20);
        costs["idleAfterScroll"]=Idle();Evidence("cost-"+shape,new{shape,phase="scrolled",costs});
        var id=shape=="nested"?r.Editor.Outline.ObjectIds(r.Session.Current).Last():shape=="many"?"/items/99999":"/items/79";
        costs["selectAndFrame"]=Measure(()=>{Assert.True(r.Editor.Select(id));r.Host.Frame(r.Draw);});Assert.Equal(id,r.Editor.Primary);
        if(shape!="nested")Assert.Equal("id"+id.Split('/')[2],r.Session.Current.At(id+"/id")!.Value.GetString());
        // One native search keystroke, after keyboard navigation activates the real Find field.
        ActivateFind(r);
        costs["searchKeystroke"]=Measure(()=>r.Host.Type(r.Draw,"q"));costs["searchCommit"]=Measure(()=>r.Host.Press(r.Draw,ImGuiKey.Enter));
        Assert.Contains("Nothing holds 'q'",r.Window.Status,StringComparison.Ordinal);
        if(shape=="files")
        {
            costs["selectFileAndFrame"]=Measure(()=>{Assert.True(r.Workspace.Show("file499.json"));r.Host.Frame(r.Draw);});
            Assert.Equal(500,r.Project.Files.Count);Assert.True(r.Workspace.Show("file001.json"));r.Host.Frames(r.Draw,5);
            Assert.Equal("file001.json",r.Workspace.Active!.Name);r.Bottom("Files###panel.project.files");r.ClickLast("Files###panel.project.files");
            Assert.Equal("rows.json",r.Workspace.Active!.Name);Assert.Same(r.Window,r.Workspace.Window);Assert.Equal(500,r.Project.Files.Select(f=>f.Name).Distinct().Count());
        }
        Evidence("cost-"+shape,new{shape,phase="complete",scrollWindow,fileCount=r.Project.Files.Count,outlineCount=r.Editor.Outline.ObjectIds(r.Session.Current).Count(),selection=r.Editor.Primary,costs});
    }
    [LongTheory][InlineData(100000)]
    public void Thousand_tail_additions_and_removals_are_one_undoable_batch(int count)
    {
        using var r=new Rig(count);Assert.True(r.Editor.Select("/items/50000"));var original=r.Session.Current;
        var root=JsonNode.Parse(original.Root.GetRawText())!;var items=root["items"]!.AsArray();
        for(int i=0;i<1000;i++)items.Add(new JsonObject{["id"]="new"+i,["name"]="Added "+i,["amount"]=i});
        Assert.True(r.Session.ExecuteBatch([new SetValue("/items",JsonSerializer.SerializeToElement(items)),new SetValue("/stamp",J("1"))]));
        Assert.Equal(count+1000,r.Session.Current.At("/items")!.Value.GetArrayLength());Assert.Equal("/items/50000",r.Editor.Primary);Assert.Equal("id50000",r.IdAt(50000));Assert.Equal(1,r.Session.History.UndoCount);r.Host.Frames(r.Draw);
        Assert.True(r.Session.Undo());Assert.Same(original,r.Session.Current);Assert.True(r.Session.Redo());
        Assert.True(r.Session.ExecuteBatch([new SetValue("/items",original.At("/items")!.Value),new SetValue("/stamp",J("2"))]));r.Host.Frames(r.Draw);
        Assert.Equal(count,r.Session.Current.At("/items")!.Value.GetArrayLength());Assert.Equal("id50000",r.IdAt(50000));Assert.Equal("/items/50000",r.Editor.Primary);Assert.Equal(2,r.Session.History.UndoCount);
        Evidence("batch1000",new{added=count+1000,removedTo=count,primary=r.Editor.Primary,undoSteps=r.Session.History.UndoCount});
    }
}
