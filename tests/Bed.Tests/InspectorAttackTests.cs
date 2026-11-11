using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui=Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// INVENTED fixtures and assertions. BORROWED measurement protocol: accepted A184, without its private data.
[Collection(ImGuiCollection.Name)]
public sealed class InspectorAttackTests(ITestOutputHelper output)
{
    static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static JsonElement J(string text)=>JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=512}).RootElement.Clone();
    sealed class Note(string name)
    {public string Name{get;}=name;public string Phase{get;set;}="starting";public Dictionary<string,object?> Values{get;}=[];public List<string> Faults{get;}=[];}
    void Write(Note n)
    {
        var json=JsonSerializer.Serialize(n,new JsonSerializerOptions{NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals});output.WriteLine(json);
        if(Environment.GetEnvironmentVariable("A209_OBSERVATIONS") is {Length:>0} path){Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,n.Name+".json"),json);}
    }
    void Proof(string name,Action<Note> act)
    {
        var n=new Note(name);Write(n);try{act(n);}catch(Exception e){n.Faults.Add(e.ToString());}n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));
    }
    static void Need(Note n,bool yes,string why){if(!yes)n.Faults.Add(why);}
    sealed class LongFactAttribute:FactAttribute
    {public LongFactAttribute(){if(Environment.GetEnvironmentVariable("A209_LONG")!="1")Skip="Set A209_LONG=1 for the large inspector/cost probes (20 warm-up,150 timed frames); no large-run or timing claim otherwise.";}}
    [System.Runtime.InteropServices.DllImport("ucrtbase",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)]static extern int _set_error_mode(int mode);
    [System.Runtime.InteropServices.DllImport("ucrtbase",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)]static extern uint _set_abort_behavior(uint flags,uint mask);
    static void NoDialogs(){if(OperatingSystem.IsWindows()){_set_error_mode(2);_set_abort_behavior(0,3);}}
    sealed record Spec(string Text,string Schema);
    static Spec Make(string shape)
    {
        var props=new JsonObject{["name"]=new JsonObject{["type"]="string"},["amount"]=new JsonObject{["type"]="integer",["minimum"]=0}};
        var item=new JsonObject{["name"]="Anchor",["amount"]=3};var defs=new JsonObject();
        switch(shape)
        {
            case "wide":for(int i=0;i<4998;i++)item["f"+i.ToString("D4")]=i;break;
            case "list":item["value"]=new JsonArray(Enumerable.Range(0,50000).Select(i=>(JsonNode?)JsonValue.Create(i)).ToArray());props["value"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="integer",["minimum"]=0}};break;
            case "oneof":case "anyof":
                item["value"]=JsonNode.Parse("""{"kind":"n","qty":3}""");props["value"]=JsonNode.Parse("{\""+(shape=="oneof"?"oneOf":"anyOf")+"\":[{\"type\":\"object\",\"required\":[\"kind\",\"qty\"],\"properties\":{\"kind\":{\"const\":\"n\"},\"qty\":{\"type\":\"integer\",\"minimum\":0}},\"additionalProperties\":false},{\"type\":\"object\",\"required\":[\"kind\",\"text\"],\"properties\":{\"kind\":{\"const\":\"s\"},\"text\":{\"type\":\"string\"}},\"additionalProperties\":false},{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}]}");break;
            case "cycle":item["value"]="cycle";props["value"]=new JsonObject{["$ref"]="#/$defs/loop"};defs["loop"]=new JsonObject{["$ref"]="#/$defs/loop",["type"]="string"};break;
            case "unknown":item["value"]=7;break;
            case "longtext":item["value"]=new string('x',300*1024)+"\u03bb";props["value"]=new JsonObject{["type"]="string"};break;
            case "huge":item["value"]=JsonNode.Parse("1e400");props["value"]=new JsonObject{["type"]="number"};break;
            case "null":item["value"]=null;props["value"]=new JsonObject{["type"]=new JsonArray("string","null")};break;
            case "enum":item["value"]="choice0000";props["value"]=new JsonObject{["enum"]=new JsonArray(Enumerable.Range(0,1000).Select(i=>(JsonNode?)JsonValue.Create("choice"+i.ToString("D4"))).ToArray())};break;
            case "deep":props["value"]=new JsonObject();break;
            case "plain":break;
            default:throw new Exception(shape);
        }
        var schema=new JsonObject{["type"]="object",["properties"]=new JsonObject{["items"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object",["properties"]=props}}},["$defs"]=defs};
        var text=shape=="deep"?"{\"items\":[{\"name\":\"Anchor\",\"amount\":3,\"value\":"+string.Concat(Enumerable.Repeat("{\"n\":",200))+"\"leaf\""+new string('}',200)+"}]}":new JsonObject{["items"]=new JsonArray(item)}.ToJsonString();
        return new(text,schema.ToJsonString());
    }
    sealed class Rig:IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"inspector-attack-"+Guid.NewGuid().ToString("N"));
        internal string Data=>Path.Combine(Root,"data");internal string Schemas=>Path.Combine(Root,"schemas");internal string FilePath=>Path.Combine(Data,"rows.json");
        internal readonly HeadlessHost Host;internal readonly DataProject Project;internal readonly DataWorkspace Workspace;internal bool Opened{get;}
        internal EditorWindow<DataDocument> Window=>Workspace.Window!;internal EditorModel<DataDocument> Editor=>Window.Editor;internal DocumentSession<DataDocument> Session=>Editor.Session;internal Inspector<DataDocument> Inspector=>Window.Inspector!;
        readonly Dictionary<string,string> original;
        internal Rig(string shape)
        {
            NoDialogs();var spec=Make(shape);Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);File.WriteAllText(FilePath,spec.Text,new UTF8Encoding(false));File.WriteAllText(Path.Combine(Schemas,"rows.schema.json"),spec.Schema,new UTF8Encoding(false));original=Bytes();
            Host=new HeadlessHost(1600,1200){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetIO().IniFilename=default;
            Project=DataProject.Open(Data,Schemas);Workspace=new DataWorkspace(Project);Opened=Workspace.Show("rows.json");
            if(Opened){foreach(var panel in Editor.Panels)Editor.SetOpen(panel.Id,false);Assert.True(Editor.Select("/items/0"));}
        }
        internal Dictionary<string,string> Bytes()=>Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>Path.GetRelativePath(Root,p),p=>Convert.ToBase64String(File.ReadAllBytes(p)));
        internal void DiskSame(Note n)=>Need(n,JsonSerializer.Serialize(Bytes())==JsonSerializer.Serialize(original),"Inspector operations changed fixture bytes/entries without Save.");
        internal void Frame()=>Host.Frame(Workspace.Draw);
        internal void Start()=>Host.Frames(Workspace.Draw,4);
        internal InspectorRow Row(string pointer)
        {
            var owner=Editor.Primary!;var rows=Inspector.Rows();InspectorRow? row=null;var here="";
            foreach(var part in Pointer.Split(pointer[owner.Length..])){here+=Pointer.Join([part]);row=rows.Single(r=>DataEditing.PlaceOfRow(r.Id)==here);rows=Inspector.Nested(row);}
            return row??throw new Exception("No row for "+pointer);
        }
        internal void Focus(string pointer,bool reveal=true)
        {
            if(reveal)Assert.True(Window.Reveal(pointer),Window.Status);Host.Frames(Workspace.Draw,3);var row=Row(pointer);Assert.True(row.Editable,"Row isn't editable: "+pointer);
            var at=Window.InspectorPlace(row.Id)??throw new Exception("Row isn't drawn: "+pointer);
            for(int i=0;i<100&&(at.Y<80||at.Y>1180);i++){Host.MoveMouse(Workspace.Draw,at.X+12,150);ImGui.GetIO().AddMouseWheelEvent(0,at.Y>1180?-3:3);Host.Frames(Workspace.Draw,2);at=Window.InspectorPlace(row.Id)??throw new Exception("Row place lost");}
            Host.Click(Workspace.Draw,at.X+12,at.Y+7);Assert.True(ImGui.GetIO().WantTextInput,"Native field wasn't focused.");Host.Press(Workspace.Draw,ImGuiKey.A,ctrl:true);Host.Press(Workspace.Draw,ImGuiKey.Backspace);
        }
        internal void Commit(string text){Host.Type(Workspace.Draw,text);Host.Press(Workspace.Draw,ImGuiKey.Enter);Host.Frames(Workspace.Draw,2);}
        internal string Screen()
        {
            var dir=Environment.GetEnvironmentVariable("A209_OBSERVATIONS")??Path.GetTempPath();Directory.CreateDirectory(dir);var path=Path.Combine(dir,"inspector-text-"+Guid.NewGuid().ToString("N")+".log");Host.Frame(()=>{ImGui.LogToFile(0,path);Workspace.Draw();ImGui.LogFinish();});return File.ReadAllText(path);
        }
        public void Dispose()
        {
            Host.Dispose();var boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;Assert.StartsWith(boundary,Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);
        }
    }
    [Theory][InlineData("oneof")][InlineData("anyof")]
    public void Union_rows_rebind_across_branch_change_undo_redo(string shape)=>Proof("union_"+shape,n=>
    {
        using var r=new Rig(shape);Assert.True(r.Opened,r.Workspace.Said);r.Start();var qty=r.Row("/items/0/value/qty");Need(n,qty.Kind==PropertyKind.Integer&&qty.Editable,"Selected numeric branch has wrong row kind.");Need(n,r.Inspector.Set(qty.Id,9L),"Numeric branch edit refused.");Need(n,r.Session.Current.At("/items/0/value/qty")!.Value.GetInt32()==9,"Wrong branch field edited.");
        var before=r.Session.Current.Root.GetRawText();Assert.True(r.Session.Execute(new SetValue("/items/0/value",J("""{"kind":"s","text":"new"}"""))));var changed=r.Session.Current.Root.GetRawText();var fields=r.Inspector.Nested(r.Inspector.Rows().Single(x=>x.Id=="value"));
        Need(n,fields.Any(x=>x.Label=="text")&&!fields.Any(x=>x.Label=="qty"),"Inspector retained the old branch shape.");Assert.True(r.Session.Undo());Need(n,r.Session.Current.Root.GetRawText()==before,"Undo lost numeric branch.");Assert.True(r.Session.Redo());Need(n,r.Session.Current.Root.GetRawText()==changed&&r.Row("/items/0/value/text").Editable,"Redo lost/rebound branch incorrectly.");n.Values["rows"]=fields;
        Assert.True(r.Session.Execute(new SetValue("/items/0/value",J("[8,9]"))));var third=r.Session.Current.Root.GetRawText();var list=r.Inspector.Rows().Single(x=>x.Id=="value");var entries=r.Inspector.Nested(list);Need(n,entries.Count==2&&entries.All(x=>x.Kind==PropertyKind.Integer),"Union list branch retained object fields or wrong item kind.");Need(n,r.Inspector.Set(entries[1].Id,10L),"Union list item edit refused.");Need(n,r.Session.Current.At("/items/0/value/0")!.Value.GetInt32()==8&&r.Session.Current.At("/items/0/value/1")!.Value.GetInt32()==10,"Union list branch edited wrong index.");Need(n,r.Session.Undo()&&r.Session.Current.Root.GetRawText()==third,"Union item undo lost list.");Need(n,r.Session.Undo()&&r.Session.Current.Root.GetRawText()==changed,"Union structure undo lost text branch.");Need(n,r.Session.Redo()&&r.Session.Current.Root.GetRawText()==third&&r.Inspector.Nested(r.Inspector.Rows().Single(x=>x.Id=="value")).Count==2,"Union structure redo retained wrong shape.");n.Values["thirdBranchRows"]=entries;n.Values["screen"]=r.Screen();r.DiskSame(n);
    });
    [Theory][InlineData("unknown")][InlineData("null")][InlineData("enum")][InlineData("cycle")][InlineData("huge")]
    public void Value_shapes_preserve_data_and_show_their_real_kind(string shape)=>Proof("value_"+shape,n=>
    {
        using var r=new Rig(shape);n.Values["opened"]=r.Opened;n.Values["openReason"]=r.Workspace.Said;
        if(shape=="cycle"&&!r.Opened){Need(n,r.Workspace.Said.Contains("rows.json",StringComparison.Ordinal)&&r.Workspace.Said.Contains("loop",StringComparison.OrdinalIgnoreCase),"Cyclic schema refusal omitted the file or cycle reason.");r.DiskSame(n);return;}
        Assert.True(r.Opened,r.Workspace.Said);r.Start();var row=r.Inspector.Rows().Single(x=>x.Id=="value");n.Values["row"]=row;
        if(shape=="unknown"){Need(n,row.Kind==PropertyKind.Number&&row.Editable,"Undescribed numeric field lost its inferred numeric kind.");Need(n,r.Inspector.Set(row.Id,9d),"Undescribed number refused.");Need(n,r.Session.Current.At("/items/0/value")!.Value.ValueKind==JsonValueKind.Number&&r.Session.Current.At("/items/0/value")!.Value.GetDouble()==9d,"Number became text or lost its edited value.");}
        if(shape=="null"){Need(n,Equals(row.Value,"null")&&!row.Editable,"Explicit null was confused with a missing editable field.");Need(n,r.Session.Current.At("/items/0/value")!.Value.ValueKind==JsonValueKind.Null,"Explicit null lost.");}
        if(shape=="enum"){Need(n,row.Choices.Count==1000&&row.Kind==PropertyKind.Choice,"Thousand-choice enum missing values/kind.");Need(n,r.Inspector.Set(row.Id,"choice0999"),"Last enum choice refused.");Need(n,r.Session.Current.At("/items/0/value")!.Value.GetString()=="choice0999","Enum edited wrong value.");}
        if(shape=="cycle")Need(n,row.Kind==PropertyKind.Text&&r.Inspector.Set(row.Id,"still safe"),"Cyclic schema didn't resolve editable text safely.");
        if(shape=="huge")Need(n,r.Session.Current.At("/items/0/value")!.Value.GetRawText()=="1e400","Finite huge number was changed on read/draw.");
        n.Values["screen"]=r.Screen();n.Values["status"]=r.Window.Status;r.DiskSame(n);
    });
    [Theory][InlineData(double.PositiveInfinity)][InlineData(double.NegativeInfinity)][InlineData(double.NaN)]
    public void Nonfinite_number_proposals_refuse_without_throw_or_mutation(double value)=>Proof("nonfinite_"+(double.IsNaN(value)?"nan":value>0?"positive":"negative"),n=>
    {
        using var r=new Rig("huge");Assert.True(r.Opened,r.Workspace.Said);var before=r.Session.Current;bool ok=false;
        try{ok=r.Inspector.Set("value",value);}catch(Exception e){n.Faults.Add("Inspector.Set let "+e.GetType().Name+" escape for nonfinite input.");n.Values["escaped"]=e.ToString();}
        Need(n,!ok&&ReferenceEquals(before,r.Session.Current)&&r.Session.Journal.Count==0,"Nonfinite proposal changed data/history.");Need(n,r.Inspector.LastIssues.Any(i=>i.ObjectId=="/items/0"&&i.Path?.Contains("value")==true),"Nonfinite refusal omitted selected field reason.");n.Values["issues"]=r.Inspector.LastIssues;n.Values["rawValue"]=r.Session.Current.At("/items/0/value")!.Value.GetRawText();r.DiskSame(n);
    });
    [Fact]
    public void Refused_native_text_stays_typed_and_points_to_the_field()=>Proof("native_refusal",n=>
    {
        using var r=new Rig("plain");Assert.True(r.Opened,r.Workspace.Said);r.Start();var before=r.Session.Current;var journal=r.Session.Journal.Count;r.Focus("/items/0/amount");r.Commit("-1");var text=r.Screen();n.Values["refusedScreen"]=text;n.Values["status"]=r.Window.Status;n.Values["issues"]=r.Inspector.LastIssues;
        Need(n,ReferenceEquals(before,r.Session.Current)&&r.Session.Journal.Count==journal,"Schema-refused typing changed the draft/history.");Need(n,Regex.IsMatch(text,@"\{\s*-1\s*\}"),"Refused -1 did not remain in the native input box.");Need(n,r.Window.Status.Contains("amount",StringComparison.OrdinalIgnoreCase)&&r.Inspector.LastIssues.Any(i=>i.ObjectId=="/items/0"&&i.Path?.Contains("amount")==true),"Refusal doesn't identify the selected item's amount.");
        r.Focus("/items/0/amount",reveal:false);r.Commit("5");Need(n,r.Session.Current.At("/items/0/amount")!.Value.GetInt32()==5,"Correction didn't replace retained refused typing.");r.DiskSame(n);
    });
    [Fact]
    public void Native_large_finite_number_input_never_crashes_or_saves_infinity()=>Proof("native_huge_input",n=>
    {
        using var r=new Rig("huge");Assert.True(r.Opened,r.Workspace.Said);r.Start();var before=r.Session.Current;r.Focus("/items/0/value");n.Phase="before native large-number commit";Write(n);
        try{r.Commit("2e400");}catch(Exception e){n.Faults.Add("Native number commit threw "+e.GetType().Name);n.Values["escaped"]=e.ToString();}
        var raw=r.Session.Current.At("/items/0/value")!.Value.GetRawText();n.Values["rawAfter"]=raw;Need(n,ReferenceEquals(before,r.Session.Current)||raw=="2e400","Large finite numeric typing became an inexact/nonfinite value.");
        if(!n.Values.ContainsKey("escaped")&&ReferenceEquals(before,r.Session.Current)){var screen=r.Screen();n.Values["screen"]=screen;n.Values["status"]=r.Window.Status;n.Values["issues"]=r.Inspector.LastIssues;Need(n,Regex.IsMatch(screen,@"\{\s*2e400\s*\}")&&r.Window.Status.Contains("value",StringComparison.OrdinalIgnoreCase),"Refused huge input lost typed text or field reason.");}r.DiskSame(n);
    });
    [Fact]
    public void Depth_200_file_boundary_is_explicit_and_preserves_bytes()=>Proof("depth200_file",n=>
    {
        using var r=new Rig("deep");n.Values["opened"]=r.Opened;n.Values["person"]=r.Workspace.Said;
        if(r.Opened){r.Start();Need(n,r.Window.Reveal("/items/0/value"+string.Concat(Enumerable.Repeat("/n",200))),"Cannot reveal depth200 leaf.");n.Values["screen"]=r.Screen();}
        else Need(n,r.Workspace.Said.Contains("rows.json",StringComparison.Ordinal)&&(r.Workspace.Said.Contains("depth",StringComparison.OrdinalIgnoreCase)||r.Workspace.Said.Contains("nest",StringComparison.OrdinalIgnoreCase)),"Depth refusal doesn't name the file and boundary.");r.DiskSame(n);
    });
    [Fact]
    public void Depth_200_public_in_memory_edit_has_no_unhandled_exception()=>Proof("depth200_memory",n=>
    {
        var spec=Make("deep");var schemas=new JsonSchemas().Add("rows.schema.json",spec.Schema);var root=J(spec.Text);var doc=new DataDocument(root){Source=root};DocumentSession<DataDocument> session;
        try{session=DataKit.Session(doc,schemas,"rows.schema.json");}
        catch(InvalidDocumentException e){n.Values["refusedAtSessionBoundary"]=true;n.Values["issues"]=e.Issues;n.Values["reason"]=e.Message;Need(n,e.Issues.Any(i=>i.Path=="$"&&i.Message.Contains("deep",StringComparison.OrdinalIgnoreCase)&&i.Message.Contains("64",StringComparison.Ordinal)),"In-memory depth refusal omitted root/depth boundary.");Need(n,doc.Root.GetRawText()==spec.Text,"Refused session creation changed the original document.");return;}
        var editor=new EditorModel<DataDocument>(session,DataEditing.OutlineFor(schemas,"rows.schema.json"));var inspector=new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas,"rows.schema.json"));Assert.True(editor.Select("/items/0"));
        var before=session.Current;n.Values["rows"]=inspector.Rows();bool ok=false;
        try{ok=inspector.Set("name","Edited");}catch(Exception e){n.Faults.Add("Depth200 in-memory inspector edit threw "+e.GetType().Name);n.Values["escaped"]=e.ToString();}
        n.Values["applied"]=ok;n.Values["issues"]=inspector.LastIssues;if(!ok&&n.Faults.Count==0)Need(n,inspector.LastIssues.Count>0,"Refused depth200 edit gave no reason.");Need(n,ok?session.Current.At("/items/0/name")!.Value.GetString()=="Edited":ReferenceEquals(before,session.Current),"Depth edit lost state or changed wrong place.");Need(n,!ok||JsonSchemas.Same(before.At("/items/0/value")!.Value,session.Current.At("/items/0/value")!.Value),"Depth edit lost a nested value.");
    });
    [LongFact]
    public void Long_text_uses_the_size_fallback_without_value_loss()=>Proof("long_text",n=>
    {
        using var r=new Rig("longtext");Assert.True(r.Opened,r.Workspace.Said);r.Start();var before=r.Session.Current.At("/items/0/value")!.Value.GetString();var screen=r.Screen();n.Values["screen"]=screen;n.Values["utf8Bytes"]=Encoding.UTF8.GetByteCount(before!);Need(n,screen.Contains("Text panel",StringComparison.Ordinal),"Text over MostInBox wasn't redirected safely.");r.Focus("/items/0/name");r.Commit("Name changed");Need(n,r.Session.Current.At("/items/0/value")!.Value.GetString()==before,"Editing another field lost long text.");r.DiskSame(n);
    });
    [LongFact]
    public void Wide_object_exposes_every_field_and_edits_only_its_target()=>Proof("wide_fields",n=>
    {
        using var r=new Rig("wide");Assert.True(r.Opened,r.Workspace.Said);var rows=r.Inspector.Rows();Need(n,rows.Count==5000,"Wide object didn't expose all5000 fields.");var before=r.Session.Current;Need(n,r.Inspector.Set("f4997",123456d),"Last wide field refused.");foreach(var prop in before.At("/items/0")!.Value.EnumerateObject())Need(n,prop.Name=="f4997"?r.Session.Current.At("/items/0/f4997")!.Value.GetInt32()==123456:JsonSchemas.Same(prop.Value,r.Session.Current.At("/items/0/"+prop.Name)!.Value),"Wide edit changed wrong field: "+prop.Name);n.Values["rowCount"]=rows.Count;r.Start();n.Values["screen"]=r.Screen();r.DiskSame(n);
    });
    [LongFact]
    public void Long_list_add_remove_and_structure_undo_redo_are_exact()=>Proof("list_structure",n=>
    {
        using var r=new Rig("list");Assert.True(r.Opened,r.Workspace.Said);var before=r.Session.Current.Root.GetRawText();var list=r.Inspector.Rows().Single(x=>x.Id=="value");Need(n,r.Inspector.AddItem(list),"Long-list add failed.");var afterAdd=r.Session.Current.Root.GetRawText();var arr=r.Session.Current.At("/items/0/value")!.Value;Need(n,arr.GetArrayLength()==50001&&arr.EnumerateArray().Take(50000).Select(x=>x.GetInt32()).SequenceEqual(Enumerable.Range(0,50000))&&arr[50000].GetInt32()==49999,"Add changed old list values or wrong appended value.");
        var row=r.Inspector.Nested(r.Inspector.Rows().Single(x=>x.Id=="value"))[12345];Need(n,r.Inspector.RemoveItem(row),"Long-list remove failed.");var afterRemove=r.Session.Current.Root.GetRawText();arr=r.Session.Current.At("/items/0/value")!.Value;Need(n,arr.EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(Enumerable.Range(0,50000).Where(i=>i!=12345).Append(49999)),"Remove changed wrong index or shifted data incorrectly.");
        Need(n,r.Session.Undo()&&r.Session.Current.Root.GetRawText()==afterAdd,"Undo remove lost list shape.");Need(n,r.Session.Undo()&&r.Session.Current.Root.GetRawText()==before,"Undo add lost original.");Need(n,r.Session.Redo()&&r.Session.Current.Root.GetRawText()==afterAdd,"Redo add differs.");Need(n,r.Session.Redo()&&r.Session.Current.Root.GetRawText()==afterRemove,"Redo remove differs.");
        row=r.Inspector.Nested(r.Inspector.Rows().Single(x=>x.Id=="value"))[12345];Need(n,r.Inspector.Set(row.Id,777L),"Moved index edit refused.");Need(n,r.Session.Current.At("/items/0/value/12345")!.Value.GetInt32()==777&&r.Session.Current.At("/items/0/value/12344")!.Value.GetInt32()==12344&&r.Session.Current.At("/items/0/value/12346")!.Value.GetInt32()==12347,"Row cache edited wrong post-structure field.");var edited=r.Session.Current.Root.GetRawText();Need(n,r.Session.Undo()&&r.Session.Current.Root.GetRawText()==afterRemove,"Undo post-shape edit lost data.");Need(n,r.Session.Redo()&&r.Session.Current.Root.GetRawText()==edited,"Redo post-shape edit lost exact data.");n.Values["beforeHash"]=Hash(before);n.Values["afterHash"]=Hash(r.Session.Current.Root.GetRawText());n.Values["undoCount"]=r.Session.History.UndoCount;r.Start();Need(n,r.Window.Reveal("/items/0/value/12345"),"Changed list index cannot be revealed.");r.Host.Frames(r.Workspace.Draw,3);n.Values["screen"]=r.Screen();n.Values["changedRow"]=r.Row("/items/0/value/12345");n.Values["status"]=r.Window.Status;r.DiskSame(n);
    });
    void Cost(string shape)=>Proof("cost_"+shape+"_"+(Environment.GetEnvironmentVariable("A209_PHASE")??"first"),n=>
    {
        using var r=new Rig(shape);n.Values["shape"]=shape;n.Values["opened"]=r.Opened;
        if(!r.Opened){n.Values["blockedBy"]=r.Workspace.Said;n.Values["idleAndKeystrokeCost"]="N/A: file did not reach the inspector";r.DiskSame(n);return;}
        r.Start();if(shape=="list")Assert.True(r.Window.Reveal("/items/0/value/49999"),r.Window.Status);r.Host.Frames(r.Workspace.Draw,3);
        var first=r.Session.Current;for(int i=0;i<20;i++)r.Frame();var times=new double[150];var alloc=new long[150];
        n.Phase="before150 timed frames";Write(n);
        for(int i=0;i<150;i++){var a=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();r.Frame();times[i]=Stopwatch.GetElapsedTime(t).TotalMilliseconds;alloc[i]=GC.GetAllocatedBytesForCurrentThread()-a;}
        var sorted=times.Order().ToArray();var bytes=alloc.Order().ToArray();n.Values["idleMs"]=times;n.Values["idleThreadBytes"]=alloc;n.Values["medianMs"]=sorted[75];n.Values["p95Ms"]=sorted[142];n.Values["maxMs"]=sorted[^1];n.Values["medianBytes"]=bytes[75];n.Values["p95Bytes"]=bytes[142];n.Values["diagnosticOver60HzBudget"]=sorted[75]>1000d/60;
        Need(n,ReferenceEquals(first,r.Session.Current),"Idle frames mutated document.");r.Focus("/items/0/name");var before=r.Session.Current;var aKey=GC.GetAllocatedBytesForCurrentThread();var tKey=Stopwatch.GetTimestamp();r.Commit("Anchorx");n.Values["keystrokeMs"]=Stopwatch.GetElapsedTime(tKey).TotalMilliseconds;n.Values["keystrokeThreadBytes"]=GC.GetAllocatedBytesForCurrentThread()-aKey;
        Need(n,r.Session.Current.At("/items/0/name")!.Value.GetString()=="Anchorx","Measured native text event failed to commit its intended field.");foreach(var prop in before.At("/items/0")!.Value.EnumerateObject().Where(p=>p.Name!="name"))Need(n,JsonSchemas.Same(prop.Value,r.Session.Current.At("/items/0/"+prop.Name)!.Value),"Keystroke changed another field: "+prop.Name);
        var changed=r.Session.Current.Root.GetRawText();Need(n,r.Session.Undo()&&JsonSchemas.Same(before.Root,r.Session.Current.Root),"Measured edit cannot undo exactly.");Need(n,r.Session.Redo()&&r.Session.Current.Root.GetRawText()==changed,"Measured edit cannot redo exactly.");n.Values["screen"]=r.Screen();n.Values["status"]=r.Window.Status;n.Values["fieldsHeld"]=r.Window.FieldsHeld;n.Values["runtime"]=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;n.Values["os"]=System.Runtime.InteropServices.RuntimeInformation.OSDescription;n.Values["processors"]=Environment.ProcessorCount;r.DiskSame(n);
    });
    [LongFact]public void Cost_deep()=>Cost("deep");
    [LongFact]public void Cost_wide()=>Cost("wide");
    [LongFact]public void Cost_list()=>Cost("list");
    [LongFact]public void Cost_oneof()=>Cost("oneof");
    [LongFact]public void Cost_anyof()=>Cost("anyof");
    [LongFact]public void Cost_cycle()=>Cost("cycle");
    [LongFact]public void Cost_unknown()=>Cost("unknown");
    [LongFact]public void Cost_longtext()=>Cost("longtext");
    [LongFact]public void Cost_huge()=>Cost("huge");
    [LongFact]public void Cost_null()=>Cost("null");
    [LongFact]public void Cost_enum()=>Cost("enum");
}
