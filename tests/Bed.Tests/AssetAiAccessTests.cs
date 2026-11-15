using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class AssetAiAccessTests : IDisposable
{
    private readonly string root=Directory.CreateTempSubdirectory("bed-ai-permission-").FullName;
    private string Data=>Path.Combine(root,"data");
    private string Schemas=>Path.Combine(root,"schemas");
    private string Asset=>Path.Combine(root,"assets","private.txt");
    public AssetAiAccessTests()
    {
        Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);Directory.CreateDirectory(Path.GetDirectoryName(Asset)!);
        File.WriteAllText(Path.Combine(Data,"items.json"),"""{"items":[{"name":"PRIVATE_SENTINEL"}]}""");
        File.WriteAllText(Asset,"PRIVATE_ASSET_SENTINEL");
        File.WriteAllText(Path.Combine(Schemas,"items.schema.json"),"""{"type":"object"}""");
    }
    private void Mark(AiUse permission,string? asset=null)
    {
        asset??=Asset;
        using var settings=JsonDocument.Parse("{}");
        var record=new AssetRecord(new string('a',32),Path.GetFileName(asset),new string('b',64),"test",1,settings.RootElement.Clone(),new("fixture","self-authored","test"),permission);
        File.WriteAllText(AssetRecord.SidecarFor(asset),record.Write());
    }
    private AgentSession Session()=>new(DataProject.Open(Data,Schemas));
    private static void Refused(JsonObject result,string word)
    {
        Assert.Contains(word,result.ToJsonString(),StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTINEL",result.ToJsonString());Assert.NotNull(result["error"]);
    }
    [Theory]
    [InlineData(AiUse.Forbidden,"forbidden")]
    [InlineData(AiUse.Unknown,"unknown")]
    public void Every_agent_operation_refuses_before_reads_checks_proposals_or_edits(AiUse permission,string word)
    {
        Mark(permission);var session=Session();var before=File.ReadAllText(Path.Combine(Data,"items.json"));
        var operations=typeof(AgentSession).GetMethods(BindingFlags.Public|BindingFlags.Instance)
            .Where(m=>m.ReturnType==typeof(JsonObject)).ToArray();
        Assert.Equal(24,operations.Length);
        foreach(var operation in operations)
        {
            var args=operation.GetParameters().Select(p=>p.HasDefaultValue?p.DefaultValue:p.ParameterType==typeof(string)?"items.json":p.ParameterType==typeof(long)?(object)0L:p.ParameterType==typeof(int)?(object)0:throw new InvalidOperationException(p.Name)).ToArray();
            Refused((JsonObject)operation.Invoke(session,args)!,word);
        }
        Assert.Equal(before,File.ReadAllText(Path.Combine(Data,"items.json")));
    }
    [Fact]
    public void Explicitly_allowed_assets_preserve_queries_and_permissions_are_reread()
    {
        Mark(AiUse.Allowed);var session=Session();Assert.Contains("PRIVATE_SENTINEL",session.Get("items.json","").ToJsonString());
        Mark(AiUse.Forbidden);Refused(session.Get("items.json",""),"forbidden");
        Mark(AiUse.Allowed);Assert.Null(session.Get("items.json","")["error"]);
    }
    [Fact]
    public void Separate_asset_folder_schema_and_direct_file_records_are_checked()
    {
        var outside=Directory.CreateDirectory(Path.Combine(root,"outside")).FullName;
        var asset=Path.Combine(outside,"image.png");File.WriteAllText(asset,"not an image");Mark(AiUse.Forbidden,asset);
        var session=Session().UseAssets(outside);Refused(session.Describe(),"forbidden");
        Refused(AssetAiAccess.Refusal(asset)!,"forbidden");
        Mark(AiUse.Allowed,asset);Mark(AiUse.Unknown,Path.Combine(Schemas,"private.schema.json"));Refused(Session().Describe(),"unknown");
    }
    [Fact]
    public void Malformed_record_refuses_without_echoing_its_contents()
    {
        File.WriteAllText(AssetRecord.SidecarFor(Asset),"PRIVATE_SENTINEL");Refused(Session().Describe(),"unknown");
    }
    private static JsonObject Call(McpServer server,string name)=>server.Handle(new JsonObject
    { ["jsonrpc"]="2.0",["id"]=1,["method"]="tools/call",["params"]=new JsonObject{["name"]=name,["arguments"]=new JsonObject()}}.ToJsonString())!["result"]!.AsObject();
    [Theory]
    [InlineData(AiUse.Forbidden,"forbidden")]
    [InlineData(AiUse.Unknown,"unknown")]
    public void Mcp_never_calls_ui_build_or_observation_providers_when_permission_is_missing(AiUse permission,string word)
    {
        Mark(permission);var calls=0;
        JsonObject Provider(){calls++;return new(){["imageData"]="PRIVATE_SENTINEL"};}
        var server=new McpServer(Session(),"test",Provider,Provider){PlayObservation=(_,_)=>Provider()};
        foreach(var tool in new[]{"ui_snapshot","build","play_start","play_screenshot","play_hold_key","play_stop","get","find","check","proposal_show"})
        {var reply=Call(server,tool);Assert.True(reply["isError"]!.GetValue<bool>());Assert.Contains(word,reply.ToJsonString());Assert.DoesNotContain("SENTINEL",reply.ToJsonString());Assert.Single(reply["content"]!.AsArray());}
        Assert.Equal(0,calls);
    }
    [Fact]
    public void Provider_permission_change_discards_the_entire_reply_including_images()
    {
        Mark(AiUse.Allowed);
        var server=new McpServer(Session(),"test",()=>{Mark(AiUse.Forbidden);return new(){["secret"]="PRIVATE_SENTINEL"};},null);
        var reply=Call(server,"ui_snapshot");Assert.True(reply["isError"]!.GetValue<bool>());Assert.DoesNotContain("SENTINEL",reply.ToJsonString());
    }
    [Theory]
    [InlineData("get")][InlineData("find")][InlineData("describe")][InlineData("problems")]
    [InlineData("refs")][InlineData("check")][InlineData("scene")][InlineData("build")][InlineData("ui")][InlineData("import")]
    public void Cli_refuses_before_map_adapter_or_source_file_diagnostics(string command)
    {
        Mark(AiUse.Forbidden);using var output=new StringWriter();using var errors=new StringWriter();
        var code=Bed.Agent.Cli.Program.Run(["--data",Data,"--schemas",Schemas,command],TextReader.Null,output,errors);
        Assert.NotEqual(0,code);Assert.Contains("forbidden",output+errors.ToString());Assert.DoesNotContain("SENTINEL",output+errors.ToString());
    }
    [Theory]
    [InlineData(AiUse.Forbidden,"forbidden")][InlineData(AiUse.Unknown,"unknown")]
    public void Observation_refuses_before_starting_a_process_or_reading_its_output(AiUse permission,string word)
    {
        Mark(permission);using var observation=new PlayObservation();
        var launch=new PlayLaunch(new(Path.Combine(root,"missing.exe"),root,[]),null,root,[]);
        Assert.Contains(word,Assert.Throws<InvalidOperationException>(()=>observation.Start(launch,new(Path.Combine(root,"settings")),false)).Message);
    }
    [Fact]
    public void Permission_change_during_a_failed_provider_also_hides_its_diagnostic()
    {
        Mark(AiUse.Allowed);
        Refused(AssetAiAccess.Guard(()=>{Mark(AiUse.Forbidden);throw new IOException("PRIVATE_SENTINEL");},root),"forbidden");
    }
    [Fact]
    public void Headless_ui_project_switch_is_checked_before_open_and_recent_open()
    {
        Mark(AiUse.Forbidden);
        using var launcher=new Bed.Editor.ImGui.ProjectLauncher(new Bed.Editor.ImGui.RecentProjects(Path.Combine(root,"recent.json"))){AiObservation=true};
        var project=new Bed.Editor.ImGui.ProjectPaths(Data,Schemas);
        Assert.False(launcher.Open(project));Assert.Contains("forbidden",launcher.Said);Assert.Null(launcher.Workspace);
        Assert.False(launcher.OpenFolder(root));Assert.Contains("forbidden",launcher.Said);
        Assert.False(launcher.OpenRecent(project));Assert.Contains("forbidden",launcher.Said);
    }
    [Fact]
    public void External_adapter_and_map_inputs_remain_protected_after_configuration()
    {
        var other=Directory.CreateTempSubdirectory("bed-ai-external-").FullName;
        try
        {
            var asset=Path.Combine(other,"map.json");File.WriteAllText(asset,"PRIVATE_SENTINEL");
            var session=Session();session.ProtectAiPaths(other);Assert.Null(session.Describe()["error"]);
            Mark(AiUse.Forbidden,asset);Refused(session.Describe(),"forbidden");
        }
        finally{Directory.Delete(other,true);}
    }
    [Fact]
    public void Permission_checks_do_not_open_the_forbidden_source_bytes()
    {
        Mark(AiUse.Forbidden);
        using var locked=new FileStream(Asset,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Refused(Session().Get("items.json",""),"forbidden");
    }
    public void Dispose()=>Directory.Delete(root,true);
}
