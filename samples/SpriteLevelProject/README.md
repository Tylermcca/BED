# Flag Garden: a flat sprite level

Open this folder in BED. It is a small cartoon garden: a sky with clouds and a sun, rolling hills, grassy ground, wooden crates, coins, flowers, two flags and a round lilac friend. Every picture is original, drawn by this folder's script. The level has seventeen sprites in four drawing layers: sky, hills, the ground and crates, then the friend, flags, coins and flowers in front. The flag-and-block prefab appears twice; the right flag has a local size change. There is no physics or player controller in this sample.

1. Select the round friend in the Outliner and move it in the flat view. Undo to bring it back.
2. Select the right flag. Its size differs from the saved prefab; revert Size to match the left flag, or undo to keep the change.
3. Save. Choose Build and Play to prepare the saved sprites, then Play. Allow the sample build command if BED asks. The existing console runtime loads and checks the level; it does not open a game window. Stop ends the program.
4. After Build and Play has prepared the current saved level, choose File > Export build..., Build saved files, then Copy build to folder.... Keep the whole exported folder together. Its README explains how to run the checked copied runtime.

The download includes its game program. In a source checkout, build SceneStarter and copy its runtime output into this project's game folder before Play. Regenerate art and saved sample files with `python samples/SpriteLevelProject/generate_assets.py`; use `--check` to compare without writing. All eight PNGs and rights records are self-authored and reproducible; no outside art is used.
