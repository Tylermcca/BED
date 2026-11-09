using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Brutal.Authoring.Data;

// Piece J (A038), from A060's accepted design (docs/plan/check_actions.md): a registered check runs on a frozen snapshot
// of the project, off the editor's frame, with progress and cancel, and its result goes stale the moment an input changes.
// MIXED: Unreal's Data Validation (registered validators, a report per asset), Unity's Progress API, and the Language
// Server Protocol's versioned diagnostics (a result belongs to the version it was computed on); INVENTED: the snapshot
// receipt and "a stale result never certifies".

/// <summary>What a check is: a stable dotted id, a name people see, what it checks, and how long it may run.</summary>
/// <param name="Id">Unique dotted id, such as <c>links.all</c>.</param>
/// <param name="Label">What the Checks panel and the palette show.</param>
/// <param name="Description">What it checks and what it doesn't.</param>
/// <param name="Timeout">How long a run may take before it's stopped as timed out.</param>
/// <param name="OptionsSchema">A JSON Schema for the choices a run can take, or null for a check that takes none. They
/// are data a check reads, never commands or paths it obeys.</param>
/// <param name="Reads">Files or folders outside the project that the check also reads (a game's own data, say). A run's
/// result goes stale when any of them changes, as it does when the project does (a check that read a game's own data
/// would otherwise leave an old result looking current after that data changed).</param>
public sealed record CheckDefinition(string Id, string Label, string Description, TimeSpan Timeout, string? OptionsSchema = null, IReadOnlyList<string>? Reads = null);

/// <summary>Where a finding is: a file, the item it belongs to, and the exact place, as far as each is known.</summary>
/// <param name="File">The data file's name.</param>
/// <param name="Item">The owning item's pointer, or null for a file-wide finding.</param>
/// <param name="At">The exact place, or null when only the item or file is known.</param>
public sealed record CheckPlace(string File, string? Item = null, string? At = null);

/// <summary>One thing a check found: the problem (with its severity), where, and what to do next.</summary>
/// <param name="Problem">The issue to report, including its severity and message.</param>
/// <param name="Place">The known file, item or field, or null when no location is known.</param>
/// <param name="Next">A plain next action for the person reading the finding.</param>
public sealed record CheckFinding(Issue Problem, CheckPlace? Place, string Next);

/// <summary>What a check returns. <paramref name="Complete"/> is false when it couldn't look at everything; then no
/// result is "clean", whatever it found.</summary>
/// <param name="Complete">Whether the check inspected everything it intended to inspect. True does not by itself mean the result is clean or current.</param>
/// <param name="Findings">The findings, in report order. Use an empty list when none were found; an incomplete report is still not clean.</param>
/// <param name="Summary">A short human-readable account of the result.</param>
public sealed record CheckReport(bool Complete, IReadOnlyList<CheckFinding> Findings, string Summary);

/// <summary>How far a run has got: a fraction from 0 to 1, or null when it can't say, and a short message.</summary>
/// <param name="Fraction">Progress from zero to one, or null when it cannot be measured.</param>
/// <param name="Message">A short description of the work happening now.</param>
public sealed record CheckProgress(double? Fraction, string Message);

/// <summary>A check a game or plug-in registers. It reads only its snapshot, honours cancellation, and returns every
/// problem it finds; a problem found is a finding, never an exception. It never edits the project.</summary>
public interface ICheck
{
    /// <summary>What it is.</summary>
    CheckDefinition Definition { get; }

    /// <summary>Checks <paramref name="snapshot"/>. Runs on a worker, never the editor's frame.</summary>
    Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel);
}

/// <summary>
/// The project's inputs, frozen when a check starts: every data file (an open file's current draft, unsaved edits and
/// repair drafts included; others as on disk, including files added to the folder since the editor listed it), every
/// schema, and the link map if there is one. Its hash names exactly
/// what was checked; its <see cref="Signature"/> says when the project has moved on: any edit to an open draft (its
/// revision), or any change on disk. An edit undone back to the saved file is the saved file again, so a result on that
/// file is current again (identical bytes can't make a result wrong). Nothing in it touches
/// the live project.
/// </summary>
public sealed class CheckSnapshot
{
    private readonly Dictionary<string, byte[]> inputs;
    private readonly string options;
    private Func<string, string?>? schemaFor;

    private CheckSnapshot(Dictionary<string, byte[]> inputs, string signature, string options, string meaning)
    {
        this.inputs = inputs;
        this.options = options;
        Signature = signature;
        using var sha = SHA256.Create();
        foreach (var (name, bytes) in inputs.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            var head = Encoding.UTF8.GetBytes($"{name}\n{bytes.Length}\n");
            sha.TransformBlock(head, 0, head.Length, null, 0);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        if (meaning != NoOptions)   // the same project under different choices is a different check; none hashes as before
        {
            var chosen = Encoding.UTF8.GetBytes($"\0options\n{meaning}");
            sha.TransformBlock(chosen, 0, chosen.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        InputHash = Convert.ToHexStringLower(sha.Hash!);
    }

    /// <summary>The inputs' names: <c>data/wares.json</c>, <c>schemas/wares.schema.json</c>, and <c>map</c>.</summary>
    public IReadOnlyList<string> Names => inputs.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>The empty options object: what a run has when nothing was chosen.</summary>
    public const string NoOptions = "{}";

    /// <summary>The choices this run was started with, as an object, checked against the check's schema; empty when none.</summary>
    public JsonElement Options
    {
        get { using var document = JsonDocument.Parse(options); return document.RootElement.Clone(); }
    }

    /// <summary>A SHA-256 over every input's name and bytes, and any options: what this snapshot is.</summary>
    public string InputHash { get; }

    /// <summary>The project's state when captured (each file's edit revision or disk stamp): a different one means the
    /// result is stale.</summary>
    public string Signature { get; }

    /// <summary>An input's bytes, or null when there's no such input.</summary>
    public byte[]? Bytes(string name) => inputs.TryGetValue(name, out var b) ? b.ToArray() : null;

    /// <summary>An input as text (UTF-8), or null.</summary>
    public string? Text(string name) => inputs.TryGetValue(name, out var b) ? Encoding.UTF8.GetString(b) : null;

    /// <summary>Captures <paramref name="project"/> now, with its link map if it has one and any options, which are
    /// JSON (key order, spacing and how a number is spelled don't change the hash).</summary>
    public static CheckSnapshot Capture(DataProject project, ReferenceMap? map = null, string? options = null) => CaptureLater(project, map, options)();

    /// <summary>A frozen candidate copy with only one existing data document replaced in memory.
    /// Signature still identifies the captured base project; InputHash identifies the candidate bytes.</summary>
    public CheckSnapshot WithDocument(string file, DataDocument candidate)
    {
        var key = "data/" + file;
        if (!inputs.ContainsKey(key)) throw new ArgumentException("The target is not in the captured project.", nameof(file));
        var copy = inputs.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        copy[key] = Encoding.UTF8.GetBytes(new DataFormat().Write(candidate));
        return new CheckSnapshot(copy, Signature, options, Canonical(options, sameNumbers: true)) { schemaFor = schemaFor };
    }

    /// <summary>The same capture in two steps (A184 rank 4): the moment is pinned now, cheaply, on the caller's thread: each draft's
    /// immutable document, each saved file's path, the signature and the options. The returned function does the slow part, on
    /// any thread: it writes the drafts out and reads the saved files and schemas. A saved file changed in between makes the result
    /// stale against the pinned signature, never wrongly current.</summary>
    internal static Func<CheckSnapshot> CaptureLater(DataProject project, ReferenceMap? map = null, string? options = null)
    {
        var files = project.Files.Select(f => (f.Name, f.Path, Draft: f.Session is { Dirty: true } s ? s.Current : null)).ToList();
        var (dataFolder, schemaFolder, schemaFor, mapText) = (project.DataFolder, project.SchemaFolder, project.SchemaFor, map?.Text);
        var signature = SignatureOf(project);
        var (plain, same) = (Canonical(options, sameNumbers: false), Canonical(options, sameNumbers: true));
        return () =>
        {
            var inputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (name, path, draft) in files)
            {
                if (draft is not null) inputs["data/" + name] = Encoding.UTF8.GetBytes(new DataFormat().Write(draft));
                else if (File.Exists(path)) inputs["data/" + name] = File.ReadAllBytes(path);
            }
            // A file put in the folder by another program is checked too, without waiting for the editor to list it (A066).
            var onDisk = Directory.Exists(dataFolder) ? Directory.EnumerateFiles(dataFolder, "*.json").Order(StringComparer.Ordinal) : Enumerable.Empty<string>();
            foreach (var path in onDisk)
                inputs.TryAdd("data/" + Path.GetFileName(path), File.ReadAllBytes(path));
            foreach (var schema in Directory.EnumerateFiles(schemaFolder, "*.json"))
                inputs["schemas/" + Path.GetFileName(schema)] = File.ReadAllBytes(schema);
            if (mapText is not null) inputs["map"] = Encoding.UTF8.GetBytes(mapText);
            return new CheckSnapshot(inputs, signature, plain, same) { schemaFor = schemaFor };
        };
    }

    // One text for one meaning: object keys in order, no spacing. With sameNumbers, equal numbers are spelled the same
    // too (found by A077: 1, 1.0 and 1e0 hashed apart): for hashing only, since a check asking for a whole number must
    // still be able to read the one it was given.
    private static string Canonical(string? options, bool sameNumbers)
    {
        if (string.IsNullOrWhiteSpace(options)) return NoOptions;
        using var document = JsonDocument.Parse(options);
        using var text = new MemoryStream();
        using (var writer = new Utf8JsonWriter(text)) Write(document.RootElement, writer, sameNumbers);
        return Encoding.UTF8.GetString(text.ToArray());

        static void Write(JsonElement value, Utf8JsonWriter writer, bool sameNumbers)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        Write(property.Value, writer, sameNumbers);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in value.EnumerateArray()) Write(item, writer, sameNumbers);
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.Number when sameNumbers:
                    writer.WriteRawValue(ExactNumber(value.GetRawText()));
                    break;
                default:
                    value.WriteTo(writer);
                    break;
            }
        }
    }

    // A JSON number as digits and a power of ten, with no zeros at either end, so equal values read the same and different
    // ones never do, however long: done on the text, since a double would make different large numbers equal.
    internal static string ExactNumber(string number)
    {
        var negative = number.StartsWith('-');
        var body = negative ? number[1..] : number;
        var e = body.IndexOfAny(['e', 'E']);
        var exponent = e < 0 ? System.Numerics.BigInteger.Zero : System.Numerics.BigInteger.Parse(body[(e + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? body : body[..e];
        var dot = mantissa.IndexOf('.');
        var fraction = dot < 0 ? "" : mantissa[(dot + 1)..];
        var digits = (dot < 0 ? mantissa : mantissa[..dot]) + fraction;
        exponent -= fraction.Length;
        var kept = digits.TrimEnd('0');
        exponent += digits.Length - kept.Length;
        kept = kept.TrimStart('0');
        if (kept.Length == 0) return "0";   // zero has one meaning, signed or not
        return $"{(negative ? "-" : "")}{kept}e{exponent}";
    }

    /// <summary>The project's state now, to compare with a snapshot's <see cref="Signature"/>: each file's disk size and time, with
    /// its session revision when it has unsaved edits, and the schemas' disk stamps. Cheap enough to ask every frame. (The map is a
    /// loaded object, fixed for the session.)</summary>
    public static string SignatureOf(DataProject project) => SignatureLater(project)();

    /// <summary>The same signature in two steps: what's needed from the project is taken now, cheaply, on the caller's thread (each
    /// file's name, and a draft's revision or a saved file's path); the returned function reads the stamps, on any thread, and gives
    /// exactly what <see cref="SignatureOf"/> would have given at this moment, if nothing on disk has moved since.</summary>
    internal static Func<string> SignatureLater(DataProject project)
    {
        var listed = project.Files.Select(f => (f.Name, Draft: f.Session is { Dirty: true } s ? $":r{s.Revision}" : null, f.Path)).ToList();
        var names = listed.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var (dataFolder, schemaFolder) = (project.DataFolder, project.SchemaFolder);
        return () =>
        {
            var parts = new StringBuilder();
            foreach (var (name, draft, path) in listed)
                parts.Append(name).Append(draft).Append(Stamp(path)).Append('|');   // a draft's file stamped too: one deleted or changed under it shows (A198)
            // Files on disk the editor hasn't listed yet count too, so one added outside makes a result stale at once (A066).
            var onDisk = Directory.Exists(dataFolder) ? Directory.EnumerateFiles(dataFolder, "*.json").Order(StringComparer.Ordinal) : Enumerable.Empty<string>();
            foreach (var path in onDisk.Where(p => !names.Contains(Path.GetFileName(p))))
                parts.Append(Path.GetFileName(path)).Append(Stamp(path)).Append('|');
            foreach (var schema in Directory.EnumerateFiles(schemaFolder, "*.json").Order(StringComparer.Ordinal))
                parts.Append(Path.GetFileName(schema)).Append(Stamp(schema)).Append('|');
            return parts.ToString();
        };

        static string Stamp(string path) => File.Exists(path) ? $":{new FileInfo(path).Length}@{File.GetLastWriteTimeUtc(path).Ticks}" : ":absent";
    }

    // The data folder's JSON files as they are on disk now, in name order.
    private static IEnumerable<string> DataFiles(DataProject project) =>
        Directory.Exists(project.DataFolder) ? Directory.EnumerateFiles(project.DataFolder, "*.json").Order(StringComparer.Ordinal) : [];

    /// <summary>Writes the snapshot into a fresh folder (<c>data/</c>, <c>schemas/</c>, <c>reference_map.json</c>) and
    /// opens it read-only, so a check can use the project's own tools on exactly what was captured. The caller deletes
    /// <paramref name="folder"/> when done.</summary>
    public DataProject OpenCopy(string folder, out ReferenceMap? map)
    {
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        Directory.CreateDirectory(Path.Combine(folder, "schemas"));
        foreach (var (name, bytes) in inputs)
            File.WriteAllBytes(name == "map" ? Path.Combine(folder, "reference_map.json") : Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)), bytes);
        map = Text("map") is { } m ? ReferenceMap.Read(m) : null;
        var copy = DataProject.Open(Path.Combine(folder, "data"), Path.Combine(folder, "schemas"), readOnly: true);
        copy.SchemaFor = schemaFor;   // or a check's copy would refuse the files the project opens
        return copy;
    }
}

/// <summary>Where a run stands. Only <see cref="Running"/> and <see cref="Cancelling"/> can change.</summary>
public enum CheckState
{
    /// <summary>Working.</summary>
    Running,

    /// <summary>Asked to stop; waiting for the check to notice.</summary>
    Cancelling,

    /// <summary>Finished and returned a report (which may hold problems).</summary>
    Completed,

    /// <summary>The check crashed or returned nothing: not a finding, and never clean.</summary>
    Failed,

    /// <summary>Stopped on request.</summary>
    Cancelled,

    /// <summary>Ran past its time limit.</summary>
    TimedOut,
}

/// <summary>One run of a check, on one snapshot. Its state, progress and report change only until it ends; anything the
/// check reports after that is ignored.</summary>
public sealed class CheckRun
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource cancel = new();
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // One immutable publication for cross-thread readers; the existing gate still serializes all writers.
    // Separate public getter calls can see different running publications; terminal publications never change.
    private sealed record RunStatus(CheckState State, CheckProgress? Progress = null, CheckReport? Report = null, string? Problem = null, DateTimeOffset? FinishedAt = null);
    private RunStatus status = new(CheckState.Running);
    internal CheckRun(CheckDefinition check, CheckSnapshot snapshot) : this(check, snapshot.Signature, () => snapshot) { }

    // Started: the moment is pinned now (the project's signature), and the inputs are captured on the worker, inside the check's
    // own time limit, so starting never stops the editor (A184 rank 4).
    internal CheckRun(CheckDefinition check, string signature, Func<CheckSnapshot> capture, Action<CheckRun>? completed = null)
    {
        Check = check;
        onCompleted = completed;
        runId = completed is null ? null : Guid.NewGuid().ToString("N");
        startSignature = signature;
        snapshot = new Lazy<CheckSnapshot>(capture, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private readonly Action<CheckRun>? onCompleted;
    private readonly string? runId;

    /// <summary>Opaque instance-local id for a terminal ordinary run. Null while running or for isolated proposal checks.
    /// An id does not certify freshness and may have been evicted from the bounded paging history.</summary>
    public string? RunId => IsOver ? runId : null;

    private readonly Lazy<CheckSnapshot> snapshot;
    private readonly string startSignature;
    // The outside inputs' stamps, taken on the worker just before the check reads them, so they match what it read.
    private string? reads;

    // What the check's outside inputs were when it started: each file's path, size and last write, folders' files included.
    // Cheap enough to ask every frame; any write changes it.
    private static string ReadsSignature(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0) return "";
        var text = new System.Text.StringBuilder();
        foreach (var path in paths)
        {
            var files = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal) : File.Exists(path) ? [path] : Enumerable.Empty<string>();
            text.Append(path).Append('|');
            foreach (var f in files)
            {
                var info = new FileInfo(f);
                text.Append(f).Append(':').Append(info.Exists ? info.Length : -1).Append(':').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append(';');
            }
        }
        return text.ToString();
    }

    /// <summary>Which check.</summary>
    public CheckDefinition Check { get; }

    /// <summary>What it checked.</summary>
    public CheckSnapshot Snapshot => snapshot.Value;

    /// <summary>What it checked, once captured; null before, or if the files couldn't be read. Never waits.</summary>
    public CheckSnapshot? Captured => snapshot.IsValueCreated ? snapshot.Value : null;

    /// <summary>Where it stands.</summary>
    public CheckState State => Volatile.Read(ref status).State;

    /// <summary>Its last progress, or null before any.</summary>
    public CheckProgress? Progress => Volatile.Read(ref status).Progress;

    /// <summary>Its report, once <see cref="CheckState.Completed"/>.</summary>
    public CheckReport? Report => Volatile.Read(ref status).Report;

    /// <summary>Why it failed, timed out or was cancelled, with what to do next.</summary>
    public string? Problem => Volatile.Read(ref status).Problem;

    /// <summary>UTC time when this run published its terminal outcome, or null while unfinished.
    /// This observes completion (including cancellation/failure), not input freshness or worker exit.</summary>
    public DateTimeOffset? FinishedAt => Volatile.Read(ref status).FinishedAt;

    // The check's own work. A run ends when the work answers, its time runs out or it's cancelled; a check that ignores its
    // token can keep working after that, so the work's life is tracked apart from the run's (A180 code health rank 5).
    private Task work = Task.CompletedTask;

    /// <summary>The check's own work: it completes when the check's code returns, which can be after the run has ended, if the
    /// check ignores being stopped. Whatever it does then doesn't change the run.</summary>
    public Task Worker => work;

    /// <summary>Whether the run has ended but its check's code is still going: it was stopped, and didn't stop.</summary>
    public bool Lingering => IsOver && !work.IsCompleted;

    /// <summary>Completes when the run has ended, however it ended.</summary>
    public Task Ended => ended.Task;

    /// <summary>Whether it has ended.</summary>
    public bool IsOver => State is not (CheckState.Running or CheckState.Cancelling);

    /// <summary>Whether the project has changed since this run's snapshot (an unsaved edit; a file added, removed or
    /// changed on disk; a schema), or anything outside it that the check reads (<see cref="CheckDefinition.Reads"/>).</summary>
    public bool IsStale(DataProject project)
    {
        if (CheckSnapshot.SignatureOf(project) != startSignature || Volatile.Read(ref reads) is not { } r) return true;
        // An outside input that can't be read now can't be shown unchanged, so the result is stale, never a crash (found by A199).
        try { return ReadsSignature(Check.Reads) != r; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return true; }
    }

    // What IsStale last said, and when, for a caller that draws it every frame (A179: the Checks panel spent about 124 ms of an
    // idle frame re-reading every file's stamp, twice per check). Either answer is re-asked once its age passes: an undo back to the
    // saved file makes a stale result current again (A060).
    private (long At, bool Stale)? lastAsked;

    /// <summary>Like <see cref="IsStale(DataProject)"/>, but reusing an answer younger than <paramref name="maxAge"/>: for a panel that asks every
    /// frame. A change shows within <paramref name="maxAge"/>; an exact answer is always <see cref="IsStale(DataProject)"/>.</summary>
    public bool IsStale(DataProject project, TimeSpan maxAge)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        // No age: asks again, exactly. A recheck still on its way is older than this answer, so it's dropped, never let replace it (A198).
        if (maxAge <= TimeSpan.Zero) { var exact = IsStale(project); (lastAsked, refreshing) = ((now, exact), null); return exact; }
        // A fresh answer worked out off the frame has landed: it's the answer from now on (a fault reads as stale, never as current).
        if (refreshing is { IsCompleted: true } done)
        {
            lastAsked = (now, !done.IsCompletedSuccessfully || done.Result);
            refreshing = null;
        }
        if (lastAsked is { } seen)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(seen.At, now) < maxAge) return seen.Stale;
            // Past its age: the stamps are read on a worker, and the last answer stands until the new one lands, so a panel that
            // asks every frame never waits on the disk (A184 rank 5: 23 and 53 ms frames each time the answer expired).
            if (refreshing is null)
            {
                var signature = CheckSnapshot.SignatureLater(project);
                var (expected, reads, paths) = (startSignature, Volatile.Read(ref this.reads), Check.Reads);
                refreshing = Task.Run(() => signature() != expected || reads is null || ReadsSignature(paths) != reads);
            }
            return seen.Stale;
        }
        var stale = IsStale(project);   // the first answer is exact
        (lastAsked, refreshing) = ((now, stale), null);
        return stale;
    }

    private Task<bool>? refreshing;

    /// <summary>Whether this run says the project, as it is now, has no problems: it completed, looked at everything,
    /// found no errors or warnings, and nothing has changed since.</summary>
    public bool Clean(DataProject project) => State == CheckState.Completed && Report is { Complete: true, Findings.Count: 0 } && !IsStale(project);

    /// <summary>One word for people: running, clean, warnings, errors, incomplete, failed, cancelled or timed out; and
    /// "(stale)" when the project has changed since.</summary>
    public string Verdict(DataProject project) => Verdict(project, TimeSpan.Zero);

    /// <summary><see cref="Verdict(DataProject)"/>, reusing a staleness answer younger than <paramref name="maxAge"/>, for a panel.</summary>
    public string Verdict(DataProject project, TimeSpan maxAge)
    {
        var word = State switch
        {
            CheckState.Running => "running",
            CheckState.Cancelling => "cancelling",
            CheckState.Failed => "failed",
            CheckState.Cancelled => "cancelled",
            CheckState.TimedOut => "timed out",
            _ when !Report!.Complete => "incomplete",
            _ when Report.Findings.Any(f => f.Problem.Severity == Severity.Error) => "errors",
            _ when Report.Findings.Count > 0 => "warnings",
            _ => "clean",
        };
        return IsOver && (maxAge > TimeSpan.Zero ? IsStale(project, maxAge) : IsStale(project)) ? word + " (stale)" : word;
    }

    /// <summary>Asks the check to stop. Returns false when it has already ended. Asking twice is the same as once.</summary>
    public bool Cancel()
    {
        lock (gate)
        {
            if (IsOver) return false;
            Volatile.Write(ref status, status with { State = CheckState.Cancelling });
        }
        cancel.Cancel();
        return true;
    }

    internal void Run(ICheck check)
    {
        var progress = new Reporter(this);
        var timer = new CancellationTokenSource(check.Definition.Timeout);
        var both = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token, timer.Token);
        // Before the work starts: a quick check can finish, and its token sources be disposed below, before the next line runs.
        var deadline = Task.Delay(Timeout.InfiniteTimeSpan, timer.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        var token = both.Token;   // read once: `both` may be disposed by the time a queued run reads it
        var work = Task.Run(() =>
        {
            var inputs = Snapshot;   // captured here, on the worker
            Volatile.Write(ref reads, ReadsSignature(Check.Reads));
            return check.RunAsync(inputs, progress, token);
        }, CancellationToken.None);
        this.work = work;
        // The token lives as long as the check's own work, however long that runs after the run has ended. A fault that comes
        // after the run ended is observed here, so it's never an unobserved task exception later; it changes nothing.
        work.ContinueWith(t => { _ = t.Exception; both.Dispose(); timer.Dispose(); }, TaskScheduler.Default);
        // Whichever comes first: the check's answer, or its time running out (a check that ignores its token can't hold
        // the run open past its limit).
        Task.WhenAny(work, deadline, Task.Delay(Timeout.InfiniteTimeSpan, cancel.Token).ContinueWith(_ => { }, TaskScheduler.Default))
            .ContinueWith(_ => Finish(work, timer.IsCancellationRequested, cancel.IsCancellationRequested), TaskScheduler.Default);
    }

    private void Finish(Task<CheckReport> work, bool timedOut, bool cancelled)
    {
        lock (gate)
        {
            if (IsOver) return;
            var nextState = status.State;
            var nextReport = status.Report;
            var nextProblem = status.Problem;
            if (work.IsCompletedSuccessfully && work.Result is { } report && !cancelled)
            {
                if (report.Findings is null) (nextState, nextReport, nextProblem) = (CheckState.Failed, null, "The check returned a report with no findings list; it's a fault in the check, not a result.");
                else
                {
                    // Its own copy: a check that keeps its list can't change a finished result (found by A074; A060 C17).
                    try
                    {
                        var findings = report.Findings.ToArray();
                        // A missing finding, or one with no problem, isn't a result anyone can show (found by A199).
                        if (Array.FindIndex(findings, f => f is null || f.Problem is null || f.Problem.Message is null) is var bad and >= 0)
                            (nextState, nextReport, nextProblem) = (CheckState.Failed, null, $"The check returned finding {bad + 1} of {findings.Length} with no problem in it; that's a fault in the check, not a result.");
                        else (nextState, nextReport) = (CheckState.Completed, report with { Findings = findings, Summary = report.Summary ?? "" });
                    }
                    catch (Exception e) { (nextState, nextReport, nextProblem) = (CheckState.Failed, null, $"The check's findings couldn't be read: {e.Message}. That's a fault in the check, not a problem in the data."); }
                }
            }
            else if (cancelled) (nextState, nextProblem) = (CheckState.Cancelled, "Cancelled. Run it again for a result.");
            else if (timedOut) (nextState, nextProblem) = (CheckState.TimedOut, $"It ran past its {Check.Timeout.TotalSeconds:0.#} s limit and was stopped; nothing it found counts. Try again, or with less data.");
            else if (work.IsFaulted && !snapshot.IsValueCreated) (nextState, nextProblem) = (CheckState.Failed, $"The project's files couldn't be read for this check: {work.Exception!.GetBaseException().Message}. Nothing was checked; try again.");
            else if (work.IsFaulted) (nextState, nextProblem) = (CheckState.Failed, $"The check failed: {work.Exception!.GetBaseException().Message}. That's a fault in the check, not a problem in the data.");
            else (nextState, nextProblem) = (CheckState.Failed, "The check returned nothing.");
            // Build the terminal result completely before publishing its state. Read the latest progress here:
            // findings enumeration may reenter Reporter while this thread owns the reentrant writer gate.
            Volatile.Write(ref status, status with { State = nextState, Report = nextReport, Problem = nextProblem, FinishedAt = DateTimeOffset.UtcNow });
        }
        onCompleted?.Invoke(this);
        ended.TrySetResult();
    }

    // Progress from the worker: kept only while running, and only when it makes sense (a fraction from 0 to 1, or none).
    private sealed class Reporter(CheckRun run) : IProgress<CheckProgress>
    {
        public void Report(CheckProgress value)
        {
            if (value is null || value.Fraction is { } f && (double.IsNaN(f) || f < 0 || f > 1)) return;
            lock (run.gate) if (!run.IsOver) Volatile.Write(ref run.status, run.status with { Progress = value });
        }
    }
}

/// <summary>
/// The checks a project offers, and their runs: registered by the tools and by a game's plug-in, started on a snapshot of
/// the project, one run per check at a time. Registration is all or nothing; a bad one adds none.
/// </summary>
public sealed class Checks
{
    /// <summary>Maximum ordinary terminal runs retained for id-based paging, across all checks in this instance.</summary>
    public const int RunHistoryLimit = 8;
    private readonly Lock historyGate = new();
    private readonly List<(CheckRun Run, long Order)> history = [];
    private long completionOrder;

    // Called only by terminal publication, before Ended completes. Never touches owner-thread last/lingering/proposal state.
    private void Remember(CheckRun run)
    {
        lock (historyGate)
        {
            history.Add((run, completionOrder++));
            history.Sort((a, b) =>
            {
                var time = Nullable.Compare(a.Run.FinishedAt, b.Run.FinishedAt);
                return time != 0 ? time : a.Order.CompareTo(b.Order);
            });
            while (history.Count > RunHistoryLimit) history.RemoveAt(0);
        }
    }

    /// <summary>Reads an ordinary terminal run by its opaque id, without starting work. Unknown/evicted ids, another
    /// check's id, and currently stale inputs are refused. Undo can restore freshness. History is memory-only.</summary>
    public CheckRun? FindRun(string id, string runId, DataProject project, out string why)
    {
        CheckRun? found;
        lock (historyGate) found = history.FirstOrDefault(x => x.Run.RunId == runId).Run;
        if (found is null) { why = "That run is unavailable in this session (unknown or evicted)."; return null; }
        if (found.Check.Id != id) { why = "That run belongs to a different check."; return null; }
        if (found.IsStale(project)) { why = "That run is stale; run the check again for current findings."; return null; }
        why = "Found the requested run.";
        return found;
    }

    private readonly Dictionary<string, ICheck> checks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckRun> last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CheckRun>> lingering = new(StringComparer.Ordinal);

    /// <summary>How many ended runs of one check may still be working, ignoring being stopped, before another start is refused.
    /// One lets a timed-out check be tried again at once; a check that never stops can't pile up workers without bound.</summary>
    public const int MaxLingering = 2;

    /// <summary>The ended runs of the check <paramref name="id"/> whose code is still going.</summary>
    public int LingeringRuns(string id)
    {
        var runs = lingering.TryGetValue(id, out var older) ? older : [];
        runs.RemoveAll(r => !r.Lingering);
        // The latest run counts too, before another start records it (found by A185), and only once.
        return runs.Count + (last.TryGetValue(id, out var latest) && latest.Lingering && !runs.Contains(latest) ? 1 : 0);
    }
    private readonly Dictionary<string, JsonSchemas> optionSchemas = new(StringComparer.Ordinal);

    /// <summary>The registered checks, in the order they were added.</summary>
    public IReadOnlyList<CheckDefinition> Registered => checks.Values.Select(c => c.Definition).ToList();

    /// <summary>Adds checks, all or none. Throws <see cref="ArgumentException"/>, naming the first problem, for an empty
    /// or repeated id, an empty label, a time limit that isn't positive, or an options schema that can't be read.</summary>
    public Checks Add(params ICheck[] add)
    {
        var ids = new HashSet<string>(checks.Keys, StringComparer.Ordinal);
        var schemas = new Dictionary<string, JsonSchemas>(StringComparer.Ordinal);
        foreach (var c in add)
        {
            var d = c.Definition;
            if (string.IsNullOrWhiteSpace(d.Id)) throw new ArgumentException("A check needs an id.");
            if (!ids.Add(d.Id)) throw new ArgumentException($"There's already a check with the id '{d.Id}'.");
            if (string.IsNullOrWhiteSpace(d.Label)) throw new ArgumentException($"The check '{d.Id}' needs a label.");
            if (d.Timeout <= TimeSpan.Zero) throw new ArgumentException($"The check '{d.Id}' needs a time limit above zero.");
            if (d.OptionsSchema is { } schema)
            {
                try
                {
                    using (var parsed = JsonDocument.Parse(schema))
                        if (parsed.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
                            throw new ArgumentException("a JSON Schema is an object or true or false");   // found by A077
                    schemas[d.Id] = new JsonSchemas().Add(OptionsSchemaName, schema);
                }
                catch (Exception e) when (e is JsonException or InvalidDocumentException or ArgumentException or InvalidOperationException)
                {
                    throw new ArgumentException($"The check '{d.Id}' has an options schema that can't be read: {e.Message}");
                }
            }
        }
        foreach (var (id, schema) in schemas) optionSchemas[id] = schema;
        foreach (var c in add) checks[c.Definition.Id] = c;
        return this;
    }

    /// <summary>The last run of the check <paramref name="id"/>, or null.</summary>
    public CheckRun? Last(string id) => last.GetValueOrDefault(id);

    /// <summary>Starts the check <paramref name="id"/> on a snapshot of <paramref name="project"/> taken now, and returns
    /// at once; the check runs on a worker. <paramref name="options"/> is a JSON object of the choices it takes, checked
    /// against its schema; leave it out for none. Returns null, saying why, when there's no such check, it's already
    /// running, or the options aren't ones it accepts. Nothing is run on options it refuses.</summary>
    public CheckRun? Start(string id, DataProject project, out string why, ReferenceMap? map = null, string? options = null)
    {
        if (!checks.TryGetValue(id, out var check)) { why = $"There's no check called '{id}'."; return null; }
        if (proposalRuns.Any(r => r.Check.Id == id && !r.IsOver))
        { why = "That check is running for a proposal; wait or cancel it."; return null; }
        if (proposalRuns.Count(r => r.Check.Id == id && r.Lingering) + LingeringRuns(id) >= MaxLingering)
        { why = "Stopped checks are still working; wait for them to finish."; return null; }
        if (last.TryGetValue(id, out var running) && !running.IsOver) { why = $"'{check.Definition.Label}' is already running; wait for it, or cancel it."; return null; }
        if (running is { Lingering: true })
        {
            if (!lingering.TryGetValue(id, out var runs)) lingering[id] = runs = [];
            if (!runs.Contains(running)) runs.Add(running);
        }
        if (LingeringRuns(id) >= MaxLingering)
        {
            why = $"'{check.Definition.Label}' was stopped {MaxLingering} times and is still working, ignoring it; wait for those to finish before running it again.";
            return null;
        }
        if (OptionsProblem(id, check.Definition, options) is { } refused) { why = refused; return null; }
        var run = new CheckRun(check.Definition, CheckSnapshot.SignatureOf(project), CheckSnapshot.CaptureLater(project, map, options), Remember);
        last[id] = run;
        run.Run(check);
        why = $"Started '{check.Definition.Label}'.";
        return run;
    }

    private readonly List<CheckRun> proposalRuns = [];

    /// <summary>Starts an isolated run on an already frozen proposed snapshot. Does not replace Last.
    /// Call on the same owner thread as Start. Explicit options in the snapshot are validated as usual.</summary>
    public CheckRun? StartProposed(string id, CheckSnapshot snapshot, out string why)
    {
        if (!checks.TryGetValue(id, out var check)) { why = $"There's no check called '{id}'."; return null; }
        proposalRuns.RemoveAll(r => r.IsOver && !r.Lingering);
        if (last.TryGetValue(id, out var current) && !current.IsOver || proposalRuns.Any(r => r.Check.Id == id && !r.IsOver))
        { why = "That check is already running; wait or cancel it."; return null; }
        if (LingeringRuns(id) + proposalRuns.Count(r => r.Check.Id == id && r.Lingering) >= MaxLingering)
        { why = "Stopped checks are still working; wait for them to finish."; return null; }
        if (OptionsProblem(id, check.Definition, snapshot.Options.GetRawText()) is { } problem) { why = problem; return null; }
        var run = new CheckRun(check.Definition, snapshot);
        proposalRuns.Add(run);
        try { run.Run(check); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        { proposalRuns.Remove(run); why = "Proposal check could not start: " + e.Message; return null; }
        why = "Started isolated proposal check.";
        return run;
    }

    private const string OptionsSchemaName = "options.schema.json";

    // A key an object gives twice, at any depth, spelled the same or escaped differently, or null (A060 C21; found by A077).
    private static string? RepeatedKey(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Select(RepeatedKey).FirstOrDefault(k => k is not null);
        if (value.ValueKind != JsonValueKind.Object) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return property.Name;
            if (RepeatedKey(property.Value) is { } inner) return inner;
        }
        return null;
    }

    // Why these options can't be used, or null. An empty request is checked too, so a check with required options is
    // refused rather than run on a hidden default (A060 C05).
    private string? OptionsProblem(string id, CheckDefinition check, string? options)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(string.IsNullOrWhiteSpace(options) ? CheckSnapshot.NoOptions : options); }
        catch (JsonException e) { return $"The options for '{check.Label}' aren't JSON: {e.Message}"; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return $"The options for '{check.Label}' must be an object.";
            if (RepeatedKey(root) is { } repeated) return $"The options for '{check.Label}' name '{repeated}' twice, so which one is meant isn't clear.";
            if (!optionSchemas.TryGetValue(id, out var schema))
                return root.EnumerateObject().Any() ? $"'{check.Label}' takes no options." : null;
            var problems = schema.Validate(root, OptionsSchemaName).Where(p => p.Severity == Severity.Error).Select(p => p.Message).ToList();
            return problems.Count == 0 ? null : $"The options for '{check.Label}' can't be used: {string.Join(" ", problems)}";
        }
    }
}

/// <summary>
/// The tools' own link check: every link the reference map describes, followed on the snapshot. A link that names
/// nothing, or that two items answer, is an error with its place and the next step; a link the map only describes in
/// words is a warning. Without a map there's nothing to check, and it says so.
/// </summary>
public sealed class LinkCheck : ICheck
{
    /// <inheritdoc />
    public CheckDefinition Definition { get; } = new("links.all", "Check every link",
        "Follows every link the reference map describes, in every file, unsaved edits included: names nothing, or names two things.", TimeSpan.FromMinutes(2));

    /// <inheritdoc />
    public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
    {
        if (snapshot.Text("map") is null) return Task.FromResult(new CheckReport(true, [], "This project has no link map, so there are no links to check."));
        var folder = Directory.CreateTempSubdirectory("brutal-check-").FullName;
        try
        {
            var project = snapshot.OpenCopy(folder, out var map);
            var findings = new List<CheckFinding>();
            var count = 0;
            progress.Report(new CheckProgress(null, "Following links..."));
            foreach (var (at, result) in map!.Links(project))
            {
                cancel.ThrowIfCancellationRequested();
                count++;
                var item = Pointer.Join(Pointer.Split(at.Pointer).Take(2));
                var place = new CheckPlace(at.File, item, at.Pointer);
                var note = result.Note is { Length: > 0 } n ? n + ". " : "";
                switch (result.Outcome)
                {
                    case ReferenceOutcome.Missing:
                        findings.Add(new(Issue.Error(note + "This names nothing.", item, at.Pointer), place, "Check its spelling, or add the item it names."));
                        break;
                    case ReferenceOutcome.Ambiguous:
                        findings.Add(new(Issue.Error(note + "This names two or more items.", item, at.Pointer), place, "Give one of them a different id."));
                        break;
                    case ReferenceOutcome.NotLoaded:
                        findings.Add(new(Issue.Error(note + "The file it points into can't be opened.", item, at.Pointer), place, "See the Files panel for why."));
                        break;
                    case ReferenceOutcome.NeedsScope:
                        findings.Add(new(Issue.Warning(note + "The map describes this link only in words, so it wasn't checked.", item, at.Pointer), place, "State it in the map, so it can be checked."));
                        break;
                }
            }
            var unopened = project.Files.Where(f => !f.Open()).Select(f => f.Name).ToList();
            foreach (var name in unopened)
                findings.Add(new(Issue.Error($"{name} can't be opened, so its links weren't checked."), new CheckPlace(name), "Open it in the Files panel to see why, and repair it."));
            progress.Report(new CheckProgress(1, $"Followed {count} links."));
            return Task.FromResult(new CheckReport(unopened.Count == 0, findings, $"Followed {count} links; {findings.Count} finding(s)."));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
