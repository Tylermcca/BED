# Flag Garden: a flat sprite level

Open this folder in BED. It is a small cartoon garden: a sky with clouds and a sun, rolling hills, grassy ground, wooden crates, coins, flowers, two flags and a round lilac friend. Every picture is original, drawn by this folder's script. The level has seventeen sprites in four drawing layers: sky, hills, the ground and crates, then the friend, flags, coins and flowers in front. The flag-and-block prefab appears twice; the right flag has a local size change. Play turns it into a small game: walk the round friend, collect the three coins and reach a flag.

1. Select the round friend in the Outliner and move it in the flat view. Undo to bring it back.
2. Select the right flag. Its size differs from the saved prefab; revert Size to match the left flag, or undo to keep the change.
3. Save. Choose Build and Play to prepare the saved sprites, then Play. Allow the sample build command if BED asks. The game opens in its own window and shows the level as saved. Stop, Esc or closing the window ends it.
## Play the garden

- **Move:** arrow keys or A and D. **Jump:** Space, Up or W (let go early for a short hop).
- **Coins:** touch one to collect it; the count is in the top corner. With every coin collected, touch a flag: "You did it!".
- **R** starts again from the beginning. **Esc** or closing the window quits.

The game reads only the built level, so whatever you move in BED and save is what you play. It follows one convention instead of fixed positions:

- `round_friend` is the player.
- Things named `coin...` are coins; things with `flag` in their name are goals.
- Everything at drawing order 0 (the ground and crate layer, including the flag posts' blocks) is solid. You stand on the filled part of a picture, so grass tips and rounded corners don't count. Everything else (sky, hills, flowers) is scenery.
- Falling off the level puts the friend back at the start, keeping the coins.

Headless runs never open a window: `SceneStarter.exe --project . --seconds 0` loads and exits, and `--frames N --input "right*40,right+jump*12" --shot frame.png` plays N steps of scripted keys (`left`, `right`, `jump`, `restart`, `none`, each `*frames`) and saves the last frame as a picture. `--headless` keeps the game running without a window until it is closed.

## Export

4. After Build and Play has prepared the current saved level, choose File > Export build..., Build saved files, then Copy build to folder.... Keep the whole exported folder together. Its README explains how to run the checked copied runtime.

The download includes its game program. In a source checkout, build SceneStarter and copy its runtime output into this project's game folder before Play. Regenerate art and saved sample files with `python samples/SpriteLevelProject/generate_assets.py`; use `--check` to compare without writing. All eight PNGs and rights records are self-authored and reproducible; no outside art is used.
