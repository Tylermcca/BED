# Write an adapter for your game

For the complete two-file data/reference/map route, begin at the [developer walkthrough](developer_walkthrough.md).

## What an adapter is

A project with `data/` and `schemas/` opens in the general app without an adapter. The schemas provide its ordinary forms and validation. Add an `IProjectAdapter` when your game needs domain rules, project checks or extra editor panels. It runs inside the tools process.

Build **Lantern Yard**, a small example with two lamps, a brightness rule, a check and a panel. The example is self-authored and uses the tools' public APIs; no game records or art are used. For everyday editing, see [Editing data](editing_data.md). For tools embedded in your own application, see [Writing a plugin](writing_a_plugin.md); this page covers the general app's DLL loader.

You need Windows, PowerShell 7, the .NET 10 SDK, and a working BED checkout with its existing dependencies available. Run every PowerShell block below, in order, in **one shell initially located in an empty working directory, with BRUTAL_AUTHORING set to that checkout**. They create a fresh temporary folder, build a development host and adapter there, and take offscreen PNGs. They do not launch an OS window or write build outputs into the checkout. The save exercise writes only an additional disposable copy; the original example stays unchanged. This is a developer build, not a self-contained release package.

## Your first adapter

### Smallest compiling adapter

The complete Lantern Yard example below includes rules, checks and a panel. If you only need the contract's starting point, this is the smallest useful implementation. Put it in its own class-library project using the same project reference and target framework as P02. Do not add it to the Lantern Yard assembly: that would intentionally load two adapters. It uses the default false Recognises and empty Checks, so choose it explicitly in the launcher's adapter selector (a DLL argument only loads candidates) or use the agent's explicit --adapter route. It adds no game rules or panel; schemas still apply.

```csharp
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;

// BORROWED adapter contract; INVENTED minimal Garden name.
public sealed class PlainGardenAdapter : IProjectAdapter
{
    public string Name => "Plain Garden";
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
}
```

The first block records the checkout location, then enters a brand-new example folder. Keep that shell open: later blocks use its variables and helper. Restore may fetch the checkout's already-declared dependencies if they are absent from your package cache; this example adds no packages.

```powershell
# P01 - Run all blocks in one PowerShell 7 shell, starting in an empty folder; set BRUTAL_AUTHORING first.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$authoringRoot = $env:BRUTAL_AUTHORING
if (!(Test-Path -LiteralPath (Join-Path $authoringRoot 'Bed.slnx'))) {
    throw 'Set BRUTAL_AUTHORING to the tools checkout.'
}
$work = Join-Path $env:TEMP ('lantern-yard-guide-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
Set-Location -LiteralPath $work
function Save-Demo([string]$relative, [string]$text) {
    $target = Join-Path $work $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::WriteAllText($target, $text.Replace("`r`n", "`n").TrimEnd() + "`n", [Text.UTF8Encoding]::new($false))
}
# Keep all intermediate outputs and existing-package restores in this throwaway folder.
$env:UseArtifactsOutput = 'true'
$env:IncludeProjectNameInArtifactsPaths = 'true'
$env:ArtifactsPath = Join-Path $work 'build'
$env:DOTNET_CLI_HOME = Join-Path $work 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $work 'nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $work 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $work 'nuget-plugins'
$env:RestoreFallbackFolders = Join-Path $env:USERPROFILE '.nuget/packages'
$env:TEMP = Join-Path $work 'tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$project = Join-Path $work 'lantern-yard'
Write-Output "Example folder: $work"
```

The next block writes the complete project, class, data and schema. Paste it whole. The assembly name comes from `LanternYardAdapter.csproj`; the folder loader needs its filename to end in `Adapter.dll`. The project references the public editor project through `AuthoringRoot`, supplied at build time. It targets the same runtime as the host. Only the UI managed-string registration hint is suppressed; build errors still fail the commands.

```powershell
# P02 - Complete project, adapter, data and schema.
Save-Demo 'adapter/LanternYardAdapter.csproj' @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDependencyFile>true</GenerateDependencyFile>
    <!-- Public registration APIs take managed strings; suppress only this UI-literal hint. -->
    <NoWarn>$(NoWarn);BRUTALCORE0011</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(AuthoringRoot)/src/Bed.Editor.ImGui/Bed.Editor.ImGui.csproj" />
  </ItemGroup>
</Project>
'@
Save-Demo 'adapter/LanternYardAdapter.cs' @'
using System.Text.Json;
using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;

namespace LanternYard;

/// <summary>INVENTED lantern data, wired through the existing public adapter interface.</summary>
public sealed class LanternYardAdapter : IProjectAdapter
{
    /// <inheritdoc />
    public string Name => "Lantern Yard";

    /// <inheritdoc />
    public bool Recognises(ProjectPaths paths)
    {
        var file = Path.Combine(paths.Data, "lamps.json");
        if (!File.Exists(file)) return false;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "lantern-yard";
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <inheritdoc />
    public DataProject Open(ProjectPaths paths)
    {
        var project = DataProject.Open(paths.Data, paths.Schemas,
            rules: file => file == "lamps.json" ? new IntensityRule() : null);
        // Every example file uses this schema, including names that cannot carry a $schema field.
        project.SchemaFor = _ => "lamps.schema.json";
        // INVENTED budget over the proposed persisted result: only this save's candidates replace disk.
        project.SaveCheck = file =>
        {
            var result = Saved(paths.Data);
            foreach (var candidate in project.BeingSaved(file))
                result[candidate.Name] = candidate.Session!.Current.Root;
            return Budget(result);
        };
        project.FileCheck = change =>
        {
            var result = Saved(paths.Data);
            if (change.Kind is FileChangeKind.Delete or FileChangeKind.Rename)
                result.Remove(change.Name);
            if (change.Kind is FileChangeKind.Create or FileChangeKind.Restore)
                result[change.Name] = Read(change.Text!);
            if (change.Kind is FileChangeKind.Rename)
                result[change.NewName!] = Read(File.ReadAllText(Path.Combine(paths.Data, change.Name)));
            return Budget(result);
        };
        return project;
    }

    private static JsonElement Read(string text)
    {
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static Dictionary<string, JsonElement> Saved(string folder) =>
        Directory.EnumerateFiles(folder, "*.json").ToDictionary(path => Path.GetFileName(path)!,
            path => Read(File.ReadAllText(path)), StringComparer.Ordinal)!;

    private static string? Budget(Dictionary<string, JsonElement> result)
    {
        if (!result.ContainsKey("lamps.json")) return "Keep lamps.json: the yard needs its main file.";
        var total = result.Values.Sum(Intensity);
        return total > 5 ? "Yard budget 5 exceeded; lower a lamp's intensity and save again." : null;
    }

    private static long Intensity(JsonElement root) =>
        root.TryGetProperty("lamps", out var lamps) && lamps.ValueKind == JsonValueKind.Array
            ? lamps.EnumerateArray().Sum(lamp => (long)lamp.GetProperty("intensity").GetInt32()) : 0L;

    /// <inheritdoc />
    public IEnumerable<ICheck> Checks => [new UniqueNames()];

    /// <inheritdoc />
    public void Configure(EditorModel<DataDocument> editor, DataTableView table)
    {
        if (table.File != "lamps.json") return;
        editor.Add(new Panel("yard.summary", "Lantern Yard", () =>
        {
            ImGui.Text("Each lamp's intensity must fit its own limit."u8);
            ImGui.Text($"Lamp count: {editor.Session.Current.Root.GetProperty("lamps").GetArrayLength()}");
        }, Width: 320, Height: 160, Side: PanelSide.Right));
    }

    private sealed class IntensityRule : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument document)
        {
            var issues = new List<Issue>();
            if (document.Root.ValueKind != JsonValueKind.Object
                || !document.Root.TryGetProperty("lamps", out var lamps)
                || lamps.ValueKind != JsonValueKind.Array) return issues; // schema explains shape errors
            var index = 0;
            foreach (var lamp in lamps.EnumerateArray())
            {
                if (lamp.ValueKind == JsonValueKind.Object
                    && lamp.TryGetProperty("intensity", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intensity)
                    && lamp.TryGetProperty("limit", out var bound) && bound.ValueKind == JsonValueKind.Number && bound.TryGetInt32(out var limit)
                    && intensity > limit)
                    issues.Add(Issue.Error($"Intensity {intensity} exceeds limit {limit}; lower intensity or raise limit.",
                        $"/lamps/{index}", $"/lamps/{index}/intensity"));
                index++;
            }
            return issues;
        }
    }

    private sealed class UniqueNames : ICheck
    {
        public CheckDefinition Definition { get; } =
            new("yard.unique_names", "Unique lamp names",
                "Checks only name uniqueness, not every schema or game rule.", TimeSpan.FromSeconds(3));

        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var text = snapshot.Text("data/lamps.json");
            if (text is null) return Task.FromResult(new CheckReport(false, [], "lamps.json is missing; no complete name audit."));
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("lamps", out var lamps) || lamps.ValueKind != JsonValueKind.Array)
                return Task.FromResult(new CheckReport(false, [], "There is no lamps array; repair the data first."));
            var names = lamps.EnumerateArray().Select(l =>
                l.ValueKind == JsonValueKind.Object && l.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()! : "").ToArray();
            var repeated = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var findings = new List<CheckFinding>();
            for (var i = 0; i < names.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                if (names[i].Length == 0 || repeated.Contains(names[i]))
                    findings.Add(new CheckFinding(Issue.Error(names[i].Length == 0 ? "Give this lamp a name." : $"Lamp name '{names[i]}' appears more than once."),
                        new CheckPlace("lamps.json", $"/lamps/{i}", $"/lamps/{i}/name"), "Choose a distinct nonblank name for this lamp."));
            }
            progress.Report(new CheckProgress(1, "Read every lamp name."));
            return Task.FromResult(new CheckReport(true, findings, $"{findings.Count} name problem(s)."));
        }
    }
}

'@
Save-Demo 'lantern-yard/data/lamps.json' @'
{"kind":"lantern-yard","lamps":[{"id":"north","name":"Signal","limit":5,"intensity":2},{"id":"south","name":"Signal","limit":4,"intensity":1}]}
'@
Save-Demo 'lantern-yard/schemas/lamps.schema.json' @'
{"type":"object","required":["kind","lamps"],"properties":{"kind":{"const":"lantern-yard","type":"string"},"lamps":{"type":"array","items":{"type":"object","required":["id","name","limit","intensity"],"properties":{"id":{"type":"string","minLength":1},"name":{"type":"string","minLength":1},"limit":{"type":"integer","minimum":0},"intensity":{"type":"integer","minimum":0}},"additionalProperties":false}}},"additionalProperties":false}
'@
```

Build both projects, then load the adapter explicitly. `--settings` keeps preferences and recent projects in the example folder. `--shot` renders headlessly and exits; open the resulting `explicit.png` as an image when you want to inspect it. `--adapter` accepts a DLL path and can be repeated. It loads candidates; Automatic selection still calls `Recognises` on them.

```powershell
# P03 - Build the development host and the adapter, using only existing tools dependencies.
dotnet build (Join-Path $authoringRoot 'src/Bed.Launcher/Bed.Launcher.csproj') --output (Join-Path $work 'app') --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Host build failed.' }
dotnet build (Join-Path $work 'adapter/LanternYardAdapter.csproj') "-p:AuthoringRoot=$authoringRoot" --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Adapter build failed.' }
$appFolder = Join-Path $work 'app'
$appExe = Join-Path $appFolder 'BED.exe'
$adapterDll = Join-Path $env:ArtifactsPath 'bin/LanternYardAdapter/debug/LanternYardAdapter.dll'
& $appExe $project --adapter $adapterDll --settings (Join-Path $work 'settings-app') --shot (Join-Path $work 'explicit.png')
if ($LASTEXITCODE -ne 0) { throw 'Explicit adapter open failed.' }
```

For folder discovery, put your adapter under the host's `adapters/` directory, alongside its private dependencies, or in a folder of your own that you choose in **Preferences > Locations**, next to **Extra adapters**. Discovery is recursive and looks for `*Adapter.dll`, so `LanternYard.dll` would be missed. An explicitly supplied DLL does not need that suffix. Here only the adapter and its dependency description are copied; the development host already supplies the tools assemblies. Do not replace those host assemblies with incompatible copies. When your adapter has additional private libraries, ship those too and keep host and adapter versions compatible.

```powershell
# P04 - The folder loader searches recursively for *Adapter.dll.
$installed = Join-Path $appFolder 'adapters/lantern-yard'
New-Item -ItemType Directory -Path $installed -Force | Out-Null
Copy-Item -LiteralPath $adapterDll -Destination $installed
Copy-Item -LiteralPath (Join-Path (Split-Path $adapterDll) 'LanternYardAdapter.deps.json') -Destination $installed
& $appExe $project --settings (Join-Path $work 'settings-app') --shot (Join-Path $work 'autoload.png')
if ($LASTEXITCODE -ne 0) { throw 'Folder-discovered adapter open failed.' }
```

The second shot, `autoload.png`, must show the same Lantern Yard panel. Loading the same full DLL path through more than one route is deduplicated. Avoid shipping two different assemblies that claim the same project. If several candidates recognise it, the start screen can require a choice; give them clear, stable names.

## Your game's rules

`Recognises` should be quick, read-only and specific. This example reads a marker from `data/lamps.json`, handles missing or malformed input and checks JSON kinds before reading strings. Match your own file layout or marker rather than every JSON project. Returning false lets another adapter or the plain editor open the folder.

`Open` returns a `DataProject`. Its file-to-validator function adds the intensity rule only to `lamps.json`; schema validation still checks types, required fields and minimums. The domain validator checks that each intensity fits that row's limit. Its `Issue` includes row and cell pointers so the editor can show the refusal at the right place. Validators report problems; they must not silently repair the document or write to disk.

The proof below attempts intensity 6 against limit 5. It must be refused, leave the session clean and print the rule's actual explanation. A valid rename later changes only the draft. The example schema and rule are intentionally small; uniqueness is a separate check here.

## A check before saving

Use a schema or per-file rule for cheap conditions that must hold after every edit, such as a lamp exceeding its own limit. Use an ICheck for an explicit read-only audit with progress, cancellation, findings and stale-result tracking. Running that audit does not automatically block Save.

Use DataProject.SaveCheck for a whole-project or expensive final permission before a dirty file is saved. Return null only when it may be written; return a useful reason when it must stay unsaved. The complete Open method above wires the invented Lantern Yard budget: BeingSaved(file) contributes the current save candidates; every other file contributes its saved bytes. Two individually valid lamps can exceed the shared budget. This small budget rule does not replace schema, relationship or reference checks. A failed read throws into AskSaveCheck, which returns a refusal. An unsaved sibling must not grant permission that will be absent from disk after this one file is written. SaveAll supplies the proposed group and the group writer; the callback must judge that actual proposal.

The proof driver below creates a fresh copy, raises one lamp to 5 so the total is 6, observes the refused Save with unchanged disk bytes and an editable dirty draft, corrects it to 4, and saves the accepted total of 5. It prints the real refusal and the successful retry. Another copy lowers a sibling only in memory, then proves saving the main file still meets the persisted sibling's budget; both drafts and disk survive refusal. The original example remains unchanged.

DataFile.Save, a project's editor Save/Save as and agent Save routes meet the configured callback for dirty files. An ordinary unconfigured command-line project does not inherit an adapter from another process. DataFile.Save on a clean file skips the callback and touches no file. SaveAll judges the candidate group before writing. It captures draft revisions, runs each file's normal checks, then rechecks revisions, read-only policy, outside changes and writability without calling the expensive callback again.

BeingSaved(file) reports the candidates for the current attempt: one file for an ordinary save, or the remaining dirty group during SaveAll. A refusal removes that file and starts judgment again on the reduced group, so another file cannot rely on a draft that will stay unsaved. A changed revision, policy, disk version or write failure can also remove a candidate. SaveAll returns the refused files with Problems; a reduced valid group may still save. A returned refusal therefore does not mean the entire project's files stayed unchanged.

Before each group write, FileGroup compares the current disk text with the session's expected version and captures the original bytes. If a write or that comparison fails, it attempts to put previously written files back, but only while they still hold this attempt's text. For files it touched, final states come from reading actual bytes; untouched entries stay AsItWas. Only files confirmed to hold the new text are marked saved. The proof below saves two coordinated drafts together, then refuses one and shows the remaining dependent refused against disk. After correcting the refusal, both save together.

Create, rename, delete and restore do not call SaveCheck. FileCheck receives their proposed FileChange, including candidate Text for Create/Restore and NewName for Rename. It also runs for previews; return null to allow, or a useful reason to refuse. The example applies that operation to a temporary dictionary of saved inputs, then runs the same budget rule. Deleting or renaming the required lamps.json is refused. A throw becomes a refusal. FileCheck is not an edit validator and must not write files. Direct session writes outside the project route also do not become safe merely because a project has a callback.

The callback is synchronous: a slow validator pauses the caller, including the editor frame when Save is chosen there. Keep it bounded and read-only; prefer a registered check for interactive progress/cancellation. Do not assume an uncooperative callback can be cancelled. AskSaveCheck turns a thrown exception into a refusal and refuses a draft revision or read-only policy changed during the callback. DataFile.Save also rechecks outside disk changes afterward. Saving from inside SaveCheck is blocked: DataFile.Save returns false with a check-running reason, and nested SaveAll returns the dirty files without writing. These guards are not permission to mutate the project or write files from the callback.

A refusal names the file and reason in DataFile.Problems; DataWorkspace.SaveAll joins those reasons for the person, and failed drafts/history remain available. For a file held by another program, the message says to close it and save again. Correct the draft or checker setup and retry through the same authority.

### Remaining storage limit

A power cut can leave part of a group written with no report. If an original file cannot be put back, or another program has replaced this attempt's bytes, the writer leaves what is there and reports which files need checking; it cannot make an unwritable device writable or overwrite a newer outside version to force a rollback. For touched files, Written/AsItWas/Unknown describes the bytes it could read, not just the operations attempted; untouched entries are AsItWas. Check the named files and preserve/reconcile the remaining drafts before retrying.

## Choose a schema before opening files

Use SchemaFor when several names use one schema and the format cannot carry a $schema property. The example assigns every file to lamps.schema.json before any file opens. Return null to fall back to the data's string $schema or the usual name.schema.json. A thrown chooser refuses opening with its reason.

An open DataFile keeps its chosen Schema even if SchemaFor changes. Changing the delegate does not revalidate or switch an existing session. Preserve drafts before deliberately closing/reopening a file; a workspace caches its editors, so reopen the project/workspace when you need a fresh binding. Create refuses a schema that disagrees with the chooser and checks its initial data before writing. The proof below demonstrates both the retained open choice and a deliberately reopened choice, on disposable files.

## Describe rows that can be remembered

IObjectType.RowsVersion defaults to null, the safe choice. Null asks the inspector to obtain nested rows again rather than reuse them. Leave it null if rows read external state you cannot version reliably.

A non-null long is a promise that every dependency outside the document, selection and requested row is represented by that number. Change it whenever those dependencies change. A constant is suitable only when there are no such changing dependencies; it is not a general speed switch. The inspector also considers document identity, selection, focus lock and type when remembering nested rows. Documents must remain immutable.

DataEditing.ObjectType uses JsonSchemas.Generation, so replacing a schema invalidates those rows even when the data document is the same object. The proof below checks that number changes and that a minimal custom type inherits the safe null default. It does not require every adapter to implement its own cache.

## Keep a format's Write predictable

IDocumentFormat.Write returns canonical text; it does not save a file. Write must return the same text for the same document and leave the document and other state unchanged. Do not add timestamps, random IDs, counters or file/network work there. Dirty queries call Write when their cached document/saved-text pair changes. It is not reserved for explicit Save; keep it bounded and do not rely on a particular call count.

The built-in DataFormat reuses unchanged source spans. A custom format still owns its documented canonical encoding, property order, line endings and final newline. Use the session/project save route for disk writes and its policy checks. The proof below calls the same format twice and checks equal text, unchanged document identity and unchanged disk; that checks this example, not the purity of arbitrary adapter code.

## A check

An `ICheck` has a stable id, a user-facing name, an explanation and a time budget. Its `RunAsync` receives a captured `CheckSnapshot`, progress and a cancellation token. Read that snapshot, honour cancellation, and return findings with useful locations and next steps. **Never mutate the live session, save files, write to the project or use a check as an edit.** External tools must obey the same read-only contract.

`UniqueNames` checks names case-insensitively. Two rows named Signal produce two findings. `Complete=true` means it finished that audit, even when it found errors. Missing input returns `Complete=false`. A result is clean only when the run succeeded, is complete, has no findings and still matches the current project. Editing afterward makes an earlier result stale. An empty list from an incomplete, cancelled or failed run is not a clean result. “Clean” here covers this check's declared name audit; it does not certify every game rule.

ProjectPaths.In(root) automatically discovers root/reference_map.json (underscore), alongside data/ and schemas/. The data guide's small driver explicitly loads reference-map.json (hyphen); that filename is not automatically discovered by the launcher. Use the underscore name for the launcher route, or explicitly construct ProjectPaths with its Map argument. Keep the map outside data/ so it is not treated as a data document.

### Cross-file references and protected fields

For an invented Garden, plants.json can contain a plants array whose bed names an id in beds.json's beds array. Give both files their own matching schemas. This complete map goes in reference_map.json beside data/ and schemas/:

```json
{"game": "Garden", "labels": [{"file": "beds.json", "list": "/beds", "field": "label"}], "references": [{"file": "plants.json", "at": "/plants/*/bed", "target": {"file": "beds.json", "list": "/beds", "key": "id"}, "evidence": "INVENTED Garden bed assignment, declared by this example."}]}
```

The launcher reads ProjectPaths.Map and supplies the reference map to its workspace; the adapter's Open returns the data project, not a workspace. The CLI needs --map explicitly, even when the underscore file is in the conventional location. get plants.json /plants/0/bed should resolve north to beds.json /beds/0; refs beds.json /beds/0 should find the plant. A map describes and checks links; it does not alone promise to refuse every dangling-link edit or save. Use the linked Start here recipe's project SaveCheck when referential integrity must block saves.

Do not confuse a JSON Schema readOnly annotation with an enforced lock. For a fixed generated Garden catalogue identity, put this property under the plants schema's properties and include catalogue in required:

```json
{"type":"string","readOnly":true,"const":"garden-v1","description":"Fixed generated catalogue identity; const enforces this value."}
```

Here const, not readOnly, refuses changing the value, through both editor session commands and the agent. The field may still look editable: a refusal is not a disabled widget. This is only a fixed-value recipe, not a general immutable-per-record-id solution. For values that vary by record, add a domain validator that compares against the authoritative identity. Whole generated files can use readOnlyFile, which is a save policy, not a field edit lock; see Writing a plugin. Never bypass the owning DataFile save route.

For a repeatable demonstration, the next two blocks create a small console proof driver using the same public APIs. It refuses the invalid intensity, runs the real Checks panel command, renames a lamp in memory, rechecks, and confirms the original file bytes stayed intact. It also observes controlled adapter failures and renders the Checks panel and custom panel separately. No test package is needed.

```powershell
# P05 - Standalone proof driver; no test package is needed.
Save-Demo 'verify/Verify.csproj' @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NoWarn>$(NoWarn);BRUTALCORE0011</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../adapter/LanternYardAdapter.csproj" />
  </ItemGroup>
</Project>
'@
Save-Demo 'verify/Program.cs' @'
using System.Text.Json;
using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;

// INVENTED proof driver. Only its supplied example/settings/output paths are used; no OS window.
if (args.Length != 5) { Console.Error.WriteLine("usage: Verify adapter.dll project checks.png panel.png settings"); return 2; }
var (loaded, problems) = Adapters.Load([args[0]]);
foreach (var p in problems) Console.Error.WriteLine(p);
if (problems.Count != 0 || loaded.Count != 1) throw new InvalidOperationException("Expected one healthy example adapter.");
var adapter = loaded[0];
var paths = ProjectPaths.In(args[1]) ?? throw new InvalidOperationException("Example needs data/ and schemas/.");
var before = File.ReadAllBytes(Path.Combine(paths.Data, "lamps.json"));
var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(args[4], "recent.json"))) { Adapter = adapter };
Need(launcher.Open(paths), launcher.Said);
var workspace = launcher.Workspace ?? throw new InvalidOperationException("No workspace.");
var window = workspace.Window ?? throw new InvalidOperationException("No file editor.");
var session = workspace.Active!.Session!;
Need(!session.Execute(new SetValue("/lamps/0/intensity", JsonSerializer.SerializeToElement(6))), "Rule should refuse intensity6.");
Console.WriteLine("RULE refused: " + session.LastIssues.Single().Message);
Need(!session.Dirty, "A refusal must not dirty the document.");
Need(window.Editor.Panels.Any(p => p.Id == "yard.summary"), "The adapter panel is missing.");
var action = EditorWindow<DataDocument>.PanelCommandAction + "project.checks/run.yard.unique_names";
Need(window.Run(action), window.Status);
var first = workspace.Checks.Last("yard.unique_names")!;
await first.Ended.WaitAsync(TimeSpan.FromSeconds(10));
var firstReport = first.Report ?? throw new InvalidOperationException("No completed name report.");
Need(firstReport.Complete && firstReport.Findings.Count == 2 && !first.Clean(workspace.Project), "Both duplicate names must be found.");
Console.WriteLine("CHECK errors: " + firstReport.Summary);
Console.WriteLine("FINDING: " + firstReport.Findings[0].Problem.Message);
Need(session.Execute(new SetValue("/lamps/1/name", JsonSerializer.SerializeToElement("South signal"))), "Rename should be valid.");
Need(window.Run(action), window.Status);
var second = workspace.Checks.Last("yard.unique_names")!;
await second.Ended.WaitAsync(TimeSpan.FromSeconds(10));
Need(second.Clean(workspace.Project), "Re-check must be complete, empty and current.");
Console.WriteLine("CHECK clean: " + second.Report!.Summary);
Need(before.SequenceEqual(File.ReadAllBytes(Path.Combine(paths.Data, "lamps.json"))), "Proof must leave example disk data intact.");

// A fresh copy proves SaveCheck without changing the original example.
var saveRoot = Path.Combine(args[4], "save-demo");
Need(!Directory.Exists(saveRoot), "Use a fresh proof folder.");
foreach (var folder in new[] { "data", "schemas" })
{
    Directory.CreateDirectory(Path.Combine(saveRoot, folder));
    foreach (var input in Directory.EnumerateFiles(Path.Combine(args[1], folder), "*.json"))
        File.Copy(input, Path.Combine(saveRoot, folder, Path.GetFileName(input)));
}
var saveProject = adapter.Open(ProjectPaths.In(saveRoot)!);
var saveWorkspace = new DataWorkspace(saveProject);
Need(saveWorkspace.Show("lamps.json"), saveWorkspace.Said);
var saveFile = saveProject.Find("lamps.json")!;
var savedBefore = File.ReadAllBytes(saveFile.Path);
Need(saveFile.Session!.Execute(new SetValue("/lamps/0/intensity", JsonSerializer.SerializeToElement(5))), "Per-lamp value should be valid.");
Need(!saveWorkspace.Window!.Run("editor.save"), "Total 6 must be refused.");
Need(saveFile.Dirty && savedBefore.SequenceEqual(File.ReadAllBytes(saveFile.Path)), "Refusal must retain the draft and disk bytes.");
Console.WriteLine("SAVE refused: " + saveWorkspace.Window.Status);
Need(saveFile.Session.Execute(new SetValue("/lamps/0/intensity", JsonSerializer.SerializeToElement(4))), "Correction should be valid.");
Need(saveWorkspace.Window.Run("editor.save") && !saveFile.Dirty, saveWorkspace.Window.Status);
using (var accepted = JsonDocument.Parse(File.ReadAllText(saveFile.Path)))
    Need(accepted.RootElement.GetProperty("lamps")[0].GetProperty("intensity").GetInt32() == 4, "Accepted draft missing from disk.");
Console.WriteLine("SAVE retry: total 5 accepted; only the disposable copy changed.");

// A lower unsaved sibling cannot grant permission to persist an over-budget main file.
var siblingRoot = Path.Combine(args[4], "sibling-demo");
Directory.CreateDirectory(Path.Combine(siblingRoot, "data"));
Directory.CreateDirectory(Path.Combine(siblingRoot, "schemas"));
File.Copy(Path.Combine(paths.Schemas, "lamps.schema.json"), Path.Combine(siblingRoot, "schemas/lamps.schema.json"));
File.WriteAllBytes(Path.Combine(siblingRoot, "data/lamps.json"), before);
File.WriteAllText(Path.Combine(siblingRoot, "data/spare.json"),
    "{\"kind\":\"lantern-yard\",\"lamps\":[{\"id\":\"spare\",\"name\":\"Spare\",\"limit\":5,\"intensity\":2}]}");
var siblings = adapter.Open(ProjectPaths.In(siblingRoot)!);
var main = siblings.Find("lamps.json")!; var spare = siblings.Find("spare.json")!;
Need(main.Open() && spare.Open(), "Sibling fixture must open.");
var mainDisk = File.ReadAllBytes(main.Path); var spareDisk = File.ReadAllBytes(spare.Path);
Need(spare.Session!.Execute(new SetValue("/lamps/0/intensity", JsonSerializer.SerializeToElement(0))), "Lower draft.");
Need(main.Session!.Execute(new SetValue("/lamps/0/intensity", JsonSerializer.SerializeToElement(4))), "Valid per-row draft.");
Need(!main.Save() && main.Dirty && spare.Dirty && mainDisk.SequenceEqual(File.ReadAllBytes(main.Path))
    && spareDisk.SequenceEqual(File.ReadAllBytes(spare.Path)), "Unsaved sibling must not grant save permission.");
Console.WriteLine("SAVE sibling: refused against persisted inputs; both drafts and disk preserved.");

// Both candidates now describe a valid persisted result, so the group saves together.
var originalCheck = siblings.SaveCheck!;
var intendedCounts = new List<int>();
siblings.SaveCheck = file => { intendedCounts.Add(siblings.BeingSaved(file).Count); return originalCheck(file); };
var failed = siblings.SaveAll();
Need(failed.Count == 0 && !main.Dirty && !spare.Dirty, "Both dependent drafts must save together.");
Need(intendedCounts.Count == 2 && intendedCounts.All(count => count == 2), "Both checks see this attempt's group.");
Need(!mainDisk.SequenceEqual(File.ReadAllBytes(main.Path)) && !spareDisk.SequenceEqual(File.ReadAllBytes(spare.Path)),
    "Both accepted candidates must be on disk.");
Console.WriteLine("SAVE all: group 2 judged before writing; both dependent drafts saved.");

// When the lower provider is refused, the remaining draft must meet saved inputs instead.
var groupMain = File.ReadAllBytes(main.Path); var groupSpare = File.ReadAllBytes(spare.Path);
Need(main.Session!.Execute(new SetValue("/lamps/0/intensity", JsonSerializer.SerializeToElement(2))), "Lower provider draft.");
Need(spare.Session!.Execute(new SetValue("/lamps/0/intensity", JsonSerializer.SerializeToElement(2))), "Dependent draft.");
intendedCounts.Clear();
siblings.SaveCheck = file =>
{
    intendedCounts.Add(siblings.BeingSaved(file).Count);
    return file == main ? "This tutorial provider must stay a draft." : originalCheck(file);
};
failed = siblings.SaveAll();
Need(failed.Count == 2 && failed.Contains(main) && failed.Contains(spare) && main.Dirty && spare.Dirty,
    "Refused provider and its dependent must both remain drafts.");
Need(intendedCounts.SequenceEqual(new[] { 2, 1 }), "The dependent must be judged in the reduced group.");
Need(groupMain.SequenceEqual(File.ReadAllBytes(main.Path)) && groupSpare.SequenceEqual(File.ReadAllBytes(spare.Path)),
    "Both original disk files must survive these refusals.");
siblings.SaveCheck = originalCheck;
Need(siblings.SaveAll().Count == 0 && !main.Dirty && !spare.Dirty, "Retry judges and writes both corrected candidates.");
Console.WriteLine("SAVE all refusal: groups 2 then 1; both drafts and disk retained; corrected retry saved both.");


// Whole-file previews and writes meet the same policy, without touching the original example.
Need(!saveProject.Delete("lamps.json", out var removeWhy, preview: true), "Deleting the required file must refuse.");
Need(!saveProject.Rename("lamps.json", "moved.json", out var renameWhy, preview: true), "Renaming the required file must refuse.");
Need(savedBefore.Length > 0 && File.Exists(saveFile.Path), "Preview changed files.");
Console.WriteLine("FILE refused: " + removeWhy + " / " + renameWhy);

// Show schema choice lifetime on a separate minimal project.
var schemaRoot = Path.Combine(args[4], "schema-demo");
Directory.CreateDirectory(Path.Combine(schemaRoot, "data"));
Directory.CreateDirectory(Path.Combine(schemaRoot, "schemas"));
File.WriteAllText(Path.Combine(schemaRoot, "data/first.json"), "{}");
foreach (var name in new[] { "shared.schema.json", "other.schema.json" })
    File.WriteAllText(Path.Combine(schemaRoot, "schemas", name), "{\"type\":\"object\"}");
var schemas = DataProject.Open(Path.Combine(schemaRoot, "data"), Path.Combine(schemaRoot, "schemas"));
schemas.SchemaFor = _ => "shared.schema.json";
var chosen = schemas.Find("first.json")!;
Need(chosen.Open() && chosen.Schema == "shared.schema.json", "Host schema choice.");
schemas.SchemaFor = _ => "other.schema.json";
Need(chosen.Open() && chosen.Schema == "shared.schema.json", "Open choice must stay fixed.");
chosen.Close();
Need(chosen.Open() && chosen.Schema == "other.schema.json", "Deliberate reopen must choose afresh.");
var type = DataEditing.ObjectType(schemas.Schemas, "other.schema.json");
var version = type.RowsVersion;
schemas.Schemas.Add("other.schema.json", "{\"type\":\"object\",\"title\":\"Revised\"}");
Need(type.RowsVersion > version, "Schema generation must change.");
Need(((IObjectType<DataDocument>)new LiveRows()).RowsVersion is null, "Custom rows default must stay live.");
var document = chosen.Session!.Current;
var disk = File.ReadAllBytes(chosen.Path);
var firstText = chosen.Session.Format.Write(document);
Need(firstText == chosen.Session.Format.Write(document) && ReferenceEquals(document, chosen.Session.Current)
    && disk.SequenceEqual(File.ReadAllBytes(chosen.Path)), "Write must not change state or disk.");
Console.WriteLine("SCHEMA retained/reopened; RowsVersion generation/null; deterministic Write: OK.");

// A fresh example driver observes fault handling without modifying the actual adapter.
foreach (var mode in new[] { "name", "open", "checks", "configure" })
{
    var fault = new ProjectLauncher(new RecentProjects(Path.Combine(args[4], mode, "recent.json")))
        { Adapter = new FaultAdapter(adapter, mode) };
    var ok = fault.Open(paths);
    Console.WriteLine("FAULT " + mode + " (opened=" + ok + "): " + fault.Said);
}
var constructor = Adapters.Load([typeof(ConstructorFault).Assembly.Location]);
Console.WriteLine("FAULT constructor: " + string.Join(" | ", constructor.Problems));
Need(constructor.Problems.Any(p => p.Contains("Example constructor failure")), "Constructor error must be reported.");

using (var host = new HeadlessHost(1600, 1000))
{
    EditorWindow<DataDocument>.Configure(ImGui.GetIO());
    window.Editor.SetOpen("yard.summary", false); // show one right-hand panel at a time
    host.Frames(launcher.Draw, 4);
    host.Save(args[2]);
    window.Editor.SetOpen("project.checks", false);
    window.Editor.SetOpen("yard.summary", true);
    host.Frames(launcher.Draw, 4);
    host.Save(args[3]);
}
Console.WriteLine("PANEL rendered; draft was checked without saving the fixture.");
return 0;

static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }

// No external-dependency version is promised: IObjectType supplies the safe null default.
internal sealed class LiveRows : IObjectType<DataDocument>
{
    public string Id => "example.live";
    public string Label => "Live example";
    public IReadOnlyList<InspectorRow> Rows(DataDocument document, IReadOnlyList<string> ids) => [];
    public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument document, IReadOnlyList<string> ids,
        string propertyId, object? value, bool reset) => null;
}

internal sealed class FaultAdapter(IProjectAdapter inner, string mode) : IProjectAdapter
{
    public string Name => mode == "name" ? throw new InvalidOperationException("Example name failure") : inner.Name;
    public DataProject Open(ProjectPaths p) => mode == "open" ? throw new InvalidOperationException("Example Open failure") : inner.Open(p);
    public IEnumerable<ICheck> Checks => mode == "checks" ? throw new InvalidOperationException("Example Checks failure") : inner.Checks;
    public void Configure(EditorModel<DataDocument> e, DataTableView t)
    {
        if (mode == "configure") throw new InvalidOperationException("Example Configure failure");
        inner.Configure(e, t);
    }
}

/// <summary>An explicitly loaded negative fixture, never an installed adapter.</summary>
public sealed class ConstructorFault : IProjectAdapter
{
    /// <summary>Deliberate proof-only constructor failure.</summary>
    public ConstructorFault() => throw new InvalidOperationException("Example constructor failure");
    /// <inheritdoc />
    public string Name => "Never constructed";
    /// <inheritdoc />
    public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data, p.Schemas);
    /// <inheritdoc />
    public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
}

'@
```

```powershell
# P06 - Check the rule, the check, fault messages and two real headless panels.
dotnet build (Join-Path $work 'verify/Verify.csproj') "-p:AuthoringRoot=$authoringRoot" --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Verifier build failed.' }
$verifierDll = Join-Path $env:ArtifactsPath 'bin/Verify/debug/Verify.dll'
dotnet $verifierDll $adapterDll $project (Join-Path $work 'checks.png') (Join-Path $work 'panel.png') (Join-Path $work 'settings-proof')
if ($LASTEXITCODE -ne 0) { throw 'Adapter proof failed.' }
```

Read `checks.png`: the completed name check is clean after the draft rename. The draft is unsaved; a fresh app open still sees the original duplicate names. The save proof writes only settings-proof/save-demo; its original input bytes stay intact. The proof uses a fresh explicit launcher, so its controlled fault fixtures do not enter the installed `adapters/` folder.

## A panel

`Configure` runs for each opened file editor and receives that file's `EditorModel<DataDocument>` and `DataTableView`. Guard the file name when your panel applies to one document. This example adds `yard.summary` with a stable id and title; its draw callback reads the session's current draft each frame. Register the panel while configuring the editor, not on every draw.

`PanelSide.Right` docks it on the right; other right-side panels can share that space as tabs. The proof temporarily closes the custom panel to expose Checks, then reverses them for `panel.png`. Read that image too: it must say Lamp count: 2 and explain the intensity rule. This small status panel belongs on the right. For a large map use PanelSide.Centre, as in [the complete map composition](start_here.md#map-inside-the-adapter); Width is only an initial floating-size hint, not an override of the dock's allocated region. Draw the map using the available content height. The panel is read-only. For editable controls, submit session commands so validation, undo and the draft all agree; do not change JSON or disk behind the session.

## Give Play the game's saved-file command

Implement `DefaultPlay(ProjectPaths)` to suggest the program, literal arguments, working folder and optional build command. BED uses these defaults when `.bed/project.json` has no Play settings. Saved fields override defaults; omitted fields inherit them, and an empty argument array clears default arguments. The adapter does not write settings or approve a command.

`ProjectPaths.Root` is the project root for Play settings, relative program/build paths and the saved asset cache. It is separate from `Data`: your data may live several folders below it. A folder provider should return an absolute Root, or a Root relative to the folder being inspected. It must exist and contain the returned data folder. Keep it when translating project paths. BED supplies a resolved absolute Root to `DefaultPlay`; do not reconstruct it from the parent of nested data.

This standalone adapter uses a nonstandard `content/game` layout. Compile it in a separate class library with the same reference as P02; replace its example paths and arguments with your game's actual build output and command. Its game must accept `--project .` and read saved files from that folder. Discovery and default settings only describe files and commands; neither launches anything.

<!-- DOC09 compiling Play adapter -->
```csharp
using System;
using System.IO;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;

// INVENTED example layout and command; BORROWED public adapter contract.
public sealed class SavedGameAdapter : IProjectAdapter
{
    public string Name => "Saved game example";

    public ProjectPaths? FindProject(string folder)
    {
        var root = Path.GetFullPath(folder);
        var data = Path.Combine(root, "content", "game");
        var schemas = Path.Combine(root, "schemas");
        return Directory.Exists(data) && Directory.Exists(schemas)
            ? new ProjectPaths(data, schemas) { Root = root }
            : null;
    }

    public DataProject Open(ProjectPaths paths) => DataProject.Open(paths.Data, paths.Schemas);
    public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }

    public PlaySettings? DefaultPlay(ProjectPaths paths)
    {
        var root = paths.Root ?? throw new ArgumentException("Supply the project root.");
        return new PlaySettings(
            program: Path.Combine(root, "src", "Game", "bin", "Release", "net10.0", "Game.exe"),
            arguments: ["--project", "."],
            workingFolder: root,
            build: new PlayBuildSettings("dotnet",
                ["build", "src/Game/Game.csproj", "-c", "Release"],
                ["src/**/*.cs", "src/**/*.csproj", "Directory.*.props"]));
    }
}
```

Play offers the normal save decision for pending edits and runs the game against saved files. Build and Play runs the declared build from the project root, then starts the same program; input globs let BED detect when a rebuild is needed. The build command and programs outside the project still require the person's exact-command approval. A changed command needs approval again. Keep arguments as separate strings, never a shell command line. See [Play](playing.md) for settings, freshness checks and stopping.

## An in-process play mode across files

The following is the separate embedded preview API. For the game executable and the common F5 command, use `DefaultPlay` above.

A play mode (`PlayMode<DataDocument>`) is handed only its own file's document. When a game needs more than one file, implement the other overload, `Configure(DataWorkspace workspace, DataFile file, EditorModel<DataDocument> editor, DataTableView table)`; the general app calls it, and by default it calls the two-argument one. Register the mode there, and have its `Start` read each file's current draft from `workspace.Project` (`Find(name)?.Session?.Current`) when Play is pressed: that's what the person sees, unsaved edits included. Never read the files from disk behind the editor, and never write anything while starting or playing.

The workspace owns the game's lifetime. At most one file's game runs at a time; a second is refused, naming the first. The workspace stops it, through `EditorModel.Stop` and so your session's `Dispose`, before it shows another file, before the playing file is renamed or deleted, and when the project is closed or replaced or the app shuts down (`DataWorkspace.StopPlay`, `DataWorkspace.Dispose`, `ProjectLauncher.Dispose`). A close that waits on unsaved edits leaves the game running. Make `Dispose` safe to call twice and keep it from throwing; if it throws anyway, the game is still stopped and the workspace says why.

## When it goes wrong

Run the final block to observe an ordinary non-adapter library, a missing project layout and a project this adapter does not recognise. The last copy changes both its data marker and schema so that plain fallback still has valid data. The expected missing-layout exit code is checked explicitly.

```powershell
# P07 - Expected diagnostics on disposable inputs, always headless.
& $appExe $project --adapter (Join-Path $appFolder 'Bed.dll') --settings (Join-Path $work 'settings-app') --shot (Join-Path $work 'ordinary-library.png') 2> (Join-Path $work 'ordinary-library.txt')
if ($LASTEXITCODE -ne 0) { throw 'An optional bad library should not stop the valid project.' }
Get-Content -LiteralPath (Join-Path $work 'ordinary-library.txt')
& $appExe 'missing-layout' --settings (Join-Path $work 'settings-app') --shot (Join-Path $work 'missing-layout.png') 2> (Join-Path $work 'missing-layout.txt')
if ($LASTEXITCODE -ne 1) { throw 'A missing project should return exit 1.' }
Get-Content -LiteralPath (Join-Path $work 'missing-layout.txt')
& $appExe $project --adapter (Join-Path $work 'AbsentAdapter.dll') --settings (Join-Path $work 'settings-app') --shot (Join-Path $work 'absent-library.png') 2> (Join-Path $work 'absent-library.txt')
if ($LASTEXITCODE -ne 0) { throw 'An absent optional adapter should not stop the discovered valid one.' }
Get-Content -LiteralPath (Join-Path $work 'absent-library.txt')
# A loaded adapter is still chosen by Recognises when Automatic is selected.
$other = Join-Path $work 'other-project'
Copy-Item -LiteralPath $project -Destination $other -Recurse
$text = [IO.File]::ReadAllText((Join-Path $other 'data/lamps.json')).Replace('lantern-yard', 'some-other-game')
[IO.File]::WriteAllText((Join-Path $other 'data/lamps.json'), $text, [Text.UTF8Encoding]::new($false))
$schema = [IO.File]::ReadAllText((Join-Path $other 'schemas/lamps.schema.json')).Replace('lantern-yard', 'some-other-game')
[IO.File]::WriteAllText((Join-Path $other 'schemas/lamps.schema.json'), $schema, [Text.UTF8Encoding]::new($false))
& $appExe $other --adapter $adapterDll --settings (Join-Path $work 'settings-app') --shot (Join-Path $work 'unrecognised.png')
if ($LASTEXITCODE -ne 0) { throw 'Unrecognised data should still open plainly.' }
```

The diagnostic table below is filled from these runs and the proof driver. Paths in constructor messages are the generated absolute path, so yours will differ. These failures are deliberately injected into disposable example objects, not instructions to break the installed adapter.

| Observed case | Exact message or observed behaviour | What to do |
|---|---|---|
| Intensity above its row's limit | `Intensity 6 exceeds limit 5; lower intensity or raise limit.` | Correct that value; the refused command did not dirty the draft. |
| Duplicate names | `Lamp name 'Signal' appears more than once.` | Choose a distinct name and rerun Unique lamp names; the driver observed `2 name problem(s).`, then `0 name problem(s).`. |
| Ordinary library supplied as an adapter | `Bed.dll holds no game adapter (a public IProjectAdapter with an empty constructor).` | Supply the adapter assembly, containing a public concrete implementation with a public parameterless constructor. This optional library problem did not prevent the discovered valid adapter opening. |
| Missing DLL | The observed message starts `AbsentAdapter.dll didn't load: Could not find file '` and ends with the supplied absolute DLL path followed by `'.` | Fix the path or rebuild the DLL; this optional failure did not stop the discovered valid adapter opening. |
| Missing project layout | `missing-layout doesn't hold a data folder and a schemas folder. Choose the two folders separately.` | Choose a parent containing both folders, or select them separately in the start screen. Headless open returned exit 1 and produced no editor shot. |
| Constructor throws | `Verify.dll: ConstructorFault didn't load: Example constructor failure` | Fix the adapter constructor and rebuild. The proof-only fixture is in Verify.dll, never installed. |
| Name getter throws | `Opened lantern-yard with FaultAdapter: 1 files; choose another in the Files panel. The adapter FaultAdapter couldn't give its name: Example name failure.` | Fix the getter; the class name is used as a fallback. Open succeeded with a diagnostic. |
| Open throws | `Lantern Yard couldn't open the project: Example Open failure. That's a fault in the adapter, not your data: choose None (plain data) to open it without.` | Fix Open or choose None to use the plain editor. This controlled open returned false. |
| Checks getter throws | `Opened lantern-yard with Lantern Yard: 1 files; choose another in the Files panel. Lantern Yard's checks weren't added: Example Checks failure` | Fix the getter/enumeration and reopen; the project opened without those checks. |
| Configure throws | `Opened lantern-yard with Lantern Yard: 1 files; choose another in the Files panel. The game adapter couldn't finish setting up lamps.json: Example Configure failure. Its panels may be missing; your data is safe.` | Fix Configure and reopen the file. The project still opened; custom panels were absent. |
| Loaded adapter returns false from Recognises | `Opened other-project: 1 files; choose another in the Files panel.` | This is a successful plain open, not a loader error. Check the marker, select the intended adapter in the start screen when appropriate, or keep the plain editor. The Lantern Yard panel is absent. |

A filename that does not end in Adapter.dll is not considered by folder discovery; use the suffix or an explicit path. Missing files and incompatible binaries are also loader problems: check the supplied path, matching host/runtime and private dependencies before reopening.

For compiler or restore errors, read the first build error before trying the app: check the SDK, checkout path, existing package availability and matching tool version. A build that fails has not produced a usable adapter. A schema error is separate from recognition: repair the data or schema; do not loosen a game's contract to hide an error. If a rule rejects an edit, its targeted issue explains which value to fix. The Checks panel shows a failed, cancelled, timed-out, incomplete or stale run as such; fix the cause and rerun it.

### The same rules without a window

The agent is a separate host: loading an adapter in the editor does not configure a later command-line process. Continue in the same tutorial shell after P07. This block compiles the CLI into the existing isolated outputs, opens the adapter, and checks two expected refusals without changing the original fixture. No extra package is added.

```powershell
# P08 - Same adapter rules through the agent, on the original unsaved fixture.
dotnet build (Join-Path $authoringRoot 'src/Bed.Agent.Cli/Bed.Agent.Cli.csproj') --output (Join-Path $work 'cli') --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Agent build failed.' }
$agentDll = Join-Path $work 'cli/Bed.Agent.Cli.dll'
$agentArgs = @('--data', (Join-Path $project 'data'), '--schemas', (Join-Path $project 'schemas'), '--adapter', $adapterDll)
dotnet $agentDll @agentArgs describe
if ($LASTEXITCODE -ne 0) { throw 'Agent adapter open failed.' }
dotnet $agentDll @agentArgs set lamps.json /lamps/0/intensity 6 --save
if ($LASTEXITCODE -ne 1) { throw 'Agent must refuse the domain rule.' }
dotnet $agentDll @agentArgs check yard.unique_names
if ($LASTEXITCODE -ne 1) { throw 'Original duplicate names must still be found.' }
```

Exit 0 means done, 1 means refusal or a check that is not clean, and 2 means bad arguments or unreadable inputs. The expected rule refusal names /lamps/0/intensity; the original file still has the two Signal names. Add --map with an explicit reference map path for cross-file navigation. --read-only cannot currently be combined with --adapter; do not silently drop the adapter to get read-only mode, because that drops the game's rules too.

## Trust

**An adapter is a .NET program with the tools process's permissions. It is not a sandboxed data file.** Its constructor, getters, recognition, open code and draw callbacks can execute code. Catches around some entry points improve diagnostics; they cannot prevent malicious file access, network access, hangs or native crashes. A check's time budget does not forcibly stop uncooperative code. Only install adapters and their dependencies from authors you trust; inspect source and provenance where possible and test with a disposable copy of your project.

Keep this example separate from your game's shipped data until you have tested your own adapter against your game's contracts. Use self-authored public fixtures, obtain the right to redistribute dependencies and content, and name a compatible host version. This example adds no NuGet packages; it relies on the checkout's existing BRUTAL and tools dependencies and their notices. A development build does not grant redistribution rights or settle the tools' final release licence.

## Reference maps beside adapters

BED resolves the map before constructing the immutable workspace. An explicit `--map` (or Map field) wins, including when it is missing or invalid: that error is reported, never hidden by fallback. Otherwise the project root's `reference_map.json` wins. If it is absent, BED looks for `reference_map.json` beside the chosen adapter's DLL. If neither exists, the workspace has no map. For explicit data/schema folders, the project root is the parent of the data folder. Only the selected adapter contributes a fallback; plain-data mode does not use adapter maps.

Ship your map beside your adapter with `CopyToOutputDirectory` and `CopyToPublishDirectory`, referencing its existing single source file. Do not duplicate the checked-in map. Adapter DLL dependencies must be beside the DLL, and the filename must end in `Adapter.dll` for automatic discovery under BED's `adapters/` directory. Loading a DLL is not automatically selecting it: implement `Recognises` or select it explicitly in the start screen.

The same resolution is used by BED's existing `--shot` route. No adapter-specific screenshot executable or rendering path is required. The fallback is a BED project-loader feature, not a promise that unrelated command-line hosts use that loader.


## Recognising a nonstandard project folder

An adapter may implement the optional default method `ProjectPaths? FindProject(string folder)`.
The default returns null. Non-null recognises that folder and selects that adapter; a second
`Recognises` call is not required. For example, an Acme adapter whose schemas are beside its data can return
`new ProjectPaths("content", "content")` after checking its own marker. Decline unrelated folders with null.
This is discovery only: do not write files or start a game from this method.

An explicitly selected adapter gets the first chance to recognise the folder; a null answer falls back to the standard layout. With automatic selection, BED tries the standard `data/` plus `schemas/` layout first, then asks the discovered adapters in order, stopping at the first non-null answer.
Relative returned paths resolve against the original folder. Empty/missing/invalid folders or an adapter
exception produce a plain refusal, never an automatic switch to another provider. The normal project loader
still validates schemas and maps before opening a workspace.

Map order is an explicit map, `reference_map.json` in the declared Root (or the original folder when Root is absent), the provider's Map,
then the existing adapter-DLL sidecar. Explicit data/schema arguments remain explicit and bypass folder discovery.
`ProjectLauncher.OpenFolder(folder, explicitMap)` exposes the positional-folder map override; old OpenFolder calls remain valid.

A provider-derived recent entry stores its original Folder and exact adapter identity, not cached data/schema/map
paths as explicit inputs. Reopening rediscovers them. An explicit map override remains explicit. If the remembered
provider is unavailable and no standard layout exists, opening is refused with the retained adapter identity;
BED does not guess, use stale paths or silently choose another adapter. Old recent entries and explicitly
chosen folders retain their prior behavior. The folder is shown in Recent projects even while its provider is absent.

## Managed source hooks

The five examples below compile in [ManagedSourceHooks.cs](../../samples/StarterAdapter/ManagedSourceHooks.cs). They are independent helper methods in the existing sample library, not a second adapter loaded beside it. Build them with `dotnet build samples/StarterAdapter/StarterAdapter.csproj -c Release` from the checkout root. Use the same target framework and editor references as that project; no new package is needed.

### Choose exactly which files belong

Inside `IProjectAdapter.Open`, use `ManagedSourceHooks.OpenSources(paths)`. Names passed to `admit` are case-sensitive paths relative to the selected data folder, with `/` separators. `recursive: true` visits nested folders; the predicate admits only `index.json` and JSON files under `entries/`. For example, `entries/sub/one.json` belongs; `generated/one.json` does not. The schema in this small fixture is `source.schema.json`: an object with a required string `text` property.

Exclusion is an ownership boundary for project documents, snapshots and file operations; it is not a sandbox for the adapter's own code. Mark admitted generated documents read-only with `readOnlyFile` if you want users to inspect them. Keep `ProjectPaths.Root` pointing at the actual project root when a source workspace lives deeper in the tree, so Play and prepared assets use the right root.

<!-- DOC19 ADMIT -->
```csharp
/// <summary>Admits one index and nested entries; other files never become project documents.</summary>
public static DataProject OpenSources(ProjectPaths paths, bool readOnly = false)
{
    var project = DataProject.Open(paths.Data, paths.Schemas, readOnly,
        readOnlyFile: null, rules: null, recursive: true,
        admit: name => name == "index.json"
            || name.StartsWith("entries/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal));
    project.SchemaFor = _ => "source.schema.json";
    return project;
}
```


### React after a real save

Register this once per project, retain the returned subscription, and dispose it when your adapter/workspace owner is done. `FilesSaved` runs synchronously on the save caller's thread after files are written and sessions marked saved. Native background-save completion runs it on the owner. A single dirty Save raises one notification; Save all raises one list of the files actually written. A no-edit or refused save raises none. Observer exceptions are contained; an observer cannot veto or roll back the finished save.

Keep the callback short. Queue background work there if needed; capture the exact committed inputs before starting it and recheck freshness before publishing generated files. This notification is not permission to overwrite outputs after the source changes.

<!-- DOC19 SAVED -->
```csharp
/// <summary>Observes committed dirty saves on the save caller's thread; dispose on the owner to unsubscribe.</summary>
public static IDisposable AfterSaved(DataProject project, Action<IReadOnlyList<string>> react)
{
    void Saved(IReadOnlyList<DataFile> files) => react(files.Select(file => file.Name).ToArray());
    project.FilesSaved += Saved;
    return new Subscription(() => project.FilesSaved -= Saved);
}
```


### Finish background work without an open panel

Use `DataWorkspace.Ticked` to poll work on the workspace owner's thread. It fires at the end of `FinishSaves`, even when no save is pending; `Draw` already pumps that method each frame. A headless host that draws nothing must call `workspace.FinishSaves()` on its owner. `Ticked` is not a timer, does not create a thread, and does not depend on a panel's visibility. Do not put the pump in the panel's draw callback.

This helper delivers one task result once, including a cancellation/failure message. Retain/dispose its subscription on the owner; disposing unhooks the observer and does not cancel the task. The caller owns task cancellation, fresh-input checks and any publication transaction. Do not touch ImGui, project sessions or panel state from the worker itself.

<!-- DOC19 TICK -->
```csharp
/// <summary>Delivers one completed task from the workspace's owner-thread pump; disposal unhooks, not cancels, it.</summary>
public static IDisposable DeliverOnTick<T>(DataWorkspace workspace, Task<T> work, Action<T> accept, Action<string> failed)
{
    void Tick()
    {
        if (!work.IsCompleted) return;
        workspace.Ticked -= Tick;
        if (work.IsCompletedSuccessfully) accept(work.Result);
        else failed(work.IsCanceled ? "Background work was cancelled." : work.Exception!.GetBaseException().Message);
    }
    workspace.Ticked += Tick;
    return new Subscription(() => workspace.Ticked -= Tick);
}
```


### Check before committing, in the background

Call this after constructing a `DataWorkspace` over the admitted source project. `SaveCheck` returns null to allow the proposed save, or a plain reason to refuse it. It must read/check, not mutate documents or write files. Exceptions refuse the save. BED's single-file native Save uses a worker when `BackgroundSaves` is true, returns a pending message, then finishes through the owning file on the owner pump. An edit, reload, outside change or read-only change can still refuse completion; a passed old verdict does not authorize a newer draft.

`BeingSaved(saving)` identifies the candidate documents this save would persist. Count their drafts and use saved bytes for everything else. This example totals the fixture's text against a caller-supplied limit; another open file's unsaved draft does not count in a single Save. Save all and direct/agent saves keep their existing synchronous behavior. Schemas and per-edit rules still run. Background checking does not turn an unchecked write into a save path, and Save does not automatically rebuild outputs unless the adapter explicitly observes it.

<!-- DOC19 CHECK -->
```csharp
/// <summary>Checks the proposed persisted text total; other unsaved drafts do not count in a single-file save.</summary>
public static void ConfigureBackgroundCheck(DataWorkspace workspace, int maximumCharacters)
{
    ArgumentOutOfRangeException.ThrowIfNegative(maximumCharacters);
    var project = workspace.Project;
    project.SaveCheck = saving =>
    {
        var drafts = project.BeingSaved(saving).ToDictionary(file => file.Name, file => file.Session!.TextToSave);
        var total = 0;
        foreach (var file in project.Files)
        {
            var text = drafts.TryGetValue(file.Name, out var draft) ? draft : File.ReadAllText(file.Path);
            using var json = JsonDocument.Parse(text);
            total = checked(total + json.RootElement.GetProperty("text"u8).GetString()!.Length);
        }
        return total <= maximumCharacters ? null : $"Combined source text exceeds {maximumCharacters} characters.";
    };
    workspace.BackgroundSaves = true;
}
```


### Edit multiline prose through the same document

In the workspace-aware `Configure` overload, use `editor.Add(ManagedSourceHooks.TextPanel(file, editor))`. The panel captures that file's existing session. Valid changes go through `EditorModel.Execute(new SetValue(...))`, so schema/domain refusals, Undo and native Save use the same document as the table/Inspector. There is no separate Apply buffer in this example. BED's raw Text panel still requires its own Apply action.

The native multiline field records a named TextField with value/focus actions; readonly/disabled controls do not accept value requests. This panel refreshes its buffer after Undo or another session edit and restores the previous accepted value when validation refuses. Its small fixed buffer is part of the example: size a production buffer for your schema's supported text limit and handle oversize text explicitly. A source is not editable merely because a panel can show its string. Keep the file's read-only reason and host edit guards.

<!-- DOC19 TEXT -->
```csharp
/// <summary>Edits /text through the file's existing session, including Undo refresh and read-only refusal.</summary>
public static Panel TextPanel(DataFile file, EditorModel<DataDocument> editor)
{
    if (!ReferenceEquals(file.Session, editor.Session)) throw new ArgumentException("Use the file's own editor session.", nameof(editor));
    var buffer = new ImInputString(4096, ""u8);
    var message = "";
    return new Panel("sample.source.text", "Source text", () =>
    {
        var value = editor.Session.Current.At("/text")!.Value.GetString()!;
        if (System.Text.Encoding.UTF8.GetByteCount(value) >= buffer.Capacity)
        { Ui.TextWrapped("This example's source text exceeds its buffer; increase the supported limit before editing."u8); return; }
        var id = ImGui.GetID("Source prose"u8).Value;
        void Reset()
        {
            if (ImGui.GetCurrentContext().ActiveId.Value == id) ImGui.Internal.ClearActiveID();
            buffer.SetValue(value.AsSpan());
        }
        if (buffer.ToString() != value) Reset();
        var why = file.ReadOnlyReason;
        Ui.BeginDisabled(why is not null, why);
        if (Ui.InputTextMultiline("Source prose"u8, buffer, new float2(-1, 140), why is null ? 0 : ImGuiInputTextFlags.ReadOnly))
        {
            if (editor.Execute(new SetValue("/text", JsonSerializer.SerializeToElement(buffer.ToString())))) message = "";
            else { message = string.Join("; ", editor.Refused.Concat(editor.Session.LastIssues).Select(issue => issue.Message)); Reset(); }
        }
        Ui.EndDisabled();
        if (why is not null) Ui.TextWrapped(why);
        if (message.Length > 0) Ui.Status(message, "sample.source.text.refusal");
    }, Side: PanelSide.Centre);
}
```


Run the compiled sample behaviors with `dotnet test tests/Bed.Tests -c Release --filter FullyQualifiedName~ManagedSourceHooksDoc19Tests`. They use temporary self-authored sources and a headless host. `python -B -m pytest tools/test_adapter_hooks_doc19.py -q -p no:cacheprovider` checks that these five published snippets stay identical to the compiled sample regions and still name the real hook declarations.
