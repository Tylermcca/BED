# Make your first game with BED, Gum and an AI

Start with a small idea: catch a bright square five times. It moves to a new place after each catch. BED changes the title and winning target, Gum changes the screens, and the C# source controls how the game works. Change one thing at a time.

## What you need

- Windows 10 or 11, 64-bit, and the BED ZIP unpacked into a folder you can write to. Open **BED.exe** there. Follow **START_HERE.txt** if Windows asks for the Visual C++ Redistributable.
- For the assistant steps, your own AI app installed separately. You can also follow the commands below yourself in Windows PowerShell.
- For the screen step, [download and set up Gum](https://docs.flatredball.com/gum/gum-tool/setup). On Windows, unpack **Gum-win-x64.zip** and keep its folder together. Remember where **Gum.exe** is.
- For the final code step, install the **Windows x64 SDK** from [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). The SDK is the set of tools that builds C# code; the Runtime download alone cannot build it. The first build needs an internet connection to fetch the starter's included packages.

You can try the included player and change its saved settings before installing Gum or the SDK.

## 1. Create your own copy

In BED choose **File > New game...** (or **New game...** on the start screen). Choose a parent folder and a name, then **Create and open**. For example, choose Documents and call it My first game. BED creates that new folder. Choose a name whose folder does not already exist.

To use the command line instead, open Windows PowerShell. Go to the unpacked BED folder, where **BED.exe** and **bed-cli.exe** are. Replace both example paths below with your own full paths. Keep quotation marks around paths with spaces.

```powershell
Set-Location "C:/Tools/BED"
./bed-cli.exe new "C:/Games/My first game" --preview
./bed-cli.exe new "C:/Games/My first game"
```

Preview lists the files and writes nothing. The second command creates the folder. If a starter file is missing, keep the refusal and repair the BED installation; do not ask the assistant to invent a replacement player.

If you used the command line, now choose **File > Open project...** in BED and select **My first game**, not its data subfolder. Open **settings.json** in **Files** to see its settings.

Choose **Play** in BED. On the first run, BED asks to run the saved player command. Read the command and working folder, then choose **Allow this exact command and continue** for your new game. In the game, click Play or press Enter. Click the bright square or press Space to catch it. Five catches win. R starts again; Esc returns to the menu, and Esc from the menu closes the game. Try these controls before changing anything.

## 2. Change the winning target

The new folder contains **AGENTS.md**, its rules for an assistant, and **CLAUDE.md**, which points to those same rules. Give your assistant the new game's folder and ask it to read those files and **README.md**.

Ask: “Use the BED connection or bed-cli to change the winning target from five catches to three. Keep the title. Show the preview before saving.” To connect the project, use **Help > Connect an AI app**, choose your app and review the connection it offers. **Help > Use with an AI assistant** also has examples you can copy.

The same data change works without an assistant. In PowerShell, stay in the folder containing **bed-cli.exe** and replace each game path with yours:

```powershell
./bed-cli.exe --data "C:/Games/My first game/data" --schemas "C:/Games/My first game/schemas" get settings.json /goal
./bed-cli.exe --data "C:/Games/My first game/data" --schemas "C:/Games/My first game/schemas" set settings.json /goal 3 --preview
./bed-cli.exe --data "C:/Games/My first game/data" --schemas "C:/Games/My first game/schemas" set settings.json /goal 3 --save
./bed-cli.exe --data "C:/Games/My first game/data" --schemas "C:/Games/My first game/schemas" problems settings.json
```

`goal` is the saved winning target; `title` is the other setting. The replies should show a target of 5, a preview from 5 to 3, a saved change, then zero reported setting problems. A reply saying “couldn't check” means the check could not finish. Read its reason.

The file's rules allow a target from 1 to 50. Those checks cover the saved settings; try the game too. If it is already running, its target updates when you save. Press R to start from zero and catch the square three times. The title should stay the same.

Ordinary BED edits have Undo. Each command above starts and ends its own program, so there is no later command-line Undo for that saved change. Preview it first; to restore five, make another checked change to 5.

### Check without opening a game window

In PowerShell, go to **your game's folder** for the next command:

```powershell
Set-Location "C:/Games/My first game"
./game/StarterGame.exe --project . --headless --script start,collect,collect,collect
```

Here `.` means the current folder. `--headless` runs without a window, and the script starts a round and catches the square three times. With the unchanged code and the saved target of 3, the reply shows `Goal: 3`, `Score: 3` and `Won: true`. This checks that short sequence, not every possible play session.

### See a picture without a game window

Choose **Tools > Stop** if ordinary Play is still running, open **settings.json**, then choose **View > Game picture** and **Start picture run**. The panel shows the new game's menu. **Take picture** captures it again without advancing play or changing saved files; **Stop picture run** ends that run. To play the game, stop the picture run and use ordinary **Play**.

### Watch the score in Live values

Choose **Tools > Stop** if ordinary Play is still running, open **settings.json**, then open the command palette (**Ctrl+Shift+P**), choose **Show Live values**, then press **Start live values**. This runs without a game window. BED shows **Score**, **Winning target** and **Round won (1 = yes)**. Press **Try start (quarter second)**, then **Try collect (quarter second)** to watch the score change. The **Try back (quarter second)** button returns to the menu; **Try restart (quarter second)** starts a fresh round. Choose **Tools > Stop** when you finish. These trials do not change saved files. Edit the winning target through the checked settings as above.

## 3. Change the screen in Gum

1. In BED choose **File > Show Gum screens**, select **Menu** and press **Edit in Gum**. If BED asks where Gum is, select **Gum.exe** from the folder you unpacked. This opens your existing **ui/Game.gumj**; do not create a second project.
2. Select the **Menu** screen, then its **Tip** text. Change its **Text** to “Catch the light three times.” Save in Gum. Leave the running game on its menu and watch that tip change.
3. Select the **Play** screen and its **Target** square. Change **Blue** to **240** and save. Start a round in the game to see the square's new colour.

You can also open **ui/Game.gumj** directly in Gum. To change which Gum BED opens, use **Options > Locations > Gum**.

Keep the part names **PlayButton**, **BackButton**, **Target**, **Title** and **Score** for this exercise: the game uses those names to find its buttons and text. **Title** comes from the saved title setting; **Score** comes from the running game. Change the tip and colours in Gum, and change the winning target in BED. Renaming a part or changing how the square moves also needs a source change.

For an assistant doing screen work, use [Gum's own AI instructions](https://github.com/vchelaru/Gum/blob/main/CLAUDE.md). Ask it to work in Gum for screen changes and through BED's data tools for saved settings.

## 4. Change a rule in code, then build

The two saved settings cannot describe every new idea. For a code experiment, ask your assistant: “Read source/Game.cs. Make each catch add two towards the winning target, keeping the menu controls and the saved setting names. Keep the included package versions. Explain the small change and how to check it.”

Install the .NET 10 SDK first, as listed above. Open a new PowerShell window after installation and run `dotnet --list-sdks`; it should list a 10.x SDK. A text editor is enough to read the copied files in **source**; they do not need the BED source checkout.

Save the code and settings, then stop the old game. In BED choose **Tools > Build game**, then press **Build saved files** in the panel. Read the command and working folder BED shows, then use **Allow this exact command and build** to approve that build. BED remembers an approved command, so later builds may start without asking again. Build uses saved files and does not start Play. If it reports a source error, open the reported line, fix it and build again.

From your game's folder, the equivalent build is:

```powershell
dotnet publish source/StarterGame.csproj -c Release --self-contained true -o game
```

After a successful build, choose Play again. With the example rule above and a target of 3, the first catch counts as 2 and the second reaches 4 and wins. The same check without a window is:

```powershell
./game/StarterGame.exe --project . --headless --script start,collect,collect
```

The reply should show `Goal: 3`, `Score: 4` and `Won: true`. To save a picture of that frame, add `--shot picture.png` to that command. Read the result, look at the picture and try the controls yourself before deciding whether to keep the rule.

Keep the included Gum versions for this exercise. **game/THIRD-PARTY-NOTICES.txt** lists the licences and notices for the libraries included with the player; review it before changing those libraries or preparing a release.
