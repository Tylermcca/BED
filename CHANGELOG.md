# Changelog

Notable changes to BED are recorded here, following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0-rc.1]

Release candidate for 0.3: everything since 0.2.1-rc.1.

### Added

- **Prefabs.** Place a reusable piece more than once and point its named slots at objects in the level. Change one copy: each changed field is marked and has a **Revert** button. When the original changes, **Review update** shows every change before you accept it. Your local changes are kept, and an update that would break one is refused as a whole. Duplicate a copy, unlink it with **Break Link**, or delete it.
- **2D levels.** Edit flat levels in X and Y. Place sprites from imported PNG images by picker or by dragging, and set their size, pivot, drawing order and sampling. Move, rotate and scale them with handles or the keyboard. The flat view remembers its camera for each level.
- **Levels in games and builds.** A game that opts in can read saved prefab and sprite levels, and Export build carries them with their checked assets. The copied game runs without BED, the original prefab files or the source images.
- **Physics.** Set a level's gravity and fixed time step in **View > Physics**. Give objects static, dynamic or kinematic bodies with a box, sphere, capsule or a box fitted to the model's bounds, plus mass and friction, in the Inspector. **Bounds** shows collision shapes in the view. A separate physics sample runs the saved bodies in a game, in Play and in an exported build. Flat levels keep bodies in X and Y. Simulation never changes your saved level.
- **Sound preview.** Play and stop WAV and Ogg sounds in Assets, with their format and length, and a plain reason when a sound can't play.
- **Pause and Step.** A game that supports it can pause, advance exactly one tick and resume from Play (F9, F10 and F11), while its window keeps drawing.
- **Go to the problem.** A check finding, or a message the game prints in a set format, can take you to the saved field it names. **Back** returns you to where you were.
- **Recent projects.** Pin projects to the top of the list. A project that has moved stays listed until you remove it.
- **Engine-style layout.** The Outliner sits on the left, the view in the centre, the Inspector on the right and Files and Assets at the bottom. Play, Pause, Step and Stop sit in the middle of the toolbar.
- **Camera controls like other game editors.** Alt and the left button orbit, the middle button pans, Alt and the right button dolly, and holding the right button with W, A, S, D, Q and E flies. F frames the selection. An optional W/E/R keymap for move, rotate and scale is in Preferences.
- **A guided tour.** The first time you open a project, BED offers a short tour of a working example. You can skip it and reopen it from **Help > Take the tour**.
- **Report a problem.** **Help > Report a problem** shows exactly what a report contains, with personal paths removed. Copy it, save it, or open the public new-issue page in your browser to submit it yourself. Nothing is sent automatically. `bed-cli report` prints the same report.
- **Readability.** Choose 100%, 150% or 200% text and a high-contrast theme in Preferences; both are remembered. With the Windows theme setting, BED follows Windows high contrast.
- **Assistive tools.** Screen readers and other assistive tools can press buttons, tick boxes, choose items and set or step numbers, using the same checks as the keyboard and mouse.
- Search the Outliner by property names and values, edit a transform value across several selected objects at once, and have tables remember column widths, order and sort for each file.

### Changed

- **Renamed code.** BED's own libraries, projects and namespaces are now `Bed.*` instead of `Brutal.Authoring.*`. Rebuild game adapters, plug-ins and games that read levels against the renamed libraries. The programs are still `BED.exe` and `bed-cli.exe`, and BRUTAL's own package names are unchanged.
- **Settings folder.** Personal settings move to a new BED folder. The first start copies your old settings once and leaves the old folder alone. Saved project, data, level and prefab files are unchanged.
- **Plug-in API 0.2.** Plug-ins and adapters declare API 0.2; the 0.1 line is no longer accepted. The [compatibility notes](docs/api/compatibility_changes.md) list what changed and how to update.
- Text is larger by default and follows Windows' text size.
- The start screen fills the window.
- Tables size their columns to their content.
- Errors appear next to the field you edited, as well as in the status bar.
- Opening a level project shows its level straight away.
- In the 3D view, hold Shift while dragging to turn snapping off or on for that drag. Alt now orbits the camera. Alt still reverses snapping for arrow-key nudges.
- Large projects, big levels and saves do less repeated work, so the editor stays responsive.

### Fixed

- The 3D and 2D views could not be closed. They now close from their tab, their own window, the menu or the keyboard, and ask to save a changed level first.
- **Save all** and **Close project** refused in level projects. They now save or close levels and data files together, in one decision.
- Levels were listed in Files as files BED can't open. They now open.
- Play and **Save as** in levels showed leftover "not connected yet" messages. Play now runs the saved level. Save as explains that a level keeps its place in the project, and Save updates it.
- Copy, Paste and the other Edit menu commands now work in prefab and 2D levels.
- **Save all** with data and level files together needed extra clicks while checks ran. It now finishes in one click, and the editor stays responsive.
- Jumping to a search result or a new item in a long list didn't scroll to it. It now scrolls the item into view.
- The keyboard couldn't reach every control in the asset picker. It now can.
- `bed-cli --help` printed to the error stream. It now prints to standard output and exits successfully.
- Closing a project with several changed files now lists each file with its own Save and Don't Save choice.
- Undoing back to the saved state left the file marked as changed. It is now marked as saved.
- The prefab picker showed its "no prefabs" note even when prefabs were listed. It now shows the note only when there are none.
- A game that took a while to start could miss the Pause and Step connection. BED now waits for it.
- A broken sound file showed a decoder error. It now shows a plain reason.
- Move and rotate handles ignored the high-contrast theme. They now use its colours.
- With a command-line adapter file that holds several adapters, the wrong one could be picked. The one that recognises the project is now used.
- In the Inspector's long lists, the **Go to** box could shrink so a click missed it. It now moves to its own line when space is short.
- In Files, a row's **Repair** button could hang past the panel edge after pressing End. The whole row now scrolls into view.
- Choosing a disabled command in the command palette with the keyboard now says why it is unavailable.
- At larger text sizes, menus, links in Play messages and fix-it hints could be cut off. They now wrap or stay visible.

### Known issues

- Physics runs only through the separate physics sample, which uses a Windows-only native library. A game must opt in to physics.
- Builds and the download are Windows only.
- No sample level for prefabs, 2D or physics ships yet. The download's level sample is an older-format 3D level, and BED doesn't convert it. To try these features, your game's adapter must provide a new-format level.
- The OpenGL fallback view shows sprite outlines, not images.
- This is a release candidate. A full walkthrough with a real screen reader has not been done for this release.

## [0.2.1-rc.1] - 2026-11-09

Release candidate for 0.2: everything since 0.2.0-preview.

### Highlights

- Edit data projects whose files sit in folders and subfolders, with a game deciding which files belong to the project.
- A game can check a file in the background before it saves, and react once it has saved (for example, to rebuild a model from the saved design and refresh its preview).
- Edit long text, such as paragraphs of story, in multi-line text fields.
- See everything in the project that refers to the selected item, and jump to each one by keyboard.
- Replace a value across data files with a preview first, or apply one field to all selected table rows as one undoable edit.
- In levels: copy and paste objects between levels, group and ungroup them, rename them with every reference updated, and search the Outliner.
- `bed-cli scene` makes the same checked level edits from the command line.
- Open the 3D level sample, place a model, move it and save the level. The sample includes a textured crate.
- See models with multiple material parts, colours and textures in the 3D view. Browse texture thumbnails and model part counts in Assets.
- Press **F5** to Play your saved files with the game's own program, **Ctrl+F5** to Build and Play, and **Shift+F5** to Stop. Review the resolved commands in Play settings and read or copy the game's output in the Play console.
- Export a build from saved files, wait for its checks to pass, then copy it to a new or empty folder. The level sample's build includes its saved level and prepared assets and runs with BED closed. Export shows the file count, size and copied folder, and remembers the last destination for each project.
- Open the latest eight recent projects from the keyboard and change shortcuts in Keys. Follow the [keyboard guide](docs/guide/keyboard.md) for shortcuts and dialog controls.
- Read game output, labelled asset details and Export progress in the UI tree. Navigate Play settings and Export dialogs by keyboard.
- Enter supported check options in the Checks panel, with invalid choices blocking Run and changed choices marking old results stale.
- Find assistant connection examples in Help with a data file open. The start screen has a Getting started guide; the AI guide covers finding data, previewing edits and running checks.
- Open project folders using an adapter-recognised layout as well as the standard data and schema folders.

### Added

- Duplicate selected objects with **Ctrl+D**. When deleting an object, review the objects that refer to it before confirming; undo restores the change.
- Nudge number fields with **Up/Down**, **Shift** for larger steps and **Ctrl** for smaller steps.
- `bed-cli ai-setup` prints the same connection examples as the editor, with optional JSON output.
- The release check verifies the packaged AI guide and its setup examples alongside the level's Play and Export journeys.

### Changed

- A selected adapter's folder layout takes precedence over the standard layout; if the adapter returns no layout, the standard layout is used.

### Fixed

- BED no longer closes when a saved layout opens a panel in its own window.
- Saving a file you didn't change leaves its bytes, line endings and final newline exactly as they were.
- Keep Help visible on narrow menu bars by shortening optional heading and status text.
- Escape closes the start screen's Getting started guide and returns focus to its opener.
