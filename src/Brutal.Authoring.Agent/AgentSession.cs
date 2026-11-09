using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;

namespace Brutal.Authoring.Agent;

/// <summary>
/// The editor for an AI agent (goal 20): a data project worked through calls, not a window. Every answer is plain JSON
/// with a <c>next</c> hint saying what to do from here, so an agent new to a project is useful in one or two calls.
/// <para>
/// Edits go the same way a person's do: the document's own commands, its schema, the project's read-only and
/// outside-change refusals, one undo step each, and style-keeping saves. Any edit can be previewed first. Nothing is
/// saved unless asked, and nothing here ever touches git.
/// </para>
/// INVENTED: the self-describing calls and the hints; they sit over goal 2's commands and journal.
/// </summary>
public sealed class AgentSession
{
    /// <summary>Creates an ordinary standalone session, without proposal hosting.</summary>
    public AgentSession(DataProject project, ReferenceMap? map = null)
    { Project = project; Map = map; Checks = new Checks().Add(new LinkCheck()); }

    /// <summary>A hosted session sharing the workspace proposal store. The host must marshal proposal calls to its editor thread.</summary>
    public AgentSession(DataProject project, ReferenceMap? map, EditProposals proposals)
    {
        if (!ReferenceEquals(project, proposals.Project)) throw new ArgumentException("Proposal store belongs to a different project.", nameof(proposals));
        Project = project; Map = map; Checks = proposals.Checks; this.proposals = proposals;
    }
    private readonly EditProposals? proposals;

    /// <summary>The project being worked.</summary>
    public DataProject Project { get; }

    /// <summary>The game's reference map, if any.</summary>
    public ReferenceMap? Map { get; }

    /// <summary>The project's checks: the tools' link check, and any a game adds (piece J).</summary>
    public Checks Checks { get; }

    private static JsonObject NoProposalHost() => new() { ["error"] = "Proposals need a running editor; host dispatch is required." };
    /// <summary>Lists proposals and optionally the file session/revision for submission. Passive; hosted only.</summary>
    public JsonObject ListProposals(string? file = null) => proposals?.List(file) ?? NoProposalHost();
    /// <summary>Reads an existing proposal and its candidate check receipt without executing it.</summary>
    public JsonObject ShowProposal(string id) => proposals?.Show(id) ?? NoProposalHost();
    /// <summary>Rejects exactly an id/digest; no document edit.</summary>
    public JsonObject RejectProposal(string id, string digest) => proposals?.Reject(id, digest) ?? NoProposalHost();
    /// <summary>Remote acceptance is always refused. Only the person's host-owned editor action accepts.</summary>
    public JsonObject AcceptProposal(string id, string digest) => new() { ["error"] = "Remote proposal acceptance is disabled; review and accept in the editor." };
    /// <summary>Submits only explicit set/insert/remove operations and explicit check ids, never arbitrary catalog commands.</summary>
    public JsonObject SubmitProposal(string file, string session, long revision, string operations, string checkIds)
    {
        if (proposals is null) return NoProposalHost();
        try
        {
            using var ops = JsonDocument.Parse(operations);
            using var ids = JsonDocument.Parse(checkIds);
            if (ops.RootElement.ValueKind != JsonValueKind.Array || ids.RootElement.ValueKind != JsonValueKind.Array)
                return new JsonObject { ["error"] = "operations and checks must be arrays." };
            var parsed = new List<EditProposalOperation>();
            foreach (var op in ops.RootElement.EnumerateArray())
            {
                if (op.ValueKind != JsonValueKind.Object || op.EnumerateObject().Any(p => p.Name is not ("op" or "at" or "value" or "index")) ||
                    op.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != op.EnumerateObject().Count())
                    return new JsonObject { ["error"] = "Each operation needs unique op/at/value/index fields only." };
                parsed.Add(new(op.GetProperty("op"u8).GetString()!, op.GetProperty("at"u8).GetString()!,
                    op.TryGetProperty("value"u8, out var value) ? value.Clone() : null,
                    op.TryGetProperty("index"u8, out var index) ? index.GetInt32() : null));
            }
            return proposals.Submit(file, session, revision, parsed, ids.RootElement.EnumerateArray().Select(id => id.GetString()!).ToArray());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { return new JsonObject { ["error"] = "Invalid proposal arguments: " + e.Message }; }
    }

    // ---- checks ----

    /// <summary>The checks there are, each with its last verdict.</summary>
    public JsonObject ListChecks()
    {
        var list = new JsonArray();
        foreach (var d in Checks.Registered)
        {
            var entry = new JsonObject { ["id"] = d.Id, ["label"] = d.Label, ["description"] = d.Description };
            if (d.OptionsSchema is { } schema) entry["options"] = JsonNode.Parse(schema);
            entry["last"] = "never run";
            entry["finishedAt"] = null;
            entry["ageSeconds"] = null;
            if (Checks.Last(d.Id) is { } run)
            {
                var finished = run.FinishedAt;
                entry["last"] = run.Verdict(Project);
                if (finished is { } at)
                {
                    entry["finishedAt"] = at.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                    // Wall-clock age is display-only; clock rollback never changes run scheduling or freshness.
                    entry["ageSeconds"] = Math.Max(0, (DateTimeOffset.UtcNow - at).TotalSeconds);
                }
            }
            list.Add(entry);
        }
        return new JsonObject { ["checks"] = list,
            ["saveCoverage"] = "Save coverage unknown: these are the latest registered check runs, not a record of checks performed for a save.",
            ["next"] = "check(id) runs one on the project as it is now, unsaved edits included" };
    }

    /// <summary>Runs the check <paramref name="id"/> on the project as it is now and waits for it (up to its own time
    /// limit): its verdict, whether that's clean, every finding with its place and next step, and the snapshot's hash.
    /// With <paramref name="limit"/> it returns that many findings (at most <see cref="MostFindings"/>) and how to get the
    /// rest. With <paramref name="first"/> it reads on from that finding in the same run without running it again, so the
    /// pages belong together; it runs the check only if there's no run to read.
    /// <paramref name="options"/> is a JSON object of the choices the check takes (<see cref="ListChecks"/> gives their
    /// schema). Options start a run, so they can't go with <paramref name="first"/>, which reads one.</summary>
    /// <param name="runId">Optional terminal-run id in this session. Reads only that run, refuses stale/evicted ids,
    /// and never starts work. Without it the legacy latest-run behavior is unchanged.</param>
    public JsonObject RunCheck(string id, int? first = null, int? limit = null, string? options = null, string? runId = null)
    {
        if (runId is not null && (string.IsNullOrWhiteSpace(runId) || options is not null))
            return Fail("run needs a nonempty id and cannot be combined with options", "options start a new run; run reads an existing one");
        if (first is not null && options is not null) return Fail("options start a new run, and first reads the last one, so they can't go together", "check with options and limit, then read on with first alone");
        if (first < 0) return Fail($"first can't be {first}: findings count from 0", "first=0 is the first finding");
        if (limit is < 1 or > MostFindings) return Fail($"limit can't be {limit}: it's from 1 to {MostFindings}", $"leave it out for every finding, or read {MostFindings} at a time");
        var paged = runId is not null || first is not null || limit is not null;
        CheckRun? run;
        if (runId is not null)
        {
            run = Checks.FindRun(id, runId, Project, out var why);
            if (run is null) return Fail(why, "run ids last only within one session; start a new check without run when needed");
        }
        else run = first is not null ? Checks.Last(id) : null;
        if (run is { IsOver: false }) return Fail($"'{run.Check.Label}' is still running, so it has no findings to read yet", "try again when it has ended");
        if (run is null)
        {
            if (Checks.Start(id, Project, out var why, Map, options) is not { } started) return Fail(why, "checks lists them, with the options each takes");
            run = started;
            run.Ended.Wait(run.Check.Timeout + TimeSpan.FromSeconds(5));
        }
        var result = new JsonObject
        {
            ["id"] = id, ["state"] = run.State.ToString(), ["verdict"] = run.Verdict(Project), ["clean"] = run.Clean(Project),
            ["inputHash"] = run.Captured?.InputHash,   // null when the files couldn't be read: the problem says why
        };
        if (run.RunId is { } completedId) result["run"] = completedId;
        if (run.Captured is { } captured && captured.Options.EnumerateObject().Any()) result["options"] = JsonNode.Parse(captured.Options.GetRawText());
        if (run.Problem is { } problem) result["problem"] = problem;
        if (run.Report is { } report)
        {
            result["summary"] = report.Summary;
            result["complete"] = report.Complete;
            var start = Math.Min(first ?? 0, report.Findings.Count);
            var count = Math.Min(limit ?? (paged ? DefaultFindings : report.Findings.Count), report.Findings.Count - start);
            result["findings"] = new JsonArray(report.Findings.Skip(start).Take(count).Select((f, i) =>
            {
                var finding = new JsonObject
                {
                    ["severity"] = f.Problem.Severity.ToString().ToLowerInvariant(), ["message"] = f.Problem.Message,
                    ["file"] = f.Place?.File, ["item"] = f.Place?.Item, ["at"] = f.Place?.At, ["next"] = f.Next,
                };
                if (paged) finding["ordinal"] = start + i;
                return (JsonNode)finding;
            }).ToArray());
            if (paged)
            {
                result["total"] = report.Findings.Count;
                result["first"] = start;
                result["stale"] = run.IsStale(Project);
                if (start + count < report.Findings.Count) result["nextFirst"] = start + count;
            }
        }
        var stale = paged && run.IsStale(Project);
        if (runId is not null && stale)
            return Fail("That run is stale; run the check again for current findings.", "start a new check without run");
        result["next"] = result["nextFirst"] is { } more
            ? (stale ? "the project has changed since this run, so this is an old result: check again without first for a current one, or " : "") + $"check again with {(runId is null ? "" : $"run={runId} and ")}first={more} to read on in this same run"
            : run.Clean(Project) ? "nothing to fix"
            : stale ? "the project has changed since this run: check again without first for a current result"
            : run.Report is { Findings.Count: > 0 } ? "get each finding's place, fix it with set, then check again" : "see problem";
        return result;
    }

    /// <summary>The most findings one page of <see cref="RunCheck"/> can hold.</summary>
    public const int MostFindings = 500;

    // A page's size when first is given without a limit (A060 C17: "paging default 50", as find has).
    private const int DefaultFindings = 50;

    // ---- understanding ----

    /// <summary>
    /// The whole project at a glance: each file with its title and description from its schema, whether it can be
    /// saved (and why not), its top-level lists with item counts, and how many problems it has; then the calls that
    /// edit, and where to start. Opening a file to count it doesn't change it.
    /// </summary>
    public JsonObject Describe()
    {
        var files = new JsonArray();
        foreach (var f in Project.Files)
        {
            var entry = new JsonObject { ["file"] = f.Name };
            var opened = f.Open();
            if (f.Schema is { } schema && Project.Schemas.Find(schema) is { ValueKind: JsonValueKind.Object } s)
            {
                entry["schema"] = schema;
                if (Text(s, "title") is { } title) entry["title"] = title;
                if (Text(s, "description") is { } description) entry["description"] = FirstSentence(description);
            }
            if (f.ReadOnlyReason is { } why) entry["readOnly"] = why;
            if (!opened)
            {
                entry["cantOpen"] = string.Join(" ", f.Problems.Select(p => p.Message));
                files.Add(entry);
                continue;
            }
            var root = f.Session!.Current.Root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var lists = new JsonArray();
                foreach (var p in root.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object))
                {
                    var list = new JsonObject { ["at"] = Pointer.Join([p.Name]), ["count"] = p.Value.ValueKind == JsonValueKind.Array ? p.Value.GetArrayLength() : p.Value.EnumerateObject().Count() };
                    if (Map?.LabelField(f.Name, Pointer.Join([p.Name, "0"])) is { } labelField) list["labelField"] = labelField;
                    lists.Add(list);
                }
                entry["lists"] = lists;
            }
            var problems = f.Session.Validator.Validate(f.Session.Current).Count(i => i.Severity == Severity.Error);
            if (problems > 0) entry["problems"] = problems;
            if (f.Dirty) entry["unsaved"] = true;
            files.Add(entry);
        }
        return new JsonObject
        {
            ["game"] = Map?.Game,
            ["data"] = Project.DataFolder,
            ["readOnly"] = Project.ReadOnly,
            ["files"] = files,
            ["calls"] = new JsonArray(
                "describe: this summary",
                "find(file, list, text?): items in a list whose label or fields contain text",
                "get(file, at): a value, with its label and, for an id, where it leads",
                "problems(file?): what the schemas find wrong",
                "refs(file, at): everything that refers to an item, through the reference map",
                "repair(file): open a file that doesn't fit its schema, to fix it",
                "set(file, at, value, preview?): change one value (JSON); preview shows it without applying",
                "insert(file, list, index, value, preview?) / remove(file, at, preview?)",
                "undo(file), save(file?)",
                "create(file, schema, preview?) / rename(file, to, preview?) / delete(file, preview?) / restore(file): files themselves, changed on disk at once",
                "checks / check(id): the project's checks, such as every link, run on it as it is now"),
            ["next"] = "find or get in the file you need; set with preview=true first; save when the edits are right",
        };
    }

    /// <summary>The items of the list at <paramref name="list"/> in <paramref name="file"/> whose label, or any scalar
    /// field, contains <paramref name="text"/> (ignoring case), or all of them; at most <paramref name="limit"/>.</summary>
    public JsonObject Find(string file, string list, string? text = null, int limit = 50)
    {
        if (NotAPlace(list) is { } bad) return bad;
        if (Open(file, out var f) is { } refused) return refused;
        if (f.Session!.Current.At(list) is not { } at || at.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
            return Fail($"{list} isn't a list or object in {file}", "describe shows each file's lists");
        var items = new JsonArray();
        var total = 0;
        var entries = at.ValueKind == JsonValueKind.Array
            ? at.EnumerateArray().Select((v, i) => (Pointer: $"{list}/{i}", Value: v))
            : at.EnumerateObject().Select(p => (Pointer: list + Pointer.Join([p.Name]), Value: p.Value));
        foreach (var (pointer, value) in entries)
        {
            var label = LabelOf(f, pointer, value);
            if (text is { Length: > 0 } && !Contains(label, text) && !Scalars(value).Any(s => Contains(s, text))) continue;
            total++;
            if (items.Count < limit) items.Add(new JsonObject { ["at"] = pointer, ["label"] = label });
        }
        return new JsonObject
        {
            ["file"] = file, ["list"] = list, ["matches"] = total, ["items"] = items,
            ["next"] = total > items.Count ? $"showing {items.Count} of {total}; narrow the text, or raise the limit (limit, or --limit on the command line)" : "get(file, at) to see an item",
        };
    }

    /// <summary>The value at <paramref name="at"/>, with its label when it's a list item, and, when the map says it's an
    /// id, where it leads.</summary>
    public JsonObject Get(string file, string at)
    {
        if (NotAPlace(at) is { } bad) return bad;
        if (Open(file, out var f) is { } refused) return refused;
        if (f.Session!.Current.At(at) is not { } value) return Fail($"nothing is at {at} in {file}", "find lists a list's items and their places");
        var result = new JsonObject { ["file"] = file, ["at"] = at, ["value"] = JsonNode.Parse(value.GetRawText()) };
        if (Pointer.Split(at).Count >= 2 && value.ValueKind == JsonValueKind.Object) result["label"] = LabelOf(f, at, value);
        if (Map is { } m && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            var r = m.Resolve(Project, file, at, value);
            if (r.Outcome != ReferenceOutcome.NotAReference)
            {
                result["link"] = new JsonObject { ["outcome"] = r.Outcome.ToString(), ["file"] = r.Target?.File, ["at"] = r.Target?.Pointer, ["note"] = r.Note };
            }
        }
        result["next"] = "set(file, at, value, preview=true) to change it";
        return result;
    }

    /// <summary>What the schemas find wrong: in one file, or in every file of the project.</summary>
    public JsonObject Problems(string? file = null)
    {
        var list = new JsonArray();
        foreach (var f in Project.Files.Where(f => file is null || f.Name == file))
        {
            if (!f.Open()) { foreach (var p in f.Problems) list.Add(Problem(f.Name, p)); continue; }
            foreach (var p in f.Session!.Validator.Validate(f.Session.Current).Where(i => i.Severity == Severity.Error)) list.Add(Problem(f.Name, p));
        }
        return new JsonObject { ["count"] = list.Count, ["problems"] = list, ["next"] = list.Count == 0 ? "nothing to fix" : "get the place, then set it right" };
    }

    /// <summary>Everything in the project that refers to the item at <paramref name="at"/> in <paramref name="file"/>,
    /// through the reference map: each place, with its item's label. Needs a map.</summary>
    public JsonObject Refs(string file, string at)
    {
        if (NotAPlace(at) is { } bad) return bad;
        if (Map is null) return Fail("this project has no reference map, so links aren't known", "start the tool with a map");
        if (Open(file, out var f) is { } refused) return refused;
        if (f.Session!.Current.At(at) is null) return Fail($"nothing is at {at} in {file}", "find lists a list's items and their places");
        var from = new JsonArray();
        foreach (var r in Map.ReferencesTo(Project, new ReferenceTarget(file, at)))
        {
            var entry = new JsonObject { ["file"] = r.File, ["at"] = r.Pointer };
            var parts = Pointer.Split(r.Pointer);
            if (parts.Count >= 2 && Project.Find(r.File) is { Session: { } s } source && s.Current.At(Pointer.Join(parts.Take(2))) is { ValueKind: JsonValueKind.Object } item)
                entry["item"] = LabelOf(source, Pointer.Join(parts.Take(2)), item);
            from.Add(entry);
        }
        return new JsonObject
        {
            ["file"] = file, ["at"] = at, ["count"] = from.Count, ["from"] = from,
            ["next"] = from.Count == 0 ? "nothing refers to it; removing it breaks no link" : "get any of them; changing this item's id would break these links",
        };
    }

    /// <summary>Opens a file that doesn't fit its schema for repair: its problems are listed, and edits that add none
    /// are accepted until none are left.</summary>
    public JsonObject Repair(string file)
    {
        if (Project.Find(file) is not { } f) return Fail($"there's no file called {file}", "describe lists the files");
        if (!f.Repair()) return Fail($"{file} can't be repaired here: {string.Join(" ", f.Problems.Select(p => p.Message))}", "fix it outside the editor first");
        var problems = new JsonArray(f.Session!.Problems.Select(p => (JsonNode)Say(p)).ToArray());
        return new JsonObject
        {
            ["file"] = file, ["repairing"] = f.Session.Repairing, ["problems"] = problems,
            ["next"] = f.Session.Repairing ? "set each problem's place right; edits that add no problem are accepted; save when done" : "it's valid; edit as usual",
        };
    }

    // ---- files ----

    /// <summary>Makes a new file from a schema (<see cref="DataProject.Create"/>), on disk at once; preview shows what it
    /// would hold.</summary>
    public JsonObject Create(string file, string schema, bool preview = false)
    {
        if (!Project.Create(file, schema, out var why, preview)) return Fail(why, Project.SchemaNames.Count > 0 ? "schemas: " + string.Join(", ", Project.SchemaNames) : "the schema folder is empty");
        return new JsonObject { ["file"] = file, ["schema"] = schema, [preview ? "wouldDo" : "done"] = why, ["next"] = preview ? "create without preview to make it" : "insert items into its lists; save(file) after edits" };
    }

    /// <summary>Renames a file (<see cref="DataProject.Rename"/>), on disk at once; says how many links point into it,
    /// since the reference map names files.</summary>
    public JsonObject Rename(string file, string to, bool preview = false)
    {
        var links = Map?.LinksInto(Project, file) ?? 0;
        if (!Project.Rename(file, to, out var why, preview)) return Fail(why, "describe lists the files and what can be saved");
        var result = new JsonObject { ["file"] = file, ["to"] = to, [preview ? "wouldDo" : "done"] = why, ["linksInto"] = links };
        result["next"] = links > 0 ? "the reference map names this file: update it, or these links point at the old name" : preview ? "rename without preview to do it" : "describe shows the new name";
        return result;
    }

    /// <summary>Deletes a file (<see cref="DataProject.Delete"/>), on disk at once; restore(file) puts it back while this
    /// session lasts. Preview first: it says how many links would be left naming nothing.</summary>
    public JsonObject Delete(string file, bool preview = false)
    {
        var links = Map?.LinksInto(Project, file) ?? 0;
        if (!Project.Delete(file, out var why, preview)) return Fail(why, "describe lists the files and what can be saved");
        return new JsonObject
        {
            ["file"] = file, [preview ? "wouldDo" : "done"] = why, ["linksInto"] = links,
            ["next"] = preview ? (links > 0 ? $"{links} link(s) in other files would name nothing; refs(file, at) lists them by item" : "delete without preview to do it")
                : "restore(file) puts it back while this session lasts",
        };
    }

    /// <summary>Puts back a file deleted in this session (<see cref="DataProject.Restore"/>).</summary>
    public JsonObject Restore(string file) =>
        Project.Restore(file, out var why) ? new JsonObject { ["file"] = file, ["done"] = why, ["next"] = "describe shows it" }
            : Fail(why, Project.Deleted.Count > 0 ? "deleted here: " + string.Join(", ", Project.Deleted) : "nothing was deleted in this session");

    // ---- editing ----

    /// <summary>Sets the value at <paramref name="at"/> to <paramref name="json"/> (any JSON; <c>null</c> as text
    /// removes the field). With <paramref name="preview"/>, shows what would change and whether the schema accepts it,
    /// changing nothing.</summary>
    public JsonObject Set(string file, string at, string json, bool preview = false)
    {
        JsonElement? value;
        try { value = json.Trim() == "null" ? null : JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException e) { return Fail($"the value isn't JSON: {e.Message}", "write text in quotes (\"ore\"), numbers bare (12.5)"); }
        return Edit(file, new SetValue(at, value), at, preview);
    }

    /// <summary>Inserts <paramref name="json"/> into the list at <paramref name="list"/> before <paramref name="index"/>
    /// (the list's length appends).</summary>
    public JsonObject Insert(string file, string list, int index, string json, bool preview = false)
    {
        JsonElement value;
        try { value = JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException e) { return Fail($"the value isn't JSON: {e.Message}", "give the whole item as a JSON object"); }
        return Edit(file, new InsertItem(list, index, value), $"{list}/{index}", preview);
    }

    /// <summary>Removes the list item at <paramref name="at"/>.</summary>
    public JsonObject Remove(string file, string at, bool preview = false) => Edit(file, new RemoveItem(at), at, preview);

    /// <summary>Undoes the last edit to <paramref name="file"/>.</summary>
    public JsonObject Undo(string file)
    {
        if (Open(file, out var f) is { } refused) return refused;
        return f.Session!.Undo()
            ? new JsonObject { ["undone"] = true, ["unsaved"] = f.Dirty, ["next"] = "carry on, or save(file)" }
            : Fail("there's nothing to undo in " + file, "undo only steps back through this session's edits");
    }

    /// <summary>Saves one file, or every file with unsaved edits. Refused for a read-only file, or one changed outside
    /// since it was opened; the edits stay either way.</summary>
    public JsonObject Save(string? file = null)
    {
        var targets = file is null ? Project.Dirty : Project.Find(file) is { } one ? [one] : [];
        if (file is not null && targets.Count == 0) return Fail($"there's no file called {file}", "describe lists the files");
        var saved = new JsonArray();
        var refused = new JsonArray();
        // All of them together when no file is named, as a person's Save all: files that depend on each other can only go together.
        var notSaved = file is null ? Project.SaveAll() : [.. targets.Where(f => !f.Save())];
        foreach (var f in targets)
            if (notSaved.Contains(f)) refused.Add(new JsonObject { ["file"] = f.Name, ["why"] = string.Join(" ", f.Problems.Select(p => p.Message)) });
            else saved.Add(f.Name);
        return new JsonObject
        {
            ["saved"] = saved, ["refused"] = refused,
            ["next"] = refused.Count == 0 ? "done; nothing is committed to git by this tool" : "a refused file keeps its edits; read why, then decide",
        };
    }

    private JsonObject Edit(string file, ICommand<DataDocument> command, string at, bool preview)
    {
        if (NotAPlace(at) is { } bad) return bad;
        if (Open(file, out var f) is { } refused) return refused;
        var session = f.Session!;
        var before = session.Current.At(at) is { } b ? JsonNode.Parse(b.GetRawText()) : null;
        var applied = command.Apply(session.Current);
        if (!applied.Ok) return Fail(string.Join(" ", applied.Issues.Select(i => Say(i))), "check the place with get, or the list with find");
        var issues = session.Validator.Validate(applied.Document!).Where(i => i.Severity == Severity.Error).ToList();
        if (session.Repairing)   // repairing: problems already there may stay; only ones this edit adds refuse it (found by A056)
        {
            var known = session.Problems.Select(p => (p.ObjectId, p.Path, p.Message)).ToHashSet();
            issues = issues.Where(i => !known.Contains((i.ObjectId, i.Path, i.Message))).ToList();
        }
        var after = applied.Document!.At(at) is { } a ? JsonNode.Parse(a.GetRawText()) : null;
        var change = new JsonObject { ["file"] = file, ["at"] = at, ["before"] = before, ["after"] = after };
        if (f.ReadOnlyReason is { } why) change["readOnly"] = why + "; you can preview and edit, but not save it";
        if (issues.Count > 0)
        {
            change["refused"] = new JsonArray(issues.Select(i => (JsonNode)Say(i)).ToArray());
            change["next"] = "the schema refuses this; change the value and try again";
            return change;
        }
        if (preview)
        {
            change["preview"] = true;
            change["next"] = "the schema accepts it; call again without preview to apply";
            return change;
        }
        if (!session.Execute(command))
        {
            change["refused"] = new JsonArray(session.LastIssues.Select(i => (JsonNode)Say(i)).ToArray());
            return change;
        }
        change["applied"] = true;
        change["unsaved"] = true;
        change["next"] = f.ReadOnlyReason is null ? "save(file) when ready; undo(file) steps back" : "this file can't be saved here; undo(file) steps back";
        return change;
    }

    // ---- assets: declared origins (content rights) ----

    /// <summary>The folder imported assets live in, or null when this session works on data only (then <see cref="Import"/>
    /// and <see cref="Declare"/> are refused). Set with <see cref="UseAssets"/>.</summary>
    public string? AssetFolder { get; private set; }

    private IReadOnlyCollection<string>? ourNames;

    /// <summary>Works assets under <paramref name="folder"/> as well: imports and declarations go through
    /// <see cref="AssetImport"/>, as the editor's do, and the content rights check (<see cref="ContentRightsCheck"/>) is
    /// registered. <paramref name="ownNames"/> are the project's own rights holders.</summary>
    public AgentSession UseAssets(string folder, IReadOnlyCollection<string>? ownNames = null)
    {
        AssetFolder = Path.GetFullPath(folder);
        ourNames = ownNames;
        Checks.Add(new ContentRightsCheck(AssetFolder, ourNames: ownNames));
        return this;
    }

    /// <summary>Imports the asset <paramref name="file"/> (relative to <see cref="AssetFolder"/>) with its origin declared:
    /// <c>ours</c>; <c>licensed</c> with <paramref name="licence"/> and <paramref name="source"/>; or <c>publicDomain</c>
    /// with <paramref name="source"/>. Refused, with exactly what was found, when the file's own rights tags contradict the
    /// declaration (see <see cref="AssetImport.Import"/>). Writes the sidecar at once, unless the project is read-only or its
    /// <see cref="DataProject.FileCheck"/> refuses the write (<see cref="FileChangeKind.Replace"/>).</summary>
    public JsonObject Import(string file, string origin, string? licence = null, string? source = null, string? author = null, string? aiUse = null)
    {
        if (AssetPath(file, out var path) is { } no) return no;
        if (!AssetOrigins.TryParse(origin, out var declared))
            return Fail($"'{origin}' isn't an origin: an import declares ours, licensed or publicDomain", "ours: made by this project; licensed: give licence and source; publicDomain: give source");
        var ai = aiUse?.ToLowerInvariant() switch { null or "" or "unknown" => AiUse.Unknown, "allowed" => AiUse.Allowed, "forbidden" => AiUse.Forbidden, _ => (AiUse?)null };
        if (ai is null) return Fail($"'{aiUse}' isn't allowed, forbidden or unknown", "leave aiUse out for unknown (treated as forbidden)");
        var provenance = new Provenance(author ?? "", licence ?? "", source ?? "", "", declared);
        var importer = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var sidecarName = Path.GetRelativePath(AssetFolder!, AssetRecord.SidecarFor(path)).Replace(Path.DirectorySeparatorChar, '/');
        var result = AssetImport.Import(path, importer.Length > 0 ? importer : "file", 1, JsonDocument.Parse("{}").RootElement, provenance, ai.Value, ourNames,
            record => WriteRefusal(new FileChange(FileChangeKind.Replace, sidecarName, Text: record.Write())));
        var answer = new JsonObject { ["file"] = file, ["declared"] = AssetOrigins.Name(declared) };
        answer["rightsFound"] = new JsonArray(result.Scan.Tags.Select(t => (JsonNode)t.ToString()).ToArray());
        var warnings = result.Issues.Where(i => i.Severity != Severity.Error).Select(i => (JsonNode)i.Message).ToArray();
        if (warnings.Length > 0) answer["warnings"] = new JsonArray(warnings);
        if (!result.Ok)
        {
            answer["refused"] = new JsonArray(result.Issues.Where(i => i.Severity == Severity.Error).Select(i => (JsonNode)i.Message).ToArray());
            answer["next"] = "nothing was written: correct the declaration, or use a file whose tags agree with it";
            return answer;
        }
        answer["imported"] = result.Record!.Guid;
        answer["next"] = "imported; check rights.assets lists every asset's origin";
        return answer;
    }

    /// <summary>Changes an imported asset's declared origin through its <see cref="DeclareAssetOrigin"/> command, judged
    /// against the tags found at import as an import is. With <paramref name="preview"/> nothing is written; otherwise the
    /// sidecar is saved at once.</summary>
    public JsonObject Declare(string file, string origin, string? licence = null, string? source = null, bool preview = false)
    {
        if (AssetPath(file, out var path) is { } no) return no;
        if (!File.Exists(AssetRecord.SidecarFor(path))) return Fail($"{file} isn't imported yet", "import it with its origin declared");
        if (!AssetOrigins.TryParse(origin, out var declared))
            return Fail($"'{origin}' isn't an origin: declare ours, licensed or publicDomain", "ours: made by this project; licensed: give licence and source; publicDomain: give source");
        DocumentSession<AssetRecord> record;
        try { record = AssetImport.Open(path, ourNames); }
        catch (DocumentFormatException e) { return Fail($"{file}'s record can't be read: {e.Message}", "fix or restore its sidecar"); }
        var answer = new JsonObject { ["file"] = file, ["declared"] = AssetOrigins.Name(declared) };
        if (!record.Execute(new DeclareAssetOrigin(declared, licence ?? "", source ?? "")))
        {
            answer["refused"] = new JsonArray(record.LastIssues.Where(i => i.Severity == Severity.Error).Select(i => (JsonNode)i.Message).ToArray());
            answer["next"] = "nothing was written: correct the declaration";
            return answer;
        }
        if (preview) { answer["preview"] = true; answer["next"] = "run it again without preview to write it"; return answer; }
        var sidecarName = Path.GetRelativePath(AssetFolder!, AssetRecord.SidecarFor(path)).Replace(Path.DirectorySeparatorChar, '/');
        if (WriteRefusal(new FileChange(FileChangeKind.Replace, sidecarName, Text: record.TextToSave)) is { } refusedWrite)
        {
            answer["refused"] = new JsonArray(refusedWrite);
            answer["next"] = "nothing was written";
            return answer;
        }
        record.Save();
        answer["written"] = true;   // not "applied": the command line saves data files after an applied edit
        answer["next"] = "saved beside the asset";
        return answer;
    }

    // Why an asset record mustn't be written: the project is read-only, or the game's file check says no (a check that
    // throws is a refusal, as it is for data files). Null when it may be.
    private string? WriteRefusal(FileChange change)
    {
        if (Project.ReadOnly) return "the project is open read-only, so no asset record can be written";
        try { return Project.FileCheck?.Invoke(change) is { } no ? $"the game refused writing {change.Name}: {no}" : null; }
        catch (Exception e) when (e is not OutOfMemoryException) { return $"the game's file check failed on {change.Name} ({e.Message}), so nothing was written"; }
    }

    // An asset's full path inside the asset folder; a path outside it (or no asset folder) is refused.
    private JsonObject? AssetPath(string file, out string path)
    {
        path = "";
        if (AssetFolder is null) return Fail("this session has no asset folder", "open the project with an asset folder (--assets) to import assets");
        var full = Path.GetFullPath(Path.Combine(AssetFolder, file));
        var root = AssetFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return Fail($"{file} is outside the asset folder", "name a file inside the asset folder, relative to it");
        if (!File.Exists(full)) return Fail($"there's no file {file} in the asset folder", "copy it into the asset folder first");
        path = full;
        return null;
    }

    // ---- helpers ----

    private JsonObject? Open(string file, out DataFile f)
    {
        f = Project.Find(file)!;
        if (f is null) return Fail($"there's no file called {file}", "describe lists the files");
        return f.Open() ? null : Fail($"{file} can't be opened: {string.Join(" ", f.Problems.Select(p => p.Message))}", "problems(file) shows what's wrong with it");
    }

    private string LabelOf(DataFile f, string pointer, JsonElement value) =>
        Map?.Label(f.Name, pointer, value) ?? DataEditing.OutlineFor(Project.Schemas, f.Schema!).Label(f.Session!.Current, pointer) ?? pointer;

    private static JsonObject Fail(string why, string next) => new() { ["error"] = why, ["next"] = next };

    // A place that isn't a JSON pointer gets an honest error at the call, never a thrown FormatException (found by A183).
    private static JsonObject? NotAPlace(string? at) =>
        at is null ? Fail("no place was given", "a place is a JSON pointer, like /wares/0/price; \"\" is the whole file")
        : at.Length > 0 && at[0] != '/' ? Fail($"'{at}' isn't a place: a JSON pointer starts with /", "try /" + at + ", or find to list a list's items and their places")
        : null;

    private static JsonObject Problem(string file, Issue i) => new() { ["file"] = file, ["path"] = i.Path, ["message"] = i.Message };

    private static string Say(Issue i) => i.Path is { } p ? $"{p}: {i.Message}" : i.Message;

    private static string? Text(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? text : text[..(end + 1)];
    }

    private static bool Contains(string? s, string text) => s is not null && s.Contains(text, StringComparison.OrdinalIgnoreCase);

    // A value's scalar fields as text: strings as they are, numbers and true/false as written (found by A045: booleans
    // weren't searched).
    private static IEnumerable<string> Scalars(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Scalar(p.Value)),
        _ => Scalar(e),
    };

    private static IEnumerable<string> Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => [e.GetRawText()],
        _ => [],
    };
}

