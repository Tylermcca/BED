# BED: a game editor for BRUTAL games

BED edits a game's data and its 2D and 3D levels (prefabs, sprites, physics), checks every change, plays the game and exports a copy, by hand or with an AI assistant.

[Start with the AI guide](docs/guide/using_bed_with_ai.md) for a sample walkthrough, connection examples and when to approve writes. With a project data file open in BED, choose **Help > Use with an AI assistant** for setup examples with your project's paths filled in.

New to BED? Begin with **[Start here](docs/guide/start_here.md)**: ten minutes, no programming.

BED edits the data your game reads: lists of items, enemies, recipes, levels and settings, kept as JSON files. Every edit is checked as you make it, so a typo or an impossible value is caught before it's saved, not when the game crashes.

It's made for games written in C# on the [BRUTAL framework](https://github.com/ahwootech/BRUTAL), but the data editor works on any folder of JSON files.

![BED editing the example project's recipes, with the File, Edit, View, Tools and Help menus](docs/images/bed-data-editor.png)

## Try it (Windows, 2 minutes)

1. Download **BED-win-x64.zip** from the [Releases page](../../releases). The current download is **0.3.0-rc.1**, a release candidate for 0.3.
2. Unzip it anywhere and double-click **BED.exe**. BED needs the Microsoft Visual C++ Redistributable (x64). If it is missing, install it from [Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe), then open BED again.
3. Click **Try the example**. The first time, BED offers a short guided tour. Take it, or skip it and reopen it later from **Help > Take the tour**.
4. Click a cell in the table, type a new value and press Enter.
5. Now type **-5** in the "minutes" column. BED refuses it and shows why next to the field.
6. Press **Ctrl+Z** to undo and **Ctrl+S** to save.

The zip also has a small checkers game made with BED. `START_HERE.txt` in the zip covers both.

### Try a 3D level

The download includes a level sample. **Try the example** opens recipes; open the level sample separately.

1. Choose **Open project** and select `samples\scene-starter` in the unzipped folder (or `samples/SceneStarterProject` if you [build from source](#build-from-source)). The level opens straight away. If it doesn't, open **level.json** from **Files**.
2. Press **Insert** outside a text field to open **Add object**, select the sample cube, pillar or textured crate, then choose **Add selected object**. Clear the selection first to place it on its own, or select an object to place it inside that group.
3. Select the new object and change **Position X** in the Inspector, or drag a move handle. Press **Ctrl+Z** to undo and **Ctrl+S** to save.
4. To look around, hold **Alt** and drag with the left button to orbit, drag with the middle button to pan, and press **F** to frame the selection.
5. Choose **Play** in the middle of the toolbar to run the saved files. If asked, choose **Save**, **Don't Save** (keep your edits and run the saved version), or **Cancel**. Configure **Tools > Play settings...** if needed; choose **Build and Play** when the program needs building, and approve the displayed command if asked. Choose **Stop** to end the run. See the [Play guide](docs/guide/playing.md).
6. Save, then choose **File > Export build** and **Build saved files**. When the build checks finish, enter a new or empty **Destination folder** and choose **Copy build to folder...**. See the [Export guide](docs/guide/exporting.md).

See [levels](docs/guide/levels.md), [assets](docs/guide/assets.md), [Play](docs/guide/playing.md) and [troubleshooting Play and Export](docs/guide/troubleshooting.md) for help. Play and Export build both work from your saved files.

Browse the [guide index](docs/guide/README.md) for everyday editing, levels, Play, Export and extension tutorials.

## Use it on your own data

On the start screen, under **Open a project**, choose:

- **Data folder:** your JSON files.
- **Schema folder:** a [JSON Schema](https://json-schema.org/) for each file. `wares.json` is checked by `wares.schema.json`.

Then click **Open**. If a file doesn't match its schema, BED still opens it and helps you fix it one edit at a time. It won't save over a file that another program changed after you opened it.

## What it does

- **3D levels.** Add models, move, rotate and resize them, group them, and undo or save your changes. The camera works like other game editors: Alt and the left button orbit, the middle button pans, and right button with W, A, S, D, Q and E flies.
- **Prefabs.** Place a reusable piece more than once, change one copy, and review an updated original before you accept it. Your local changes are kept.
- **2D levels.** Arrange sprites on a flat XY level, with size, pivot, drawing order and image sampling.
- **Physics.** Set a level's gravity and time step, and give objects static, dynamic or kinematic bodies with box, sphere, capsule or fitted-bounds shapes, mass and friction. A game that supports physics simulates them when it runs; BED never changes your level while it runs.
- **Assets.** Browse OBJ and glTF models, inspect their part counts, preview texture thumbnails and play WAV and Ogg sounds. Add models to a level; BED explains any problems that block placement.
- **Tables and forms** for your data, with undo and redo for every change, and errors shown next to the field.
- **Links between files.** Click a name to jump to the item it refers to, and see everything that refers to an item.
- **Play and Export.** Run the game from your saved files, pause it and step it one tick at a time if the game supports it, and export a copy that runs without BED.
- **Your game's own rules.** A small C# adapter adds checks, panels or a playable preview for your game. See [Writing an adapter](docs/guide/writing_an_adapter.md).
- **A command line** (`bed-cli.exe`) that makes the same checked edits, for scripts, AI assistants and screen readers.
- **Keyboard controls and assistive tools.** Text follows Windows' text size, with larger sizes and high contrast in Preferences. See the [keyboard guide](docs/guide/keyboard.md), [accessibility](docs/guide/accessibility.md) and the [screen-reader command-line walkthrough](docs/guide/using_a_screen_reader.md).
- **Problem reports.** **Help > Report a problem** shows exactly what a report contains. You can copy it, save it or open a new issue page; nothing is sent for you. See [reporting a problem](docs/guide/reporting_a_problem.md).

## What BED looks like

BED opens a game's data folder and shows each file as a table you can sort, filter and fill. Pick a row to edit it in the inspector, follow a link to the item it points at, and undo anything. With a game's adapter loaded, BED also checks every edit against that game's own rules and can start the game using your saved data.

| | |
|---|---|
| ![BED's start window](docs/images/bed-start-window.png) | ![BED's File menu open over the recipes table](docs/images/bed-file-menu.png) |
| The start window: open a recent project, start a new one, or try the example. | Standard Windows menus, with Alt keys and shortcuts. |
| ![BED showing the recipes sample as a table, in the dark theme](docs/images/bed-dark-theme.png) | ![BED editing a level: the object list, a 3D view with move handles, and the inspector](docs/images/bed-level-editor.png) |
| The recipes sample in the dark theme: a table of linked items, where the arrows jump to the ware each recipe uses. BED follows your Windows light or dark setting. | Editing a level: pick an object in the list or the 3D view, then move, rotate or scale it with the handles or the keyboard. |
| ![BED's asset picker listing the level sample's two models, with the cube's details](docs/images/bed-asset-picker.png) | ![BED showing the checkers sample's board](docs/images/sample-checkers.png) |
| Adding a model to a level: pick it from the project's assets, see where it came from and its licence, then place it. | The checkers sample: an adapter adds a board you edit by clicking, and a Play button. |

## Made with BED

I'm using BED on three or four games of my own at the moment, each a different kind of game with its own adapter. They're why BED exists, and they keep shaping what it needs to do next.

| | |
|---|---|
| ![A card game's table](docs/images/card-table.png) | ![BED on a card game's settings](docs/images/bed-card-settings.png) |
| A card game's table. | BED on the card game's settings, with a Play button for each game. |
| ![A space sim, docked at a station](docs/images/space-station.png) | ![BED on a space sim's ship augments](docs/images/bed-space-augments.png) |
| A space sim, docked at a station. | BED on the space sim's ship augments. |
| ![BED on another game's object catalogue](docs/images/bed-object-catalogue.png) | |
| BED on another game's catalogue of objects. | |

## Roadmap

0.2 added 3D levels, assets, Play and Windows builds to the data editor. 0.3 adds prefabs, 2D levels with sprites and physics; the current download, 0.3.0-rc.1, is its release candidate. Next, 0.4 works with the running game. See [the roadmap](ROADMAP.md) for what's done and what's left.

## Build from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/Tylermcca/BED.git
cd BED
dotnet run --project src/Bed.Launcher
```

To package your build as a Windows zip: `python tools/package_release.py`. It goes in `dist/`.

## Made with AI help

BED was written with the help of Claude Code and Codex. There's no AI inside the app, and it sends nothing anywhere.

## Licence

MIT, see [LICENSE.txt](LICENSE.txt). BRUTAL has its own licence; the notices for everything BRUTAL and BED use are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

See [CHANGELOG.md](CHANGELOG.md) for release notes and unreleased changes.
