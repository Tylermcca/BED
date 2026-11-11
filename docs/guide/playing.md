# Try a project with Play

**Play** runs your game's program using the project's saved files. Open a project and a data file, then choose **Play** in the toolbar. If the program needs setting up, BED opens **Play settings** and explains what is missing.

## Quick steps

1. Save your changes (**Ctrl+S**, or **Save all** in **Files**). Play always runs what's saved.
2. Click **Play** in the middle of the toolbar.
3. If BED says the program is missing or out of date, choose **Build and Play** in the toolbar or **Tools > Build and Play**. BED builds the game and then starts it.
4. If BED opens **Play settings**, fill in what it says is missing (most projects with an adapter fill these in for you). See *Set up Play* below.
5. Use **Pause** and **Step** if your game supports them, and **Stop** to finish.

## Set up Play

Choose **Tools > Play settings...**. Your project's adapter may already fill in these fields. A project without an adapter can use the same settings.

- **Program:** the game's executable. Use **Browse program...** to choose it.
- **Arguments (JSON list):** each argument as a separate quoted item. Use `[]` for none, or `["--seconds", "5"]` for two arguments if your game supports them. BED passes these items directly to the program.
- **Working folder:** where the game runs. `.` means the project root. Use **Browse working folder...** to choose another folder.
- **Build command:** the tool that builds the game, if needed. A tool name such as `dotnet` must be installed and available on PATH. A relative command path starts at the project root.
- **Build arguments (JSON list):** arguments for that tool, for example `["build", "-c", "Release"]`. The build runs from the project root.
- **Build inputs (JSON list of globs):** project-relative file patterns such as `["src/**/*.cs", "src/**/*.csproj"]`. BED compares their modification times with the program to decide whether a build is needed. Include the files your build depends on.

Choose **Save Play settings**. This saves the configuration; choose **Play** separately to run it. If BED reports a problem, fix the named field and save again.

### Where settings live

BED saves these fields under `play` in `.bed/project.json` at the project root. Relative program and working-folder paths start there. Arguments that name files are interpreted by the game from its working folder.

For example, a game with its executable inside `bin` might use:

```json
{
  "play": {
    "program": "bin/Game.exe",
    "arguments": [],
    "workingFolder": ".",
    "build": {
      "command": "dotnet",
      "arguments": ["build", "-c", "Release"],
      "inputs": ["src/**/*.cs", "src/**/*.csproj"]
    }
  }
}
```

Use your game's actual executable path and build arguments. Leave the build fields empty if you build elsewhere. When a field is absent from the file, BED uses the adapter's default for that field, if supplied. An empty argument list clears default arguments. Opening a project alone does not create this settings file.

## Save and run

1. Choose **Play**.
2. If you have unsaved edits, choose **Save** to run them, **Don't Save** to run the last saved files while keeping your edits in BED, or **Cancel** to return to editing. A failed save leaves the game stopped; fix the reported problem first.
3. If BED asks for approval, read the displayed command and working folder, then choose **Allow this exact command and continue** or **Cancel**.
4. Read **Play console** for status, the game's output and any errors. **Running** means the program has started; it may still be loading.

If BED says the program is missing or older than its build inputs, choose **Build and Play** in **Play console**. BED runs the configured build command and starts the game after a successful build. If no build command is configured, add one in **Play settings**, or build the game yourself. Fix any reported build error before trying again.

## Approvals

Build commands require your approval. BED also asks when the program or working folder is outside the project. The prompt shows the exact command and working folder it will use. Build and Play can ask separately for the external game command and the build command.

Approvals are remembered in your local user preferences, separately from `.bed/project.json`. Sharing project settings does not share your approval. Changing the command, arguments or working folder can require a fresh approval. Saving settings alone grants none.

## Stop and return to editing

Choose **Stop** in the toolbar or **Stop program** in **Play console**. BED asks the program to close and waits up to five seconds, then ends its remaining processes. If the program has no window to close, BED ends it immediately. Wait for completion before starting it again.

Play does not bring changes made inside the game back into BED. To create a build you can copy to another computer, see [building and export](building.md).

## Link a game diagnostic to its saved data

A game can opt in by printing one UTF-8 line to stdout or stderr:

```text
BED_DIAGNOSTIC_V1 {"file":"data/items.json","pointer":"/items/0/name","message":"Choose a nonempty name."}
```

The line contains the exact prefix `BED_DIAGNOSTIC_V1 ` followed by a JSON object with exactly three string keys: `file`, `pointer`, and `message`. Key order does not matter. The entire line, excluding its line ending, must fit in 4096 UTF-8 bytes. JSON escaping keeps quotes and newlines in a message inside that single line.

`file` starts at the project root (the `BED_PROJECT` environment variable), even when Play uses another working folder or the editor opens a data subfolder. Use forward slashes and no rooted path, backslash, colon, empty segment, `.` or `..` segment. `pointer` is an RFC 6901 JSON Pointer: empty selects the document root; `/items/0/name` selects a field. Escape a key's `~` as `~0` and `/` as `~1`.

Games using `Bed.Runtime` can format a line without doing any editor I/O:

```csharp
Console.WriteLine(ProjectDiagnostic.Format(
    "data/items.json", "/items/0/name", "Choose a nonempty name."));
```

Import `Bed.Runtime`. `Format` throws `ArgumentException` for invalid paths, pointers, Unicode or an oversized line. Other languages can emit the same format directly.

In **Play console**, activate **Open** beside the message by click or keyboard to reveal its item or field. **Back to previous location** returns to your earlier selection. Only files admitted by the open project can be opened. Missing items report a reason through the same navigation used by check findings. If a saved input or draft has changed since Play began, BED refuses the old location and asks you to run Play again; this includes running with unsaved drafts via **Don't Save**.

Malformed or unrecognised lines remain ordinary console text. **Copy all** retains the original output. Diagnostics are advisory locations: printing one does not edit data, acknowledge game readiness or exchange project state with the editor.

## Optional cooperative Pause and Step

Games may reference the BCL-only Bed.Runtime library and keep one `using var control = FrameControl.ConnectFromEnvironment();` for the run. At every simulation tick boundary, call `control.PollFrame()`. Advance simulation only when it returns true. Continue pumping input and drawing on false; never block the UI waiting for a permit. One Step means one simulation tick. Call again after that tick to acknowledge completion. Clear any simulation accumulator across paused periods so Resume does not catch up paused wall time. No helper or invalid/missing launch environment means ordinary simulation; disconnect releases the gate at the next poll.

BED's Play console offers Pause simulation (F9), Step one frame (F10) and Resume simulation (F11), also in the command palette. Only a helper authenticated as the exact owned game process enables them. Pause becomes effective only after acknowledgement; one command may be pending. Step requires acknowledged Paused, gives exactly one tick, and acknowledges at the next boundary. BED waits for the owned game to enable frame control until Stop, exit or disposal. Until its hello arrives, controls stay disabled with "This game hasn't enabled frame control yet." After the handshake, a five-second command acknowledgement timeout disables controls and reports unknown state; it does not kill an otherwise healthy game. Stop still uses the existing owned-process route. Legacy game/session implementations need not implement the optional IProjectFrameControlSession capability.

Transport v1 is a per-run local duplex asynchronous CurrentUserOnly pipe, fresh `bed-frame-<32hex>` endpoint and random64hex token. Child-only BED_FRAME_PIPE/TOKEN/VERSION are set for the game, cleared for builds; stdout/stderr stay plain. A 4-byte little-endian positive length precedes UTF8 JSON (2..8192 bytes, exact case-sensitive keys Version/Token/Id/Op/Frames/State, no duplicate/extra fields). Version1 and token must match. Hello: Id0, Ophello, Frames0, Staterunning. Commands: increasing Id1.., Oppause/step/resume, Frames0, Staterequest. Replies: matching Id, Opack, completed Frames, Statepaused/running as requested. Unknown/oversize/malformed/stale messages close control. It is a cooperative same-user channel, not a sandbox against malicious game code.

The Windows peer check uses [GetNamedPipeClientProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid) on the server SafePipeHandle (BOOL result, out ULONG PID). [CurrentUserOnly](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0) also matches elevation on Windows. The game simulation and document draft never cross this pipe.

The original standalone sample is samples/RoutePuzzle.Game, indexed in samples/README.md. Build it, copy its runtime output to an admitted project's game folder, and choose RoutePuzzle.Game.exe in Play settings with `--route brook,summit`. BED_PROJECT supplies its saved data root. This console demonstration applies route choices only on permitted20ms ticks, with no catch-up accumulator. Its optional --tick-log creates a new test receipt only; existing files are never replaced. It adds no window or audio dependency. The earlier RoutePuzzle.Authoring Try route mode remains an independent in-process demonstration.
