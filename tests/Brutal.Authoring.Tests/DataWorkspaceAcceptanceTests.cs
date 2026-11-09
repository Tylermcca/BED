using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED: self-authored fixtures and independent public-contract acceptance tests.
// Expectations come from the card/public doc contracts; fixtures are independent and DataWorkspaceTests was not copied.
// A bounded public-palette metadata read exposed adjacent methods; those bodies were not used as expectation oracles.
[Collection(ImGuiCollection.Name)]
public sealed class DataWorkspaceAcceptanceTests(ITestOutputHelper output)
{
    private const string AData = """{"items":[{"id":"a0","name":"OUTLINE ANCHOR A048","amount":3,"local":"a1","remote":"b0","deep":"inner0","ambiguous":"duplicate","unopened":"bad"},{"id":"a1","name":"Second kite","amount":7,"local":"a2","remote":"b1","deep":"inner1"},{"id":"a2","name":"Third kite","amount":11,"local":"a0","remote":"b0","deep":"inner0"}]}""";
    private const string BData = """{"records":[{"id":"b0","name":"Blue target","amount":4,"local":"b1","details":[{"id":"inner0","value":41}]},{"id":"b1","name":"Green target","amount":9,"local":"b0","details":[{"id":"inner1","value":42}]}]}""";
    private const string CData = """{"records":[{"id":"duplicate","name":"Duplicate one","amount":1},{"id":"duplicate","name":"Duplicate two","amount":2}]}""";
    private const string ASchema = """{"type":"object","required":["items"],"additionalProperties":false,"properties":{"items":{"type":"array","items":{"type":"object","required":["id","name","amount"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"amount":{"type":"integer","minimum":0},"local":{"type":"string","x-ref":"/items"},"remote":{"type":"string"},"deep":{"type":"string"},"ambiguous":{"type":"string"},"unopened":{"type":"string"},"details":{"type":"array","items":{"type":"object","required":["id","value"],"properties":{"id":{"type":"string"},"value":{"type":"integer"}},"additionalProperties":false}}},"additionalProperties":false}}}}""";
    private const string BSchema = """{"type":"object","required":["records"],"additionalProperties":false,"properties":{"records":{"type":"array","items":{"type":"object","required":["id","name","amount"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"amount":{"type":"integer","minimum":0},"local":{"type":"string","x-ref":"/records"},"remote":{"type":"string"},"deep":{"type":"string"},"ambiguous":{"type":"string"},"unopened":{"type":"string"},"details":{"type":"array","items":{"type":"object","required":["id","value"],"properties":{"id":{"type":"string"},"value":{"type":"integer"}},"additionalProperties":false}}},"additionalProperties":false}}}}""";

    private const string FileReason="This fixture is generated and must be saved through its upstream source.";
    private const uint ArrowColour=0xffd13bf1u;
    private const uint LayoutColour=0xff37c3e9u;
    private sealed class Fixture : IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"A048-owned",Guid.NewGuid().ToString("N"));
        public string Data=>Path.Combine(Root,"data"); public string Schemas=>Path.Combine(Root,"schemas");
        public readonly DataProject Project; public readonly DataWorkspace Workspace; public readonly HeadlessHost Host;
        public readonly ReferenceMap? Map;
        public float2 FilesOrigin,LastFileMin,LastFileMax,TableOrigin,TableMin,TableMax,ControlMin,ControlMax;
        public float2 MarkerWindowPos,MarkerWindowSize,MarkerMin,MarkerMax;public bool MarkerDocked;
        public float LineStep,LineHeight,FrameHeight,FrameStep,SplitSize,PaddingY;
        public Fixture(bool readOnly=false,bool perFile=false,bool broken=false,bool empty=false,bool marker=false,string? linkCase=null,Action<EditorModel<DataDocument>,DataTableView>? configure=null,bool noMap=false)
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            System.IO.File.WriteAllText(Path.Combine(Schemas,"a.schema.json"),linkCase is null?ASchema:"""{"type":"object","required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","required":["id","name","remote"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"remote":{"type":"string"}},"additionalProperties":false}}},"additionalProperties":false}""");System.IO.File.WriteAllText(Path.Combine(Schemas,"b.schema.json"),BSchema);System.IO.File.WriteAllText(Path.Combine(Schemas,"c.schema.json"),BSchema);
            if(!empty)
            {
                System.IO.File.WriteAllText(PathOf("a.json"),linkCase is null?AData:JsonSerializer.Serialize(new{items=new[]{new{id="a0",name="Source arrow",remote=linkCase switch{"found"=>"b0","missing"=>"absent","sentinel"=>"none","ambiguous"=>"duplicate","unopenable"=>"bad",_=>"b0"}}}}),new UTF8Encoding(false));
                System.IO.File.WriteAllText(PathOf("b.json"),BData);System.IO.File.WriteAllText(PathOf("c.json"),CData);
            }
            if(broken||linkCase=="unopenable") { System.IO.File.WriteAllText(Path.Combine(Schemas,"d-broken.schema.json"),BSchema);System.IO.File.WriteAllText(PathOf("d-broken.json"),"{broken"); }
            var refs=new JsonArray();
            void Ref(string field,string file,string list,string key,string[]? sentinels=null)
            { var r=new JsonObject{["file"]="a.json",["at"]="/items/*/"+field,["target"]=new JsonObject{["file"]=file,["list"]=list,["key"]=key},["evidence"]="INVENTED self-authored acceptance fixture"};if(sentinels is not null)r["sentinels"]=new JsonArray(sentinels.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());refs.Add(r); }
            if(linkCase!="unmapped")Ref("remote",linkCase=="ambiguous"?"c.json":linkCase=="unopenable"?"d-broken.json":"b.json","/records","id",["none"]);
            Ref("deep","b.json","/records/*/details","id");Ref("ambiguous","c.json","/records","id");Ref("unopened","d-broken.json","/records","id");
            Map=noMap?null:ReferenceMap.Read(new JsonObject{["game"]="A048 self-authored workspace",["labels"]=new JsonArray(),["references"]=refs}.ToJsonString());
            Project=DataProject.Open(Data,Schemas,readOnly,perFile?n=>n=="a.json"?FileReason:null:null);
            Host=new HeadlessHost(1280,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetIO().IniFilename=default;
            Workspace=new DataWorkspace(Project,configure:(editor,table)=>
            {
                if(marker)editor.Add(new Panel("a048.marker","A048 Left marker",()=>
                {
                    Metrics();MarkerWindowPos=ImGui.GetWindowPos();MarkerWindowSize=ImGui.GetWindowSize();MarkerDocked=ImGui.IsWindowDocked();MarkerMin=ImGui.GetCursorScreenPos();MarkerMax=MarkerMin+new float2(140,70);ImGui.GetWindowDrawList().AddRectFilled(MarkerMin,MarkerMax,LayoutColour);ImGui.Dummy(new float2(140,70));
                },Side:PanelSide.Left));
                configure?.Invoke(editor,table);
            },map:Map);
            if(!empty)Show("a.json");
        }
        public string PathOf(string name)=>Path.Combine(Data,name);
        public DataFile File(string name)=>Project.Find(name)!;
        public DocumentSession<DataDocument> Session(string name="a.json") { var f=File(name);if(f.Session is null)Assert.True(f.Open());return f.Session!; }
        public EditorWindow<DataDocument> Window=>Workspace.Window!;
        public DataTableView Table=>Workspace.TableOf(Workspace.Active!.Name)!;
        public void Show(string name)=>Assert.True(Workspace.Show(name),Workspace.Said);
        public void Edit(string name,int value)
        { Show(name);Assert.True(Table.SetCell(name=="a.json"?"/items/0":"/records/0","amount",(long)value),Table.Said); }
        private void Metrics(){LineStep=ImGui.GetTextLineHeightWithSpacing();LineHeight=ImGui.GetTextLineHeight();FrameHeight=ImGui.GetFrameHeight();FrameStep=ImGui.GetFrameHeightWithSpacing();SplitSize=ImGui.GetStyle().DockingSeparatorSize;PaddingY=ImGui.GetStyle().WindowPadding.Y;}
        public void DrawFiles()
        {
            ImGui.SetNextWindowPos(new float2(20,20));ImGui.SetNextWindowSize(new float2(430,450));ImGui.Begin("A048 Controlled Files");Metrics();FilesOrigin=ImGui.GetCursorScreenPos();Workspace.FilesPanel.Draw();LastFileMin=ImGui.GetItemRectMin();LastFileMax=ImGui.GetItemRectMax();ImGui.End();
        }
        public void DrawTable()
        {
            ImGui.SetNextWindowPos(new float2(20,20));ImGui.SetNextWindowSize(new float2(1120,550));ImGui.Begin("A048 Controlled Table");
            ImGui.PushStyleColor(ImGuiCol.Button,ArrowColour);ImGui.PushStyleColor(ImGuiCol.ButtonHovered,ArrowColour);ImGui.PushStyleColor(ImGuiCol.ButtonActive,ArrowColour);
            ImGui.BeginChild("A048 table area",new float2(1080,330));Metrics();TableOrigin=ImGui.GetCursorScreenPos();
            Table.Draw();TableMin=ImGui.GetItemRectMin();TableMax=ImGui.GetItemRectMax();ImGui.EndChild();
            ImGui.Button("A048 button rendering positive control");ControlMin=ImGui.GetItemRectMin();ControlMax=ImGui.GetItemRectMax();ImGui.PopStyleColor(3);ImGui.End();
        }
        public string Text(Action draw)
        {
            var path=Path.Combine(Root,Guid.NewGuid().ToString("N")+".log");Host.Frame(()=>{ImGui.LogToFile(0,path);draw();ImGui.LogFinish();});return System.IO.File.ReadAllText(path);
        }
        public float2 FilePoint(string name)
        {
            Host.Frames(DrawFiles);var files=Project.Files.Select(x=>x.Name).ToList();var index=files.IndexOf(name);Assert.True(index>=0);
            return new float2(LastFileMin.X+8,(LastFileMin.Y+LastFileMax.Y)/2-LineStep*(files.Count-1-index));
        }
        public void ChooseFile(string name){var p=FilePoint(name);Host.Click(DrawFiles,p.X,p.Y);}
        public string HoverFile(string name)
        {var p=FilePoint(name);Host.MoveMouse(DrawFiles,p.X,p.Y);Host.Frames(DrawFiles,45);return Text(DrawFiles);}
        public void FilesButton(bool save)
        {
            Host.Frames(DrawFiles);var style=ImGui.GetStyle();var saveWidth=ImGui.CalcTextSize("Save all").X+2*style.FramePadding.X;var width=ImGui.CalcTextSize(save?"Save all":"Refresh").X+2*style.FramePadding.X;
            var x=FilesOrigin.X+(save?0:saveWidth+style.ItemSpacing.X)+width/2;Host.Click(DrawFiles,x,FilesOrigin.Y+LineStep+FrameHeight/2);
        }
        public void Palette(string query,bool enter=true)
        {
            Host.Frames(Workspace.Draw);Host.Press(Workspace.Draw,ImGuiKey.P,ctrl:true,shift:true);Assert.True(Window.PaletteOpen,"Ctrl+Shift+P must open the actual palette.");Host.Type(Workspace.Draw,query);if(enter)Host.Press(Workspace.Draw,ImGuiKey.Enter);
        }
        public void TypeFilter(string text)
        {
            Host.Frames(DrawTable);Host.Click(DrawTable,TableOrigin.X+240,TableOrigin.Y+FrameHeight/2);Host.Type(DrawTable,text);Assert.Equal(text,Table.Filter);
        }
        public void ClickAmountHeader()
        {
            Host.Frames(DrawTable);var y=TableOrigin.Y+2*FrameStep+LineHeight/2;
            // Bounded public header hit search calibrated by the synthetic probe; no private widget IDs/state injection.
            for(var x=TableMin.X+8;x<TableMax.X-4;x+=16){Host.Click(DrawTable,x,y);if(Table.Sort?.Column=="amount")break;}
            Assert.NotNull(Table.Sort);Assert.Equal("amount",Table.Sort!.Value.Column);
        }
        public void Dispose()
        {
            Host.Dispose();var full=Path.GetFullPath(Root);var owner=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"A048-owned"));
            if(!string.Equals(Path.GetDirectoryName(full),owner,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(full),"N",out _))throw new InvalidOperationException("Unsafe fixture cleanup boundary.");
            if(Directory.Exists(full))Directory.Delete(full,true);
        }
    }
    private sealed record Snapshot(DataDocument Current,long Revision,int Undo,bool Redo,int Journal,bool Dirty,byte[] Bytes,DateTime Mtime);
    private static Snapshot Before(Fixture f,string name="a.json")
    {var s=f.Session(name);return new(s.Current,s.Revision,s.History.UndoCount,s.History.CanRedo,s.Journal.Count,s.Dirty,System.IO.File.ReadAllBytes(f.PathOf(name)),System.IO.File.GetLastWriteTimeUtc(f.PathOf(name)));}
    private static void Same(Fixture f,Snapshot b,string name="a.json",bool disk=true)
    {
        var s=f.Session(name);Assert.Same(b.Current,s.Current);Assert.Equal(b.Revision,s.Revision);Assert.Equal(b.Undo,s.History.UndoCount);Assert.Equal(b.Redo,s.History.CanRedo);Assert.Equal(b.Journal,s.Journal.Count);Assert.Equal(b.Dirty,s.Dirty);
        if(disk){Assert.Equal(b.Bytes,System.IO.File.ReadAllBytes(f.PathOf(name)));Assert.Equal(b.Mtime,System.IO.File.GetLastWriteTimeUtc(f.PathOf(name)));}
    }
    private static string Normal(string text)=>Regex.Replace(text,@"\s+"," ").Trim();
    private static void Reason(string text,string? expected=null)
    {Assert.False(string.IsNullOrWhiteSpace(text));if(expected is null)Assert.Matches("(?i)(read.only|outside|changed|disk)",text);else Assert.Contains(Normal(expected),Normal(text),StringComparison.OrdinalIgnoreCase);}
    private static void OutsideReason(string text) {Assert.Matches("(?i)(outside|changed.*disk|disk.*changed|changed.*since.*(open|save)|another.*(program|process|writer))",text);}
    private static void OpenReasons(Fixture f,string name) {Assert.NotEmpty(f.File(name).Problems);foreach(var problem in f.File(name).Problems)Reason(f.Workspace.Said,problem.Message);}
    private string Screen(Fixture f,Action draw) {var text=f.Text(draw);output.WriteLine(text);return text;}
    private static int Colour(Fixture f,uint colour,float2 min,float2 max)
    {var count=0;for(var y=Math.Max(0,(int)Math.Ceiling(min.Y));y<Math.Min(f.Host.Height,(int)max.Y);y++)for(var x=Math.Max(0,(int)Math.Ceiling(min.X));x<Math.Min(f.Host.Width,(int)max.X);x++)if(f.Host.Pixels[y*f.Host.Width+x]==colour)count++;return count;}
    private (int Count,float2 Min,float2 Max) ArrowPixels(Fixture f)
    {
        f.Host.Drawing=true;f.Host.Frames(f.DrawTable);var text=Screen(f,f.DrawTable);Assert.Contains("Source arrow",text);Assert.Contains("1 of 1 shown",text);Assert.True(Colour(f,ArrowColour,f.ControlMin,f.ControlMax)>100,"Own button must positively prove public style and CPU rasterizer render the unique button colour.");
        var min=new float2(f.TableMin.X,f.TableMin.Y+f.LineStep);var max=new float2(f.TableMax.X,min.Y+f.FrameStep);Assert.True(f.Host.Covered((int)min.X,(int)min.Y,(int)max.X,(int)max.Y)>50,"The actual table row must draw.");
        var n=0;var lo=new float2(float.MaxValue,float.MaxValue);var hi=default(float2);
        for(var y=Math.Max(0,(int)min.Y);y<Math.Min(f.Host.Height,(int)max.Y);y++)for(var x=Math.Max(0,(int)min.X);x<Math.Min(f.Host.Width,(int)max.X);x++)if(f.Host.Pixels[y*f.Host.Width+x]==ArrowColour){n++;lo=new float2(Math.Min(lo.X,x),Math.Min(lo.Y,y));hi=new float2(Math.Max(hi.X,x+1),Math.Max(hi.Y,y+1));}
        var fullBody=Colour(f,ArrowColour,f.TableMin,f.TableMax);Assert.Equal(fullBody,n);
        output.WriteLine($"Arrow full-table-body pixel count={fullBody}; row pixel count={n}; row=({min.X},{min.Y})-({max.X},{max.Y}); calibration=({f.ControlMin.X},{f.ControlMin.Y})-({f.ControlMax.X},{f.ControlMax.Y})");return(n,lo,hi);
    }

    private (float2 Min,float2 Max) BackButtonPixels(Fixture f)
    {
        var pixels=new HashSet<(int X,int Y)>();for(var y=(int)f.TableOrigin.Y;y<(int)(f.TableOrigin.Y+f.FrameHeight);y++)for(var x=(int)f.TableOrigin.X;x<(int)f.TableMax.X;x++)if(f.Host.Pixels[y*f.Host.Width+x]==ArrowColour)pixels.Add((x,y));
        var components=new List<(int Count,float2 Min,float2 Max)>();
        while(pixels.Count>0){var first=pixels.First();pixels.Remove(first);var queue=new Queue<(int X,int Y)>();queue.Enqueue(first);var count=0;var min=first;var max=first;while(queue.Count>0){var p=queue.Dequeue();count++;min=(Math.Min(min.X,p.X),Math.Min(min.Y,p.Y));max=(Math.Max(max.X,p.X),Math.Max(max.Y,p.Y));foreach(var next in new[]{(p.X-1,p.Y),(p.X+1,p.Y),(p.X,p.Y-1),(p.X,p.Y+1)})if(pixels.Remove(next))queue.Enqueue(next);}if(count>20)components.Add((count,new float2(min.X,min.Y),new float2(max.X+1,max.Y+1)));}
        Assert.NotEmpty(components);var back=components.MaxBy(c=>c.Max.X);var expectedWidth=ImGui.CalcTextSize("Back").X+2*ImGui.GetStyle().FramePadding.X;Assert.InRange(back.Max.X-back.Min.X,expectedWidth-3,expectedWidth+3);Assert.True(back.Min.X>f.TableOrigin.X+180,"Back must be beyond the left list-combo arrow; independent connected components exclude its colour.");output.WriteLine($"Back rightmost component pixels={back.Count}; bbox=({back.Min.X},{back.Min.Y})-({back.Max.X},{back.Max.Y}); components={components.Count}");return(back.Min,back.Max);
    }

    // DataWorkspace public contracts: cached per-file editors, view state, refusal and cross-file history.
    [Fact] public void Show_opens_the_requested_file_and_exposes_its_window_and_table()
    {using var f=new Fixture();f.Show("b.json");Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Same(f.File("b.json").Session,f.Window.Editor.Session);Assert.Same(f.Table,f.Workspace.TableOf("b.json"));}
    [Fact] public void Unshown_file_has_no_table_then_gets_one_when_shown()
    {using var f=new Fixture();Assert.Null(f.Workspace.TableOf("b.json"));f.Show("b.json");Assert.NotNull(f.Workspace.TableOf("b.json"));}
    [Fact] public void Switching_reuses_the_same_window_table_and_session()
    {using var f=new Fixture();var w=f.Window;var t=f.Table;var s=f.Session();f.Show("b.json");f.Show("a.json");Assert.Same(w,f.Window);Assert.Same(t,f.Table);Assert.Same(s,f.Session());}
    [Fact] public void Configure_runs_once_for_each_created_file_editor()
    {var counts=new Dictionary<string,int>();using var f=new Fixture(configure:(e,t)=>{var n=Path.GetFileName(e.Session.FilePath)!;counts[n]=counts.GetValueOrDefault(n)+1;});f.Show("b.json");f.Show("a.json");f.Show("b.json");Assert.Equal(1,counts["a.json"]);Assert.Equal(1,counts["b.json"]);Assert.False(counts.ContainsKey("c.json"));}
    [Fact] public void Unknown_show_preserves_active_window_selection_and_view()
    {using var f=new Fixture();f.Window.Editor.Select("/items/1");f.Table.Filter="Second";var w=f.Window;var t=f.Table;var s=Before(f);Assert.False(f.Workspace.Show("absent.json"));Assert.Contains("absent.json",f.Workspace.Said);Assert.Same(w,f.Window);Assert.Same(t,f.Table);Assert.Equal("/items/1",f.Window.Editor.Primary);Assert.Equal("Second",f.Table.Filter);Same(f,s);}
    [Fact] public void Unopenable_show_preserves_prior_editor_and_explains_why()
    {using var f=new Fixture(broken:true);var w=f.Window;var s=Before(f);Assert.False(f.Workspace.Show("d-broken.json"));Assert.Same(w,f.Window);Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Contains("d-broken.json",f.Workspace.Said);OpenReasons(f,"d-broken.json");Assert.Null(f.Workspace.TableOf("d-broken.json"));Same(f,s);}
    [Fact] public void Selection_survives_switching_both_ways()
    {using var f=new Fixture();f.Window.Editor.Select("/items/1");f.Show("b.json");f.Window.Editor.Select("/records/1");f.Show("a.json");Assert.Equal(new[]{"/items/1"},f.Window.Editor.Selection);f.Show("b.json");Assert.Equal(new[]{"/records/1"},f.Window.Editor.Selection);}
    [Fact] public void Public_filter_and_sort_view_state_survive_switching()
    {using var f=new Fixture();f.Table.Filter="kite";f.Table.Sort=("amount",true);var s=Before(f);f.Show("b.json");f.Table.Filter="Green";f.Table.Sort=("name",false);f.Show("a.json");Assert.Equal("kite",f.Table.Filter);Assert.Equal(("amount",true),f.Table.Sort);Same(f,s);f.Show("b.json");Assert.Equal("Green",f.Table.Filter);Assert.Equal(("name",false),f.Table.Sort);}
    [Fact] public void Unsaved_drafts_survive_file_switching_without_disk_writes()
    {using var f=new Fixture();f.Edit("a.json",17);var a=Before(f);f.Edit("b.json",23);var b=Before(f,"b.json");f.Show("a.json");Assert.Equal(17,f.Session().Current.At("/items/0/amount")!.Value.GetInt32());Same(f,a);f.Show("b.json");Same(f,b,"b.json");}
    [Fact] public void Undo_in_a_never_changes_b_history_or_draft()
    {using var f=new Fixture();f.Edit("a.json",17);f.Edit("b.json",23);var b=Before(f,"b.json");f.Show("a.json");Assert.True(f.Window.Run("editor.undo"));Assert.Equal(3,f.Session().Current.At("/items/0/amount")!.Value.GetInt32());Same(f,b,"b.json");}
    [Fact] public void Undo_in_b_never_changes_a_history_or_draft()
    {using var f=new Fixture();f.Edit("a.json",17);var a=Before(f);f.Edit("b.json",23);Assert.True(f.Window.Run("editor.undo"));Assert.Equal(4,f.Session("b.json").Current.At("/records/0/amount")!.Value.GetInt32());Same(f,a);}
    [Fact] public void Switching_and_selection_are_not_document_edits()
    {using var f=new Fixture();var a=Before(f);var b=Before(f,"b.json");f.Window.Editor.Select("/items/2");f.Show("b.json");f.Window.Editor.Select("/records/1");f.Show("a.json");Same(f,a);Same(f,b,"b.json");}
    [Fact] public void Refresh_adds_new_file_without_dropping_current_draft()
    {using var f=new Fixture();f.Edit("a.json",17);var a=Before(f);Assert.Null(f.Project.Find("d-new.json"));System.IO.File.WriteAllText(f.PathOf("d-new.json"),BData);System.IO.File.WriteAllText(Path.Combine(f.Schemas,"d-new.schema.json"),BSchema);f.Workspace.Refresh();Assert.NotNull(f.Project.Find("d-new.json"));Same(f,a);}
    [Fact] public void Refresh_keeps_gone_files_and_their_unsaved_session()
    {using var f=new Fixture();f.Edit("a.json",17);var a=Before(f);var file=f.File("a.json");System.IO.File.Delete(f.PathOf("a.json"));f.Workspace.Refresh();Assert.Same(file,f.File("a.json"));Assert.Equal(DataFileState.Missing,file.State);Assert.True(file.Dirty);Same(f,a,disk:false);}
    [Fact] public void Refresh_restored_file_keeps_its_pending_session()
    {using var f=new Fixture();f.Edit("a.json",17);var session=f.Session();var draft=session.Current;System.IO.File.Delete(f.PathOf("a.json"));f.Workspace.Refresh();System.IO.File.WriteAllText(f.PathOf("a.json"),AData.Replace("\"amount\":3","\"amount\":77"));f.Workspace.Refresh();Assert.Same(session,f.File("a.json").Session);Assert.Same(draft,session.Current);Assert.Equal(17,session.Current.At("/items/0/amount")!.Value.GetInt32());}
    [Fact] public void SaveAll_saves_multiple_writable_drafts()
    {using var f=new Fixture();f.Edit("a.json",17);f.Edit("b.json",23);Assert.True(f.Workspace.SaveAll(),f.Workspace.Said);Assert.False(f.Session().Dirty);Assert.False(f.Session("b.json").Dirty);using var a=JsonDocument.Parse(System.IO.File.ReadAllText(f.PathOf("a.json")));using var b=JsonDocument.Parse(System.IO.File.ReadAllText(f.PathOf("b.json")));Assert.Equal(17,a.RootElement.GetProperty("items")[0].GetProperty("amount").GetInt32());Assert.Equal(23,b.RootElement.GetProperty("records")[0].GetProperty("amount").GetInt32());}
    [Fact] public void SaveAll_readonly_file_refusal_does_not_prevent_independent_save()
    {using var f=new Fixture(perFile:true);f.Edit("a.json",17);var a=Before(f);f.Edit("b.json",23);Assert.False(f.Workspace.SaveAll());Reason(f.Workspace.Said,FileReason);Assert.Contains("a.json",f.Workspace.Said);Same(f,a);Assert.False(f.Session("b.json").Dirty);using var b=JsonDocument.Parse(System.IO.File.ReadAllText(f.PathOf("b.json")));Assert.Equal(23,b.RootElement.GetProperty("records")[0].GetProperty("amount").GetInt32());}
    [Fact] public void SaveAll_outside_change_refusal_keeps_external_and_pending_versions()
    {using var f=new Fixture();f.Edit("a.json",17);var a=Before(f);var outside=AData.Replace("\"amount\":3","\"amount\":77");System.IO.File.WriteAllText(f.PathOf("a.json"),outside);var bytes=System.IO.File.ReadAllBytes(f.PathOf("a.json"));var stamp=System.IO.File.GetLastWriteTimeUtc(f.PathOf("a.json"));f.Edit("b.json",23);Assert.False(f.Workspace.SaveAll());OutsideReason(f.Workspace.Said);Assert.Contains("a.json",f.Workspace.Said);Same(f,a,disk:false);Assert.Equal(bytes,System.IO.File.ReadAllBytes(f.PathOf("a.json")));Assert.Equal(stamp,System.IO.File.GetLastWriteTimeUtc(f.PathOf("a.json")));Assert.False(f.Session("b.json").Dirty);using var healthy=JsonDocument.Parse(System.IO.File.ReadAllText(f.PathOf("b.json")));Assert.Equal(23,healthy.RootElement.GetProperty("records")[0].GetProperty("amount").GetInt32());}
    [Fact] public void Save_refuses_outside_changes_and_keeps_edits()
    {using var f=new Fixture();f.Edit("a.json",17);var a=Before(f);var outside=AData.Replace("\"amount\":3","\"amount\":77");System.IO.File.WriteAllText(f.PathOf("a.json"),outside);var stamp=System.IO.File.GetLastWriteTimeUtc(f.PathOf("a.json"));Assert.False(f.Window.Run("editor.save"));OutsideReason(f.Window.Status);Same(f,a,disk:false);Assert.Equal(outside,System.IO.File.ReadAllText(f.PathOf("a.json")));Assert.Equal(stamp,System.IO.File.GetLastWriteTimeUtc(f.PathOf("a.json")));}
    [Fact] public void SaveAll_identifies_every_unsaved_readonly_file()
    {using var f=new Fixture(readOnly:true);f.Edit("a.json",17);var a=Before(f);f.Edit("b.json",23);var b=Before(f,"b.json");Assert.False(f.Workspace.SaveAll());Assert.Contains("a.json",f.Workspace.Said);Assert.Contains("b.json",f.Workspace.Said);Reason(f.Workspace.Said,f.File("a.json").ReadOnlyReason!);Same(f,a);Same(f,b,"b.json");}
    [Fact] public void GoTo_cross_file_selects_exact_item_and_remembers_source()
    {using var f=new Fixture();f.Window.Editor.Select("/items/1");var a=Before(f);var b=Before(f,"b.json");Assert.True(f.Workspace.GoTo(new("b.json","/records/1")),f.Workspace.Said);Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Equal("/records",f.Table.Shown);Assert.Equal("/records/1",f.Window.Editor.Primary);Assert.True(f.Workspace.Back());Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Equal("/items/1",f.Window.Editor.Primary);Same(f,a);Same(f,b,"b.json");}
    [Fact] public void GoTo_nested_scalar_selects_nearest_outline_item_and_says_inside_path()
    {using var f=new Fixture();var a=Before(f);var b=Before(f,"b.json");Assert.True(f.Workspace.GoTo(new("b.json","/records/0/details/0/value")),f.Workspace.Said);Assert.Equal("/records/0",f.Window.Editor.Primary);Assert.Equal("/records",f.Table.Shown);Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Contains("/details/0/value",f.Workspace.Said);Same(f,a);Same(f,b,"b.json");}
    [Fact] public void Follow_map_link_switches_file_and_selects_target_without_editing()
    {using var f=new Fixture();f.Window.Editor.Select("/items/0");var a=Before(f);var b=Before(f,"b.json");Assert.True(f.Table.Follow("/items/0","remote"),f.Table.Said);Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Equal("/records/0",f.Window.Editor.Primary);Same(f,a);Same(f,b,"b.json");}
    [Fact] public void Follow_nested_map_target_selects_nearest_item_and_inside_path()
    {using var f=new Fixture();var a=Before(f);var b=Before(f,"b.json");Assert.True(f.Table.Follow("/items/0","deep"),f.Table.Said);Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Equal("/records/0",f.Window.Editor.Primary);Assert.Contains("/details/0",f.Workspace.Said);Same(f,a);Same(f,b,"b.json");}
    [Fact] public void Back_cross_file_then_source_local_history_is_layered()
    {using var f=new Fixture();f.Window.Editor.Select("/items/0");var ta=f.Table;Assert.True(ta.Follow("/items/0","local"));Assert.Equal("/items/1",f.Window.Editor.Primary);Assert.True(ta.Follow("/items/1","remote"));var tb=f.Table;Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.True(tb.Back());Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Equal("/items/1",f.Window.Editor.Primary);Assert.True(ta.Back());Assert.Equal("/items/0",f.Window.Editor.Primary);}
    [Fact] public void Back_uses_target_local_history_before_cross_file_history()
    {using var f=new Fixture();f.Window.Editor.Select("/items/0");Assert.True(f.Table.Follow("/items/0","remote"));var tb=f.Table;Assert.True(tb.Follow("/records/0","local"));Assert.Equal("/records/1",f.Window.Editor.Primary);Assert.True(tb.Back());Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Equal("/records/0",f.Window.Editor.Primary);Assert.True(tb.Back());Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Equal("/items/0",f.Window.Editor.Primary);}
    [Fact] public void Workspace_Back_with_no_cross_file_history_is_false_and_unchanged()
    {using var f=new Fixture();f.Window.Editor.Select("/items/1");var a=Before(f);Assert.False(f.Workspace.Back());Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Equal("/items/1",f.Window.Editor.Primary);Same(f,a);}
    [Fact] public void Table_Back_with_no_local_or_cross_history_is_false_and_unchanged()
    {using var f=new Fixture();f.Window.Editor.Select("/items/1");var a=Before(f);Assert.False(f.Table.Back());Assert.Equal("/items/1",f.Window.Editor.Primary);Same(f,a);}
    [Fact] public void Failed_GoTo_unopenable_file_says_why_without_a_bogus_history_entry()
    {using var f=new Fixture(broken:true);f.Window.Editor.Select("/items/1");Assert.True(f.Workspace.GoTo(new("b.json","/records/0")));var w=f.Window;Assert.False(f.Workspace.GoTo(new("d-broken.json","/records/0")));Assert.Same(w,f.Window);Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Contains("d-broken.json",f.Workspace.Said);OpenReasons(f,"d-broken.json");Assert.True(f.Workspace.Back());Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Equal("/items/1",f.Window.Editor.Primary);Assert.False(f.Workspace.Back());}
    [Fact] public void Public_foreign_link_to_unopenable_file_reports_failure_without_switching()
    {using var f=new Fixture(broken:true,noMap:true,configure:(ed,t)=>t.Links=(pointer,value)=>pointer.EndsWith("/unopened",StringComparison.Ordinal)?new ReferenceTarget("d-broken.json","/records/0"):null);var a=Before(f);Assert.NotNull(f.Table.LinkOf("/items/0","unopened"));Assert.False(f.Table.Follow("/items/0","unopened"));Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Contains("d-broken.json",f.Workspace.Said);OpenReasons(f,"d-broken.json");Same(f,a);}
    [Fact] public void Map_NotLoaded_does_not_invent_a_followable_target()
    {using var f=new Fixture(linkCase:"unopenable");var value=f.Session().Current.At("/items/0/remote")!.Value;Assert.Equal(ReferenceOutcome.NotLoaded,f.Map!.Resolve(f.Project,"a.json","/items/0/remote",value).Outcome);Assert.Null(f.Table.LinkOf("/items/0","remote"));}
    [Fact] public void Global_readonly_Save_refuses_with_reason_before_any_write() {using var f=new Fixture(readOnly:true);f.Edit("a.json",17);var a=Before(f);var destination=Path.Combine(f.Root,"copy.json");Assert.False(f.Window.Run("editor.save"));Reason(f.Window.Status,f.File("a.json").ReadOnlyReason!);Same(f,a);Assert.False(System.IO.File.Exists(destination));}
    [Fact] public void Global_readonly_SaveAs_refuses_with_reason_before_any_write() {using var f=new Fixture(readOnly:true);f.Edit("a.json",17);var a=Before(f);var destination=Path.Combine(f.Root,"copy.json");Assert.False(f.Window.SaveAs(destination));Reason(f.Window.Status,f.File("a.json").ReadOnlyReason!);Same(f,a);Assert.False(System.IO.File.Exists(destination));}
    [Fact] public void Global_readonly_SaveAll_refuses_with_reason_before_any_write() {using var f=new Fixture(readOnly:true);f.Edit("a.json",17);var a=Before(f);var destination=Path.Combine(f.Root,"copy.json");Assert.False(f.Workspace.SaveAll());Reason(f.Workspace.Said,f.File("a.json").ReadOnlyReason!);Same(f,a);Assert.False(System.IO.File.Exists(destination));}
    [Fact] public void Per_file_readonly_Save_refuses_with_reason_before_any_write() {using var f=new Fixture(perFile:true);f.Edit("a.json",17);var a=Before(f);var destination=Path.Combine(f.Root,"copy.json");Assert.False(f.Window.Run("editor.save"));Reason(f.Window.Status,FileReason);Same(f,a);Assert.False(System.IO.File.Exists(destination));}
    [Fact] public void Per_file_readonly_SaveAs_refuses_with_reason_before_any_write() {using var f=new Fixture(perFile:true);f.Edit("a.json",17);var a=Before(f);var destination=Path.Combine(f.Root,"copy.json");Assert.False(f.Window.SaveAs(destination));Reason(f.Window.Status,FileReason);Same(f,a);Assert.False(System.IO.File.Exists(destination));}
    [Fact] public void Per_file_readonly_SaveAll_refuses_with_reason_before_any_write() {using var f=new Fixture(perFile:true);f.Edit("a.json",17);var a=Before(f);var destination=Path.Combine(f.Root,"copy.json");Assert.False(f.Workspace.SaveAll());Reason(f.Workspace.Said,FileReason);Same(f,a);Assert.False(System.IO.File.Exists(destination));}

    // Real public Files, palette, table and docking routes; all fixture data is invented.
    [Fact] public void Files_panel_lists_every_file_including_unopened_and_broken()
    {using var f=new Fixture(broken:true);var text=Screen(f,f.DrawFiles);foreach(var name in new[]{"a.json","b.json","c.json","d-broken.json"})Assert.Contains(name,text);Assert.Contains("4 files",text);Assert.Null(f.Workspace.TableOf("b.json"));}
    [Fact] public void Empty_project_draws_a_zero_file_list()
    {using var f=new Fixture(empty:true);var text=Screen(f,f.Workspace.Draw);Assert.Contains("0 files",text);Assert.Null(f.Workspace.Active);Assert.Null(f.Workspace.Window);}
    [Fact] public void Files_row_click_really_switches_editor_and_preserves_drafts()
    {using var f=new Fixture();f.Edit("a.json",17);var a=Before(f);f.ChooseFile("b.json");Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Same(f.Session("b.json"),f.Window.Editor.Session);f.ChooseFile("a.json");Assert.Equal("a.json",f.Workspace.Active!.Name);Same(f,a);}
    [Fact] public void Files_unsaved_star_appears_after_edit_and_clears_after_undo()
    {using var f=new Fixture();f.Edit("a.json",17);Assert.Matches(@"(?m)^.*(?:\*.*a\.json|a\.json.*\*).*$",Screen(f,f.DrawFiles));Assert.True(f.Window.Run("editor.undo"));Assert.False(f.Session().Dirty);Assert.DoesNotMatch(@"(?m)^.*(?:\*.*a\.json|a\.json.*\*).*$",Screen(f,f.DrawFiles));}
    [Fact] public void Files_unsaved_star_clears_after_real_Save_all_button()
    {using var f=new Fixture();f.Edit("a.json",17);Assert.Matches(@"(?m)^.*(?:\*.*a\.json|a\.json.*\*).*$",Screen(f,f.DrawFiles));f.FilesButton(true);Assert.False(f.Session().Dirty);Assert.Equal(17,JsonNode.Parse(System.IO.File.ReadAllText(f.PathOf("a.json")))!["items"]![0]!["amount"]!.GetValue<int>());Assert.DoesNotMatch(@"(?m)^.*(?:\*.*a\.json|a\.json.*\*).*$",Screen(f,f.DrawFiles));}
    [Fact] public void Files_cannot_open_marker_has_actual_problem_on_hover()
    {using var f=new Fixture(broken:true);f.ChooseFile("d-broken.json");Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Contains("(can't open)",Screen(f,f.DrawFiles));f.Show("a.json");f.Host.MoveMouse(f.DrawFiles,1000,700);var away=Screen(f,f.DrawFiles);foreach(var problem in f.File("d-broken.json").Problems)Assert.DoesNotContain(Normal(problem.Message),Normal(away));var hover=f.HoverFile("d-broken.json");output.WriteLine(hover);Assert.NotEmpty(f.File("d-broken.json").Problems);foreach(var problem in f.File("d-broken.json").Problems)Reason(hover,problem.Message);}
    [Fact] public void Files_readonly_marker_has_file_reason_on_hover()
    {using var f=new Fixture(perFile:true);Assert.Contains("(read-only)",Screen(f,f.DrawFiles));f.Host.MoveMouse(f.DrawFiles,1000,700);Assert.DoesNotContain(Normal(FileReason),Normal(Screen(f,f.DrawFiles)));var hover=f.HoverFile("a.json");output.WriteLine(hover);Reason(hover,FileReason);}
    [Fact] public void Files_gone_marker_keeps_dirty_editor_after_Refresh_button()
    {using var f=new Fixture();f.Edit("a.json",17);var s=f.Session();System.IO.File.Delete(f.PathOf("a.json"));f.FilesButton(false);Assert.Same(s,f.Session());Assert.True(s.Dirty);var text=Screen(f,f.DrawFiles);Assert.Contains("a.json",text);Assert.Contains("(gone from the folder)",text);Assert.Equal(17,s.Current.At("/items/0/amount")!.Value.GetInt64());}
    [Fact] public void Save_all_button_saves_both_independent_drafts()
    {using var f=new Fixture();f.Edit("a.json",17);f.Edit("b.json",23);f.FilesButton(true);Assert.False(f.Session().Dirty);Assert.False(f.Session("b.json").Dirty);Assert.Equal(17,JsonNode.Parse(System.IO.File.ReadAllText(f.PathOf("a.json")))!["items"]![0]!["amount"]!.GetValue<int>());Assert.Equal(23,JsonNode.Parse(System.IO.File.ReadAllText(f.PathOf("b.json")))!["records"]![0]!["amount"]!.GetValue<int>());}
    [Fact] public void Refresh_button_discovers_new_matching_file()
    {using var f=new Fixture();System.IO.File.WriteAllText(Path.Combine(f.Schemas,"e-new.schema.json"),ASchema);System.IO.File.WriteAllText(f.PathOf("e-new.json"),AData);Assert.Null(f.Project.Find("e-new.json"));f.FilesButton(false);Assert.NotNull(f.Project.Find("e-new.json"));Assert.Contains("e-new.json",Screen(f,f.DrawFiles));}
    [Fact] public void Save_all_palette_action_saves_both_drafts()
    {using var f=new Fixture();f.Edit("a.json",17);f.Edit("b.json",23);f.Palette("Save all files");Assert.False(f.Session().Dirty);Assert.False(f.Session("b.json").Dirty);Assert.Equal(23,JsonNode.Parse(System.IO.File.ReadAllText(f.PathOf("b.json")))!["records"]![0]!["amount"]!.GetValue<int>());Assert.Equal(17,JsonNode.Parse(System.IO.File.ReadAllText(f.PathOf("a.json")))!["items"]![0]!["amount"]!.GetValue<int>());}
    [Fact] public void Refresh_palette_action_discovers_added_and_removed_files()
    {using var f=new Fixture();System.IO.File.WriteAllText(f.PathOf("e-new.json"),AData);System.IO.File.Delete(f.PathOf("c.json"));f.Palette("Refresh files");Assert.NotNull(f.Project.Find("e-new.json"));Assert.False(f.File("c.json").State!=DataFileState.Missing);var text=Screen(f,f.DrawFiles);Assert.Contains("e-new.json",text);Assert.Contains("(gone from the folder)",text);}
    [Fact] public void Readonly_Save_all_palette_is_unavailable_with_visible_reason_and_no_write()
    {using var f=new Fixture(readOnly:true);f.Edit("a.json",17);var a=Before(f);f.Palette("Save all files",false);var entry=Assert.Single(f.Window.PaletteEntries("Save all files"),x=>x.Label=="Save all files");Assert.False(string.IsNullOrWhiteSpace(entry.Unavailable));Reason(entry.Unavailable!,f.File("a.json").ReadOnlyReason!);var text=Screen(f,f.Workspace.Draw);var start=text.LastIndexOf("Save all files",StringComparison.Ordinal);Assert.True(start>=0);Reason(text[start..],entry.Unavailable);f.Host.Press(f.Workspace.Draw,ImGuiKey.Enter);Same(f,a);}
    [Fact] public void Real_filter_typing_survives_file_switch_without_document_edit()
    {using var f=new Fixture();var a=Before(f);f.TypeFilter("Second");Assert.Single(f.Table.Visible());f.ChooseFile("b.json");f.ChooseFile("a.json");Assert.Equal("Second",f.Table.Filter);Assert.Single(f.Table.Visible());Assert.Equal("/items/1",f.Table.Visible()[0]);Same(f,a);}
    [Fact] public void Real_header_sort_survives_switch_with_selection_and_filter()
    {using var f=new Fixture();var a=Before(f);f.TypeFilter("kite");f.ClickAmountHeader();var sort=f.Table.Sort;f.Window.Editor.Select("/items/2");f.ChooseFile("b.json");f.ChooseFile("a.json");Assert.Equal(sort,f.Table.Sort);Assert.Equal("kite",f.Table.Filter);Assert.Equal("/items/2",f.Window.Editor.Primary);Same(f,a);}
    [Fact] public void Ctrl_S_saves_current_writable_file_without_saving_another_draft()
    {using var f=new Fixture();f.Edit("b.json",23);var b=Before(f,"b.json");f.Edit("a.json",17);f.Host.Frames(f.Workspace.Draw);f.Host.Press(f.Workspace.Draw,ImGuiKey.S,ctrl:true);Assert.False(f.Session().Dirty);Assert.Equal(17,JsonNode.Parse(System.IO.File.ReadAllText(f.PathOf("a.json")))!["items"]![0]!["amount"]!.GetValue<int>());Same(f,b,"b.json");}
    [Fact] public void Ctrl_S_readonly_file_explains_refusal_and_retains_draft()
    {using var f=new Fixture(perFile:true);f.Edit("a.json",17);var a=Before(f);f.Host.Frames(f.Workspace.Draw);f.Host.Press(f.Workspace.Draw,ImGuiKey.S,ctrl:true);Reason(f.Window.Status,FileReason);Same(f,a);}
    [Fact] public void Found_cross_file_arrow_is_visible_and_really_clickable()
    {using var f=new Fixture(linkCase:"found");var a=Before(f);var b=Before(f,"b.json");var result=f.Map!.Resolve(f.Project,"a.json","/items/0/remote",f.Session().Current.At("/items/0/remote")!.Value);Assert.Equal(ReferenceOutcome.Found,result.Outcome);Assert.Equal(new ReferenceTarget("b.json","/records/0"),f.Table.LinkOf("/items/0","remote"));var p=ArrowPixels(f);Assert.True(p.Count>0,"A Found cross-file link must have a visible Follow arrow in the rendered data row; full table/body and calibration were independently checked.");f.Host.Drawing=false;f.Host.Click(f.DrawTable,(p.Min.X+p.Max.X)/2,(p.Min.Y+p.Max.Y)/2);Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Equal("/records/0",f.Window.Editor.Primary);Same(f,a);Same(f,b,"b.json");Assert.True(f.Table.Back());Assert.Equal("a.json",f.Workspace.Active!.Name);}
    [Fact] public void Missing_cross_file_value_has_no_visible_or_actionable_arrow()
    {using var f=new Fixture(linkCase:"missing");var a=Before(f);var result=f.Map!.Resolve(f.Project,"a.json","/items/0/remote",f.Session().Current.At("/items/0/remote")!.Value);Assert.Equal(ReferenceOutcome.Missing,result.Outcome);Assert.Null(f.Table.LinkOf("/items/0","remote"));var p=ArrowPixels(f);Assert.Equal(0,p.Count);Assert.Equal("a.json",f.Workspace.Active!.Name);Same(f,a);}
    [Fact] public void Sentinel_cross_file_value_has_no_visible_or_actionable_arrow()
    {using var f=new Fixture(linkCase:"sentinel");var a=Before(f);var result=f.Map!.Resolve(f.Project,"a.json","/items/0/remote",f.Session().Current.At("/items/0/remote")!.Value);Assert.Equal(ReferenceOutcome.Sentinel,result.Outcome);Assert.Null(f.Table.LinkOf("/items/0","remote"));var p=ArrowPixels(f);Assert.Equal(0,p.Count);Assert.Equal("a.json",f.Workspace.Active!.Name);Same(f,a);}
    [Fact] public void Ambiguous_cross_file_value_has_no_visible_or_actionable_arrow()
    {using var f=new Fixture(linkCase:"ambiguous");var a=Before(f);var result=f.Map!.Resolve(f.Project,"a.json","/items/0/remote",f.Session().Current.At("/items/0/remote")!.Value);Assert.Equal(ReferenceOutcome.Ambiguous,result.Outcome);Assert.Null(f.Table.LinkOf("/items/0","remote"));var p=ArrowPixels(f);Assert.Equal(0,p.Count);Assert.Equal("a.json",f.Workspace.Active!.Name);Same(f,a);}
    [Fact] public void NotAReference_cross_file_value_has_no_visible_or_actionable_arrow()
    {using var f=new Fixture(linkCase:"unmapped");var a=Before(f);var result=f.Map!.Resolve(f.Project,"a.json","/items/0/remote",f.Session().Current.At("/items/0/remote")!.Value);Assert.Equal(ReferenceOutcome.NotAReference,result.Outcome);Assert.Null(f.Table.LinkOf("/items/0","remote"));var p=ArrowPixels(f);Assert.Equal(0,p.Count);Assert.Equal("a.json",f.Workspace.Active!.Name);Same(f,a);}
    [Fact] public void NotLoaded_cross_file_value_has_no_visible_or_actionable_arrow()
    {using var f=new Fixture(linkCase:"unopenable");var a=Before(f);var result=f.Map!.Resolve(f.Project,"a.json","/items/0/remote",f.Session().Current.At("/items/0/remote")!.Value);Assert.Equal(ReferenceOutcome.NotLoaded,result.Outcome);Assert.Null(f.Table.LinkOf("/items/0","remote"));var p=ArrowPixels(f);Assert.Equal(0,p.Count);Assert.Equal("a.json",f.Workspace.Active!.Name);Same(f,a);}
    [Fact] public void Left_panel_first_layout_is_rendered_above_functioning_outliner()
    {using var f=new Fixture(marker:true);f.Window.Editor.Select("/items/2");Assert.Equal("/items/2",f.Window.Editor.Primary);var a=Before(f);f.Host.Drawing=true;f.Host.Frames(f.Workspace.Draw,5);Assert.True(f.MarkerDocked);Assert.True(f.MarkerWindowPos.X<f.Host.Width/3f);Assert.True(f.MarkerMax.Y<f.MarkerWindowPos.Y+f.MarkerWindowSize.Y);Assert.True(Colour(f,LayoutColour,f.MarkerMin,f.MarkerMax)>6000,"Distinctive owned panel marker must render in the left dock.");var outlinerY=f.MarkerWindowPos.Y+f.MarkerWindowSize.Y+f.SplitSize+f.FrameHeight+f.PaddingY+f.LineStep+f.LineHeight/2;Assert.True(outlinerY>f.MarkerMax.Y,"Outliner target must be below the marker, unconditionally.");f.Host.Drawing=false;f.Host.Click(f.Workspace.Draw,f.MarkerWindowPos.X+32,outlinerY);Assert.Equal("/items/0",f.Window.Editor.Primary);Same(f,a);}
    [Fact] public void Cross_file_Back_button_really_returns_to_source_without_editing()
    {using var f=new Fixture();f.Window.Editor.Select("/items/1");var a=Before(f);var b=Before(f,"b.json");Assert.True(f.Workspace.GoTo(new("b.json","/records/0")));Assert.Equal("b.json",f.Workspace.Active!.Name);Assert.Equal("/records/0",f.Window.Editor.Primary);f.Host.Drawing=true;f.Host.Frames(f.DrawTable);var text=Screen(f,f.DrawTable);Assert.Contains("[ Back ]",text);Assert.True(Colour(f,ArrowColour,f.ControlMin,f.ControlMax)>100);var bounds=BackButtonPixels(f);f.Host.Drawing=false;f.Host.Click(f.DrawTable,(bounds.Min.X+bounds.Max.X)/2,(bounds.Min.Y+bounds.Max.Y)/2);Assert.Equal("a.json",f.Workspace.Active!.Name);Assert.Equal("/items/1",f.Window.Editor.Primary);Same(f,a);Same(f,b,"b.json");}

}
