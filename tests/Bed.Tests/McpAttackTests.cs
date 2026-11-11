using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Agent;
using Bed.Data;
using Xunit;
using Xunit.Abstractions;
using Cli=Bed.Agent.Cli.Program;

namespace Bed.Tests;

// INVENTED neutral project, malformed messages and recovery schedules. BORROWED: JSON-RPC 2.0,
// the locally pinned MCP 2025-06-18 stdio contract and the owner's A207 error policies. No network.
public sealed class McpAttackTests(ITestOutputHelper output)
{
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
    private static string Hash(string text)=>Hash(Encoding.UTF8.GetBytes(text));
    private static JsonObject Rpc(string method,int id=101,JsonNode? parameters=null)
    {var r=new JsonObject{["jsonrpc"]="2.0",["id"]=id,["method"]=method};if(parameters is not null)r["params"]=parameters;return r;}
    private static JsonObject Args(string tool,JsonNode? arguments,int id=101)=>Rpc("tools/call",id,new JsonObject{["name"]=tool,["arguments"]=arguments});
    private static JsonObject SetArgs(int amount=8)=>new(){["file"]="rows.json",["at"]="/items/0/amount",["value"]=amount.ToString(System.Globalization.CultureInfo.InvariantCulture)};
    private sealed class Note(string name)
    {public string Name{get;}=name;public string Phase{get;set;}="starting";public Dictionary<string,object?> Values{get;}=[];public List<string> Faults{get;}=[];}
    private void Write(Note n)
    {var text=JsonSerializer.Serialize(n);output.WriteLine(text);if(Environment.GetEnvironmentVariable("A207_OBSERVATIONS") is {Length:>0} d){Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,n.Name+".json"),text);}}
    private void Proof(string name,Action<Note> action)
    {var n=new Note(name);Write(n);try{action(n);}catch(Exception e){n.Faults.Add(e.ToString());}n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));}
    private static void Need(Note n,bool condition,string problem){if(!condition)n.Faults.Add(problem);}
    private sealed class Fixture:IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"mcp-attack-"+Guid.NewGuid().ToString("N"));
        internal string Data=>Path.Combine(Root,"data");internal string Schemas=>Path.Combine(Root,"schemas");internal string FilePath=>Path.Combine(Data,"rows.json");
        internal readonly AgentSession Agent;internal readonly McpServer Server;internal DocumentSession<DataDocument> Session=>Agent.Project.Find("rows.json")!.Session!;
        internal Fixture(bool readOnly=false)
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);File.WriteAllText(FilePath,"""{"items":[{"id":"a","name":"Alpha","amount":3},{"id":"b","name":"Beta","amount":4}]}""",new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Schemas,"rows.schema.json"),"""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","required":["id","name","amount"],"additionalProperties":false,"properties":{"id":{"type":"string"},"name":{"type":"string"},"amount":{"type":"integer","minimum":0,"maximum":100}}}}}}""");
            Agent=new AgentSession(DataProject.Open(Data,Schemas,readOnly));Assert.True(Agent.Project.Find("rows.json")!.Open());Server=new McpServer(Agent,"neutral-proof");
        }
        internal string[] Entries()=>Directory.EnumerateDirectories(Root,"*",SearchOption.AllDirectories).Select(p=>"D:"+Path.GetRelativePath(Root,p)).Concat(Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories).Select(p=>"F:"+Path.GetRelativePath(Root,p))).Order(StringComparer.Ordinal).ToArray();
        internal string Disk()=>Hash(string.Join("\n",Entries())+"\n"+string.Join("\n",Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p=>Path.GetRelativePath(Root,p)+":"+Hash(File.ReadAllBytes(p)))));
        internal string Draft()=>Hash(Session.Current.Root.GetRawText()+"\n"+Session.Revision+"\n"+Session.Journal.Count+"\n"+Session.History.UndoCount);
        public void Dispose()
        {foreach(var check in Agent.Checks.Registered){var r=Agent.Checks.Last(check.Id);if(r is not null){if(!r.IsOver)r.Cancel();Assert.True(r.Worker.Wait(TimeSpan.FromSeconds(10)));}}var boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;Assert.StartsWith(boundary,Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    private sealed class ReplyWriter: StringWriter
    {private readonly Stopwatch watch=Stopwatch.StartNew();internal readonly List<double> At=[];public override void WriteLine(string? value){At.Add(watch.Elapsed.TotalMilliseconds);base.WriteLine(value);}}
    private static List<JsonObject> ParseReplies(string raw,Note n)
    {
        var result=new List<JsonObject>();foreach(var line in raw.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {try{if(JsonNode.Parse(line) is not JsonObject o){n.Faults.Add("Response line isn't a JSON object.");continue;}result.Add(o);Need(n,o["jsonrpc"]?.ToJsonString()=="\"2.0\""&&o.ContainsKey("id"),"Response lacks JSON-RPC 2.0/id.");Need(n,o.ContainsKey("result")!=o.ContainsKey("error"),"Response has both/neither result and error.");if(o.ContainsKey("error"))Need(n,o["error"] is JsonObject e&&e["code"] is JsonValue code&&code.TryGetValue<int>(out _)&&e["message"] is JsonValue message&&message.TryGetValue<string>(out var m)&&!string.IsNullOrWhiteSpace(m),"RPC error lacks integer code/nonempty message.");if(o["id"] is { } id){using var d=JsonDocument.Parse(id.ToJsonString());Need(n,d.RootElement.ValueKind is JsonValueKind.String or JsonValueKind.Number,"Response id is not a string/number/null.");}}
         catch(Exception e){n.Faults.Add("Response cannot be parsed: "+e.Message);}}
        return result;
    }
    private static bool Id(JsonObject r,int id)=>r["id"]?.ToJsonString()==id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static int? ErrorCode(JsonObject r)=>r["error"] is JsonObject e&&e["code"] is JsonValue v&&v.TryGetValue<int>(out var code)?code:null;
    private static JsonObject? Payload(JsonObject r)=>r["result"] is JsonObject p&&p["content"] is JsonArray a&&a.FirstOrDefault()?["text"] is JsonValue text&&text.TryGetValue<string>(out var s)?JsonNode.Parse(s) as JsonObject:null;
    public static IEnumerable<object[]> Cases=>new[]{"invalid_json","empty_line","whitespace","split_lines","eof_mid_request","line_10mb","missing_version","wrong_version","missing_version_edit","wrong_version_edit","notification_ping","notification_initialized","notification_unknown","notification_edit","invalid_no_id","object_id","bool_id","batch_array","scalar_root","null_root","missing_method","method_number","unknown_method","unknown_tool","missing_argument","wrong_argument_type","bad_pointer","schema_refusal","readonly_save","missing_check","missing_tool_name","tool_name_number","params_array","arguments_array","arguments_null","valid_set_save","valid_preview","list_tools"}.Select(s=>new object[]{s});
    [Theory,MemberData(nameof(Cases))]
    public void Hostile_lines_answer_correctly_preserve_data_and_keep_serving(string kind)=>Proof(kind,n=>
    {
        using var f=new Fixture(kind=="readonly_save");if(kind=="readonly_save")Assert.True(f.Agent.Set("rows.json","/items/0/amount","9")["applied"]!.GetValue<bool>());
        var beforeDisk=f.Disk();var beforeDraft=f.Draft();var beforeEntries=f.Entries();var expected=JsonNode.Parse(f.Session.Current.Root.GetRawText())!;string request;int? expectedCode=null;var expectedBadReplies=1;var toolError=false;var mayChange=kind=="valid_set_save";var saveAfter=false;var eof=kind=="eof_mid_request";
        switch(kind)
        {
            case "invalid_json":request="{bad";expectedCode=-32700;break;
            case "empty_line":request="";expectedBadReplies=0;break;
            case "whitespace":request="   \t";expectedBadReplies=0;break;
            case "split_lines":request="{\"jsonrpc\":\"2.0\",\n\"id\":101,\"method\":\"ping\"}";expectedCode=-32700;expectedBadReplies=2;break;
            case "eof_mid_request":request="{\"jsonrpc\":\"2.0\",\"id\":101,";expectedCode=-32700;break;
            case "line_10mb":var q=Rpc("ping");q["padding"]="";var size=q.ToJsonString().Length;q["padding"]=new string('x',10*1024*1024-size);request=q.ToJsonString();Assert.Equal(10*1024*1024,Encoding.UTF8.GetByteCount(request));break;
            case "missing_version":request="""{"id":101,"method":"ping"}""";expectedCode=-32600;break;
            case "wrong_version":request="""{"jsonrpc":"1.0","id":101,"method":"ping"}""";expectedCode=-32600;break;
            case "missing_version_edit":case "wrong_version_edit":var edit=Args("set",SetArgs());if(kind=="missing_version_edit")edit.Remove("jsonrpc");else edit["jsonrpc"]="1.0";request=edit.ToJsonString();expectedCode=-32600;saveAfter=true;break;
            case "notification_ping":request="""{"jsonrpc":"2.0","method":"ping"}""";expectedBadReplies=0;break;
            case "notification_initialized":request="""{"jsonrpc":"2.0","method":"notifications/initialized"}""";expectedBadReplies=0;break;
            case "notification_unknown":request="""{"jsonrpc":"2.0","method":"notifications/selfAuthored"}""";expectedBadReplies=0;break;
            case "notification_edit":var notice=Args("set",SetArgs());notice.Remove("id");request=notice.ToJsonString();expectedBadReplies=0;saveAfter=true;break;
            case "invalid_no_id":request="{}";expectedCode=-32600;break;
            case "object_id":request="""{"jsonrpc":"2.0","id":{"bad":1},"method":"ping"}""";expectedCode=-32600;break;
            case "bool_id":request="""{"jsonrpc":"2.0","id":true,"method":"ping"}""";expectedCode=-32600;break;
            case "batch_array":request="["+Rpc("ping").ToJsonString()+"]";expectedCode=-32600;break;
            case "scalar_root":request="42";expectedCode=-32600;break;
            case "null_root":request="null";expectedCode=-32600;break;
            case "missing_method":request="""{"jsonrpc":"2.0","id":101}""";expectedCode=-32600;break;
            case "method_number":request="""{"jsonrpc":"2.0","id":101,"method":7}""";expectedCode=-32600;break;
            case "unknown_method":request=Rpc("selfAuthored/unknown").ToJsonString();expectedCode=-32601;break;
            case "unknown_tool":request=Args("selfAuthored.unknown",new JsonObject()).ToJsonString();expectedCode=-32602;break;
            case "missing_argument":request=Args("get",new JsonObject{["file"]="rows.json"}).ToJsonString();toolError=true;break;
            case "wrong_argument_type":request=Args("get",new JsonObject{["file"]=7,["at"]="/items/0"}).ToJsonString();toolError=true;break;
            case "bad_pointer":request=Args("get",new JsonObject{["file"]="rows.json",["at"]="items/0"}).ToJsonString();toolError=true;break;
            case "schema_refusal":request=Args("set",SetArgs(-1)).ToJsonString();toolError=true;break;
            case "readonly_save":request=Args("save",new JsonObject{["file"]="rows.json"}).ToJsonString();toolError=true;break;
            case "missing_check":request=Args("check",new JsonObject{["id"]="no.such.check"}).ToJsonString();toolError=true;break;
            case "missing_tool_name":request=Rpc("tools/call",parameters:new JsonObject{["arguments"]=new JsonObject()}).ToJsonString();expectedCode=-32602;break;
            case "tool_name_number":request=Rpc("tools/call",parameters:new JsonObject{["name"]=7,["arguments"]=new JsonObject()}).ToJsonString();expectedCode=-32602;break;
            case "params_array":request=Rpc("tools/call",parameters:new JsonArray()).ToJsonString();expectedCode=-32602;break;
            case "arguments_array":request=Args("describe",new JsonArray()).ToJsonString();expectedCode=-32602;break;
            case "arguments_null":request=Args("describe",null).ToJsonString();expectedCode=-32602;break;
            case "valid_set_save":request=Args("set",SetArgs()).ToJsonString();saveAfter=true;break;
            case "valid_preview":var preview=SetArgs();preview["preview"]=true;request=Args("set",preview).ToJsonString();break;
            default:request=Rpc("tools/list").ToJsonString();break;
        }
        n.Values["request"]=request.Length<4096?request:null;n.Values["requestUtf8Bytes"]=Encoding.UTF8.GetByteCount(request);n.Values["requestHash"]=Hash(request);n.Values["expectedCode"]=expectedCode;n.Values["expectedHostileReplies"]=expectedBadReplies;n.Phase="serving";Write(n);
        var input=request+(eof?"":"\n"+(saveAfter?Args("save",new JsonObject{["file"]="rows.json"},202).ToJsonString()+"\n":"")+Rpc("ping",9001).ToJsonString()+"\n");using var writer=new ReplyWriter();Exception? escaped=null;
        try{f.Server.Run(new StringReader(input),writer);}catch(Exception e){escaped=e;n.Values["escaped"]=e.ToString();n.Faults.Add("Run let a request exception escape: "+e.GetType().Name+": "+e.Message);}
        var raw=writer.ToString();n.Values["rawResponses"]=raw;n.Values["responseAtMs"]=writer.At;var replies=ParseReplies(raw,n);var hostile=replies.Where(r=>!Id(r,9001)&&!Id(r,202)).ToArray();Need(n,hostile.Length==expectedBadReplies,"Wrong number of hostile-message answers: "+hostile.Length+" versus "+expectedBadReplies);
        foreach(var r in hostile)
        {
            if(expectedCode is { } code){Need(n,ErrorCode(r)==code,"Expected JSON-RPC error "+code+", got "+r.ToJsonString());if(code==-32700||kind is "object_id" or "bool_id" or "scalar_root" or "null_root" or "batch_array" or "invalid_no_id")Need(n,r["id"] is null,"Malformed/undetectable id was echoed rather than null.");}
            else if(toolError){Need(n,r["result"]?["isError"]?.ToJsonString()=="true","Known-tool refusal lacks result.isError true.");Need(n,Payload(r) is { } p&&(p.ContainsKey("error")||p["refused"] is JsonArray {Count:>0}),"Tool refusal has no structured reason.");}
            else Need(n,r["result"] is not null&&r["error"] is null,"Valid control did not return a result.");
        }
        if(!eof){var after=replies.SingleOrDefault(r=>Id(r,9001));n.Values["sameRunAfterAnswered"]=after is not null;Need(n,after?["result"] is JsonObject,"The next valid ping was lost from the same Run.");}
        using var recovery=new StringWriter();f.Server.Run(new StringReader(Rpc("ping",9002).ToJsonString()+"\n"),recovery);n.Values["recoveryResponses"]=recovery.ToString();Need(n,ParseReplies(recovery.ToString(),n).Any(r=>Id(r,9002)&&r["result"] is not null),"The same server/session no longer answers on a fresh input stream.");
        var changed=f.Disk()!=beforeDisk;var draftChanged=f.Draft()!=beforeDraft;n.Values["diskChanged"]=changed;n.Values["draftChanged"]=draftChanged;n.Values["beforeDiskHash"]=beforeDisk;n.Values["afterDiskHash"]=f.Disk();n.Values["amount"]=f.Session.Current.At("/items/0/amount")!.Value.GetInt32();Need(n,changed==mayChange,"Disk change differs from valid-call authorization.");Need(n,draftChanged==mayChange,"Draft changed without the intended valid edit.");if(mayChange){expected["items"]![0]!["amount"]=8;Need(n,JsonNode.DeepEquals(expected,JsonNode.Parse(f.Session.Current.Root.GetRawText())),"Valid edit changed an unrelated field.");Need(n,beforeEntries.SequenceEqual(f.Entries()),"Valid edit/save added or removed a project entry.");Need(n,JsonNode.DeepEquals(expected,JsonNode.Parse(File.ReadAllText(f.FilePath))),"Valid save changed an unrelated disk field.");Need(n,f.Session.Current.At("/items/0/amount")!.Value.GetInt32()==8,"Valid saved control has wrong value.");using var disk=JsonDocument.Parse(File.ReadAllBytes(f.FilePath));Need(n,disk.RootElement.GetProperty("items")[0].GetProperty("amount").GetInt32()==8,"Valid save did not reach disk.");}n.Values["escapedType"]=escaped?.GetType().FullName;
    });
    [Fact]
    public void Ten_thousand_requests_all_answer_with_their_own_id()=>Proof("load_10000",n=>
    {using var f=new Fixture();var disk=f.Disk();var draft=f.Draft();var input=string.Join("\n",Enumerable.Range(0,10000).Select(i=>Rpc("ping",i).ToJsonString()))+"\n";using var writer=new ReplyWriter();var timer=Stopwatch.StartNew();f.Server.Run(new StringReader(input),writer);n.Values["elapsedMs"]=timer.Elapsed.TotalMilliseconds;n.Values["rawResponses"]=writer.ToString();var replies=ParseReplies(writer.ToString(),n);Need(n,replies.Count==10000,"Request load lost or duplicated a response.");Need(n,replies.Select(r=>r["id"]?.ToJsonString()).SequenceEqual(Enumerable.Range(0,10000).Select(i=>i.ToString(System.Globalization.CultureInfo.InvariantCulture))),"Request load mixed response ids.");Need(n,replies.All(r=>r["result"] is JsonObject),"Ping load has non-result answers.");n.Values["requests"]=10000;n.Values["diskChanged"]=f.Disk()!=disk;n.Values["draftChanged"]=f.Draft()!=draft;Need(n,f.Disk()==disk&&f.Draft()==draft,"Ping load changed data.");});
    private sealed class SlowCheck:ICheck
    {public CheckDefinition Definition{get;}=new("proof.slow","Controlled slow check","Self-authored 500 ms check",TimeSpan.FromSeconds(3));public async Task<CheckReport> RunAsync(CheckSnapshot snapshot,IProgress<CheckProgress> progress,CancellationToken cancel){progress.Report(new(.5,"waiting in owned fixture"));await Task.Delay(500,cancel);return new(true,[],"controlled slow check completed");}}
    [Fact]
    public void A_long_check_is_bounded_and_following_requests_answer()=>Proof("long_check_then_ping",n=>
    {using var f=new Fixture();f.Agent.Checks.Add(new SlowCheck());var disk=f.Disk();var draft=f.Draft();var request=Args("check",new JsonObject{["id"]="proof.slow"});var input=request.ToJsonString()+"\n"+Rpc("ping",9001).ToJsonString()+"\n"+Rpc("ping",9002).ToJsonString()+"\n";using var writer=new ReplyWriter();var timer=Stopwatch.StartNew();f.Server.Run(new StringReader(input),writer);n.Values["elapsedMs"]=timer.Elapsed.TotalMilliseconds;n.Values["rawResponses"]=writer.ToString();n.Values["responseAtMs"]=writer.At;var replies=ParseReplies(writer.ToString(),n);Need(n,replies.Count==3&&Id(replies[0],101)&&Id(replies[1],9001)&&Id(replies[2],9002),"Queued requests were lost/mixed after the check.");Need(n,Payload(replies[0])?["state"]?.ToJsonString()=="\"Completed\"","Controlled check did not complete.");Need(n,timer.Elapsed<TimeSpan.FromSeconds(10),"Controlled check exceeded its explicit completion budget.");n.Values["diskChanged"]=f.Disk()!=disk;n.Values["draftChanged"]=f.Draft()!=draft;Need(n,f.Disk()==disk&&f.Draft()==draft,"Check/ping sequence changed data.");});
    [Fact]
    public void An_unreadable_strict_utf8_reader_stops_cleanly_without_a_write()=>Proof("strict_utf8_reader",n=>
    {using var f=new Fixture();var disk=f.Disk();var draft=f.Draft();var bytes=InvalidUtf8Input();using var reader=new StreamReader(new MemoryStream(bytes),new UTF8Encoding(false,true),detectEncodingFromByteOrderMarks:false,bufferSize:128);using var writer=new StringWriter();try{f.Server.Run(reader,writer);}catch(Exception e){n.Faults.Add("Unreadable strict decoder escaped Run: "+e.GetType().Name);n.Values["escaped"]=e.ToString();}n.Values["inputBase64"]=Convert.ToBase64String(bytes);n.Values["rawResponses"]=writer.ToString();ParseReplies(writer.ToString(),n);using var recovery=new StringWriter();f.Server.Run(new StringReader(Rpc("ping",9002).ToJsonString()+"\n"),recovery);n.Values["recoveryResponses"]=recovery.ToString();Need(n,ParseReplies(recovery.ToString(),n).Any(r=>Id(r,9002)),"Session lost after unreadable decoder.");n.Values["diskChanged"]=f.Disk()!=disk;n.Values["draftChanged"]=f.Draft()!=draft;Need(n,f.Disk()==disk&&f.Draft()==draft,"Invalid strict UTF-8 changed data.");});
    private static byte[] InvalidUtf8Input()
    {
        var set=Args("set",new JsonObject{["file"]="rows.json",["at"]="/items/0/name",["value"]="\"bad INVALID_BYTES\""}).ToJsonString();var text=set+"\n"+Args("save",new JsonObject{["file"]="rows.json"},202).ToJsonString()+"\n"+Rpc("ping",9001).ToJsonString()+"\n";var at=text.IndexOf("INVALID_BYTES",StringComparison.Ordinal);return [..Encoding.UTF8.GetBytes(text[..at]),0xC3,0x28,..Encoding.UTF8.GetBytes(text[(at+"INVALID_BYTES".Length)..])];
    }
    [Fact]
    public async Task The_real_cli_refuses_invalid_utf8_line_and_keeps_serving()
    {
        var n=new Note("cli_invalid_utf8");Write(n);
        try
        {
            using var f=new Fixture();var before=f.Disk();var input=InvalidUtf8Input();var dir=new DirectoryInfo(AppContext.BaseDirectory);var repo=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));var dll=Environment.GetEnvironmentVariable("A207_CLI")??Path.Combine(repo,"src","Bed.Agent.Cli","bin",dir.Parent!.Name,dir.Name,"Bed.Agent.Cli.dll");Assert.True(File.Exists(dll),dll);
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=f.Root};foreach(var arg in new[]{dll,"--data",f.Data,"--schemas",f.Schemas,"mcp"})start.ArgumentList.Add(arg);using var p=Process.Start(start)!;var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
            try{await p.StandardInput.BaseStream.WriteAsync(input);p.StandardInput.Close();await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}
            catch{if(!p.HasExited)p.Kill(entireProcessTree:true);await p.WaitForExitAsync();throw;}
            n.Values["exit"]=p.ExitCode;n.Values["inputBase64"]=Convert.ToBase64String(input);var raw=await stdout;n.Values["rawResponses"]=raw;n.Values["stderr"]=await stderr;var replies=ParseReplies(raw,n);Need(n,p.ExitCode==0,"Real CLI did not end cleanly after finite input.");var first=replies.FirstOrDefault();Need(n,first is not null&&ErrorCode(first)==-32700&&first["id"] is null,"Invalid UTF-8 line did not receive parse error -32700/id null.");Need(n,replies.Any(r=>Id(r,9001)&&r["result"] is JsonObject),"Real CLI lost the valid ping after invalid UTF-8.");n.Values["diskChanged"]=f.Disk()!=before;n.Values["beforeDiskHash"]=before;n.Values["afterDiskHash"]=f.Disk();Need(n,f.Disk()==before,"Real CLI saved invalid UTF-8 edit bytes after permissive/code-page decoding.");
        }
        catch(Exception e){n.Faults.Add(e.ToString());}n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));
    }
}
