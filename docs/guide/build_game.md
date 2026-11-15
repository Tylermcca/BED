# Build the game

**Tools > Build game** prepares the saved game without starting it. Building does not save open edits or show that the game plays correctly.

1. Save your edits and stop Play.
2. Choose **Tools > Build game**, then **Build saved files**.
3. If BED asks, read the exact command and working folder. **Allow this exact command and build** lets that command run on your computer. Allow it only for a project you trust.
4. Read the result and the **Play console**. For each reported error, BED shows what went wrong, its file and line when supplied, and something to try.
5. Fix the error, save, and build again. An **Open source at line** link opens that file in your own editor; see [open game code](open_game_code.md).

If the project has no build command, open **Tools > Play settings...** and follow the game's build instructions. BED does not invent a command. Choose **Cancel build** to stop a build in progress.

A successful build means the build finished without a reported error. Try **Play** afterward to check the game itself. **Build and Play** is the separate command that builds and then starts the game.

An AI assistant can request the same build and read its errors. It cannot grant the command permission or save your edits as part of that build request. If permission is missing, approve the exact command in BED yourself, then ask again.

Next: [Play](playing.md), [let an AI look at the game](see_the_game.md), or [Export](exporting.md).
