using System.Text.Json;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>Piece J, from A060's design: registered checks on a frozen snapshot, off the frame, with progress, cancel and
/// a time limit; a result is clean only when complete, without errors or warnings, and still current.</summary>
public sealed class CheckTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("checks-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Map => Path.Combine(root, "reference_map.json");

    public CheckTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        File.WriteAllText(Path.Combine(root, "schemas", "wares.schema.json"), "{}");
        File.WriteAllText(Path.Combine(root, "schemas", "jobs.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore"},{"id":"salt"},{"id":"salt"}]}""");
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"ware":"ore"},{"ware":"silk"},{"ware":"salt"}]}""");
        File.WriteAllText(Map, """
        {"game":"T","labels":[],"references":[{"file":"jobs.json","at":"/jobs/*/ware","target":{"file":"wares.json","list":"/wares","key":"id"},"evidence":"test"}]}
        """);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DataProject Project() => DataProject.Open(Data, Path.Combine(root, "schemas"));

    private sealed class Fake(string id, Func<CheckSnapshot, IProgress<CheckProgress>, CancellationToken, Task<CheckReport>> run, double seconds = 30) : ICheck
    {
        public CheckDefinition Definition { get; } = new(id, "Fake " + id, "A test check.", TimeSpan.FromSeconds(seconds));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel) => run(snapshot, progress, cancel);
    }

    private static Task<CheckReport> Clean(CheckSnapshot s, IProgress<CheckProgress> p, CancellationToken c) => Task.FromResult(new CheckReport(true, [], "ok"));

    private static async Task<CheckRun> Finished(CheckRun? run)
    {
        Assert.NotNull(run);
        await run!.Ended.WaitAsync(TimeSpan.FromSeconds(30));
        return run;
    }

    [Fact]
    public void Registration_is_all_or_nothing_and_says_what_is_wrong()
    {
        var checks = new Checks().Add(new Fake("a", Clean));
        Assert.Contains("already a check with the id 'a'", Assert.Throws<ArgumentException>(() => checks.Add(new Fake("b", Clean), new Fake("a", Clean))).Message);
        Assert.Equal(["a"], checks.Registered.Select(d => d.Id));   // b wasn't added either
        Assert.Throws<ArgumentException>(() => checks.Add(new Fake(" ", Clean)));
        Assert.Throws<ArgumentException>(() => checks.Add(new Fake("z", Clean, seconds: 0)));
        Assert.Null(checks.Start("nothing", Project(), out var why));
        Assert.Contains("no check called 'nothing'", why);
    }

    [Fact]
    public async Task A_check_sees_unsaved_edits_and_leaves_the_project_untouched()
    {
        var p = Project();
        var jobs = p.Find("jobs.json")!;
        Assert.True(jobs.Open());
        Assert.True(jobs.Session!.Execute(new SetValue("/jobs/1/ware", JsonSerializer.SerializeToElement("ore"))));
        var disk = File.ReadAllBytes(jobs.Path);
        var revision = jobs.Session.Revision;
        string? seen = null;
        var run = await Finished(new Checks().Add(new Fake("look", (s, _, _) => { seen = s.Text("data/jobs.json"); return Task.FromResult(new CheckReport(true, [], "")); }))
            .Start("look", p, out _));
        Assert.Contains("\"ore\"", seen!.Split("silk")[0]);
        Assert.DoesNotContain("silk", seen);
        Assert.Equal(disk, File.ReadAllBytes(jobs.Path));
        Assert.Equal(revision, jobs.Session.Revision);
        Assert.True(jobs.Session.Dirty);
        Assert.Contains("schemas/wares.schema.json", run.Snapshot.Names);
        Assert.Equal(64, run.Snapshot.InputHash.Length);
    }

    [Fact]
    public async Task The_link_check_finds_what_names_nothing_and_what_names_two_with_places()
    {
        var p = Project();
        var run = await Finished(new Checks().Add(new LinkCheck()).Start("links.all", p, out _, ReferenceMap.Read(File.ReadAllText(Map))));
        Assert.True(run.State == CheckState.Completed, run.Problem);
        Assert.Equal("errors", run.Verdict(p));
        var missing = Assert.Single(run.Report!.Findings, f => f.Problem.Message.Contains("names nothing"));
        Assert.Equal(new CheckPlace("jobs.json", "/jobs/1", "/jobs/1/ware"), missing.Place);
        Assert.Contains("spelling", missing.Next);
        Assert.Single(run.Report.Findings, f => f.Problem.Message.Contains("two or more") && f.Place!.At == "/jobs/2/ware");
        Assert.False(run.Clean(p));
    }

    [Fact]
    public async Task Fixed_data_checks_clean_and_any_change_after_makes_the_result_stale()
    {
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore"},{"id":"salt"},{"id":"silk"}]}""");
        var p = Project();
        var checks = new Checks().Add(new LinkCheck());
        var run = await Finished(checks.Start("links.all", p, out _, ReferenceMap.Read(File.ReadAllText(Map))));
        Assert.True(run.State == CheckState.Completed, run.Problem);
        Assert.Equal("clean", run.Verdict(p));
        Assert.True(run.Clean(p));
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/id", JsonSerializer.SerializeToElement("iron"))));
        Assert.True(run.IsStale(p));
        Assert.Equal("clean (stale)", run.Verdict(p));
        Assert.False(run.Clean(p));   // a stale result never certifies
        Assert.True(wares.Session.Undo());
        Assert.False(run.IsStale(p));   // back to exactly the checked file
        File.WriteAllText(Path.Combine(Data, "extra.json"), "{}");
        p.Refresh();
        Assert.True(run.IsStale(p));   // a file added
    }

    [Fact]
    public async Task Without_a_map_the_link_check_says_there_is_nothing_to_check()
    {
        var run = await Finished(new Checks().Add(new LinkCheck()).Start("links.all", Project(), out _));
        Assert.Contains("no link map", run.Report!.Summary);
    }

    [Fact]
    public async Task A_check_that_throws_has_failed_and_one_that_could_not_finish_is_incomplete_never_clean()
    {
        var p = Project();
        var checks = new Checks().Add(new Fake("boom", (_, _, _) => throw new InvalidOperationException("parser broke")),
            new Fake("partial", (_, _, _) => Task.FromResult(new CheckReport(false, [], "looked at half"))));
        var boom = await Finished(checks.Start("boom", p, out _));
        Assert.Equal(CheckState.Failed, boom.State);
        Assert.Contains("parser broke", boom.Problem);
        Assert.Contains("not a problem in the data", boom.Problem);
        var partial = await Finished(checks.Start("partial", p, out _));
        Assert.Equal("incomplete", partial.Verdict(p));
        Assert.False(partial.Clean(p));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Notes_stay_visible_and_only_a_complete_current_result_is_clean(bool complete)
    {
        var p=Project();
        var run=await Finished(new Checks().Add(new Fake("note",(_,_,_)=>Task.FromResult(new CheckReport(complete,
            [new CheckFinding(new Issue(Severity.Info,"A useful note."),null,"Read this note.")],"Checked.")))).Start("note",p,out _));
        Assert.Equal(complete?"clean":"incomplete",run.Verdict(p));Assert.Equal(complete,run.Clean(p));
        var note=Assert.Single(run.Report!.Findings);Assert.Equal(Severity.Info,note.Problem.Severity);Assert.Equal("A useful note.",note.Problem.Message);
        if(complete)
        {
            File.AppendAllText(Path.Combine(Data,"wares.json")," ");
            Assert.Equal("clean (stale)",run.Verdict(p));Assert.False(run.Clean(p));
        }
    }

    [Fact]
    public async Task Warnings_alone_are_warnings_not_clean()
    {
        var p = Project();
        var run = await Finished(new Checks().Add(new Fake("warn", (_, _, _) => Task.FromResult(new CheckReport(true,
            [new CheckFinding(Issue.Warning("odd but allowed"), new CheckPlace("wares.json"), "Look at it.")], "")))).Start("warn", p, out _));
        Assert.Equal("warnings", run.Verdict(p));
        Assert.False(run.Clean(p));
    }

    [Fact]
    public async Task Cancel_stops_a_run_once_and_a_late_answer_is_ignored()
    {
        var p = Project();
        var release = new TaskCompletionSource();
        var reported = new TaskCompletionSource();
        var checks = new Checks().Add(new Fake("slow", async (_, progress, cancel) =>
        {
            progress.Report(new CheckProgress(0.5, "half"));
            progress.Report(new CheckProgress(7, "nonsense"));   // ignored: not a fraction
            reported.SetResult();
            await release.Task;   // ignores its token, as a badly written check might
            return new CheckReport(true, [], "late");
        }));
        var run = checks.Start("slow", p, out _)!;
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(30));   // not a fixed wait: a busy machine may start the worker late
        Assert.Equal(0.5, run.Progress?.Fraction);
        Assert.Null(checks.Start("slow", p, out var busy));
        Assert.Contains("already running", busy);
        Assert.True(run.Cancel());
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(CheckState.Cancelled, run.State);
        release.SetResult();
        await Task.Delay(100);
        Assert.Equal(CheckState.Cancelled, run.State);   // the late answer changed nothing
        Assert.Null(run.Report);
        Assert.False(run.Cancel());   // already over
    }

    [Fact]
    public async Task A_check_past_its_time_limit_has_timed_out_even_if_it_ignores_its_token()
    {
        var p = Project();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<CheckReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        var run = new Checks().Add(new Fake("stuck", (_, _, cancel) =>
        {
            token = cancel;
            entered.SetResult();
            return never.Task;   // deliberately ignores cancellation until released below
        }, seconds: 1)).Start("stuck", p, out _)!;
        try
        {
            // Ended can complete before a busy worker captures its inputs. Its verdict is then
            // correctly stale; wait for entry so this test concerns a running, token-ignoring check.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Finished(run);
            Assert.Equal(CheckState.TimedOut, run.State);
            Assert.Contains("limit", run.Problem);
            Assert.True(token.IsCancellationRequested);
            Assert.False(never.Task.IsCompleted);
            Assert.True(run.Lingering);
            never.SetResult(new CheckReport(true, [], "too late"));
            await run.Worker.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(CheckState.TimedOut, run.State);
            Assert.Equal("timed out", run.Verdict(p));
        }
        finally
        {
            never.TrySetResult(new CheckReport(true, [], "cleanup"));
            await run.Worker.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task A_stopped_check_that_keeps_working_lingers_and_cannot_pile_up_without_bound()
    {
        var p = Project();
        var release = new TaskCompletionSource();
        var checks = new Checks().Add(new Fake("stuck", async (_, _, _) => { await release.Task; return new CheckReport(true, [], "too late"); }, seconds: 0.2));

        var first = await Finished(checks.Start("stuck", p, out _));
        Assert.Equal(CheckState.TimedOut, first.State);
        Assert.True(first.Lingering);                          // ended, but its code is still going
        Assert.Equal(1, checks.LingeringRuns("stuck"));        // counted at once, not only at the next start (found by A185)

        var second = await Finished(checks.Start("stuck", p, out var why));   // one retry straight away is fine
        Assert.True(second.Lingering, why);
        Assert.Null(checks.Start("stuck", p, out why));        // a third would make three runaway workers
        Assert.Contains("still working", why);
        Assert.Equal(Checks.MaxLingering, checks.LingeringRuns("stuck"));

        release.SetResult();
        await Task.WhenAll(first.Worker, second.Worker).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(first.Lingering);
        Assert.Equal(CheckState.TimedOut, first.State);         // what it said late changes nothing
        Assert.Equal(0, checks.LingeringRuns("stuck"));
        var restarted = checks.Start("stuck", p, out why);
        Assert.NotNull(restarted);                             // and it can run again
        // The last assertion starts a real capture worker too; finish it before deleting its inputs.
        await restarted.Worker.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_fault_after_a_run_has_ended_is_observed_and_changes_nothing()
    {
        var p = Project();
        var release = new TaskCompletionSource();
        var run = await Finished(new Checks().Add(new Fake("late", async (_, _, _) => { await release.Task; throw new InvalidOperationException("late fault"); }, seconds: 0.2)).Start("late", p, out _));
        Assert.Equal(CheckState.TimedOut, run.State);

        var unobserved = false;
        void Saw(object? _, UnobservedTaskExceptionEventArgs e) { if (e.Exception.GetBaseException().Message == "late fault") unobserved = true; }
        TaskScheduler.UnobservedTaskException += Saw;
        try
        {
            release.SetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => run.Worker.WaitAsync(TimeSpan.FromSeconds(30)));
            await Task.Delay(100);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(unobserved);
            Assert.Equal(CheckState.TimedOut, run.State);
            Assert.False(run.Lingering);
        }
        finally { TaskScheduler.UnobservedTaskException -= Saw; }
    }

    [Fact]
    public async Task A_panels_freshness_is_reworked_off_the_frame_and_lands_while_the_exact_answer_stays_exact()
    {
        // A184 rank 5: the 500 ms cache's expiry cost 23-53 ms frames; now the stamps are read on a worker.
        var p = Project();
        var run = await Finished(new Checks().Add(new Fake("look", Clean)).Start("look", p, out _));
        var age = TimeSpan.FromMilliseconds(40);
        Assert.False(run.IsStale(p, age));                                   // the first answer is exact
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore"}]}""");
        File.SetLastWriteTimeUtc(Path.Combine(Data, "wares.json"), DateTime.UtcNow.AddSeconds(5));
        Assert.True(run.IsStale(p));                                         // exact: at once
        Assert.False(run.IsStale(p, age));                                   // the panel's answer is still the cached one
        await Task.Delay(60);
        Assert.False(run.IsStale(p, age));                                   // past its age: the last answer stands, a re-check starts
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!run.IsStale(p, age) && DateTime.UtcNow < until) await Task.Delay(10);
        Assert.True(run.IsStale(p, age));                                    // and the fresh answer lands
    }

    [Fact]
    public async Task An_exact_answer_is_never_replaced_by_an_older_recheck_landing_after_it()
    {
        // A198: a recheck that started before an exact answer, and landed after it, said the changed project was current again.
        var p = Project();
        var run = await Finished(new Checks().Add(new Fake("look", Clean)).Start("look", p, out _));
        var age = TimeSpan.FromSeconds(30);
        Assert.False(run.IsStale(p, TimeSpan.FromMilliseconds(1)));
        await Task.Delay(20);
        Assert.False(run.IsStale(p, TimeSpan.FromMilliseconds(1)));          // past its age: a recheck starts on a worker...
        await Task.Delay(300);                                               // ...and finishes, unread: "current"
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore"}]}""");
        File.SetLastWriteTimeUtc(Path.Combine(Data, "wares.json"), DateTime.UtcNow.AddSeconds(5));
        Assert.True(run.IsStale(p, TimeSpan.Zero));                          // exact, and newer
        Assert.True(run.IsStale(p, age));                                    // the older recheck doesn't replace it
    }

    [Fact]
    public async Task A_file_with_unsaved_edits_deleted_on_disk_makes_the_result_stale()
    {
        // A198: a draft's signature was its revision alone, so its file vanishing under it went unseen.
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"id":"ore"}]}""");
        var p = Project();
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/id", JsonSerializer.SerializeToElement("iron"))));
        var run = await Finished(new Checks().Add(new Fake("look", Clean)).Start("look", p, out _));
        Assert.False(run.IsStale(p));
        File.Delete(Path.Combine(Data, "wares.json"));
        Assert.True(run.IsStale(p));
    }

    [Fact]
    public async Task A_check_sees_the_project_as_it_was_when_started_even_though_it_captures_on_its_worker()
    {
        // A184 rank 4: the slow part of capture moved off the editor's thread; the moment is still the Start.
        var p = Project();
        var file = p.Find("wares.json")!;
        Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new SetValue("/wares/0/id", JsonSerializer.SerializeToElement("tin"))));
        var gate = new TaskCompletionSource();
        string? seen = null;
        var run = new Checks().Add(new Fake("look", async (s, _, _) => { await gate.Task; seen = s.Text("data/wares.json"); return new CheckReport(true, [], ""); })).Start("look", p, out _)!;
        Assert.True(file.Session.Execute(new SetValue("/wares/0/id", JsonSerializer.SerializeToElement("lead"))));   // after Start
        gate.SetResult();
        await Finished(run);
        Assert.Contains("tin", seen);
        Assert.DoesNotContain("lead", seen);
        Assert.True(run.IsStale(p));                                                           // and it says the project has moved on
    }

    [Fact]
    public async Task Files_that_cant_be_read_for_a_check_say_so_and_never_crash_a_reader()
    {
        var p = Project();
        var path = Path.Combine(Data, "jobs.json");
        Checks checks = new Checks().Add(new Fake("look", Clean));
        CheckRun run;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))   // another program holds it
            run = await Finished(checks.Start("look", p, out _));
        Assert.Equal(CheckState.Failed, run.State);
        Assert.Contains("couldn't be read", run.Problem);
        Assert.Null(run.Captured);                                                             // never throws
    }
}
