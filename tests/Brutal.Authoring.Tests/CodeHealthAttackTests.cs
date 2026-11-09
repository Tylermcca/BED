using System.Collections.Concurrent;
using Brutal.Authoring.Data;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

// INVENTED: independent bounded lifetime/isolation attacks. BORROWED: existing public Checks contracts.
public sealed class CodeHealthAttackTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("a185-checks-").FullName;
    private readonly ITestOutputHelper output;
    private DataProject Project { get; }
    public CodeHealthAttackTests(ITestOutputHelper output)
    {
        this.output = output;
        var data = Path.Combine(root, "data"); var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
        File.WriteAllText(Path.Combine(data, "toy.json"), "{\"value\":0}");
        File.WriteAllText(Path.Combine(schemas, "toy.schema.json"), "{}");
        Project = DataProject.Open(data, schemas);
    }
    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("a185-checks-", Path.GetFileName(root)); Directory.Delete(root, true);
    }
    private sealed class Controlled(string id, bool fault = false, int timeoutMs = 15000) : ICheck
    {
        public CheckDefinition Definition { get; } = new(id, id, "Self-authored bounded check.", TimeSpan.FromMilliseconds(timeoutMs));
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TokenRegistrationWorked { get; private set; }
        public async Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken token)
        {
            progress.Report(new CheckProgress(.25, "ready")); Entered.TrySetResult();
            await Release.Task;
            using var registration = token.Register(() => { });
            TokenRegistrationWorked = true; // token source must outlive the lingering worker
            progress.Report(new CheckProgress(.75, "late"));
            if (fault) throw new InvalidOperationException("a185-late-fault");
            return new CheckReport(true, [], "late clean result");
        }
    }
    private sealed class QueueCheck(string id, ConcurrentQueue<Controlled> queue) : ICheck
    {
        public CheckDefinition Definition { get; } = new(id, id, "Independent queued check.", TimeSpan.FromSeconds(15));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken token) =>
            queue.TryDequeue(out var next) ? next.RunAsync(snapshot, progress, token) : throw new InvalidOperationException("test queue exhausted");
    }
    private CheckRun Start(Checks checks, string id)
    {
        var run = checks.Start(id, Project, out var why); Assert.True(run is not null, why); return run!;
    }
    private static Task End(CheckRun run) => run.Ended.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task Enter(Controlled c) => c.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Late_success_after_cancel_cannot_revive_report_progress_or_clean_verdict()
    {
        var c = new Controlled("late-success"); var checks = new Checks().Add(c); var run = Start(checks, c.Definition.Id);
        try
        {
            await Enter(c); Assert.True(run.Cancel()); await End(run);
            Assert.True(run.Lingering); Assert.Equal("ready", run.Progress!.Message);
            c.Release.TrySetResult(); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(CheckState.Cancelled, run.State); Assert.Null(run.Report); Assert.Equal("ready", run.Progress.Message);
            Assert.False(run.Clean(Project)); Assert.False(run.Lingering); Assert.True(c.TokenRegistrationWorked); Assert.False(run.Cancel());
            output.WriteLine("late-success: Cancelled/report-null/progress-ready/not-clean; Worker finishes, token still registerable");
        }
        finally { c.Release.TrySetResult(); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task Late_fault_after_timeout_is_observable_but_never_replaces_terminal_state()
    {
        var c = new Controlled("late-fault", fault: true, timeoutMs: 30); var checks = new Checks().Add(c); var run = Start(checks, c.Definition.Id);
        try
        {
            await Enter(c); await End(run); Assert.Equal(CheckState.TimedOut, run.State); Assert.True(run.Lingering);
            c.Release.TrySetResult();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => run.Worker.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("a185-late-fault", ex.Message); Assert.True(c.TokenRegistrationWorked);
            Assert.Equal(CheckState.TimedOut, run.State); Assert.Null(run.Report); Assert.False(run.Clean(Project)); Assert.False(run.Lingering);
            output.WriteLine("late-fault: Worker exposes original exception; TimedOut/report-null unchanged; token lives until Worker ends");
            // Awaiting Worker observes this fault itself: this test makes no separate GC/unobserved-event claim.
        }
        finally
        {
            c.Release.TrySetResult();
            try { await run.Worker.WaitAsync(TimeSpan.FromSeconds(10)); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task Many_start_cancel_release_cycles_do_not_leave_phantom_lingering_workers()
    {
        var queue = new ConcurrentQueue<Controlled>(); var checks = new Checks().Add(new QueueCheck("cycle", queue));
        for (var i = 0; i < 40; i++)
        {
            var c = new Controlled("cycle-worker"); queue.Enqueue(c); var run = Start(checks, "cycle");
            try
            {
                await Enter(c); Assert.True(run.Cancel()); await End(run); Assert.True(run.Lingering);
                c.Release.TrySetResult(); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(CheckState.Cancelled, run.State); Assert.Equal(0, checks.LingeringRuns("cycle"));
            }
            finally { c.Release.TrySetResult(); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10)); }
        }
        output.WriteLine("40 sequential start/cancel/release cycles: terminal Cancelled, no phantom lingering, no unbounded retained workers");
    }

    [Fact]
    public async Task Lingering_count_includes_both_cancelled_workers_before_another_start_attempt()
    {
        var first = new Controlled("one"); var second = new Controlled("two");
        var queue = new ConcurrentQueue<Controlled>([first, second]);
        var checks = new Checks().Add(new QueueCheck("count", queue)); var runs = new List<CheckRun>();
        try
        {
            foreach (var c in new[] { first, second })
            {
                var run = Start(checks, "count"); runs.Add(run); await Enter(c); Assert.True(run.Cancel()); await End(run);
            }
            Assert.All(runs, run => Assert.True(run.Lingering));
            var actual = checks.LingeringRuns("count");
            output.WriteLine($"lingering-introspection: actual={actual}, observedWorkerCount={runs.Count(run => run.Lingering)}; no third Start attempted");
            Assert.Equal(Checks.MaxLingering, actual);
        }
        finally
        {
            first.Release.TrySetResult(); second.Release.TrySetResult();
            await Task.WhenAll(runs.Select(run => run.Worker)).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Repeated_refused_starts_respect_the_limit_and_releasing_one_worker_allows_retry()
    {
        var first = new Controlled("first"); var second = new Controlled("second"); var third = new Controlled("third");
        var queue = new ConcurrentQueue<Controlled>([first, second, third]);
        var checks = new Checks().Add(new QueueCheck("budget", queue));
        var runs = new List<CheckRun>();
        try
        {
            foreach (var c in new[] { first, second })
            {
                var r = Start(checks, "budget"); runs.Add(r); await Enter(c); Assert.True(r.Cancel()); await End(r);
            }
            for (var i = 0; i < 30; i++)
            {
                Assert.Null(checks.Start("budget", Project, out var why)); Assert.Contains("still working", why);
                Assert.Equal(Checks.MaxLingering, checks.LingeringRuns("budget"));
            }
            first.Release.TrySetResult(); await runs[0].Worker.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, checks.LingeringRuns("budget"));
            var retry = Start(checks, "budget"); runs.Add(retry); await Enter(third); third.Release.TrySetResult(); await End(retry);
            Assert.Equal(CheckState.Completed, retry.State); Assert.True(retry.Clean(Project));
            output.WriteLine("per-registry budget: two lingering; 30 refusals add none; release one permits a completed retry");
        }
        finally
        {
            first.Release.TrySetResult(); second.Release.TrySetResult(); third.Release.TrySetResult();
            await Task.WhenAll(runs.Select(r => r.Worker)).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Two_registries_sharing_one_project_do_not_share_cancellation_or_last_result()
    {
        var a = new Controlled("same"); var b = new Controlled("same"); var left = new Checks().Add(a); var right = new Checks().Add(b);
        var ra = Start(left, "same"); var rb = Start(right, "same");
        try
        {
            await Task.WhenAll(Enter(a), Enter(b)); Assert.True(ra.Cancel()); await End(ra);
            Assert.False(rb.IsOver); Assert.Same(ra, left.Last("same")); Assert.Same(rb, right.Last("same"));
            b.Release.TrySetResult(); await End(rb); Assert.Equal(CheckState.Completed, rb.State); Assert.True(rb.Clean(Project));
            a.Release.TrySetResult(); await ra.Worker.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(CheckState.Cancelled, ra.State);
            File.AppendAllText(Path.Combine(root, "data/toy.json"), " ");
            Assert.True(rb.IsStale(Project)); Assert.False(rb.Clean(Project));
            output.WriteLine("shared project: independent registry last/cancel/result; real outside change stales the completed result");
        }
        finally
        {
            a.Release.TrySetResult(); b.Release.TrySetResult();
            await Task.WhenAll(ra.Worker, rb.Worker).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(1000)]
    public async Task Cancel_timeout_races_end_once_and_ignore_late_success(int timeoutMs)
    {
        var c = new Controlled("race", timeoutMs: timeoutMs); var checks = new Checks().Add(c); var run = Start(checks, c.Definition.Id);
        try
        {
            await Enter(c); run.Cancel(); await End(run);
            var terminal = run.State; Assert.Contains(terminal, new[] { CheckState.Cancelled, CheckState.TimedOut }); Assert.True(run.Lingering);
            c.Release.TrySetResult(); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10)); await End(run);
            Assert.Equal(terminal, run.State); Assert.Null(run.Report); Assert.False(run.Clean(Project)); Assert.False(run.Cancel());
            output.WriteLine($"cancel/timeout({timeoutMs}ms): {terminal}; Ended stable, late success/progress ignored");
        }
        finally { c.Release.TrySetResult(); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
}
