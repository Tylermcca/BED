# BED 0.5.0-preview.1

Release-page text for the 0.5.0-preview.1 preview, published 2026-11-15. The stable download is still 0.4.0.

This preview gives you a playable starting point and tools for changing a game with your own AI assistant. You can build saved code, see what a supported game is doing, try a value and choose whether to keep it.

## Start with a playable game

Choose **File > New game** and an empty folder. BED creates a small collection game with a menu and play screen made in Gum, editable settings and C# source. Its Windows player is included, so you can try it before installing an SDK. Saving settings or a screen updates the running game.

The [first-game guide](../guide/first_game_with_ai.md) walks through changing the goal, editing a screen in Gum and rebuilding a code change. The new folder includes rules for your assistant: use BED's checked data tools, show a preview, keep each change undoable and save explicitly.

The bundled checkers sample also has a standalone player. It loads the saved board without BED's editor libraries.

## Work with your own assistant

**Help > Connect an AI app** previews the settings change before adding a connection. BED backs up existing settings, and you can remove the connection later. The assistant is a separate app.

Asset permissions apply to AI requests for data, files, pictures and live values. If an asset is forbidden for AI use, its permission is unknown, or the record cannot be checked, BED refuses the request without sending project content. Its owner can review the [asset record in BED](../guide/content_rights.md); the assistant cannot grant itself permission.

Open **View > Game picture** to see a supported game without opening its window. Stop ordinary Play first, choose **Start picture run**, then **Take picture** whenever you want a fresh picture. **Stop picture run** ends it. The game pauses between requests. Games made with **File > New game** support this panel too; see [game pictures](../guide/see_the_game.md).

In the bundled garden and physics island, an assistant can ask for a game picture, try a short key hold and describe what changed, without opening a game window. The island also lets it move the camera and use **jump** to lift a crate. Press **Space** to try the jump in the island's own player, or use **jump** in **Live values**. Games choose which of these tools they support.

The **Live values** panel shows values a game shares while it runs. Try jump strength in the garden, or inspect body height, speed, gravity and mass on the island. A trial affects that run. **Keep this value** writes a confirmed setting into saved data.

## Build, open code and export

**Tools > Build game** builds saved files without starting Play. **Tools > Open game code** opens your chosen editor, and a build error can take you to its source line. BED asks before running an unapproved build command.

Export now includes credits, notices, licence and logo files, a release checklist and an AI-content worksheet for you to review. See [checking a build before selling](../guide/selling_a_build.md).

## Easier to read and arrange

Panel edges and tabs are clearer in every theme, with a visible drag grip and drop targets. A game can request a large preview in the middle; your saved layout still wins. Pick lists can show friendly names supplied by the game's maker while preserving the saved values.

## Plans you can follow

The [future-ideas register](../future.md) lists later requests, where each came from, the support it would need and its current status. It separates working features from plans without promising delivery dates.

## Fixed

- **Edit in Gum** waits for the copy BED already started instead of opening another while it loads.
- An assistant's read tools say **couldn't check** when an input is missing, instead of returning an apparently empty result.
- Automated read checks now match the **couldn't check** wording for missing or unreadable inputs.
- **New game...** on the start screen works before a project is open. Sample rows use names such as **2D garden** and **Physics island** for mouse, keyboard and screen-reader users.
- The shorter **Start live values** button fits in a narrower window; the explanation that no game window opens stays beside it.
- Games that do not share live values no longer get an empty **Live values** panel in the standard layout.
- Build and live-value commands are ready as soon as a file opens.
- Building the Windows app no longer breaks a following test build with missing documentation-file errors.

## Before you try it

The build is for Windows x64. BED needs Microsoft's Visual C++ Redistributable (x64), as explained in the ZIP's **START_HERE.txt**. Gum and your assistant app are installed separately. Rebuilding the starter's source needs the .NET 10 SDK.

Game pictures and live values need support from the game. Export still needs your review before release. The OpenGL fallback shows sprite outlines rather than images, and a full walkthrough with a real screen reader is still outstanding.

When the preview is published, unpack its ZIP into a new folder and open **BED.exe**. Try **File > New game** or **Try the example**. Tell us what went wrong through **Help > Report a problem**; include what you were trying to do and what happened.
