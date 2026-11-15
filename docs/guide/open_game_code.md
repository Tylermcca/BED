# Open game code

Use your own code editor alongside BED.

1. Choose **Tools > Open game code**. BED opens the project's one top-level solution, or its folder when there is no single solution to choose.
2. To choose the app, open **Options > Locations > Code editor**. Choose **Windows choice**, **VS Code**, **Visual Studio** or **Rider**.
3. For a named editor, leave the program empty to find an installed copy in the usual places, or browse to its program. BED remembers this choice on your computer. It does not install an editor.

[Build errors](build_game.md) appear in the **Play console**. Click an error's source link to open the current file at its reported line in the same chosen editor. VS Code and Rider also receive the reported column. If a file changed after the build, the error's old line may now point somewhere different.

**Windows choice** uses the usual app for that file. Windows has no shared way to pass a line number to every app, so BED opens the file and shows which line to visit yourself. Choose one of the named editors for a jump to the line.

If the chosen program is missing, check its location. If a reported source file is missing or outside the project, BED explains why it cannot open that link. Opening code does not save, build or run your game.

Next: [build again](build_game.md) after fixing and saving the source in your editor.
