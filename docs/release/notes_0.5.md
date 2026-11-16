# BED 0.5.0

BED 0.5 helps you start a game, work on it with your own AI app, and see inside it while it runs. It's the 0.5 preview from last week, now the main download.

**Download:** BED-win-x64.zip below. Unzip it into a new folder and open **BED.exe**. Windows may say it doesn't know the publisher: click **More info**, then **Run anyway**.

## New

- **Start a game in one step.** **File > New game** makes a small game you can play straight away, with a menu and play screen made in Gum, settings you can edit, and its C# code. The [first-game guide](https://github.com/Tylermcca/BED/blob/main/docs/guide/first_game_with_ai.md) walks you through changing it.
- **Work with your own AI app.** **Help > Connect an AI app** connects Claude, Codex or Cursor to your project after showing you the change. Your assistant makes the same checked, undoable edits you do. BED has no AI inside and sends nothing anywhere.
- **See and try values while the game runs.** **Live values** shows values from a running game and lets you try a new one. **Keep this value** saves it; otherwise your files don't change.
- **See the game without its window.** **View > Game picture** takes a picture of a running game whenever you ask. An AI assistant can do the same, press keys, and see what changed.
- **Build and open your code.** **Tools > Build game** builds your code, and a build error takes you to its line in your own code editor.
- **Get ready to sell.** Export adds the credits, licence notices and a checklist to review before you sell a game.
- **Clearer panels.** Tabs and panel edges are easier to see and drag in every theme.

## Fixed

- Pressing **Edit in Gum** while Gum was still opening started a second copy. It now waits for the first.
- An assistant's checks could look clean when a file couldn't be read. They now say they couldn't check, and why.
- **New game** on the start screen didn't work before a project was open. It now does.
- The **Start live values** button didn't fit in a narrow window. It now fits.
- Games that don't share live values showed an empty **Live values** panel. It's now hidden for them.

## Known issues

- Live values and game pictures work only in games set up for them, such as the included garden and physics island.
- A full check with a real screen reader hasn't been done yet.
- The OpenGL fallback view shows sprite outlines, not images.
- Windows only. Gum and your AI app are installed separately.

**Please try to break it.** Tell us what goes wrong with **Help > Report a problem**.
