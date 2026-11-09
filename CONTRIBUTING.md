# Contributing

Build small changes around the existing public APIs. Start with [the goals](docs/goals.md): easy to use, powerful when needed, and expandable. [The architecture research](docs/research/editor_architecture.md) explains the choices. This guide describes development checks; it authorizes no release or game integration.

**BORROWED:** existing public contracts and repository policies. **MIXED:** the architecture research combines several editors. **INVENTED:** this guide's teaching order and private verification audit.

## Layout

| Path | Responsibility and boundary |
|---|---|
| src/Brutal.Authoring | Immutable documents, typed commands, validation, history, journals and safe saving. The core has no UI dependency. |
| src/Brutal.Authoring.Data | JSON documents, schemas, data projects, reference maps and frozen checks. Keep game rules out of this generic layer. |
| src/Brutal.Authoring.Editor | Editor models, commands, panels, selection and editing behavior shared by hosts. |
| src/Brutal.Authoring.Editor.ImGui and Editor.Text | UI views and headless test seams. Editor.Text wraps the separately noticed ImCode dependency. |
| src/Brutal.Authoring.Import | Import and diagnostics; fixtures describe expected refusals as well as successful imports. |
| src/Brutal.Authoring.Agent and Agent.Cli | Typed AI calls and the CLI/MCP stdio host. The CLI currently references Editor.ImGui for adapter loading; moving that contract below the editor is acknowledged debt, not a new runtime dependency policy. |
| src/Brutal.Authoring.Editor.App and Launcher | Desktop shells. Tests use headless routes; do not launch a desktop window as a test. |
| samples/ | Small neutral demonstrations such as Toy, Tiles and StarterAdapter, with original data. |
| tests/Brutal.Authoring.Tests | xUnit unit and acceptance tests. The solution currently has one test project. |
| tools/ and fixtures/ | Python checks, fixture generators and retained expected results. Source and runtime dependency notices stay with their respective inputs. |

Game types and private records belong in external game-side adapters. Never add a ship, quest or particular game's prop model to the public library. A host can reference editor code; a shipping game must not acquire editor/AI dependencies through it. The research's proposed runtime split is a design boundary, not a claim that another Runtime project already exists. [Extension points](docs/api/extension_points.md) gives compiled examples and current limitations.

## Building and checking

Use .NET 10 as selected by global.json, Python 3.12, and the existing pytest and openpyxl installations. Run these from the repository root in PowerShell. Restore uses configured NuGet sources; an offline run needs the pinned packages already in a cache/feed. Do not add or upgrade packages to get a check green.

```powershell
dotnet --version
python --version
dotnet restore Brutal.Authoring.slnx --nologo -v:minimal
dotnet build Brutal.Authoring.slnx --no-restore --nologo -v:minimal
dotnet test Brutal.Authoring.slnx --no-build --no-restore --nologo -v:minimal
python -m pytest -q tools
python tools/check_fixtures.py .
python tools/check_doc.py CONTRIBUTING.md --sections "## Layout,## Building and checking,## House rules,## Tests,## Review,## Sign-off (DCO),## Not decided yet"
```

## Sign-off (DCO)

Every commit in a public contribution carries a `Signed-off-by: Your Name <you@example.com>` line (`git commit -s` adds it). By signing off you certify the [Developer Certificate of Origin 1.1](https://developercertificate.org/): that you wrote the change, or otherwise have the right to submit it under the project's MIT licence. It is not a copyright assignment and there is no CLA. Use your real name or a consistent public identity. Sign-off applies to contributions from 2026-11-08 on; earlier history is not re-signed.

The whole `dotnet test` above takes about nine minutes. Run it once to see a fresh checkout is sound, and again only as the lead's one full run per merge batch (see Tests), not after each change. Build again after a code change before using --no-build. Read every exit and summary: a passing filtered run checks what you changed; the lead's batch run is what checks the rest. A skipped external-input test proves nothing about the absent game or published artifact; disclose its reason. Existing optional schema limitations remain visible in the suite. Python tests exercise the tooling; the fixture check verifies the import matrix and named generators; the document check verifies required sections. These checks do not certify a release, every platform or human usability.

No command above launches a window. For optional private integration/published-output checks, the owner supplies excluded input configuration and named authorization; do not put private paths/data in this guide. Package generation, dependency-record staging and publishing are separate maintainer work requiring the applicable approvals, outside this contributor walkthrough.

## House rules

Change documents only through typed commands and the validated session/editor boundary. A refused edit must leave current content, revision, journal and Undo intact. Keep deterministic serialization and stable command ids; history, replay and save/load/save must preserve the promised bytes. Opening, repair, Undo and Redo are explicit lifecycle operations.

Nothing unchecked should reach disk: use the project's save/file policy and existing writer rather than a side write around validation. Recheck read-only state, outside changes, session identity and draft revision at the relevant save boundary. Failed saves retain the old file and explain how to recover. Save all must judge the intended saved set, not unrelated unsaved siblings. An advisory check is not automatically a mandatory save policy; a game supplies that policy explicitly.

Checks inspect a frozen snapshot, honor cancellation and report incomplete input honestly. Completed is not synonymous with clean. A finding names an actual known file/item/field and a next action; leave an unknown place unknown rather than guessing a pointer that navigates somewhere else. Keep host hooks bounded and distinguish verdict completion from a lingering worker. No checker may mutate the live project.

Keep public APIs documented with XML comments: the src build enforces missing member documentation. Discuss interface/schema changes before implementing against them. Add no dependency without approval, exact version and licence evidence. Preserve the existing notices; an upstream dependency is not original fixture art.

Use provenance labels for designs, samples and fixtures: **BORROWED** names the source and applicable permission, **INVENTED** identifies original work, **MIXED** identifies both the sources and new part. Reusing an API contract is not permission to copy a game's data. Write original fixtures/prose/art; exclude private projects, workbook records and purchased assets. The separately pinned official JSON Schema suite is an existing MIT-noticed fixture exception, not a general exception for downloaded content.

## Documenting public contracts

Document what a caller needs to know at the declaration: accepted inputs, units and coordinate frames, null or empty meanings, ordering, refused cases, and whether an operation changes a document or writes a file. Read the implementation and its focused tests before promising a guarantee. Describe a result's limits as well as its successful case; a complete check is not automatically clean or current.

A type summary does not explain every positional record input. Add a `<param name="...">` for each such input on the record declaration, using the exact parameter name. Keep interface documentation or a valid `<inheritdoc />` for implementing members where the contract is shared. Use `<paramref>` and `<typeparamref>` for names in prose, and escape XML punctuation. Write for a developer using the public API, without private game examples.

The source projects generate XML documentation and treat warnings as errors. Missing public-member documentation is checked, but `Directory.Build.props` currently suppresses CS1573 (a partially documented parameter list). A passing build alone therefore does not establish complete parameter documentation. Do not change warning suppression or a signature merely to make a documentation patch fit. Review positional inputs and default/null behavior explicitly.

For a comment-only change, record every documented declaration and source reference, and verify that removing only the added XML comment lines restores the original C# text. Parse the added XML and check parameter names against the declaration. When the SDK is available, build the touched projects with XML documentation enabled and inspect their actual warnings. When it is unavailable, hand over that static list and say compilation, compiler-resolved references and generated XML output are unverified; do not label a static parse as a successful build. No broad runtime test campaign is needed for comments alone.

## Tests

Put a focused regression beside the relevant cases in tests/Brutal.Authoring.Tests; the SDK includes new C# files automatically. Use Fact for a case and Theory with explicit data for a matrix. Reproduce the bug, assert the promised outcome, and cover the meaningful refusal/recovery boundary. Do not assert only that no exception occurred when the behavior could still be wrong.

SessionTests illustrates validated edits, history and replay; inspect its actual assertions before adding another case. This exact command runs those existing examples after the build above:

```powershell
dotnet test tests/Brutal.Authoring.Tests/Brutal.Authoring.Tests.csproj --no-build --no-restore --nologo -v:minimal --blame-hang-timeout 10m --filter FullyQualifiedName~SessionTests
```

While you work, run only the tests for what you changed, with a filter like this one and a 10-minute hang timeout: a run far past its usual time is presumed hung. Don't run the whole suite to check a small change. The lead runs everything once per merge batch, on exactly what lands. Slow sets (big matrices, pixel comparisons, fuzzers) run only when their area changed. A job gets about one and a half times its coding time, tests included; past that, stop and say what's done and what isn't. Keep all existing assertions: never weaken, skip or delete a failing test to obtain green. Use temporary directories for settings/files and clean only the directory your test owns. Never use a person's saves or clipboard. A required unavailable input should say so and skip explicitly, never count as a passing workflow.

UI cases use HeadlessHost with real input and observable content/history outcomes, without a desktop window. Put tests using ImGui's global context in the existing ImGuiCollection serial collection. Inspect offscreen images when layout matters, and cover keyboard/focus/narrow panes/200% text when relevant. A screenshot or injected text does not establish screen-reader, IME or unaided first-run usability. Python tooling regressions belong beside their tools as test_*.py, with self-authored temporary inputs.

## Review

Describe the concrete problem, resulting behavior and smallest reproduction. Name the existing interface and goal/research decision being used. Include provenance, changed paths, exact commands, real summaries and all failures/skips. Separate observed results from source inferences and proposed future work. Provide a seeded spot-check of at least 20 output items, or all if fewer, with quotes and what each was checked against.

A reviewer checks scope, public/game separation, dependency/content provenance, safety on refusal and persistence, and the actual tests. They rerun the gate and independently sample claims. Resolve findings with a focused fix and repeat the affected checks; do not silently change the expected answer. Interfaces or schemas that need a different contract go back for a decision first. Game integration needs its owner's explicit named authorization. Passing development checks is not permission to publish.

A contribution should be reviewable without knowing any particular game that uses BED.

## Names

The editor is called **BED**. The code's namespaces, project files and solution still use the earlier working name, Brutal.Authoring. They will be renamed together in one change, so please don't rename them as part of a contribution. Nothing in this guide grants trademark permission or publishes a package.

The licence is **MIT**, chosen by the user on 2026-11-01 and recorded in LICENSE.txt and README.md. This is an existing decision, not one made by this guide. BRUTAL and other dependencies retain their own terms and notices. No contribution, green build or generated record grants approval to publish or settles every dependency obligation.
