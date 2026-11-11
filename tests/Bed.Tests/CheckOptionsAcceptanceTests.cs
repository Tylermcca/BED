using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor.ImGui;
using Xunit;
using CliProgram = Bed.Agent.Cli.Program;

namespace Bed.Tests;

/// <summary>A077: independently authored finite-options admission, receipts and transport acceptance tests.
/// Fixtures are INVENTED ledger rows; no game, shell command, native window or extra package is used.</summary>
public sealed class CheckOptionsAcceptanceTests
{
    private const string Id = "review.options";
    private const string Schema = """
        {"type":"object","required":["mode","limits"],"properties":{
          "mode":{"type":"string","enum":["narrow","all"]},
          "limits":{"type":"object","required":["take"],"properties":{"take":{"type":"integer","minimum":1,"maximum":3}},"additionalProperties":false},
          "ratio":{"type":"number","exclusiveMinimum":0,"maximum":1},
          "note":{"type":"string","maxLength":64},
          "tags":{"type":"array","items":{"type":"string"},"maxItems":3}
        },"additionalProperties":false}
        """;
    private const string Good = """{"mode":"all","limits":{"take":2},"ratio":0.5}""";
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Directory.CreateTempSubdirectory("check-options-").FullName;
        internal string Data => Path.Combine(Root,"data");
        internal string Schemas => Path.Combine(Root,"schemas");
        internal string File => Path.Combine(Data,"ledgers.json");
        internal readonly DataProject Project;
        internal Fixture()
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
            System.IO.File.WriteAllText(File,"{\"items\":[{\"id\":\"n\",\"tag\":\"north\"},{\"id\":\"s\",\"tag\":\"south\"}]}\n");
            System.IO.File.WriteAllText(Path.Combine(Schemas,"ledgers.schema.json"),"{\"type\":\"object\"}\n");
            Project=DataProject.Open(Data,Schemas);
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    private sealed class Probe(string? schema=Schema,string id=Id) : ICheck
    {
        private int calls;
        internal int Calls=>Volatile.Read(ref calls);
        internal readonly ConcurrentQueue<CheckSnapshot> Seen=[];
        internal Func<CheckSnapshot,Task<CheckReport>>? Body;
        public CheckDefinition Definition {get;}=new(id,"Scoped ledger audit",
            "Audits only the selected mode; a clean narrow audit does not certify all rows.",TimeSpan.FromSeconds(10),schema);
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot,IProgress<CheckProgress> progress,CancellationToken cancel)
        {
            Interlocked.Increment(ref calls); Seen.Enqueue(snapshot);
            return Body?.Invoke(snapshot)??Task.FromResult(new CheckReport(true,[],"Selected options: "+snapshot.Options.GetRawText()));
        }
    }
    private static async Task<CheckRun> Start(Checks checks,Fixture f,string? options=Good,string id=Id)
    {
        var run=checks.Start(id,f.Project,out var why,options:options);
        Assert.NotNull(run); Assert.False(string.IsNullOrWhiteSpace(why));
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(CheckState.Completed,run.State);
        return run;
    }
    // Every refusal is checked both before any run and after a completed good run: no hidden replacement or call.
    private static async Task Refused(string? options,string? schema=Schema)
    {
        using var f=new Fixture();var probe=new Probe(schema);var checks=new Checks().Add(probe);
        var bytes=System.IO.File.ReadAllBytes(f.File);
        Assert.Null(checks.Start(Id,f.Project,out var firstWhy,options:options));
        Assert.False(string.IsNullOrWhiteSpace(firstWhy)); Assert.Null(checks.Last(Id)); Assert.Equal(0,probe.Calls);
        var old=await Start(checks,f,schema is null?null:Good);
        var calls=probe.Calls;
        Assert.Null(checks.Start(Id,f.Project,out var why,options:options));
        Assert.False(string.IsNullOrWhiteSpace(why)); Assert.Same(old,checks.Last(Id)); Assert.Equal(calls,probe.Calls);
        Assert.True(old.Clean(f.Project)); Assert.Equal(bytes,System.IO.File.ReadAllBytes(f.File));
        Assert.All(f.Project.Files,x=>Assert.False(x.Dirty));
    }
    private static CheckSnapshot Snap(Fixture f,string? options)=>CheckSnapshot.Capture(f.Project,options:options);
    private static AgentSession Agent(Fixture f,Probe p){var a=new AgentSession(f.Project);a.Checks.Add(p);return a;}
    private static JsonObject Mcp(AgentSession a,string tool,JsonObject args)
    {
        var request=new JsonObject{["jsonrpc"]="2.0",["id"]=1,["method"]="tools/call",
            ["params"]=new JsonObject{["name"]=tool,["arguments"]=args}};
        var reply=new McpServer(a).Handle(request.ToJsonString())!;
        Assert.Null(reply["error"]);
        return JsonNode.Parse(reply["result"]!["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
    }
    private static (int Code,JsonObject? Json,string Error) Cli(Fixture f,params string[] rest)
    {
        using var output=new StringWriter();using var error=new StringWriter();
        var code=CliProgram.Run(["--data",f.Data,"--schemas",f.Schemas,..rest],new StringReader(""),output,error);
        return(code,string.IsNullOrWhiteSpace(output.ToString())?null:JsonNode.Parse(output.ToString())!.AsObject(),error.ToString());
    }

    [Fact] public async Task Required_options_are_not_filled_from_a_hidden_default()=>await Refused(null);
    [Fact] public async Task Empty_object_does_not_satisfy_required_fields()=>await Refused("{}");
    [Fact] public async Task Whitespace_does_not_satisfy_required_fields()=>await Refused(" \r\n ");
    [Fact] public async Task Required_nested_field_is_enforced()=>await Refused("""{"mode":"all","limits":{}}""");
    [Fact] public async Task Unknown_enum_value_is_refused()=>await Refused("""{"mode":"other","limits":{"take":2}}""");
    [Fact] public async Task Lower_integer_limit_is_enforced()=>await Refused("""{"mode":"all","limits":{"take":0}}""");
    [Fact] public async Task Upper_integer_limit_is_enforced()=>await Refused("""{"mode":"all","limits":{"take":4}}""");
    [Fact] public async Task Fraction_is_not_an_integer_option()=>await Refused("""{"mode":"all","limits":{"take":1.5}}""");
    [Fact] public async Task Exclusive_number_minimum_is_enforced()=>await Refused("""{"mode":"all","limits":{"take":2},"ratio":0}""");
    [Fact] public async Task Inclusive_number_maximum_is_enforced()=>await Refused("""{"mode":"all","limits":{"take":2},"ratio":1.1}""");
    [Fact] public async Task Nested_value_must_have_the_right_type()=>await Refused("""{"mode":"all","limits":{"take":"2"}}""");
    [Fact] public async Task Unknown_root_key_is_refused()=>await Refused("""{"mode":"all","limits":{"take":2},"outputDirectory":"../elsewhere"}""");
    [Fact] public async Task Unknown_nested_key_is_refused()=>await Refused("""{"mode":"all","limits":{"take":2,"command":"do something"}}""");
    [Fact] public async Task Explicit_null_is_not_missing_or_a_default()=>await Refused("""{"mode":"all","limits":null}""");

    [Theory]
    [InlineData("[]")][InlineData("null")][InlineData("true")][InlineData("7")][InlineData("\"text\"")]
    public async Task Options_must_be_an_object(string value)=>await Refused(value);
    [Theory]
    [InlineData("{")][InlineData("{\"mode\":NaN}")][InlineData("{\"mode\":\"all\",}")]
    public async Task Unreadable_json_is_a_refusal_not_a_run(string value)=>await Refused(value);

    [Fact] public async Task Very_large_text_is_refused_by_its_declared_bound()
    {
        var value=JsonNode.Parse(Good)!.AsObject();value["note"]=new string('z',262144);
        await Refused(value.ToJsonString());
    }
    [Fact] public async Task Oversized_array_is_refused_by_its_declared_bound()
    {
        var value=JsonNode.Parse(Good)!.AsObject();value["tags"]=new JsonArray(Enumerable.Range(0,4096).Select(_=>(JsonNode)JsonValue.Create("tag")!).ToArray());
        await Refused(value.ToJsonString());
    }
    [Fact] public async Task Deep_json_past_the_reader_bound_is_refused_without_starting()
        =>await Refused("{\"blob\":"+new string('[',80)+"0"+new string(']',80)+"}");
    [Fact] public async Task Duplicate_root_keys_are_refused_before_canonicalization()
        =>await Refused("""{"mode":"narrow","mode":"all","limits":{"take":2}}""");
    [Fact] public async Task Duplicate_nested_keys_are_refused_before_canonicalization()
        =>await Refused("""{"mode":"all","limits":{"take":1,"take":2}}""");
    [Fact] public async Task Escaped_equivalent_duplicate_names_are_refused()
        =>await Refused("""{"mode":"narrow","\u006dode":"all","limits":{"take":2}}""");
    [Fact] public async Task A_check_with_no_schema_refuses_nonempty_options()
        =>await Refused("""{"unexpected":1}""",null);

    [Fact] public async Task Valid_boundary_values_reach_the_check_unchanged()
    {
        using var f=new Fixture();var p=new Probe();var c=new Checks().Add(p);
        foreach(var take in new[]{1,3})
        {
            var run=await Start(c,f,$$"""{"mode":"all","limits":{"take":{{take}}},"ratio":1}""");
            Assert.Equal(take,run.Snapshot.Options.GetProperty("limits").GetProperty("take").GetInt32());
            Assert.True(run.Clean(f.Project));
        }
        Assert.Equal(2,p.Calls);
    }
    [Fact] public async Task Supplementary_unicode_combining_text_and_controls_are_lossless()
    {
        using var f=new Fixture();var p=new Probe();var value=JsonNode.Parse(Good)!.AsObject();
        const string text="lamp \U0001F4A1 e\u0301 \u0000 \u2028";
        value["note"]=text;var run=await Start(new Checks().Add(p),f,value.ToJsonString());
        Assert.Equal(text,run.Snapshot.Options.GetProperty("note").GetString());Assert.Equal(1,p.Calls);
    }
    [Fact] public async Task Command_looking_text_is_only_finite_data()
    {
        using var f=new Fixture();var p=new Probe();var value=JsonNode.Parse(Good)!.AsObject();
        const string note="a literal --not-a-program | ../outside";value["note"]=note;
        var run=await Start(new Checks().Add(p),f,value.ToJsonString());
        Assert.Equal(note,run.Snapshot.Options.GetProperty("note").GetString());Assert.Equal(1,p.Calls);
        Assert.Single(Directory.GetFiles(f.Data));Assert.Single(Directory.GetFiles(f.Schemas));
    }
    [Fact] public void Malformed_schema_prevents_registration()
    {
        var c=new Checks();var error=Assert.Throws<ArgumentException>(()=>c.Add(new Probe("{")));
        Assert.Contains("schema",error.Message,StringComparison.OrdinalIgnoreCase);Assert.Empty(c.Registered);
    }
    [Fact] public void Bad_schema_in_a_batch_publishes_none_and_preserves_existing()
    {
        var c=new Checks().Add(new Probe(null,"kept.check"));
        Assert.Throws<ArgumentException>(()=>c.Add(new Probe(Schema,"good.check"),new Probe("{","bad.check")));
        Assert.Equal(["kept.check"],c.Registered.Select(d=>d.Id));Assert.Null(c.Last("good.check"));Assert.Null(c.Last("bad.check"));
    }
    [Fact] public async Task False_schema_is_valid_metadata_but_cannot_admit_a_run()
    {
        using var f=new Fixture();var good=new Probe(Schema,"kept.check");var bad=new Probe("false");
        var c=new Checks().Add(good,bad);var old=await Start(c,f,Good,"kept.check");
        Assert.Null(c.Start(Id,f.Project,out var why,options:"{}"));Assert.False(string.IsNullOrWhiteSpace(why));
        Assert.Null(c.Last(Id));Assert.Same(old,c.Last("kept.check"));Assert.Equal(0,bad.Calls);Assert.Equal(1,good.Calls);
    }
    [Fact] public void Non_schema_json_root_is_rejected_at_registration()
    {
        var c=new Checks();Assert.Throws<ArgumentException>(()=>c.Add(new Probe("17")));Assert.Empty(c.Registered);
    }
    [Fact] public async Task An_unresolvable_schema_reference_cannot_start_a_run()
    {
        using var f=new Fixture();var good=new Probe(Schema,"kept.check");var bad=new Probe("""{"$ref":"missing.schema.json"}""");
        var c=new Checks().Add(good,bad);var old=await Start(c,f,Good,"kept.check");
        Assert.Null(c.Start(Id,f.Project,out var why,options:Good));Assert.False(string.IsNullOrWhiteSpace(why));
        Assert.Null(c.Last(Id));Assert.Same(old,c.Last("kept.check"));Assert.Equal(0,bad.Calls);Assert.Equal(1,good.Calls);
    }

    [Fact] public void Object_order_whitespace_and_string_escapes_have_one_hash()
    {
        using var f=new Fixture();
        Assert.Equal(Snap(f,"""{"b":[{"z":2,"a":"a"}],"a":1}""").InputHash,
            Snap(f," { \"a\":1, \"b\": [ {\"a\":\"\\u0061\",\"z\":2} ] } ".Replace("\\\\u0061","\\u0061")).InputHash);
    }
    [Theory]
    [InlineData("1","1.0")][InlineData("1","1e0")][InlineData("0","-0")]
    public void Equivalent_numeric_meaning_has_one_hash(string left,string right)
    {
        using var f=new Fixture();Assert.Equal(Snap(f,"{\"n\":"+left+"}").InputHash,Snap(f,"{\"n\":"+right+"}").InputHash);
    }
    [Fact] public void Different_options_meanings_change_the_receipt()
    {
        using var f=new Fixture();Assert.NotEqual(Snap(f,"""{"mode":"narrow"}""").InputHash,Snap(f,"""{"mode":"all"}""").InputHash);
        Assert.NotEqual(Snap(f,"""{"n":1}""").InputHash,Snap(f,"""{"n":"1"}""").InputHash);
    }
    [Fact] public void Array_order_is_meaningful_and_unicode_is_not_normalized_away()
    {
        using var f=new Fixture();Assert.NotEqual(Snap(f,"""{"a":[1,2]}""").InputHash,Snap(f,"""{"a":[2,1]}""").InputHash);
        Assert.NotEqual(Snap(f,"{\"s\":\"é\"}").InputHash,Snap(f,"{\"s\":\"e\\u0301\"}").InputHash);
    }
    [Fact] public void No_options_keeps_the_pinned_pre_options_hash()
    {
        using var f=new Fixture();const string legacy="9b31a14bacc4ffed1ad034fd104fa8dbb40ee1f5f96dbd3256ab97c562f8ed02";
        foreach(var options in new string?[]{null,""," \n","{}"," { } "})
        {var s=Snap(f,options);Assert.Equal(legacy,s.InputHash);Assert.Equal(CheckSnapshot.NoOptions,s.Options.GetRawText());}
    }
    [Fact] public async Task A_check_cannot_mutate_its_options_through_a_detached_node()
    {
        using var f=new Fixture();var p=new Probe();
        p.Body=s=>{var node=JsonNode.Parse(s.Options.GetRawText())!.AsObject();node["mode"]="narrow";
            return Task.FromResult(new CheckReport(true,[],s.Options.GetProperty("mode").GetString()!));};
        var r=await Start(new Checks().Add(p),f);Assert.Equal("all",r.Report!.Summary);Assert.Equal("all",r.Snapshot.Options.GetProperty("mode").GetString());
    }
    [Fact] public async Task Captured_options_survive_caller_document_disposal_and_buffer_reuse()
    {
        using var f=new Fixture();var p=new Probe();var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.Body=async s=>{await release.Task;return new CheckReport(true,[],s.Options.GetProperty("mode").GetString()!);};
        var c=new Checks().Add(p);string encoded;
        using(var doc=JsonDocument.Parse(Good))encoded=doc.RootElement.GetRawText();
        var r=c.Start(Id,f.Project,out _,options:encoded)!;
        try {encoded="""{"mode":"narrow","limits":{"take":1}}""";Assert.Equal("all",r.Snapshot.Options.GetProperty("mode").GetString());}
        finally {release.TrySetResult();}
        await r.Ended.WaitAsync(TimeSpan.FromSeconds(15));Assert.Equal("all",r.Report!.Summary);
    }
    [Fact] public async Task Two_successive_runs_keep_independent_option_receipts()
    {
        using var f=new Fixture();var p=new Probe();var c=new Checks().Add(p);
        var first=await Start(c,f,Good);var second=await Start(c,f,"""{"mode":"narrow","limits":{"take":1}}""");
        Assert.NotSame(first,second);Assert.Same(second,c.Last(Id));Assert.Equal(2,p.Calls);
        Assert.Equal("all",first.Snapshot.Options.GetProperty("mode").GetString());Assert.Equal("narrow",second.Snapshot.Options.GetProperty("mode").GetString());
        Assert.NotEqual(first.Snapshot.InputHash,second.Snapshot.InputHash);
    }
    [Fact] public async Task Changed_project_is_stale_even_with_the_same_options()
    {
        using var f=new Fixture();var p=new Probe();var r=await Start(new Checks().Add(p),f);
        var file=f.Project.Find("ledgers.json")!;Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new SetValue("/items/0/tag",JsonSerializer.SerializeToElement("west"))));
        Assert.True(r.IsStale(f.Project));Assert.False(r.Clean(f.Project));Assert.Contains("stale",r.Verdict(f.Project));
    }
    [Fact] public void Narrow_clean_result_discloses_scope_and_wider_result_does_not_hide_findings()
    {
        using var f=new Fixture();var p=new Probe();
        p.Body=s=>Task.FromResult(s.Options.GetProperty("mode").GetString()=="narrow"
            ?new CheckReport(true,[],"Only north rows audited.")
            :new CheckReport(true,[new CheckFinding(Issue.Error("South row needs review."),new("ledgers.json","/items/1","/items/1/tag"),"Review the south tag.")],"All rows audited."));
        var a=Agent(f,p);var narrow=a.RunCheck(Id,options:"""{"mode":"narrow","limits":{"take":1}}""");
        Assert.True(narrow["clean"]!.GetValue<bool>());Assert.Equal("narrow",narrow["options"]!["mode"]!.GetValue<string>());Assert.Contains("Only north",narrow["summary"]!.GetValue<string>());
        Assert.Contains("selected mode",a.ListChecks()["checks"]!.AsArray().Single(x=>x!["id"]!.GetValue<string>()==Id)!["description"]!.GetValue<string>());
        var wide=a.RunCheck(Id,options:Good);Assert.False(wide["clean"]!.GetValue<bool>());Assert.Single(wide["findings"]!.AsArray());
    }
    [Fact] public async Task Ignoring_options_cannot_mutate_the_receipt_or_invent_a_hidden_default()
    {
        using var f=new Fixture();var p=new Probe(){Body=_=>Task.FromResult(new CheckReport(true,[],"This deliberately ignores choices; producer responsibility."))};
        var r=await Start(new Checks().Add(p),f);Assert.True(r.Clean(f.Project));
        Assert.Equal("all",r.Snapshot.Options.GetProperty("mode").GetString());Assert.Equal(1,p.Calls);
    }
    [Fact] public void Agent_discovery_exposes_the_actual_options_schema_without_running()
    {
        using var f=new Fixture();var p=new Probe();var a=Agent(f,p);
        var entry=a.ListChecks()["checks"]!.AsArray().Single(x=>x!["id"]!.GetValue<string>()==Id)!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Schema),entry["options"]));Assert.Equal(0,p.Calls);
    }
    [Fact] public void Agent_first_with_options_refuses_without_replacing_a_good_run()
    {
        using var f=new Fixture();var p=new Probe();var a=Agent(f,p);a.RunCheck(Id,options:Good);var old=a.Checks.Last(Id);
        var bad=a.RunCheck(Id,first:0,options:"{}");Assert.NotNull(bad["error"]);Assert.Same(old,a.Checks.Last(Id));Assert.Equal(1,p.Calls);
    }
    [Fact] public void Every_agent_page_carries_the_original_options()
    {
        using var f=new Fixture();var p=new Probe();
        p.Body=_=>Task.FromResult(new CheckReport(true,Enumerable.Range(0,3).Select(i=>new CheckFinding(Issue.Warning("item "+i),null,"inspect")).ToArray(),"Three warnings."));
        var a=Agent(f,p);var one=a.RunCheck(Id,limit:1,options:Good);var two=a.RunCheck(Id,first:1,limit:1);
        Assert.True(JsonNode.DeepEquals(one["options"],two["options"]));Assert.Equal(one["inputHash"]!.GetValue<string>(),two["inputHash"]!.GetValue<string>());
        Assert.Equal(1,p.Calls);Assert.Equal(1,two["findings"]![0]!["ordinal"]!.GetValue<int>());
    }
    [Fact] public void Agent_and_mcp_agree_for_valid_options_and_receipts()
    {
        using var f=new Fixture();var p=new Probe();var a=Agent(f,p);
        var direct=a.RunCheck(Id,options:Good);var rpc=Mcp(a,"check",new(){["id"]=Id,["options"]=JsonNode.Parse(Good)});
        Assert.Equal(direct["inputHash"]!.GetValue<string>(),rpc["inputHash"]!.GetValue<string>());Assert.True(JsonNode.DeepEquals(direct["options"],rpc["options"]));Assert.Equal(2,p.Calls);
    }
    [Fact] public void Mcp_discovery_exposes_schema_and_does_not_call_the_check()
    {
        using var f=new Fixture();var p=new Probe();var a=Agent(f,p);var list=Mcp(a,"checks",new());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Schema),list["checks"]!.AsArray().Single(x=>x!["id"]!.GetValue<string>()==Id)!["options"]));Assert.Equal(0,p.Calls);
    }
    [Fact] public void Mcp_invalid_options_preserve_a_good_run_and_do_not_call()
    {
        using var f=new Fixture();var p=new Probe();var a=Agent(f,p);a.RunCheck(Id,options:Good);var old=a.Checks.Last(Id);
        foreach(JsonNode? options in new JsonNode?[]{null,JsonValue.Create("bad"),new JsonArray(),JsonNode.Parse("{}")})
        {
            var bad=Mcp(a,"check",new(){["id"]=Id,["options"]=options?.DeepClone()});Assert.NotNull(bad["error"]);Assert.Same(old,a.Checks.Last(Id));Assert.Equal(1,p.Calls);
        }
    }
    [Fact] public void Mcp_first_with_options_is_refused_before_a_run()
    {
        using var f=new Fixture();var p=new Probe();var a=Agent(f,p);
        var bad=Mcp(a,"check",new(){["id"]=Id,["first"]=0,["options"]=JsonNode.Parse(Good)});
        Assert.NotNull(bad["error"]);Assert.Null(a.Checks.Last(Id));Assert.Equal(0,p.Calls);
    }
    [Fact] public void Mcp_duplicate_option_keys_are_a_recoverable_refusal()
    {
        using var f=new Fixture();var p=new Probe();var a=Agent(f,p);
        var raw="""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"check","arguments":{"id":"review.options","options":{"mode":"all","mode":"narrow","limits":{"take":2}}}}}""";
        var reply=new McpServer(a).Handle(raw);Assert.NotNull(reply);
        Assert.True(reply["error"] is not null||reply["result"]?["isError"]?.GetValue<bool>()==true);
        Assert.Null(a.Checks.Last(Id));Assert.Equal(0,p.Calls);
    }
    [Fact] public void Cli_and_agent_agree_for_empty_options_of_the_builtin_check()
    {
        using var f=new Fixture();var direct=new AgentSession(f.Project).RunCheck("links.all",options:"{}");
        var cli=Cli(f,"check","links.all","--options","{}");Assert.Equal(0,cli.Code);Assert.Empty(cli.Error);
        Assert.Equal(direct["inputHash"]!.GetValue<string>(),cli.Json!["inputHash"]!.GetValue<string>());Assert.True(cli.Json["clean"]!.GetValue<bool>());
    }
    [Theory][InlineData("[]")][InlineData("{")][InlineData("{\"outputDirectory\":\"../outside\"}")]
    public void Cli_and_agent_agree_on_refusal_for_builtin_options(string options)
    {
        using var f=new Fixture();var a=new AgentSession(f.Project).RunCheck("links.all",options:options);
        var c=Cli(f,"check","links.all","--options",options);Assert.NotNull(a["error"]);Assert.Equal(1,c.Code);Assert.NotNull(c.Json!["error"]);
    }
    [Fact] public void Cli_first_with_options_is_refused()
    {
        using var f=new Fixture();var c=Cli(f,"check","links.all","--first","0","--options","{}");
        Assert.Equal(1,c.Code);Assert.NotNull(c.Json!["error"]);
    }
    [Fact] public void Cli_missing_options_argument_is_usage_failure_not_silent_default()
    {
        using var f=new Fixture();var c=Cli(f,"check","links.all","--options");
        Assert.Equal(2,c.Code);Assert.Null(c.Json);Assert.Contains("--options",c.Error);
    }
    [Fact] public void Panel_refuses_required_options_with_a_reason_and_without_running()
    {
        using var f=new Fixture();var p=new Probe();var w=new DataWorkspace(f.Project);w.Checks.Add(p);
        Assert.False(w.RunCheck(Id));Assert.False(string.IsNullOrWhiteSpace(w.Said));Assert.Contains("options",w.Said,StringComparison.OrdinalIgnoreCase);
        Assert.Null(w.Checks.Last(Id));Assert.Equal(0,p.Calls);
    }
    [Fact] public async Task Optional_schema_defaults_are_not_implicitly_materialized()
    {
        using var f=new Fixture();var p=new Probe("""{"type":"object","properties":{"choice":{"type":"integer","default":2}},"additionalProperties":false}""");
        var r=await Start(new Checks().Add(p),f,"{}");Assert.Empty(r.Snapshot.Options.EnumerateObject());Assert.True(r.Clean(f.Project));
    }
}
