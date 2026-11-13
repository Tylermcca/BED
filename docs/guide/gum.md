# Gum screens

[Gum](https://docs.flatredball.com/gum) is a free tool for designing game menus and screens. If your game's folder has a Gum project in it, BED checks your screens and can open them in Gum for you. You design the screens in Gum; BED never changes them.

## What you need

- A BED project with a Gum project inside it: a `.gumx` or `.gumj` file in the project's folder or in a folder just inside it.
- To open screens from BED, Gum itself. You don't need Gum for the checks.

## Check your screens

1. Open your project in BED.
2. Open the **Checks** panel. It lists **Gum screens** next to BED's other checks.
3. Run it. BED asks Gum's own checker (the same one as `gumcli check`) to look at every screen in the Gum project. It lists anything a screen uses that isn't in the project's folder (a picture, a font, a part made in Gum, a picture a font or animation file needs) and anything else Gum would warn about, such as a part that contains itself or a file name whose capital letters don't match the file on disk.
4. Each problem says which screen it's in, what's wrong, and what to do, such as putting the file back or changing it in Gum. Problems that belong to no one screen, such as one of Gum's built-in types or a translation file, are listed once for the whole project.
5. Where Gum has a code for the problem (GUM0004, say), the problem carries it with a link to Gum's own help page for it. Hold the pointer over a problem to see them.

### Fonts

What a font needs depends on how your game draws Gum's text, so BED reads the Gum package your game's project file names:

- **BRUTAL games (Gum.Brutal) and other Skia games** draw `.ttf` and `.otf` fonts themselves. They never use Gum's ready-made letters, so BED doesn't ask for them. A `.fnt` font file can't be drawn this way, so BED warns about one: that text would show in a stand-in font.
- **MonoGame, KNI, FNA, raylib and sokol games** draw Gum's ready-made letters from the Gum project's `FontCache` folder. BED warns when the letters for a font and size aren't there. To make them, run `gumcli fonts` on your Gum project, or start Gum with `--rebuildfonts` and the project.
- **Games that make letters while they run (KernSmith)** need neither.

If BED can't tell from your project files which of these your game uses, it says so and doesn't check fonts, rather than guess.

### Agents

BED's command line and its connection for AI agents (`checks` and `check`) can run the **Gum screens** check too, whenever a Gum project sits next to the data. Each problem comes with Gum's code and help page, so an agent can look it up.

## See your screens

Choose **File > Show Gum screens**. The **Gum screens** panel shows each screen as your game will draw it; pick one from the list at the top. When you save a screen in Gum, the picture updates by itself, and BED says **Updated from Gum just now**.

### If parts contain each other

A part can accidentally include itself, directly or through another part. BED now shows the rest of that screen and says which parts form the loop. Open the screen in Gum and remove the link that closes the loop; save to see the corrected picture in BED. The **Gum screens** check lists the problem too.

### Saving changes and pictures

BED uses Gum preview 6 to follow saves. Gum watches the project and updates the picture; there is no extra reload setup to do in BED. This also works when a screen has a loop: the parts outside it still draw and follow saves.

The picture file types BED checks come from the Gum version it includes. A file type Gum does not support appears as a problem in the **Gum screens** check; use a picture type Gum can read.

## Edit your screens in Gum

1. Press **Edit in Gum** in the **Gum screens** panel, or choose **File > Edit screens in Gum**.
2. BED looks for Gum in the usual places. If Gum already has your screens open, BED brings it to the front instead of opening a second copy. If BED can't find Gum, it asks you to pick `Gum.exe`, and remembers it.
3. Make your changes in Gum and save. The picture in BED updates, and so does a game that is running and shows the screens.

To change where Gum is later, open **Options** and click **Locations** to open that section. Next to **Gum**, choose **Browse...** to pick it, **Find it for me** to look again, or **Back to normal** to forget your choice.

## If it doesn't work

- **"Gum isn't on this computer yet"**: Gum is free. Download it from [docs.flatredball.com/gum](https://docs.flatredball.com/gum), then press **Edit in Gum** again and show BED where `Gum.exe` is.
- **"saved by a newer Gum than BED can read"**: your Gum is newer than this BED. Your screens are fine; open them in Gum.
- **No Gum screens in the Checks panel**: BED only looks in the project's folder and the folders just inside it. Move the Gum project there.
- **"This project has 2 Gum projects"**: BED doesn't pick one for you. Open Gum on its own and choose the project from its File menu.
- **"BED can't tell how your game draws the text"**: add your game's Gum package (such as Gum.Brutal.OGL or Gum.MonoGame) to a project file in the project's folder or a folder just inside it, or keep the built game there.
