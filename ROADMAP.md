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

## 0.2: the first release (in progress)

The goal: someone new to BED can open the sample level, add a model, position it, press Play to run the game, and export a Windows build that runs without BED.

### A data editor that works like a normal Windows program: done
- Start window with recent projects, New project, Open project and Try the example.
- File, Edit, View, Tools and Help menus, Alt keys and shortcuts, and a command search (Ctrl+Shift+P).
- Save / Don't Save / Cancel when closing with unsaved changes.
- Right-click menus, and tooltips that show shortcuts and explain why a command is unavailable.
- Light and dark themes that follow Windows, and sharp text at any display scaling.
- Panels you can move into their own windows; the layout is remembered and can be reset.
- Screen reader support (being checked in real windows before release).
- Tables, forms, links between files, checks, and undo for every edit.
- AI assistants can read, check and edit data through the command line and MCP.

### Levels: done
- Open a level and see its objects in a list and in a 3D view.
- Add, move, rotate, scale and parent objects with handles or the keyboard, with snapping and X/Y/Z constraints.
- Frame the selection, orbit, pan and zoom.
- Undo any change, and save, close and reopen a level with nothing lost.
- If a change can't be saved exactly (for example, skewing an object), BED explains why instead of quietly changing it.

### Assets: mostly done
- Browse the project's models and drop them into a level by dragging or with the keyboard.
- OBJ and glTF models, with import settings (scale, which way is up) and reimport that keeps placed objects intact.
- Every asset records where it came from and its licence, and BED won't include an asset in a build if its licence doesn't allow it.
- Textures in the 3D view.
- In progress: full support for textured models, and loading a project's assets when it opens.

### Play: done
- Press Play and the game starts in its own window, from your saved files. If you have unsaved edits, BED asks first.
- The game's messages appear in BED's console. Stop closes it cleanly.
- If the game crashes, BED keeps running and your unsaved edits are safe.

### Build: in progress
- BED runs the game's own build, checks every file it produces and the licences of everything included, and test-runs the result.
- A failed or cancelled build never touches your last good one.
- In progress: the Export build command, and copying a tested build to a folder you choose.

### Before release
- An automated test of the whole workflow on the sample level, from opening it to exporting a build.
- Hands-on testing in real windows, including with a screen reader and in the 3D view.
- Using BED for a real task on one of my own games.
- A preview build for testers, then the release.

## 0.3: next

- **2D.** A flat view, sprites with sizes and pivots, and 2D levels with the same place, parent, snap and save tools.
- **Prefabs.** Place the same piece many times, change one copy, see what you changed and undo it. Editing the original never overwrites changes you made to a copy.
- **Sound preview.** Play and stop sound files in the editor.
- **Pause and step.** Pause the running game and advance it one frame at a time.
- **Inspect the running game.** See an object's current values while the game runs.
- **Live tweaks.** Try a change in the running game without changing your saved files.
- **Jump to the problem.** A game error or failed check takes you straight to the object or field that caused it.

## Later

Driven by what games built with BED actually need. Roughly in order of priority:

- Material and lighting previews.
- Showing the running game inside BED's window.
- Builds for platforms other than Windows.
- Nested prefabs.
- Animation preview.
- A basic profiler for the running game.
- Collision and physics settings.
- Particles and game-UI layout.
- Code changes while the game is running (hot reload).
- More rendering backends alongside Vulkan.

Version 1.0 comes once the adapter interface and the file formats are stable.

## Not planned

To stay focused, BED won't include visual scripting, terrain tools, a shader graph, a full animation editor or built-in team collaboration. Use dedicated tools for those, and your usual version control (such as Git) for working as a team.
