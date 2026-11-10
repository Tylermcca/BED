# SceneStarter project

Open this folder with BED's **Open project** command (or Ctrl+O). The packaged copy is `samples/scene-starter` beside BED. Keep `data`, `schemas`, `reference_map.json` and `assets` together.

The level contains two empty objects and a textured crate. The asset picker also offers a cube and a tall pillar. All models and the crate's 8 by 8 plank texture are self-authored. Their sidecars record stable IDs, source hashes, importer settings and origin `ours`. Copy each model together with its material, texture and sidecars when making a personal copy.

Run `python samples/SceneStarterProject/generate_assets.py` from the repository root to reproduce all nine asset files. It also runs the crate generator. Add `--check` to compare the committed files without changing them. The script leaves the level alone.

Open the project in BED to prepare its assets. BED stores the published derived generation under `.bed/cache`; source models and sidecars stay in `assets`. Keep that cache with a saved project when using the sample runtime outside BED. Missing publication tells you to open the project in BED once; changed derived bytes refuse instead of being repaired by the game.

The 3D view uses the project's prepared assets. It draws each model part with its own base colour and texture through the Vulkan renderer. Picking and movement still select the whole object. The CPU fallback draws the part colours and says that textures are unavailable there. Missing or stale assets show placeholders until the project prepares them again.

Play uses `game/SceneStarter.exe` inside this project and its saved `data/level.json`, through the same project Play settings as other programs. The packaged sample needs no outside-program approval. A custom program outside the project still uses the normal exact-command approval. The save prompt lets you save your edits, run the last saved files or cancel. The program reads the saved generation, prints a plain loaded-level message and keeps running until Stop; it does not receive a draft or report readiness over a protocol.

Export build copies the saved level and its complete asset generation. The exported program reads them through the same runtime reader, without BED. See the Play guide for settings, build approval and Stop.

For an optional glTF example on a personal copy, run `python samples/SceneStarterProject/generate_textured_glb.py --out "<assets folder of your copy>"`. Add `--check` to verify the four generated files without changing them. This produces a self-authored `textured-quad.glb`, an 8 by 8 four-colour PNG and their sidecars; keep all four together. The GLB refers to that separately admitted PNG. Open the copy in BED and add the model through the asset picker. The generator never adds it to the shipped level.
