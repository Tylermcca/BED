# Your first document type: a tile map

Build a tile-map document with validation, undo and replay. You will use the existing Tiles sample, then add one command through its public API.

You need a tools checkout and the .NET 10 SDK. Use Windows PowerShell 5.1 or PowerShell 7 at the repository root. The first build may restore the repository's declared dependencies.

The sample uses original tile data. It runs from the command line without opening a graphics window; you do not need game files.

## Step 1 — Give the document a shape

Open the source of `samples/Tiles/TileMap.cs`. TileMap holds width, height, cell size in metres, an immutable list of TileKind records and an immutable array of nullable tile ids. Cells use row-major order: index = y * width + x. A null cell is empty.

An immutable collection matters. A caller holding a document must not edit a session by changing an array behind its back. Make a new record and collection for an edit. The sample caps maps at 1,000,000 cells as an explicit allocation budget; it is not an engine-wide limit.

To build a different document type, replace this model and its rules. Keep the core reference. `samples/Tiles/Tiles.csproj` references only `src/Bed`, not Toy, an editor or a game.

## Step 2 — Define its text format

TileFormat implements IDocumentFormat<TileMap>. Kind is `tiles.map`; Version is 1. Read uses StrictReader to reject missing, duplicate, unknown and wrongly typed fields with a JSON path. Cells are a string-or-null array, so it checks each raw JsonElement returned by Element. StrictReader.Array is for arrays of objects.

Write uses Utf8JsonWriter with Canonical.WriterOptions, explicit key order and a final newline. It preserves kind order and row-major cell order. Do not save documents with an unrelated serializer and assume the bytes will match. A future format change needs an explicit version/migration decision.

DocumentSession handles file writes and its saved-state baseline; the sample does not implement a second atomic-file manager.

## Step 3 — State what makes a whole document valid

TileValidator implements IValidator<TileMap>. It checks positive dimensions, matching cell count, positive finite cell size, unique valid ids, nonblank labels and every cell reference. It reports Issue.Error with an object identifier and JSON path. For example, an unknown id in the first cell points to object `cell:0,0` and `$.cells[0]`.

The format checks representation. The validator checks meaning. A candidate can parse successfully yet be refused because a cell refers to a kind that does not exist.

## Step 4 — Describe edits as commands

Read `samples/Tiles/Commands.cs`. Each record implements ICommand<TileMap> and returns CommandResult<TileMap>: a new candidate or explanatory issues. It does not write a file or mutate an existing snapshot.

Paint and clear use cell coordinates. FillRectangle builds one candidate, so the fill is one journal entry and one undo step. Its rectangle uses an inclusive start and exclusive end. A null kind id clears that rectangle.

RemoveKind refuses a used kind. Its issue names the kind, reports the total used-cell count and gives coordinates (first eight for large maps), then tells the user to clear or repaint. ResizeMap preserves overlapping cells, leaves added cells empty and crops cells beyond a smaller size. One undo restores the prior size and cells. That cropping policy is explicit sample behavior, not an implicit core rule.

## Step 5 — Register, validate, then commit

TilesKit.Catalog registers seven stable `tiles.*` command ids, version 1 and searchable labels by hand. There are no assembly scans.

DocumentSession<TileMap> connects the format, validator, catalogue and starting document. Execute builds a candidate and validates it before commit. Refusal keeps Current, revision, history and journal unchanged. Undo and redo use the same session. Save marks the current canonical text clean; replay uses the same base plus JournalFile entries.

Build the worked example:

```powershell
dotnet build samples/Tiles/Tiles.csproj --nologo -v:q
```

Check that the build succeeds with zero warnings and errors before continuing.

## Step 6 — Exercise it without a window

Use a fresh scratch directory. The following commands create a 3 by 2 map with half-metre cells and apply two edits:

```powershell
New-Item -ItemType Directory -Path .tutorial-tiles | Out-Null
$tiles = "samples/Tiles/bin/Debug/net10.0/tiles.dll"
dotnet $tiles new .tutorial-tiles/base.json 3 2 0.5
Copy-Item .tutorial-tiles/base.json .tutorial-tiles/map.json
$journal = @'
{"kind":"journal","version":1,"entries":[{"op":"do","command":"tiles.add_kind","version":1,"args":{"id":"grass","label":"Grass"}},{"op":"do","command":"tiles.fill","version":1,"args":{"x":0,"y":0,"width":2,"height":2,"kindId":"grass"}}]}
'@
[IO.File]::WriteAllText("$PWD/.tutorial-tiles/paint.json", $journal, [Text.UTF8Encoding]::new($false))
dotnet $tiles apply .tutorial-tiles/map.json .tutorial-tiles/paint.json
dotnet $tiles check .tutorial-tiles/map.json
dotnet $tiles replay .tutorial-tiles/base.json .tutorial-tiles/paint.json .tutorial-tiles/replayed.json
(Get-FileHash .tutorial-tiles/map.json).Hash -eq (Get-FileHash .tutorial-tiles/replayed.json).Hash
```

Output from the native commands and comparison:

```text
made .tutorial-tiles/base.json
.tutorial-tiles/map.json: applied 2 step(s)
.tutorial-tiles/map.json: valid, 3 x 2 cells, 1 kind(s)
.tutorial-tiles/replayed.json: 3 x 2 cells
True
```

The directory, copy and write commands print nothing. After each native call, read `$LASTEXITCODE`:

- 0: done.
- 1: an edit or document was refused; no new result was saved.
- 2: arguments were wrong, or input was unreadable or malformed.
- 3: the file changed outside the program during apply.

Use labels for people and stable command and kind ids for references.

## Step 7 — Add one small tool through the public API

Create a tiny consumer inside the same fresh scratch directory:

```powershell
dotnet new console -n TilesProbe -o .tutorial-tiles/probe --framework net10.0 --no-restore
dotnet add .tutorial-tiles/probe/TilesProbe.csproj reference samples/Tiles/Tiles.csproj
```

The first command reports that the Console App template was created. The second reports the Tiles project reference was added. This scratch consumer references the worked example's types. The Tiles sample itself still references only the core.

Replace the generated `.tutorial-tiles/probe/Program.cs` with this **complete program**:

```csharp
using Bed;
using Tiles;

var catalog = TilesKit.Catalog()
    .Register<StampOrigin>("tiles.stamp_origin", 1, "Stamp origin");
var basis = TileMap.Empty();
var session = new DocumentSession<TileMap>(
    TilesKit.Format, TilesKit.Validator, catalog, basis);
if (!session.Execute(new AddKind("grass", "Grass")) ||
    !session.Execute(new StampOrigin("grass")))
    throw new InvalidOperationException("The example edit was refused.");
Console.WriteLine(session.Current.Cells[0]);
var replayed = DocumentSession<TileMap>.Replay(
    basis, JournalFile.Read(JournalFile.Write(session.Journal)),
    TilesKit.Format, TilesKit.Validator, catalog);
Console.WriteLine(TilesKit.Format.Write(session.Current) ==
                  TilesKit.Format.Write(replayed.Current));

public sealed record StampOrigin(string KindId) : ICommand<TileMap>
{
    public CommandResult<TileMap> Apply(TileMap map) =>
        new FillRectangle(0, 0, 1, 1, KindId).Apply(map);
}
```

Run it:

```powershell
dotnet run --project .tutorial-tiles/probe/TilesProbe.csproj -v:q
```

Output:

```text
grass
True
```

The custom command is registered explicitly and replayed using that catalogue. It delegates candidate construction, not a nested session commit. Core validation and history still run once. No core edit or empty plug-in framework was needed. If your new document needs a missing core capability, record the requirement rather than changing an interface to fit.

## Step 8 — Where to go next

The sample's tests are in `tests/Bed.Tests/TilesTests.cs`. They cover references, bounded arrays, refusal, undo, strict reading, bytes and all four CLI exit codes. Build your own domain tests as you replace the tile rules.

To add selection, workspaces, a command palette and focus, use EditorModel<TDoc> in Bed.Editor with your IDocumentOutline<TDoc>. See docs/guide/api_overview.md for those APIs.

This tutorial does not open an editor window. Keep game-specific rules in your adapter and use content you created yourself.
