# Quick start: your JSON, your rules

Build a small JSON editor with a custom rule, a project check and links between files. Then preview and save an edit from the command line.

You need PowerShell, the .NET 10 SDK selected by global.json, and the repository's pinned dependencies. Start in the source repository root in a fresh checkout or source copy where .quick-start does not exist. The example uses original garden data; you do not need game files, workbooks or purchased art.

Follow the sections in order. Write both the adapter and its check before building them. See [Extension points](api/extension_points.md) for the APIs used here.

## Your data

Create the two folders:

<!-- command:folders; exit:0 -->
```powershell
New-Item -ItemType Directory -Force .quick-start/data, .quick-start/schemas
```

Write .quick-start/schemas/items.schema.json exactly as follows. score is an integer; its nonnegative rule will belong to the adapter, so the schema deliberately has no minimum.

<!-- file:.quick-start/schemas/items.schema.json -->
```json
{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"score":{"type":"integer"}},"required":["id","name","score"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}
```

Write .quick-start/data/items.json. The $schema hint is not needed: the matching items.schema.json filename selects its schema.

<!-- file:.quick-start/data/items.json -->
```json
{"items":[{"id":"seed","name":"Seed tray","score":1}]}
```

Build the two hosts. These commands restore the existing dependencies as needed; add no packages.

<!-- command:launcher-build; exit:0 -->
```powershell
dotnet build src/Brutal.Authoring.Launcher/Brutal.Authoring.Launcher.csproj --nologo -v:minimal
```

<!-- command:cli-build; exit:0 -->
```powershell
dotnet build src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --nologo -v:minimal
```

Open the project without a window first. --shot writes an image; --settings keeps this trial separate from your recent-project settings. Expect exit 0, an Opened message and .quick-start/plain.png.

<!-- command:plain-open; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Launcher/Brutal.Authoring.Launcher.csproj --no-build -- .quick-start --settings .quick-start/settings --shot .quick-start/plain.png
```

## Your rules

Create the adapter folder:

<!-- command:adapter-folder; exit:0 -->
```powershell
New-Item -ItemType Directory -Force .quick-start/adapter
```

Write .quick-start/adapter/QuickGardenAdapter.csproj. The relative reference points back to this checkout; no game project is referenced.

<!-- file:.quick-start/adapter/QuickGardenAdapter.csproj -->
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../src/Brutal.Authoring.Editor.ImGui/Brutal.Authoring.Editor.ImGui.csproj" /></ItemGroup>
</Project>
```

Write .quick-start/adapter/GardenAdapter.cs. Open attaches the nonnegative-score rule to items.json. A refused score names its field.

Configure is empty because the built-in table and inspector already edit this data. Write InventoryCheck.cs in the next section before building the adapter.

<!-- file:.quick-start/adapter/GardenAdapter.cs -->
```csharp
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
namespace QuickGarden;
public sealed class GardenAdapter : IProjectAdapter
{
    public string Name => "Quick garden";
    public bool Recognises(ProjectPaths p) => File.Exists(Path.Combine(p.Data, "items.json"));
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas,
        rules: file => file == "items.json" ? new NonNegative() : null);
    public IEnumerable<ICheck> Checks => [new InventoryCheck()];
    public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    private sealed class NonNegative : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d)
        {
            var issues = new List<Issue>();
            if (d.Root.ValueKind == JsonValueKind.Object && d.Root.TryGetProperty("items"u8, out var items) && items.ValueKind == JsonValueKind.Array)
                for (var i = 0; i < items.GetArrayLength(); i++)
                    if (items[i].ValueKind == JsonValueKind.Object && items[i].TryGetProperty("score"u8, out var score) && score.ValueKind == JsonValueKind.Number && score.GetDouble() < 0)
                        issues.Add(Issue.Error("Score must be zero or higher.", path: $"/items/{i}/score"));
            return issues;
        }
    }
}
```

## Your checks

Write .quick-start/adapter/InventoryCheck.cs. It counts a snapshot of the items and reports an empty inventory without editing the project.

This check is advisory, not a mandatory save rule. The validator above refuses negative scores on each edit.

<!-- file:.quick-start/adapter/InventoryCheck.cs -->
```csharp
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
namespace QuickGarden;
public sealed class InventoryCheck : ICheck
{
    public CheckDefinition Definition => new("garden.inventory", "Garden inventory",
        "Counts the frozen items list and reports an empty inventory.", TimeSpan.FromSeconds(2));
    public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (snapshot.Text("data/items.json") is not { } text)
            return Task.FromResult(new CheckReport(false,
                [new(Issue.Error("items.json is missing."), new("items.json"), "Restore items.json and run again.")],
                "The inventory could not be read."));
        using var json = JsonDocument.Parse(text);
        var count = json.RootElement.GetProperty("items"u8).GetArrayLength();
        CheckFinding[] findings = count == 0
            ? [new(Issue.Warning("The inventory is empty.", path: "/items"), new("items.json", At: "/items"), "Add a garden item and run again.")]
            : [];
        return Task.FromResult(new CheckReport(true, findings, $"Counted {count} garden items."));
    }
}
```

Build the adapter:

<!-- command:adapter-build; exit:0 -->
```powershell
dotnet build .quick-start/adapter/QuickGardenAdapter.csproj --nologo -v:minimal
```

List its checks, then run the one you added. The listing should contain garden.inventory; the run should complete, count one item and say clean=true. Completion alone is not a clean result when a check has findings or incomplete input.

<!-- command:checks; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll checks
```

<!-- command:check; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll check garden.inventory
```

## Links between files

Write .quick-start/schemas/recipes.schema.json and .quick-start/data/recipes.json. uses stores an item's id; the map supplies its meaning, rather than changing the stored field into another representation.

<!-- file:.quick-start/schemas/recipes.schema.json -->
```json
{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"recipes":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"uses":{"type":"string"}},"required":["id","name","uses"],"additionalProperties":false}}},"required":["recipes"],"additionalProperties":false}
```

<!-- file:.quick-start/data/recipes.json -->
```json
{"recipes":[{"id":"sowing","name":"Start a tray","uses":"seed"}]}
```

Write .quick-start/reference_map.json. evidence names where the relationship is authored; it is required. /recipes/*/uses points into items.json's /items list using id as its key. Labels make records readable. A map resolves links; it does not grant permissions or implement cascading rename/delete.

<!-- file:.quick-start/reference_map.json -->
```json
{"game":"Quick garden","labels":[{"file":"items.json","list":"/items","field":"name"},{"file":"recipes.json","list":"/recipes","field":"name"}],"references":[{"file":"recipes.json","at":"/recipes/*/uses","target":{"file":"items.json","list":"/items","key":"id"},"evidence":"docs/quick_start.md: Links between files; recipes.uses stores an item id"}]}
```

Reopen without a window, this time with the adapter. The project folder automatically selects reference_map.json. Expect Quick garden in the Opened message and .quick-start/linked.png.

<!-- command:linked-open; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Launcher/Brutal.Authoring.Launcher.csproj --no-build -- .quick-start --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll --settings .quick-start/settings --shot .quick-start/linked.png
```

Follow the stored id through the CLI. Expect link.outcome=Found, file=items.json and at=/items/0.

<!-- command:link; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --map .quick-start/reference_map.json --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll get recipes.json /recipes/0/uses
```

## An AI on your data

The same adapter attaches the rule and check to typed AI calls. Start with describe: it explains the files, calls and next steps.

<!-- command:describe; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --map .quick-start/reference_map.json --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll describe
```

Preview a negative score. **Exit 1 is expected here:** the refused list must contain /items/0/score and Score must be zero or higher. Nothing changes on disk. Do not treat this expected refusal as a failed build or replace the schema/rule to make it pass.

<!-- command:refused; exit:1 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --map .quick-start/reference_map.json --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll set items.json /items/0/score -1 --preview
```

Preview 2 instead, then deliberately save that edit. Without --save a standalone CLI process discards its draft; the next command does not inherit it. --preview changes nothing even when validation accepts it.

<!-- command:preview; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --map .quick-start/reference_map.json --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll set items.json /items/0/score 2 --preview
```

<!-- command:save; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --map .quick-start/reference_map.json --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll set items.json /items/0/score 2 --save
```

Read it in a fresh process: value should be 2.

<!-- command:reopen-value; exit:0 -->
```powershell
dotnet run --project src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj --no-build -- --data .quick-start/data --schemas .quick-start/schemas --map .quick-start/reference_map.json --adapter .quick-start/adapter/bin/Debug/net10.0/QuickGardenAdapter.dll get items.json /items/0/score
```

Use the CLI's mcp mode to serve these calls over stdio to your configured AI client. The server has no sign-in of its own; you configure the client separately.

--read-only with --adapter is unsupported and refused. Load only adapters you trust: they execute code in your process and are not sandboxed.

## What you'll do in the window

To open a window, use the same launcher command with the project, adapter and settings arguments, but leave out --shot and its image path. This opens an interactive desktop application.

Open items.json, select Seed tray and inspect its fields. Try the refused negative score, then a valid edit; use Undo/Redo and Save. In recipes.json, select uses and follow its link back to the item. Find Garden inventory in the checks panel, Run it and inspect findings/next steps. Verify the selected record and pending save status before closing. The generic table/inspector, command palette and check UI are supplied by the host; this adapter adds no special panel.

This example edits data; it does not create runtime gameplay or integrate a game. Review the window workflow and any AI-client setup separately.

The repository uses MIT. Naming, Ahwoo's approval and public release still need the user's decision.
