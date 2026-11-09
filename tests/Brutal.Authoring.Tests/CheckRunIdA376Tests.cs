using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;
using Xunit;
using Cli = Brutal.Authoring.Agent.Cli.Program;

namespace Brutal.Authoring.Tests;

// Invented fixture and public seams only. Prepared, not executed by this worker.
public sealed class CheckRunIdA376Tests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("run-id-a376-").FullName;
        public DataProject Project { get; }
        public Fixture()
        {
            var data = Path.Combine(Root, "data"); var schemas = Path.Combine(Root, "schemas");
            Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(data, "lanterns.json"), """{"value":1}""");
            File.WriteAllText(Path.Combine(schemas, "lanterns.schema.json"), """{"type":"object"}""");
            Project = DataProject.Open(data, schemas);
        }
        public AgentSession Agent(ICheck check) { var a = new AgentSession(Project); a.Checks.Add(check); return a; }
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("run-id-a376-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup boundary.");
            Directory.Delete(full, true);
        }
    }
    private sealed class Numbered(string id = "lantern.audit") : ICheck
    {
        public int Calls;
        public CheckDefinition Definition { get; } = new(id, "Lantern audit", "Invented findings.", TimeSpan.FromSeconds(5));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            var generation = Interlocked.Increment(ref Calls);
            return Task.FromResult(new CheckReport(true, Enumerable.Range(0, 3).Select(i =>
                new CheckFinding(Issue.Error($"generation{generation}:finding{i}"), null, "Review the lantern.")).ToArray(), "Invented audit."));
        }
    }
    private static string Id(JsonObject result) => result["run"]!.GetValue<string>();
    private static string Message(JsonObject result) => result["findings"]![0]!["message"]!.GetValue<string>();

    [Fact]
    public void Explicit_id_reads_first_run_after_second_finishes()
    {
        using var f = new Fixture(); var c = new Numbered(); var a = f.Agent(c);
        var first = a.RunCheck(c.Definition.Id, limit: 1); var second = a.RunCheck(c.Definition.Id, limit: 1);
        Assert.NotEqual(Id(first), Id(second));
        var page = a.RunCheck(c.Definition.Id, first: 1, limit: 1, runId: Id(first));
        Assert.Equal("generation1:finding1", Message(page)); Assert.Equal(Id(first), Id(page));
        Assert.Equal(2, c.Calls); Assert.Equal(3, page["total"]!.GetValue<int>());
        Assert.Contains("run=" + Id(first), page["next"]!.GetValue<string>());
    }
    [Fact]
    public void Eight_run_bound_evicts_oldest_but_does_not_replace_latest()
    {
        using var f = new Fixture(); var c = new Numbered(); var a = f.Agent(c);
        var old = Id(a.RunCheck(c.Definition.Id));
        JsonObject latest = null!;
        for (var i = 0; i < Checks.RunHistoryLimit; i++) latest = a.RunCheck(c.Definition.Id);
        var refused = a.RunCheck(c.Definition.Id, runId: old);
        Assert.NotNull(refused["error"]); Assert.Contains("evicted", refused["error"]!.GetValue<string>());
        Assert.Equal(Id(latest), Id(a.RunCheck(c.Definition.Id, first: 0)));
        Assert.Equal(9, c.Calls);
    }
    [Fact]
    public void Without_id_first_still_reads_latest_and_absent_first_still_runs()
    {
        using var f = new Fixture(); var c = new Numbered(); var a = f.Agent(c);
        var first = a.RunCheck(c.Definition.Id, first: 0, limit: 1); // legacy missing-last fallback
        var second = a.RunCheck(c.Definition.Id, limit: 1);
        Assert.Equal("generation2:finding1", Message(a.RunCheck(c.Definition.Id, first: 1, limit: 1)));
        Assert.NotEqual(Id(first), Id(second)); Assert.Equal(2, c.Calls);
    }
    [Fact]
    public void Unknown_other_session_wrong_check_and_options_never_start_work()
    {
        using var f = new Fixture(); var c = new Numbered(); var a = f.Agent(c);
        var id = Id(a.RunCheck(c.Definition.Id));
        Assert.NotNull(a.RunCheck(c.Definition.Id, runId: "unknown")["error"]);
        Assert.NotNull(a.RunCheck("other.audit", runId: id)["error"]);
        Assert.NotNull(a.RunCheck(c.Definition.Id, options: "{}", runId: id)["error"]);
        var other = f.Agent(new Numbered());
        Assert.NotNull(other.RunCheck(c.Definition.Id, runId: id)["error"]);
        Assert.Equal(1, c.Calls);
    }
    [Fact]
    public void Stale_id_refuses_and_undo_restores_current_without_rerun()
    {
        using var f = new Fixture(); var c = new Numbered(); var a = f.Agent(c);
        var file = f.Project.Find("lanterns.json")!; Assert.True(file.Open());
        var id = Id(a.RunCheck(c.Definition.Id));
        Assert.True(file.Session!.Execute(new SetValue("/value", JsonSerializer.SerializeToElement(2))));
        Assert.NotNull(a.RunCheck(c.Definition.Id, runId: id)["error"]);
        Assert.True(file.Session.Undo());
        Assert.Equal(id, Id(a.RunCheck(c.Definition.Id, runId: id)));
        Assert.Equal(1, c.Calls);
    }
    [Fact]
    public async Task Proposal_run_is_not_in_ordinary_history()
    {
        using var f = new Fixture(); var c = new Numbered(); var a = f.Agent(c);
        var ordinary = Id(a.RunCheck(c.Definition.Id));
        var proposal = a.Checks.StartProposed(c.Definition.Id, CheckSnapshot.Capture(f.Project), out var why);
        Assert.NotNull(proposal); await proposal!.Ended.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(proposal.RunId); Assert.Equal(ordinary, a.Checks.Last(c.Definition.Id)!.RunId);
        Assert.Equal(ordinary, Id(a.RunCheck(c.Definition.Id, runId: ordinary)));
    }
    [Fact]
    public void Mcp_passes_the_explicit_run_and_exposes_schema()
    {
        using var f = new Fixture(); var c = new Numbered(); var a = f.Agent(c);
        var first = Id(a.RunCheck(c.Definition.Id)); a.RunCheck(c.Definition.Id);
        var server = new McpServer(a);
        var reply = server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = "check", ["arguments"] = new JsonObject {
                ["id"] = c.Definition.Id, ["run"] = first, ["first"] = 1, ["limit"] = 1 } } }.ToJsonString())!;
        var page = JsonNode.Parse(reply["result"]!["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
        Assert.Equal("generation1:finding1", Message(page)); Assert.Equal(2, c.Calls);
        var tool = Assert.Single(McpServer.Tools(), t => t!["name"]!.GetValue<string>() == "check");
        Assert.NotNull(tool!["inputSchema"]!["properties"]!["run"]);
    }
    private sealed class Cancellable : ICheck
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CheckDefinition Definition { get; } = new("lantern.wait", "Wait audit", "Invented cancellable check.", TimeSpan.FromSeconds(30));
        public async Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
            return new(true, [], "Unreachable without cancellation.");
        }
    }
    [Fact]
    public async Task Cancelled_run_keeps_id_and_non_clean_verdict()
    {
        using var f = new Fixture(); var c = new Cancellable(); var a = f.Agent(c);
        var run = a.Checks.Start(c.Definition.Id, f.Project, out var why);
        Assert.NotNull(run);
        await c.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(run!.RunId); Assert.True(run.Cancel());
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(run.RunId);
        var page = a.RunCheck(c.Definition.Id, runId: run.RunId);
        Assert.Equal("Cancelled", page["state"]!.GetValue<string>());
        Assert.False(page["clean"]!.GetValue<bool>());
        try { await run.Worker.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
    }

    [Fact]
    public void One_shot_cli_refuses_run_before_opening_any_project()
    {
        var output = new StringWriter(); var errors = new StringWriter();
        Assert.Equal(2, Cli.Run(["--run", "opaque"], new StringReader(""), output, errors));
        Assert.Equal("", output.ToString());
        Assert.Contains("run ids last only within one session; use MCP or the agent", errors.ToString());
    }
}
