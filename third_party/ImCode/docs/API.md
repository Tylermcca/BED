# ImCode API overview

All UI calls must happen on the ImGui thread. Positions are zero based (`TextPosition(Line, Col)`, columns in UTF-16 code units).

## ImCode.Text

| Type | Purpose |
|---|---|
| `TextDocument` | Lines of text. All edits go through `Replace(range, text)`, which raises `Changed` with a `TextChange`. |
| `EditSession` | Document plus selection, `UndoStack`, clipboard and editing commands (`Type`, `Backspace`, `NewLine`, `IndentLines`, `ToggleLineComment`, `Find`, `ReplaceAll`, `Undo`, ...). Group edits into one undo step with `using (session.BeginEdit()) { ... }`. Several sessions can edit one document if they share the `UndoStack`. |
| `IClipboard` | Clipboard abstraction, `MemoryClipboard` for tests. |
| `TextNavigation`, `TextSearch` | Word/char movement and plain text search on a document. |
| `ImCode.Languages.Language` | Base class for languages: `Tokenize(line, state, tokens)` plus optional `GetHover`, `GetCompletions`, `GetDiagnostics`, `MatchScore`, `LineCommentToken`. |
| `SimpleLanguage` | Configurable highlighter (keywords, comments, strings, numbers, operators) with keyword and document word completion. |
| `CSharpLanguage.Create()`, `MarkdownLanguage`, `PlainTextLanguage` | Built-in languages. |
| `LanguageRegistry` | Lookup by name or extension and content detection (`Detect(fileName, text)`). |
| `SyntaxHighlighter` | Incremental tokenization of a document, used by the widget. |
| `ImCode.Vim.VimEngine` | Vim emulation on an `EditSession`, driven by `HandleChar`, `HandleKey`, `HandleCtrl`. |

A language for a C-like syntax:

```csharp
var lua = new SimpleLanguage("Lua")
{
    Extensions = [".lua"],
    LineComment = "--",
    BlockComment = ("--[[", "]]"),
    Keywords = new() { ["local"] = TokenType.Keyword, ["function"] = TokenType.Keyword, ["end"] = TokenType.Keyword, ["nil"] = TokenType.Constant },
};
```

Override `GetHover`, `GetCompletions` or `GetDiagnostics` in a subclass to add language services. Diagnostics are requested after edits (debounced) and shown as underlines and in the hover tooltip.

## ImCode

`ImCodeEditor` draws and handles one editor:

- `bool Draw(id, size)`: call once per frame; a size of 0 fills the available space. Returns true if the text changed.
- `Text`, `Session`, `Document`, `Language`, `ReadOnly`, `Diagnostics`, `Highlights`
- `Options` (`EditorOptions`: font, size, zoom, tab size, line numbers, vim mode, completion, delays) and `Theme` (`ColorTheme.Dark()`, `ColorTheme.Light()`, per `TokenType` colors)
- `Focus()`, `ScrollToCaret()`, `ScreenPosition(pos)`, `TextPositionAt(screen)`
- `WordActivated` event (Ctrl+Click, vim `gf`), `Vim` and `VimStatus`

Keys map to command ids (`EditorCommands`). Add commands and bindings:

```csharp
editor.RegisterCommand("my.upper", e => e.Session.InsertText(e.Session.SelectedText.ToUpperInvariant()));
editor.KeyMap.Bind(new KeyChord(ImGuiKey.U, Ctrl: true, Shift: true), "my.upper");
```

## ImCode.Workspace

`EditorWorkspace` manages `EditorTab`s:

- `NewTab`, `Open`, `Save`, `SaveAs`, `SaveTo`, `SaveAll`, `Close`, `CloseAll(onClosed)` (asks for unsaved changes), `Activate`
- `Draw(id, size)` inside your window, or `DrawWindow(title, ref open)` with its own menu bar; `DrawMenus()` for a host menu bar
- `Settings` (`WorkspaceSettings`, JSON via `SettingsPath`, `LoadSettings`, `SaveSettings`, `ApplySettings`), `SaveSession`, `RestoreSession`
- `RegisterCommand(id, title, action)` and `KeyMap` for workspace shortcuts, `ShowMessage` for the status bar
- `IDocumentStore` to load and save from somewhere other than the file system (`FileSystemDocumentStore` is the default)
- `EditorTab.Split()` for a second view of the same document, `EditorTab.SidePanel` for custom content next to the editor
- Vim ex commands (`:w`, `:q`, `:e`, `:sp`, ...) are handled by the workspace
