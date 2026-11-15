using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

public sealed class AiGuideRulesTests:IDisposable
{
    private readonly string root=Directory.CreateTempSubdirectory("bed-ai-rules-").FullName;
    private string Data=>Path.Combine(root,"data");
    private string Schemas=>Path.Combine(root,"schemas");
    private const string MapText="""{"game":"Example","labels":[],"references":[{"file":"links.json","at":"/links/*/item","target":{"file":"items.json","list":"/items","key":"id"},"evidence":"self-authored fixture"}]}""";
    public AiGuideRulesTests()
    {
        Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Data,"items.json"),"""{"items":[{"id":"one"}]}""");
        File.WriteAllText(Path.Combine(Data,"links.json"),"""{"links":[]}""");
        foreach(var name in new[]{"items","links"})File.WriteAllText(Path.Combine(Schemas,name+".schema.json"),"""{"type":"object"}""");
    }
    public void Dispose()=>Directory.Delete(root,true);
    private AgentSession Session(bool map=true)=>new(DataProject.Open(Data,Schemas),map?ReferenceMap.Read(MapText):null);
    private static void Missing(JsonObject result,string input)
    {Assert.StartsWith("couldn't check:",result["error"]!.GetValue<string>());Assert.Contains(input,result["error"]!.GetValue<string>());Assert.Null(result["count"]);Assert.Null(result["matches"]);}
    [Fact]
    public void Missing_files_locations_maps_checks_and_proposal_hosts_are_unanswered_queries()
    {
        var session=Session(false);
        Missing(session.Problems("absent.json"),"absent.json");Missing(session.Get("absent.json","/items"),"absent.json");Missing(session.Find("items.json","/absent"),"/absent");
        Missing(session.Refs("items.json","/items/0"),"reference map");Missing(session.RunCheck("absent"),"absent");Missing(session.ListProposals(),"running editor");Missing(session.ShowProposal("absent"),"running editor");
    }
    [Theory][InlineData("missing file")][InlineData("missing schema")][InlineData("broken JSON")]
    public void A_reference_query_never_claims_zero_when_a_mapped_source_cannot_be_read(string fault)
    {
        if(fault=="missing file")File.Delete(Path.Combine(Data,"links.json"));
        else if(fault=="missing schema")File.Delete(Path.Combine(Schemas,"links.schema.json"));
        else File.WriteAllText(Path.Combine(Data,"links.json"),"{");
        Missing(Session().Refs("items.json","/items/0"),"links.json");
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Unknown_scope_or_ambiguous_targets_do_not_become_a_no_references_answer(bool ambiguous)
    {
        File.WriteAllText(Path.Combine(Data,"links.json"),"""{"links":[{"item":"one"}]}""");
        var map=JsonNode.Parse(MapText)!;
        if(ambiguous)File.WriteAllText(Path.Combine(Data,"items.json"),"""{"items":[{"id":"one"},{"id":"one"}]}""");
        else map["references"]![0]!["scope"]="a context only described in words";
        var session=new AgentSession(DataProject.Open(Data,Schemas),ReferenceMap.Read(map.ToJsonString()));
        Missing(session.Refs("items.json","/items/0"),"links.json");
        var knownValue=session.Get("links.json","/links/0/item");Assert.Equal("one",knownValue["value"]!.GetValue<string>());Assert.StartsWith("couldn't check:",knownValue["link"]!["note"]!.GetValue<string>());
    }
    [Fact]
    public void A_genuine_empty_search_and_complete_reference_query_still_report_zero()
    {
        var session=Session();var found=session.Find("items.json","/items","absent");Assert.Null(found["error"]);Assert.Equal(0,found["matches"]!.GetValue<int>());
        var references=session.Refs("items.json","/items/0");Assert.Null(references["error"]);Assert.Equal(0,references["count"]!.GetValue<int>());
        var run=session.RunCheck("links.all");Assert.True(run["clean"]!.GetValue<bool>());Assert.Empty(run["findings"]!.AsArray());Assert.Contains("Other checks",run["next"]!.GetValue<string>());
        Assert.Contains("Other checks",session.Problems()["next"]!.GetValue<string>());
    }
    private static JsonObject Call(McpServer server,string tool)=>server.Handle(new JsonObject{["jsonrpc"]="2.0",["id"]=1,["method"]="tools/call",["params"]=new JsonObject{["name"]=tool,["arguments"]=new JsonObject()}}.ToJsonString())!["result"]!.AsObject();
    [Fact]
    public void Missing_or_failed_UI_hosts_report_the_missing_input_and_keep_the_connection_alive()
    {
        foreach(var server in new[]{new McpServer(Session()),new McpServer(Session(),uiSnapshot:()=>throw new IOException("frame unavailable"))})
        {
            var result=Call(server,"ui_snapshot");Assert.True(result["isError"]!.GetValue<bool>());var text=JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
            Assert.StartsWith("couldn't check:",text["error"]!.GetValue<string>());Assert.False(Call(server,"describe")["isError"]!.GetValue<bool>());
        }
    }
    [Fact]
    public void Missing_query_arguments_are_explicit_and_failed_schema_reads_name_the_missing_input()
    {
        var server=new McpServer(Session());var result=Call(server,"get");Assert.True(result["isError"]!.GetValue<bool>());
        Assert.StartsWith("couldn't check:",JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!["error"]!.GetValue<string>());
        File.Delete(Path.Combine(Schemas,"links.schema.json"));var problems=Session().Problems("links.json");Assert.Equal(1,problems["count"]!.GetValue<int>());
        Assert.StartsWith("couldn't check:",problems["problems"]![0]!["message"]!.GetValue<string>());
    }
    [Theory][InlineData("folders")][InlineData("arguments")][InlineData("map")]
    public void CLI_query_setup_and_argument_failures_name_the_missing_input(string fault)
    {
        string[] args=fault switch
        {
            "folders"=>["get"],
            "map"=>["--data",Data,"--schemas",Schemas,"--map",Path.Combine(root,"missing-map.json"),"refs","items.json","/items/0"],
            _=>["--data",Data,"--schemas",Schemas,"get"]
        };
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(2,Bed.Agent.Cli.Program.Run(args,new StringReader(""),output,errors));Assert.StartsWith("couldn't check:",errors.ToString());Assert.Equal("",output.ToString());
    }
    [Fact]
    public void Describe_names_unreadable_files_instead_of_inventing_their_contents()
    {
        File.WriteAllText(Path.Combine(Data,"links.json"),"{");var files=Session().Describe()["files"]!.AsArray();var broken=Assert.Single(files,f=>f!["file"]!.GetValue<string>()=="links.json")!;
        Assert.StartsWith("couldn't check:",broken["cantOpen"]!.GetValue<string>());Assert.Null(broken["lists"]);
    }
}
