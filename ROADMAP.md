# BED roadmap

BED is an editor for games built on BRUTAL. It started as a data editor: tables, forms, checks, links and undo for a game's JSON files. The plan is to grow it into a small, reliable game editor, adding one complete workflow at a time rather than a long list of separate panels.

There are no dates. A release ships when everything in it works from start to finish on the included samples and on a real game made with BED.

## Principles

- **Easy to use.** Open a project with no setup, find any command by typing its name, and every error says what went wrong, where, and what to do next.
- **Powerful when you need it.** Tables, forms, links, bulk edits with a preview, checks and a command line for big projects.
- **Extensible.** A game adds its own rules, panels, checks and Play button through a small C# adapter. Nothing about one game is built into the editor.
- **Safe edits.** Every change is checked before it's applied, can be undone in one step, and BED never saves over a file another program changed.
- **Games ship only what they need.** A game reads its data and levels through a small runtime package and never needs BED to run.
- **Usable by everyone.** Everything works from the keyboard, text can be made larger, and screen readers can read the interface.
- **Works with AI.** An AI assistant uses the same checked commands you do, and only makes changes you approve.
- **Open source.** MIT licensed, with original samples and a record of every third-party licence.

## 0.2: the first release (release candidate: 0.2.1-rc.1)

The goal: someone new to BED can open the sample level, add a model, position it, press Play to run the game, and export a Windows build that runs without BED.

### A data editor that works like a normal Windows program: done
- Start window with recent projects, New project, Open project and Try the example.
- File, Edit, View, Tools and Help menus, Alt keys and shortcuts, and a command search (Ctrl+Shift+P).
- Save / Don't Save / Cancel when closing with unsaved changes.
- Right-click menus, and tooltips that show shortcuts and explain why a command is unavailable.
- Light and dark themes that follow Windows, and sharp text at any display scaling.
- Panels you can move into their own windows; the layout is remembered and can be reset.
- Screen readers can read the whole editor in real windows.
- Tables, forms, links between files, checks, and undo for every edit.
- AI assistants can read, check and edit data through the command line and MCP.

### Levels: done
- Open a level and see its objects in a list and in a 3D view.
- Add, move, rotate, scale and parent objects with handles or the keyboard, with snapping and X/Y/Z constraints.
- Frame the selection, orbit, pan and zoom.
- Undo any change, and save, close and reopen a level with nothing lost.
- If a change can't be saved exactly (for example, skewing an object), BED explains why instead of quietly changing it.

### Assets: done
- Browse the project's models and drop them into a level by dragging or with the keyboard.
- OBJ and glTF models, with import settings (scale, which way is up) and reimport that keeps placed objects intact.
- Every asset records where it came from and its licence, and BED won't include an asset in a build if its licence doesn't allow it.
- Textured models in the 3D view, in Play and in builds.
- A project's models are ready when it opens.

### Play: done
- Press Play and the game starts in its own window, from your saved files. If you have unsaved edits, BED asks first.
- The game's messages appear in BED's console. Stop closes it cleanly.
- If the game crashes, BED keeps running and your unsaved edits are safe.

### Build: done
- BED runs the game's own build, checks every file it produces and the licences of everything included, and test-runs the result.
- A failed or cancelled build never touches your last good one.
- Export build copies a tested build to a new or empty folder you choose.

### Before release
- Done: an automated test of the whole workflow on the sample level, from opening it to exporting a build.
- Done: using BED for real work on one of my own games. All of that game's design data is now edited in BED.
- Done: hands-on testing in real windows, including the 3D view.
- Done: the 0.2.1-rc.1 release candidate for testers. The 0.3.0-rc.1 candidate builds on it.

## 0.3: prefabs, 2D and physics (release candidate: 0.3.0-rc.1)

The goal: build a level from reusable pieces, in 2D or 3D, give it physics, and play and export it.

### Prefabs: done
- Place a reusable piece more than once, and point its named slots at objects in the level.
- Change one copy. Changed fields are marked, and **Revert** puts one back.
- Review every change to an updated original before accepting it. Local changes are kept, and an update that would break one is refused as a whole.
- Duplicate, unlink or delete a copy, and copy and paste pieces between levels.

### 2D levels: done
- Flat levels in X and Y, with sprites from imported PNG images.
- Size, pivot, drawing order and image sampling, with handles, the keyboard and snapping.
- A camera remembered for each level.

### Physics: done
- Gravity and a fixed time step for each level.
- Static, dynamic and kinematic bodies with box, sphere, capsule or fitted-bounds shapes, mass and friction.
- Edit them in **View > Physics** and the Inspector, and see collision shapes in the view.
- A separate physics sample runs them in a game, in Play and in an exported build. Simulation belongs to the game and never changes the saved level.

### Levels in games and builds: done
- A game that opts in reads saved prefab and sprite levels.
- Export build carries them with their checked assets, and the copied game runs without BED or the original prefab files and images.

### Also in 0.3: done
- Sound preview in Assets.
- Pause and Step for games that support them.
- Check findings and game messages that take you to the saved field.
- Larger default text that follows Windows, high contrast, and assistive tools that can press buttons and set values.
- An engine-style layout and camera, an optional W/E/R keymap, and a first-run guided tour.
- **Help > Report a problem**, which shows exactly what a report contains and sends nothing by itself.
- Pinned recent projects.

### Before release
- Now: the 0.3.0-rc.1 release candidate for testers, then the release.

## 0.4: next, working with the running game

- **Inspect the running game.** See an object's current values while the game runs.
- **Live tweaks.** Try a change in the running game without changing your saved files.
- **Fixes from real use.** Fix what turns up when BED is used on real games and by testers.

## Later

Driven by what games built with BED actually need. Roughly in order of priority:

- Material and lighting previews.
- Showing the running game inside BED's window.
- Builds for platforms other than Windows.
- Nested prefabs.
- Animation preview.
- A basic profiler for the running game.
- Particles and game-UI layout.
- Code changes while the game is running (hot reload).
- More rendering backends alongside Vulkan.

Version 1.0 comes once the adapter interface and the file formats are stable.

## Not planned

To stay focused, BED won't include visual scripting, terrain tools, a shader graph, a full animation editor or built-in team collaboration. Use dedicated tools for those, and your usual version control (such as Git) for working as a team.
