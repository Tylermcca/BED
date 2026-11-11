# Using a screen reader

The Windows x64 desktop window now has an AccessKit accessibility tree and checked actions for built-in wrapped controls. See [Accessibility](accessibility.md) for keyboard routes, text size, high contrast and the limits of the tested assistive-tool support. A complete real screen-reader walkthrough after those changes has not been completed.

This page describes the `bed-cli` command-line fallback for data tasks. Its text output can be read through your terminal. It uses the same agent API as AI tools. The historical audit in `docs/research/accessibility_audit.md` describes earlier gaps; it is not the current desktop support statement.

## What you can do

Every call checks the same schemas as the window, and nothing changes on disk without `--save`. It checks the game's
own rules too only when you name the game's adapter with `--adapter <the adapter's DLL>` (see Getting started). Without
it, you're editing the plain data against its schemas, and a change the game would refuse can still be saved.

| Task | Call |
|---|---|
| See what's in the project: files, lists, counts, problems | `describe` |
| Find items by name or any field | `find <file> <list> [text]` |
| Read a value, its item's name, and where an id leads | `get <file> <at>` |
| List what's wrong | `problems [file]` |
| See what refers to an item | `refs <file> <at>` (with `--map`) |
| Change a value | `set <file> <at> <json> --preview`, then again with `--save` |
| Add or remove a list item | `insert <file> <list> <index> <json>`, `remove <file> <at>` |
| Make, rename or delete a file | `create <file> <schema>`, `rename <file> <new name>`, `delete <file>`, each with `--save` to do it |
| Fix a file that doesn't fit its schema | `repair <file>` |

Places are JSON Pointers: `/wares/3/price` is the price of the fourth ware.

## Getting started

Build it once from the repository root:

```
dotnet build src/Bed.Agent.Cli -c Release
```

Then call it with your game's data and schema folders:

```
dotnet src/Bed.Agent.Cli/bin/Release/net10.0/Bed.Agent.Cli.dll --data <data folder> --schemas <schema folder> describe
```

If your game has an adapter, add it, so every call uses the game's rules and checks as the
window does:

```
dotnet src/Bed.Agent.Cli/bin/Release/net10.0/Bed.Agent.Cli.dll --data <data folder> --schemas <schema folder> --adapter <the adapter's DLL> describe
```

`--read-only` can't be used with `--adapter` yet.

Each answer to a call that runs is JSON with a `next` field saying what to do next, refusals included. When the
arguments, folders or adapter are wrong, nothing runs and the reason is one plain line of text instead.
Exit code 0 means done, 1 refused or problems found
(the answer says why), and 2 means the arguments or folders were wrong.

## Tips for screen readers

- Use a terminal your reader follows well (Windows Terminal with NVDA, or Narrator's scan mode).
- `find` lists 50 items unless you pass `--limit N`. Narrow with text rather than reading long lists.
- Always run a change with `--preview` first: it says what would change and whether it's allowed, and changes nothing.
- A game whose plug-in serves `--mcp` can also be driven from any MCP client, including AI assistants, with
  the same rules.
