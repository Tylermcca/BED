using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
using ImGui=Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED neutral rows and safety expectations. BORROWED: existing stable view-sort,
// plain rectangular TSV, validated batch/undo and HeadlessHost contracts. No game data or OS input.
[Collection(ImGuiCollection.Name)]
public sealed class TableAttackTests(ITestOutputHelper output)
{
    private const string Strings="""{"name":{"type":"string"},"left":{"type":"string"},"right":{"type":"string"}}""";
    private const string Numbers="""{"name":{"type":"string"},"value":{"type":"number","minimum":0}}""";
    private static string Root(string rows)=>"{\"$schema\":\"table.schema.json\",\"items\":["+rows+"]}";
    private static string RawFileHash(string file)=>Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(file)));
    private static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private sealed class Note(string name)
    {public string Name{get;}=name;public string Phase{get;set;}="starting";public Dictionary<string,object?> Values{get;}=[];public List<string> Faults{get;}=[];}
    private void Write(Note n)
    {var json=JsonSerializer.Serialize(n);output.WriteLine(json);if(Environment.GetEnvironmentVariable("A206_OBSERVATIONS") is {Length:>0} d){Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,n.Name+".json"),json);}}
    private void Proof(string name,Action<Note> action)
    {var n=new Note(name);Write(n);try{action(n);}catch(Exception e){n.Faults.Add(e.ToString());}n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));}
    private static void Need(Note n,bool ok,string fault){if(!ok)n.Faults.Add(fault);}
    private sealed class Rig:IDisposable
    {
        internal readonly string Folder=Path.Combine(Path.GetTempPath(),"table-attack-"+Guid.NewGuid().ToString("N"));
        internal readonly DataProject Project;internal readonly DataWorkspace Workspace;internal readonly DataTableView Table;
        internal readonly DocumentSession<DataDocument> Session;internal readonly string DiskHash;internal HeadlessHost? Host;
        internal string File=>Path.Combine(Folder,"data","rows.json");
        internal Rig(string text,string properties="{}")
        {
            Directory.CreateDirectory(Path.Combine(Folder,"data"));Directory.CreateDirectory(Path.Combine(Folder,"schemas"));
            System.IO.File.WriteAllText(File,text,new UTF8Encoding(false));DiskHash=RawFileHash(File);
            var schema="{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":"+properties+",\"additionalProperties\":true}}}}";
            System.IO.File.WriteAllText(Path.Combine(Folder,"schemas","table.schema.json"),schema);
            Project=DataProject.Open(Path.Combine(Folder,"data"),Path.Combine(Folder,"schemas"));Workspace=new DataWorkspace(Project);Assert.True(Workspace.Show("rows.json"),Workspace.Said);
            Table=Workspace.TableOf("rows.json")!;Session=Workspace.Active!.Session!;
        }
        internal string Text=>Session.Current.Root.GetRawText();
        internal string? StringAt(int row,string field)=>Session.Current.At($"/items/{row}/"+Pointer.Join([field])[1..])?.GetString();
        internal void Start()
        {Host??=new HeadlessHost(1600,1200){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());}
        internal void DrawTable()
        {ImGui.SetNextWindowPos(new(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new(1500,1100),ImGuiCond.Always);ImGui.Begin("A206 table");Table.Draw();ImGui.End();}
        internal void Settle(){Start();Host!.Frames(DrawTable,5);}
        internal void Key(ImGuiKey key,bool ctrl=false,bool shift=false)=>Host!.Press(DrawTable,key,ctrl,shift);
        internal void Pick(string field)
        {
            Settle();Key(ImGuiKey.DownArrow);Key(ImGuiKey.DownArrow);Key(ImGuiKey.Enter);for(var i=0;i<100&&Table.Anchor!=("/items/0",field);i++)Key(ImGuiKey.Tab);
            Assert.Equal(("/items/0",field),Table.Anchor);Key(ImGuiKey.Escape);
        }
        internal string Screen()
        {Start();string s="";Host!.Frame(()=>{ImGui.LogToClipboard();DrawTable();ImGui.LogFinish();s=ImGui.GetClipboardText().ToString();});return s;}
        internal void DiskSame(Note n){Need(n,RawFileHash(File)==DiskHash,"Unsaved table operation changed the data file.");n.Values["diskUnchanged"]=RawFileHash(File)==DiskHash;}
        public void Dispose()
        {Host?.Dispose();var boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;Assert.StartsWith(boundary,Path.GetFullPath(Folder),StringComparison.OrdinalIgnoreCase);Directory.Delete(Folder,true);}
    }
    public static IEnumerable<object[]> ShapeCases=>new[]{"late_field","mixed_shapes","sparse_fields","cell_100kb"}.Select(s=>new object[]{s});
    [Theory,MemberData(nameof(ShapeCases))]
    public void Shapes_keep_every_field_and_row_kind(string kind)=>Proof(kind,n=>
    {
        string text;
        if(kind=="late_field")text=Root(string.Join(",",Enumerable.Range(0,502).Select(i=>"{\"name\":\"row"+i+"\""+(i==501?",\"late\":73":"")+"}")));
        else if(kind=="mixed_shapes")text=Root("""{"name":"object","value":{"inside":1}},{"name":"number","value":7},{"name":"list","value":[1,2]}""");
        else if(kind=="sparse_fields")text=Root("""{"name":"first","left":"A"},{"name":"second","right":"B"},{"name":"third","left":"C","extra":3}""");
        else text=Root(JsonSerializer.Serialize(new{name="long",left=new string('x',102400)+"\u03bb"}));
        using var r=new Rig(text);var before=r.Text;var columns=r.Table.Columns().Select(c=>c.Id).ToArray();n.Values["columns"]=columns;
        if(kind=="late_field")Need(n,columns.Contains("late"),"A field present only after row 500 is omitted from Columns.");
        if(kind=="mixed_shapes")
        {var cells=Enumerable.Range(0,3).Select(i=>r.Table.Cell("/items/"+i,"value")!).ToArray();n.Values["kinds"]=cells.Select(c=>new{c.Kind,c.Editable,c.Value}).ToArray();Need(n,!cells[0].Editable&&cells[1].Editable&&!cells[2].Editable,"Per-row nested/scalar editability was flattened.");Need(n,r.Table.SetCell("/items/1","value",9.0),r.Table.Said);Need(n,r.Session.Current.At("/items/0/value")!.Value.ValueKind==JsonValueKind.Object&&r.Session.Current.At("/items/2/value")!.Value.ValueKind==JsonValueKind.Array,"Editing scalar changed its nested neighbours.");Assert.True(r.Session.Undo());}
        if(kind=="sparse_fields")Need(n,new[]{"name","left","right","extra"}.All(columns.Contains),"Heterogeneous row fields disappeared.");
        if(kind=="cell_100kb")
        {r.Settle();var screen=r.Screen();n.Values["screenLength"]=screen.Length;Need(n,r.Table.Cell("/items/0","left")!.Value?.ToString()?.Length==102401,"Long cell was truncated in the table model.");r.Table.Anchor=("/items/0","left");var copy=r.Table.Copy()!;n.Values["copiedLength"]=copy.Length;Need(n,copy==r.StringAt(0,"left"),"Long cell copy lost characters.");var plan=r.Table.PlanPaste("/items/0","left",copy);Need(n,plan.Ok&&r.Table.Apply(plan),r.Table.Said);}
        Need(n,r.Text==before,"Shape inspection or no-op copy changed the document.");r.DiskSame(n);
    });
    public static IEnumerable<object[]> SortCases=>new[]{"huge_numbers_asc","huge_numbers_desc","large_integer_sort","stable_ties_asc","stable_ties_desc","mixed_sort","missing_empty_asc","missing_empty_desc","unicode_accent","unicode_case","sorted_edit","filtered_edit","sorted_paste","integer_paste_precision"}.Select(s=>new object[]{s});
    [Theory,MemberData(nameof(SortCases))]
    public void Sorting_filtering_and_edits_keep_visible_row_identity(string kind)=>Proof(kind,n=>
    {
        var rows="""{"name":"Cobalt","value":30},{"name":"Amber","value":10},{"name":"Birch","value":20}""";var props="{}";string[]? order=null;
        if(kind.StartsWith("huge_numbers",StringComparison.Ordinal)){rows="""{"name":"p2","value":2e400},{"name":"p1","value":1e400},{"name":"n1","value":-1e400},{"name":"n2","value":-2e400},{"name":"zero","value":0},{"name":"one","value":1}""";order=kind.EndsWith("desc",StringComparison.Ordinal)?["p2","p1","one","zero","n1","n2"]:["n2","n1","zero","one","p1","p2"];}
        if(kind=="large_integer_sort"){rows="""{"name":"plus1","value":9007199254740993},{"name":"base","value":9007199254740992},{"name":"plus2","value":9007199254740994}""";props="""{"value":{"type":"integer"}}""";order=["base","plus1","plus2"];}
        if(kind.StartsWith("stable_ties",StringComparison.Ordinal)){rows="""{"name":"tie0","value":2},{"name":"low","value":1},{"name":"tie2","value":2},{"name":"missing"}""";order=kind.EndsWith("desc",StringComparison.Ordinal)?["tie0","tie2","low","missing"]:["low","tie0","tie2","missing"];}
        if(kind=="mixed_sort"){rows="""{"name":"nine","value":"9"},{"name":"two","value":2},{"name":"ten","value":"10"},{"name":"false","value":false}""";order=["ten","two","nine","false"];}
        if(kind.StartsWith("missing_empty",StringComparison.Ordinal)){rows="""{"name":"missing"},{"name":"empty","value":""},{"name":"z","value":"z"},{"name":"null","value":null}""";order=kind.EndsWith("desc",StringComparison.Ordinal)?["z","null","empty","missing"]:["empty","null","z","missing"];}
        if(kind.StartsWith("unicode",StringComparison.Ordinal))rows="""{"name":"caf\u00e9"},{"name":"CAFE"},{"name":"cafe\u0301"},{"name":"CAF\u00c9"},{"name":"Else"}""";
        if(kind=="integer_paste_precision"){rows="""{"name":"exact","value":9007199254740992}""";props="""{"value":{"type":"integer"}}""";}
        using var r=new Rig(Root(rows),props);var before=r.Text;
        if(order is not null)
        {r.Table.Sort=("value",kind.EndsWith("desc",StringComparison.Ordinal));var visible=r.Table.Visible();var got=visible.Select(p=>r.Session.Current.At(p+"/name")!.Value.GetString()!).ToArray();n.Values["expected"]=order;n.Values["actual"]=got;n.Values["displayValues"]=visible.Select(p=>r.Table.Cell(p,"value")?.Value?.ToString()).ToArray();Need(n,got.SequenceEqual(order),"Sort order differs from exact numeric/text/tie/missing contract.");Need(n,r.Text==before&&r.Session.Journal.Count==0,"Sort changed document order/history.");}
        else if(kind.StartsWith("unicode",StringComparison.Ordinal))
        {r.Table.Filter=kind=="unicode_accent"?"CAF\u00c9":"cafe";var expected=kind=="unicode_accent"?new[]{"/items/0","/items/3"}:new[]{"/items/1","/items/2"};var got=r.Table.Visible().ToArray();n.Values["query"]=r.Table.Filter;n.Values["visible"]=got;Need(n,got.SequenceEqual(expected),"Ordinal case-insensitive filter lost its documented literal-accent behavior.");Need(n,r.Text==before,"Filter edited source data.");}
        else if(kind=="integer_paste_precision")
        {var plan=r.Table.PlanPaste("/items/0","value","9007199254740993");n.Values["planCommands"]=plan.Commands.Count;Need(n,plan.Ok&&r.Table.Apply(plan),r.Table.Said);n.Values["value"]=r.Session.Current.At("/items/0/value")!.Value.GetRawText();Need(n,r.Session.Current.At("/items/0/value")!.Value.GetRawText()=="9007199254740993","An exact integer paste was silently considered unchanged after double coercion.");}
        else
        {
            if(kind=="filtered_edit")r.Table.Filter="Birch";else r.Table.Sort=("value",false);
            var visible=r.Table.Visible().ToArray();n.Values["beforeVisible"]=visible;var target=visible[0];
            if(kind=="sorted_paste")
            {var plan=r.Table.PlanPaste(target,"value","7\n8");Need(n,plan.Ok&&r.Table.Apply(plan),r.Table.Said);Need(n,r.Session.Current.At("/items/1/value")!.Value.GetDouble()==7&&r.Session.Current.At("/items/2/value")!.Value.GetDouble()==8&&r.Session.Current.At("/items/0/value")!.Value.GetDouble()==30,"Sorted paste landed on stored row numbers instead of shown pointers.");}
            else if(kind=="filtered_edit")
            {Need(n,r.Table.SetCell(target,"name","Gone"),r.Table.Said);Need(n,r.StringAt(2,"name")=="Gone"&&r.StringAt(0,"name")=="Cobalt"&&r.Table.Visible().Count==0,"Filtered edit targeted a hidden row or retained stale visibility.");}
            else{Need(n,r.Table.SetCell(target,"value",99.0),r.Table.Said);Need(n,r.Session.Current.At("/items/1/value")!.Value.GetDouble()==99&&r.Session.Current.At("/items/0/value")!.Value.GetDouble()==30,"Sorted edit targeted the wrong stored row.");}
            n.Values["said"]=r.Table.Said;Need(n,r.Session.History.UndoCount==1,"One table action did not produce exactly one undo.");Need(n,r.Session.Undo()&&r.Text==before,"Undo did not restore the exact pre-edit document.");
        }
        r.DiskSame(n);
    });
    public static IEnumerable<object[]> PasteCases=>new[]{"copy_rectangle","copy_tab_cell","copy_newline_cell","copy_sparse_rectangle","too_many_rows","too_many_columns","ragged","schema_refusal","one_undo","stale_plan"}.Select(s=>new object[]{s});
    [Theory,MemberData(nameof(PasteCases))]
    public void Copy_paste_is_lossless_or_refused_and_atomic(string kind)=>Proof(kind,n=>
    {
        var rows="""{"name":"Row0","left":"A","right":"B"},{"name":"Row1","left":"C","right":"D"},{"name":"Row2","left":"E","right":"F"}""";var props=Strings;
        if(kind=="copy_tab_cell")rows="""{"name":"Row0","left":"a\tb","right":"keep"},{"name":"Row1","left":"untouched","right":"still"}""";
        if(kind=="copy_newline_cell")rows="""{"name":"Row0","left":"a\nb","right":"keep"},{"name":"Row1","left":"untouched","right":"still"}""";
        if(kind=="copy_sparse_rectangle")rows="""{"name":"Row0","left":"A"},{"name":"Row1","left":"C","right":"D"}""";
        if(kind=="schema_refusal"||kind=="one_undo"||kind=="stale_plan"){rows="""{"name":"Row0","value":1},{"name":"Row1","value":2},{"name":"Row2","value":3}""";props=Numbers;}
        using var r=new Rig(Root(rows),props);var before=r.Text;var old=r.Session.Current;
        DataTableView.BatchPlan plan;
        if(kind.StartsWith("copy_",StringComparison.Ordinal))
        {
            r.Pick("left");if(kind is "copy_rectangle" or "copy_sparse_rectangle"){r.Key(ImGuiKey.RightArrow,shift:true);r.Key(ImGuiKey.DownArrow,shift:true);Need(n,r.Table.RangeEnd==("/items/1","right"),"Keyboard did not select the intended 2x2 rectangle.");}
            var copy=r.Table.Copy()!;n.Values["clipboard"]=copy;n.Values["rangeEnd"]=r.Table.RangeEnd is { } end?new{end.Item,end.Field}:null;plan=r.Table.PlanPaste("/items/0","left",copy);n.Values["planProblems"]=plan.Problems;n.Values["plannedCommands"]=plan.Commands.Count;var ok=r.Table.Apply(plan);n.Values["applied"]=ok;n.Values["said"]=r.Table.Said;n.Values["afterCells"]=Enumerable.Range(0,Math.Min(2,r.Table.Items().Count)).Select(i=>new{row=i,left=r.Session.Current.At("/items/"+i+"/left")?.GetRawText(),right=r.Session.Current.At("/items/"+i+"/right")?.GetRawText()}).ToArray();
            Need(n,r.Text==before,"Copying cells and pasting them back changed values, field presence or neighbouring cells.");Need(n,r.Session.History.UndoCount==0,"A lossless no-op copy/paste created history.");
        }
        else
        {
            var text=kind switch{"too_many_rows"=>"a\nb\nc\nd","too_many_columns"=>"a\tb\tc","ragged"=>"a\tb\nc","schema_refusal"=>"7\n-1","one_undo"=>"7\n8",_=>"7\n8"};
            var field=kind is "schema_refusal" or "one_undo" or "stale_plan"?"value":"left";plan=r.Table.PlanPaste("/items/0",field,text);n.Values["planProblems"]=plan.Problems;n.Values["plannedCommands"]=plan.Commands.Count;
            if(kind=="stale_plan")
            {Need(n,r.Table.SetCell("/items/0","name","Changed after preview"),r.Table.Said);var justBefore=r.Text;var history=r.Session.History.UndoCount;var ok=r.Table.Apply(plan);n.Values["applied"]=ok;n.Values["said"]=r.Table.Said;
             // Existing BatchPlan carries no expected revision. This probe records its scope; no new contract is imposed.
             n.Values["previewAfterEditBehavior"]=ok?"applies original absolute cell targets after an unrelated name edit":"refused after edit";Need(n,r.StringAt(0,"name")=="Changed after preview","Paste overwrote an unrelated post-preview edit.");Need(n,r.Session.History.UndoCount>=history,"Paste corrupted prior history.");}
            else if(kind=="one_undo")
            {Need(n,plan.Ok&&r.Table.Apply(plan),r.Table.Said);Need(n,r.Session.History.UndoCount==1&&r.Session.Journal.Count==1,"Multi-cell paste was split into multiple undo/journal steps.");Need(n,r.Session.Current.At("/items/0/value")!.Value.GetDouble()==7&&r.Session.Current.At("/items/1/value")!.Value.GetDouble()==8,"Accepted rectangle has wrong values.");Need(n,r.Session.Undo()&&r.Text==before,"One undo failed to restore all pasted cells.");Need(n,!r.Session.Undo(),"Paste left extra undo steps.");}
            else
            {var ok=r.Table.Apply(plan);n.Values["applied"]=ok;n.Values["said"]=r.Table.Said;Need(n,!ok,"Invalid rectangle/schema paste was accepted.");Need(n,ReferenceEquals(old,r.Session.Current)&&r.Text==before&&r.Session.Journal.Count==0&&r.Session.History.UndoCount==0,"Refused paste changed state/history.");if(kind=="schema_refusal")Need(n,r.Table.Said.Contains("Row1",StringComparison.Ordinal)&&r.Table.Said.Contains("value",StringComparison.Ordinal),"Schema refusal failed to identify the row and cell.");}
        }
        n.Values["beforeHash"]=Hash(before);n.Values["afterHash"]=Hash(r.Text);r.DiskSame(n);
    });
    private sealed class LongFactAttribute:FactAttribute
    {public LongFactAttribute(){if(Environment.GetEnvironmentVariable("A206_LONG")!="1")Skip="Set A206_LONG=1 for the 50,000-row/200-column/10-MB table attack and timing proof; no long-run claim otherwise.";}}
    // Native assertions must write to stderr and exit, never raise a Windows CRT dialog.
    [System.Runtime.InteropServices.DllImport("ucrtbase",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int _set_error_mode(int mode);
    [System.Runtime.InteropServices.DllImport("ucrtbase",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern uint _set_abort_behavior(uint flags,uint mask);
    [LongFact]
    public void Two_hundred_columns_draw_without_native_failure()=>Proof("columns_200",n=>
    {var row="{"+string.Join(",",Enumerable.Range(0,200).Select(i=>"\"c"+i.ToString("D3",CultureInfo.InvariantCulture)+"\":"+i))+"}";using var r=new Rig(Root(row));n.Values["columns"]=r.Table.Columns().Count;Need(n,r.Table.Columns().Count==200,"Not all 200 fields became columns.");n.Phase="before native 201-column table draw";Write(n);if(OperatingSystem.IsWindows()){_set_error_mode(2);_set_abort_behavior(0,3);}r.Settle();n.Values["screenLength"]=r.Screen().Length;r.DiskSame(n);});
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private delegate void ClipboardSetter(nint context,nint text);
    // Simulates clipboard-provider input directly into HeadlessHost's own in-memory provider,
    // avoiding the binding's small transient ImString arena. No OS clipboard or unsafe-build switch.
    private static void LargeClipboard(string text)
    {var p=System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(text);try{System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<ClipboardSetter>(ImGui.GetPlatformIO().Platform_SetClipboardTextFn)(0,p);}finally{System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p);}}
    [LongFact]
    public void Ten_MB_clipboard_is_exact_and_one_undo()=>Proof("clipboard_10mb",n=>
    {using var r=new Rig(Root("""{"name":"Row0","left":"old","right":"keep"}"""),Strings);r.Start();var before=r.Text;var text=new string('x',10*1024*1024-2)+"\u03bb";LargeClipboard(text);var pasted=ImGui.GetClipboardText().ToString();n.Values["characters"]=pasted.Length;n.Values["utf8Bytes"]=Encoding.UTF8.GetByteCount(pasted);n.Values["clipboardHash"]=Hash(pasted);Need(n,pasted==text,"Clipboard storage truncated 10 MB input.");var plan=r.Table.PlanPaste("/items/0","left",pasted);Need(n,plan.Ok&&r.Table.Apply(plan),r.Table.Said);Need(n,r.StringAt(0,"left")==text&&r.StringAt(0,"right")=="keep","10 MB paste corrupted its cell or neighbour.");r.DiskSame(n);n.Phase="10 MB applied, before table draw";Write(n);try{r.Settle();n.Values["drawCompleted"]=true;}catch(Exception e){n.Values["drawCompleted"]=false;n.Values["drawError"]=e.ToString();n.Faults.Add("Drawing the accepted 10 MB cell threw: "+e.Message);}var one=r.Session.History.UndoCount==1;var undone=r.Session.Undo()&&r.Text==before;n.Values["oneUndoRestored"]=one&&undone;Need(n,one&&undone,"10 MB paste did not undo as one step.");r.DiskSame(n);});
    private static void Measure(Note n,string key,Action action,int warm=20,int samples=150)
    {for(var i=0;i<warm;i++)action();var times=new List<double>();var allocations=new List<long>();for(var i=0;i<samples;i++){var b=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();action();times.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);allocations.Add(GC.GetAllocatedBytesForCurrentThread()-b);}var ts=times.Order().ToArray();var bs=allocations.Order().ToArray();n.Values[key]=new{warmups=warm,samples,times,allocations,medianMs=ts[samples/2],p95Ms=ts[(int)Math.Ceiling(samples*.95)-1],maxMs=ts[^1],medianBytes=bs[samples/2],p95Bytes=bs[(int)Math.Ceiling(samples*.95)-1],maxBytes=bs[^1]};}
    private static void Once(Note n,string key,Action action)
    {var b=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();action();n.Values[key]=new{samples=1,elapsedMs=Stopwatch.GetElapsedTime(t).TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-b};}
    [LongFact]
    public void Fifty_thousand_rows_cost_and_identity()=>Proof("rows_50000",n=>
    {
        var rows=string.Join(",",Enumerable.Range(0,50000).Select(i=>"{\"name\":\"Row"+i+"\",\"value\":"+(50000-i)+"}"));using var r=new Rig(Root(rows),Numbers);n.Values["items"]=50000;n.Values["dataBytes"]=new FileInfo(r.File).Length;var before=r.Text;
        var random=new Random(206051);var sample=Enumerable.Range(0,20).Select(_=>random.Next(50000)).Concat([0,63,64,49999]).Distinct().ToArray();foreach(var i in sample)Assert.Equal(50000-i,r.Session.Current.At("/items/"+i+"/value")!.Value.GetInt32());n.Values["listIndexSeed"]=206051;n.Values["listIndexSample"]=sample;
        r.Start();foreach(var panel in r.Workspace.Window!.Editor.Panels)r.Workspace.Window.Editor.SetOpen(panel.Id,false);r.Workspace.Window.Editor.SetOpen("editor.table",true);r.Host!.Frames(r.Workspace.Draw,5);Measure(n,"frame50000",()=>r.Host.Frame(r.Workspace.Draw));
        Once(n,"sort50000",()=>{r.Table.Sort=("value",false);Assert.Equal("/items/49999",r.Table.Visible()[0]);});Need(n,r.Text==before,"Sort changed file order.");
        // Actual filter InputText receives one ImGui text event; setter/Visible-only work is not called a keystroke.
        r.Table.Sort=null;r.Table.Filter="";r.Host.Frames(r.DrawTable,3);var focus=true;
        void FilterDraw(){ImGui.SetNextWindowPos(new(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new(1500,1100),ImGuiCond.Always);ImGui.Begin("A206 table");if(focus){ImGui.SetKeyboardFocusHere(1);focus=false;}r.Table.Draw();ImGui.End();}
        r.Host.Frames(FilterDraw,2);n.Values["filterWantTextInput"]=ImGui.GetIO().WantTextInput;
        Once(n,"filterKeystroke50000",()=>{ImGui.GetIO().AddInputCharactersUTF8("9");r.Host.Frame(FilterDraw);});r.Host.Frame(FilterDraw);n.Values["filter"]=r.Table.Filter;Need(n,r.Table.Filter=="9","Filter keystroke did not reach the real table filter.");var expected=r.Table.Items().Where(p=>r.Session.Current.At(p+"/name")!.Value.GetString()!.Contains('9')||r.Session.Current.At(p+"/value")!.Value.GetRawText().Contains('9')).ToArray();Need(n,r.Table.Visible().SequenceEqual(expected),"Large filter returned wrong items.");n.Values["filteredRows"]=r.Table.Visible().Count;
        r.Table.Filter="";r.Table.Sort=null;var paste=string.Join("\n",Enumerable.Range(0,1000).Select(i=>(70000+i).ToString(CultureInfo.InvariantCulture)));DataTableView.BatchPlan? plan=null;
        Once(n,"paste1000",()=>{plan=r.Table.PlanPaste("/items/0","value",paste);Assert.True(plan.Ok,string.Join(";",plan.Problems));Assert.True(r.Table.Apply(plan),r.Table.Said);});n.Values["pasteCells"]=plan!.Cells;Need(n,r.Session.History.UndoCount==1,"1000 cells were not one undo.");foreach(var i in Enumerable.Range(0,1000))Assert.Equal(70000+i,r.Session.Current.At("/items/"+i+"/value")!.Value.GetInt32());Assert.Equal(49000,r.Session.Current.At("/items/1000/value")!.Value.GetInt32());Need(n,r.Session.Undo()&&r.Text==before,"Large paste failed exact undo.");foreach(var i in sample)Assert.Equal(50000-i,r.Session.Current.At("/items/"+i+"/value")!.Value.GetInt32());n.Values["runtime"]=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;n.Values["os"]=System.Runtime.InteropServices.RuntimeInformation.OSDescription;n.Values["processors"]=Environment.ProcessorCount;n.Values["clock"]=Stopwatch.Frequency;r.DiskSame(n);
    });
}
