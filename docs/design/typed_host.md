# Typed document host

Status: SC-S0a contract for the approved 0.2 route, based on main `1658914`.
This document changes no behavior or public API. Names in the interface sketch are proposed.
The implementation belongs to SC-S1a through SC-S1c, not to this contract task.

## Decision

**MIXED:** retain BED's generic command/session model and per-file editing history, while generalising the existing DataWorkspace host to dispatch typed files. This follows the per-document history and editor/runtime separation described in [the architecture research](../research/editor_architecture.md). It is not a second workspace or a second asset database.

A file has one authoritative `DocumentSession<TDoc>` in its open project. A data file uses `DataDocument`; a scene uses `Scene`. Outline, Properties, viewport, text tools, commands and save all refer to that same session. Do not project Scene into DataDocument, maintain a JSON shadow writer, or reopen a file privately for a panel.

The host owns admission and lifetime. The session owns validated edits, revision, dirty state, history and journal. A view owns presentation and selection, never a competing document. The runtime reader consumes an immutable saved/input snapshot; it does not acquire an editor session or reference UI assemblies.

## Existing seams and the missing connection

Source anchors below are relative to the repository at the stated base.

| Existing source | Fact and consequence |
| --- | --- |
| `src/Brutal.Authoring/DocumentSession.cs:27,54,106-116,163-172` | `DocumentSession<TDoc>` already supplies typed current state, revision, commands and batch edits. Reuse it; do not add another undo journal. |
| `src/Brutal.Authoring/DocumentSession.cs:250-301` | Save atomically writes and updates its disk baseline; `CheckDisk` and `Revert` are separate operations. Atomic write alone is **not** a conflict policy. Revert discards history only after a successful parse/validation. |
| `src/Brutal.Authoring/Scene.cs:54-88,366` | Scene is immutable; SceneFormat checks the configured kind and version; SceneKit registers typed commands. A custom scene kind is not necessarily the literal string `scene`. |
| `src/Brutal.Authoring.Editor/SceneEditing.cs:15-47` | Existing typed outline, object fields and command mapping can bind to an `EditorModel<Scene>`. |
| `src/Brutal.Authoring.Editor/EditorModel.cs:134-145,242-266,432-447` | The editor holds the supplied session, selection and Play lifecycle; commands and revision-aware batches already provide the mutation boundary. |
| `src/Brutal.Authoring.Data/DataProject.cs:500-565,591-665` | DataFile owns a `DocumentSession<DataDocument>`; Open hardcodes DataFormat. Save enforces read-only, check and external-change rules. Close directly drops its session, including unsaved edits. The host must guard destructive closure. |
| `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:36-38,62-95,205-253` | Current windows and configuration callbacks are data-specific. Show preserves the previous active file on refusal and checks that a cached window still holds the file's own session. Preserve these facts. |
| `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs:101-137,490-525,541-583` | Play stops before switching/replacing an owner. Pending saves check session identity **and** revision. File panels/configuration use the existing session, and their failures do not invalidate the file. |
| `samples/SceneTool/Program.cs:48-66`; `tests/Brutal.Authoring.Tests/SceneToolTests.cs:133` | A scene consumer and explicit outside-edit conflict test exist. Their existence is not evidence that the workspace already hosts scenes. |

**Important:** wrapping DataWorkspace alone is insufficient. Direct DataFile/agent opens would otherwise parse the same scene as generic JSON and create a second session. Typed admission must cover those entry points too.

## Proposed interface boundary

**INVENTED:** a small type-erased lifetime surface with typed access behind it. This is an interface sketch, not copy-ready declarations or an approved persisted schema.

```csharp
// Proposed in Brutal.Authoring.Editor; no ImGui/runtime dependency.
interface IDocumentHostEntry
{
    DocumentKey Key { get; }          // project identity + admitted file identity
    Type DocumentType { get; }       // DataDocument or Scene
    long Generation { get; }         // changes when this entry is replaced/closed
    long Revision { get; }
    bool Dirty { get; }
    DiskState CheckDisk();
}

interface ITypedDocumentHostEntry<TDoc> : IDocumentHostEntry where TDoc : class
{
    DocumentSession<TDoc> Session { get; }
    EditorModel<TDoc> Editor { get; }
}

// Proposed owner operations. Results carry issues/reasons, not just a null cast.
OpenResult Open(DocumentKey key, DocumentBinding binding);
LookupResult<TDoc> TryGet<TDoc>(DocumentKey key) where TDoc : class;
TransitionResult Switch(DocumentKey key);
SaveResult Save(DocumentKey key, long generation, long revision);
CloseResult Close(DocumentKey key, CloseDecision decision);
ReloadResult Reload(DocumentKey key, ReloadDecision decision);
```

`DocumentKey`, binding, results and decision types are proposed parts of the same contract. Do not introduce them independently in different projects. No `object Current` setter, raw write callback, universal cast or untyped command execution belongs on this surface. Typed callers execute through the existing editor/session. The normal session API remains available to trusted in-process code; this contract is not a sandbox against malicious plug-ins.

An admitted binding provides the expected CLR type, format/kind/version, validator, command catalog and typed editor factory. Scene bindings use SceneFormat/SceneKit/SceneEditing. Data bindings wrap the **existing** DataFile session and keep its schema, repair, reference-map, save-check and read-only policy. Bindings are supplied through host configuration before opening files; this card does not invent a project-file field, extension convention or format migration.

The registry returns the existing entry on a second compatible open. A second incompatible type/binding is refused; it cannot replace or reinterpret the first session. A disposed generation is never reused, even if a newly opened session starts again at revision zero. All admission and view attachment occur on the editor owner thread. No background parser may publish an entry after its request has become stale.

### File identity and routing

Resolve through the project's admitted file list and existing path restrictions before reserving a key. Map requests back to the canonical admitted file identity; equivalent path spellings must not create multiple writers. Preserve existing case/path behavior for data projects. Do not silently add symbolic-link or hard-link traversal; aliases outside the existing admission contract are refused until a dedicated identity rule is implemented and tested.

Routing is explicit, not trial-and-error parsing. The registered binding selects the type; its format verifies the actual contents. A JSON scene is not offered to the data route when Scene parsing fails. Unknown/ambiguous bindings, a wrong configured kind or unsupported version refuse admission with the original format path and reason. Generic data files with their own `kind` property remain data unless an explicit scene binding claims them.

Reserve the key before creation. Publish one entry only after parse, validation and session creation succeed. Release a failed reservation; retain the previously active entry, its draft, selection and history. A scene-bound file visible through the legacy DataProject surface must refuse `DataFile.Open/Repair` with a typed-route reason rather than create a data session. Data-only CLI/agent commands likewise refuse the wrong type; adding scene automation is separate work.

## Lifecycle and refusal matrix

Every refusal preserves document, dirty state, undo/redo, journal, active selection and disk bytes unless the row explicitly describes a successful transition. Reasons name the file and the next action. Keep existing data diagnostic text unchanged; the following scene messages are suggested wording, not implemented diagnostics.

| Operation | Required behavior | Refusal or preservation proof |
| --- | --- | --- |
| Open valid file | Create or return the single compatible entry. Attach typed views to that session. | Repeated data/scene opens and differently spelled admitted paths return the same authority; no duplicate history. |
| Open missing, malformed, invalid, wrong kind/version or ambiguous route | Do not publish a partial entry or switch away. | Preserve original parser/validator issues; no fallback to DataDocument. Candidate data repair remains an explicit data-only action. Scene repair is not implicitly enabled. |
| Get wrong typed entry | Return a wrong-kind result. | `TryGet<DataDocument>` on a scene cannot manufacture a wrapper/document; no invalid cast escapes into UI. |
| Switch A to B | Validate/open B first; then stop A's Play and activate B. Keep A's dirty session/history/selection. | Dirty alone is not a reason to discard or prohibit ordinary file switching. Failed B leaves A active. Stop failures are reported using the existing stop/cleanup contract. |
| Save | Use the entry's policy, same generation and revision, validation/check result and last-loaded disk baseline. Write atomically; mark saved only after success. | Refuse read-only, closed/stale entry, active check, denied check, revision changed during check, missing/changed disk or I/O failure. Do not bypass DataFile policy by calling Session.Save directly. |
| Save as | Explicit destination decision and type-preserving serializer; acquire destination identity before writing. | Refuse collision with another open entry, read-only destination or unapproved overwrite. Do not create a second entry for the same target. Existing data behavior remains the compatibility baseline. |
| Close clean | Stop Play, cancel/invalidate pending work, detach views/subscriptions, then release the entry. | No disposed owner remains reachable through cached windows or pending completions. |
| Close dirty | Save / Discard / Cancel decision belongs to the existing host prompt flow. | Cancel retains everything. Save must succeed before close. Discard is explicit; low-level DataFile.Close is never treated as a prompt or safe discard decision. |
| External edit while open | Mark conflict and retain the in-memory draft. | No automatic overwrite, silent reload or type switch. Missing files are not silently recreated. |
| Reload | Explicit decision to discard local edits/history, then parse and validate with the same binding. | Failed reload preserves old session state. Valid reload advances revision, clears stale gesture/proposal references and prunes selection through existing model rules. Changed kind is a refusal, not a hot conversion. |
| Close project / shutdown | Resolve dirty decisions first; stop all Play and detach every entry before disposing project services. | Cancellation keeps project active. No view/async callback outlives its project generation. |

Save checks must capture both entry generation/session identity and revision. Recheck the disk baseline after a lengthy check, immediately before writing, as data saves already do. This does not claim an operating-system compare-and-swap guarantee against a writer racing the final check; do not advertise stronger atomic conflict protection than the existing primitives provide.

Preserve DataProject's existing data-only SaveAll transaction and diagnostics. For a mixed typed batch, refuse plainly until SC-S1c supplies a single preparation/commit path with rollback/partial-failure semantics proved. Never implement mixed SaveAll as a loop that reports success after silently saving only some files. Per-file scene save must work before mixed SaveAll is exposed.

## Compatibility and exact implementation ownership

This is a future work split, not permission to edit paths currently owned by a Stage 1 task. One host implementation owner serializes SC-S1a/b/c. Rebase after the active DataWorkspace and shell owners land.

| Path | Future owner and narrow responsibility |
| --- | --- |
| `docs/design/typed_host.md` | SC-S0a: this contract only. |
| New `src/Brutal.Authoring.Editor/DocumentHost.cs` | SC-S1a host owner: entry/key/binding/result types and single-session registry; no UI references. |
| New `src/Brutal.Authoring.Editor/SceneDocumentEntry.cs` | Same host owner: Scene session/model binding and policy; use SC-S0b reader decisions, no duplicate parser. |
| `src/Brutal.Authoring.Data/DataProject.cs` | Same host owner, explicitly coordinated before SC-S1a starts: narrow claimed-file admission guard and data-entry reuse. Preserve data validation, repair, save transaction and diagnostics. This additional shared path is necessary to prevent a second writer, not a general DataProject redesign. |
| `src/Brutal.Authoring.Editor.ImGui/DataWorkspace.cs` | SC-S1a/b/c serialized owner: registry-backed typed dispatch, cached typed views, shared current-entry/lifecycle routing. No simultaneous behavior writer. |
| `src/Brutal.Authoring.Editor.ImGui/EditorWindow.cs` | SC-S1b/c, coordinated with the shell owner: generic model attachment and typed save/close routes; preserve menu IDs, Ui wrappers and keyboard facts. |
| New `tests/Brutal.Authoring.Tests/SceneHostConnectionTests.cs` | Same host owner: admission, wrong-kind and authority identity tests. |
| New `tests/Brutal.Authoring.Tests/TypedHostLifecycleTests.cs` | SC-S1c: dirty/save/close/external-change and stale completion proofs. |
| `src/Brutal.Authoring/Scene.cs`, `DocumentSession.cs`, existing SceneEditing/Viewport | Reuse unchanged for S1a. Any required change returns to its owning contract first. Scene reader work belongs to SC-S0b; asset identities/resolution to SC-S0c/AS01; GPU ownership to SC-S5c. |

Retain the data constructor, DataFilePanel and ConfigureFile signatures. They continue to run only for DataDocument. Add a typed registration surface rather than passing Scene through those delegates. Legacy `Active`, `Window`, `TableOf`, data checks/proposals and reference-map calls are data-only compatibility views: on an active scene they return their existing nullable absence/refusal form, never the last data file. A new current-entry surface drives shared shell actions for either kind. Any caller assuming non-null data state must be audited in SC-S1a before scene activation.

The scene's selection is shared by its outline/Properties/viewport through one EditorModel. Data tables and data text panels do not attach to it. A future scene text editor would have to parse/validate a candidate and commit through that same typed session, not save independently.

## Required implementation exit

Use synthetic public fixtures only. These are future tests, not results from this document task:

1. Open one data file and one scene; repeat both opens; assert session reference identity and independent histories. Wrong-type lookups and legacy data opens of the scene refuse without a second session.
2. Edit A, switch to B and back; assert A's exact document/history/journal/selection remain. Malformed B never replaces A. Panels receive the exact authoritative session.
3. Refuse wrong scene kind/version, unknown binding and stale generation; retain original issues and no disk writes.
4. Save both types through their policy. Inject external edit/deletion and a session/revision change during a pending check; assert refusal, unchanged outside bytes and retained draft. Preserve the existing outside-edit SceneTool proof.
5. Exercise Save / Discard / Cancel and save failure on close. Stop Play and dispose view resources exactly once; late callbacks cannot mutate or save a replacement entry.
6. Reload success and failure: success preserves type and intentionally resets history; failure preserves the entire old draft. No implicit repair or type conversion.
7. Run existing data workspace, save-check, proposal, scene editing and viewport scopes unchanged, then the owner's merge regression suite. No assertions or baseline replacement to accommodate the new route.

The SC-S0a exit is this decision/compatibility matrix and named authority/ownership. No host implementation, runtime test, new dependency, schema, save hook or behavior change is included.
