# Your first settings change

**Goal:** change a game setting, see BED refuse a bad value and explain why, run the game's own check, then save, undo and redo. About ten minutes.

**What you need:** the BED download, unzipped. No terminal, build or programming.

## Get the sample ready

1. Unzip the release into a folder you can write to. Double-click **BED.exe**.
2. In the unpacked release, find **samples/first-task**. Copy that folder to a new working folder named **MyFirstTask**. It contains data/settings.json and schemas/settings.schema.json.
3. Keep the editor and its **adapters** folder together. The release includes the trusted, self-authored FirstTaskAdapter in adapters/first-task. If the sample or its adapter is missing, download the release again.

## The task

1. On the start screen, click **Open project...**, choose your **MyFirstTask** folder and open it. BED finds its data and schemas folders and loads the sample's adapter automatically. If `settings.json` isn't shown, click it in **Files**.
2. Find the search box above the inspector, type **Practice**, and press Enter. Activate its first result (Tab, then Enter after searching, or click the result). The selected preset must be Practice, not Default. Its `rounds` value starts at **60**, and `reserve` is **10**.
3. In Practice's `rounds` field, select the current text, type **5**, and press Enter. This is a deliberate mistake. Expect a message containing **“rounds must be greater than reserve (10)”**, with advice to increase rounds or lower reserve. The refused input may remain in the field so you can correct it; it has not become the saved setting. If BED accepts 5, the sample's adapter didn't load: check that the **adapters** folder is next to BED.exe.
4. Replace that text with **80** and press Enter. Practice now has 80 rounds; Default must still have 60. Nothing is saved yet. We are changing settings, not running a game preview.
5. Press **Ctrl+Shift+P**, type **Check first task settings**, and activate **Run check: Check first task settings**. Wait for it to finish. Expect a complete, current report saying both presets were checked: identities, numeric limits and reserve below rounds. It should also say **Practice has 80 rounds**, proving which draft you checked, and that no simulation was run. If the command is absent, stop and check that FirstTaskAdapter loaded. “Links” alone is not this check.
6. Press **Ctrl+S** to save. Practice's 80 is now written to `data/settings.json`. Saving doesn't run the check for you; that's what step 5 was for.
7. Press **Ctrl+Z** to undo. Practice goes back to 60 and the file is marked unsaved again; the file on disk still says 80. The check you ran in step 5 was for the 80-round version, so run it again if you keep this one.
8. Press **Ctrl+Y** to redo, then **Ctrl+S** to keep the 80-round version. Close and reopen the project if you want to see the saved value.

Default is deliberately fixed, so edit Practice to explore. The sample's rules catch a number out of range, a changed Default, a wrong preset name, and a reserve that isn't below rounds.

## Next steps

- [Start here](start_here.md) covers data, a 3D level and Play in one walk.
- [Edit project data](data_workflow.md) covers tables, forms, links and changing many rows.
