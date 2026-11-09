# Your first settings change

Use the release zip containing the **FirstTask** sample. This walkthrough targets about ten minutes, not a measured completion time. In this source-prepared delivery the build, packaged walk and automated test are still **UNRUN**. A maintainer must rebuild and verify the zip before claiming these steps work in a distributed release.

## Start here

1. Unzip the release into a folder you can write to. Double-click **BED.exe**. No SDK, terminal or build is part of this task.
2. In the unpacked release, find **samples/first-task**. Copy that folder to a new working folder named **MyFirstTask**. It contains data/settings.json and schemas/settings.schema.json. The copy is yours to change; its full location is chosen by you, not a pre-existing path promised by this guide.
3. Keep the editor and its **adapters** folder together. The release includes the trusted, self-authored FirstTaskAdapter in adapters/first-task. If the sample or adapter is missing, stop and get the complete verified release rather than trying the task without its rules.

## The task

1. On the start screen, leave **Game adapter** on **Automatic** (not None). In **Data folder**, type the full path to your `MyFirstTask` folder. Clear **Schema folder** and **Link map** if either contains an unrelated previous path, then click **Open**. You can leave Schema folder empty for this paired-folder sample. The editor finds the two subfolders. Open `settings.json` in Files if it is not already shown.
2. Find the search box above the inspector, type **Practice**, and press Enter. Activate its first result (Tab, then Enter after searching, or click the result). The selected preset must be Practice, not Default. Its `rounds` value starts at **60**, and `reserve` is **10**.
3. In Practice's `rounds` field, select the current text, type **5**, and press Enter. This is a deliberate mistake. Expect a message containing **“rounds must be greater than reserve (10)”**, with advice to increase rounds or lower reserve. The refused input may remain in the field so you can correct it; it has not become the saved setting. If it is accepted, stop: the sample adapter/rule is not active or this walkthrough needs repair.
4. Replace that text with **80** and press Enter. Practice now has 80 rounds; Default must still have 60. Nothing is saved yet. We are changing settings, not running a game preview.
5. Press **Ctrl+Shift+P**, type **Check first task settings**, and activate **Run check: Check first task settings**. Wait for it to finish. Expect a complete, current report saying both presets were checked: identities, numeric limits and reserve below rounds. It should also say **Practice has 80 rounds**, proving which draft you checked, and that no simulation was run. If the command is absent, stop and check that FirstTaskAdapter loaded. “Links” alone is not this check. This manual check does not claim to cover arbitrary schema extensions or guarantee a game's behavior.
6. Press **Ctrl+S** to save. Practice's 80 is now written to `data/settings.json`. Saving doesn't run the check for you; that's what step 5 was for.
7. Press **Ctrl+Z** to undo. Practice goes back to 60 and the file is marked unsaved again; the file on disk still says 80. The check you ran in step 5 was for the 80-round version, so run it again if you keep this one.
8. Press **Ctrl+Y** to redo, then **Ctrl+S** to keep the 80-round version. Close and reopen the project if you want to see the saved value.

Default is intentionally fixed. To explore, edit Practice. The sample catches an invalid number/range, changed fixed default, wrong preset identity or a reserve that is not below rounds. It is not a balance evaluator, preview, spreadsheet importer or simulation.

## Acceptance checklist for a later approved evaluation

These are proposed targets, not recorded timings or test results.

- [ ] A fresh fixture uses the exact checked-in settings/schema, not a different test-only catalog.
- [ ] Real input opens the project, selects Practice, rejects 5 without changing document revision, undo count, journal or disk, then accepts 80. Default stays unchanged.
- [ ] Real palette input starts the registered settings check; the complete current result names its actual coverage and no simulation.
- [ ] Save writes 80; Undo returns the draft to 60 without changing disk; the old check becomes stale; Redo/Save keeps 80.
- [ ] Additional negative controls: non-integer text, 0, 101, and editing Default all produce useful refusals without an unintended write. The prepared theory covers these four controls, alongside the main walk's cross-field refusal. All remain UNRUN.
- [ ] Four of five first-time participants finish the task after setup in under ten minutes without coaching. Record wrong turns and requests for help; do not infer success from the headless test.
- [ ] On a stated machine and fixture, edit feedback meets a proposed 250 ms target, and a fresh settings check meets a proposed two-second target across one cold and five warm observations. No timing observations were made here.
- [ ] Evaluate the real distributed package separately: adapter loading, opening, saving and reopening. A source-linked test does not prove package contents, installation, rendering or accessibility.

## Maintainer appendix: prepared automated check

This appendix is not needed by a newcomer. Maintainer command after this card is applied and execution is allowed:

```
dotnet test tests/Brutal.Authoring.Tests/Brutal.Authoring.Tests.csproj -c Release --filter FullyQualifiedName~FirstTaskWalkTests --blame-hang-timeout 10m
```

Status for this delivery: **UNRUN**. It uses one serial headless context and native input events, not direct document mutation. The test loads the adapter object as test setup and copies only the two sample JSON inputs into its owned temporary folder. It does not launch the app or verify dynamic loading of the built DLL. Source provenance and gates are in the card's handoff, not evidence that this runtime command passed.
