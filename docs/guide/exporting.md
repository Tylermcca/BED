# Export a game build

**Export build** creates a folder you can run without BED. It uses your saved files. Stop Play and save your edits with **Ctrl+S** before starting.

Export build is in BED **0.2.1-rc.1** and later.

The project needs an adapter that supplies a build command and the checks for its output. If **File > Export build** is unavailable, follow your project's build setup instructions.

## Build, verify and copy

1. Choose **File > Export build**.
2. Choose **Build saved files**. Read **Export status** and the output below it. Fix any reported data, asset or build problem before trying again.
3. Wait through **Building saved files...** and **Checking the unpacked build...**. BED checks the saved inputs, the files produced by the build and the project's test launch. **Copy build to folder...** becomes available only after verification succeeds.
4. Enter a new or empty **Destination folder**. BED may suggest the last folder you used for this project. Choose a fresh folder if that one still contains an earlier build.
5. Choose **Copy build to folder...**. Wait through **Copying verified build...** until **Build copied to** names your destination, then open it in File Explorer.

BED refuses a destination that already contains files. It does not replace your previous build. If copying fails, read the message and choose a suitable folder.

Use **Tab** to move between controls and **Enter** to activate a button. After typing a destination, press **Tab** to leave the field. **Escape** cancels a running export or closes the window. **Cancel export** also stops the current operation, including copying; a canceled copy leaves the destination unchanged. **Close** cancels it if it is still running. Your edits stay in BED.

## Read the export log

Choose **Open log folder** in the Export build window to find **export.log**. It records the build's output, the unpacked check and the copy result, including errors. The button becomes available after an export has created its log.

Each run has its own folder under your local BED export storage, beside the verified build and outside the project. The log is not included in the copied game. BED keeps the last **3** runs for each project; copy a log elsewhere if you need it for longer. An active build, check or copy is kept while it is in use.

## From the command line

For a project with one level document and an adapter that supports Export build, run this from the folder containing BED.exe:

```powershell
.\BED.exe --probe-export "C:\work\project" --destination "C:\builds\game" --timeout 60
```

Replace the two paths with your saved project and a new or empty output folder. Keep the project's adapter in the **adapters** folder beside BED.exe. This runs without opening a window. It builds from saved files, verifies the build, and copies it only after the checks pass, using the same steps as **File > Export build**. Save any open edits first; the command does not save them for you.

The command prints JSON with the copied files, their sizes and hashes, and exits with code **0** on success. On failure it prints a message and exits with a non-zero code. It refuses a destination containing files and does not save Play settings or approvals. `--timeout` is optional: the default is **60 seconds**, and accepted values are **1 to 300**. Run the copied game using the instructions below.

## What is in the folder

Keep the copied folder together. It contains the game's executable and the runtime files its build needs.

For a level project using BED's saved assets, it also contains:

- **data/level.json:** the saved level, including the object positions you saved.
- **.bed/cache/current.json:** the pointer to the prepared asset generation used by this build.
- **.bed/cache/generations/**: that generation's manifest and prepared model and texture files.

The prepared cache is part of the game build. Copy the whole folder when moving it to another computer. Removing the cache or copying only the executable can leave the game without its assets.

## Run the copied game

Use the executable and launch instructions supplied by your project. Run it from the copied folder so its relative paths find the saved level and assets there. BED can be closed.

The bundled level example uses a console program to confirm that the saved level loads. To check its exported build in PowerShell, open the copied folder in a terminal and run:

```powershell
.\Game.exe --project . --seconds 0
```

Replace **Game.exe** with the executable's actual filename. Here `.` means the copied folder, and `--seconds 0` makes this example report the loaded level and exit. These arguments belong to the example; another game's arguments can differ. The example's exported executable needs the .NET 10 runtime installed on the computer where you run it.

Try the copied game before sharing it. Include its required runtime files, licences and notices. See [content rights](content_rights.md) for asset permissions and [Play](playing.md) for trying saved files while editing.
