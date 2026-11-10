# Try a project with Play

**Play** runs your game's program using the project's saved files. Open a project and a data file, then choose **Play** in the toolbar. If the program needs setting up, BED opens **Play settings** and explains what is missing.

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
