using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Brutal.Authoring.Data;

/// <summary>A single-file proposal operation: set/remove at a pointer, or insert at a list pointer and index.</summary>
public sealed record EditProposalOperation(string Op, string At, JsonElement? Value = null, int? Index = null);

/// <summary>Workspace-owned, memory-only proposals. All calls belong to the constructing editor thread.
/// A host must marshal hosted agent calls there. AcceptLocal is a host UI capability, never a remote tool.</summary>
public sealed class EditProposals(DataProject project, Checks checks, ReferenceMap? map = null) : IDisposable
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, Pending> items = new(StringComparer.Ordinal);
    private readonly Dictionary<DocumentSession<DataDocument>, string> sessions = [];
    private int closed;
    private sealed class Pending
    {
        public required string Id;
        public required string Digest;
        public required DataFile File;
        public required DocumentSession<DataDocument> Session;
        public required long Revision;
        public required string Before;
        public required string After;
        public required IReadOnlyList<ICommand<DataDocument>> Commands;
        public required CheckSnapshot Snapshot;
        public required IReadOnlyList<string> SelectedChecks;
        public required IReadOnlyList<Issue> Validation;
        public List<CheckRun> Runs = [];
        public string State = "pending";
        public string? Problem;
    }
    /// <summary>The exact workspace project; hosts must not inject this store into a different project.</summary>
    public DataProject Project => project;
    /// <summary>The workspace's registry. Proposal runs never replace its ordinary Last results.</summary>
    public Checks Checks => checks;
    private string? Guard() => Environment.CurrentManagedThreadId != owner
        ? "Proposal calls require the editor thread; the host must marshal this call."
        : Volatile.Read(ref closed) == 1 ? "The proposal workspace is closed." : null;
    private static JsonObject Fail(string reason) => new() { ["error"] = reason };
    private string Identity(DocumentSession<DataDocument> session)
    {
        if (!sessions.TryGetValue(session, out var id)) sessions[session] = id = Guid.NewGuid().ToString("N");
        return id;
    }

    /// <summary>Lists proposals. With file, also supplies the current session identity/revision needed to submit.</summary>
    public JsonObject List(string? file = null)
    {
        if (Guard() is { } error) return Fail(error);
        var result = new JsonObject { ["proposals"] = new JsonArray(items.Values.Select(p => (JsonNode)ShowCore(p)).ToArray()) };
        if (file is not null)
        {
            var f = project.Find(file);
            if (f is null || !f.Open()) return Fail("The proposal file cannot be opened.");
            result["file"] = file;
            result["session"] = Identity(f.Session!);
            result["revision"] = f.Session!.Revision;
        }
        return result;
    }

    /// <summary>Submits explicit operations/check ids. No live draft, save or history is changed.</summary>
    public JsonObject Submit(string file, string session, long revision,
        IReadOnlyList<EditProposalOperation> operations, IReadOnlyList<string> checkIds)
    {
        if (Guard() is { } error) return Fail(error);
        var f = project.Find(file);
        if (f is null || !f.Open()) return Fail("The proposal file cannot be opened.");
        var source = f.Session!;
        if (Identity(source) != session || source.Revision != revision) return Fail("The proposal base session or revision is stale; review the current document.");
        if (operations.Count == 0) return Fail("A proposal needs at least one operation.");
        if (checkIds.Distinct(StringComparer.Ordinal).Count() != checkIds.Count) return Fail("Selected check ids must be unique.");
        if (checkIds.Any(id => !checks.Registered.Any(d => d.Id == id))) return Fail("A selected check is not registered; nothing was submitted.");
        var commands = new List<ICommand<DataDocument>>();
        var candidate = source.Current;
        Pending? submitted = null;
        try
        {
            foreach (var op in operations)
            {
                if (op.At is null || op.At.Length > 0 && !op.At.StartsWith('/') || op.At.Length == 0 && op.Op != "set") return Fail("Operations need a JSON Pointer; only set may replace the document root.");
                ICommand<DataDocument> command = op.Op switch
                {
                    "set" when op.Value is not null && op.Index is null => new SetValue(op.At, op.Value.GetValueOrDefault().Clone()),
                    "insert" when op.Value is not null && op.Index is >= 0 => new InsertItem(op.At, op.Index.GetValueOrDefault(), op.Value.GetValueOrDefault().Clone()),
                    "remove" when op.Value is null && op.Index is null => new RemoveItem(op.At),
                    _ => throw new ArgumentException("Only set(value), insert(value,index), and remove are supported.")
                };
                var applied = command.Apply(candidate);
                if (!applied.Ok) return Fail(string.Join(" ", applied.Issues.Select(i => i.Message)));
                candidate = applied.Document!;
                commands.Add(command);
            }
            var known = source.Problems.Select(i => (i.ObjectId, i.Path, i.Message)).ToHashSet();
            var validation = source.Validator.Validate(candidate).ToArray();
            var errors = validation.Where(i => i.Severity == Severity.Error &&
                (!source.Repairing || !known.Contains((i.ObjectId, i.Path, i.Message)))).ToList();
            if (errors.Count > 0) return Fail(string.Join(" ", errors.Select(i => i.Message)));
            if (!ReferenceEquals(f.Session, source) || source.Revision != revision) return Fail("The document changed during proposal validation.");
            var before = new DataFormat().Write(source.Current);
            var after = new DataFormat().Write(candidate);
            var snapshot = CheckSnapshot.Capture(project, map).WithDocument(file, candidate);
            var id = Guid.NewGuid().ToString("N");
            var digestText = JsonSerializer.Serialize(new { id, file, session, revision, before, after, inputHash = snapshot.InputHash, checkIds });
            var p = new Pending { Id = id, Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digestText))),
                File = f, Session = source, Revision = revision, Before = before, After = after, Commands = commands.AsReadOnly(), Snapshot = snapshot, SelectedChecks = checkIds.ToArray(), Validation = validation };
            items.Add(id, p);
            submitted = p;
            foreach (var check in checkIds)
            {
                var run = checks.StartProposed(check, snapshot, out var why);
                if (run is null)
                {
                    p.Problem = check + ": " + why;
                    foreach (var started in p.Runs) started.Cancel();
                    break;
                }
                p.Runs.Add(run);
            }
            return ShowCore(p);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            if (submitted is not null)
            {
                submitted.Problem = "Proposal checks could not start: " + e.Message;
                foreach (var run in submitted.Runs) run.Cancel();
                return ShowCore(submitted);
            }
            return Fail("Proposal could not be prepared: " + e.Message);
        }
    }

    /// <summary>Reads a proposal and its isolated check receipt without starting another check.</summary>
    public JsonObject Show(string id)
    {
        if (Guard() is { } error) return Fail(error);
        return items.TryGetValue(id, out var p) ? ShowCore(p) : Fail("No such proposal in this workspace.");
    }
    private string? Blocked(Pending p)
    {
        if (p.State != "pending") return "The proposal is " + p.State + ".";
        if (p.Problem is not null) return p.Problem;
        if (p.Runs.Count != p.SelectedChecks.Count) return "Not all selected proposal checks started.";
        if (!ReferenceEquals(project.Find(p.File.Name), p.File) || !ReferenceEquals(p.File.Session, p.Session) || p.Session.Revision != p.Revision)
            return "The proposal base session or revision is stale; submit a new proposal.";
        if (CheckSnapshot.SignatureOf(project) != p.Snapshot.Signature) return "Project inputs changed since proposal checks; submit a new proposal.";
        foreach (var run in p.Runs)
        {
            if (!run.IsOver) return "Selected proposal checks are still running.";
            if (run.State != CheckState.Completed) return "A selected proposal check " + run.Verdict(project) + "; acceptance is blocked.";
            if (run.Report is not { Complete: true } report) return "A selected proposal check is incomplete.";
            if (run.IsStale(project)) return "A selected proposal check is stale; submit a new proposal.";
            if (report.Findings.Any(f => f.Problem.Severity == Severity.Error)) return "A selected proposal check reported errors.";
        }
        return null;
    }
    private JsonObject ShowCore(Pending p)
    {
        string? blocked;
        try { blocked = Blocked(p); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { blocked = "Freshness could not be read: " + e.Message; }
        return new JsonObject
        {
            ["id"] = p.Id, ["digest"] = p.Digest, ["file"] = p.File.Name, ["session"] = Identity(p.Session),
            ["revision"] = p.Revision, ["state"] = p.State, ["before"] = p.Before, ["after"] = p.After,
            ["blocked"] = blocked, ["inputHash"] = p.Snapshot.InputHash,
            ["selectedChecks"] = new JsonArray(p.SelectedChecks.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
            ["checkCoverage"] = p.SelectedChecks.Count == 0 ? "No checks selected; only candidate validation ran." : "Only the explicitly selected checks cover this candidate; this is not a save receipt.",
            ["validation"] = new JsonArray(p.Validation.Select(i => (JsonNode)new JsonObject { ["severity"] = i.Severity.ToString(), ["message"] = i.Message }).ToArray()),
            ["checks"] = new JsonArray(p.Runs.Select(run => (JsonNode)new JsonObject {
                ["id"] = run.Check.Id, ["state"] = run.State.ToString(), ["problem"] = run.Problem,
                ["complete"] = run.Report?.Complete, ["summary"] = run.Report?.Summary,
                ["findings"] = new JsonArray((run.Report?.Findings ?? []).Select(f => (JsonNode)new JsonObject {
                    ["severity"] = f.Problem.Severity.ToString(), ["message"] = f.Problem.Message }).ToArray()) }).ToArray()),
            ["next"] = "Review exact before/after and check findings in the editor; remote acceptance is disabled."
        };
    }

    /// <summary>Rejects exactly the reviewed proposal digest without editing the document.</summary>
    public JsonObject Reject(string id, string digest)
    {
        if (Guard() is { } error) return Fail(error);
        if (!items.TryGetValue(id, out var p) || p.Digest != digest) return Fail("Proposal id/digest does not match.");
        if (p.State != "pending") return Fail("The proposal is already " + p.State + ".");
        p.State = "rejected";
        foreach (var run in p.Runs) run.Cancel();
        return ShowCore(p);
    }

    /// <summary>Host-owned local UI acceptance, never exposed by MCP. Rechecks base and check receipts, then commits one undo step.</summary>
    public JsonObject AcceptLocal(string id, string digest)
    {
        if (Guard() is { } error) return Fail(error);
        if (!items.TryGetValue(id, out var p) || p.Digest != digest) return Fail("Proposal id/digest does not match.");
        string? blocked;
        try { blocked = Blocked(p); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("Freshness could not be read: " + e.Message); }
        if (blocked is not null) return Fail(blocked);
        if (!p.Session.ExecuteBatch(p.Commands)) return Fail(string.Join(" ", p.Session.LastIssues.Select(i => i.Message)));
        p.State = "accepted";
        return ShowCore(p);
    }

    /// <summary>Closes the workspace's proposals and requests cancellation; does not wait on workers or write data.</summary>
    public void Dispose()
    {
        // Closing only cancels running checks, which is safe from any thread (an async test or host may finish elsewhere).
        if (Interlocked.Exchange(ref closed, 1) == 1) return;
        foreach (var p in items.Values) foreach (var run in p.Runs) run.Cancel();
    }
}
