# Edit game data

Change a value, see validation, and save it without writing code. You need BED and a writable project with data and schemas. The built-in tour supplies a safe example copy.

## Try a price change

1. From the start screen choose **Take the tour**, then **Try the example**. If the tour is hidden, open **Getting started** first. BED makes a separate copy; the installed example stays unchanged.
2. Choose **Skip tour** to explore freely, then open **wares.json** in **Files**.
3. In the table, change Bread's price to **4** and leave the cell. The changed value remains in the draft.
4. Try **-1**. Read the validation message beside the field; the invalid value cannot replace the valid draft.
5. Enter **5**, leave the cell, and press **Ctrl+S**. Check that the status says **Unsaved changes: None**.
6. Close and reopen the project to check the saved price. For your own data, choose **File > Open project...** and select your project's data and schema folders.

If the file opens as text or has no editable fields, ask the project's maintainer for its schema and editor setup. A read-only file reports why it cannot be saved. Keep a valid draft while you fix the reported problem.

Next, follow [Start here](start_here.md) for a complete editing session or [Play](playing.md) to run your saved changes.

## For developers: embed the data editor

Use JSON data and JSON Schemas to build an editor with an outline, inspector and table. Edits are validated and undoable. Saving preserves unchanged source text where possible.

This SDK tutorial uses a local BED checkout, Windows PowerShell 5.1 and .NET SDK 10. Existing BRUTAL dependencies must be available from the repository's configured package sources; see [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md). No additional package is needed. Use writable throwaway inputs.

Set BRUTAL_AUTHORING to your checkout's absolute path. Run all PowerShell blocks in order in one shell, starting in an empty working directory. Paste each block into that open shell: don't save them as a `.ps1` file, because Windows' default script policy refuses to run one; the first block binds that checkout for the supplied command-line projects. C# inside a here-string is written by that block; no IDE setup is assumed. Expected nonzero exits are stated. Restore/build diagnostic lines may vary.

The components need a host. Below you build a complete small console host that draws a PNG and receives scripted keys with **no window, GPU or OS input**. This is a runnable integration example, not an interactive desktop application. A graphical host calls the same Configure/draw APIs from its ImGui loop; this tutorial never launches one.

The catalog and Lantern Garden examples are original demonstration data. You do not need game files or purchased content.

## Start with two files

Use a root object with named sections. A section can be an object of settings or a list of objects. That shape gives the current outline something to show.

Describe each editable field under `properties` and give it a `type`. Use `required` for fields that must exist; declaring a property alone does not require it. Use `enum` for a fixed set of choices. Here the choices are strings. `additionalProperties: false` catches misspelled keys, while `minimum` catches an invalid quantity. Set `type` on containers too, so a string cannot stand in for the whole object.

Use Windows PowerShell 5.1, .NET SDK 10 and a fresh temporary folder. The blocks create the example files as UTF-8 with LF endings and a final newline.

First bind the checkout and two example roots:

~~~powershell
$repo = $env:BRUTAL_AUTHORING
if (!(Test-Path "$repo/Bed.slnx")) { throw "Set BRUTAL_AUTHORING to the tools checkout." }
Set-Location -LiteralPath $repo
$work = Join-Path $env:TEMP ("authoring-data-guide-" + [Guid]::NewGuid().ToString("N"))
$example = Join-Path $work "catalog"
$garden = Join-Path $work "lantern-garden"
New-Item -ItemType Directory -Path $work | Out-Null
~~~

Keep both variables: **$example always means catalog**, **$garden always means Lantern Garden**. Later catalog validation and outside-edit exercises still use $example. Every data file the tutorial makes stays below $work. (The build below writes its own output beside the sample, inside the checkout, as any build does.)

Build the supplied command-line tool:

```powershell
dotnet build samples/DataCheck/DataCheck.csproj --nologo -v:q
```

Check that the build succeeds before continuing. Then create and validate the example:

```powershell
New-Item -ItemType Directory -Path "$example/data", "$example/schemas" | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$schema = @'
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.invalid/catalog.schema.json",
  "type": "object",
  "properties": {
    "settings": {
      "type": "object",
      "properties": {
        "title": {
          "type": "string",
          "description": "The label shown for this collection."
        },
        "enabled": {
          "type": "boolean",
          "default": true
        }
      },
      "required": [
        "title",
        "enabled"
      ],
      "additionalProperties": false
    },
    "items": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "properties": {
          "name": {
            "type": "string",
            "description": "An optional readable item name."
          },
          "id": {
            "type": "string"
          },
          "title": {
            "type": "string"
          },
          "mode": {
            "type": "string",
            "enum": [
              "warm",
              "cool"
            ],
            "default": "warm",
            "description": "Choose the colour family."
          },
          "quantity": {
            "type": "integer",
            "minimum": 1,
            "description": "Use a whole number of at least one."
          },
          "size": {
            "type": "number",
            "minimum": 0,
            "maximum": 10,
            "default": 1,
            "description": "A nonnegative example size, at most ten."
          },
          "active": {
            "type": "boolean",
            "default": true
          },
          "note": {
            "type": "string",
            "description": "Optional notes."
          },
          "details": {
            "type": "object",
            "properties": {
              "caption": {
                "type": "string",
                "default": "Nested caption"
              }
            },
            "required": [
              "caption"
            ],
            "additionalProperties": false
          },
          "steps": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "id": {
                  "type": "string",
                  "default": "step"
                },
                "amount": {
                  "type": "integer",
                  "minimum": 1,
                  "default": 1
                }
              },
              "required": [
                "id",
                "amount"
              ],
              "additionalProperties": false
            }
          },
          "tags": {
            "type": "array",
            "uniqueItems": true,
            "items": {
              "type": "string",
              "enum": [
                "warm",
                "cool",
                "spare"
              ]
            }
          }
        },
        "required": [
          "mode",
          "quantity"
        ],
        "additionalProperties": false
      }
    }
  },
  "required": [
    "settings",
    "items"
  ],
  "additionalProperties": false
}
'@
$data = @'
{
  "settings": {
    "title": "Desk kit",
    "enabled": true
  },
  "items": [
    {
      "name": "Reading lamp",
      "mode": "warm",
      "quantity": 2,
      "size": 1.0,
      "active": false,
      "note": "Café",
      "details": {
        "caption": "Nested caption"
      },
      "steps": [
        {
          "id": "step",
          "amount": 1
        }
      ],
      "tags": [
        "warm"
      ]
    },
    {
      "id": "spare",
      "mode": "cool",
      "quantity": 1
    },
    {
      "title": "Backup",
      "mode": "warm",
      "quantity": 1
    },
    {
      "mode": "warm",
      "quantity": 1
    }
  ]
}
'@
[IO.File]::WriteAllText("$example/schemas/catalog.schema.json", $schema.Replace("`r`n", "`n") + "`n", $utf8)
[IO.File]::WriteAllText("$example/data/catalog.json", $data.Replace("`r`n", "`n") + "`n", $utf8)
$check = "samples/DataCheck/DataCheck.csproj"
dotnet run --no-build --project $check -- "$example/data" "$example/schemas"
$LASTEXITCODE
```

Actual output; the last line is `$LASTEXITCODE`:

```text
1 file(s) checked, 0 with problems.
0
```

The schema's `$id` is an identifier, not a download request. This example uses file-name matching: `catalog.json` is checked against `catalog.schema.json`.

## Start a project

The catalog and schema now exist. Make a console project and add the actual references; no per-field descriptors are needed:

~~~powershell
$hostDir = Join-Path $work "host"
$hostProject = Join-Path $hostDir "GuideHost.csproj"
dotnet new console --name GuideHost --output $hostDir --framework net10.0 --no-restore
dotnet add $hostProject reference "$repo/src/Bed.Data/Bed.Data.csproj" "$repo/src/Bed.Editor.ImGui/Bed.Editor.ImGui.csproj"
~~~

Write this complete Program.cs. Its modes implement the later exercises on tutorial inputs.

~~~powershell
$program = @'
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;

var root = Path.GetFullPath(args[0]);
var mode = args.Length > 1 ? args[1] : "open";
if (mode == "--shot") mode = "render"; // headless alias, never a native window
void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
DataProject Open(string at) => DataProject.Open(Path.Combine(at, "data"), Path.Combine(at, "schemas"));

if (mode == "outside")
{
    // Only fresh copies of the tutorial: never run this exercise on real project files.
    var demo = Path.Combine(root, "outside-exercise");
    Need(!Directory.Exists(demo), "Exercise already exists; choose a fresh tutorial root.");
    Directory.CreateDirectory(Path.Combine(demo, "data"));
    Directory.CreateDirectory(Path.Combine(demo, "schemas"));
    foreach (var name in new[] { "data/catalog.json", "schemas/catalog.schema.json" })
        File.Copy(Path.Combine(root, name), Path.Combine(demo, name));
    var p = Open(demo);
    var f = p.Find("catalog.json")!;
    Need(f.Open(), "Could not open exercise catalog.");
    var session = f.Session!;
    Need(session.Execute(new SetValue("/items/0/quantity", JsonSerializer.SerializeToElement(3))), "Edit refused.");
    var external = File.ReadAllText(f.Path).Replace("Desk kit", "Outside edit");
    File.WriteAllText(f.Path, external, new UTF8Encoding(false));
    Need(!f.Save() && f.Dirty && File.ReadAllText(f.Path) == external, "Outside edit was not protected.");
    Console.WriteLine("Refused: " + f.Problems[0].Message);
    var preserved = Path.Combine(demo, "preserved-catalog.json");
    File.WriteAllText(preserved, session.Format.Write(session.Current), new UTF8Encoding(false));
    Need(File.ReadAllText(preserved).Contains("\"quantity\": 3"), "Draft copy missing edit.");
    p.Refresh();
    Need(ReferenceEquals(session, f.Session) && f.Session!.Current.At("/items/0/quantity")!.Value.GetInt32() == 3,
        "Refresh lost the draft.");
    Console.WriteLine("Refresh: same session, draft quantity 3; external title still on disk.");
    // Reopening is deliberate only after preservation. A fresh workspace avoids cached editors.
    var fresh = Open(demo);
    var replacement = new DataWorkspace(fresh);
    Need(replacement.Show("catalog.json"), replacement.Said);
    Need(fresh.Find("catalog.json")!.Session!.Current.At("/settings/title")!.Value.GetString() == "Outside edit",
        "Fresh project did not read disk.");
    Console.WriteLine("Reopen: external title Outside edit, disk quantity 2; preserved draft quantity 3.");
    // Also prove deletion refusal on this disposable copy, with a live dirty session.
    var deleted = fresh.Find("catalog.json")!;
    Need(deleted.Session!.Execute(new SetValue("/items/0/quantity", JsonSerializer.SerializeToElement(4))), "Edit refused.");
    File.WriteAllText(Path.Combine(demo, "preserved-before-delete.json"),
        deleted.Session.Format.Write(deleted.Session.Current), new UTF8Encoding(false));
    File.Delete(deleted.Path);
    Need(!deleted.Save() && deleted.Dirty && !File.Exists(deleted.Path), "Deleted file was recreated.");
    Console.WriteLine("Deleted: " + deleted.Problems[0].Message);
    Console.WriteLine("Recovery copies: " + demo);
    return;
}

var project = Open(root);
var mapPath = Path.Combine(root, "reference-map.json");
var map = File.Exists(mapPath) ? ReferenceMap.Read(File.ReadAllText(mapPath)) : null;
var workspace = new DataWorkspace(project, map: map);
var fileName = map is null ? "catalog.json" : "gardeners.json";
Need(workspace.Show(fileName), workspace.Said);
var editor = workspace.Window!.Editor;
var table = workspace.TableOf(fileName)!;
Console.WriteLine($"Opened {fileName}; {project.Files.Count} files listed.");
if (mode == "open") return;

if (mode == "links")
{
    Need(map is not null, "Create Lantern Garden and its map first.");
    var document = workspace.Active!.Session!.Current;
    foreach (var pointer in new[] { "/gardeners/0", "/gardeners/1", "/gardeners/2" })
        Console.WriteLine($"Label {pointer}: {map!.Label(fileName, pointer, document.At(pointer)!.Value) ?? "(fallback)"}");
    foreach (var field in new[] { "seed", "missing", "duplicate", "none", "unloaded", "scope_only", "plain", "pair", "tagged", "plot", "team", "mood" })
    {
        var at = "/gardeners/0/" + field;
        var result = map!.Resolve(project, fileName, at, document.At(at)!.Value);
        Console.WriteLine($"{field}: {result.Outcome}; {result.Target?.File} {result.Target?.Pointer}; {result.Note}");
    }
    editor.Select("/gardeners/0");
    Need(!table.Follow("/gardeners/0", "duplicate"), "Duplicate must refuse.");
    Need(table.Said.Contains("2 items") && table.Said.Contains("different id"), "Resolver reason/remedy lost.");
    Console.WriteLine("Follow duplicate: " + table.Said);
    Need(table.Follow("/gardeners/0", "seed"), table.Said);
    Console.WriteLine("Follow seed: " + workspace.Active!.Name + " " + workspace.Window!.Editor.Primary);
    Need(workspace.Back(), "No cross-file Back.");
    Need(workspace.Active!.Name == fileName && workspace.Window!.Editor.Primary == "/gardeners/0", "Back lost origin.");
    Console.WriteLine("Back: " + fileName + " /gardeners/0");
    Need(table.Follow("/gardeners/0", "plot"), table.Said);
    Console.WriteLine("Nested: " + workspace.Said);
    Need(project.Dirty.Count == 0, "Navigation dirtied data.");
    return;
}

table.List = "/items";
editor.Select("/items/0");
if (mode == "checked")
{
    var session = workspace.Active!.Session!;
    var journal = session.Journal.Count;
    Need(!table.SetCell("/items/0", "quantity", 0L), "Invalid quantity accepted.");
    Need(session.Journal.Count == journal && !session.Dirty, "Refusal changed draft.");
    Console.WriteLine("Invalid quantity: " + table.Said);
    Need(table.SetCell("/items/0", "quantity", 3L), table.Said);
    Need(session.Undo() && !session.Dirty, "Undo did not return to original.");
    table.Filter = "Reading";
    var fill = table.PlanFill("quantity", "4");
    Need(fill.Ok && fill.Cells == 1 && !session.Dirty, "Fill preview changed data.");
    Need(table.Apply(fill), table.Said);
    Need(session.Current.At("/items/1/quantity")!.Value.GetInt32() == 1, "Fill touched hidden row.");
    Need(session.Undo() && !session.Dirty, "Fill undo failed.");
    table.Filter = "";
    var paste = table.PlanPaste("/items/0", "quantity", "3\n4");
    Need(paste.Ok && paste.Cells == 2 && !session.Dirty, "Paste preview changed data.");
    Need(table.Apply(paste) && session.Undo() && !session.Dirty, "Paste/undo failed.");
    Need(!table.PlanPaste("/items/0", "quantity", "3\t1\n4").Ok, "Ragged paste accepted.");
    Console.WriteLine("Checked edit, one-step undo, filtered fill, paste and ragged refusal: OK; disk unchanged.");
    return;
}

if (mode == "inspector")
{
    var demo = Path.Combine(root, "inspector-demo");
    Need(!Directory.Exists(demo), "Use a fresh inspector-demo folder.");
    Directory.CreateDirectory(Path.Combine(demo, "data"));
    Directory.CreateDirectory(Path.Combine(demo, "schemas"));
    var properties = new JsonObject { ["present"] = new JsonObject { ["type"] = "string" } };
    for (var n = 0; n < 60; n++) properties["extra" + n] = new JsonObject { ["type"] = "string" };
    var itemSchema = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["id"] = new JsonObject { ["type"] = "string" },
            ["details"] = new JsonObject { ["type"] = "object", ["properties"] = properties },
            ["steps"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }
        }
    };
    var schema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject {
        ["items"] = new JsonObject { ["type"] = "array", ["items"] = itemSchema } } };
    var steps = new JsonArray();
    for (var n = 0; n < 120; n++) steps.Add("entry" + n);
    var data = new JsonObject { ["items"] = new JsonArray(new JsonObject {
        ["id"] = "example", ["details"] = new JsonObject { ["present"] = "kept" }, ["steps"] = steps }) };
    File.WriteAllText(Path.Combine(demo, "schemas/long.schema.json"), schema.ToJsonString());
    File.WriteAllText(Path.Combine(demo, "data/long.json"), data.ToJsonString());
    var p = Open(demo); var ws = new DataWorkspace(p); Need(ws.Show("long.json"), ws.Said);
    var win = ws.Window!; var session = ws.Active!.Session!;
    Need(win.Editor.Select("/items/0"), "Select example.");
    var before = session.Format.Write(session.Current); var history = session.History.UndoCount;
    using var proof = new HeadlessHost(1600, 1100) { Drawing = false };
    EditorWindow<DataDocument>.Configure(ImGui.GetIO());
    Action view = ws.Draw;
    proof.MoveMouse(view, -100, -100); proof.Frames(view, 5);
    var list = win.Inspector!.Rows().Single(row => DataEditing.PlaceOfRow(row.Id) == "/steps");
    var last = win.Inspector.Nested(list).Last();
    Need(win.Find("ENTRY119") == 1 && win.Found.Count == 1, "Deep case-insensitive search.");
    Need(win.Reveal(win.Found[0].Pointer), win.Status); proof.Frames(view, 4);
    Need(win.InspectorPlace(last.Id) is not null, "Reveal must draw the last page.");
    Need(win.Reveal("/items/0/steps"), win.Status); proof.Frames(view, 4);
    var firstAt = win.InspectorPlace(list.Id, InspectorPart.First)!.Value;
    proof.Click(view, firstAt.X + 6, firstAt.Y + 7); proof.Frames(view, 4);
    Need(win.InspectorPlace(last.Id) is null, "First leaves the last page.");
    var goAt = win.InspectorPlace(list.Id, InspectorPart.GoTo)!.Value;
    proof.Click(view, goAt.X + 6, goAt.Y + 7);
    proof.Press(view, ImGuiKey.A, ctrl: true); proof.Press(view, ImGuiKey.Backspace);
    proof.Type(view, "120"); proof.Press(view, ImGuiKey.Enter); proof.Frames(view, 4);
    Need(win.InspectorPlace(last.Id) is not null, "Go to uses one-based item numbers.");
    var lastAt = win.InspectorPlace(list.Id, InspectorPart.Last)!.Value;
    proof.Click(view, lastAt.X + 6, lastAt.Y + 7); proof.Frames(view, 4);
    Need(win.InspectorPlace(last.Id) is not null, "Last retains the last page.");
    proof.Drawing = true; proof.Frame(view); proof.Save(Path.Combine(demo, "long-list.png"));
    Need(win.Reveal("/items/0/details/extra59"), win.Status); proof.Frames(view, 4);
    var details = win.Inspector.Rows().Single(row => DataEditing.PlaceOfRow(row.Id) == "/details");
    var extra = win.Inspector.Nested(details).Single(row => DataEditing.PlaceOfRow(row.Id) == "/details/extra59");
    Need(win.InspectorPlace(extra.Id) is not null, "Reveal exposes an unset field.");
    proof.Frame(view); proof.Save(Path.Combine(demo, "unset-fields.png"));
    Need(before == session.Format.Write(session.Current) && history == session.History.UndoCount && !session.Dirty,
        "Navigation changed the document.");
    Console.WriteLine("Inspector navigation: First, Go to, Last, unset fields and search/reveal pass; no document edits.");
    Console.WriteLine("Inspector pictures: " + demo);
    return;
}

using var host = new HeadlessHost(1600, 900) { Drawing = false };
EditorWindow<DataDocument>.Configure(ImGui.GetIO());
Action draw = workspace.Draw;
host.MoveMouse(draw, -100, -100);
host.Frames(draw, 5);
if (mode == "keyboard")
{
    // A panel action toggles it: only execute Show when closed.
    if (!editor.IsOpen("editor.table"))
    {
        host.Press(draw, ImGuiKey.P, ctrl: true, shift: true);
        host.Press(draw, ImGuiKey.A, ctrl: true);
        host.Type(draw, "editor.panel.editor.table");
        host.Press(draw, ImGuiKey.Enter);
    }
    Need(editor.IsOpen("editor.table"), "Table did not open.");
    string FocusedWindow() => Marshal.PtrToStringUTF8((IntPtr)ImGui.GetCurrentContext().NavWindow.Name) ?? "";
    bool reached = false;
    for (int n = 1; n <= 10 && !reached; n++)
    {
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Ctrl, true);
        for (int k = 0; k < n; k++) host.Press(draw, ImGuiKey.Tab);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Ctrl, false);
        host.Frames(draw);
        reached = FocusedWindow() == "Table###panel.editor.table";
    }
    Need(reached, "Keyboard did not reach Table.");
    // Enter its scrolling child, then find a cell by its observable anchor, not a fixed Tab count.
    host.Press(draw, ImGuiKey.DownArrow);
    host.Press(draw, ImGuiKey.DownArrow);
    host.Press(draw, ImGuiKey.Enter);
    for (int n = 0; n < 100 && table.Anchor != ("/items/0", "quantity"); n++)
        host.Press(draw, ImGuiKey.Tab);
    Need(table.Anchor == ("/items/0", "quantity") && ImGui.GetCurrentContext().NavId.Value != 0,
        "No keyboard focus/picked quantity checkpoint.");
    Console.WriteLine($"Keyboard checkpoint: {FocusedWindow()}, quantity picked, focus ID nonzero.");
    host.Press(draw, ImGuiKey.A, ctrl: true);
    host.Type(draw, "3");
    host.Press(draw, ImGuiKey.Enter);
    Need(editor.Session.Current.At("/items/0/quantity")!.Value.GetInt32() == 3, "Typed edit did not confirm.");
    Console.WriteLine("Keyboard quantity 2 -> 3; draft only.");
}
else Need(mode == "render", "Mode must be open/render/keyboard/checked/links/outside.");
host.Drawing = true;
host.Frame(draw);
var png = Path.GetFullPath(args[2]);
host.Save(png);
Need(host.Covered() > 1000, "Empty render.");
Console.WriteLine("Rendered offscreen: " + png);
'@
[IO.File]::WriteAllText("$hostDir/Program.cs", $program.Replace("`r`n", "`n") + "`n", $utf8)
dotnet build $hostProject --nologo -v:q
dotnet run --no-build --project $hostProject -- $example open
$LASTEXITCODE
~~~

Expected: build succeeds, then Opened catalog.json; 1 files listed., exit 0. The open mode checks project/session wiring and exits. Next use its headless modes.

## Open a folder as a project

Keep game JSON directly in a data folder and schemas directly in a separate schemas folder. DataProject lists direct *.json files in ordinal name order; it does not recurse or read data bytes until you open a file. Each opened file has its own session, undo history, dirty state and outside-change baseline.

Use a string $schema in a data file to select a local schema by file name. Without one, catalog.json selects catalog.schema.json. A path or URL in $schema still selects its final file name from the supplied schema set; it is not an instruction to download it. If your schema rejects additional properties, declare the data's $schema property too.

Wire DataProject into DataWorkspace in your host. DataWorkspace supplies a Files panel and gives each shown file its outline, inspector, table and palette. Switching files keeps each one's selection and edits. Refresh lists the folder again; a vanished file keeps its session and unsaved work, marked Missing. A restored file with a session keeps that session rather than silently replacing your edits.

To use Files without a pointer, hold Ctrl and press Tab to choose the Files panel, then release Ctrl. Tab or Shift+Tab reaches its actions and file rows; Enter activates the focused control. New and Rename put focus in the name field: type the name, choose the schema for New if needed, then choose Make it or Rename it. Enter in the name field submits that form. Cancel or Escape returns focus to its opening action. A successful New or Rename returns focus to the file row. Delete asks again: Tab to Delete it and press Enter to confirm, or use Cancel/Escape to return. After deletion focus moves to Restore; restoring returns it to that file's row. Actions appear only when the host and project allow them.

If the host offers Close project, reach it in Files and press Enter. With unsaved edits the launcher offers Save all, then close; Close anyway; and Keep it open. Tab to the choice and press Enter. Escape while that prompt has focus keeps the project and its draft open. This is the editor's project-close prompt, not an operating-system window-close shortcut.

The Start a project driver supplies the full setup: DataProject.Open receives data/ and schemas/, DataWorkspace.Show opens a file, and its other modes exercise those components. The driver explicitly loads root/reference-map.json if present; the library does not auto-load a map by its location. The general launcher and ProjectPaths.In use root/reference_map.json (underscore) for automatic discovery instead. Keep the tutorial's hyphenated name when running this explicit driver; use the underscore name for the [complete adapter/launcher walkthrough](developer_walkthrough.md), or pass an explicit ProjectPaths.Map.

| Refusal | What to do |
|---|---|
| Either folder is missing | Correct the path; project opening throws DirectoryNotFoundException. |
| A schema is malformed JSON | Correct it before opening the project; opening throws JsonException. |
| A data file is malformed, has no matching schema, or fails validation | Read that file's Problems/Files-panel reason. Other files can still open. Correct the data file and retry opening it. If you changed schema files, reopen the project to load the new schema set; Refresh only relists data files. |
| A file was deleted | Preserve its retained edits. Refresh does not discard or reload them. |
| The project or an individual file is read-only | You can inspect it; saving is refused. In-memory changes are not permission to write it. |

Open read-only with DataProject.Open(dataFolder, schemaFolder, readOnly: true). A host can instead use readOnlyFile to return a reason for particular generated files. The project-wide rule wins. DataWorkspace routes Save and Save As through these rules.

## Use it without a window

Render the actual docked workspace to a PNG and run the checked-path and keyboard exercises:

~~~powershell
dotnet run --no-build --project $hostProject -- $example render "$example/workspace.png"
$LASTEXITCODE
dotnet run --no-build --project $hostProject -- $example checked
$LASTEXITCODE
dotnet run --no-build --project $hostProject -- $example keyboard "$example/keyboard.png"
$LASTEXITCODE
~~~

Each exits 0. workspace.png is a CPU-rendered frame. The checked mode uses the SDK explicitly: it refuses quantity 0, commits 3 then undoes, previews/applies/undoes a filtered fill and a two-row paste, and refuses a ragged paste. It never saves catalog.json.

The keyboard mode uses HeadlessHost key events with the pointer away. It checks Table is open, enters its scrolling child, tabs until /items/0 / quantity is the picked cell and the native focus ID is nonzero, then types 3 and confirms with Enter. Expected: Keyboard checkpoint: Table... quantity picked, focus ID nonzero.; Keyboard quantity 2 -> 3; draft only. keyboard.png shows that result. A picked cell left by an earlier mouse action alone does not establish keyboard focus. The driver fails if it cannot reach the checkpoint; it never substitutes an SDK edit silently.

Create HeadlessHost before accessing ImGui, call EditorWindow<DataDocument>.Configure(ImGui.GetIO()) once for that context, and pass workspace.Draw to Frame/Frames. Press/Type/Click feed only that host; Save writes its last frame as PNG. Dispose before making another host: ImGui's context is process-wide. Drawing=false still advances UI/input; set true and run a frame before saving pixels.

### Use the authoring CLI

The CLI comes from this checkout; no global authoring install is assumed:

~~~powershell
$cli = Join-Path $repo "src/Bed.Agent.Cli/Bed.Agent.Cli.csproj"
dotnet build $cli --nologo -v:q
dotnet run --no-build --project $cli -- --help
$LASTEXITCODE
$authoring = @("run", "--no-build", "--project", $cli, "--", "--data", "$example/data", "--schemas", "$example/schemas")
& dotnet @authoring describe
$LASTEXITCODE
& dotnet @authoring find catalog.json /items Reading
$LASTEXITCODE
& dotnet @authoring get catalog.json /items/0/quantity
$LASTEXITCODE
& dotnet @authoring set catalog.json /items/0/quantity 3 --preview
$LASTEXITCODE
& dotnet @authoring set catalog.json /items/0/quantity 0 --preview
$LASTEXITCODE
~~~

--help prints usage to stderr and exits **2** today; there is no dedicated successful help verb. The next four calls exit 0: describe lists catalog and next calls; find labels Reading lamp; get returns 2; valid preview describes the change. The invalid quantity preview is refused with minimum 1 and exits **1**. Preview changes nothing.

An ordinary CLI edit without --preview applies to its process's draft, then the process ends; disk is unchanged without --save. Persist an intended edit only with explicit --save. Each invocation opens a new project and cannot see another host's unsaved drafts. mcp instead keeps AgentSession alive over stdio; clients can preview, edit, undo and explicitly save that session. This recipe does not register a client or launch a GUI.

## Find and edit an item

The outline shows top-level object and list keys: **settings** and **items** here. Object items in a top-level list appear beneath that list. Their labels use a nonempty string `name`, otherwise `id`, otherwise `title`, otherwise their zero-based position, such as **#3**. The four example items appear as **Reading lamp**, **spare**, **Backup** and **#3**.

Select **Reading lamp** to inspect its fields. Field labels default to their keys, such as `quantity`. Current schema support also lets a property's nonempty `title` override its field label. With the schema-aware `DataEditing.OutlineFor`, an item schema's `x-label` names a preferred label field; the usual name/id/title/position fallbacks still apply. The plain `DataEditing.Outline` uses those fallbacks directly.

| JSON description | Inspector |
|---|---|
| `type: string` | Text |
| `type: integer` | Whole number |
| `type: number` | Number |
| `type: boolean` | On/off |
| String `enum` | Choice list, before the ordinary type control |
| Object or array value | Summary with a fold arrow when children exist; editable nested rows below |
| Null | No dedicated null control |

The outline opens top-level sections and their object list items; it remains shallow. The inspector now draws nested objects and lists recursively below the selected item. Open a fold arrow to reach their fields, including scalar list items. The table still shows container summaries; use the inspector for their contents. JSON Pointer remains available through the SDK.

A field's `description` supplies its help text: the window shows its first sentence under the field, and the whole text when you hover over it. The example quantity says **Use a whole number of at least one.** A `default` supplies a reset value; it is not inserted into missing fields on load and does not make a value valid by itself. The first item's `active: false` differs from its default `true`, so it is an override. Reset applies the default through a command and can itself be undone.

The schema language accepts more shapes than the current scalar controls can comfortably edit. Use string enums for choice widgets; numeric or mixed-type enums and nullable/union types are not a promise of a type-aware control for every alternative. Validation still checks the resulting JSON. The schema's `readOnly` annotation does not itself enforce an editing lock.

## Nested fields, lists and fold arrows

Select Reading lamp. Its details object now has a fold arrow and an editable caption underneath. Its steps list has an object item with its own fields; tags has ordinary scalar/choice items. The outline does not need a separate entry for every child: the inspector follows the selected parent's nested shape. A field's schema title can change its label, so use the label shown rather than assuming a JSON key is always visible.

Use **Add** on a list and **Remove** beside an item. Each accepted change is a document command with validation, undo and a journal entry. A refused Add or Remove changes nothing and reports the reason; a minimum length, uniqueness rule or domain constraint can refuse it.

For a nonempty ordinary list, Add starts from its last item. A copied object's own string id is made distinct among that list's neighbors; nested IDs and relationships are not comprehensively remapped. A copied scalar string gets a distinct suffix when necessary. For an enum list, Add first chooses the first unused declared choice; when no unused choice remains the ordinary fallback is still judged by validation, so a unique list can refuse a duplicate.

For an empty or missing list, the editor first tries a schema-settled value: default, first enum choice, empty array or an object whose required fields are all settled. Otherwise it tries a same-named list elsewhere in the document with a compatible basic type, then a list with the same resolved item-schema shape, then a simple schema-shaped empty value. These are starting values, not permission to invent valid game content: the complete candidate still has to pass validation. If no valid first item can be made, supply an adapter/default or a valid template instead of weakening the schema. A missing optional list is created with its first item as one command.

Click a fold arrow, or reach it by keyboard and press Space, to hide or show children. Containers with more than 50 displayed children start folded; an explicit choice survives growth or shrinkage. Once opened, a long list shows at most 50 items per page. First, Earlier, Later and Last change its page. In Go to, type the displayed one-based item number and press Enter: 1 means the first item, not JSON index 1. Out-of-range numbers are clamped to the first or last item. Adding to a long list turns to the page holding the new item.

A long object can have many fields declared by its schema but only a few set. When it offers more than 50 fields, the inspector initially shows the fields it has. Open its fold and choose Show N fields not set to expose the others; Hide the fields not set returns to the smaller view. This changes neither the JSON nor defaults. An object with every field unset still has a fold and that button. If more than 50 fields remain visible, the same page controls apply to that displayed list of fields.

Folds, pages and showing unset fields are view state: they do not dirty data, enter undo or get saved as content. Fold keys use escaped field names and item names, with an occurrence number for twins. Duplicate displayed names therefore have separate folds. Removing an earlier twin can renumber the later ones; unnamed items also depend on their position. Do not use a fold label as persistent object identity.

Text submits on Enter or leaving its field. Folding away valid pending text uses that same commit path. Read any refusal and correct it; the previously valid document remains unchanged. InspectorPlace is an automation locator for the most recent draw, not a durable handle: fields removed by selection/list/fold changes cease to have places. Coordinates can be outside the panel and require scrolling before a click.

The next command runs the complete driver already written above, with its headless --shot alias. It draws the real nested inspector without an OS window and writes only inside the example folder:

~~~powershell
dotnet run --no-build --project $hostProject -- $example --shot "$example/nested.png"
if ($LASTEXITCODE -ne 0) { throw "Nested inspector render failed." }
~~~

Inspect nested.png. Look for fold arrows beside details and steps, indented child inputs, and Add and Remove controls. Use the image to check this layout; review other layouts and keyboard routes separately.

## Search inside a file

Use Find in this file above the inspector and press Enter. It searches the current file's draft, through nested objects and arrays, matching field names and scalar text case-insensitively. It trims the query; a blank query finds nothing. It is a literal search, not a regular expression. At most 100 places are shown in document order; if there are more, make the query longer. This is separate from the table's filter, which changes its visible rows.

Choose a result to select its known containing item, open its folds, turn to its page and scroll to the row. JSON search visits fields present in the draft; it does not search missing optional fields. From the SDK, Reveal can also expose a schema-declared unset field when you already know its pointer. A place with no displayed inspector row is refused with a reason. A changed document invalidates the result list, so run the search again after an edit or undo. A plain host can supply its own EditorWindow.Searcher; DataWorkspace wires DataSearch for JSON.

Before Reveal changes selection, it tries to commit pending typed text. A valid edit becomes a normal undoable draft change, which can make the old search results stale. If that edit is refused, or an earlier refused edit still needs correction, Reveal stays put and explains why. Correct or undo it before navigating. Reveal itself is a view action; it neither saves nor makes an independent history step.

The following exercise builds only neutral example data in a fresh subfolder. It checks the long-list controls, saved-free view changes, sparse unset fields and search/reveal, then draws two offscreen pictures:

~~~powershell
dotnet run --no-build --project $hostProject -- $example inspector
if ($LASTEXITCODE -ne 0) { throw "Inspector navigation proof failed." }
~~~

Expect: Inspector navigation: First, Go to, Last, unset fields and search/reveal pass; no document edits. Inspect long-list.png and unset-fields.png in the printed inspector-demo folder. Review your own datasets separately.

## A refused save

A project's adapter can register SaveCheck as a final permission for a dirty file. A refusal says why it was not saved and leaves the edit/history available; correct the draft or checker setup and retry. For example, the Lantern Yard adapter tutorial reports its whole-project intensity budget while retaining individually valid lamp edits. See [A check before saving](writing_an_adapter.md#a-check-before-saving).

Save all uses DataProject.SaveAll: it judges the proposed group, removes a refused file, and judges the rest again against what will really be saved. BeingSaved tells the adapter that attempt's candidates; another dirty file is not included in a one-file save. A valid remaining group may save even though other files were refused. The writer checks expected disk versions, puts earlier writes back after a write failure while they still hold its text, and reports final file states. Read the named refusals and keep the remaining drafts; see the adapter guide's [remaining storage limit](writing_an_adapter.md#remaining-storage-limit). Saving from inside SaveCheck is blocked. A clean DataFile.Save does nothing. Create, rename, delete and restore instead meet DataProject.FileCheck when configured; they do not call SaveCheck. See the adapter guide for a candidate-file policy. Direct session writes and a separately opened unconfigured CLI project do not inherit another host's adapter. An expensive synchronous callback can pause the editor; it should return a refusal when its authority is unavailable, not quietly waive the rule.

## Keep edits on the checked path

A host uses `JsonSchemas` to load the schema folder, `DataKit.Open` to open a validated `DataDocument`, `DataEditing.OutlineFor` for its outline, and `DataEditing.ObjectType` for its inspector rows. Register that type with `Inspector<DataDocument>` over the editor model. These are the existing reusable components, not a request to implement a custom document format.

The inspector builds `SetValue` commands. The data command catalogue also provides `InsertItem`, `RemoveItem` and `MoveItem`. A session validates the candidate document before committing it. Setting the example quantity to zero is refused; its current value, history and journal stay unchanged. A successful edit enters undo and the journal. Editing several selected objects uses one batch and one undo step. Undo, redo and journal replay use the same session path. Selection and focus are view state.

## Keyboard

These are the standard window controls, not screen-reader support. Hold Ctrl and press Tab to choose a panel, then release Ctrl; Tab and Shift+Tab move between its controls. Use the visible focus cue, not an old selection. Ctrl+Shift+P opens the command palette with the default bindings.

In a map or 3D viewport, Tab to the canvas outline. Enter selects the next marker/object in source order; Shift+Enter selects the previous one, wrapping at the ends. Tab leaves. Arrows move the selected movable item; each accepted step is undoable. Alt reverses snapping for that move. With no selection, arrows move the view instead.

- Map: arrow steps use the active grid, or ten pixels on an axis without a positive grid step. =/+ and - zoom about the centre. Zoom in before using Ctrl+arrows to pan. 0 resets zoom/pan to the current fitted bounds; the host's Fit control recomputes them.
- 3D viewport: arrows move on the ground axis nearest the camera's right/forward direction, leaving height unchanged. With snapping off, the step is 0.5 units. Ctrl+arrows orbit; =/+ and - zoom. There is no viewport 0-to-fit shortcut.

The main =/+ and - keys and keypad + and - work; keypad Enter works too. Movement can be refused by the binding, validation, focus or Play state. See the Files instructions above for New, Rename, Delete, Restore and the unsaved-close prompt; Escape there cancels rather than discarding a draft.

## Work in the table by keyboard

If Table is already visible, leave it open. Otherwise open the default Ctrl+Shift+P palette, search Table, choose **Show Table** and Enter. **Hide Table** closes an already-open panel; do not use it as an opening step. A closed panel also opens when one of its panel commands runs. Host/user shortcut overrides may replace the defaults. Use Ctrl+Tab to choose an editor window, then Tab to reach its controls; the exact Tab count depends on the current layout, columns and visible controls.

Choose a top-level object list. Type in the filter to match names or shown values. Tab to a column header and press Enter to sort; press Enter again to reverse. Filter and sort change the view, not source order, dirty state or undo.

Drag a header edge to resize a column, or drag a header to change its position. The item-name column stays pinned. Column widths, order and sort are remembered for each file and list in your local user layout, outside the project. Right-click a column header and choose **Reset table layout** to restore the default widths and order and return to file order. With a header focused, Menu or Shift+F10 opens the same menu; use the arrow keys and Enter to choose reset. These preferences do not create an edit or undo step. A changed set or order of schema fields starts with a fresh layout; moving the file also starts fresh.

First establish current keyboard navigation: hold Ctrl while cycling Tab to Table, release Ctrl, use Down/Down/Enter to enter its scrolling child, then Tab to the intended cell. Confirm its focus cue rather than an old selection. The executable keyboard mode above uses Table.Anchor plus the native focus ID as the checkpoint. Layout/columns change the required Tab count.

Once a cell is picked and currently focused:

| Key / action | Result |
|---|---|
| Tab / Shift+Tab | Move forward/back through editable cells. |
| Escape, then Left/Right or Up/Down | Leave text editing, then move between columns or shown rows. |
| Enter in a text/number cell | Start editing or confirm the typed value. |
| Escape while typing | Cancel the uncommitted text; do not create a document command. |
| Space on an on/off cell | Toggle it as one undoable edit. |
| Enter on a choice, arrows, Enter | Open its choices, select, then confirm; Escape cancels the choice. |
| Shift+arrows in cell-navigation mode | Extend a block from the anchor cell. |
| Ctrl+C in cell-navigation mode | Copy the block, or the picked cell, as tab-separated rows in shown order. |
| Ctrl+C while editing text | Copy the text selection instead. |

Leave the text editor before using arrows to navigate or Shift+arrows to select cells; otherwise the text caret owns those keys. Use document Undo after a confirmed edit; text-edit undo belongs to the active text editor. Copying is view/clipboard work, not a document edit. An unset cell copies as empty text.

Open the palette and run:

- **Fill a column of the shown rows...**: choose a field, enter its value, and preview. Fill affects the filtered shown rows, leaving hidden rows alone.
- **Paste the clipboard at the picked cell**: pick the top-left destination first. The clipboard must contain a plain rectangular grid: tabs between cells and lines between rows, in the current shown order.
- **Copy the picked cells**: copy the selected block or cell. Without a picked cell, Copy and Paste explain “pick a cell first”.

Preview changes nothing. Tab to Apply and press Enter to commit every planned cell as one undo step; choose Cancel to leave data/history untouched. Paste uses a dot for decimals, true/false for booleans, and choices exactly as listed. Ragged rows, overflow past shown rows/columns, bad scalar text, schema violations, focus locks or play locks refuse the whole batch. Read the row/field and rule in the table's message, correct the value or selection, then preview again. No embedded-tab/newline quoting format is promised.

Use Tab to reach a reference's Follow arrow and Enter to follow it. Tab to Back and Enter to return. Following changes selection/view, not document history. Nested objects/lists remain summaries in this table. Select their containing item and use the inspector's nested rows and fold arrows to edit them.

## Link your data

Declare links; do not rely on field names looking like IDs. Within one document, a string field's schema may say "x-ref": "/tools"; values match the target items' id field. Set "x-ref-key": "code" to use another key. Supply DataEditing.SchemaReferences(schemas, schemaName) to DataTableView.References when wiring a table yourself; DataWorkspace wires the schema resolver for its files. Missing or duplicate matches resolve to nothing. x-ref is editor metadata, not a JSON Schema foreign-key constraint.

Try the **Lantern Garden** example. Use the separate $garden root bound above; $example continues to mean catalog:

~~~powershell
New-Item -ItemType Directory -Path "$garden/data", "$garden/schemas" | Out-Null
~~~

The following PowerShell blocks write the files exactly. Keep reference-map.json beside those folders, outside data/, so it is not mistaken for a game document.

Save data/beds.json:

~~~powershell
$json = @'
{
  "beds": [
    {
      "id": "terrace",
      "label": "Terrace",
      "plots": [
        {
          "id": "patch",
          "label": "Upper patch"
        }
      ]
    },
    {
      "id": "alcove",
      "label": "Alcove",
      "plots": [
        {
          "id": "patch",
          "label": "Shaded patch"
        }
      ]
    }
  ],
  "seeds": [
    {
      "id": "fern",
      "label": "Fern spores"
    },
    {
      "id": "fern",
      "label": "Second fern packet"
    },
    {
      "id": "aster",
      "label": "Aster seeds"
    }
  ],
  "teams": {
    "tenders": {
      "label": "Tenders"
    }
  },
  "moods": [
    "ready",
    "rest"
  ]
}
'@
[IO.File]::WriteAllText("$garden/data/beds.json", $json.Replace("`r`n", "`n") + "`n", $utf8)
~~~

Save data/gardeners.json:

~~~powershell
$json = @'
{
  "gardeners": [
    {
      "name": "Mira",
      "count": 3,
      "tool": "dibber",
      "seed": "aster",
      "bed": "alcove",
      "plot": "patch",
      "pair": "terrace/alcove",
      "tagged": "note/aster",
      "missing": "unlisted",
      "duplicate": "fern",
      "none": "none",
      "unloaded": "aster",
      "scope_only": "patch",
      "team": "tenders",
      "mood": "ready",
      "inputs": [
        "aster",
        "unlisted"
      ],
      "plain": "aster"
    },
    {
      "name": "Sable",
      "seed": "fern",
      "bed": "missing-bed",
      "plot": "patch",
      "pair": "terrace/absent",
      "tagged": "only-one"
    },
    {
      "id": "unnamed",
      "note": "No template fields"
    }
  ],
  "tools": [
    {
      "code": "dibber",
      "label": "Hand dibber"
    }
  ],
  "homes": {
    "terrace": {
      "plot": "patch"
    }
  }
}
'@
[IO.File]::WriteAllText("$garden/data/gardeners.json", $json.Replace("`r`n", "`n") + "`n", $utf8)
~~~

Save schemas/beds.schema.json:

~~~powershell
$json = @'
{
  "type": "object",
  "properties": {
    "beds": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "id": {
            "type": "string"
          },
          "label": {
            "type": "string"
          },
          "plots": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "id": {
                  "type": "string"
                },
                "label": {
                  "type": "string"
                }
              },
              "required": [
                "id",
                "label"
              ],
              "additionalProperties": false
            }
          }
        },
        "required": [
          "id",
          "label",
          "plots"
        ],
        "additionalProperties": false
      }
    },
    "seeds": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "id": {
            "type": "string"
          },
          "label": {
            "type": "string"
          }
        },
        "required": [
          "id",
          "label"
        ],
        "additionalProperties": false
      }
    },
    "teams": {
      "type": "object",
      "additionalProperties": {
        "type": "object",
        "properties": {
          "label": {
            "type": "string"
          }
        },
        "required": [
          "label"
        ],
        "additionalProperties": false
      }
    },
    "moods": {
      "type": "array",
      "items": {
        "type": "string"
      }
    }
  },
  "required": [
    "beds",
    "seeds",
    "teams",
    "moods"
  ],
  "additionalProperties": false
}
'@
[IO.File]::WriteAllText("$garden/schemas/beds.schema.json", $json.Replace("`r`n", "`n") + "`n", $utf8)
~~~

Save schemas/gardeners.schema.json:

~~~powershell
$json = @'
{
  "type": "object",
  "properties": {
    "gardeners": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "name": {
            "type": "string"
          },
          "count": {
            "type": "integer",
            "minimum": 0
          },
          "id": {
            "type": "string"
          },
          "note": {
            "type": "string"
          },
          "tool": {
            "type": "string",
            "x-ref": "/tools",
            "x-ref-key": "code"
          },
          "seed": {
            "type": "string"
          },
          "bed": {
            "type": "string"
          },
          "plot": {
            "type": "string"
          },
          "pair": {
            "type": "string"
          },
          "tagged": {
            "type": "string"
          },
          "missing": {
            "type": "string"
          },
          "duplicate": {
            "type": "string"
          },
          "none": {
            "type": "string"
          },
          "unloaded": {
            "type": "string"
          },
          "scope_only": {
            "type": "string"
          },
          "team": {
            "type": "string"
          },
          "mood": {
            "type": "string"
          },
          "plain": {
            "type": "string"
          },
          "inputs": {
            "type": "array",
            "items": {
              "type": "string"
            }
          }
        },
        "required": [],
        "additionalProperties": false,
        "x-label": "name"
      }
    },
    "tools": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "code": {
            "type": "string"
          },
          "label": {
            "type": "string"
          }
        },
        "required": [
          "code",
          "label"
        ],
        "additionalProperties": false,
        "x-label": "label"
      }
    },
    "homes": {
      "type": "object",
      "additionalProperties": {
        "type": "object",
        "properties": {
          "plot": {
            "type": "string"
          }
        },
        "required": [
          "plot"
        ],
        "additionalProperties": false
      }
    }
  },
  "required": [
    "gardeners",
    "tools",
    "homes"
  ],
  "additionalProperties": false
}
'@
[IO.File]::WriteAllText("$garden/schemas/gardeners.schema.json", $json.Replace("`r`n", "`n") + "`n", $utf8)
~~~

Both files are schema-valid, including the intentionally duplicated seed ID and dangling reference strings. These schemas check shapes/types, not reference integrity. Validate them with datacheck using their data and schemas directories; expect “2 file(s) checked, 0 with problems.”

Write a reference map for cross-file links without changing the game schemas. In list/at paths, * matches any index or key. A target key names an item field; $key matches object keys, and $value matches a list of plain IDs. A source place holding a list of IDs also matches each item's pointer. The separate keys: true option treats source object keys as IDs. An also list adds target lists; multiple matches across them are Ambiguous, never guessed.

Save reference-map.json:

~~~powershell
$json = @'
{
  "game": "Lantern Garden",
  "labels": [
    {
      "file": "gardeners.json",
      "list": "/gardeners",
      "template": "{name} x{count}"
    },
    {
      "file": "beds.json",
      "list": "/beds",
      "field": "label"
    }
  ],
  "references": [
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/seed",
      "target": {
        "file": "beds.json",
        "list": "/seeds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture."
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/missing",
      "target": {
        "file": "beds.json",
        "list": "/seeds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture."
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/duplicate",
      "target": {
        "file": "beds.json",
        "list": "/seeds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture."
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/none",
      "target": {
        "file": "beds.json",
        "list": "/seeds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture.",
      "sentinels": [
        "none"
      ]
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/unloaded",
      "target": {
        "file": "absent.json",
        "list": "/seeds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture."
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/scope_only",
      "target": {
        "file": "beds.json",
        "list": "/beds/*/plots",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture.",
      "scope": "The gardener's bed; words alone do not specify how."
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/plot",
      "target": {
        "file": "beds.json",
        "list": "/beds/*/plots",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture.",
      "scope": "The bed named by the sibling bed field.",
      "narrow": {
        "by": "1/bed",
        "file": "beds.json",
        "list": "/beds",
        "key": "id"
      }
    },
    {
      "file": "gardeners.json",
      "at": "/homes/*/plot",
      "target": {
        "file": "beds.json",
        "list": "/beds/*/plots",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture.",
      "narrow": {
        "by": "1#",
        "file": "beds.json",
        "list": "/beds",
        "key": "id"
      }
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/pair",
      "target": {
        "file": "beds.json",
        "list": "/beds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture.",
      "parts": {
        "separator": "/",
        "targets": [
          {
            "file": "beds.json",
            "list": "/beds",
            "key": "id"
          },
          {
            "file": "beds.json",
            "list": "/beds",
            "key": "id"
          }
        ]
      }
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/tagged",
      "target": {
        "file": "beds.json",
        "list": "/seeds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture.",
      "parts": {
        "separator": "/",
        "targets": [
          null,
          {
            "file": "beds.json",
            "list": "/seeds",
            "key": "id"
          }
        ]
      }
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/team",
      "target": {
        "file": "beds.json",
        "list": "/teams",
        "key": "$key"
      },
      "evidence": "Invented Lantern Garden guide fixture."
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/mood",
      "target": {
        "file": "beds.json",
        "list": "/moods",
        "key": "$value"
      },
      "evidence": "Invented Lantern Garden guide fixture."
    },
    {
      "file": "gardeners.json",
      "at": "/gardeners/*/inputs",
      "target": {
        "file": "beds.json",
        "list": "/seeds",
        "key": "id"
      },
      "evidence": "Invented Lantern Garden guide fixture."
    }
  ]
}
'@
[IO.File]::WriteAllText("$garden/reference-map.json", $json.Replace("`r`n", "`n") + "`n", $utf8)
~~~

Validate and run the host on the separate garden root. Its links mode prints a Label/Resolve loop, follows a link, checks Back and attempts the ambiguous link to expose its reason.

~~~powershell
dotnet run --no-build --project $check -- "$garden/data" "$garden/schemas"
$LASTEXITCODE
dotnet run --no-build --project $hostProject -- $garden links
$LASTEXITCODE
$gardenAuthoring = @("run", "--no-build", "--project", $cli, "--", "--data", "$garden/data", "--schemas", "$garden/schemas", "--map", "$garden/reference-map.json")
& dotnet @gardenAuthoring describe
$LASTEXITCODE
& dotnet @gardenAuthoring get gardeners.json /gardeners/0/duplicate
$LASTEXITCODE
& dotnet @gardenAuthoring get gardeners.json /gardeners/0/seed
$LASTEXITCODE
~~~

Expected: 2 files checked, zero with problems; each invocation exits 0. An unresolved link reported by get is a successful query, not an edit refusal. Labels are Mira x3, Sable x and (fallback); the third outline label falls back to unnamed. The Resolve loop prints the outcomes below. ReferenceMap.Read validates map format, not every source link. Deliberate missing/duplicate links are schema-valid. --map explicitly supplies the map; location alone does not.

The evidence field records why a place is a reference. This example identifies its demonstration data. In your map, cite the repository-relative schema, validator or design rule that establishes the link; arbitrary nonempty text is not enough.

Use the new features deliberately:

- **template** replaces each {field} with that item's text, number or true/false spelling. The first gardener labels as **Mira x3**. A missing count gives **Sable x**; if all named fields are missing, Label returns null and the editor falls back to its schema/name/id/title/position rule. A template has no single LabelField.
- **parts** splits an ID at its separator and resolves each named part independently, in order. The pair terrace/alcove resolves both beds; Parts contains both targets and the main Target is the last one, /beds/1. This does not implicitly filter later parts by earlier ones. A null target means that part is not an ID: tagged skips note and resolves aster. Wrong part counts or a missing part refuse the composite, identifying the problem.
- **narrow** makes scope machine-readable. Its Relative JSON Pointer starts at the source value: 1/bed finds the sibling bed field; 1# finds the containing object's key in homes. The plot named patch appears in two beds, but the first gardener's alcove context resolves only /beds/1/plots/0. A missing context container yields Missing; multiple context matches yield Ambiguous. Scope text alone explains intent and yields NeedsScope; it does not perform filtering.

Inspect the API outcomes before assuming a link is navigable:

| Source in gardeners.json | ReferenceOutcome | Meaning |
|---|---|---|
| /gardeners/0/seed | Found → beds.json /seeds/2 | Exactly one target. |
| /gardeners/0/missing | Missing | No target ID. |
| /gardeners/0/duplicate | Ambiguous | Two seed records match. |
| /gardeners/0/none | Sentinel | Deliberate non-ID. |
| /gardeners/0/unloaded | NotLoaded | absent.json is not in this project (an unopenable target also qualifies). |
| /gardeners/0/scope_only | NeedsScope | Scope is supplied only in words. |
| /gardeners/0/plain | NotAReference | The map says nothing about that place. |

The table does not add coloured outcome badges. DataWorkspace now wires DataTableView.LinkProblem: a failed Follow includes the resolver's Note and a concrete next step. The duplicate attempt reports two fern matches and asks for different ids. Missing suggests spelling/addition; NotLoaded points to Files; NeedsScope explains words-only scope; Sentinel deliberately names nothing. Off-map values keep the generic no-target message rather than an invented reason. The links mode verifies the duplicate Note/remedy. Never assume the resolver picks the first duplicate. A Found result means the resolver found an item, not that the stock outline can navigate every depth. For ordinary top-level targets such as seed, pair and tagged here, Follow opens the target file, shows its list and selects the item. For a nested target, Follow selects the nearest item the outline knows and the workspace reports where inside it the link points. The narrowed plot here selects Alcove at /beds/1 and reports “Went to Alcove in beds.json; the link points inside it, at /plots/0.” The outline is still shallow; select that containing item and expand its inspector rows to reach the nested plot. The table itself remains a summary for containers. Back still returns across files after local Back history is spent. Neither operation edits the documents. Duplicate IDs must be fixed or disambiguated in the map/domain; the resolver never picks the first row.

The simple local tool link remains in the same gardeners document: its schema resolves dibber by code to /tools/0. Cross-file links use the supplied map/Links and GoTo host seam, not a filename embedded in x-ref. Changes to schemas/maps do not automatically establish a game's missing semantic rules.

## Check a broken file

`datacheck` checks each top-level `*.json` in the data folder; it does not recurse into subfolders. Schemas are loaded from top-level `*.json` in the schema folder. A data file's string `$schema` selects a schema by its file name; otherwise the tool uses the data file's base name plus `.schema.json`. Referenced schema files must be loaded locally. It does not fetch schemas from the web. If you put `$schema` in data checked by a closed schema, declare that data property too.

Make a separate broken copy:

```powershell
$check = "samples/DataCheck/DataCheck.csproj"
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Path "$example/broken" | Out-Null
$broken = [IO.File]::ReadAllText("$example/data/catalog.json").Replace('"quantity": 2', '"quantity": 0')
[IO.File]::WriteAllText("$example/broken/catalog.json", $broken, $utf8)
dotnet run --no-build --project $check -- "$example/broken" "$example/schemas"
$LASTEXITCODE
```

Actual output:

```text
catalog.json $.items[0].quantity: must be at least 1
1 file(s) checked, 1 with problems.
1
```

Problems include the file name and a JSON path. Files and issues are printed in a stable sorted order. Exit code **0** means valid, **1** means problems were found, and **2** means bad arguments or a file/schema that could not be checked. A missing matching schema also returns 2. A shell step can use `$LASTEXITCODE` to decide whether to continue.

Calling the tool without arguments demonstrates the usage error:

```powershell
$check = "samples/DataCheck/DataCheck.csproj"
dotnet run --no-build --project $check
$LASTEXITCODE
```

Actual output:

```text
usage: datacheck <data-folder> <schema-folder>
2
```

## Know what the validator checks

`JsonSchemas` implements a subset of draft 2020-12. Supported validation and reference keywords are:

- Values and references: `type`, `enum`, `const`, `$ref`, `$defs`.
- Objects: `properties`, `required`, `additionalProperties`, `patternProperties`, `propertyNames`, `minProperties`, `maxProperties`, `dependentRequired`.
- Arrays: `items`, `prefixItems`, `contains`, `minItems`, `maxItems`, `uniqueItems`.
- Numbers: `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`.
- Strings: `minLength`, `maxLength`, `pattern`.
- Combinations: `allOf`, `anyOf`, `oneOf`, `not`, `if`, `then`, `else`.

The annotations `$schema`, `$id`, `$comment`, `title`, `description`, `default`, `examples`, `deprecated`, `readOnly` and `writeOnly` are accepted without adding validation conditions. `$id` also supplies schema identity/base information for references. Any `x-` keyword is accepted as tool metadata, including `x-label`.

The inspector follows properties, pattern/additional properties, array items/prefix items, references, `allOf` and the selected if/then/else branch to describe a field. Validation support for `anyOf` and `oneOf` does not imply an automatic inspector form for every branch.

An unsupported keyword is reported; the value is not silently called fully checked. For example, `format` is unsupported:

```powershell
$check = "samples/DataCheck/DataCheck.csproj"
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Path "$example/unsupported-data", "$example/unsupported-schemas" | Out-Null
[IO.File]::WriteAllText("$example/unsupported-data/sample.json", "{}`n", $utf8)
[IO.File]::WriteAllText("$example/unsupported-schemas/sample.schema.json", '{"type":"object","format":"uuid"}' + "`n", $utf8)
dotnet run --no-build --project $check -- "$example/unsupported-data" "$example/unsupported-schemas"
$LASTEXITCODE
```

Actual output:

```text
sample.json $: the schema uses 'format', which this validator doesn't support, so this value wasn't fully checked
1 file(s) checked, 1 with problems.
1
```

## Four known suite limitations

The [official-suite adapter](../../tests/Bed.Tests/JsonSchemaSuiteTests.cs) lists four skipped cases. The validator does not fully conform to draft 2020-12:

| Suite file, group:case (zero-based) | Current limit |
|---|---|
| `optional/non-bmp-regex.json`, 0:0, “matches empty” | A quantified emoji pattern uses .NET UTF-16 code units rather than whole Unicode characters. |
| Same file, 0:2, “matches two” | The same pattern quantifier can repeat half a surrogate pair. |
| Same file, 1:4, “doesn't match two” | The same issue affects `patternProperties`. |
| `ref.json`, 20:0, “data is valid against nested sibling” | A nested schema's `$id` beside `$ref` changes the reference base in an evaluation order not yet modelled. |

Check such schemas with care before relying on them for authoring rules. Other unsupported draft keywords produce the explicit diagnostic shown above.

## Try it: an outside edit

Run only on the tutorial catalog. This mode refuses an existing outside-exercise folder, makes fresh private copies and leaves the original catalog untouched:

~~~powershell
dotnet run --no-build --project $hostProject -- $example outside
$LASTEXITCODE
~~~

Expected exit 0. It edits quantity 2 to 3 in memory, then independently changes the disk title to Outside edit. Save refuses and keeps both dirty draft and external text. It serializes preserved-catalog.json outside the data folder. This is an explicit recovery copy, not a normal save bypass.

Refresh only relists: the same session retains quantity 3. After verifying the recovery copy, the driver deliberately opens a **new project and workspace**. They read the external title and disk quantity 2; the recovery copy still holds draft quantity 3. Compare/reconcile before applying a new edit to real data.

Finally it gives the disposable copy a dirty quantity 4, preserves it to preserved-before-delete.json, then deletes that copied catalog externally. Save refuses, keeps the dirty draft and leaves the file absent. Its message offers **Save as** to keep your version or closing it to let it go. The source is never silently recreated.

In a graphical host, Save as with a distinct intended path preserves a draft; project policy still applies. Never automatically Close/Open on conflict: Close discards edits, and DataFile.Close alone does not rebuild cached workspace editors. Refresh does not reload schemas, merge disk text or rebase a draft. Reopen the project after schema changes, construct a fresh workspace for deliberate disk reload, and preserve needed drafts first.

## When saving is refused

Save through DataFile or DataWorkspace so project policy and outside-change checks apply. A direct DocumentSession.Save call is not a substitute for those guards.

| Condition | Result and next step |
|---|---|
| Project is read-only | Save and Save As are refused; the Save All palette action explains why. Request the intended writable project from your host. |
| Host marks this file read-only | Read its reason, such as another tool owning the generated file. Change the authoritative input through that tool. |
| File was changed outside the editor or deleted | Save is refused and your in-memory edits remain. Preserve/reconcile your work and the disk version before deciding which version to reopen. |
| File is not open | Open and validate it first; there is no session to save. |

An open, unchanged writable file's Save is a no-op: bytes and timestamp stay intact. Save All reports files it could not save; each refused file drops out before the remaining group is judged again. A valid reduced group can save. Use the [saving explanation above](#a-refused-save) to distinguish that result from a write failure and put-back.

To discard a bare DataFile session deliberately, call Close: it drops unsaved edits without writing, and the next Open reads disk into a fresh session. A DataWorkspace caches its file editors; do not assume calling DataFile.Close refreshes those cached editors. After preserving the drafts you want to keep, have the host reopen the project and construct a new workspace for a fresh disk view. Do not close/reopen automatically as a conflict remedy. Refresh only updates the listing and retains sessions; it does not merge an outside edit or rebase your unsaved work.

## Save without reformatting the whole file

`DataFormat` remembers the source it read. Its writer reuses unchanged spans verbatim, including short objects on one line, key order and number spellings such as the example's `1.0`. In the example, changing only the first quantity from 2 to 3 changes only that inline item's line; the other rows, literal **Café** and `1.0` remain intact.

Newly generated portions use two-space indentation and literal Unicode. Output keeps the file's line endings (LF for a new file; a file read with CRLF keeps CRLF on every line) and ends with a final newline, so an input without one gains it. Adding or removing a property/item can also change a neighbouring comma or container boundary; replacing a whole container can change that container's lines. Source preservation is not a promise that every structural edit changes one line, nor that other line-ending styles remain byte-identical.

Use the session for atomic writing; use DataFile/DataWorkspace for project read-only policy and outside-edit refusal. See [DataDocument and DataFormat](../../src/Bed.Data/DataDocument.cs), [JsonSchemas](../../src/Bed.Data/JsonSchemas.cs), [DataEditing](../../src/Bed.Editor/DataEditing.cs) and the [datacheck entry point](../../samples/DataCheck/Program.cs) for the current contracts.
