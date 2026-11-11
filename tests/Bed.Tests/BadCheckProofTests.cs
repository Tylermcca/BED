using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

// INVENTED adversarial checks on self-authored JSON. All held workers are released in finally.
// The current agent protocol calls both operations "check": no first starts; first=0 reads that run.
[Collection(ImGuiCollection.Name)]
public sealed class BadCheckProofTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("bad-check-proof-").FullName;
    private const string Id = "probe.bad";
    private DataProject Project()
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        File.WriteAllText(Path.Combine(data, "items.json"), "{\"$schema\":\"any.schema.json\",\"rows\":[{\"id\":\"first\",\"value\":1}]}");
        File.WriteAllText(Path.Combine(schemas, "any.schema.json"), "{}");
        return DataProject.Open(data, schemas);
    }
    public void Dispose() => Directory.Delete(root, true);
    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Probe(Func<CheckSnapshot, IProgress<CheckProgress>, CancellationToken, Task<CheckReport>> body,
        double seconds = 30, IReadOnlyList<string>? reads = null, string id = Id, string label = "Bad probe") : ICheck
    {
        public CheckDefinition Definition { get; } = new(id, label, "Self-authored adversarial check.", TimeSpan.FromSeconds(seconds), Reads: reads);
        public Task<CheckReport> RunAsync(CheckSnapshot s, IProgress<CheckProgress> p, CancellationToken c) => body(s, p, c);
    }
    private static CheckReport Clean() => new(true, [], "Self-authored clean result.");
    private static JsonObject Call(AgentSession a, bool read = false, int limit = 500)
    {
        var args = new JsonObject { ["id"] = Id, ["limit"] = limit }; if (read) args["first"] = 0;
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = "check", ["arguments"] = args } };
        var reply = new McpServer(a).Handle(request.ToJsonString())!;
        return JsonNode.Parse(reply["result"]!["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
    }
    private static async Task End(CheckRun run) => await run.Ended.WaitAsync(TimeSpan.FromSeconds(8));
    private static void Draw(DataWorkspace w)
    {
        ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(1100, 900));
        ImGui.Begin("Bad check proof"); w.ChecksPanel.Draw(); ImGui.End();
    }
    private static string Text(HeadlessHost host, DataWorkspace w)
    {
        host.Frame(() => { ImGui.LogToClipboard(); Draw(w); ImGui.LogFinish(); });
        return ImGui.GetClipboardText().ToString() ?? ""; // HeadlessHost owns an in-memory clipboard, never the OS clipboard.
    }
    private void NoCrashes(params (string Route, Exception? Error)[] attempts)
    {
        foreach (var (route, error) in attempts) output.WriteLine(route + ": " + (error is null ? "OK" : error.GetType().Name + ": " + error.Message[..Math.Min(300, error.Message.Length)]));
        Assert.All(attempts, x => Assert.Null(x.Error));
    }
    private static void Parity(DataWorkspace w, AgentSession a, JsonObject result, string panel)
    {
        var run = w.Checks.Last(Id)!; var other = a.Checks.Last(Id)!;
        Assert.Equal(run.State.ToString(), result["state"]!.GetValue<string>());
        Assert.Equal(run.Verdict(w.Project), result["verdict"]!.GetValue<string>());
        Assert.Equal(run.Clean(w.Project), result["clean"]!.GetValue<bool>());
        Assert.Equal(run.Captured?.InputHash, result["inputHash"]?.GetValue<string>());
        var verdict = result["verdict"]!.GetValue<string>();
        if (verdict.EndsWith(" (stale)", StringComparison.Ordinal))
        {
            Assert.Contains("Bad probe: " + verdict[..^8], panel);
            Assert.Contains("Results are out of date", panel);
        }
        else Assert.Contains("Bad probe: " + verdict, panel);
        if (run.Problem is { } problem) { Assert.Equal(problem, result["problem"]!.GetValue<string>()); Assert.Contains(problem, panel); }
        Assert.Same(other, a.Checks.Last(Id));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Throws_before_or_after_progress_are_failed_reports_for_both_routes(bool afterProgress)
    {
        var p = Project(); var w = new DataWorkspace(p); var a = new AgentSession(p);
        Probe Make() => new((_, progress, _) => { if (afterProgress) progress.Report(new(.4, "before exception")); throw new IOException("self-authored crash"); });
        w.Checks.Add(Make()); a.Checks.Add(Make()); Assert.True(w.RunCheck(Id)); await End(w.Checks.Last(Id)!);
        JsonObject? result = null; var agentError = Record.Exception(() => result = Call(a));
        using var host = new HeadlessHost(1100, 900) { Drawing = false }; string panel = "";
        var panelError = Record.Exception(() => { host.Frames(() => Draw(w), 2); panel = Text(host, w); });
        NoCrashes(("agent new run", agentError), ("panel", panelError));
        Assert.Equal(CheckState.Failed, w.Checks.Last(Id)!.State); Assert.False(w.Checks.Last(Id)!.Clean(p)); Parity(w, a, result!, panel);
        var old = a.Checks.Last(Id); var reread = Call(a, read: true); Assert.Same(old, a.Checks.Last(Id)); Assert.Equal(result!["problem"]!.ToJsonString(), reread["problem"]!.ToJsonString());
    }
    [Fact]
    public async Task Throwing_after_cancel_cannot_replace_the_cancelled_result_for_either_route()
    {
        var p = Project(); var w = new DataWorkspace(p); var a = new AgentSession(p); var entered = Gate(); var release = Gate();
        Probe Make() => new(async (_, progress, _) => { progress.Report(new(.2, "held before late throw")); entered.TrySetResult(true); await release.Task; throw new IOException("late self-authored crash"); });
        w.Checks.Add(Make()); a.Checks.Add(Make()); var workers = new List<Task>();
        try
        {
            Assert.True(w.RunCheck(Id)); var run = w.Checks.Last(Id)!; workers.Add(run.Worker); await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(run.Cancel()); await End(run); Assert.Equal(CheckState.Cancelled, run.State);
            entered = Gate(); var agentTask = Task.Run(() => Call(a));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(8)); var ar = a.Checks.Last(Id)!; workers.Add(ar.Worker);
            Assert.True(ar.Cancel()); await End(ar); var result = await agentTask.WaitAsync(TimeSpan.FromSeconds(8));
            release.SetResult(true); try { await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(8)); } catch (IOException) { }
            Assert.All(new[] { run, ar }, r => { Assert.Equal(CheckState.Cancelled, r.State); Assert.Null(r.Report); Assert.False(r.Lingering); });
            using var host = new HeadlessHost(1100, 900) { Drawing = false }; host.Frames(() => Draw(w), 2); Parity(w, a, result, Text(host, w));
            Assert.Equal("Cancelled", Call(a, read: true)["state"]!.GetValue<string>());
        }
        finally { release.TrySetResult(true); foreach (var t in workers) { try { await t.WaitAsync(TimeSpan.FromSeconds(8)); } catch (IOException) { } } }
    }
    private sealed class UnreliableReads(string path) : IReadOnlyList<string>
    {
        internal bool Broken;
        public int Count => 1;
        public string this[int i] => i == 0 ? path : throw new IndexOutOfRangeException();
        public IEnumerator<string> GetEnumerator() { if (Broken) throw new IOException("self-authored Reads crash"); return ((IEnumerable<string>)new[] { path }).GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Reads_that_throw_at_start_or_when_a_completed_result_is_read_never_crash(bool afterRun)
    {
        var p = Project(); var path = Path.Combine(root, "outside.txt"); File.WriteAllText(path, "self-authored outside input");
        var wr = new UnreliableReads(path) { Broken = !afterRun }; var ar = new UnreliableReads(path) { Broken = !afterRun };
        var w = new DataWorkspace(p); var a = new AgentSession(p);
        w.Checks.Add(new Probe((_, _, _) => Task.FromResult(Clean()), reads: wr)); a.Checks.Add(new Probe((_, _, _) => Task.FromResult(Clean()), reads: ar));
        Assert.True(w.RunCheck(Id)); await End(w.Checks.Last(Id)!); _ = Call(a);
        wr.Broken = ar.Broken = true; JsonObject? result = null;
        var agentError = Record.Exception(() => result = Call(a, read: true));
        using var host = new HeadlessHost(1100, 900) { Drawing = false }; string panel = "";
        var panelError = Record.Exception(() => panel = Text(host, w));
        NoCrashes(("agent read result", agentError), ("panel", panelError));
        Assert.False(result!["clean"]!.GetValue<bool>()); Assert.False(w.Checks.Last(Id)!.Clean(p));
        Parity(w, a, result, panel);
    }
    private static CheckReport BadReport(string kind)
    {
        CheckFinding Finding(string message, string file = "items.json", string at = "/rows/0/value") => new(Issue.Error(message, path: at), new(file, At: at), "Fix the self-authored probe.");
        return kind switch
        {
            "null-findings" => new(true, null!, "Null findings"),
            "null-finding" => new(true, [null!], "Null entry"),
            "null-issue" => new(true, [new(null!, new("items.json"), "Fix null problem")], "Null problem"),
            "missing-file" => new(true, [Finding("Missing file place", "absent.json")], "Bad place"),
            "invalid-pointer" => new(true, [Finding("Malformed pointer", at: "/rows/~2broken")], "Bad pointer"),
            "nowhere-pointer" => new(true, [Finding("Missing pointer target", at: "/rows/999/value")], "Missing place"),
            "non-pointer" => new(true, [Finding("A foreign item id", at: "a foreign id")], "Foreign place"),
            "many" => new(true, Enumerable.Range(0, 10000).Select(i => Finding("Self-authored finding " + i)).ToArray(), "10000 self-authored findings"),
            "huge" => new(true, [Finding(new string('x', 12 * 1024 * 1024))], "A 12 MiB self-authored message"),
            _ => throw new ArgumentException(kind)
        };
    }
    private async Task ReportRoutes(string kind)
    {
        var p = Project(); var w = new DataWorkspace(p); var a = new AgentSession(p);
        w.Checks.Add(new Probe((_, _, _) => Task.FromResult(BadReport(kind)))); a.Checks.Add(new Probe((_, _, _) => Task.FromResult(BadReport(kind))));
        Assert.True(w.RunCheck(Id)); var run = w.Checks.Last(Id)!; await End(run);
        JsonObject? result = null; var agentError = Record.Exception(() => result = Call(a));
        var resultError = Record.Exception(() => _ = Call(a, read: true));
        using var host = new HeadlessHost(1100, 900) { Drawing = false }; string panel = "";
        var panelError = Record.Exception(() => { host.Frames(() => Draw(w), 2); panel = Text(host, w); });
        Exception? revealError = null;
        if (kind is "missing-file" or "invalid-pointer" or "nowhere-pointer" or "non-pointer")
            revealError = Record.Exception(() => { foreach (var f in run.Report!.Findings) if (!w.Reveal(f)) Assert.False(string.IsNullOrWhiteSpace(w.Said)); });
        NoCrashes(("agent new run", agentError), ("agent read result", resultError), ("panel", panelError), ("reveal", revealError));
        Parity(w, a, result!, panel); Assert.False(result!["clean"]!.GetValue<bool>());
        if (kind == "many")
        {
            Assert.Equal(100, w.FindingsShown(Id)); Assert.Contains("Showing 100 of 10000", panel);
            Assert.Equal(10000, result["total"]!.GetValue<int>()); Assert.Equal(500, result["findings"]!.AsArray().Count);
            var ordinal = 0;
            for (var page = result; ; )
            {
                foreach (var f in page["findings"]!.AsArray()) { Assert.Equal("Self-authored finding " + ordinal, f!["message"]!.GetValue<string>()); Assert.Equal(ordinal++, f["ordinal"]!.GetValue<int>()); }
                if (page["nextFirst"] is not { } next) break;
                page = a.RunCheck(Id, next.GetValue<int>(), 500);
            }
            Assert.Equal(10000, ordinal); for (int i = 0; i < 99; ++i) w.ShowMoreFindings(Id); Assert.Equal(10000, w.FindingsShown(Id));
        }
        if (run.Report is { } report && kind != "many") Assert.Equal(report.Findings.Count, result["findings"]!.AsArray().Count);
    }
    [Theory]
    [InlineData("null-findings")] [InlineData("null-finding")] [InlineData("null-issue")]
    [InlineData("missing-file")] [InlineData("invalid-pointer")] [InlineData("nowhere-pointer")] [InlineData("non-pointer")] [InlineData("many")]
    public Task Bad_reports_are_contained_and_panel_and_agent_agree(string kind) => ReportRoutes(kind);
    [Fact]
    public Task Huge_message_never_crashes_the_panel_or_agent() => ReportRoutes("huge");
    [Fact]
    public async Task Out_of_range_progress_is_ignored_and_backwards_progress_stays_well_formed()
    {
        var p = Project(); var w = new DataWorkspace(p); var a = new AgentSession(p); var entered = Gate(); var release = Gate(); var workers = new List<Task>();
        Probe Make() => new(async (_, progress, _) =>
        {
            progress.Report(new(.8, "eighty"));
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -1, 2 }) progress.Report(new(bad, "invalid"));
            progress.Report(null!); entered.TrySetResult(true); await release.Task;
            progress.Report(new(.2, "twenty after eighty")); return Clean();
        });
        w.Checks.Add(Make()); a.Checks.Add(Make());
        try
        {
            Assert.True(w.RunCheck(Id)); var run = w.Checks.Last(Id)!; workers.Add(run.Worker); await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(.8, run.Progress!.Fraction); Assert.Equal("eighty", run.Progress.Message);
            using var host = new HeadlessHost(1100, 900) { Drawing = false }; host.Frames(() => Draw(w), 2); Assert.Contains("eighty", Text(host, w));
            entered = Gate(); var agentTask = Task.Run(() => Call(a)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(8)); var ar = a.Checks.Last(Id)!; workers.Add(ar.Worker); Assert.Equal(.8, ar.Progress!.Fraction);
            release.SetResult(true); await End(run); var answer = await agentTask.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.All(new[] { run, ar }, r => { Assert.Equal(.2, r.Progress!.Fraction); Assert.Equal("twenty after eighty", r.Progress.Message); });
            Parity(w, a, answer, Text(host, w)); Assert.True(answer["clean"]!.GetValue<bool>());
        }
        finally { release.TrySetResult(true); await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(8)); }
    }
    [Theory]
    [InlineData("edit")] [InlineData("save")] [InlineData("rename")] [InlineData("delete")]
    public async Task Racing_mutations_are_stale_in_both_real_routes(string operation)
    {
        var p = Project(); var w = new DataWorkspace(p); var a = new AgentSession(p); var enteredW = Gate(); var enteredA = Gate(); var release = Gate(); var workers = new List<Task>();
        Probe Make(TaskCompletionSource<bool> entered) => new(async (_, _, _) => { entered.SetResult(true); await release.Task; return Clean(); });
        w.Checks.Add(Make(enteredW)); a.Checks.Add(Make(enteredA));
        try
        {
            Assert.True(w.RunCheck(Id)); var run = w.Checks.Last(Id)!; workers.Add(run.Worker); await enteredW.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var agentTask = Task.Run(() => Call(a)); await enteredA.Task.WaitAsync(TimeSpan.FromSeconds(8)); workers.Add(a.Checks.Last(Id)!.Worker);
            var file = p.Find("items.json")!;
            if (operation is "edit" or "save") { Assert.True(file.Open()); Assert.True(file.Session!.Execute(new SetValue("/rows/0/value", JsonSerializer.SerializeToElement(22)))); if (operation == "save") Assert.True(file.Save()); }
            if (operation == "rename") Assert.True(p.Rename("items.json", "renamed.json", out var why), why);
            if (operation == "delete") Assert.True(p.Delete("items.json", out var why), why);
            release.SetResult(true); await End(run); var answer = await agentTask.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(run.IsStale(p)); Assert.False(run.Clean(p)); Assert.Contains("(stale)", answer["verdict"]!.GetValue<string>()); Assert.False(answer["clean"]!.GetValue<bool>());
            using var host = new HeadlessHost(1100, 900) { Drawing = false }; host.Frames(() => Draw(w), 2); Parity(w, a, answer, Text(host, w));
            var read = Call(a, read: true); Assert.True(read["stale"]!.GetValue<bool>()); Assert.Contains("changed", read["next"]!.GetValue<string>());
        }
        finally { release.TrySetResult(true); await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(8)); }
    }
    [Fact]
    public async Task Ignored_cancel_and_timeout_hit_the_lingering_cap_without_holding_the_panel_frame()
    {
        var p = Project(); var w = new DataWorkspace(p); var a = new AgentSession(p); var release = Gate(); var workers = new List<Task>();
        Probe Make() => new(async (_, _, _) => { await release.Task; return Clean(); }, seconds: .25);
        w.Checks.Add(Make()); a.Checks.Add(Make());
        try
        {
            for (int i = 0; i < Checks.MaxLingering; ++i)
            {
                Assert.True(w.RunCheck(Id)); var run = w.Checks.Last(Id)!; workers.Add(run.Worker);
                if (i == 0) { Assert.True(run.Cancel()); await End(run); Assert.Equal(CheckState.Cancelled, run.State); }
                else { await End(run); Assert.Equal(CheckState.TimedOut, run.State); }
                Assert.True(run.Lingering); Assert.Equal(i + 1, w.Checks.LingeringRuns(Id));
                var answer = await Task.Run(() => Call(a)).WaitAsync(TimeSpan.FromSeconds(8)); var ar = a.Checks.Last(Id)!; workers.Add(ar.Worker);
                Assert.Equal("TimedOut", answer["state"]!.GetValue<string>()); Assert.Equal(i + 1, a.Checks.LingeringRuns(Id));
            }
            Assert.False(w.RunCheck(Id)); Assert.Contains("still working", w.Said);
            var refused = Call(a); Assert.Equal(w.Said, refused["error"]!.GetValue<string>()); Assert.False(string.IsNullOrWhiteSpace(refused["next"]!.GetValue<string>()));
            using var host = new HeadlessHost(1100, 900) { Drawing = false }; host.Frames(() => Draw(w), 5);
            var times = new List<double>(); for (int i = 0; i < 20; ++i) { var timer = Stopwatch.StartNew(); host.Frame(() => Draw(w)); times.Add(timer.Elapsed.TotalMilliseconds); }
            times.Sort(); output.WriteLine($"Lingering panel: median={times[10]:F3}ms; p95={times[18]:F3}ms; 20 headless logic frames, no rasterisation.");
            Assert.True(times[10] < 100 && times[18] < 250, "The small headless panel must remain responsive while checks ignore stop requests.");
        }
        finally { release.TrySetResult(true); await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(8)); }
        Assert.Equal(0, w.Checks.LingeringRuns(Id)); Assert.Equal(0, a.Checks.LingeringRuns(Id));
    }
    [Theory]
    [InlineData("duplicate")] [InlineData("empty-label")]
    public void Bad_registration_is_atomic_and_same_for_person_and_agent(string kind)
    {
        var p = Project(); var w = new DataWorkspace(p); var a = new AgentSession(p);
        Probe Good() => new((_, _, _) => Task.FromResult(Clean()));
        ICheck[] Batch() => kind == "duplicate" ? [Good(), Good()] : [Good(), new Probe((_, _, _) => Task.FromResult(Clean()), id: "probe.other", label: "")];
        var we = Assert.Throws<ArgumentException>(() => w.Checks.Add(Batch())); var ae = Assert.Throws<ArgumentException>(() => a.Checks.Add(Batch()));
        Assert.Equal(we.Message, ae.Message); Assert.DoesNotContain(w.Checks.Registered, d => d.Id == Id); Assert.DoesNotContain(a.Checks.Registered, d => d.Id == Id);
        Assert.False(w.RunCheck(Id)); Assert.Equal(w.Said, Call(a)["error"]!.GetValue<string>());
        using var host = new HeadlessHost(1100, 900) { Drawing = false }; host.Frames(() => Draw(w), 3);
    }
}
