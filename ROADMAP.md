# BED roadmap

BED is a free editor for games made with BRUTAL. It grows one complete job at a time, and a release ships only when everything in it works from start to finish on the samples and on a real game. There are no dates.

## What guides it

- **Easy first.** Open a project with no setup. Every error says what went wrong and what to do.
- **Safe edits.** Every change is checked, can be undone, and BED never saves over a file another program changed.
- **Your game stays yours.** A game adds its own rules and panels through a small adapter, and never needs BED to run.
- **Usable by everyone.** Everything works from the keyboard, text can be made larger, and screen readers can read it.
- **Works with your AI app.** An assistant makes the same checked changes you do, and only the ones you approve. BED has no AI inside and sends nothing anywhere.

## Released

- **0.2:** the data editor, 3D levels, assets with licence records, Play and Export.
- **0.3:** prefabs, 2D levels, physics and samples to try them.
- **0.4:** game screens made in Gum.
- **0.5 (out now):** start a new game in one step, work with your own AI app, see and try values in the running game, build and open your code, and paperwork for selling a game. See the [changelog](CHANGELOG.md).

## Next: 0.5.x

Fixes from real use, and a full check with a real screen reader.

## Later

Roughly in order, and shaped by what games made with BED need:

- Live values and game pictures for any game, not only ones set up for them.
- Material and lighting previews.
- The running game shown inside BED's window.
- Builds for platforms other than Windows.
- Prefabs inside prefabs.
- Animation preview.
- A simple profiler for the running game.
- Particles.
- Code changes while the game runs.
- More ways to draw, alongside Vulkan.

Each idea, where it came from and where it stands is in the [ideas list](docs/future.md). Version 1.0 comes once adapters and file formats stop changing.

## Not planned

Visual scripting, terrain tools, a shader graph, a full animation editor and built-in team tools. Use dedicated tools for those, and Git or similar for working as a team.
