using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

// Synthetic documents and explicit owner-thread dispatch; no UI, listener, real data or external AI.
public sealed class EditProposalTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private sealed class Probe(string id, TimeSpan? timeout = null) : ICheck
    {
        public CheckDefinition Definition { get; } = new(id, id, "Synthetic proposal check", timeout ?? TimeSpan.FromMinutes(1));
        public TaskCompletionSource<CheckSnapshot> Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CheckReport> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        { Seen.TrySetResult(snapshot); return Answer.Task; }
    }
    private sealed class Fixture
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("a335-proposals-").FullName;
        public DataProject Project { get; }
        public Checks Checks { get; } = new();
        public EditProposals Store { get; }
        public AgentSession Agent { get; }
        public Probe Probe { get; }
        public Fixture(TimeSpan? timeout = null)
        {
            Probe = new("fixture.candidate", timeout);
            var data = Path.Combine(Root, "data"); var schemas = Path.Combine(Root, "schemas");
            Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(data, "sample.json"), "{\"value\":1}");
            File.WriteAllText(Path.Combine(schemas, "sample.schema.json"), "{\"type\":\"object\"}");
            Project = DataProject.Open(data, schemas);
            Checks.Add(Probe);
            Store = new(Project, Checks);
            Agent = new(Project, null, Store);
        }
        public DocumentSession<DataDocument> Session => Project.Find("sample.json")!.Session!;
        public JsonObject Submit(bool check = false)
        {
            var basis = Store.List("sample.json");
            return Store.Submit("sample.json", basis["session"]!.GetValue<string>(), basis["revision"]!.GetValue<long>(),
                [new("set", "/value", JsonSerializer.SerializeToElement(2)), new("set", "/note", JsonSerializer.SerializeToElement("synthetic"))],
                check ? [Probe.Definition.Id] : []);
        }
    }
    // A private synthetic owner thread demonstrates the required host marshalling without implementing a product bridge.
    private sealed class Host : IAsyncDisposable
    {
        private readonly BlockingCollection<Action<Fixture>> queue = new();
        private readonly Thread thread;
        private readonly TaskCompletionSource<Fixture> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Host(TimeSpan? timeout = null)
        {
            thread = new Thread(() =>
            {
                Fixture f;
                try { f = new(timeout); ready.SetResult(f); }
                catch (Exception e) { ready.SetException(e); return; }
                foreach (var action in queue.GetConsumingEnumerable()) action(f);
            }) { IsBackground = true, Name = "A335 synthetic owner" };
            thread.Start();
        }
        public async Task<T> On<T>(Func<Fixture, T> action)
        {
            await ready.Task.WaitAsync(Guard);
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(f => { try { done.SetResult(action(f)); } catch (Exception e) { done.SetException(e); } });
            return await done.Task.WaitAsync(Guard);
        }
        public Task Do(Action<Fixture> action) => On(f => { action(f); return true; });
        public async Task Finished(string id)
        {
            using var timeout = new CancellationTokenSource(Guard);
            while (await On(f => f.Store.Show(id)["checks"]!.AsArray().Any(c => c!["state"]!.GetValue<string>() is "Running" or "Cancelling")))
                await Task.Delay(1, timeout.Token);
        }
        public async ValueTask DisposeAsync()
        {
            var f = await ready.Task.WaitAsync(Guard);
            f.Probe.Answer.TrySetResult(new(true, [], "fixture cleanup"));
            var ids = await On(x => x.Store.List()["proposals"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ToArray());
            foreach (var id in ids) await Finished(id);
            await Do(x => x.Store.Dispose());
            queue.CompleteAdding();
            Assert.True(await Task.Run(() => thread.Join(Guard)));
            queue.Dispose();
            var full = Path.GetFullPath(f.Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(temp, full, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("a335-proposals-", Path.GetFileName(full));
            Directory.Delete(full, true);
        }
    }
    private static string Id(JsonObject p) => p["id"]!.GetValue<string>();
    private static string Digest(JsonObject p) => p["digest"]!.GetValue<string>();

    [Fact]
    public async Task Accepted_batch_is_one_undo_step_and_never_saves()
    {
        await using var h = new Host();
        await h.Do(f =>
        {
            var p = f.Submit(); Assert.Null(p["error"]);
            Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
            var before = f.Session.History.UndoCount;
            Assert.Equal("accepted", f.Store.AcceptLocal(Id(p), Digest(p))["state"]!.GetValue<string>());
            Assert.Equal(before + 1, f.Session.History.UndoCount);
            Assert.Equal(2, f.Session.Current.At("/value")!.Value.GetInt32());
            Assert.True(f.Session.Undo());
            Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
            Assert.Null(f.Session.Current.At("/note"));
            Assert.Equal("{\"value\":1}", File.ReadAllText(Path.Combine(f.Root, "data", "sample.json")));
        });
    }

    [Fact]
    public async Task Intervening_edit_reject_and_wrong_digest_never_apply()
    {
        await using var h = new Host();
        await h.Do(f =>
        {
            var stale = f.Submit();
            Assert.True(f.Session.Execute(new SetValue("/value", JsonSerializer.SerializeToElement(9))));
            Assert.Contains("stale", f.Store.AcceptLocal(Id(stale), Digest(stale))["error"]!.GetValue<string>());
            var rejected = f.Submit();
            Assert.NotNull(f.Store.AcceptLocal(Id(rejected), "wrong")["error"]);
            Assert.Equal("rejected", f.Store.Reject(Id(rejected), Digest(rejected))["state"]!.GetValue<string>());
            Assert.NotNull(f.Store.AcceptLocal(Id(rejected), Digest(rejected))["error"]);
            Assert.Equal(9, f.Session.Current.At("/value")!.Value.GetInt32());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checks_read_candidate_not_saved_data_and_errors_block_but_warnings_do_not(bool error)
    {
        await using var h = new Host();
        var p = await h.On(f => f.Submit(check: true));
        var probe = await h.On(f => f.Probe);
        var snapshot = await probe.Seen.Task.WaitAsync(Guard);
        using var json = JsonDocument.Parse(snapshot.Text("data/sample.json")!);
        Assert.Equal(2, json.RootElement.GetProperty("value").GetInt32());
        await h.Do(f =>
        {
            Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
            Assert.Null(f.Checks.Last(f.Probe.Definition.Id));
            Assert.NotNull(f.Store.AcceptLocal(Id(p), Digest(p))["error"]);
        });
        probe.Answer.SetResult(new(true, [new(error ? Issue.Error("synthetic error") : Issue.Warning("synthetic warning"), null, "review fixture")], "candidate inspected"));
        await h.Finished(Id(p));
        await h.Do(f =>
        {
            Assert.Single(f.Store.Show(Id(p))["checks"]![0]!["findings"]!.AsArray());
            var result = f.Store.AcceptLocal(Id(p), Digest(p));
            if (error) { Assert.NotNull(result["error"]); Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32()); }
            else Assert.Equal("accepted", result["state"]!.GetValue<string>());
        });
    }

    [Fact]
    public async Task Incomplete_and_changed_project_receipts_do_not_certify()
    {
        await using var h = new Host();
        var p = await h.On(f => f.Submit(check: true));
        var probe = await h.On(f => f.Probe);
        await probe.Seen.Task.WaitAsync(Guard);
        probe.Answer.SetResult(new(false, [], "not all inspected"));
        await h.Finished(Id(p));
        await h.Do(f => Assert.Contains("incomplete", f.Store.AcceptLocal(Id(p), Digest(p))["error"]!.GetValue<string>()));
        await h.Do(f =>
        {
            var other = f.Submit();
            File.WriteAllText(Path.Combine(f.Root, "schemas", "extra.schema.json"), "{}");
            Assert.Contains("inputs changed", f.Store.AcceptLocal(Id(other), Digest(other))["error"]!.GetValue<string>());
        });
    }

    [Fact]
    public async Task Remote_accept_and_unmarshalled_calls_are_refused()
    {
        await using var h = new Host();
        var store = await h.On(f => f.Store);
        Assert.Contains("editor thread", store.List()["error"]!.GetValue<string>());
        await h.Do(f =>
        {
            var p = f.Submit();
            Assert.NotNull(f.Agent.AcceptProposal(Id(p), Digest(p))["error"]);
            Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
            Assert.NotNull(new AgentSession(f.Project).ListProposals()["error"]);
        });
    }
    [Fact]
    public async Task Timed_out_candidate_check_blocks_acceptance()
    {
        await using var h = new Host(TimeSpan.FromMilliseconds(25));
        var p = await h.On(f => f.Submit(check: true));
        var probe = await h.On(f => f.Probe);
        await probe.Seen.Task.WaitAsync(Guard);
        await h.Finished(Id(p));
        await h.Do(f =>
        {
            Assert.Equal("TimedOut", f.Store.Show(Id(p))["checks"]![0]!["state"]!.GetValue<string>());
            Assert.NotNull(f.Store.AcceptLocal(Id(p), Digest(p))["error"]);
            Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
        });
    }

    [Fact]
    public async Task Reject_cancels_pending_check_and_never_applies_late_result()
    {
        await using var h = new Host();
        var p = await h.On(f => f.Submit(check: true));
        var probe = await h.On(f => f.Probe);
        await probe.Seen.Task.WaitAsync(Guard);
        await h.Do(f => Assert.Equal("rejected", f.Store.Reject(Id(p), Digest(p))["state"]!.GetValue<string>()));
        await h.Finished(Id(p));
        await h.Do(f =>
        {
            Assert.Equal("Cancelled", f.Store.Show(Id(p))["checks"]![0]!["state"]!.GetValue<string>());
            Assert.NotNull(f.Store.AcceptLocal(Id(p), Digest(p))["error"]);
            Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
        });
    }

    [Fact]
    public async Task Hosted_typed_submission_rejects_unknown_operation_fields()
    {
        await using var h = new Host();
        await h.Do(f =>
        {
            var basis = f.Agent.ListProposals("sample.json");
            var token = basis["session"]!.GetValue<string>();
            var revision = basis["revision"]!.GetValue<long>();
            var bad = f.Agent.SubmitProposal("sample.json", token, revision,
                "[{\"op\":\"set\",\"at\":\"/value\",\"value\":2,\"local\":true}]", "[]");
            Assert.NotNull(bad["error"]);
            var good = f.Agent.SubmitProposal("sample.json", token, revision,
                "[{\"op\":\"set\",\"at\":\"/value\",\"value\":2}]", "[]");
            Assert.Null(good["error"]);
            Assert.Equal("pending", good["state"]!.GetValue<string>());
            Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
            Assert.Contains("No checks selected", good["checkCoverage"]!.GetValue<string>());
        });
    }

}
