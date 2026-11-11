# Prefab and physics sample

Open this folder with BED's **Open project** command (or Ctrl+O). The packaged copy is `samples/prefab-physics` beside BED. Keep `data`, `schemas`, `assets`, `reference_map.json` and `.bed/project.json` together.

## What it shows

- **A prefab.** `data/crate.json` defines a crate: one box model with a dynamic body (1 kg, friction 0.6, a half-metre box shape).
- **Three copies of it.** The level places the crate three times as a small, slightly offset stack. Each copy follows the prefab.
- **An override.** The top crate keeps its link to the prefab but sets its own mass to 5 kg. The Inspector marks it as a local change.
- **Three kinds of body.** The floor is static, the crates are dynamic and the red and white paddle is kinematic. A game moves a kinematic body itself; this sample's game slides it back and forth along X, 1.5 m each way every 4 seconds.
- **World physics.** The level sets gravity to 9.8 m/s² downward and a fixed step of 0.01 seconds.

`.bed/project.json` turns physics on for this project, so Play uses the physics starter game in `game/`. That game has no window yet. It loads the saved level, runs the simulation and reports what happened as text. The packaged copy has the game installed. In a source checkout, install it under `game/` first, as the [physics starter README](../PhysicsStarter/README.md) describes.

## Things to try

1. Select a crate in the 3D view. Its body (motion, mass, friction and shape) appears in the Inspector.
2. Open **View > Physics** to see the world's gravity and fixed step.
3. Change a crate's mass in the Inspector, then save. Only that copy changes; **Reset** puts the prefab's value back.
4. Press **Build and Play**. The first time, BED shows the exact build command and asks you to allow it. The game then prepares the saved level, drops the stack under gravity, slides the paddle and reports in the Play output: the number of bodies and fixed steps, where the top crate came to rest and how far the paddle travelled.
5. Use **Export build** to copy a standalone game that runs the same saved physics without BED.

Play and Export use your saved files only. Simulated movement never changes the level.

## Assets

The scene is a small grassy island: three wooden crates, a red and white paddle, a picket fence, two round trees and a beach ball. The fence, trees and ball are scenery with no body, so the physics game ignores them. Every model, material and texture is self-authored by the script below; their sidecars record stable IDs, source hashes, importer settings and origin `ours`.

The crate model is slightly larger than its 1 m body (1.08 m), so the 1 m box BED draws for each prefab copy's placement stays hidden inside it.

Run `python samples/PrefabPhysicsProject/generate_assets.py` from the repository root to reproduce every sample file: the models, materials, textures and sidecars, the crate prefab, the level, the schema, the reference map and `.bed/project.json`. Add `--check` to compare the committed files without changing them.

BED keeps its prepared assets in `.bed/cache` and the built level in `.bed/generated`. Neither is part of the sample; BED makes them again when you open and build the project.
