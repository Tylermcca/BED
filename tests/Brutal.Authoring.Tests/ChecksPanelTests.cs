using System.Text.Json;
using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Checks for people and agents (piece J): the Checks panel runs one off the frame, its findings lead to their
/// places, its verdict goes stale with an edit; agents run the same checks and get the same findings.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class ChecksPanelTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("checks-panel-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private const string MapText = """
        {"game":"T","labels":[{"file":"jobs.json","list":"/jobs","field":"code"}],
         "references":[{"file":"jobs.json","at":"/jobs/*/ware","target":{"file":"wares.json","list":"/wares","key":"id"},"evidence":"test"}]}
        """;

    public ChecksPanelTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "wares.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Schemas, "jobs.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore"}]}""");
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"code":"J1","ware":"ore"},{"code":"J2","ware":"silk"}]}""");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DataWorkspace Workspace() => new(DataProject.Open(Data, Schemas), map: ReferenceMap.Read(MapText));

    [Fact]
    public async Task The_panel_runs_the_link_check_and_a_finding_leads_to_its_place()
    {
        var w = Workspace();
        Assert.True(w.Show("wares.json"));
        Assert.False(w.Window!.Editor.IsOpen("project.checks"));   // closed until wanted
        Assert.True(w.Window.Run(Brutal.Authoring.Editor.ImGui.EditorWindow<DataDocument>.PanelCommandAction + "project.checks/run.links.all"));
        Assert.True(w.Window.Editor.IsOpen("project.checks"));   // running one opens it
        var run = w.Checks.Last("links.all")!;
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("errors", run.Verdict(w.Project));
        var finding = Assert.Single(run.Report!.Findings);
        Assert.True(w.Reveal(finding));
        Assert.Equal("jobs.json", w.Active!.Name);
        Assert.Equal("/jobs/1", w.Window!.Editor.Primary);

        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(w.Draw, 4);   // the panel draws its verdict and finding

        Assert.True(w.Window.Editor.Session.Execute(new SetValue("/jobs/1/ware", JsonSerializer.SerializeToElement("ore"))));
        Assert.Equal("errors (stale)", run.Verdict(w.Project));   // the fix isn't checked yet
        Assert.True(w.RunCheck("links.all"));
        await w.Checks.Last("links.all")!.Ended.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(w.Checks.Last("links.all")!.Clean(w.Project));   // unsaved edits are what's checked
    }

    [Fact]
    public void Agents_run_the_same_check_and_get_each_finding_with_its_place()
    {
        var agent = new AgentSession(DataProject.Open(Data, Schemas), ReferenceMap.Read(MapText));
        Assert.Contains("links.all", agent.ListChecks().ToJsonString());
        var result = agent.RunCheck("links.all");
        Assert.False(result["clean"]!.GetValue<bool>());
        var finding = Assert.Single(result["findings"]!.AsArray());
        Assert.Equal("/jobs/1/ware", finding!["at"]!.GetValue<string>());
        Assert.Contains("spelling", finding["next"]!.GetValue<string>());
        Assert.Equal(64, result["inputHash"]!.GetValue<string>().Length);
        Assert.NotNull(agent.RunCheck("nothing")["error"]);
    }

    private sealed class Many(int count) : ICheck
    {
        public int Runs;
        public CheckDefinition Definition { get; } = new("sample.many", "Many", "Finds a lot.", TimeSpan.FromSeconds(30));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            Runs++;
            return Task.FromResult(new CheckReport(true, Enumerable.Range(0, count)
                .Select(i => new CheckFinding(Issue.Error($"Finding {i}"), new CheckPlace("wares.json"), "Fix it.")).ToList(), $"{count} found."));
        }
    }

    [Fact]
    public void Agents_read_a_long_list_in_pages_of_one_run()
    {
        var agent = new AgentSession(DataProject.Open(Data, Schemas));
        var check = new Many(120);
        agent.Checks.Add(check);

        var page1 = agent.RunCheck("sample.many", limit: 50);
        Assert.Equal(50, page1["findings"]!.AsArray().Count);
        Assert.Equal(120, page1["total"]!.GetValue<int>());
        Assert.Equal(50, page1["nextFirst"]!.GetValue<int>());
        Assert.Contains("first=50", page1["next"]!.GetValue<string>());

        var read = page1["findings"]!.AsArray().Select(f => f!["message"]!.GetValue<string>()).ToList();
        for (var next = page1["nextFirst"]; next is not null;)
        {
            var page = agent.RunCheck("sample.many", next.GetValue<int>());   // 50 by default
            Assert.Equal(page1["inputHash"]!.GetValue<string>(), page["inputHash"]!.GetValue<string>());
            Assert.Equal(next.GetValue<int>(), page["findings"]![0]!["ordinal"]!.GetValue<int>());
            read.AddRange(page["findings"]!.AsArray().Select(f => f!["message"]!.GetValue<string>()));
            next = page["nextFirst"];
        }
        Assert.Equal(Enumerable.Range(0, 120).Select(i => $"Finding {i}"), read);   // every one, in order, once
        Assert.Equal(1, check.Runs);   // reading on never ran it again

        Assert.Empty(agent.RunCheck("sample.many", 500)["findings"]!.AsArray());   // past the end: nothing, and no next
        Assert.Null(agent.RunCheck("sample.many", 500)["nextFirst"]);
        Assert.Equal(120, agent.RunCheck("sample.many")["findings"]!.AsArray().Count);   // no paging asked: every finding
        Assert.Null(agent.RunCheck("sample.many")["total"]);
    }

    [Fact]
    public void A_page_of_an_old_run_says_it_is_stale_and_bad_numbers_are_refused()
    {
        var agent = new AgentSession(DataProject.Open(Data, Schemas));
        agent.Checks.Add(new Many(3));
        Assert.Single(agent.RunCheck("sample.many", 2)["findings"]!.AsArray());   // no run yet: it runs one
        Assert.False(agent.RunCheck("sample.many", 0)["stale"]!.GetValue<bool>());
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore"},{"id":"tin"}]}""");
        var old = agent.RunCheck("sample.many", 0);
        Assert.True(old["stale"]!.GetValue<bool>());
        Assert.Contains("(stale)", old["verdict"]!.GetValue<string>());
        Assert.False(old["clean"]!.GetValue<bool>());
        Assert.Contains("changed", old["next"]!.GetValue<string>());

        Assert.NotNull(agent.RunCheck("sample.many", -1)["error"]);
        Assert.NotNull(agent.RunCheck("sample.many", limit: 0)["error"]);
        Assert.NotNull(agent.RunCheck("sample.many", limit: AgentSession.MostFindings + 1)["error"]);
        Assert.NotNull(agent.RunCheck("nothing", 0)["error"]);
    }

    [Fact]
    public void Mcp_and_the_cli_page_the_same_way()
    {
        var agent = new AgentSession(DataProject.Open(Data, Schemas));
        agent.Checks.Add(new Many(5));
        var server = new McpServer(agent);
        var call = server.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"check","arguments":{"id":"sample.many","first":1,"limit":2}}}""")!;
        var page = System.Text.Json.Nodes.JsonNode.Parse(call["result"]!["content"]![0]!["text"]!.GetValue<string>())!;
        Assert.Equal(["Finding 1", "Finding 2"], page["findings"]!.AsArray().Select(f => f!["message"]!.GetValue<string>()));
        Assert.Equal(3, page["nextFirst"]!.GetValue<int>());
        var wrong = server.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"check","arguments":{"id":"sample.many","first":"one"}}}""")!;
        Assert.True(wrong["result"]!["isError"]!.GetValue<bool>());

        var map = Path.Combine(root, "map.json");
        File.WriteAllText(map, MapText);
        var output = new StringWriter();
        Assert.Equal(1, Brutal.Authoring.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "--map", map, "check", "links.all", "--first", "1", "--limit", "5"], TextReader.Null, output, TextWriter.Null));
        var cli = System.Text.Json.Nodes.JsonNode.Parse(output.ToString())!;
        Assert.Equal(1, cli["total"]!.GetValue<int>());
        Assert.Empty(cli["findings"]!.AsArray());
    }

    [Fact]
    public async Task The_panel_shows_a_long_list_a_step_at_a_time_and_a_new_run_starts_over()
    {
        var w = Workspace();
        Assert.True(w.Show("wares.json"));
        w.Checks.Add(new Many(250));
        Assert.Equal(0, w.FindingsShown("sample.many"));
        Assert.True(w.RunCheck("sample.many"));
        await w.Checks.Last("sample.many")!.Ended.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(100, w.FindingsShown("sample.many"));
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(w.Draw, 3);   // it draws with more to come
        w.ShowMoreFindings("sample.many");
        Assert.Equal(200, w.FindingsShown("sample.many"));
        w.ShowMoreFindings("sample.many");
        Assert.Equal(250, w.FindingsShown("sample.many"));
        w.ShowMoreFindings("sample.many");
        Assert.Equal(250, w.FindingsShown("sample.many"));   // never past the end
        host.Frames(w.Draw, 3);

        Assert.True(w.RunCheck("sample.many"));
        await w.Checks.Last("sample.many")!.Ended.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(100, w.FindingsShown("sample.many"));
    }

    private sealed class Choosy : ICheck
    {
        public string? Seen;
        public CheckDefinition Definition { get; } = new("sample.choosy", "Choosy", "Looks at one list.", TimeSpan.FromSeconds(30),
            """{"type":"object","properties":{"list":{"enum":["wares","jobs"]},"deep":{"type":"boolean"}},"required":["list"],"additionalProperties":false}""");
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            Seen = snapshot.Options.GetProperty("list").GetString();
            return Task.FromResult(new CheckReport(true, [], $"Looked at {Seen}."));
        }
    }

    [Fact]
    public async Task A_check_takes_only_the_options_its_schema_accepts_and_nothing_runs_on_others()
    {
        var project = DataProject.Open(Data, Schemas);
        var check = new Choosy();
        var checks = new Checks().Add(check);

        Assert.Null(checks.Start("sample.choosy", project, out var why));   // required, and no hidden default
        Assert.Contains("list", why);
        Assert.Null(checks.Start("sample.choosy", project, out why, options: """{"list":"nope"}"""));
        Assert.Contains("can't be used", why);
        Assert.Null(checks.Start("sample.choosy", project, out why, options: """{"list":"wares","extra":1}"""));
        Assert.Null(checks.Start("sample.choosy", project, out why, options: "{not json"));
        Assert.Contains("aren't JSON", why);
        Assert.Null(checks.Start("sample.choosy", project, out why, options: "[1]"));
        Assert.Contains("object", why);
        Assert.Null(check.Seen);   // none of those ran it
        Assert.Null(checks.Last("sample.choosy"));

        var run = checks.Start("sample.choosy", project, out _, options: """{"list":"jobs"}""")!;
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("jobs", check.Seen);
        Assert.True(run.Clean(project));
    }

    [Fact]
    public void Options_change_what_a_snapshot_is_but_not_their_spelling()
    {
        var project = DataProject.Open(Data, Schemas);
        var none = CheckSnapshot.Capture(project).InputHash;
        Assert.Equal(none, CheckSnapshot.Capture(project, options: "{}").InputHash);   // none hashes as it always has
        var chosen = CheckSnapshot.Capture(project, options: """{"a":1,"b":[2,{"d":3,"c":4}]}""").InputHash;
        Assert.NotEqual(none, chosen);
        Assert.Equal(chosen, CheckSnapshot.Capture(project, options: """{ "b": [2, {"c":4,"d":3}], "a": 1 }""").InputHash);
        Assert.NotEqual(chosen, CheckSnapshot.Capture(project, options: """{"a":1,"b":[{"c":4,"d":3},2]}""").InputHash);   // a list's order means something
    }

    [Fact]
    public void A_check_with_no_schema_refuses_options_and_a_bad_schema_adds_nothing()
    {
        var project = DataProject.Open(Data, Schemas);
        var checks = new Checks().Add(new Many(1));
        Assert.Null(checks.Start("sample.many", project, out var why, options: """{"x":1}"""));
        Assert.Contains("takes no options", why);

        var broken = new Bad();
        var other = new Many(1);
        Assert.Throws<ArgumentException>(() => new Checks().Add(other, broken));
        var kept = new Checks();
        Assert.Throws<ArgumentException>(() => kept.Add(other, broken));
        Assert.Empty(kept.Registered);   // all or none
    }

    private sealed class Bad : ICheck
    {
        public CheckDefinition Definition { get; } = new("sample.bad", "Bad", "Its schema is broken.", TimeSpan.FromSeconds(1), "{not a schema");
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel) => throw new InvalidOperationException();
    }

    [Fact]
    public void Agents_mcp_and_the_cli_pass_options_and_see_what_was_chosen()
    {
        var agent = new AgentSession(DataProject.Open(Data, Schemas));
        agent.Checks.Add(new Choosy());
        var listed = agent.ListChecks()["checks"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == "sample.choosy")!;
        Assert.Equal("object", listed["options"]!["type"]!.GetValue<string>());
        Assert.Null(agent.ListChecks()["checks"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == "links.all")!["options"]);

        Assert.Contains("list", agent.RunCheck("sample.choosy")["error"]!.GetValue<string>());
        var result = agent.RunCheck("sample.choosy", options: """{"list":"wares"}""");
        Assert.Equal("wares", result["options"]!["list"]!.GetValue<string>());
        Assert.True(result["clean"]!.GetValue<bool>());
        Assert.NotNull(agent.RunCheck("sample.choosy", 0, options: """{"list":"wares"}""")["error"]);   // options start a run; first reads one
        Assert.Equal("wares", agent.RunCheck("sample.choosy", 0)["options"]!["list"]!.GetValue<string>());   // the page says what its run chose

        var server = new McpServer(agent);
        var call = server.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"check","arguments":{"id":"sample.choosy","options":{"list":"jobs"}}}}""")!;
        var text = System.Text.Json.Nodes.JsonNode.Parse(call["result"]!["content"]![0]!["text"]!.GetValue<string>())!;
        Assert.Equal("jobs", text["options"]!["list"]!.GetValue<string>());

        var output = new StringWriter();
        Assert.Equal(1, Brutal.Authoring.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "check", "links.all", "--options", """{"x":1}"""], TextReader.Null, output, TextWriter.Null));
        Assert.Contains("takes no options", output.ToString());
    }

    [Fact]
    public void The_panel_refuses_a_check_that_needs_options_and_says_why()
    {
        var w = Workspace();
        Assert.True(w.Show("wares.json"));
        w.Checks.Add(new Choosy());
        Assert.False(w.RunCheck("sample.choosy"));
        Assert.Contains("list", w.Said);
        Assert.Null(w.Checks.Last("sample.choosy"));
    }

    [Fact]
    public void A_long_run_recycles_its_text_instead_of_filling_the_shared_buffer()
    {
        // BRUTAL copies non-u8 text into a fixed 8 MiB buffer the program must empty each frame. 9 MiB over many frames
        // would throw "Cannot grow beyond MaxCapacity" without ImGuiFrame.Begin, as a long-running editor would have.
        using var host = new HeadlessHost(320, 200) { Drawing = false };
        var line = new string('x', 1024);
        host.Frames(() =>
        {
            Brutal.ImGuiApi.ImGui.Begin("long run");
            for (var i = 0; i < 100; i++) Brutal.ImGuiApi.ImGui.Text(line);
            Brutal.ImGuiApi.ImGui.End();
        }, 100);
    }

    [Fact]
    public void The_cli_check_exits_1_unless_clean()
    {
        var map = Path.Combine(root, "map.json");
        File.WriteAllText(map, MapText);
        var output = new StringWriter();
        Assert.Equal(1, Brutal.Authoring.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "--map", map, "check", "links.all"], TextReader.Null, output, TextWriter.Null));
        Assert.Contains("names nothing", output.ToString());
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"code":"J1","ware":"ore"}]}""");
        Assert.Equal(0, Brutal.Authoring.Agent.Cli.Program.Run(["--data", Data, "--schemas", Schemas, "--map", map, "check", "links.all"], TextReader.Null, new StringWriter(), TextWriter.Null));
    }
}
