# Samples

Choose a sample below, then run its command from the repository root after installing the documented prerequisites. These examples use the source checkout, not published packages.

Library and adapter samples build a component for a host; they are not standalone programs. The Checkers.App and Checkers.Game commands open windows.

| Sample | What it shows and how to try it |
|---|---|
| Toy | A neutral board document and undoable command catalog; `dotnet run --project samples/Toy/Toy.csproj -- commands`. |
| Tiles | Tile-map documents and recorded edits; `dotnet run --project samples/Tiles/Tiles.csproj -- commands`. |
| SceneTool | Headless scene catalog commands; `dotnet run --project samples/SceneTool/SceneTool.csproj -- commands`. |
| DataCheck | JSON data/schema validation; `dotnet run --project samples/DataCheck/DataCheck.csproj -- samples/RoutePuzzle/data samples/RoutePuzzle/schemas`. |
| ImportCheck | The import-check command-line sample; build with `dotnet build samples/ImportCheck/ImportCheck.csproj -c Release` (no import workload is part of this index smoke check). |
| Ladder | Describe a document, add a one-class tool, then an extension; library sample, `dotnet build samples/Ladder/Ladder.csproj -c Release`, then read its Rung1/Rung2/Rung3 sources. |
| LightsPlugin | Scene lights supplied by a plug-in manifest; library sample, `dotnet build samples/LightsPlugin/LightsPlugin.csproj -c Release`, then load the built plug-in through a matching host. |
| Checkers | Shared board rules, commands and editor plug-in; library sample, `dotnet build samples/Checkers/Checkers.csproj -c Release`; use Checkers.App to try it. |
| Checkers.App | Checkers inside the editor; `dotnet run --project samples/Checkers.App/Checkers.App.csproj` opens a window. |
| Checkers.Game | The same checkers rules in a standalone player; `dotnet run --project samples/Checkers.Game/Checkers.Game.csproj` opens a window. |
| CheckersAdapter | Checkers project integration for the general host; `dotnet build samples/CheckersAdapter/CheckersAdapter.csproj -c Release`, then use the host's adapter-loading route with CheckersProject. |
| CheckersProject | Two editable checkers positions; open `samples/CheckersProject` in the host with CheckersAdapter loaded; see [its README](CheckersProject/README.md). |
| StarterAdapter | A minimal project adapter with a game rule beyond the schema, plus [compiled managed-source hooks](StarterAdapter/ManagedSourceHooks.cs) for admission, saved events, owner ticks, background checks and multiline text; `dotnet build samples/StarterAdapter/StarterAdapter.csproj -c Release`, then load it in the matching host for StarterProject. |
| StarterProject | Small generic data and a reference map; open `samples/StarterProject` in the host; see [its README](StarterProject/README.md). |
| RoutePuzzle | Runtime-only route model/loader; `dotnet build samples/RoutePuzzle/RoutePuzzle.csproj -c Release`; use its authoring companion to play, as [documented](RoutePuzzle/README.md). |
| RoutePuzzle.Game | Saved-files console player with cooperative Pause/Step/Resume; `dotnet build samples/RoutePuzzle.Game/RoutePuzzle.Game.csproj`; run with `--project samples/RoutePuzzle --seconds 5 --route brook,summit`. No window/audio in automated tests. |
| RoutePuzzle.Authoring | Data editing, frozen Check and workspace-owned Play; `dotnet build samples/RoutePuzzle.Authoring/RoutePuzzle.Authoring.csproj -c Release`, then follow [the opening instructions](RoutePuzzle/README.md#opening-it-after-integration). |
| FirstTask | The newcomer task: change a settings preset, see a mistake caught, check, save and undo; `dotnet build samples/FirstTask/FirstTask.csproj -c Release`, then open `samples/FirstTask` in BED and follow [the first task guide](../docs/guide/first_task.md). |
| RuntimeInspect | Print a saved level’s objects, mesh parts and textures without an editor; `dotnet run --project samples/RuntimeInspect/RuntimeInspect.csproj -- samples/SceneStarterProject` after preparing the sample in BED once. See [its README](RuntimeInspect/README.md). |
| SceneStarter | A tiny self-made game that loads a level with only the runtime reader; used to prove an exported build runs without BED. `dotnet build samples/SceneStarter/SceneStarter.csproj -c Release`. |
| PhysicsStarter | [Game-owned headless physics](PhysicsStarter/README.md) from explicitly enabled saved scenes, pinned native integration; `dotnet build samples/PhysicsStarter/PhysicsStarter.csproj` then `--project <saved-project> --seconds 1`. |
| SceneStarterAdapter | Connects BED to SceneStarter so Play launches the sample as its own program; `dotnet build samples/SceneStarterAdapter/SceneStarterAdapter.csproj -c Release`. |

## Check the sample index

Run `python tools/check_samples_index.py --root .` to check that sample projects belong to the solution and that documented source paths exist. It does not build or run samples. Its report distinguishes checked-in source, generated outputs and paths you must supply.

To build the samples and the solution's other projects, run `dotnet build Bed.slnx -c Release`. Every sample project belongs to that solution, including Tiles and Ladder under its tests folder. Check the build result before running a sample.

The index checker does not run that build, GUI samples, import workloads or tests.


## Runtime assets

The game-facing `src/Bed.Runtime` library reads saved project asset generations and BEDMESH geometry without an Editor, ImGui or BRUTAL dependency. Build it with `dotnet build src/Bed.Runtime/Bed.Runtime.csproj -c Release`. It does not derive assets or grant export permission.
