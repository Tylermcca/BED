using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

// Self-authored data; controlled completion signals, no game assets or expensive checks.
public sealed class CheckCoverageStatusTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private sealed class Controlled(string id) : ICheck
    {
        public CheckDefinition Definition { get; } = new(id, id, "Synthetic status fixture", TimeSpan.FromMinutes(1));
        public int Calls;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CheckReport> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult(true);
            return Answer.Task;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("a331-status-").FullName;
        public DataProject Project { get; }
        public AgentSession Agent { get; }
        public Controlled Fast { get; } = new("fixture.fast");
        public Controlled SaveOmitted { get; } = new("fixture.manual");
        public Controlled Cancelled { get; } = new("fixture.cancelled");
        private readonly List<CheckRun> owned = [];
        public Fixture()
        {
            var data = Path.Combine(root, "data");
            var schemas = Path.Combine(root, "schemas");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(data, "sample.json"), "{\"value\":1}");
            File.WriteAllText(Path.Combine(schemas, "sample.schema.json"), "{\"type\":\"object\"}");
            Project = DataProject.Open(data, schemas);
            Agent = new AgentSession(Project);
            Agent.Checks.Add(Fast, SaveOmitted, Cancelled);
        }
        public CheckRun Start(Controlled check)
        {
            var run = Agent.Checks.Start(check.Definition.Id, Project, out var why);
            Assert.True(run is not null, why);
            owned.Add(run!);
            return run!;
        }
        public void Edit()
        {
            var f = Project.Find("sample.json")!;
            Assert.True(f.Open());
            Assert.True(f.Session!.Execute(new SetValue("/value", JsonSerializer.SerializeToElement(2))));
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var check in new[] { Fast, SaveOmitted, Cancelled })
                check.Answer.TrySetResult(new(true, [], "fixture cleanup"));
            foreach (var run in owned)
            {
                await run.Worker.WaitAsync(Guard);
                await run.Ended.WaitAsync(Guard);
            }
            var full = Path.GetFullPath(root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(temp, full, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("a331-status-", Path.GetFileName(full));
            Directory.Delete(full, true);
        }
    }

    private static JsonObject Entry(JsonObject response, string id) =>
        Assert.IsType<JsonObject>(response["checks"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == id));

    [Fact]
    public async Task Passive_status_and_save_do_not_invent_a_manual_check_receipt()
    {
        await using var f = new Fixture();
        f.Edit();
        f.Agent.Save("sample.json");
        Assert.False(f.Project.Find("sample.json")!.Dirty);
        var response = f.Agent.ListChecks();
        Assert.Contains("Save coverage unknown", response["saveCoverage"]!.GetValue<string>());
        foreach (var check in new[] { f.Fast, f.SaveOmitted, f.Cancelled })
        {
            var entry = Entry(response, check.Definition.Id);
            Assert.Equal("never run", entry["last"]!.GetValue<string>());
            Assert.Null(entry["finishedAt"]);
            Assert.Null(entry["ageSeconds"]);
            Assert.Equal(0, check.Calls);
            Assert.Null(f.Agent.Checks.Last(check.Definition.Id));
        }
        Assert.Null(f.Agent.Checks.Last("links.all"));
        // Omitted by this fixture's save, but the API does not record why. Do not claim expense or missing registration.
        Assert.DoesNotContain("too expensive", response.ToJsonString());
        Assert.DoesNotContain("not registered", response.ToJsonString());
    }

    [Fact]
    public async Task Completion_timestamp_is_stable_across_stale_draft_and_undo()
    {
        await using var f = new Fixture();
        var run = f.Start(f.Fast);
        await f.Fast.Entered.Task.WaitAsync(Guard);
        Assert.Null(run.FinishedAt);
        var running = Entry(f.Agent.ListChecks(), f.Fast.Definition.Id);
        Assert.Equal("running", running["last"]!.GetValue<string>());
        Assert.Null(running["finishedAt"]);
        f.Fast.Answer.SetResult(new(true, [], "synthetic fast result"));
        await run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Completed, run.State);
        var at = Assert.IsType<DateTimeOffset>(run.FinishedAt);
        Assert.True(run.Clean(f.Project));
        var done = Entry(f.Agent.ListChecks(), f.Fast.Definition.Id);
        Assert.Equal(at, DateTimeOffset.Parse(done["finishedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(done["ageSeconds"]!.GetValue<double>() >= 0);
        f.Edit();
        Assert.True(run.IsStale(f.Project));
        Assert.False(run.Clean(f.Project));
        Assert.Contains("stale", Entry(f.Agent.ListChecks(), f.Fast.Definition.Id)["last"]!.GetValue<string>());
        Assert.Equal(at, run.FinishedAt);
        Assert.True(f.Project.Find("sample.json")!.Session!.Undo());
        Assert.False(run.IsStale(f.Project));
        Assert.Equal(at, run.FinishedAt);
        Assert.Equal(1, f.Fast.Calls);
    }

    [Fact]
    public async Task Cancelled_terminal_observation_does_not_wait_for_or_change_after_late_worker()
    {
        await using var f = new Fixture();
        var run = f.Start(f.Cancelled);
        await f.Cancelled.Entered.Task.WaitAsync(Guard);
        run.Cancel();
        await run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Cancelled, run.State);
        Assert.Null(run.Report);
        var at = Assert.IsType<DateTimeOffset>(run.FinishedAt);
        Assert.True(run.Lingering);
        var response = Entry(f.Agent.ListChecks(), f.Cancelled.Definition.Id);
        Assert.Equal("cancelled", response["last"]!.GetValue<string>());
        Assert.NotNull(response["finishedAt"]);
        f.Cancelled.Answer.SetResult(new(true, [], "late answer must not certify"));
        await run.Worker.WaitAsync(Guard);
        Assert.Equal(CheckState.Cancelled, run.State);
        Assert.Equal(at, run.FinishedAt);
        Assert.Null(run.Report);
        Assert.False(run.Clean(f.Project));
    }
}
