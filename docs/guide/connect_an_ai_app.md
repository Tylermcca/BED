# Connect an AI app

BED can connect the open project to Claude Desktop, Claude Code, Codex or Cursor. Open your project and a file first. You need the AI app installed separately.

1. Choose **Help > Connect an AI app**.
2. Choose your app's name. Read the file name and the proposed change. Opening this panel changes nothing.
3. Choose **Connect** to add this project's connection. If the app already has a settings file, BED makes a backup beside it before writing. Other connections stay in place.
4. Restart the AI app. Ask it to describe the open project's files before asking for edits.

Try: “Find the jump setting and explain it. Do not change anything.” Then try: “Preview my change and show me what it affects. Wait for my approval before applying or saving it.” Your assistant should use the project's checks and tell you what they found.

To disconnect, open the same panel and choose **Remove connection**. BED removes this project's entry; backups stay beside the app's settings file. If the file changed elsewhere, choose **Refresh preview** and read the change again. If BED cannot safely make it, it explains why instead of replacing the file.

The app may use a different personal profile or project setting; BED says which settings file it changed. Read the AI app's privacy settings before sharing project material. Connecting an assistant lets it request file changes; it does not mean you approved every edit. Some file actions write at once, so ask for a preview before approving them.

Next: [build the game](build_game.md) or [ask the AI to look at it](see_the_game.md).
