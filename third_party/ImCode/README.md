# ImCode

Code editor component for Dear ImGui on [BRUTAL](https://github.com/ahwootech/BRUTAL) (.NET 10).
Use it as a single text area in your own UI or as a complete multi-tab editor, e.g. for in-game scripting, tools and mods.

![ImCode demo](docs/screenshot.png)

## Projects

| Project | Contents |
|---|---|
| `ImCode.Text` | UI-independent core: document, selection, undo, search, languages, vim engine |
| `ImCode` | `ImCodeEditor`, the editor widget (a code text area without surrounding UI) |
| `ImCode.Workspace` | `EditorWorkspace`: tabs, split view, find/replace, status bar, file handling, settings |

## Features

- Syntax highlighting with incremental, stateful tokenizers (C#, Markdown, plain text; others via `SimpleLanguage` or a custom `Language`)
- Hover tooltips, completion popup, diagnostics with squiggles
- Undo/redo, multi-line selection, mouse word and line selection, toggle comments, auto indent
- Tabs drawn on tab stops, zoom with Ctrl+mouse wheel, dark and light theme
- Optional vim mode (motions, operators, text objects, visual mode, `.` repeat, search, ex commands)
- Rebindable keys and custom commands

## Installation

ImCode is not published on NuGet. Add it as a git submodule and reference the project you need:

```sh
git submodule add https://github.com/aproposmath/ImCode external/ImCode
dotnet add MyApp.csproj reference external/ImCode/src/ImCode.Workspace/ImCode.Workspace.csproj
```

Reference `src/ImCode/ImCode.csproj` instead if you only need the widget, or `src/ImCode.Text/ImCode.Text.csproj` for the UI-independent core. Dependent projects and the BRUTAL packages from NuGet are pulled in automatically. Requires the .NET 10 SDK.

When cloning a repository that uses ImCode, fetch the submodule with `git clone --recursive` or `git submodule update --init`. ImCode keeps its own package versions, also if your repository uses central package management.

## Quick start

```csharp
using ImCode;
using ImCode.Languages;

var editor = new ImCodeEditor("int x = 1;") { Language = CSharpLanguage.Create() };

// every frame, inside an ImGui window
if (editor.Draw("code"u8))
    Console.WriteLine(editor.Text);
```

Full editor:

```csharp
using ImCode.Workspace;

var workspace = new EditorWorkspace { SettingsPath = "imcode-settings.json" };
workspace.LoadSettings();
workspace.Open("Program.cs");

// every frame
bool open = true;
workspace.DrawWindow("Editor"u8, ref open);
```

Requirements for the host:

- Call `ImString.SharedStorage.Reset()` once per frame (after rendering), the binding does not do it.
- Pass a monospace font via `editor.Options.Font` / `workspace.Font` if the default ImGui font is not used.

See [docs/API.md](docs/API.md) for the main types and extension points, and `samples/Demo` for a GLFW/OpenGL host.

```sh
dotnet run --project samples/Demo -- [files...] [--vim]
dotnet test
```

## Background

ImCode is inspired by and derived from [StationeersIC10Editor](https://github.com/aproposmath/StationeersIC10Editor), an ImGui based IC10 editor mod for Stationeers. The game specific parts were removed and the editor was restructured into the packages above.

## AI disclaimer

Most of the code and documentation in this repository was written by an AI coding assistant under the direction of the maintainer. Treat it like any other third-party code and review it before relying on it.

## License

MIT, see [LICENSE](LICENSE).
