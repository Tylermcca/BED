# Add a planar map over your own document

Build a map with movable pins and a read-only line, save its JSON plan, and produce a headless image. The example uses the public [map binding contract](../../src/Brutal.Authoring.Editor/MapModel.cs), validation and undo history. Start with [the linked-project walkthrough](start_here.md) when you need a map inside an adapter. The plan and binding are self-authored.

Use .NET SDK 10 and a local checkout of this tools repository with its existing BRUTAL dependencies available. Set the environment variable BRUTAL_AUTHORING to that checkout's absolute path before starting. No game checkout, workbook, art or new package is needed. Dependencies are those already declared in the referenced library projects; their notices/licences remain in `THIRD-PARTY-NOTICES.md` and `NOTICE.txt`. This tutorial is not a packaging or publication recipe.

## Start empty

From an empty working directory, run this PowerShell block. NeutralMap must not already exist.

~~~powershell
if (-not (Test-Path -LiteralPath "$env:BRUTAL_AUTHORING/src/Brutal.Authoring.Editor/MapModel.cs")) {
    throw 'Set BRUTAL_AUTHORING to your local BED checkout first.'
}
New-Item -ItemType Directory -Path NeutralMap -ErrorAction Stop | Out-Null
Set-Location -LiteralPath NeutralMap
dotnet new console --framework net10.0 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Console project creation failed.' }
~~~

Replace the generated NeutralMap.csproj with this entire project. MSBuild reads BRUTAL_AUTHORING from your environment.

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(BRUTAL_AUTHORING)/src/Brutal.Authoring.Data/Brutal.Authoring.Data.csproj" />
    <ProjectReference Include="$(BRUTAL_AUTHORING)/src/Brutal.Authoring.Editor.App/Brutal.Authoring.Editor.App.csproj" />
  </ItemGroup>
</Project>
~~~

Replace Program.cs with this entire source. The sample keeps a canonical height field but moves only the two declared planar axes. The line is read-only; it never creates a connection by crossing or proximity.

~~~csharp
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Editor.App;
using Brutal.ImGuiApi;
using Brutal.Numerics;

// INVENTED neutral plan; no game records or assets.
var format = new DataFormat();
var rules = new PlanRules();
var catalog = DataKit.Catalog();
var path = Path.GetFullPath("plan.json");
var initial = format.Read("""
{
  "pins": [
    { "id": "amber", "x": 0, "y": 0, "height": 3 },
    { "id": "blue", "x": 100, "y": 60, "height": 7 }
  ]
}
""");
var session = File.Exists(path)
    ? DocumentSession<DataDocument>.Open(path, format, rules, catalog)
    : new DocumentSession<DataDocument>(format, rules, catalog, initial);
session.Save(path); // establish a file for explicit later saves/outside-edit checks
var editor = new EditorModel<DataDocument>(session,
    new FlatOutline<DataDocument>(d => PointMap.Pins(d)
        .Select(p => (Id: p.GetProperty("id"u8).GetString()!, Label: p.GetProperty("id"u8).GetString()!))));
var map = new MapCanvas<DataDocument>(editor, new PointMap());
void Draw()
{
    ImGui.SetNextWindowPos(new float2(10, 10), ImGuiCond.Always);
    ImGui.SetNextWindowSize(new float2(850, 600), ImGuiCond.Always);
    ImGui.Begin("Neutral plan"u8);
    if (ImGui.Button("Fit"u8)) map.Fit();
    ImGui.SameLine();
    if (ImGui.Button("Undo"u8)) session.Undo();
    ImGui.SameLine();
    if (ImGui.Button("Redo"u8)) session.Redo();
    ImGui.SameLine();
    if (ImGui.Button("Save"u8))
    {
        try { session.Save(); map.Said = "Saved plan.json."; }
        catch (Exception e) { map.Said = "Couldn't save plan.json: " + e.Message + " Resolve the file conflict, then retry."; }
    }
    ImGui.TextWrapped("Click a pin or Tab to the map and press Enter to select; Shift+Enter goes back. Drag or use arrows to move. Alt toggles snap. Wheel or =/- zoom; right drag or Ctrl+arrows pan after zooming. 0 resets the view; Fit refits. Tab leaves; Escape cancels a drag."u8);
    if (map.Said.Length > 0) ImGui.TextWrapped(map.Said);
    foreach (var problem in map.Problems) ImGui.TextWrapped(problem);
    map.Draw(450);
    ImGui.End();
}
// Optional interactive host. The recorded walkthrough runs only the headless branch.
if (args.Contains("--window"))
{
    EditorApp.Run(new("Neutral plan", 900, 650),
        io => EditorWindow<DataDocument>.Configure(io), Draw);
    return;
}
using var host = new HeadlessHost(900, 650);
host.Frames(Draw, 4);
var key = PointMap.Key("amber");
var at = map.ScreenOf(key) ?? throw new Exception("Amber was not drawn.");
host.Click(Draw, at.X, at.Y);
if (editor.Primary != "amber") throw new Exception("The real click did not select amber.");
var before = format.Write(session.Current);
var blue = session.Current.At("/pins/1")!.Value.Clone();
using var nativeBefore = JsonDocument.Parse(before);
var blueText = nativeBefore.RootElement.GetProperty("pins"u8)[1].GetRawText();
host.Drag(Draw, at, at + new float2(30, 15));
var moved = format.Write(session.Current);
if (before == moved || session.History.UndoCount != 1) throw new Exception("Expected one move.");
if (!JsonSchemas.Same(session.Current.At("/pins/1")!.Value, blue)) throw new Exception("Blue values changed.");
using var nativeAfter = JsonDocument.Parse(moved);
if (nativeAfter.RootElement.GetProperty("pins"u8)[1].GetRawText() != blueText)
    throw new Exception("Blue serialized text changed.");
if (session.Current.At("/pins/0/height")!.Value.GetInt32() != 3) throw new Exception("Height changed.");
if (!session.Undo() || format.Write(session.Current) != before) throw new Exception("Undo failed.");
if (!session.Redo() || format.Write(session.Current) != moved) throw new Exception("Redo failed.");
session.Save();
var reopened = DocumentSession<DataDocument>.Open(path, format, rules, catalog);
if (!JsonSchemas.Same(reopened.Current.Root, session.Current.Root)) throw new Exception("Reload failed.");
using var disk = JsonDocument.Parse(File.ReadAllText(path));
if (disk.RootElement.GetProperty("pins"u8)[1].GetRawText() != blueText)
    throw new Exception("Blue disk text changed.");
host.Frames(Draw);
host.Save("map.png");
Console.WriteLine("PASS: real click + drag, one undo/redo step, other pin/height preserved, save/reload; map.png.");

sealed class PlanRules : IValidator<DataDocument>
{
    public IReadOnlyList<Issue> Validate(DataDocument d)
    {
        var pins = PointMap.Pins(d).ToArray();
        if (pins.Select(p => p.GetProperty("id"u8).GetString()).Distinct(StringComparer.Ordinal).Count() != pins.Length)
            return [Issue.Error("Pin ids must be unique.")];
        return pins.All(p => new MapPoint(p.GetProperty("x"u8).GetDouble(), p.GetProperty("y"u8).GetDouble()).IsFinite)
            ? [] : [Issue.Error("Pin coordinates must be finite.")];
    }
}
sealed class PointMap : IMapSource<DataDocument>
{
    public static IEnumerable<JsonElement> Pins(DataDocument d) => d.At("/pins")!.Value.EnumerateArray();
    public static MapKey Key(string id) => new("plan.json", "pin", id);
    public MapSnapshot Read(DataDocument document)
    {
        // Fresh owned arrays; never mutate these after returning them.
        var markers = Pins(document).Select(p => {
            var id = p.GetProperty("id"u8).GetString()!;
            return new MapMarker(Key: Key(id), OutlineId: id, Label: id,
                Position: new(p.GetProperty("x"u8).GetDouble(), p.GetProperty("y"u8).GetDouble()));
        }).ToArray();
        return new(new("plan-local", 0, "metres", "X", "Y"), markers,
            [new(new("plan.json", "line", "amber-blue"), Key("amber"), Key("blue"))]);
    }
    public MapEdit<DataDocument>? MoveBy(DataDocument document, MapKey key, MapPoint delta)
    {
        if (key.Scope != "plan.json" || key.Kind != "pin" || !delta.IsFinite) return null;
        // Resolve the current index by stable id, never retain an index from the press.
        var found = Pins(document).Select((pin, index) => (pin, index))
            .Where(p => p.pin.GetProperty("id"u8).GetString() == key.Id).ToArray();
        if (found.Length != 1) return null;
        var (pin, i) = found[0];
        return new([
            new SetValue($"/pins/{i}/x", JsonSerializer.SerializeToElement(pin.GetProperty("x"u8).GetDouble() + delta.X)),
            new SetValue($"/pins/{i}/y", JsonSerializer.SerializeToElement(pin.GetProperty("y"u8).GetDouble() + delta.Y))
        ], [key.Id]); // every changed outline identity; trusted binding, not agent-supplied touches
    }
}

~~~

Build and exercise the exact same source without opening a native window:

~~~powershell
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet run -c Release --no-build -- --headless
if ($LASTEXITCODE -ne 0) { throw 'Map proof failed.' }
~~~

The proof prints PASS and creates map.png plus plan.json in NeutralMap. It uses a real ImGui context, mouse events and command history, without OS input or a window. It checks click selection, translation, one-step undo/redo, unchanged other pin and height, and save/reload equality. For interactive use when windows are appropriate, the built program also accepts the opt-in --window flag; the recorded walkthrough did not launch that branch. A real window needs the host's usual Vulkan/BRUTAL setup; see the public EditorApp doc comments.

## Own the binding

The current public types are in `src/Brutal.Authoring.Editor/MapModel.cs`, the controller in `src/Brutal.Authoring.Editor.ImGui/MapCanvas.cs`, and the broker in `src/Brutal.Authoring.Editor/EditorModel.cs`.

- MapKey is scope + kind + stable local id. Scope separates files/namespaces. Labels, outlines and array indices are not durable keys.
- MapMarker.OutlineId names the current editor outline item, not its durable map key. This sample's custom FlatOutline uses native ids, so OutlineId and MapEdit.Touches use the pin id. DataWorkspace's stock JSON outline instead uses current pointers such as /depots/0: keep Key as the stable depot id, but derive OutlineId and Touches from its freshly resolved array index. The [linked-project adapter](start_here.md#map-inside-the-adapter) shows that variant in a complete binding.
- Read supplies an owned, frozen snapshot for a frame; arrays must have unique keys and remain unchanged after return. Frame Id identifies the coordinate system; increment Version when its meaning changes. This cancels an old drag and refits bounds. The current implementation retains the numeric zoom when refitting; it does not reset Zoom to 1 merely because Version changes.
- MoveBy resolves a key against the current document, adds a double delta to canonical numbers, and returns commands plus every changed outline identity. Preserve all other axes and fields. Binding-specific rounding belongs here, with its own precision contract; repeated rounded small moves may vanish or drift.
- The canvas handles pointer selection, overlap click cycling, ghost translation, optional snap, wheel zoom and right pan. Tab reaches the canvas; Enter and Shift+Enter select markers in source order, including overlapping markers. Arrows move a selected movable marker; Ctrl+arrows pan after zooming in. With no selected marker, arrows pan too. =/+ and - zoom about the centre, 0 resets zoom/pan, and Tab leaves. Alt reverses the grid setting for the move. Each accepted arrow step or completed drag calls ExecuteAt with its captured revision and makes one undo step. Empty/null edits add no history. See [Keyboard](editing_data.md#keyboard).
- ExecuteAt checks play, focus, revision and declared identity existence, then commits the batch as one session undo step. It still trusts Touches and opaque command bodies. Keep this binding trusted; derive edits/touched identities from trusted semantic providers for delayed agent requests. It is not a sandbox for arbitrary commands.
- Native schemas, whole-candidate validation, reference rules, safe save and graph operations stay with the host. A bare DocumentSession is enough for this neutral local file; a production multi-file host should keep its DataProject/DataFile SaveCheck/FileCheck authority and outside-change protections. The map does not save automatically.
- Fit is explicit in the toolbar and recomputes the bounds. Tab to Fit and press Enter when new markers outside the old bounds need to become visible. The canvas's 0 key only resets zoom and pan to the current fitted bounds; it does not recompute them. Do not continuously fit every idle frame. Numeric entry/list navigation must remain available if a spatial view cannot represent the native scale.

### Embed the map in a project

Use the EditorModel supplied to IProjectAdapter.Configure; its session is already the DataFile session. Do not create a second session, and do not copy this standalone sample's session.Save into an adapter. The stock workspace Save stays on DataFile.Save and the configured SaveCheck/FileCheck authority; a custom save button must call that same DataFile.Save, or DataProject.SaveAll for coordinated drafts.

The [complete two-file example](start_here.md#map-inside-the-adapter) shows Configure, the shared editor, a cross-file SaveCheck and the actual guarded save proof together. Use PanelSide.Centre for a large map and map.Draw(Math.Max(160, ImGui.GetContentRegionAvail().Y)) to use the available panel height. Width is an initial floating-size hint; a right dock can remain narrow regardless of a large Width value.

Working-document JsonElement.GetRawText is not a persistence boundary: an edit can rebuild its in-memory formatting while preserving the saved source span. The indented input above is checked with JsonSchemas.Same for unchanged values, DataFormat.Write for serialized sibling text and File.ReadAllText after Save for actual disk text. Do not replace the value check with only a formatting comparison.

The source uses one concrete canvas and ExecuteAt rather than speculative IMapCanvas/IMapEdits interfaces. That is acceptable for this first implementation: the source adapter is replaceable and authority is already shared. Add an abstraction only for a concrete second controller/broker need, with compatibility tests. The missing trusted identity derivation is a separate incomplete requirement, not solved by adding interface names.

## Limits to plan for

The canvas diagnoses repeated marker keys and refuses moving any copy of that key; the frame continues. Non-finite positions are omitted with a reason. A finite span whose subtraction overflows is refused with “The positions are too far apart to show.” This is a bounded range check, not a guarantee that every finite scale is useful on screen.

A grid with a negative or non-finite step, or a non-finite origin, refuses movement with a reason. A marker that disappears or becomes immovable during a drag cancels that drag, as does a document revision or frame identity/version change. Read MapCanvas.Said for the latest move/refusal and its Problems for provider problems. Fix the binding or input rather than bypassing native validation.

Validate the source's keys, finite/rebased coordinates and grid before exposing a production gesture. Grids need finite nonnegative steps and finite origins; zero leaves an axis free. A positive grid snaps a drag on release and an arrow move on that key step; Alt inverts Enabled. Screen pixels narrow to float, canonical positions/deltas are double; unsupported ranges need a diagnostic/refusal. The current tiny-height path clamps to at least 64 pixels rather than creating an actually zero-size canvas.

This is planar translation. It provides **no rotation, height editing, collision solver or 3D placement/occlusion**. Edges are drawings, not a graph editor; create/delete/link/collision rules and exact numeric editing are host tools. Coincident markers cycle with repeated clicks within two pixels; a press that becomes a drag uses the candidate selected by that press. The current source supplies no separate persistent candidate-list UI or public disposal/capture owner.

## What the example checks

The headless branch checks click/drag, one-step undo/redo, preserved height and the other marker, and saved/reopened equality. Read the result and inspect map.png after running it. The optional window branch needs separate interactive verification. This small example does not exercise every malformed provider or coordinate range.
