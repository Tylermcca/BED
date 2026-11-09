using System.Collections;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

// A307 rank 15: invented inputs, public check seams and owned completion signals.
// These preserve the pre-refactor contract; they are not a stress test or proof of all memory interleavings.
public sealed class CheckRunPublicationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private sealed class ControlledCheck : ICheck
    {
        public CheckDefinition Definition { get; } = new("publication.sample", "Publication sample",
            "Read self-made sample data only.", TimeSpan.FromMinutes(1));
        public TaskCompletionSource<IProgress<CheckProgress>> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CheckReport> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            Entered.TrySetResult(progress);
            return Answer.Task; // Intentionally ignores cancellation, so physical versus logical completion is controllable.
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "a307-check-publication-" + Guid.NewGuid().ToString("N"));
        public ControlledCheck Check { get; } = new();
        public DataProject Project { get; }
        public CheckRun Run { get; }
        public Fixture()
        {
            var data = Path.Combine(root, "data");
            var schemas = Path.Combine(root, "schemas");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(data, "sample.json"), "{\"value\":1}");
            File.WriteAllText(Path.Combine(schemas, "sample.schema.json"), "{\"type\":\"object\"}");
            Project = DataProject.Open(data, schemas);
            var checks = new Checks().Add(Check);
            Run = checks.Start(Check.Definition.Id, Project, out var why)!;
            Assert.True(Run is not null, why);
        }
        public async ValueTask DisposeAsync()
        {
            Check.Answer.TrySetResult(new(true, [], "Released by fixture cleanup."));
            try { await Run.Worker.WaitAsync(Guard); }
            catch (Exception) when (Run.Worker.IsFaulted || Run.Worker.IsCanceled) { _ = Run.Worker.Exception; }
            await Run.Ended.WaitAsync(Guard);
            // Only this fixture's synthetic directory, after its worker is known to have ended.
            var full = Path.GetFullPath(root);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), full, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("a307-check-publication-", Path.GetFileName(full));
            Directory.Delete(full, true);
        }
    }

    [Fact]
    public async Task Progress_and_completed_result_keep_the_existing_contract()
    {
        await using var f = new Fixture();
        var reporter = await f.Check.Entered.Task.WaitAsync(Guard);
        Assert.Equal(CheckState.Running, f.Run.State);
        Assert.Null(f.Run.Report);
        Assert.Null(f.Run.Problem);
        Assert.Null(f.Run.Progress);
        Assert.False(f.Run.IsOver);
        var progress = new CheckProgress(.25, "One part inspected.");
        reporter.Report(progress);
        foreach (var fraction in new[] { double.NaN, double.NegativeInfinity, -.1, 1.1, double.PositiveInfinity })
        {
            reporter.Report(new(fraction, "Ignored."));
            Assert.Same(progress, f.Run.Progress);
        }
        reporter.Report(null!);
        Assert.Same(progress, f.Run.Progress);
        var unmeasured = new CheckProgress(null, "Cannot measure the remaining part.");
        reporter.Report(unmeasured);
        var findings = new List<CheckFinding>();
        f.Check.Answer.SetResult(new(true, findings, null!));
        await f.Run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Completed, f.Run.State);
        Assert.True(f.Run.IsOver);
        var result = Assert.IsType<CheckReport>(f.Run.Report);
        Assert.NotSame(findings, result.Findings);
        Assert.Empty(result.Findings);
        Assert.Equal("", result.Summary);
        Assert.Null(f.Run.Problem);
        Assert.True(f.Run.Clean(f.Project));
        Assert.Equal("clean", f.Run.Verdict(f.Project));
        findings.Add(new(new(Severity.Error, "Late mutation."), null, "Do not publish this."));
        reporter.Report(new(1, "Late progress."));
        Assert.Same(result, f.Run.Report);
        Assert.Empty(result.Findings);
        Assert.Same(unmeasured, f.Run.Progress);
    }

    [Theory]
    [InlineData("missing-list", "The check returned a report with no findings list; it's a fault in the check, not a result.")]
    [InlineData("missing-finding", "The check returned finding 1 of 1 with no problem in it; that's a fault in the check, not a result.")]
    [InlineData("throwing-list", "The check's findings couldn't be read: Controlled enumeration failure.. That's a fault in the check, not a problem in the data.")]
    [InlineData("missing-report", "The check returned nothing.")]
    public async Task Invalid_results_publish_failed_with_the_exact_diagnostic(string kind, string diagnostic)
    {
        await using var f = new Fixture();
        await f.Check.Entered.Task.WaitAsync(Guard);
        var report = kind switch
        {
            "missing-list" => new CheckReport(true, null!, "Ignored."),
            "missing-finding" => new CheckReport(true, new CheckFinding[] { null! }, "Ignored."),
            "throwing-list" => new CheckReport(true, new CallbackFindings(() => throw new InvalidOperationException("Controlled enumeration failure.")), "Ignored."),
            _ => null!,
        };
        f.Check.Answer.SetResult(report);
        await f.Run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Failed, f.Run.State);
        Assert.Null(f.Run.Report);
        Assert.Equal(diagnostic, f.Run.Problem);
        Assert.False(f.Run.Clean(f.Project));
        Assert.Equal("failed", f.Run.Verdict(f.Project));
    }

    [Fact]
    public async Task Worker_fault_retains_the_existing_failure_message()
    {
        await using var f = new Fixture();
        await f.Check.Entered.Task.WaitAsync(Guard);
        f.Check.Answer.SetException(new InvalidOperationException("Controlled failure."));
        await f.Run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Failed, f.Run.State);
        Assert.Null(f.Run.Report);
        Assert.Equal("The check failed: Controlled failure.. That's a fault in the check, not a problem in the data.", f.Run.Problem);
    }

    [Fact]
    public async Task Cancel_ends_logically_without_rewriting_late_worker_or_progress_results()
    {
        await using var f = new Fixture();
        var reporter = await f.Check.Entered.Task.WaitAsync(Guard);
        var progress = new CheckProgress(.5, "Half inspected.");
        reporter.Report(progress);
        Assert.True(f.Run.Cancel());
        await f.Run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Cancelled, f.Run.State);
        Assert.True(f.Run.IsOver);
        Assert.False(f.Run.Worker.IsCompleted);
        Assert.True(f.Run.Lingering);
        Assert.Null(f.Run.Report);
        Assert.Equal("Cancelled. Run it again for a result.", f.Run.Problem);
        Assert.False(f.Run.Cancel());
        reporter.Report(new(1, "Too late."));
        f.Check.Answer.SetResult(new(true, [], "Late answer."));
        await f.Run.Worker.WaitAsync(Guard);
        Assert.False(f.Run.Lingering);
        Assert.Equal(CheckState.Cancelled, f.Run.State);
        Assert.Null(f.Run.Report);
        Assert.Equal("Cancelled. Run it again for a result.", f.Run.Problem);
        Assert.Same(progress, f.Run.Progress);
    }

    [Fact]
    public async Task Progress_reported_reentrantly_while_copying_findings_is_preserved()
    {
        await using var f = new Fixture();
        var reporter = await f.Check.Entered.Task.WaitAsync(Guard);
        var progress = new CheckProgress(1, "Copying the finished findings.");
        // ToArray enumerates this public IReadOnlyList during Finish under the reentrant writer lock.
        var findings = new CallbackFindings(() => reporter.Report(progress));
        f.Check.Answer.SetResult(new(true, findings, "One finding."));
        await f.Run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Completed, f.Run.State);
        Assert.Same(progress, f.Run.Progress);
        Assert.Single(f.Run.Report!.Findings);
        Assert.Equal("Sample warning.", f.Run.Report.Findings[0].Problem.Message);
        Assert.Equal("warnings", f.Run.Verdict(f.Project));
    }

    [Fact]
    public async Task Status_readers_do_not_wait_for_findings_enumeration()
    {
        await using var f = new Fixture();
        var reporter = await f.Check.Entered.Task.WaitAsync(Guard);
        var copying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new CheckProgress(.75, "Copying findings.");
        Task<(CheckState State, CheckProgress? Progress, CheckReport? Report, string? Problem)>? reader = null;
        try
        {
            f.Check.Answer.SetResult(new(true, new CallbackFindings(() =>
            {
                reporter.Report(progress);
                copying.TrySetResult();
                release.Task.WaitAsync(Guard).GetAwaiter().GetResult();
            }), "One finding."));
            await copying.Task.WaitAsync(Guard);
            reader = Task.Run(() => (f.Run.State, f.Run.Progress, f.Run.Report, f.Run.Problem));
            var seen = await reader.WaitAsync(Guard);
            Assert.Equal(CheckState.Running, seen.State);
            Assert.Same(progress, seen.Progress);
            Assert.Null(seen.Report);
            Assert.Null(seen.Problem);
            Assert.False(f.Run.Ended.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            if (reader is not null) await reader.WaitAsync(Guard);
        }
        await f.Run.Ended.WaitAsync(Guard);
        Assert.Equal(CheckState.Completed, f.Run.State);
        Assert.NotNull(f.Run.Report);
    }

    private sealed class CallbackFindings(Action callback) : IReadOnlyList<CheckFinding>
    {
        public int Count => 1;
        public CheckFinding this[int index] => index == 0
            ? new(new(Severity.Warning, "Sample warning."), null, "Inspect the sample.")
            : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<CheckFinding> GetEnumerator()
        {
            callback();
            yield return this[0];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
