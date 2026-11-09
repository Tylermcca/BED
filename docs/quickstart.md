# Quick-start: edit a board without a window

You need the .NET 10 SDK and a checkout of this repository. Open PowerShell at its root. The first build restores the already-declared packages, so a fresh machine needs access to their configured feeds. Later runs can use the local package cache. No BRUTAL installation, game checkout or graphics window is needed for this sample.

This guide and its example journal are INVENTED, self-authored examples of the public first-slice contract. They contain no game data. Keep your first trial in the new `.quickstart` folder. If that folder already contains an earlier trial, choose a new empty folder and adjust all paths consistently.

Build the command-line sample:

```powershell
dotnet build samples/Toy/Toy.csproj --nologo -v:q
```

It reports `Build succeeded.`, zero warnings and zero errors. The elapsed time varies. Set the built program's path and make a scratch folder:

```powershell
$toy = "samples/Toy/bin/Debug/net10.0/toy.dll"
New-Item -ItemType Directory -Path .quickstart | Out-Null
```

These commands print nothing. Create an empty base board, then keep a working copy:

```powershell
dotnet $toy new .quickstart/base.json
Copy-Item .quickstart/base.json .quickstart/board.json
```

The first command prints `made .quickstart/base.json`. Copy-Item prints nothing. Keep the base unchanged so you can replay from it.

The example in `docs/quickstart_journal.json` is a plain text journal. You can write the same file by hand in any text editor. Do not put comments in JSON. It adds a lamp, moves it, renames it, undoes that rename and redoes it:

```json
{
  "kind": "journal",
  "version": 1,
  "entries": [
    {
      "op": "do",
      "command": "toy.add",
      "version": 1,
      "args": {
        "id": "lamp",
        "label": "Lamp",
        "x": 1,
        "y": 2
      }
    },
    {
      "op": "do",
      "command": "toy.move",
      "version": 1,
      "args": {
        "id": "lamp",
        "x": 3,
        "y": 4
      }
    },
    {
      "op": "do",
      "command": "toy.rename",
      "version": 1,
      "args": {
        "id": "lamp",
        "label": "Desk lamp"
      }
    },
    {
      "op": "undo"
    },
    {
      "op": "redo"
    }
  ]
}
```

Each `do` names a registered command and its argument version. This guide supplies an explicit `id` so later entries can name the item. You may omit it when adding an item; the command then chooses an `item_N` id and records that settled id in the session journal. `undo` and `redo` are entries in the journal, too.

Apply the journal and validate the result:

```powershell
dotnet $toy apply .quickstart/board.json docs/quickstart_journal.json
dotnet $toy check .quickstart/board.json
```

Output:

```text
.quickstart/board.json: applied 5 step(s), 1 item(s)
.quickstart/board.json: valid, 1 item(s)
```

The result has one item: id `lamp`, label `Desk lamp`, x `3`, y `4`. Applying the same add journal a second time to that working board is refused because ids must be unique. Replay uses the unchanged base instead.

Replay into a separate result and compare the complete file bytes through SHA256:

```powershell
dotnet $toy replay .quickstart/base.json docs/quickstart_journal.json .quickstart/replayed.json
(Get-FileHash .quickstart/board.json -Algorithm SHA256).Hash -eq (Get-FileHash .quickstart/replayed.json -Algorithm SHA256).Hash
```

Output:

```text
.quickstart/replayed.json: 1 item(s)
True
```

Run a second independent replay to check repeatability:

```powershell
dotnet $toy replay .quickstart/base.json docs/quickstart_journal.json .quickstart/replayed_again.json
(Get-FileHash .quickstart/replayed.json -Algorithm SHA256).Hash -eq (Get-FileHash .quickstart/replayed_again.json -Algorithm SHA256).Hash
```

Output:

```text
.quickstart/replayed_again.json: 1 item(s)
True
```

Find a command by its id:

```powershell
dotnet $toy commands toy.move
```

It prints the `toy.move` row, argument version `v1`, and its label `Move item`. Columns are padded with spaces. Search also accepts part of a visible command's label.

Check an exit code immediately after a native command with PowerShell's `$LASTEXITCODE`. For example, this refuses to overwrite the base:

```powershell
dotnet $toy new .quickstart/base.json
$LASTEXITCODE
```

The error stream says `.quickstart/base.json already exists; pick another name or remove it first`. The next line is `2`. Your existing base is unchanged.

| Code | Contract |
|---|---|
| 0 | The command completed. |
| 1 | An edit or document was refused. No output document was written. Read the issue's object/path and message. |
| 2 | Bad input: wrong arguments, unreadable or malformed files, bad journals, or new onto an existing file. |
| 3 | The board changed on disk during apply. Keep both versions and inspect them before retrying. |

The file format is versioned, canonical UTF-8 JSON with LF and a final newline. Use the tool to save boards. Keep journals and their base documents together. A journal records edits; an edited board alone does not contain its session's undo history.

Validation of this guide: every PowerShell command above was exercised from an isolated source copy, including the expected refusal. This is a headless CLI workflow, not a claim that the first novice UI study has passed.
