# Follow a problem from Play

## Open the reported field

A game can print a message that links to one of the project's saved files. In **Play console**, find the message and activate its **Open** link with the mouse, or focus the link with the keyboard and press Enter. BED opens the file and selects the reported item or field. **Back to previous location** returns to your earlier selection.

Only files admitted by the current project can open. A missing file or item produces a reason. If a draft or saved input has changed since Play began, run Play again before following the location. That also applies after using **Don't Save** to run with an unsaved draft. A message points into the saved version the game read; it never applies an edit for you.

Ordinary output and malformed diagnostic lines remain console text. **Copy all** keeps the original output, including the diagnostic line. See [Play](playing.md) for running saved files and stopping the program.

## Emit a diagnostic from a game

Reference `Bed.Runtime` and print the helper's result to stdout or stderr:

```csharp
using Bed.Runtime;

Console.WriteLine(ProjectDiagnostic.Format(
    "data/items.json", "/items/0/name", "Choose a nonempty name."));
```

This produces one UTF-8 line:

```text
BED_DIAGNOSTIC_V1 {"file":"data/items.json","pointer":"/items/0/name","message":"Choose a nonempty name."}
```

`Format` performs no I/O. It returns the line for your game to print and throws `ArgumentException` for invalid paths, pointers, Unicode or an oversized result. Other languages can write the same line directly.

## Line contract

| Part | Rule |
| --- | --- |
| Prefix | Exactly `BED_DIAGNOSTIC_V1 `, including the trailing space. |
| JSON | Exactly three string fields: `file`, `pointer`, `message`. Key order may vary; duplicate or additional fields are refused. |
| Length | At most 4096 UTF-8 bytes for the entire line, excluding its line ending. Byte length includes the prefix and JSON escaping. |
| File | Relative to the project root, available to the child as `BED_PROJECT`. Use forward slashes, with no rooted path, backslash, colon, empty segment, `.` or `..`. It is relative to that root even if the working folder or editor data folder differs. |
| Pointer | RFC 6901 JSON Pointer: empty means the document root; `/items/0/name` selects a field in the first item. Escape `~` in a key as `~0`, and `/` in a key as `~1`. |
| Message | Human-readable text encoded as a JSON string. Escape quotes and line breaks inside the string so the output remains one physical line. |

A location is advisory. It does not signal game readiness, transfer project state or bypass file admission, draft ownership or validation. Check findings use the same checked navigation and Back behaviour; a stale Play location asks you to run the game again.
