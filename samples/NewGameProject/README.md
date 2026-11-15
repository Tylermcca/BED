# Your first game

This is Catch the lights, a small original game you can change. Choose Play in BED, then click Play in the game or press Enter. Catch the bright square with the mouse or Space; it moves after each catch. R starts again; Esc returns to the menu and closes from there.

Open data/settings.json in BED to change the title or the winning target. Preview, check and save the edit; the running game reads saved changes. Ask an assistant to use bed-cli or your BED connection for these data edits.

Open ui/Game.gumj in Gum to change the Menu and Play screens. Save a screen and the running game reloads it. Try changing the menu tip or a button's colour. Use Gum's own AI instructions for screen work: https://github.com/vchelaru/Gum/blob/main/CLAUDE.md

AGENTS.md contains the project rules. CLAUDE.md points to it. Read FIRST_GAME.md in a created game for the full walkthrough.

The included Windows player runs on its own. To change its C# code, install the .NET 10 SDK (the build tools), save the source change and stop the old game, then choose Tools > Build game in BED. The declared build uses dotnet publish; review and allow that command when BED asks. Keep the Gum versions listed in the copied C# project file for this exercise. Ask the assistant to check Gum's own instructions when changing screen code.

FIRST_GAME.md gives the exact build and headless commands for your new folder. A headless check opens no window and reports the actual screen and score. It can also save the same frame the window shows. These observations do not decide whether your game is finished.

INVENTED: the click-and-collect game and its two screens. MIXED: Gum's standard element definitions reused from BED's self-authored fixture and the existing sample's Windows pixel-window pattern. See LICENSE.txt and game/THIRD-PARTY-NOTICES.txt for the bundled runtime's notices.
