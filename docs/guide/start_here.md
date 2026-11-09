# Start here: a linked game editor

Build a linked JSON editor with a map. The example uses two harbour-post files: depots with capacity, and dispatches that name a depot. You will edit them, follow a link, move a depot and check a save.

For a smaller starting point, use [Editing data](editing_data.md). To extend your editor, see [Writing an adapter](writing_an_adapter.md) and [Adding a map](adding_a_map.md). For a custom format, use [Your first document type](your_first_document_type.md); for a tool in your own host, use [Writing a plugin](writing_a_plugin.md).

Read [Content rights](content_rights.md) before importing art, models, sounds or fonts.

Tide Mail is an original example. You do not need private game data, workbooks or artwork.

You need Windows, PowerShell 7, .NET SDK 10 and a working tools checkout. Set BRUTAL_AUTHORING to the checkout's absolute path.

Run the four PowerShell blocks in order, in one shell, starting in an empty folder. They create a disposable example folder and keep build outputs there. They add no package, but may restore existing dependencies that are missing from your cache. This guide uses source projects, not published packages.

## Create a clean working folder

~~~powershell
# T01: one PowerShell 7 shell, initially in an empty folder.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$authoringRoot = $env:BRUTAL_AUTHORING
if (!$authoringRoot -or !(Test-Path -LiteralPath (Join-Path $authoringRoot 'Brutal.Authoring.slnx'))) {
    throw 'Set BRUTAL_AUTHORING to a working tools checkout first.'
}
$work = Join-Path $env:TEMP ('tide-mail-guide-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
Set-Location -LiteralPath $work
function Save-Demo([string]$relative,[string]$text) {
    $file = Join-Path $work $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
    [IO.File]::WriteAllText($file,$text.TrimEnd()+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
}
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
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$project = Join-Path $work 'game'
Write-Output "Example folder: $work"
~~~

## Data, schemas and references

A depot has a stable id, a label, x/y tile coordinates and capacity. Each dispatch names a depot and carries parcels. Depot ids must be unique, targets must exist and assigned parcels must fit capacity. These are only this example's rules.

ProjectPaths.In(root) and the launcher automatically find **reference_map.json with an underscore**, beside data/ and schemas/. The data guide's separate driver explicitly reads reference-map.json with a hyphen; that spelling works only with its explicit loader, or an explicitly supplied ProjectPaths.Map. The map belongs outside data/, not among the data documents.

~~~powershell
# T02: self-authored data, schemas and the launcher's underscore map filename.
Save-Demo 'game/data/depots.json' @'
{
  "kind": "tide-mail-depots",
  "depots": [
    {
      "id": "quay",
      "label": "Quay post",
      "x": 0,
      "y": 0,
      "capacity": 6
    },
    {
      "id": "hill",
      "label": "Hill post",
      "x": 80,
      "y": 40,
      "capacity": 4
    }
  ]
}
'@
Save-Demo 'game/data/dispatches.json' @'
{
  "kind": "tide-mail-dispatches",
  "dispatches": [
    {
      "id": "morning",
      "depot": "quay",
      "parcels": 3
    },
    {
      "id": "evening",
      "depot": "hill",
      "parcels": 2
    }
  ]
}
'@
Save-Demo 'game/schemas/depots.schema.json' @'
{"type": "object", "required": ["kind", "depots"], "properties": {"kind": {"type": "string", "const": "tide-mail-depots"}, "depots": {"type": "array", "minItems": 1, "items": {"type": "object", "required": ["id", "label", "x", "y", "capacity"], "properties": {"id": {"type": "string", "pattern": "^[a-z][a-z0-9-]*$"}, "label": {"type": "string", "minLength": 1}, "x": {"type": "number"}, "y": {"type": "number"}, "capacity": {"type": "integer", "minimum": 0, "maximum": 1000000}}, "additionalProperties": false, "x-label": "label"}}}, "additionalProperties": false}
'@
Save-Demo 'game/schemas/dispatches.schema.json' @'
{"type": "object", "required": ["kind", "dispatches"], "properties": {"kind": {"type": "string", "const": "tide-mail-dispatches"}, "dispatches": {"type": "array", "minItems": 1, "items": {"type": "object", "required": ["id", "depot", "parcels"], "properties": {"id": {"type": "string", "pattern": "^[a-z][a-z0-9-]*$"}, "depot": {"type": "string", "pattern": "^[a-z][a-z0-9-]*$"}, "parcels": {"type": "integer", "minimum": 0, "maximum": 1000000}}, "additionalProperties": false, "x-label": "id"}}}, "additionalProperties": false}
'@
Save-Demo 'game/reference_map.json' @'
{
  "game": "Tide Mail",
  "labels": [
    {
      "file": "depots.json",
      "list": "/depots",
      "field": "label"
    }
  ],
  "references": [
    {
      "file": "dispatches.json",
      "at": "/dispatches/*/depot",
      "target": {
        "file": "depots.json",
        "list": "/depots",
        "key": "id"
      },
      "evidence": "INVENTED Tide Mail depot assignment rule declared for this walk."
    }
  ]
}
'@
~~~

## Map inside the adapter

Configure receives the editor already owned by the file's workspace. Its canvas submits commands to that same session; it does not create another session or save manager. The workspace's normal Save goes through DataFile.Save and the adapter's SaveCheck. Never copy the standalone map's primitive session.Save into this host. A custom save button must call its configured DataFile.Save; use project.SaveAll for coordinated drafts.

SaveCheck starts with saved sibling bytes and overlays **only project.BeingSaved(file)**. An unsaved capacity increase must not authorize saving an otherwise invalid dispatch alone. Save all may accept both together. FileCheck judges a temporary proposal for whole-file actions; it does not write from the callback. [The adapter guide](writing_an_adapter.md#a-check-before-saving) explains group judgment, failure handling and storage limits.

MapKey is the durable scope/kind/id. MapMarker.OutlineId and MapEdit.Touches instead name the **current stock-outline pointer**, such as /depots/0. The binding resolves the current index by stable id on every read and move. A custom FlatOutline may use native ids; this stock outline does not.

A large map uses **PanelSide.Centre** and the available content height. Width is a first-use floating-size hint, not a promise to expand a right dock. A compact read-only status panel can still use Right. Marker labels appear for the selected or hovered marker; absent idle labels do not indicate clipping.

In the map panel, Tab to the canvas until its focus outline appears. Enter selects the next marker; Shift+Enter selects the previous one. Arrows move the selected movable marker by a grid step, or ten screen pixels when that axis has no active grid step. Hold Alt to reverse the snapping setting. Use = or + to zoom in and - to zoom out; after zooming in, Ctrl+arrows pan. With no marker selected, arrows pan too. Press 0 to reset zoom and pan to the current fitted view; use Fit to include markers outside those bounds. Tab leaves the canvas. See [Keyboard](editing_data.md#keyboard) for the viewport, Files forms and closing a project.

~~~powershell
# T03: complete adapter and map, using the supplied file editor.
Save-Demo 'adapter/TideMailAdapter.csproj' @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);BRUTALCORE0011</NoWarn>
    <GenerateDependencyFile>true</GenerateDependencyFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(AuthoringRoot)/src/Brutal.Authoring.Editor.ImGui/Brutal.Authoring.Editor.ImGui.csproj" />
  </ItemGroup>
</Project>
'@
Save-Demo 'adapter/TideMailAdapter.cs' @'
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;

namespace TideMail;

// INVENTED harbour-post data and rules; BORROWED public adapter/session/map wiring.
public sealed class TideMailAdapter : IProjectAdapter
{
    public string Name => "Tide Mail";
    public Dictionary<EditorModel<DataDocument>,MapCanvas<DataDocument>> Maps {get;} = [];
    public bool Recognises(ProjectPaths paths)
    {
        try { using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.Data,"depots.json")));
            return j.RootElement.TryGetProperty("kind",out var k)&&k.ValueKind==JsonValueKind.String&&k.GetString()=="tide-mail-depots"; }
        catch(Exception e) when(e is JsonException or IOException or UnauthorizedAccessException){return false;}
    }
    public DataProject Open(ProjectPaths paths)
    {
        var p=DataProject.Open(paths.Data,paths.Schemas,rules:name=>new UniqueIds(name=="depots.json"?"depots":"dispatches"));
        p.SaveCheck=f=>{var result=Saved(paths.Data);foreach(var c in p.BeingSaved(f))result[c.Name]=c.Session!.Current.Root;return Judge(result);};
        p.FileCheck=c=>{
            var result=Saved(paths.Data);
            if(c.Kind is FileChangeKind.Delete or FileChangeKind.Rename)result.Remove(c.Name);
            if(c.Kind is FileChangeKind.Create or FileChangeKind.Restore)result[c.Name]=Read(c.Text!);
            if(c.Kind is FileChangeKind.Rename)result[c.NewName!]=Read(File.ReadAllText(Path.Combine(paths.Data,c.Name)));
            return Judge(result);
        };
        return p;
    }
    private static JsonElement Read(string s){using var d=JsonDocument.Parse(s);return d.RootElement.Clone();}
    private static Dictionary<string,JsonElement> Saved(string data)=>new(StringComparer.Ordinal){
        ["depots.json"]=Read(File.ReadAllText(Path.Combine(data,"depots.json"))),
        ["dispatches.json"]=Read(File.ReadAllText(Path.Combine(data,"dispatches.json")))};
    private static string? Judge(Dictionary<string,JsonElement> files)
    {
        if(!files.TryGetValue("depots.json",out var ds)||!files.TryGetValue("dispatches.json",out var jobs))
            return "Keep depots.json and dispatches.json; Tide Mail needs both.";
        var capacities=new Dictionary<string,long>(StringComparer.Ordinal);
        foreach(var d in ds.GetProperty("depots").EnumerateArray())
            if(!capacities.TryAdd(d.GetProperty("id").GetString()!,d.GetProperty("capacity").GetInt64()))return "Depot ids must be unique.";
        var totals=capacities.Keys.ToDictionary(x=>x,_=>0L,StringComparer.Ordinal);
        foreach(var job in jobs.GetProperty("dispatches").EnumerateArray())
        {
            var id=job.GetProperty("depot").GetString()!;
            if(!capacities.ContainsKey(id))return $"Dispatch {job.GetProperty("id").GetString()} names missing depot '{id}'; choose an existing depot.";
            totals[id]=checked(totals[id]+job.GetProperty("parcels").GetInt64());
        }
        foreach(var (id,total) in totals)if(total>capacities[id])return $"Depot '{id}' has {total} parcels for capacity {capacities[id]}; lower parcels or increase capacity, then Save all.";
        return null;
    }
    public void Configure(EditorModel<DataDocument> editor,DataTableView table)
    {
        if(table.File!="depots.json")return;
        var map=new MapCanvas<DataDocument>(editor,new DepotMap());Maps[editor]=map;
        editor.Add(new Panel("tide.map","Tide Mail map",()=>map.Draw(Math.Max(160,Brutal.ImGuiApi.ImGui.GetContentRegionAvail().Y)),Width:1000,Height:600,Side:PanelSide.Centre));
    }
    private sealed class UniqueIds(string section):IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument doc)
        {
            if(!doc.Root.TryGetProperty(section,out var rows)||rows.ValueKind!=JsonValueKind.Array)return [];
            var used=new HashSet<string>(StringComparer.Ordinal);var issues=new List<Issue>();var i=0;
            foreach(var r in rows.EnumerateArray())
            {
                if(r.ValueKind==JsonValueKind.Object&&r.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String&&!used.Add(id.GetString()!))
                    issues.Add(Issue.Error("Choose a unique id.",$"/{section}/{i}",$"/{section}/{i}/id"));
                i++;
            }
            return issues;
        }
    }
}
public sealed class DepotMap:IMapSource<DataDocument>
{
    public static MapKey Key(string id)=>new("depots.json","depot",id);
    public MapSnapshot Read(DataDocument doc)
    {
        var markers=doc.Root.GetProperty("depots").EnumerateArray().Select((d,i)=>{
            var id=d.GetProperty("id").GetString()!;
            return new MapMarker(Key:Key(id), OutlineId:$"/depots/{i}", Label:d.GetProperty("label").GetString()!,
                Position:new(d.GetProperty("x").GetDouble(),d.GetProperty("y").GetDouble()));
        }).ToArray();
        return new(new("tide-tiles",0,"tiles","X","Y"),markers,[]);
    }
    public MapEdit<DataDocument>? MoveBy(DataDocument doc,MapKey key,MapPoint delta)
    {
        if(key.Scope!="depots.json"||key.Kind!="depot"||!delta.IsFinite)return null;
        var found=doc.Root.GetProperty("depots").EnumerateArray().Select((d,i)=>(d,i)).Where(v=>v.d.GetProperty("id").GetString()==key.Id).ToArray();
        if(found.Length!=1)return null;var (d,i)=found[0];var at=$"/depots/{i}";
        return new([new SetValue(at+"/x",JsonSerializer.SerializeToElement(d.GetProperty("x").GetDouble()+delta.X)),
                    new SetValue(at+"/y",JsonSerializer.SerializeToElement(d.GetProperty("y").GetDouble()+delta.Y))],[at]);
    }
}
'@
~~~

## Run the whole route

Run the driver below to follow a dispatch to its depot, return, click and drag a map marker, undo and redo, then save. It uses headless mouse events and the same adapter, workspace and commands as the editor.

The driver also checks refused orphan and over-capacity drafts. It checks that saving one file cannot rely on an unsaved sibling, then uses Save all and reopens both files.

The input is deliberately indented. Working-document GetRawText may change after an edit even for an untouched sibling. Check **JsonSchemas.Same for values**, the document format's output for serialized representation, and **the actually saved file** for disk representation. Do not treat temporary in-memory formatting as a lost-data finding.

~~~powershell
# T04: complete proof and actual launcher load, without any OS window.
Save-Demo 'proof/Proof.csproj' @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);BRUTALCORE0011</NoWarn>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../adapter/TideMailAdapter.csproj" /></ItemGroup>
</Project>
'@
Save-Demo 'proof/Program.cs' @'
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using TideMail;

if(args.Length!=2)throw new Exception("usage: proof project settings");
var root=Path.GetFullPath(args[0]);var settings=Path.GetFullPath(args[1]);
Directory.CreateDirectory(settings);
void Need(bool ok,string why){if(!ok)throw new Exception(why);}
var paths=ProjectPaths.In(root)??throw new Exception("Layout missing");
Console.WriteLine("PROJECT_MAP="+(paths.Map??"(none)"));
var a=new TideMailAdapter();Need(a.Recognises(paths),"Our game must be recognised");
var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(settings,"recent.json"))){Adapter=a};
Need(launcher.Open(paths),launcher.Said);Console.WriteLine(launcher.Said);
var w=launcher.Workspace!;var project=w.Project;
Need(w.Show("dispatches.json"),w.Said);var table=w.TableOf("dispatches.json")!;
w.Window!.Editor.Select("/dispatches/0");
var followed=table.Follow("/dispatches/0","depot");
Console.WriteLine("FOLLOW="+followed+"; "+table.Said+"; "+w.Said);
Need(followed&&w.Active!.Name=="depots.json"&&w.Window!.Editor.Primary=="/depots/0","Cross-file Follow must select Quay");
Need(w.Back()&&w.Active!.Name=="dispatches.json","Back must return to dispatch");
Need(project.Dirty.Count==0,"Link navigation edited a file");
Need(w.Show("depots.json"),w.Said);
var editor=w.Window!.Editor;var depot=project.Find("depots.json")!;var dispatch=project.Find("dispatches.json")!;
Need(editor.Select("/depots/1"),"Select the other depot before testing a click");
// Compose the adapter panel with the stock workspace, then feed actual mouse input.
var map=a.Maps[editor];
foreach(var p in editor.Panels)editor.SetOpen(p.Id,p.Id=="tide.map");
using(var host=new HeadlessHost(1400,900))
{
    EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(launcher.Draw,5);
    var pane=ImGui.Internal.FindWindowByName("Tide Mail map###panel.tide.map"u8);
    Need(!pane.IsNull()&&pane.Size.X>600,"Centre map should have a useful working width");
    Console.WriteLine("CENTRE_MAP_WIDTH="+pane.Size.X);
    var at=map.ScreenOf(DepotMap.Key("quay"));Need(at is not null,"Map panel was not drawn");
    host.Click(launcher.Draw,at!.Value.X,at.Value.Y);Need(editor.Primary=="/depots/0","Map click did not select stock outline id");
    var before=depot.Session!.Current.Root.GetRawText();var other=depot.Session.Current.At("/depots/1")!.Value.GetRawText();
    host.Drag(launcher.Draw,at.Value,at.Value+new float2(40,-20));host.Frames(launcher.Draw);
    var moved=depot.Session.Current.Root.GetRawText();
    Need(moved!=before&&depot.Session.History.UndoCount==1,"Drag must be one actual edit");
    Console.WriteLine("MAP_SELECTION="+string.Join(",",editor.Selection));Console.WriteLine("RAW_SIBLING_TEXT_EQUAL="+(depot.Session.Current.At("/depots/1")!.Value.GetRawText()==other));
    using(var actual=JsonDocument.Parse(depot.Session.Format.Write(depot.Session.Current)))Console.WriteLine("PERSISTED_OTHER_IDENTICAL="+(actual.RootElement.GetProperty("depots")[1].GetRawText()==other));
    using(var oldOther=JsonDocument.Parse(other))Console.WriteLine("SEMANTIC_OTHER_IDENTICAL="+JsonSchemas.Same(oldOther.RootElement,depot.Session.Current.At("/depots/1")!.Value));
    using(var oldOther=JsonDocument.Parse(other))Need(JsonSchemas.Same(oldOther.RootElement,depot.Session.Current.At("/depots/1")!.Value),"Other depot values changed");
    using(var serialized=JsonDocument.Parse(depot.Session.Format.Write(depot.Session.Current)))Need(serialized.RootElement.GetProperty("depots")[1].GetRawText()==other,"Other depot saved representation changed");
    Need(depot.Session.Undo()&&depot.Session.Current.Root.GetRawText()==before,"Undo failed");
    Need(depot.Session.Redo()&&depot.Session.Current.Root.GetRawText()==moved,"Redo failed");
    Need(depot.Save(),"Map draft failed checked save: "+string.Join(" ",depot.Problems.Select(x=>x.Message)));
    using(var saved=JsonDocument.Parse(File.ReadAllText(depot.Path)))Need(saved.RootElement.GetProperty("depots")[1].GetRawText()==other,"Other depot disk bytes changed");
    host.Frames(launcher.Draw);host.Save(Path.Combine(settings,"tide-map.png"));
    Console.WriteLine("MAP: real click/drag; stock outline selection; one undo/redo; other depot preserved; checked save; tide-map.png");
}
Need(dispatch.Open(),"Dispatch cannot open");var s=dispatch.Session!;
var db=File.ReadAllBytes(depot.Path);var jb=File.ReadAllBytes(dispatch.Path);
Need(s.Execute(new SetValue("/dispatches/0/depot",JsonSerializer.SerializeToElement("absent"))),"Schema-valid orphan draft should be editable");
Need(!dispatch.Save()&&dispatch.Dirty&&jb.SequenceEqual(File.ReadAllBytes(dispatch.Path)),"Missing depot must refuse safely");
Console.WriteLine("ORPHAN_REFUSAL="+string.Join(" ",dispatch.Problems.Select(x=>x.Message)));Need(s.Undo(),"Undo orphan");
Need(s.Execute(new SetValue("/dispatches/0/parcels",JsonSerializer.SerializeToElement(8))),"Eight is schema-valid");
Need(!dispatch.Save()&&jb.SequenceEqual(File.ReadAllBytes(dispatch.Path)),"Over-capacity save must refuse");
Console.WriteLine("BUDGET_REFUSAL="+string.Join(" ",dispatch.Problems.Select(x=>x.Message)));
Need(depot.Session!.Execute(new SetValue("/depots/0/capacity",JsonSerializer.SerializeToElement(10))),"Higher capacity draft valid");
Need(!dispatch.Save(),"An unsaved capacity increase cannot authorize a single-file save");
Need(db.SequenceEqual(File.ReadAllBytes(depot.Path))&&jb.SequenceEqual(File.ReadAllBytes(dispatch.Path)),"Single-file refusal changed disk");
Need(project.SaveAll().Count==0&&project.Dirty.Count==0,"Coordinated SaveAll must succeed");
using(var d=JsonDocument.Parse(File.ReadAllText(depot.Path)))Need(d.RootElement.GetProperty("depots")[0].GetProperty("capacity").GetInt32()==10,"Capacity not saved");
using(var j=JsonDocument.Parse(File.ReadAllText(dispatch.Path)))Need(j.RootElement.GetProperty("dispatches")[0].GetProperty("parcels").GetInt32()==8,"Parcels not saved");
var reopened=a.Open(paths);Need(reopened.Find("depots.json")!.Open()&&reopened.Find("dispatches.json")!.Open(),"Native reopen failed");
Console.WriteLine("SAVE: orphan/over-capacity refused; disk and drafts retained; unsaved sibling not trusted; coordinated SaveAll and native reopen PASS");
Console.WriteLine("TIDE_MAIL_WALK_PASS");
'@
dotnet build (Join-Path $work 'proof/Proof.csproj') -c Release "-p:AuthoringRoot=$authoringRoot"
if ($LASTEXITCODE -ne 0) { throw 'Tide Mail build failed.' }
$proofDll = Join-Path $work 'build/bin/Proof/release/Proof.dll'
# A fresh copy keeps launcher loading independent of the proof's accepted edits.
$launcherProject = Join-Path $work 'launcher-game'
Copy-Item -LiteralPath $project -Destination $launcherProject -Recurse
dotnet $proofDll $project (Join-Path $work 'proof-settings')
if ($LASTEXITCODE -ne 0) { throw 'Tide Mail walk failed.' }
dotnet build (Join-Path $authoringRoot 'src/Brutal.Authoring.Launcher/Brutal.Authoring.Launcher.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
$appDll = Join-Path $work 'build/bin/Brutal.Authoring.Launcher/release/BED.dll'
$adapterDll = Join-Path $work 'build/bin/TideMailAdapter/release/TideMailAdapter.dll'
dotnet $appDll $launcherProject --adapter $adapterDll --settings (Join-Path $work 'launcher-settings') --shot (Join-Path $work 'launcher.png')
if ($LASTEXITCODE -ne 0) { throw 'Launcher adapter load failed.' }
if (!(Test-Path -LiteralPath (Join-Path $work 'launcher.png'))) { throw 'No launcher image.' }
Write-Output 'TIDE_MAIL_LAUNCHER_PASS'
~~~

The build must report zero errors. The proof prints TIDE_MAIL_WALK_PASS; the actual fresh launcher process says it opened Tide Mail with two files, creates launcher.png and prints TIDE_MAIL_LAUNCHER_PASS. It also creates proof-settings/tide-map.png: inspect launcher.png for the file list and two map markers, and proof-settings/tide-map.png for selected Quay post, its outline and its inspector. No OS window opens and no desktop capture is used.

A refusal should name the missing depot or capacity problem. The bad draft and history remain available; correct or undo them, then retry through the same project authority. The tutorial never uses a primitive save to bypass a refusal.

## Where to go next

Use [Editing data](editing_data.md) for nested fields, references and ordinary file operations; [Writing an adapter](writing_an_adapter.md) for registered checks, schema selection and loader diagnostics; and [Adding a map](adding_a_map.md) for the standalone neutral-plan version, custom outlines, coordinates, gesture limits and the distinction between a drawing and a graph editor.

These examples cover the shapes and rules shown here, not every schema, game or platform. Trust only plug-ins you have reviewed. Do not assume saves recover safely from a power failure; read the storage limits in the adapter guide. Running a local example does not approve publication or complete dependency review.
