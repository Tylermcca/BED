# SceneStarter saved files and export review

A small self-authored console sample using `Bed` and `Bed.Runtime` with .NET 10. It has no Editor, ImGui or BRUTAL dependency and opens no window. This is a framework-dependent Windows sample; the destination needs the .NET 10 runtime.

## Play saved project files

`SceneStarter.exe --project PROJECT_ROOT` reads `data/level.json` and the published generation in `.bed/cache`. With no arguments it also accepts the root supplied in `BED_PROJECT`. Open the project in BED once to prepare its saved assets. The game derives nothing and never reads an editor draft, takes a cache owner lock or saves the project.

The same `SavedScene.Open` consumer can read a copied project layout. It checks the level's identities, links and local transforms through the core scene reader, then obtains each exact asset GUID from the hash-verified generation and decodes it through the Runtime mesh reader. Missing, changed or unsupported inputs refuse with exit 3; success prints a plain loaded-level message. With no lifetime option it keeps running until closed. `--seconds N` keeps it running for 0 to 60 seconds after the real load and then exits; `--exit-code N` returns that code after the load (or after the chosen duration). Use `--seconds 0` for a bounded owner smoke. There is no game-readiness handshake.

## Fixed export-review fixture

The supplied `scene.json` is a separate fixed two-object review fixture. `--smoke scene.json` preserves its exact root/child/transform/reference checks and refuses external assets. `--review FOLDER` checks the exact runtime inventory and that fixed fixture; these commands do not replace ordinary saved-project Play.

For an owner-reviewed trusted checkout:

```
dotnet publish samples/SceneStarter/SceneStarter.csproj -c Release --no-restore --self-contained false -p:UseAppHost=true -p:PublishSingleFile=false -o FRESH_OUTPUT
```

Use a new output directory. From it, run `SceneStarter.exe --review .` and `SceneStarter.exe --smoke scene.json`. Review permits exactly `SceneStarter.exe`, `SceneStarter.dll`, `SceneStarter.deps.json`, `SceneStarter.runtimeconfig.json`, `Bed.dll`, `Bed.Runtime.dll`, `scene.json`, `LICENSE.txt` and `NOTICE.txt`. It rejects extra assemblies/source files, directories, links, missing notices, empty/oversized files and dependencies outside those three managed assemblies. It does not publish, replace prior output or grant redistribution rights.

The `--build INPUT OUTPUT` producer copies the captured `data/level.json`, `.bed/cache/current.json` and that pointer's exact generation (manifest, owner marker and every derived model or texture). The host stages these files with relative paths, lengths and SHA-256 hashes. It checks the complete transport after the build and requires each saved file to match at the same output path. Other cache generations, staging files, locks, source assets and sidecars stay behind. Exports contain neither `payload.bin` nor the legacy `scene.json` fixture.

Run the exported game from its own folder with `SceneStarter.exe --project . --seconds 0`. It uses the same `SavedScene.Open` / `ProjectAssets.Open` readers as Play and exits 0 after loading. The adapter's export smoke runs this command before BED offers to copy the verified build. Changed saved files, missing generation dependencies, changed producing policy and unretained asset attributions refuse export.

Record actual build and smoke commands, exits and inventory before accepting an export. Source-level and headless fixture tests do not prove a complete published/unpacked release or native renderer. LICENSE and NOTICE remain the repository texts; no private art or game data belongs in the sample.

## Saved scene-v2 generation

An opted-in project declares root `sceneBuild` version 1 in `.bed/project.json`, with `sceneVersion: 2`, its portable `source` (for example `data/level.json`), a separate `generated` path under `.bed/generated/scenes/`, and explicit `spaces` (`3d`, `2d-xy`, or both). The adapter admits the declared source through the guarded data folder; private `.bed` files cannot be edited there. Projects without this declaration keep v1 behavior.

With the installed game in `game/`, the adapter's default build runs `game/SceneStarter.exe --compile-scene2 .`. For a direct headless generation check, run `SceneStarter.exe --compile-scene2 PROJECT`. This reads saved files and accepted embedded prefab snapshots, validates the published cache and asset kinds, then writes the flattened scene, `.bed/generated/scenes/runtime.json` and a content stamp last. It does not import assets or change the authoring source. Missing publication, unsupported space and changed inputs refuse.

The default stamp is `.bed/generated/scenes/build.stamp.json`; build inputs explicitly include the source, `.bed/project.json` and `.bed/cache/current.json`. Overrides must retain those three inputs. Unaccepted external definition edits do not invalidate an embedded acceptance. The stamp checks file contents and generated output hashes even when timestamps stay the same. It never grants program approval. Installing the game executable is a prerequisite to this generator; it does not bootstrap an absent executable from source. Both saved-project Play and export consume the checked generated scene and exact published generation. Runtime-only copied games need no authoring source, definitions, original images, project settings or build stamp. The game-owned runtime descriptor binds the generated path/hash and published generation identity/revision. Missing, changed, unflattened or mismatched runtime files refuse; runtime loading never repairs or generates them. `SavedScene.Level2` retains complete sprite metadata; `Textures` exposes its decoded published sprite/material pixels. `Level` remains the common object view for existing callers. Export requires a current saved-scene build; use Build and Play after changing the source, then export. Export rechecks source/settings/stamp, generated bytes and producing asset policies at capture/admission and before declaring its smoke command. Copy rechecks the admitted immutable build bytes.

Authored physics descriptions are currently refused by this sample compiler and runtime. A physics-capable game integration is required; parsing body settings alone never starts a simulation.
