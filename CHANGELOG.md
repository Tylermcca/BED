# Changelog

Notable changes to BED are recorded here, following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

No unreleased changes recorded.

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
