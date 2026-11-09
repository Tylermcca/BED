# Route puzzle: a small game adapter

Reach the summit before your energy runs out. Open `data/routes.json`, change a cost or the starting energy in the table, then choose **Try route** from Play. The supplied winning route is **Trailhead → Brook → Summit** (cost 4 of 5 energy); Trailhead → Ridge leaves too little energy to finish. Stop returns to the unchanged draft. Try again starts fresh. Play does not save anything.

This is a second self-authored sample beside Toy. It demonstrates one catalog, its schema, the native game's own rules, a registered Check, and a workspace-owned Play session. It uses no game content, art, randomness, clock, network or external game process.

## Projects and data

- `RoutePuzzle.csproj`: ordinary .NET model/loader/game, with no authoring references.
- `../RoutePuzzle.Authoring/`: trusted in-process `IProjectAdapter`; references the public authoring host, not the other way around. The runtime DLL and generated `RoutePuzzle.Authoring.deps.json` must stay beside the adapter DLL.
- `data/routes.json` and `schemas/routes.schema.json`: one version-1 catalog and its matching schema. The public data editor associates them by filename; no registration is required.
- `../../tests/RoutePuzzle.Sample.Tests/`: prepared native, project, snapshot, unsaved-Play and headless-lifetime cases. Fixtures are copied from these two exact files into the test output; tests never search the repository or real game folders.

## Open the sample

From the repository root, with the repository's existing SDK/dependencies available:

```text
dotnet build samples/RoutePuzzle.Authoring/RoutePuzzle.Authoring.csproj -c Release
BED --data samples/RoutePuzzle/data --schemas samples/RoutePuzzle/schemas --adapter samples/RoutePuzzle.Authoring/bin/Release/net10.0/RoutePuzzle.Authoring.dll --settings <disposable-settings-folder>
```

The second command opens the normal editor window. These instructions do not establish that your adapter and host build are compatible. Supply the actual host executable path if it is not on PATH. The adapter shares the host's authoring assemblies (`Private=false`, as in CheckersAdapter); its own RoutePuzzle dependency is copied alongside it, with an explicitly generated dependency file for the existing assembly resolver. This trusted adapter mechanism is not a sandbox. Verify loading against the matching host build before distributing.

The Checks panel offers **Check route puzzle**. It reads the frozen project snapshot, including unsaved edits, and calls the same native loader used by Play. The generic host owns timeout, stale-result tracking and cancellation. `Complete=true` means inspected; findings can still make the result fail. A missing catalog produces an incomplete result, never a clean one.

## What the catalog means

`kind` is `sample.route-puzzle`, `version` is 1. `title` is 1–80 characters. `start` and `goal` name distinct places. `energy` is an integer 1–30. There are 2–24 places; each has a unique lowercase ID (1–24 characters), a label (1–50) and 0–4 exits. Each exit names an existing place and costs an integer 1–9. Outgoing destinations cannot repeat. The goal has no exits.

The schema handles field shapes and ranges. `RouteCatalog.Read` also checks IDs, references and that a route to the goal costs no more than the energy budget. Its shortest-path check settles at most 24 vertices, without playing games. A native error names a field and a correction. Non-version-1 catalogs are refused; there are no migrations in this sample. Costs must use integer JSON number spelling (`3`, not `3.0`).

Every move spends positive energy, so a run makes at most 30 moves even if the authored graph contains a cycle. An unknown, unaffordable or finished-game choice returns false without changing state. The immutable catalog is separate from the runtime's current place, energy and visited path. The adapter starts from the `DataDocument` passed by Play, not a reread of the saved file. The existing editor prevents edits while playing and disposes the session on Stop or workspace closure.

## Extending and checking it

Copy these two sample project folders and the data/schema pair, then rename the namespace, kind and dotted Play/Check IDs. Keep runtime code free of editor references. Add new rules to the native loader first so file loading, editor validation, Check and Play agree. Keep commands through the document session for undo/save/outside-edit protection; do not mutate a running document from `Draw`.

Use this scoped command when you are ready to check the sample:

```text
dotnet test tests/RoutePuzzle.Sample.Tests/RoutePuzzle.Sample.Tests.csproj -c Release --filter FullyQualifiedName~RoutePuzzleSample.Tests.RouteSampleTests --blame-hang-timeout 10m
```

Use a disposable `ROUTE_SAMPLE_TEST_ROOT` and isolated build/cache/TEMP roots for a controlled run. Headless cases use an offscreen context; no app, listener or process is launched. Visual coverage is a small smoke check, not a layout/accessibility acceptance study. No new packages are introduced: the test project uses the repository's existing centrally versioned xUnit/test SDK dependencies. Verify compilation, adapter loading and the scoped tests on your intended host before distributing this sample. This guide is not a successful-run receipt.

## Reuse the sample

The puzzle, names, routes, data, rules and messages are original; there are no third-party fixtures or assets. The wiring follows StarterAdapter, CheckersAdapter and frozen check inputs from `Checks.cs`. Keep the runtime separate from the editor adapter when adapting this pattern. No new public interface is required.
