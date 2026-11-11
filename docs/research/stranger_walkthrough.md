# Stranger walkthrough: Editing your game's data

A049 revision 1, 2026-11-01 UTC. Fresh-context agent simulation, not a human usability study. Executable validation examples worked. Guide-only progress stopped at SDK hosting. During the original pass, a dirty session saved after deletion recreated its file, contrary to the guide's refusal promise. The owner subsequently fixed it on main; one fresh post-fix retest independently confirms refusal and retained dirty edits. The historical evidence below is preserved, with fixed disposition at W26/S08.

## Walkthrough

### Method and evidence

Sole initial instructional input: docs/guide/editing_data.md, 914 lines, captured before execution in [guide.snapshot.md](../../.gate/guide.snapshot.md). Workflow rules/cards were separate. No prior chats, product source or tests supplied instructions. First product read followed recorded S02. Source-assisted actions are labelled. Existing C#/PowerShell/SDK knowledge limits this simulation.

Provenance: fixtures BORROWED from the guide's explicitly INVENTED catalog/Lantern Garden. Observations/fixes INVENTED from this pass. No game/licensed content, external research, new dependencies or production changes. [source-input-hashes.json](../../.gate/source-input-hashes.json) records guide/later source hashes and times; later hashes cannot establish earlier versions.

[harness.ps1](../../.gate/harness.ps1) isolates process-only temporary files, CLI home, build/artifacts/restore/cache inside A049 .gate; existing user packages are read fallback. Compiler/MSBuild servers, telemetry/certificates disabled; inherited screenshot/performance/test-filter settings removed. These are task adaptations, not guide faults. This card and the user's PC rules prohibit graphical launches; the card mandates headless-host and authoring CLI substitutions. No real graphical-host usability was assessed. No GUI/game/full-repository test gate launched. $gate below is its absolute scratch binding; workdir is authoring repository root.

Guide scripts used Windows PowerShell 5.1 with BOM-bearing wrappers preserving Café accents. Initial PowerShell 7.6.5 run was repeated in 5.1; that repeat is the guide-shell result. Separate processes rebind original catalog $example; $garden names second fresh directory. Every guide section follows in order. W01-W26 are audit units, not proof of every prose assertion. Scripts retain exact fixture/C# operations; combined timings identified, no invented substep timing.

### Start with two files - lines 7-137

**W01.** Exact: `dotnet build samples/DataCheck/DataCheck.csproj --nologo -v:q`. [build.log](../../.gate/build.log): Build succeeded, 0 Warning(s), 0 Error(s), exit 0; about 7.75 s. Shell/root/SDK prerequisites clear.

**W02.** Complete lines 24-127 block unchanged after harness setup: [02-start-ps51.ps1](../../.gate/02-start-ps51.ps1). Ending command:

```powershell
$check = "samples/DataCheck/DataCheck.csproj"
dotnet run --no-build --project $check -- "$example/data" "$example/schemas"
$LASTEXITCODE
```

[02-start-ps51.log](../../.gate/02-start-ps51.log): `1 file(s) checked, 0 with problems.`, `0`; about 0.88 s. Four records/Café created UTF-8/LF/final newline; no download. Initial PS7 output retained separately in 02-start.log.

### Open a folder as a project - lines 139-170

**W03.** S01 recorded 2026-11-01T05:50:50.5490958Z before product reads: lines 147-160 require referencing project without setup commands. Guessed SDK scaffold:

```powershell
$probe=Join-Path $gate 'sdk-probe'
dotnet new console --output $probe --no-restore --framework net10.0
dotnet add "$probe/sdk-probe.csproj" reference src/Bed.Data/Bed.Data.csproj src/Bed.Editor.ImGui/Bed.Editor.ImGui.csproj
dotnet run --project "$probe/sdk-probe.csproj" -- "$gate/tmp-ps51/authoring-guide"
```

[03-sdk-project.ps1](../../.gate/03-sdk-project.ps1) writes guide C# 150-157 unchanged. [03-sdk-project.log](../../.gate/03-sdk-project.log): Opened catalog.json; 1 files listed., exit 0; about 15.23 s including setup. Snippet itself worked. Refusal/read-only prose cases relevant to saving tested later; every malformed-folder/schema case not generated.

### Find and edit an item - lines 172-192

**W04.** The constrained walkthrough requires an offscreen route, while the SDK guide assumes a separately configured host. Opening snippet exits without UI; lines 160/176 assume configured host. No HeadlessHost/draw/input/authoring CLI recipe. S02 recorded 2026-11-01T05:51:41.3032823Z, then first product reads: HeadlessHost/DataWorkspace and relevant EditorWindow/EditorModel/DataTableView/inspector entry points. No tests read. Card-authorized source-assisted host in [sdk-probe/Program.cs](../../.gate/sdk-probe/Program.cs): HeadlessHost(1800,1000), Configure(ImGui.GetIO()), workspace.Draw frames/events. Exact invocation in [05-catalog-headless.ps1](../../.gate/05-catalog-headless.ps1):

```powershell
dotnet run --project "$gate/sdk-probe/sdk-probe.csproj" -- "$gate/tmp-ps51/authoring-guide" "$gate/headless-catalog"
```

First inspector/render run about 19.25 s. [05f-catalog-headless.log](../../.gate/05f-catalog-headless.log), [01-inspector.png](../../.gate/headless-catalog/01-inspector.png): Reading lamp/spare/Backup/#3. PNGs rendered offscreen and inspected as files. Later retries distinguished from first-pass discovery.

**W05.** API substitution `editor.Select("/items/0")` true; Inspector.Rows gave text/choice/integer/number/toggle/note with literal Café accent. Quantity help `Use a whole number of at least one.` Active=false/Overridden=true/HasDefault=true. SDK/render check, not keyboard discovery; shares above run.

**W06.** API substitution `table.Type.EditCommands(current,["/items/0"],"active",null,true)` and ExecuteBatch made active true; Undo restored false. W06-reset [] is unused reflection probe, not reset result; W06-reset-execute true is action. Nested/null/union widget limits read, not forms built.

### Keep edits on the checked path - lines 194-198

**W07.** API substitution SetCell("/items/0","quantity",0L) false, `Reading lamp, quantity: must be at least 1`; quantity 2/journal count 2 unchanged. Insert/remove/move/replay paragraph supplies no executable exercise for each; not independently retested as replacement for guide.

### Work in the table by keyboard - lines 200-230

**W08.** Real events Ctrl+Shift+P, type Table, Enter opened palette then hid already-visible Table: W08-table-open false. [02-palette.png](../../.gate/headless-catalog/02-palette.png) says Hide Table. Reopened after Ctrl+A cleared retained query. Minor state trap, not broken palette. Earlier retry appended query, a task mistake retained in 05b.

**W09.** Coordinates from 1800x1000 frame. Click(748,55), type Reading: Filter=Reading, only /items/0, Dirty=false. Ctrl+A/Backspace cleared. Click quantity header(1180,101), Enter: Sort=(quantity,true); Enter reverses false, smaller values first. Extra click affects initial direction: reversal confirmed, no claim first keyboard sort starts descending. Source order/dirty unchanged. Corrected whole driver including later prerequisite retry about 10.88 s; individual actions untimed.

**W10.** Real click quantity(1180,120), Ctrl+A/type 3/Enter: value 3, Anchor=(/items/0,quantity). Later type 7/Escape kept 3. Escape/arrows/Tab/Shift+Tab left Anchor there; Shift+Down RangeEnd=null. Ctrl+C initially failed block copy. Click/Space and choice Enter/arrows/Enter left original values. Explicit Ctrl+Tab prerequisite retry then up to 50 Tabs: W10-prerequisite-picked null. [05-prerequisite-retry.png](../../.gate/headless-catalog/05-prerequisite-retry.png).

These are adapter-limited/unverified routes, not product keyboard bugs. Picked cell is not proof of current navigation focus; driver did not establish prerequisite. API-only actions below are not keyboard successes. Text-selection copy, boolean/choice cancellation, block extension, clipboard Copy/Paste and keyboard Follow/Back unverified.

First unsuccessful copy encountered inherited clipboard text. Its persisted log occurrence became [REDACTED: inherited clipboard; not a task fixture]. Known-token UTF-8/UTF-16 byte scan of all 469 A049 files found zero occurrences; no further clipboard read. Subsequent driver removes Get/Set/Ctrl+C; uses paste-plan substitution. [content-hygiene.json](../../.gate/content-hygiene.json) has metadata only. Screenshots show task fixtures. Incident is not guide/tool defect; OS-clipboard UI route unverified.

**W11.** API fill: Filter=Reading; PlanFill("quantity","4") planned 1 command without changing 2. Apply set first 4, hidden second 1 unchanged; one Undo restored. Palette Fill/Tab-to-Apply/Cancel not completed through UI; dropped plan not claimed UI Cancel.

**W12.** API PlanPaste("/items/0","quantity","3\n4") planned 2 without mutation; Apply true, one Undo restored. Ragged3-tab-1/newline4 refused paste a rectangle; five rows refused only4 remain; bad refused isn't a whole number. Zero plan structurally OK, Apply refused minimum1 without changes. Guide does not promise schema commit during preview. Copy without Anchor null. Decimal/boolean paste, focus/play refusal, text-selection copy and UI Apply/Cancel not fully exercised; prose refusal list not a generated acceptance matrix.

### Link your data - lines 232-802

**W13.** Saved guide lines 241-287/293-341/347-437/443-562/572-761 exactly under fresh $garden; map beside data/schemas. Exact writes [06-garden-fixtures.ps1](../../.gate/06-garden-fixtures.ps1). `dotnet run --no-build --project samples/DataCheck/DataCheck.csproj -- "$garden/data" "$garden/schemas"`: [06-garden-fixtures.log](../../.gate/06-garden-fixtures.log), `2 file(s) checked, 0 with problems.`, exit 0, about 0.78 s. Intentional duplicate/dangling strings schema-valid. Naming $garden and retaining $example was explicit guess for later catalog commands.

**W14.** Guessed console scaffold in map-probe; guide C# 767-775 unchanged. `dotnet run --project "$gate/map-probe/map-probe.csproj" -- "$gate/lantern-garden"`: [07b-map-snippet.log](../../.gate/07b-map-snippet.log), Opened gardeners.json; 2 files listed., exit 0. Earlier07 parse crash was task extraction off-by-one, opening brace omitted; bad input map-extraction-error.json.txt retained. Correct transcription made unchanged guide snippet succeed; not guide discrepancy. Later complete map run about 7 s including build/frame.

**W15.** Source-assisted labels: S04 recorded before Resolve public-signature lookup because opening snippet prints count only. map.Label/workspace outline: Mira x3, Sable x, Label=null/outline unnamed. [08b-map-outcomes.log](../../.gate/08b-map-outcomes.log); exact driver [map-probe/Program.cs](../../.gate/map-probe/Program.cs).

**W16.** Source-assisted `map.Resolve(project,"gardeners.json",pointer,doc.At(pointer)!.Value)` matches all seven guide outcomes:

| Field | Observed |
|---|---|
| seed | Found beds.json /seeds/2 |
| missing | Missing; nothing has id unlisted |
| duplicate | Ambiguous; 2 items have id fern |
| none | Sentinel |
| unloaded | NotLoaded; absent.json isn't open in project |
| scope_only | NeedsScope; context supplied only in words |
| plain | NotAReference |

**W17.** Source-assisted parts/narrow: pair both /beds/0 and /beds/1, main Target /beds/1; tagged aster; plot /beds/1/plots/0; home terrace /beds/0/plots/0; team /teams/tenders; mood /moods/0. Input 0 resolved, input1 Missing. Sable seed Ambiguous; missing bed/second pair part/wrong count specific Missing notes including part2 and should have2 parts separated by/. API results separate from outline navigation.

**W18.** Source-assisted Follow then Back: local tool and cross-file seed/pair/tagged/plot. Successful Back returns gardeners.json /gardeners/0. Plot selects/beds/1, `Went to Alcove in beds.json; the link points inside it, at /plots/0.` Duplicate Follow=false, `duplicate doesn't name an item here.` Documented dead-end without detailed Note. No document edits by navigation; keyboard Follow/Back unverified.

Card-authorized CLI absent from guide. `dotnet run --project src/Bed.Agent.Cli -- --help` printed usage exit 2 ([04-authoring-help.log](../../.gate/04-authoring-help.log)). Fallback exact script [10-agent-fallback.ps1](../../.gate/10-agent-fallback.ps1):

```powershell
$base=@('run','--no-build','--project','src/Bed.Agent.Cli','--','--data',"$gate/lantern-garden/data",'--schemas',"$gate/lantern-garden/schemas",'--map',"$gate/lantern-garden/reference-map.json")
& dotnet @base describe
& dotnet @base get gardeners.json /gardeners/0/duplicate
& dotnet @base get gardeners.json /gardeners/0/seed
```

[10-agent-fallback.log](../../.gate/10-agent-fallback.log): all exit 0; describe2 files / next calls, duplicate Ambiguous/two fern matches, seed Found /seeds/2. Calls took a few seconds. Earlier loop wrapper had logging-pipeline syntax error after successful calls; corrected transcript is evidence. Project came from card, not guide discovery.

### Check a broken file - lines 804-843

**W19.** Exact block 811-817 directory creation/literal replacement, preserving catalog $example. Full block [09-check-broken.ps1](../../.gate/09-check-broken.ps1):

```powershell
dotnet run --no-build --project $check -- "$example/broken" "$example/schemas"
$LASTEXITCODE
```

[09-check-broken.log](../../.gate/09-check-broken.log): `catalog.json $.items[0].quantity: must be at least 1`, `1 file(s) checked, 1 with problems.`, `1`; about 0.78 s. Clear place/fix.

**W20.** Exact `dotnet run --no-build --project $check`, then LASTEXITCODE: `usage: datacheck <data-folder> <schema-folder>`, `2`; about 0.73 s. Invocation versus content failure clear.

### Know what the validator checks - lines 845-878

**W21.** Exact block 863-869 creates {} and guide format:uuid schema, then:

```powershell
dotnet run --no-build --project $check -- "$example/unsupported-data" "$example/unsupported-schemas"
$LASTEXITCODE
```

Same retained script/log: `sample.json $: the schema uses 'format', which this validator doesn't support, so this value wasn't fully checked`, `1 file(s) checked, 1 with problems.`, `1`; about 0.75 s. Other keyword/annotation lists read, not independently proved by one example.

### Four known suite limitations - lines 880-891

**W22.** Reading only, no executable step. Counted four cases: three non-BMP regex, one nested $id/$ref. Did not open test link/run suite: not needed to explain stumble, no command supplied. Reading under a minute, not independently timed. Guide limitations, not four reproduced failures or conformance claim.

### When saving is refused - lines 893-906

**W23.** S06 recorded before public DataProject Save/Close read. Fresh invented catalog/schema copies; [save-probe/Program.cs](../../.gate/save-probe/Program.cs) API tests separate writable/project/per-file-read-only projects. Final exact invocation:

```powershell
dotnet run --project "$gate/save-probe/save-probe.csproj" -- "$gate/save-case-refresh-fixed" "$gate/tmp-ps51/authoring-guide"
```

[11d-save-cases.log](../../.gate/11d-save-cases.log): unopened save=false, catalog.json isn't open; no-op save=true/sameBytes=true/sameTimestamp=true; read-only saves=false/dirty retained. SaveAs/mixed-success SaveAll not separately exercised; rules read. W23-W26 together roughly 5-6 s including build, no individual timings.

**W24.** API set memory quantity 3, disk title Outside edit. Save=false, Dirty=true, outsideIntact=true: `catalog.json was changed outside the editor; reload it or keep your version before saving`. Preserved draft in preserved-draft.json, then project.Refresh retained session/quantity 3. Only after preservation, deliberate Close/Open loaded Outside edit. Bare DataFile discard example, not automatic conflict remedy or cached-workspace refresh.

Initial W24-refresh label omitted actual Refresh call. Standing audit caught/fixed task driver; only11d proves actual Refresh. Earlier source retained save-probe-before-refresh.cs.txt, insufficient for this assertion.

In the original pre-fix run, deleted reopened unchanged file then Save=true/empty Problems/State Missing, contrary line 901. See the later fixed disposition in W26. S08 recorded 2026-11-01T05:58:46.7761271Z before CheckDisk implementation read. Real disagreement, distinct from task errors.

### Save without reformatting the whole file - lines 908-914

**W25.** Source-assisted fresh writable `Session.Execute(new SetValue("/items/0/quantity",JsonSerializer.SerializeToElement(3))); DataFile.Save()`. Save=true, only line 4 quantity 2-to 3 changed. Café accent/literal1.0/LF/final newline preserved. [before.json](../../.gate/save-case-refresh-fixed/before.json), [after.json](../../.gate/save-case-refresh-fixed/after.json). Structural/CRLF/whole-container claims not independently tested; one-line promise appropriately narrowed.

**W26.** Historical, pre-fix mandatory fresh deletion repro: independent clean/dirty sessions, each underlying file deleted before Save. Both true; clean absent, dirty recreated/cleared dirty. In11c, seeded replay and11d:

```text
W26-delete-clean-repro {"caseDirty":false,"ok":true,"Problems":[],"state":"Missing","exists":false,"Dirty":false}
W26-delete-clean-repro {"caseDirty":true,"ok":true,"Problems":[],"state":"Open","exists":true,"Dirty":false}
```

Post-observation explanation of the initial version: CheckDisk returned Missing; DataFile.Save rejected ChangedOnDisk only, then no-oped/wrote. Original finding appended authoring mailbox. This worker left guide/product untouched.

**Owner fix and independent follow-up, 2026-11-01 UTC.** Latest inbox note says saving a deleted file is now refused with edits kept. A premature stale report was preserved byte-for-byte as a superseded draft under .gate/history and removed from the readiness path; a correction-in-progress mailbox note marked that handoff not ready for review. That sequencing mistake belongs to this task, not the guide.

Ran exactly one invocation of the unchanged existing save-probe driver on current main with a new fixture root:

```powershell
dotnet run --project "$gate/save-probe/save-probe.csproj" -- "$gate/save-case-owner-fix-20261101" "$gate/tmp-ps51/authoring-guide"
```

[14-owner-fix-save-retest.log](../../.gate/14-owner-fix-save-retest.log), exit 0, roughly 10 seconds including rebuild: clean and dirty deletion both returned ok=false, state=Missing, exists=false; dirty remained Dirty=true. Exact reason: `catalog.json was deleted outside the editor; nothing was saved. Use Save as to keep your version, or close it to let it go`. No source/probe change by this worker and no whole-walkthrough rerun. [postfix-source-before.json](../../.gate/postfix-source-before.json) and [postfix-source-after.json](../../.gate/postfix-source-after.json) fingerprint current DataProject/DocumentSession and the unchanged task driver before/after the run; the fingerprints are stable across it. The old source-input-hashes and old logs remain historical inputs, not current defects. **Disposition: fixed by owner, independently retested.**

## Stumbles

Fix ideas INVENTED. Severity for this simulation: blocks means missing route setup or failed stated save guard; slows means workaround; cosmetic minor cue. Isolation/redaction/task mistakes excluded as product defects.

| ID/where | Kind | Severity | Observation/boundary | Smallest fix |
|---|---|---|---|---|
| S01 Open folder147-160 | stopped; guessed | blocks | No project/reference/run setup; guessed scaffold worked. | guide-fix: exact console/reference/run commands. |
| S02 host160,172-198 | stopped; read code | blocks | SDK host integration is assumed, not supplied. The card-mandated headless route is also absent; this blocks this constrained pass, not proof every reader needs a headless product. | guide-fix: link a runnable SDK host starter; provide a nonwindow variant for this constrained route. |
| S03 Table202 | guessed | cosmetic | Already shown; Hide Table hid it; fresh replay agrees. | guide-fix: conditional Show/Hide instruction. |
| S04 Link764-800 | stopped; read code | slows | Count-only snippet, no Resolve call for outcome table. | guide-fix: runnable Label/Resolve loop. |
| S05 Follow800 | dead-end message | slows | Generic duplicate refusal loses two-fern Note; documented limitation. | tool-fix: preserve outcome/Note and concrete repair step. |
| S06 save893-914 | guessed; read code | slows | No executable preserve/save/reconcile exercise. | guide-fix: worked preserve/refuse/deliberate-reopen sequence. |
| S07 keyboard202-220 | stopped | slows | Adapter never established current navigation focus; no product bug proved. | guide-fix: observable focus/picked-cell checkpoint/input example. |
| S08 deletion901 | disagreement; read code | blocks historically; fixed | Historical dirty Save recreated deleted file; clean deleted Save succeeded. Owner fixed main; fresh independent retest now refuses both, keeps dirty edits/file absent. | tool-fix completed by owner: Missing refusal with actionable Save as/close message; see W26 follow-up. |
| S09 second root236,811-817 | guessed | slows | Chose $garden, retained $example; second root variable unspecified. | guide-fix: explicit catalog/garden assignments. |
| S10 CLI5,160,788-800 | stopped | slows | Card/runtime supplied CLI project/usage absent from guide. | guide-fix: build/help/describe/get separate from SDK hosting. |

S09/S10 retrospectively record choices already described, not newly timestamped pre-source discoveries; no additional source read needed. S07 adapter-limited. Task mistakes retained in logs/ledger: map brace extraction, query append, UTF-8 literal compiler error, logging pipeline syntax, anonymous dirty/Dirty JSON collision, tuple omission and missing Refresh. None counts as guide/tool defect. [stumbles-live.md](../../.gate/stumbles-live.md) preserves pre-read timestamps/corrections.

## What worked

- BORROWED guide examples observed: build/catalog/validation/usage/unsupported messages/exits matched; shell/encoding clear.
- INVENTED observation: opening snippets unchanged worked once scaffold supplied.
- INVENTED observation: labels/help/default/reset/undo and quantity refusal matched; rejected edit left journal intact.
- INVENTED observation: real filter/header/edit/cancel events worked; API batches kept hidden rows/gave useful shape/scalar errors; broader keyboard unverified.
- INVENTED observation: seven outcomes/parts/narrow/nearest-item navigation/Back matched; CLI next steps useful after discovery.
- INVENTED observation: read-only/outside edits preserved drafts; no-op preserved bytes/time; quantity save changed one line/Café/1.0/LF intact.
- INVENTED observation: shallow outline/schema/source-span limits plainly stated; no unrun cases claimed passed.

## Top 10

Ranked INVENTED fixes grounded in pass; no new interface/system/dependency. First is observed disagreement; lower ranks include onboarding improvements, not proven defects.

1. **tool-fix, now fixed: refuse deleted saves (S08/W26).** Historically this violated line 901. Owner implemented refusal; the independent fresh post-fix run rejects clean/dirty deletion, keeps the file absent and dirty edits intact, and gives Save as/close guidance. Retain this high-priority historical finding; no outstanding product fix is implied.
2. **guide-fix: runnable first project (S01/W03).** Console/references/Program.cs/run; snippet itself works.
3. **guide-fix: executable SDK host bridge (S02/W04).** Link a runnable host/Configure/draw recipe instead of assuming one exists. For this card-mandated constrained route, supply an offscreen variant. This does not establish a universal requirement for a headless product or assess graphical-host usability.
4. **guide-fix: introduce CLI (S10/W18).** Build/help/describe/find/get/preview examples expose useful fallback.
5. **tool-fix: retain resolver Note (S05/W16/W18).** Two-fern detail plus next step in existing refusal, no new panel.
6. **guide-fix: outside-edit exercise (S06/W24).** Preserve draft/refusal/Refresh-versus-reread/explicit reopen; prose warning correct.
7. **guide-fix: executable outcomes (S04/W15-17).** Resolve loop removes signature discovery.
8. **guide-fix: keyboard prerequisite checkpoint (S07/W10).** Active editor/picked cell/navigation focus; failed adapter is not product defect.
9. **guide-fix: name roots (S09/W13/W19).** Catalog/garden variables keep later copy unambiguous.
10. **guide-fix: conditional Table opening (S03/W08).** Continue if visible, otherwise Show Table; avoid Hide.

## Self-check

### Seeded six, clean folders

Executable population [W01..W21,W23..W26], W22 reading-only. Python random.Random(20261101).sample(population,6) drew W25,W03,W04,W13,W02,W21. W13 fixtures, W14 map snippet. [12-seeded-replay.ps1](../../.gate/12-seeded-replay.ps1) refuses preexisting seed-20261101 and creates independent fresh fixture folders. Caches reused; example text reconstructed from guide, not previous fixtures.

Exact replay: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $gate '12-seeded-replay.ps1')`. [12-seeded-replay.log](../../.gate/12-seeded-replay.log) contains real outputs. Six expected observations reproduced, not six guide-only/keyboard successes.

| Draw | Fresh result | Stumble/disposition |
|---|---|---|
| W25 | New W25-input/case: saved true, only line 4, cafe/numberSpelling true | S06 still needs disclosed API driver; preservation repeats. |
| W03 | New W03-project: Opened catalog.json;1 files listed after scaffold | S01 workaround from task, omission persists. |
| W04 | New W04-input: Reading lamp/spare/Backup/#3 | S02 needs task host; S03 table hide repeats; S07 prerequisite-picked null/inconclusive. |
| W13 | New W13-garden:2 files checked/0 problems/exit 0 | S09 separate variable explicit; validation repeats. |
| W02 | New W02-temp:1 file checked/0 problems/exit 0 | No associated stumble;5.1 block repeats. |
| W21 | New unsupported folders: schema uses format/exit 1 | No associated stumble; message repeats. |

Historical pre-owner-fix mandatory extra W26 in fresh W25-case repeats clean-deleted success and dirty resurrection (ok true/exists true/Dirty false). [13-targeted-map-replay.log](../../.gate/13-targeted-map-replay.log) uses fresh W13-garden: S04 needs source-assisted calls; S05 generic duplicate refusal repeats. Static omissions S01/02/04/06/09/10 re-read against same guide; successful workarounds do not supply absent instructions. S07 remains inconclusive, not reproduced product defect. Runtime stumbles missed by random six have targeted checks. These are historical first-pass/replay observations. After the owner-fix note, the additional W26 follow-up above used a new folder/current-source fingerprints and confirms the deletion issue is fixed; it does not overwrite the historical seed outputs.

### Standing twenty-item re-read

Seed 4901 from W01-W26 selected 20. W24 failed audit: label W24-refresh lacked Refresh call. Retained save-probe-before-refresh.cs.txt; fixed task driver, reran new save-case-refresh-fixed, redrew 20 with seed 4902. No product code/test changed; old W24 label is insufficient evidence.

Python random.Random(4902).sample([W01..W26],20) drew W03,W13,W21,W06,W08,W23,W14,W01,W18,W12,W17,W11,W22,W10,W16,W02,W24,W25,W26,W09. Re-read each against card: guide order/line, operation, real outcome, scope/provenance/stumble. Final 20/20 reporting items passed; unverified UI stays unverified.

| Item | Short literal finding and evidence locator | Result |
|---|---|---|
| W03 | `Opened catalog.json; 1 files listed.` — 03-sdk-project.log | PASS; scaffold remains guessed |
| W13 | `2 file(s) checked, 0 with problems.` — 06-garden-fixtures.log | PASS |
| W21 | `the schema uses 'format'` — 09-check-broken.log, W21 | PASS |
| W06 | `W06-active true`; `W06-active-after-undo false` — 05f-catalog-headless.log | PASS |
| W08 | `W08-table-open false`; `W08-reopened true` — 05f-catalog-headless.log | PASS |
| W23 | `W23-noop {"saved":true,"sameBytes":true,"sameTimestamp":true}` — 11d-save-cases.log | PASS |
| W14 | `Opened gardeners.json; 2 files listed.` — 07b-map-snippet.log | PASS; earlier brace error belongs to task |
| W01 | `0 Warning(s)`; `0 Error(s)` — build.log | PASS |
| W18 | `Went to Alcove in beds.json; the link points inside it, at /plots/0.` — 08b-map-outcomes.log, W18-follow/plot | PASS |
| W12 | `the pasted rows have different numbers of cells; paste a rectangle` — 05f-catalog-headless.log, W12-refusal | PASS |
| W17 | `part 2 of` — 08b-map-outcomes.log, W17-resolve/pair | PASS; full note names missing second part |
| W11 | `W11-values {"first":4,"hidden":1,"Said":"Set 1 cell as one step."}` — 05f-catalog-headless.log | PASS; API-only |
| W22 | `A quantified emoji pattern uses .NET UTF-16 code units rather than whole Unicode characters.` — guide.snapshot.md:886 | PASS as disclosed reading; suite unrun |
| W10 | `W10-prerequisite-picked null` — 05f-catalog-headless.log | PASS as unverified route, no keyboard success inferred |
| W16 | `"Outcome":"NeedsScope"` — 08b-map-outcomes.log, W16-resolve/scope_only | PASS; all seven checked in adjacent records |
| W02 | `1 file(s) checked, 0 with problems.` — 02-start-ps51.log | PASS; exit 0 in same log |
| W24 | `W24-refresh {"sameSession":true,"quantity":3}` — 11d-save-cases.log; corrected driver calls project.Refresh() | PASS after fix/rerun |
| W25 | `"numberSpelling":true,"finalNewline":true,"crCount":0` — 11d-save-cases.log, W25-preservation | PASS; only line 4 changed |
| W26 | `{"caseDirty":true,"ok":true,"Problems":[],"state":"Open","exists":true,"Dirty":false}` — 11d-save-cases.log, W26-delete-clean-repro | PASS; historical disagreement retained; fixed in later W26 follow-up |
| W09 | `"Filter":"Reading"`; `"Dirty":false` — 05f-catalog-headless.log, W09-filter | PASS; reversal also checked |

Additional post-fix self-check: parsed both W26 records in 14-owner-fix-save-retest.log; both ok=false/exists=false, dirty-case Dirty=true, actionable reason present. Before/after source/probe fingerprints match. Owner fix independently retested; the 20 literal historical excerpts remain unchanged and valid.

Structural gate checks headings/mentions only, not usability/keyboard/save safety. Failures/corrections retained in evidence. No worker-authored guide/product fixes in this card.
