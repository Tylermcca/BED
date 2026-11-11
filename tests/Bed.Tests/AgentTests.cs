using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>Goal 20: an AI agent works a project directly, through the same commands and refusals as a person, by
/// calls (AgentSession), the command line, or MCP.</summary>
public sealed class AgentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "agent-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");

    public AgentTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "wares.schema.json"), """
        {"title":"Wares","description":"What can be traded. Prices are in credits.","type":"object","properties":{"wares":{"type":"array","items":
          {"type":"object","required":["id"],"additionalProperties":false,"properties":{"id":{"type":"string"},"name":{"type":"string"},"price":{"type":"number","minimum":0}}}}}}
        """);
        File.WriteAllText(Path.Combine(Schemas, "jobs.schema.json"), """{"title":"Jobs","type":"object"}""");
        File.WriteAllText(Path.Combine(Data, "wares.json"), "{\n  \"wares\": [\n    { \"id\": \"ore\", \"name\": \"Ore\", \"price\": 12 },\n    { \"id\": \"salt\", \"name\": \"Salt\", \"price\": 3 }\n  ]\n}\n");
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"code":"J1","ware":"ore"}]}""");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private AgentSession Session(bool readOnly = false, Func<string, string?>? readOnlyFile = null) =>
        new(DataProject.Open(Data, Schemas, readOnly, readOnlyFile), ReferenceMap.Read("""
        {"game":"Toy","labels":[{"file":"jobs.json","list":"/jobs","field":"code"}],
         "references":[{"file":"jobs.json","at":"/jobs/*/ware","target":{"file":"wares.json","list":"/wares","key":"id"},"evidence":"test"}]}
        """));

    [Fact]
    public void Describe_tells_a_newcomer_what_is_here_and_where_to_start()
    {
        var d = Session(readOnlyFile: n => n == "jobs.json" ? "another tool writes it" : null).Describe();
        var files = d["files"]!.AsArray();
        Assert.Equal(["jobs.json", "wares.json"], files.Select(f => f!["file"]!.GetValue<string>()));
        var wares = files[1]!;
        Assert.Equal("Wares", wares["title"]!.GetValue<string>());
        Assert.Equal("What can be traded.", wares["description"]!.GetValue<string>());   // the first sentence
        Assert.Equal(2, wares["lists"]![0]!["count"]!.GetValue<int>());
        Assert.Equal("another tool writes it", files[0]!["readOnly"]!.GetValue<string>());
        Assert.Equal("code", files[0]!["lists"]![0]!["labelField"]!.GetValue<string>());
        Assert.Equal("Toy", d["game"]!.GetValue<string>());
        Assert.Contains("preview", d["next"]!.GetValue<string>());
    }

    [Fact]
    public void Find_and_get_show_labels_places_and_where_ids_lead()
    {
        var s = Session();
        var found = s.Find("wares.json", "/wares", "sal");
        Assert.Equal(1, found["matches"]!.GetValue<int>());
        Assert.Equal("/wares/1", found["items"]![0]!["at"]!.GetValue<string>());
        Assert.Equal("Salt", found["items"]![0]!["label"]!.GetValue<string>());
        var link = s.Get("jobs.json", "/jobs/0/ware")["link"]!;
        Assert.Equal("Found", link["outcome"]!.GetValue<string>());
        Assert.Equal("/wares/0", link["at"]!.GetValue<string>());
        Assert.Equal("J1", s.Get("jobs.json", "/jobs/0")["label"]!.GetValue<string>());
        Assert.Contains("find", s.Get("wares.json", "/wares/9")["next"]!.GetValue<string>());   // a wrong place says where to look
    }

    [Fact]
    public void A_preview_changes_nothing_and_a_refusal_says_why()
    {
        var s = Session();
        var preview = s.Set("wares.json", "/wares/0/price", "15", preview: true);
        Assert.True(preview["preview"]!.GetValue<bool>());
        Assert.Equal(12, preview["before"]!.GetValue<double>());
        Assert.Equal(15, preview["after"]!.GetValue<double>());
        Assert.False(s.Project.Find("wares.json")!.Dirty);
        var refused = s.Set("wares.json", "/wares/0/price", "-1");
        Assert.Contains("at least 0", refused["refused"]![0]!.GetValue<string>());
        Assert.False(s.Project.Find("wares.json")!.Dirty);
        Assert.Contains("isn't JSON", s.Set("wares.json", "/wares/0/name", "Ore Two")["error"]!.GetValue<string>());   // text needs quotes
    }

    [Fact]
    public void Edits_are_the_documents_own_undoable_commands_and_save_keeps_the_files_style()
    {
        var s = Session();
        Assert.True(s.Set("wares.json", "/wares/0/price", "15")["applied"]!.GetValue<bool>());
        Assert.True(s.Insert("wares.json", "/wares", 2, """{"id":"gems","price":90}""")["applied"]!.GetValue<bool>());
        Assert.True(s.Undo("wares.json")["undone"]!.GetValue<bool>());
        Assert.Empty(s.Save()["refused"]!.AsArray());
        Assert.Equal("{\n  \"wares\": [\n    { \"id\": \"ore\", \"name\": \"Ore\", \"price\": 15 },\n    { \"id\": \"salt\", \"name\": \"Salt\", \"price\": 3 }\n  ]\n}\n",
            File.ReadAllText(Path.Combine(Data, "wares.json")));
    }

    [Fact]
    public void Saving_is_refused_the_same_way_it_is_for_a_person()
    {
        var s = Session(readOnly: true);
        Assert.True(s.Set("wares.json", "/wares/0/price", "15")["applied"]!.GetValue<bool>());
        var save = s.Save("wares.json");
        Assert.Contains("read-only", save["refused"]![0]!["why"]!.GetValue<string>());
        Assert.Contains("\"price\": 12", File.ReadAllText(Path.Combine(Data, "wares.json")));
    }

    [Fact]
    public void A_place_that_is_not_a_pointer_is_an_honest_error_and_changes_nothing()
    {
        var s = Session();
        foreach (var result in new[] { s.Get("wares.json", "wares/0"), s.Find("wares.json", "wares"), s.Refs("wares.json", "wares/0/id"),
                     s.Set("wares.json", "wares/0/price", "15"), s.Remove("wares.json", "wares/0"), s.Insert("wares.json", "wares", 0, "{\"id\":\"x\"}"),
                     s.Get("wares.json", null!) })
        {
            Assert.NotNull(result["error"]);
            Assert.NotNull(result["next"]);
            Assert.Null(result["applied"]);
        }
        Assert.Equal(12, s.Get("wares.json", "/wares/0/price")["value"]!.GetValue<int>());   // nothing was edited
    }

    [Fact]
    public void Problems_lists_what_the_schema_finds_in_every_file()
    {
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore","price":-4},{"name":"no id"}]}""");
        var p = Session().Problems();
        Assert.Equal(2, p["count"]!.GetValue<int>());   // a file that can't open reports why
    }

    [Fact]
    public void The_mcp_server_speaks_json_rpc_and_offers_every_call_as_a_tool()
    {
        var server = new McpServer(Session());
        var init = server.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""")!;
        Assert.Equal("2025-06-18", init["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Contains("describe", init["result"]!["instructions"]!.GetValue<string>());
        Assert.Null(server.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        var tools = server.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")!["result"]!["tools"]!.AsArray();
        Assert.Equal(["ui_snapshot", "describe", "find", "get", "problems", "refs", "repair", "set", "insert", "remove", "undo", "save", "create", "rename", "delete", "restore", "proposal_list", "proposal_show", "proposal_accept", "proposal_reject", "proposal_submit", "checks", "check"], tools.Select(t => t!["name"]!.GetValue<string>()));
        var call = server.Handle("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"set","arguments":{"file":"wares.json","at":"/wares/1/price","value":"4","preview":true}}}""")!;
        var text = JsonNode.Parse(call["result"]!["content"]![0]!["text"]!.GetValue<string>())!;
        Assert.Equal(4, text["after"]!.GetValue<double>());
        Assert.False(call["result"]!["isError"]!.GetValue<bool>());
        var bad = server.Handle("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get","arguments":{"file":"wares.json"}}}""")!;
        Assert.True(bad["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal(-32601, server.Handle("""{"jsonrpc":"2.0","id":5,"method":"nope"}""")!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void The_server_runs_over_streams_one_message_per_line()
    {
        var input = new StringReader("""
        {"jsonrpc":"2.0","id":1,"method":"ping"}
        {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get","arguments":{"file":"wares.json","at":"/wares/0/id"}}}
        """);
        var output = new StringWriter();
        new McpServer(Session()).Run(input, output);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\\u0022ore\\u0022", lines[1].Replace("\\\"", "\\u0022"));
    }

    [Fact]
    public void The_command_line_edits_only_on_disk_when_told_to_save()
    {
        var o = new StringWriter();
        var args = new[] { "--data", Data, "--schemas", Schemas, "set", "wares.json", "/wares/0/price", "20" };
        Assert.Equal(0, Bed.Agent.Cli.Program.Run(args, TextReader.Null, o, TextWriter.Null));
        Assert.Contains("\"price\": 12", File.ReadAllText(Path.Combine(Data, "wares.json")));
        Assert.Equal(0, Bed.Agent.Cli.Program.Run([.. args, "--save"], TextReader.Null, new StringWriter(), TextWriter.Null));
        Assert.Contains("\"price\": 20", File.ReadAllText(Path.Combine(Data, "wares.json")));
        Assert.Equal(1, Bed.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "set", "wares.json", "/wares/0/price", "-3"], TextReader.Null, new StringWriter(), TextWriter.Null));
        Assert.Equal(2, Bed.Agent.Cli.Program.Run(["describe"], TextReader.Null, new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void The_command_line_find_takes_a_limit_like_mcp()   // A045's agent note: the hint said "raise limit", which the CLI couldn't
    {
        var o = new StringWriter();
        Assert.Equal(0, Bed.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "find", "wares.json", "/wares", "--limit", "1"], TextReader.Null, o, TextWriter.Null));
        var found = JsonNode.Parse(o.ToString())!;
        Assert.Single(found["items"]!.AsArray());
        Assert.Equal(2, found["matches"]!.GetValue<int>());
        Assert.Contains("--limit", found["next"]!.GetValue<string>());
    }

    [Fact]
    public void Refs_lists_everything_that_refers_to_an_item_and_says_when_nothing_does()
    {
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"code":"J1","ware":"ore"},{"code":"J2","ware":"ore"},{"code":"J3","ware":"salt"}]}""");
        var s = Session();
        var ore = s.Refs("wares.json", "/wares/0");
        Assert.Equal(2, ore["count"]!.GetValue<int>());
        Assert.Equal(["/jobs/0/ware", "/jobs/1/ware"], ore["from"]!.AsArray().Select(x => x!["at"]!.GetValue<string>()));
        Assert.Equal("J1", ore["from"]![0]!["item"]!.GetValue<string>());   // the referring item's label, from the map
        Assert.Contains("breaks no link", s.Refs("jobs.json", "/jobs/0")["next"]!.GetValue<string>());
        Assert.Contains("no reference map", new AgentSession(DataProject.Open(Data, Schemas)).Refs("wares.json", "/wares/0")["error"]!.GetValue<string>());
    }

    [Fact]
    public void Repair_opens_a_broken_file_and_lists_what_to_fix()
    {
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore","price":-1}]}""");
        var s = Session();
        Assert.Contains("can't be opened", s.Get("wares.json", "/wares/0")["error"]!.GetValue<string>());
        var r = s.Repair("wares.json");
        Assert.True(r["repairing"]!.GetValue<bool>());
        Assert.Contains("at least 0", r["problems"]![0]!.GetValue<string>());
        Assert.True(s.Set("wares.json", "/wares/0/price", "4")["applied"]!.GetValue<bool>());
        Assert.False(s.Project.Find("wares.json")!.Session!.Repairing);
    }

    [Fact]
    public void Files_are_made_renamed_deleted_and_restored_with_the_links_counted()
    {
        var a = Session();
        var made = a.Create("spare_wares.json", "wares.schema.json", preview: true);
        Assert.Contains("Would make", made["wouldDo"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(Data, "spare_wares.json")));
        Assert.NotNull(a.Create("spare_wares.json", "wares.schema.json")["done"]);
        Assert.True(File.Exists(Path.Combine(Data, "spare_wares.json")));
        var bad = a.Create("more.json", "ships.schema.json");
        Assert.Contains("wares.schema.json", bad["next"]!.GetValue<string>());   // it names the schemas there are

        var delete = a.Delete("wares.json", preview: true);
        Assert.Equal(1, delete["linksInto"]!.GetValue<int>());   // J1's ware
        Assert.Contains("would name nothing", delete["next"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(Data, "wares.json")));
        var bytes = File.ReadAllBytes(Path.Combine(Data, "wares.json"));
        Assert.NotNull(a.Delete("wares.json")["done"]);
        Assert.False(File.Exists(Path.Combine(Data, "wares.json")));
        Assert.NotNull(a.Restore("wares.json")["done"]);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Data, "wares.json")));
        Assert.NotNull(a.Restore("wares.json")["error"]);

        var rename = a.Rename("jobs.json", "work.json");   // its schema would change by name
        Assert.Contains("$schema", rename["error"]!.GetValue<string>());
        Assert.NotNull(a.Rename("spare_wares.json", "extra_wares.json")["done"]);
        Assert.Contains("extra_wares.json", a.Describe()["files"]!.AsArray().Select(f => f!["file"]!.GetValue<string>()));
    }

    [Fact]
    public void The_cli_only_previews_file_changes_without_save_and_mcp_offers_them()
    {
        string[] args = ["--data", Data, "--schemas", Schemas, "delete", "jobs.json"];
        Assert.Equal(0, Bed.Agent.Cli.Program.Run(args, TextReader.Null, new StringWriter(), TextWriter.Null));
        Assert.True(File.Exists(Path.Combine(Data, "jobs.json")));
        Assert.Equal(0, Bed.Agent.Cli.Program.Run([.. args, "--save"], TextReader.Null, new StringWriter(), TextWriter.Null));
        Assert.False(File.Exists(Path.Combine(Data, "jobs.json")));
        Assert.Equal(1, Bed.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "--read-only", "create", "x.json", "wares.schema.json", "--save"],
            TextReader.Null, new StringWriter(), TextWriter.Null));
        Assert.False(File.Exists(Path.Combine(Data, "x.json")));

        var server = new McpServer(Session());
        var answer = server.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"create","arguments":{"file":"new_wares.json","schema":"wares.schema.json"}}}""")!;
        Assert.False(answer["result"]!["isError"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(Data, "new_wares.json")));
    }

    [Fact]
    public void The_cli_says_an_edit_was_saved_or_how_to_keep_it()
    {
        // A065: after --save the answer still said "unsaved" and to save again; without it, it pointed at a save call
        // the command line doesn't have.
        string Run(params string[] extra)
        {
            var o = new StringWriter();
            Bed.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "set", "wares.json", "/wares/0/price", "13", .. extra], TextReader.Null, o, TextWriter.Null);
            return o.ToString();
        }
        var kept = JsonNode.Parse(Run())!;
        Assert.Contains("--save", kept["next"]!.GetValue<string>());
        var saved = JsonNode.Parse(Run("--save"))!;
        Assert.False(saved["unsaved"]!.GetValue<bool>());
        Assert.StartsWith("saved", saved["next"]!.GetValue<string>());
        Assert.Contains("13", File.ReadAllText(Path.Combine(Data, "wares.json")));
    }
}
