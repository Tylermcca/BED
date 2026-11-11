using System.Reflection;
using System.Text.Json;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

// INVENTED: deterministic interleavings of the existing two-stage capture and refresh contracts.
// Reflection only opens existing scheduling seams; no production source is changed.
public sealed class SpeedChangeAttackTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("speed-attack-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private string PathOf(string name = "items.json") => Path.Combine(Data, name);
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public SpeedChangeAttackTests()
    {
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
        File.WriteAllText(PathOf(), "{\"value\":\"one\"}");
        File.WriteAllText(Path.Combine(Schemas, "items.schema.json"), "{}");
    }
    public void Dispose() => Directory.Delete(root, true);
    private DataProject Project() => DataProject.Open(Data, Schemas);
    private static Func<T> Later<T>(string method, DataProject project) => (Func<T>)typeof(CheckSnapshot)
        .GetMethod(method, Hidden)!.Invoke(null, method == "CaptureLater" ? [project, null, null] : [project])!;
    private static CheckReport Clean() => new(true, [], "self-authored clean checker");
    private sealed class Probe(Func<CheckSnapshot, CancellationToken, Task<CheckReport>>? body = null, double seconds = 30, IReadOnlyList<string>? reads = null) : ICheck
    {
        public CheckDefinition Definition { get; } = new("attack", "Attack", "A self-authored scheduling probe.", TimeSpan.FromSeconds(seconds), Reads: reads);
        public Task<CheckReport> RunAsync(CheckSnapshot s, IProgress<CheckProgress> p, CancellationToken c) => body?.Invoke(s, c) ?? Task.FromResult(Clean());
    }
    private static async Task<CheckRun> Finished(CheckRun run) { await run.Ended.WaitAsync(TimeSpan.FromSeconds(10)); return run; }
    private static CheckRun Start(DataProject p, Probe? probe = null) => new Checks().Add(probe ?? new Probe()).Start("attack", p, out _)!;
    private static CheckRun Deferred(DataProject p, Probe probe, Func<CheckSnapshot> capture)
    {
        var run = (CheckRun)Activator.CreateInstance(typeof(CheckRun), Hidden, null,
            [probe.Definition, CheckSnapshot.SignatureOf(p), capture, null], null)!;
        typeof(CheckRun).GetMethod("Run", Hidden)!.Invoke(run, [probe]); return run;
    }
    private static void Expire(CheckRun run, bool stale) => typeof(CheckRun).GetField("lastAsked", Hidden)!.SetValue(run,
        // Two hours back, not 0: the clock counts from the machine's start, so on a machine started under an hour ago 0 isn't old enough.
        (System.Diagnostics.Stopwatch.GetTimestamp() - 2 * 3600 * System.Diagnostics.Stopwatch.Frequency, stale));
    private static void Pending(CheckRun run, Task<bool> task) => typeof(CheckRun).GetField("refreshing", Hidden)!.SetValue(run, task);
    private static Task<bool>? Pending(CheckRun run) => (Task<bool>?)typeof(CheckRun).GetField("refreshing", Hidden)!.GetValue(run);
    private static TaskCompletionSource<bool> Reply() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Edit(DataProject p, string value)
    {
        var f = p.Find("items.json")!; Assert.True(f.Open());
        Assert.True(f.Session!.Execute(new SetValue("/value", JsonSerializer.SerializeToElement(value))));
    }
    [Fact]
    public async Task An_older_refresh_must_not_replace_a_newer_exact_answer()
    {
        var p = Project(); var run = await Finished(Start(p));
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1)));
        var old = Reply(); Pending(run, old.Task); // a recheck started while the original saved file was current
        Edit(p, "two");
        Assert.True(run.IsStale(p, TimeSpan.Zero)); // newer exact answer
        old.SetResult(false); // older worker lands after the exact answer
        Assert.True(run.IsStale(p, TimeSpan.FromHours(1))); // must retain the newer answer: real regression stays failing
    }
    private sealed class PausingReads(string path) : IReadOnlyList<string>, IDisposable
    {
        private int armed;
        internal readonly ManualResetEventSlim Entered = new(), Release = new();
        internal void Arm() { Entered.Reset(); Release.Reset(); Interlocked.Exchange(ref armed, 1); }
        public int Count => 1;
        public string this[int index] => index == 0 ? path : throw new IndexOutOfRangeException();
        public IEnumerator<string> GetEnumerator()
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
            { Entered.Set(); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("A198 reads pause was not released."); }
            yield return path;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
    [Fact]
    public async Task An_actual_background_recheck_cannot_overwrite_a_newer_age_zero_answer()
    {
        var p = Project(); var outside = Path.Combine(root, "outside.txt"); File.WriteAllText(outside, "self-authored");
        using var reads = new PausingReads(outside); var run = await Finished(Start(p, new Probe(reads: reads)));
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); reads.Arm(); Expire(run, false);
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); var pending = Pending(run)!;
        Assert.True(reads.Entered.Wait(TimeSpan.FromSeconds(10))); // project signature already read by the actual worker
        try { Edit(p, "new draft"); Assert.True(run.IsStale(p, TimeSpan.Zero)); }
        finally { reads.Release.Set(); }
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(run.IsStale(p, TimeSpan.FromHours(1))); // fails: old false result wins
    }
    [Theory]
    [InlineData("add")] [InlineData("delete")]
    public async Task File_changes_during_a_real_recheck_are_seen_on_the_next_recheck(string operation)
    {
        var p = Project(); var outside = Path.Combine(root, "outside.txt"); File.WriteAllText(outside, "self-authored");
        using var reads = new PausingReads(outside); var run = await Finished(Start(p, new Probe(reads: reads)));
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); reads.Arm(); Expire(run, false);
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); var pending = Pending(run)!;
        Assert.True(reads.Entered.Wait(TimeSpan.FromSeconds(10)));
        try { if (operation == "add") File.WriteAllText(PathOf("extra.json"), "{}"); else File.Delete(PathOf()); }
        finally { reads.Release.Set(); }
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(10))); // disk change followed the worker's signature read
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); Assert.True(run.IsStale(p));
        Expire(run, false); Assert.False(run.IsStale(p, TimeSpan.FromHours(1)));
        await Pending(run)!.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(run.IsStale(p, TimeSpan.FromHours(1)));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recheck_fault_or_cancellation_is_stale(bool cancelled)
    {
        var p = Project(); var run = await Finished(Start(p));
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); var reply = Reply(); Pending(run, reply.Task);
        if (cancelled) reply.SetCanceled(); else reply.SetException(new IOException("self-authored recheck fault"));
        Assert.True(run.IsStale(p, TimeSpan.FromHours(1)));
    }
    [Fact]
    public async Task Actual_recheck_fault_from_a_removed_schema_folder_is_stale()
    {
        var p = Project(); var run = await Finished(Start(p)); Assert.False(run.IsStale(p, TimeSpan.FromHours(1)));
        Directory.Delete(Schemas, true); Expire(run, false);
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); var task = Pending(run)!;
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await task);
        Assert.True(run.IsStale(p, TimeSpan.FromHours(1)));
    }
    [Fact]
    public async Task Twenty_expiries_recheck_both_directions_without_piling_up_workers()
    {
        var p = Project(); var run = await Finished(Start(p)); var extra = PathOf("extra.json");
        Assert.False(run.IsStale(p, TimeSpan.FromHours(1))); bool prior = false;
        for (int i = 0; i < 20; ++i)
        {
            bool stale = i % 2 == 0; if (stale) File.WriteAllText(extra, "{}"); else File.Delete(extra);
            Expire(run, prior); Assert.Equal(prior, run.IsStale(p, TimeSpan.FromHours(1)));
            var task = Pending(run)!; Assert.NotNull(task);
            if (!task.IsCompleted) Assert.Same(task, Pending(run)); // one in-flight task, with no race against a landed answer
            await task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(stale, run.IsStale(p, TimeSpan.FromHours(1))); Assert.Null(Pending(run)); prior = stale;
        }
    }
    [Theory]
    [InlineData("edit")] [InlineData("save")] [InlineData("rename")] [InlineData("delete")] [InlineData("add")]
    public async Task Mutations_between_signature_pin_and_stamp_read_are_accounted_for(string operation)
    {
        var p = Project(); var before = CheckSnapshot.SignatureOf(p); var later = Later<string>("SignatureLater", p);
        if (operation == "edit") Edit(p, "draft");
        if (operation == "save") { Edit(p, "saved"); Assert.True(p.Find("items.json")!.Save()); }
        if (operation == "rename") File.Move(PathOf(), PathOf("moved.json"));
        if (operation == "delete") File.Delete(PathOf());
        if (operation == "add") File.WriteAllText(PathOf("extra.json"), "{}");
        var observed = later();
        if (operation == "edit") Assert.Equal(before, observed); // draft revision deliberately pinned before worker
        else Assert.NotEqual(before, observed);
        Assert.NotEqual(before, CheckSnapshot.SignatureOf(p));
        await Task.CompletedTask;
    }
    [Theory]
    [InlineData("edit")] [InlineData("save")] [InlineData("rename")] [InlineData("delete")]
    public async Task Pinned_dirty_document_survives_edits_save_rename_or_delete_before_capture(string operation)
    {
        var p = Project(); Edit(p, "pinned"); var capture = Later<CheckSnapshot>("CaptureLater", p);
        if (operation == "edit") Edit(p, "later");
        if (operation == "save") Assert.True(p.Find("items.json")!.Save());
        if (operation == "rename") File.Move(PathOf(), PathOf("moved.json"));
        if (operation == "delete") File.Delete(PathOf());
        var snapshot = capture(); Assert.Contains("pinned", snapshot.Text("data/items.json"));
        Assert.DoesNotContain("later", snapshot.Text("data/items.json"));
        Assert.NotEqual(snapshot.Signature, CheckSnapshot.SignatureOf(p));
        await Task.CompletedTask;
    }
    [Theory]
    [InlineData("change")] [InlineData("rename")] [InlineData("delete")] [InlineData("add")]
    public async Task Deferred_saved_inputs_are_marked_stale_when_the_disk_moves_after_start(string operation)
    {
        var p = Project(); var capture = Later<CheckSnapshot>("CaptureLater", p);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var run = Deferred(p, new Probe(), () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return capture(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            if (operation == "change") File.WriteAllText(PathOf(), "{\"value\":\"new value\"}");
            if (operation == "rename") File.Move(PathOf(), PathOf("moved.json"));
            if (operation == "delete") File.Delete(PathOf());
            if (operation == "add") File.WriteAllText(PathOf("extra.json"), "{}");
        }
        finally { release.Set(); }
        await Finished(run); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(run.IsStale(p)); Assert.False(run.Clean(p));
        if (operation == "change") Assert.Contains("new value", run.Snapshot.Text("data/items.json"));
        if (operation == "delete") Assert.DoesNotContain("data/items.json", run.Snapshot.Names);
    }
    [Fact]
    public async Task Actual_Start_must_be_stale_when_a_dirty_input_is_deleted_while_the_check_runs()
    {
        var p = Project(); Edit(p, "pinned");
        var entered = Reply(); var release = Reply();
        var run = Start(p, new Probe(async (s, token) =>
        {
            Assert.Contains("pinned", s.Text("data/items.json")); entered.SetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); return Clean();
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { File.Delete(PathOf()); }
        finally { release.SetResult(true); }
        await Finished(run); await run.Worker.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(PathOf())); Assert.Equal(CheckState.Completed, run.State);
        Assert.True(run.IsStale(p)); // source currently reports current/clean: real fault stays failing
        Assert.False(run.Clean(p));
    }
    [Fact]
    public async Task An_actual_Start_with_a_locked_input_fails_without_a_clean_report()
    {
        var p = Project(); CheckRun run;
        using (new FileStream(PathOf(), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) run = await Finished(Start(p));
        Assert.Equal(CheckState.Failed, run.State); Assert.Null(run.Report); Assert.Null(run.Captured);
        Assert.Contains("couldn't be read", run.Problem);
    }
    [Fact]
    public async Task Foreign_readers_share_one_capture_and_Captured_never_forces_it()
    {
        var p = Project(); var capture = Later<CheckSnapshot>("CaptureLater", p); int calls = 0;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var run = Deferred(p, new Probe(), () => { Interlocked.Increment(ref calls); entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return capture(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            var observed = await Task.Run(() => run.Captured).WaitAsync(TimeSpan.FromSeconds(2)); Assert.Null(observed);
            var readers = Enumerable.Range(0, 12).Select(_ => Task.Factory.StartNew(() => run.Snapshot, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            Assert.All(readers, t => Assert.False(t.IsCompleted)); release.Set();
            var snapshots = await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(snapshots, s => Assert.Same(run.Captured, s)); Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        await Finished(run);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timeout_or_cancel_ends_during_capture_and_late_work_cannot_publish(bool cancel)
    {
        var p = Project(); var capture = Later<CheckSnapshot>("CaptureLater", p); bool sawCancelled = false;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var probe = new Probe((_, token) => { sawCancelled = token.IsCancellationRequested; return Task.FromResult(Clean()); }, cancel ? 30 : .1);
        var run = Deferred(p, probe, () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return capture(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            if (cancel) Assert.True(run.Cancel()); await Finished(run);
            Assert.Equal(cancel ? CheckState.Cancelled : CheckState.TimedOut, run.State); Assert.True(run.Lingering);
            Assert.Null(run.Report); Assert.Null(run.Captured);
        }
        finally { release.Set(); }
        await run.Worker.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(sawCancelled);
        Assert.Equal(cancel ? CheckState.Cancelled : CheckState.TimedOut, run.State); Assert.Null(run.Report); Assert.False(run.Lingering);
    }
}
