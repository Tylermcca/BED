# BED 0.1 release notes

BED is an editor for game data stored in JSON. Edit tables and forms with schema checks, undo and redo, follow links between files, and add game-specific rules or previews through a C# adapter. It can help repair data that does not match its schema and checks for outside changes before saving.

## Try it

With the Windows x64 build, unzip **BED-win-x64.zip**, open **BED.exe**, and choose **Try the example**. Change a recipe, try a negative minutes value to see a refusal, and use Ctrl+Z to undo. Ctrl+S saves the desktop draft. The zip also includes a small checkers example.

BED needs the Microsoft Visual C++ Redistributable (x64). If it is missing, install it from [Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe), then open BED again.

Source builds need Windows and the .NET 10 SDK; see the [README](../../README.md).

## Highlights

- Fill in check options in the Checks panel. Missing required values or invalid choices block Run; changing the choices marks the panel's old result stale. Forms outside the supported flat scalar/enum shape still need the command line or an agent.
- Use **Help > Use with an AI assistant** (with a data file open) or the start screen's **Help: Use with an AI assistant** button for connection examples. The [AI guide](../guide/using_bed_with_ai.md) walks through finding data, previewing an edit and running a check.
- Open a project folder using the layout its adapter recognises, as well as the usual data and schema folders.

## Using an assistant

BED supplies local command-line and stdio MCP tools, not an AI model. Ordinary MCP value edits stay in the session draft until saved; preview changes nothing. One-shot CLI value edits need `--save` to write a file and are otherwise discarded when the command ends. File create, rename and delete operations can write immediately unless previewed. Keep the assistant's approval prompts enabled.

## Limits

Game-specific validation and previews need the matching trusted adapter. Separate project checks must be run explicitly: a valid edit does not mean every check passed. An adapter built into BED without a loadable DLL cannot be passed to the command line.

The desktop window has no accessibility tree. Keyboard controls do not provide desktop screen-reader support; use the [command-line route](../guide/using_a_screen_reader.md). Assistant apps have their own setup and privacy settings.
