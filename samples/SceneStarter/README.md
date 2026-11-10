# SceneStarter saved files and export review

A small self-authored console sample using `Brutal.Authoring` and `Brutal.Authoring.Runtime` with .NET 10. It has no Editor, ImGui or BRUTAL dependency and opens no window. This is a framework-dependent Windows sample; the destination needs the .NET 10 runtime.

## Play saved project files

`SceneStarter.exe --project PROJECT_ROOT` reads `data/level.json` and the published generation in `.bed/cache`. With no arguments it also accepts the root supplied in `BED_PROJECT`. Open the project in BED once to prepare its saved assets. The game derives nothing and never reads an editor draft, takes a cache owner lock or saves the project.

The same `SavedScene.Open` consumer can read a copied project layout. It checks the level's identities, links and local transforms through the core scene reader, then obtains each exact asset GUID from the hash-verified generation and decodes it through the Runtime mesh reader. Missing, changed or unsupported inputs refuse with exit 3; success prints a plain loaded-level message. With no lifetime option it keeps running until closed. `--seconds N` keeps it running for 0 to 60 seconds after the real load and then exits; `--exit-code N` returns that code after the load (or after the chosen duration). Use `--seconds 0` for a bounded owner smoke. There is no game-readiness handshake.

## Fixed export-review fixture

The supplied `scene.json` is a separate fixed two-object review fixture. `--smoke scene.json` preserves its exact root/child/transform/reference checks and refuses external assets. `--review FOLDER` checks the exact runtime inventory and that fixed fixture; these commands do not replace ordinary saved-project Play.

For an owner-reviewed trusted checkout:

```
dotnet publish samples/SceneStarter/SceneStarter.csproj -c Release --no-restore --self-contained false -p:UseAppHost=true -p:PublishSingleFile=false -o FRESH_OUTPUT
```

Use a new output directory. From it, run `SceneStarter.exe --review .` and `SceneStarter.exe --smoke scene.json`. Review permits exactly `SceneStarter.exe`, `SceneStarter.dll`, `SceneStarter.deps.json`, `SceneStarter.runtimeconfig.json`, `Brutal.Authoring.dll`, `Brutal.Authoring.Runtime.dll`, `scene.json`, `LICENSE.txt` and `NOTICE.txt`. It rejects extra assemblies/source files, directories, links, missing notices, empty/oversized files and dependencies outside those three managed assemblies. It does not publish, replace prior output or grant redistribution rights.

The `--build INPUT OUTPUT` producer copies the captured `data/level.json`, `.bed/cache/current.json` and that pointer's exact generation (manifest, owner marker and every derived model or texture). The host stages these files with relative paths, lengths and SHA-256 hashes. It checks the complete transport after the build and requires each saved file to match at the same output path. Other cache generations, staging files, locks, source assets and sidecars stay behind. Exports contain neither `payload.bin` nor the legacy `scene.json` fixture.

Run the exported game from its own folder with `SceneStarter.exe --project . --seconds 0`. It uses the same `SavedScene.Open` / `ProjectAssets.Open` readers as Play and exits 0 after loading. The adapter's export smoke runs this command before BED offers to copy the verified build. Changed saved files, missing generation dependencies, changed producing policy and unretained asset attributions refuse export.

Record actual build and smoke commands, exits and inventory before accepting an export. Source-level and headless fixture tests do not prove a complete published/unpacked release or native renderer. LICENSE and NOTICE remain the repository texts; no private art or game data belongs in the sample.
