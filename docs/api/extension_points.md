# Extension points: connect your own game

Source snapshot for A188 revision2, on the owner-named d4dface update; actual input bytes are pinned. These examples are **INVENTED**, self-authored demonstrations of **BORROWED** existing public contracts. Use .NET10, nullable references and implicit usings. Keep game rules in an authoring adapter assembly. The examples are complete separate class-library files; reference the existing projects named below. They describe the current API, not a compatibility promise or release package. No new dependency, game integration or licensing decision is part of this page.

Verification on the pinned source: **all6 extracted examples built independently, exit0, with0 warnings/0errors**. The receipts are in the A188 acceptance (`handoff/reviews/A188.md`); no drawing is needed. This does not certify every host behavior.

## Adapters

Use `IProjectAdapter` for project recognition, extra validators, checks and per-file editor setup. Definitions: `IProjectAdapter` `src/Brutal.Authoring.Editor.ImGui/ProjectLauncher.cs:568`; loader `Adapters` `src/Brutal.Authoring.Editor.ImGui/ProjectLauncher.cs:612`; selection `Adapter` `src/Brutal.Authoring.Editor.ImGui/ProjectLauncher.cs:241` and `NoAdapter` `src/Brutal.Authoring.Editor.ImGui/ProjectLauncher.cs:244`. A public concrete class needs a public parameterless constructor. Runtime discovery/deployment examples, not source-file anchors: the launcher loads explicit `--adapter` DLLs plus `*Adapter.dll` recursively under its `adapters/`; first recognising loaded adapter wins unless overridden or disabled. Name its assembly `DemoAdapter.dll` for automatic discovery, or pass the actual DLL path explicitly. Deploy its dependency metadata/libraries beside it; host authoring assemblies are shared. Recognition should be cheap and side-effect free. `Open` must return a project with all game validators/read-only/save/file policies; `Configure` adds views, while `Checks` supplies registered checks. Host reports adapter load/open/setup faults; loaded code is trusted in-process. Build against Editor.ImGui:

<!-- example:Adapters -->
```csharp
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using System.Text.Json;
public sealed class DemoAdapter : IProjectAdapter
{
    public string Name => "Demo";
    public bool Recognises(ProjectPaths p) => File.Exists(Path.Combine(p.Data, "items.json"));
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas,
        rules: name => name == "items.json" ? new NonNegative() : null);
    public IEnumerable<ICheck> Checks => [];
    public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    private sealed class NonNegative : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) =>
            d.At("/score") is { ValueKind: JsonValueKind.Number } score &&
            score.TryGetInt32(out var n) && n < 0
            ? [Issue.Error("Score must be zero or higher.", path: "/score")] : [];
    }
}
```

## Checks

`ICheck`/`CheckDefinition`: `src/Brutal.Authoring.Data/Checks.cs:43`/`:23`; snapshots `Capture` `:115`, registration/start `Add` `:538`/`Start` `:576`, run/state `CheckRun` `:279`/`CheckState` `:256`. Register stable unique ids, positive timeouts and a valid options schema; registration is all-or-none. Read `snapshot.Text/Bytes/Options`, honour cancellation, return located findings with a next step; return `Complete=false` if promised inspection was incomplete. Do not edit the live project. Options are objects, validated before work; this example defaults `required` to true. `Reads` lists outside paths for freshness, not frozen inputs.

Start pins the project's signature at once, then captures the inputs and runs the check on a worker, inside the check's time limit. States are Running, Cancelling, Completed, Failed, Cancelled, TimedOut. `await Ended` ends the verdict; a noncooperative `Worker` can linger. Late output is ignored and faults observed; `MaxLingering=2` is per check per registry (`MaxLingering` `:521`), then another start refuses. `Clean(project)` requires Completed, Complete, zero findings and current inputs (`Clean` `:407`); Completed alone is not success. Staleness uses draft revisions/disk stamps, and external Reads stamps; the maxAge overload deliberately caches, and once its answer is older than maxAge it re-reads the stamps on a worker, returning the last answer until the new one lands; an age of zero is exact (`IsStale` `src/Brutal.Authoring.Data/Checks.cs:359` / `IsStale` `src/Brutal.Authoring.Data/Checks.cs:374`). The current workspace starts with default options; required custom options need an API/AI host rather than a generated options form (`RunCheck` `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:379`). Build against Data:

<!-- example:Checks -->
```csharp
using Brutal.Authoring;
using Brutal.Authoring.Data;
public sealed class PresenceCheck : ICheck
{
    public CheckDefinition Definition => new("demo.presence", "Items file", "Checks only that items.json exists.",
        TimeSpan.FromSeconds(2), OptionsSchema:
        """{"type":"object","properties":{"required":{"type":"boolean"}},"additionalProperties":false}""",
        Reads: []);
    public Task<CheckReport> RunAsync(CheckSnapshot s, IProgress<CheckProgress> progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var required = !s.Options.TryGetProperty("required"u8, out var v) || v.GetBoolean();
        CheckFinding[] findings = required && s.Text("data/items.json") is null
            ? [new(Issue.Error("items.json is absent."), new("items.json"), "Restore items.json.")] : [];
        return Task.FromResult(new CheckReport(true, findings, "File presence checked."));
    }
    public static async Task<CheckRun> Start(DataProject p)
    {
        var checks = new Checks().Add(new PresenceCheck());
        var run = checks.Start("demo.presence", p, out var why, options: """{"required":true}""")
            ?? throw new InvalidOperationException(why);
        await run.Ended;
        return run; // inspect State, Report and Clean(p), not completion alone
    }
}
```

## Saving

`DataProject.SaveCheck`/`FileCheck`: `src/Brutal.Authoring.Data/DataProject.cs:106`/`:111`; background saves `BackgroundSaves` `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:438` and delivery `FinishSaves` `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:468`. Return null to allow, actionable text to refuse; exceptions refuse. These hooks also protect agents when their project carries the same policies. FileCheck covers create/rename/delete/restore, including previews. SaveCheck judges drafts being saved over the other files on disk; use `BeingSaved(file)` (`src/Brutal.Authoring.Data/DataProject.cs:234`) for Save all, not unrelated unsaved siblings. Callbacks must not write/reenter saves.

BackgroundSaves defaults false and affects window Save only, not Save all or AI saves. An initial false can mean pending: inspect `SavePending` and status. `Draw` delivers the finished result; a headless/non-drawing host must pump `FinishSaves`. Session identity/revision, read-only and outside-change guards still apply. SaveCheck has no cancellation/timeout contract and can be called on a worker; keep it bounded and arrange consistent inputs. Build against Editor.ImGui:

<!-- example:Saving -->
```csharp
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using System.Text.Json;
public static class SavingExample
{
    public static DataWorkspace Wire(DataProject p)
    {
        p.SaveCheck = file => file.Session?.Current.At("/reviewed") is { ValueKind: JsonValueKind.True }
            ? null : "Review this draft before saving.";
        p.FileCheck = change => change.Kind == FileChangeKind.Delete && change.Name == "items.json"
            ? "Keep items.json; other files need it." : null;
        return new DataWorkspace(p) { BackgroundSaves = true };
    }
    public static void TickWithoutDrawing(DataWorkspace workspace) => workspace.FinishSaves();
}
```

## Panels

`Panel`/`PanelCommand`: `src/Brutal.Authoring.Editor/EditorModel.cs:88`/`:110`; `DataFilePanel`: `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:17`, `FilePanels`: `:144`; `SetUpWorkspace`: `src/Brutal.Authoring.Editor.ImGui/ProjectLauncher.cs:254`. Install factories before the first file is shown: they run once per editor, before adapter Configure; existing editors retain their panels. SetUpWorkspace runs before the launcher shows a file. A throwing host setup is reported and the project stays open, though panels/settings from that setup may be missing. Keep hooks bounded and compose an existing hook rather than erase it.

The host owns ImGui context/frame and view state. Draw reads state; edits use `editor.Execute` and normal validated commands. Give stable unique panel/local-command ids, a truthful Unavailable reason and visible failure guidance. A panel command opens its panel before running and refuses edits during play. Setup/draw fault isolation does not make arbitrary extension code a sandbox. Build against Editor.ImGui (the example draws only when a real host calls it):

<!-- example:Panels -->
```csharp
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using System.Text.Json;
public static class PanelsExample
{
    public static void Wire(ProjectLauncher launcher)
    {
        var previous = launcher.SetUpWorkspace;
        launcher.SetUpWorkspace = w =>
        {
            previous?.Invoke(w);
            w.FilePanels.Add((file, editor) => new Panel("demo.score", "Score",
                () => ImGui.TextWrapped(editor.Session.LastIssues.Count > 0
                    ? string.Join("; ", editor.Session.LastIssues.Select(i => i.Message))
                    : "Use Reset score in the command palette."),
                Commands: [new PanelCommand("reset", "Reset score",
                    () => editor.Execute(new SetValue("/score", JsonSerializer.SerializeToElement(0))),
                    () => editor.Session.Current.At("/score") is null ? "This file has no score" : null)]));
        };
    }
}
```

## Play across files

A play mode (`PlayMode<DataDocument>`) gets only its own file's document. For a game started from several files, implement the overload `Configure(DataWorkspace workspace, DataFile file, EditorModel<DataDocument> editor, DataTableView table)` (`src/Brutal.Authoring.Editor.ImGui/ProjectLauncher.cs:603`); the general app calls it through `ConfigureFile` (`src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:85`), and by default it calls the two-argument `Configure`. Register the mode there and read the other files' current drafts from `workspace.Project` when Play starts; never read them from disk behind the editor, and write nothing. The old `configure:` constructor parameter of `DataWorkspace` still works for hosts that pass it; the launcher no longer does.

The workspace owns the game: `Playing` (`src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:91`) is the one editor running a game, and `EditorModel.PlayRefusal` (`src/Brutal.Authoring.Editor/EditorModel.cs:230`) refuses a second, naming the first. `StopPlay` (`src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:97`) stops it through `EditorModel.Stop` before another file is shown or repaired and before the playing file is renamed or deleted. Both are disposable: `Dispose` `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:120` stops the workspace's game, and launcher `Dispose` `src/Brutal.Authoring.Editor.ImGui/ProjectLauncher.cs:178` is for host shutdown; the launcher stops the game on a completed close and before opening another project over it, and a close waiting on unsaved edits leaves it running. A game whose `Dispose` throws is still stopped, and the failure is said with the action's own message. Make `Dispose` idempotent and nonthrowing.

## Reference maps

`ReferenceMap.Read/Resolve`: `src/Brutal.Authoring.Data/ReferenceMap.cs:103`/`:175`; full format: `docs/spec/schemas/reference_map.schema.json:1`. Describe links with file-local names, wildcard JSON Pointers, target list/key, labels and required evidence naming where that relationship is declared. Supply the map to DataWorkspace or AgentSession; it does not rewrite data/schemas or enforce game permissions. Read rejects invalid map JSON/shape. Resolution returns Found only for one owner; distinguish Missing, Ambiguous, NotLoaded, NeedsScope, Sentinel and NotAReference. No automatic cascade rename/delete is promised. Build against Data; this self-authored map follows `uses[*].of` to `items[*].id`:

<!-- example:References -->
```csharp
using Brutal.Authoring.Data;
using System.Text.Json;
public static class ReferencesExample
{
    public static ReferenceMap Create() => ReferenceMap.Read("""
        {"game":"Demo","labels":[{"file":"items.json","list":"/items","field":"name"}],
         "references":[{"file":"uses.json","at":"/uses/*/of","evidence":"docs/api/extension_points.md: Reference maps",
           "target":{"file":"items.json","list":"/items","key":"id"}}]}
        """);
    public static ReferenceResult Follow(DataProject p) => Create().Resolve(p,
        "uses.json", "/uses/0/of", JsonSerializer.SerializeToElement("alpha"));
}
```

## AI session

`AgentSession`/`Set`/`Save`: `src/Brutal.Authoring.Agent/AgentSession.cs:18`/`:321`/`:353`; `McpServer.Run/Handle`: `src/Brutal.Authoring.Agent/McpServer.cs:89`/`:149`. Supply the adapter-opened project and register its checks explicitly: this retains the same schema/game/save/file rules. Call Describe, preview, apply validated typed edits, then Save deliberately; ordinary edits are undoable and unsaved. File create/rename/delete/restore APIs operate on disk immediately unless previewed. Read-only matches a person: preview and draft edits remain possible, saving is refused (edit handling `Edit` `src/Brutal.Authoring.Agent/AgentSession.cs:371`); it is not an immutable-session flag.

MCP is JSON-RPC over newline-delimited stdio; tool argument schemas are checked. The shipped MCP server speaks over the stdio of a process the person's AI client starts: approval is that person configuring the client, with no separate sign-in in the server. A custom host owns its streams and serving decision; no UI callbacks/panels are made into AI tools automatically. The MCP tool catalog/dispatch are fixed; extra checks use the existing checks tools, while custom command tools need a host extension. The stock CLI's edit writes need `--save`; its process otherwise discards the draft. CLI `mcp` keeps one session for tool calls. `--adapter <dll>` now loads one game adapter, opens with its rules/policies and registers its checks for both ordinary calls and MCP. `--read-only` together with `--adapter` is explicitly refused until the adapter contract can carry read-only; plain-project read-only behavior above still applies. Build against Agent:

<!-- example:AI -->
```csharp
using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;
using System.Text.Json.Nodes;
public static class AiExample
{
    public static AgentSession Open(DataProject adapterOpenedProject, ReferenceMap? map = null,
        params ICheck[] checks)
    {
        var agent = new AgentSession(adapterOpenedProject, map);
        agent.Checks.Add(checks); // adapter checks are registered explicitly by the host
        return agent;
    }
    public static JsonObject Preview(AgentSession agent) =>
        agent.Set("items.json", "/score", "2", preview: true);
    public static void Serve(AgentSession agent, TextReader input, TextWriter output) =>
        new McpServer(agent).Run(input, output);
}
```

## Gaps

- **Read-only with an adapter:** CLI `--read-only --adapter <dll>` refuses plainly; IProjectAdapter.Open has no read-only argument. Adapter rules/checks now apply when `--adapter` is selected; do not silently substitute a plain project to obtain read-only.
- **MCP approval boundary, by design:** approval is the person configuring their own AI client to start the stdio server process. The server has no sign-in of its own; a custom host owns this boundary. This is not a live-client or model proof.
- **Version/lifecycle is thin:** IProjectAdapter has no API version handshake or unload/dispose contract. Match host binaries; this is separate from the editor plug-in manifest mechanism. Do not promise hot reload.
- **Freshness is limited:** Reads uses path/size/mtime, not frozen outside bytes; they are absent from snapshot InputHash (`ReadsSignature`, `src/Brutal.Authoring.Data/Checks.cs:302`; `InputHash` `:73-101`). A size/stamp-preserving outside rewrite can evade staleness. Freeze/identify extra inputs in your own check. A saved file that changes between Start and the capture makes the result stale, never wrongly current.
- **Layering:** the command line now references Editor.ImGui only for adapter loading/its contract; it does not draw a window. The adapter contract belongs below the editor, a later public-API change. Host setup is guarded now; keep host hooks bounded.

The original CLI and host-setup MISSED findings are fixed; the remaining source-backed limits are explicit. This page changes no source/test/API. Compilation checks source usage, not human usability, plug-in sandboxing, all renderer/transport behavior or future compatibility. Public release, licence and naming remain the user's decisions.
