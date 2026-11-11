# Troubleshoot Play and Export

Start with the message in **Play console** or **Export status**. Show both **Show game output** and **Show game errors** in Play console, then use **Copy all** if you need to send the output to your project maintainer. If a **lines dropped** note appears, only the most recent output remains in BED.

The messages below come from BED's current Play and Export paths. Text in angle brackets stands for the actual field, filename, command or exit code. A message can appear after `Play failed:` or `Export status:`. Your game's tools can also print their own errors; read those alongside BED's message. For setup and the full workflow, see [Play](playing.md) and [Export](exporting.md).

## Play setup

| Message | What to do |
|---|---|
| `Set the program in Tools > Play settings` | Open **Tools > Play settings...**, choose the game executable, then choose **Save Play settings**. |
| `The program does not exist. Choose it or configure its build command.` | Choose the built executable, or add the command that builds it. Saving settings alone does not run the game. |
| `The Play working folder does not exist.` | Choose an existing **Working folder**. A relative folder starts at the project root. |
| `Build tool was not found on PATH: <tool>` | Install the tool your project requires and make it available on PATH, or choose its executable inside the project. |
| `Build input patterns must stay inside the project.` | Use project-relative patterns such as `src/**/*.cs`; remove drive letters and `..`. |
| `<field> must be a JSON list of quoted strings. Use [] for no items.` | For Arguments, Build arguments or Build inputs, enter a list such as `["--seconds", "5"]`, or `[]` for none. |
| `Needs your OK: <command> (working folder: <folder>)` | Check both the full command and working folder. Choose **Allow this exact command and continue** only if they are the ones you intend to run, or **Cancel** to change settings. |
| `No build command is configured.` | Add **Build command** and its arguments in Play settings, or build outside BED before choosing Play. |

## Saving before Play

| Message | What to do |
|---|---|
| `Play runs saved files. Save changes before Play?` | Choose **Save** for the current edits, **Don't Save** for the last saved files while retaining the edits, or **Cancel**. |
| `A save check is still running; try Play after it finishes.` | Wait for the save check. Fix any reported problem, then choose Play again. |
| `<file> was changed outside BED; choose which version to keep before Play.` | The saved copy changed or is unavailable. Choose which version to keep in BED before starting the game; keep the draft until you have chosen which version to use. |
| `Documents changed; choose Play again.` | Choose Play again to make a fresh save decision for the current documents. |
| `The documents or Play command changed; choose Play again.` | Review the current settings and choose Play again. The earlier approval prompt no longer describes this request. |
| `Changes are still waiting to be saved.` | Finish the pending save/check or fix its error. Keep your drafts; the game has not started from them. |

## Running and stopping

| Message | What to do |
|---|---|
| `Stop the running program first.` | Choose **Stop** or **Stop program**, and wait for completion before starting another run. |
| `The program is missing or older than its build inputs. Choose Build and Play.` | Choose **Build and Play** in Play console. Read the build output and approve the exact build command if asked. |
| `The program is missing. Set its program or build command in Play settings.` | Correct Program in Play settings, configure its build, or build it yourself before retrying. |
| `Build succeeded but the program is still missing or stale.` | Check that the build produces the configured Program at that path. Check the build-input patterns and output modification times. |
| `The build exceeded its declared deadline.` | Read the build output for a stalled tool or prompt. Fix it and retry; if a normal build needs longer, ask the project maintainer to review its deadline. |
| `The game stopped with error code <number>.` | Read the game/build output in Play console. If it names a missing runtime or file, supply that requirement; otherwise give the command, code and output to the project maintainer. |
| `Play failed: <reason>` | Read the reason and console output, correct the named path, build or process problem, then retry. Your editor drafts remain available. |
| `Requested polite close; waiting up to 5 seconds.` | Wait for completion. BED ends the owned process tree if the program does not close in that time. |
| `The program has no window to close, so BED ended it.` | This is the result of Stop for a program with no window. Wait until it has ended before starting again. |
| `Stopped the owned process tree after the 5-second grace period.` | The program did not close in time, so BED ended its processes. Check game output if that was unexpected. |

## Pause and Step

| Message | What to do |
|---|---|
| `This game hasn't enabled frame control yet.` | The game hasn't connected Pause and Step yet, or doesn't support them. Wait for it to finish starting. If the message stays, the game doesn't support Pause and Step; see [Add Pause and Step](pause_step.md). |
| `Waiting for simulation acknowledgement.` | BED has asked the game to pause or step and is waiting for its next tick. Wait a moment. |
| `Frame control acknowledgement timed out; simulation state is unknown.` | The game didn't answer within five seconds. Pause and Step turn off for this run; the game keeps running. Choose **Stop** and Play again to retry. |

## Export build and verification

| Message | What to do |
|---|---|
| `Save changes before building.` | Save with **Ctrl+S**, resolve any save error, then start Export build again. |
| `The saved file was changed outside BED; choose which version to keep before exporting.` | Resolve the file conflict in BED first. Export checks the saved version rather than choosing a version for you. |
| `Open a file before exporting.` | Open the level or data file the adapter exports, then choose **File > Export build**. |
| `The build failed. Check the build output below.` | Fix the first relevant error in the output, then choose **Build saved files** again. |
| `The build stopped with error code <number>.` | Read the build output for the tool's explanation. Fix that problem before rebuilding. |
| `Build exceeded its time limit.` | Check for a stalled build or a tool waiting for input. Ask the project maintainer to review the limit if a normal build needs longer. |
| `Build did not produce every allowlisted output.` | The adapter expected files the build did not produce. Give the output and missing-file details to the project maintainer; rebuild after the build setup is corrected. |
| `The test launch of the exported game stopped with error code <number>.` | The exported game's test launch failed. Read its output below Export status; check its required files, arguments and runtime. Copy stays unavailable. |
| `Smoke exceeded its time limit.` | The test launch did not finish in time. Ask the project maintainer to check the launch command and how the game exits. |
| `A successful unpacked check is required before copying.` | Choose **Build saved files** and wait for a successful check. Fix any build or test-launch failure first. |
| `Export canceled. Your edits are unchanged.` | Return to editing, or choose **Build saved files** to begin another export attempt. |

## Copy destination

| Message | What to do |
|---|---|
| `Choose a destination folder before copying.` | Enter a new or empty **Destination folder** and press Tab to leave the field before choosing Copy. |
| `Choose an absolute new or empty folder.` | Use a full path, for example `C:/Builds/MyGame-01`, to a new or empty folder. |
| `Choose a new or empty folder; existing files will not be replaced.` | Choose another folder. BED will not overwrite an earlier build or unrelated files. |
| `Choose a folder outside the owned build output.` | Choose a separate destination outside BED's temporary build folder. |
| `Copy destination contains a link.` | Choose a direct folder path with no linked folders along it. |
| `Build output changed since verification.` | Build and verify again. Keep other tools from changing the verified output while it is being copied. |
| `Build changed before copy: <file>` | The named output no longer matches the verified build. Build and verify again before copying. |
| `Copy destination is no longer empty.` | Another operation added files while copying was being prepared. Choose a fresh destination. |
| `Copy canceled; destination unchanged.` | The copy stopped and its temporary files were removed. Build and verify again when you are ready to copy. |

## Progress that does not need a fix

**Running.** means the program has started; it may still be loading. **Building saved files...** and **Checking the unpacked build...** describe export progress. **Copying verified build...** means the checked files are being copied; **Cancel export** also works during this step. **Build and unpacked smoke verified; ready to copy.** means the exported game's test launch succeeded and copying is available. **Build copied to <folder>** identifies the completed destination. Keep the entire copied folder together.

If Export build is unavailable, open the file you want to export and check that the project's adapter provides a build/export setup. Give the maintainer the exact message, the command and working folder shown by BED, and the relevant output. Keep your drafts until save problems are resolved.

If you think BED itself is at fault, choose **Help > Report a problem**. See [Reporting a problem](reporting_a_problem.md).
