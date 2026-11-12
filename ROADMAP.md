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

## Done so far

- **0.2 (0.2.1-rc.1):** a data editor that works like a normal Windows program; 3D levels with handles, snapping and undo; model and texture assets with licence records; Play from saved files; tested Windows builds and Export build.
- **0.3 (0.3.1, out now):** prefabs, 2D levels, physics, levels in games and builds, a big usability pass, and samples to try them. Details below.

## 0.3: prefabs, 2D and physics (out now as 0.3.1)

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

### Fixed in 0.3.0-rc.1
- The 3D and 2D views couldn't be closed. They now close from their tab, their own window, the menu or the keyboard, and ask to save a changed level first.
- **Save all** and **Close project** refused in level projects. They now save or close levels and data files together.
- Levels were listed as files BED can't open. They now open.
- Leftover "not connected yet" messages on level Play and Save as are gone.
- Saving data and level files together needed extra clicks. It now finishes in one click.
- Jumping to a search result or a new item in a long list didn't scroll to it. It now does.
- The keyboard couldn't reach every control in the asset picker. It now can.
- Undoing back to the saved state left a file marked as changed. It's now marked as saved.
- A game that was slow to start could miss the Pause and Step connection. BED now waits for it.
- Text was cut off in places at larger sizes. It now wraps or stays visible.
- The full list is in the [changelog](CHANGELOG.md).

### Samples and fixes in 0.3.0-rc.2: done
- **Samples for the new features.** Two small original samples ship in the download: a flat sprite level with a prefab placed twice, and a 3D level with a crate prefab, a floor and a paddle, all with physics bodies. The Start here guide walks through both. Both are small games that play in their own window: a garden to run and jump through, and an island to drop crates on.
- **Small fixes from the first real-window checks.** The welcome and tour boxes no longer cover the Files panel or **Used by** when a level opens.

## Next, 0.4: one thing done well: the running game

0.4 starts small, with one game and a handful of values, and grows only once that works every time.

- **See and try values in the running game.** While the game runs, see a few of an object's values as they change, try a different value, and see the game respond. Your saved files don't change unless you choose to keep the new value.
- **Open your game's code.** Open the game's code in your own code editor from BED, and see build errors in BED in plain words, with a click that takes you to the right line.
- **Fixes from real use.**

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

### Plain words in 0.3.0-rc.3: done

- Editor text says what it means instead of using programming terms, and the level Outliner marks prefab copies with an icon, a colour and a dot instead of bracketed words.
