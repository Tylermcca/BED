# Write your first plug-in

Build a plug-in that renames and inspects memos in a Scene. Then use the Harbor Relay example to connect project rules, panels, links and an agent entry point. Keep game-specific meaning in the adapter.

The examples use original data and no game art. The manifest's MIT label applies to this example; it does not change any library or dependency licence.

Use Windows PowerShell 5.1 with this checkout and its existing .NET 10 dependencies. Work in a disposable copy of the tools repository. The examples do not reference or build a game, and their checks open no graphics window.

## Make the folder

Run from the repository root. This only creates your example folder.

~~~powershell
New-Item -ItemType Directory -Force 'samples\MemoPlugin' | Out-Null
~~~

Save the next block as samples/MemoPlugin/plugin.json. The host reads this description before calling Register. Version is your plug-in's own version; api is the oldest library API it supports. The licence is optional information under the user's current policy: omitting it loads with a warning. A supplied expression is still checked for SPDX syntax. Known clashes with enabled plug-ins or the host project licence are warnings in PluginStatus.Warnings, not registration refusals.

~~~json
{"id":"memos","name":"Memos","version":"1.0.0","api":"0.1","license":"MIT","permissions":[],"description":"Rename and inspect self-authored memos."}
~~~

Before API 1.0, the minor version must match the host as well. On API 1 or later, the major must match and the host cannot be older than the requested version. A future or retired incompatible API is refused with an issue identifying requested and provided versions; Register is not called.

Permissions are empty because this example only edits through commands and draws UI. The supported permission names are files.read, files.write, network, process and clipboard. Each request needs a plain reason. They describe intent for the user; they do not grant or enforce operating-system access.

## Describe one complete extension

Save the complete block as samples/MemoPlugin/MemoPlugin.cs. Keep the public comments: the checkout checks public API documentation. PluginAcceptanceTests.cs contains the same MemoExample example.

~~~csharp
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Brutal.ImGuiApi;

namespace MemoExample
{
    // INVENTED memo content; MIXED registration through the library's existing seams.
    /// <summary>Renames a self-authored memo through the scene command boundary.</summary>
    public sealed record RenameMemo(string Id, string Name) : ICommand<Scene>
    {
        /// <inheritdoc />
        public CommandResult<Scene> Apply(Scene document) =>
            string.IsNullOrWhiteSpace(Name)
                ? CommandResult<Scene>.Refused(Issue.Error("Give the memo a name.", Id, "$.name"))
                : new SetSceneProperty(Id, "name", JsonSerializer.SerializeToElement(Name)).Apply(document);
    }

    /// <summary>A complete, explicitly registered memo extension.</summary>
    public sealed class MemoPlugin(PluginManifest manifest) : IEditorPlugin<Scene>
    {
        /// <inheritdoc />
        public PluginManifest Manifest => manifest;

        private static bool IsMemo(SceneObject obj) =>
            obj.Properties.TryGetValue("kind", out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString() == "memo";

        /// <inheritdoc />
        public void Register(PluginRegistrar<Scene> add) => add
            .Command<RenameMemo>("memos.rename", 1, "Rename memo")
            .ObjectType(new ObjectType<Scene, SceneObject>(
                "memos.note", "Memo", (d, id) => d.Find(id) is { } o && IsMemo(o) ? o : null,
                [new("name", "Name", PropertyKind.Text,
                    o => o.Properties.TryGetValue("name", out var value) ? value.GetString() : o.Id,
                    (o, value) => new RenameMemo(o.Id, (string)value!),
                    Help: "A short name for this memo.")]))
            .Panel(new Panel("memos.overview", "Memos", () =>
                ImGui.Text($"{add.Editor.Session.Current.Objects.Count(IsMemo)} memo(s)")))
            .Maker("memos.rename", e => e.Primary is { } id && e.Session.Current.Find(id) is { } o && IsMemo(o)
                ? new RenameMemo(id, "Hello from the palette") : null)
            .Shortcut("memos.rename", "Alt+M");
    }
}
~~~

RenameMemo returns a new candidate through SetSceneProperty. A blank name returns a targeted issue with a remedy. The session still validates, records undo and writes the journal. Never mutate Current directly.

The type's finder recognises kind=memo in the object's properties. Its name row reads that object and creates RenameMemo; there is no generic dictionary setter in the inspector. This example host registers only the memo inspector type, giving each memo one clear match. If an application also registers a catch-all scene.object type, it needs an explicit policy for overlapping type finders.

Register gathers additions before the host commits them:

- command memos.rename has a stable id and argument version;
- object type memos.note describes the memo fields;
- panel memos.overview draws the current count from the shared editor;
- the maker turns a selected memo into a command with a concrete name, returning null for any other context;
- Alt+M is a default shortcut; a person's own binding wins.

Every id belongs to the manifest's memos prefix. A panel's open/closed state is view state and does not dirty the document. The palette's panel action is EditorWindow<Scene>.PanelAction plus the panel id. A throwing panel is closed and reported.

## Build it as a tools project

Save this as samples/MemoPlugin/MemoPlugin.csproj. The reference stays inside the tools checkout; it supplies the editor/core and ImGui dependencies already used here. There are no new NuGet packages.

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Registration takes managed strings; this UI literal hint does not apply. -->
    <NoWarn>$(NoWarn);BRUTALCORE0011</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Brutal.Authoring.Editor.ImGui/Brutal.Authoring.Editor.ImGui.csproj" />
  </ItemGroup>
</Project>
~~~

BRUTALCORE0011 is a UTF-8 UI-literal hint. The registration method takes ordinary managed strings, so this example suppresses that inapplicable hint, as the existing test project does. Other checks remain enabled.

~~~powershell
dotnet build samples\MemoPlugin\MemoPlugin.csproj --nologo -v:q
~~~

Expected build result:

~~~text
Build succeeded.
    0 Warning(s)
    0 Error(s)
~~~

## Give it a host and check it without a screen

Add a test like the one below to an xUnit class in the existing ImGuiCollection. It creates a memo, session, editor and inspector, registers the plug-in, runs its palette command and checks undo.

Do not run tests that create ImGui contexts concurrently. Keep only one HeadlessHost alive at a time.

The method uses the imports already shown plus System.Numerics, System.IO, System.Text.Json, Brutal.Authoring.Editor.ImGui and Xunit. Those are the existing test project's imports. It does not discover or load an assembly from a plug-in folder.

~~~csharp
    [Fact]
    public void GuideHeadlessMemoWorkflow()
    {
        var manifest = PluginManifest.Read("""
            {"id":"memos","name":"Memos","version":"1.0.0","api":"0.1","license":"MIT","permissions":[],"description":"Rename and inspect self-authored memos."}
            """);
        var item = SceneObject.At("note", null, Vector3.Zero) with
        {
            Properties = SceneObject.At("note", null, Vector3.Zero).Properties
                .Add("kind", JsonSerializer.SerializeToElement("memo"))
                .Add("name", JsonSerializer.SerializeToElement("First memo"))
        };
        var session = new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(),
            SceneKit.Catalog(), new Scene([item]));
        var editor = new EditorModel<Scene>(session, SceneEditing.Outline);
        var inspector = new Inspector<Scene>(editor);
        var shortcuts = new Shortcuts();
        var plugins = new PluginHost<Scene>(editor, inspector, shortcuts);
        Assert.True(plugins.Add(new MemoExample.MemoPlugin(manifest)));
        editor.Select("note");
        var window = new EditorWindow<Scene>(editor, inspector, shortcuts);
        using var host = new HeadlessHost(1600, 1000);
        host.Frames(window.Draw);
        Assert.True(window.Run("memos.rename"));
        host.Frames(window.Draw);
        Assert.Equal("Hello from the palette", session.Current.Find("note")!.Properties["name"].GetString());
        Assert.True(host.Covered() > 0);
        var folder = Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS");
        var path = folder is null
            ? Path.Combine(Path.GetTempPath(), "memo-guide-" + Guid.NewGuid().ToString("N") + ".png")
            : Path.Combine(folder, "memos.png");
        if (folder is not null) Directory.CreateDirectory(folder);
        try { host.Save(path); Assert.True(new FileInfo(path).Length > 8); }
        finally { if (folder is null && File.Exists(path)) File.Delete(path); }
        Assert.Equal("memos.rename", Assert.Single(session.Journal).Command);
        Assert.True(session.Undo());
        Assert.Equal("First memo", session.Current.Find("note")!.Properties["name"].GetString());
    }
~~~

PluginHost.Add is the point at which registration becomes visible, after all checks pass. Keep the returned status and show its issues if false. Do not proceed as if registration succeeded.

The test contains its own copy of the example classes. Build the standalone class library separately when you check that project. In your host, pass the MemoPlugin instance to PluginHost; placing its DLL in a folder does not load it.

The existing tests can save a frame with this block:

~~~powershell
$env:BRUTAL_AUTHORING_SHOTS = Join-Path $env:TEMP 'memos-guide-shots'
dotnet test tests\Brutal.Authoring.Tests\Brutal.Authoring.Tests.csproj --filter "FullyQualifiedName~PluginAcceptanceTests.Guide" --nologo -v:q
[Environment]::SetEnvironmentVariable('BRUTAL_AUTHORING_SHOTS', $null, 'Process')
~~~

Expected filtered result:

~~~text
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
~~~

The PNG is under the temporary folder's memos-guide-shots/memos.png. The frame shows the selected memo renamed to Hello from the palette in the outliner and name inspector, a Memos panel with one memo, and a successful rename status. The test then undoes it. The verification host uses 1600×1000 so the panel fits beside the main editor.

HeadlessHost feeds real ImGui events and rasterises on the CPU. Click, Press and Type target actual widgets, not mock callbacks. For editable ImInputString buffers, refresh EvaluateLength after native text editing before reading the managed value, including when using EnterReturnsTrue. Turning Drawing off keeps UI/input logic running; turn it back on and run a frame before inspecting pixels. Saved frames are local test artifacts; no image is added to the public repository.

## Add a play mode

A play mode runs your game's code from the current authored document, then returns to editing on Stop ([goal 14](../goals.md#14-play-from-the-editor)). The example below previews a scene count. Keep score, input and simulation state in IPlaySession, outside the document.

Implement IPlaySession, then register a PlayMode through PluginRegistrar. Its id belongs to your plugin.json manifest's prefix. Here the existing memos manifest owns memos.preview. This exact example compiles in PlayAcceptanceTests.cs; it uses the existing Scene document and packages.

~~~csharp
using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Brutal.ImGuiApi;

namespace PlayGuideExample
{
    /// <summary>INVENTED read-only scene preview with its own play lifetime.</summary>
    public sealed class PreviewGame(Scene snapshot) : IPlaySession
    {
        /// <inheritdoc />
        public string Status => $"Previewing {snapshot.Objects.Length} object(s)";
        /// <inheritdoc />
        public void Draw() => ImGui.Text(Status);
        /// <inheritdoc />
        public void Dispose() { }
    }

    /// <summary>Adds a preview through the existing plug-in seam.</summary>
    public sealed class PreviewPlugin(PluginManifest manifest) : IEditorPlugin<Scene>
    {
        /// <inheritdoc />
        public PluginManifest Manifest => manifest;
        /// <inheritdoc />
        public void Register(PluginRegistrar<Scene> add) => add.PlayMode(
            new PlayMode<Scene>("memos.preview", "Memo preview", d => new PreviewGame(d)));
    }
}
~~~

The host explicitly adds a PreviewPlugin instance to PluginHost, as it does MemoPlugin above; a play mode is checked together with every other staged addition. Registering an invalid or duplicate id refuses the whole registration. No assembly discovery is involved.

The editor passes Start the current immutable document as a snapshot. Holding that snapshot after Stop does not follow later edits. Treat it as read-only: build mutable gameplay state separately, and release your resources in Dispose. The window calls Draw each game frame and shows Status beside Stop. A throwing Start is reported; a throwing Draw stops and disposes the game and reports the error.

While playing, EditorModel.Execute and inspector Set/Reset are locked, along with the window's undo, redo, save and save-as actions; their reason tells the person to stop playing. Panel toggles and the palette remain available. Use the editor's edit boundary in your tools. Stop discards the play session, leaving authored text, undo/redo, journal, Dirty and the saved file unchanged.

Save-as also respects the play lock, including direct window.SaveAs(path): no file is written and the document's file path stays unchanged. The palette action is "editor.file_save_as". Keep save actions on the editor/project boundary so its other save refusals apply too.

The palette starts this example with EditorWindow<Scene>.PlayAction + "memos.preview" and stops it with "editor.stop". The matching UI buttons run those actions too. This example only previews a count; a game can draw a board and handle its own clicks in Draw, as [the checkers walkthrough](make_a_game.md) demonstrates.

Test with HeadlessHost inside the existing ImGuiCollection: create the document/session/editor/inspector, add the plug-in, run the play action, call host.Frames(window.Draw), check the game's Status and state, then run Stop and compare the original document, history and journal. HeadlessHost uses CPU rendering with no window, GPU or OS input. The compiled guide workflow checks this example and a drawn frame; the same acceptance file also clicks both a short four-by-four endgame and a complete standard eight-by-eight opening match to a win.

From the repository root in Windows PowerShell 5.1:

~~~powershell
dotnet test tests\Brutal.Authoring.Tests\Brutal.Authoring.Tests.csproj --filter "FullyQualifiedName~PlayAcceptanceTests.GuideHeadlessPlayWorkflow" --nologo -v:q
~~~

Only one HeadlessHost can be alive at once; the collection serialises tests that share ImGui's process-wide context. For a throwing game, assert that Game and Playing become null, Dispose runs, Status includes the error, and editing works again.

## Refusals are part of the workflow

PluginHost checks API compatibility before Register, then stages additions. Owned, valid, unused ids are required; workspace command references and shortcut syntax must resolve. A bad addition or throwing Register disables the plug-in, reports issues and commits none of its staged additions. The rest of the editor carries on.

The acceptance tests check every addition category's ownership/collisions, unknown references, invalid shortcuts and registration exceptions. They also test a null maker's unavailable reason, panel toggling/first-show size/throw handling, scripted button/checkbox/text input, Drawing=false, Covered counts and PNG signature/chunks/CRCs/zlib-decoded RGBA. The PNG checks use an independent bitwise CRC and known non-symmetric pixels; [the PNG specification](https://www.w3.org/TR/png-3/) supplies the file-format reference.

## Trusted means trusted

These plug-ins are **trusted, in-process C# code**. The host's staging protects its registration workflow; it is not a security sandbox or transaction over arbitrary C# side effects. A constructor or Register implementation could access files, network or native APIs directly. Manifest permissions cannot stop it. Review and enable code you trust; do not call an untrusted constructor just to inspect its manifest.

Assembly discovery/loading, isolation and permission enforcement are not implemented by this seam. The host takes explicit objects today. For a larger extension, add focused commands and tests through these same public seams before adding infrastructure.

## Offer actions in the palette

The memo extension above registers document commands. A data adapter can also offer actions owned by a panel: validate a catalogue, run a game's exporter, or rebuild an index. A PanelCommand has a local id, a label, a callback returning success, and an optional Unavailable callback returning a reason. The window checks availability, opens a closed panel, then runs the action. Panel commands are refused during play. A callback that edits must still use EditorModel.Execute.

The next five sections build Harbor Relay, an original beacon-routing example. Its two settings must stay in order, and a separate exporter owns its generated beacon catalogue. All example files stay in the tools checkout.

Make samples/HarborRelay and save the following as RelayActions.cs there. The action checks files already on disk; unsaved drafts must be saved or undone first.

~~~csharp
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.ImGuiApi;

namespace HarborRelay;

/// <summary>Harbor Relay's small, synchronous check action.</summary>
public sealed class RelayActions(DataProject project)
{
    /// <summary>The last check's result, shown by the panel.</summary>
    public string Status { get; private set; } = "No check run yet.";

    /// <summary>Why the disk check is unavailable, or null.</summary>
    public string? Unavailable() => project.Dirty.Count > 0
        ? "Save or undo the drafts first; this check reads files on disk."
        : null;

    /// <summary>Checks every file without saving anything.</summary>
    public bool Check()
    {
        if (Unavailable() is { } why) { Status = why; return false; }
        var check = DataProject.Open(project.DataFolder, project.SchemaFolder,
            readOnly: true, rules: project.RulesFor);
        var refused = check.Files.Where(f => !f.Open()).ToList();
        Status = refused.Count == 0 ? "All relay files open."
            : string.Join(" ", refused.SelectMany(f => f.Problems).Select(p => p.Message));
        return refused.Count == 0;
    }

    /// <summary>A panel whose action is also discoverable in the palette.</summary>
    public Panel AsPanel() => new("relay.tools", "Relay checks",
        () => ImGui.Text(Status), Width: 360, Height: 220,
        Commands: [new PanelCommand("check", "Check relay files", Check, Unavailable)],
        Side: PanelSide.Right);
}
~~~

The action id is EditorWindow<DataDocument>.PanelCommandAction + "relay.tools/check", currently editor.panel_do.relay.tools/check. The panel's own toggle is a separate action. Centre, Left and Right describe the first docked layout: Left is above the outliner, Right above the inspector, and Centre is a centre tab. People can move the panel afterward. Call DrawDocked through the workspace to use that layout. The host must first call EditorWindow<DataDocument>.Configure(io) on its ImGui IO, before the first frame, to enable docking and keyboard navigation. A HeadlessHost test needs that same setup before drawing a docked workspace; the earlier memo test uses the undocked Draw method.

The check creates fresh read-only sessions with the same game rules, so files already open in the workspace are still checked from disk. It does not reload or replace the workspace's cached sessions; Refresh only relists files. This check is short. A real exporter should start background work, refuse a second run while busy, and show its exit status and captured output. Keep its working directory and arguments under the game's control. A palette entry is an action hook, not a process runner or a substitute for an exporter's own checks.

## Protect files another tool writes

Save RelayProject.cs beside RelayActions.cs. Open receives the fixture root containing data and schemas folders. Each callback receives a file name, not its full path. Return a reason for a protected file and null for one the editor may save.

~~~csharp
using Brutal.Authoring.Data;

namespace HarborRelay;

/// <summary>One project factory shared by people and agents.</summary>
public static class RelayProject
{
    /// <summary>Opens Harbor Relay's data with its ownership policy and game rules.</summary>
    public static DataProject Open(string root, bool readOnly = false) =>
        DataProject.Open(Path.Combine(root, "data"), Path.Combine(root, "schemas"),
            readOnly: readOnly,
            readOnlyFile: name => name == "beacons.json"
                ? "The beacon exporter owns this file; change its source and export again."
                : null,
            rules: name => name == "relay.json" ? new RelayRules() : null);
}
~~~

A protected file can still open, preview, hold draft edits and undo. Saving is refused and the draft stays available; readOnlyFile is a save policy, not an edit lock. A project opened readOnly: true refuses every file's save and its reason takes precedence. Inspect ReadOnlyReason and Problems when explaining a refusal. Ordinary outside-change and outside-deletion guards still apply.

Do not use a raw session Save to bypass the owning DataFile's policy. For the workspace, use its save actions or DataFile.Save; for agents, use AgentSession.Save. Neither manifest permissions nor readOnlyFile prevent arbitrary trusted plug-in code from writing a path directly.

## Check what the schema can't

A schema can require two nonnegative numbers. Harbor Relay's loader also requires maximum to exceed minimum. Keep that check in the loader and adapt the loader into IValidator<DataDocument>, so opening and editing use the same decision as the game.

Save RelayRules.cs. It contains the example loader and validator adapter. The adapter serializes the candidate in memory without writing a file. Expected data errors become Issues; programming faults are not hidden by a catch-all handler.

~~~csharp
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;

namespace HarborRelay;

/// <summary>The invented game's loader for its two timing settings.</summary>
public static class RelayLoader
{
    /// <summary>Parses settings and checks the relationship the game needs.</summary>
    public static (int Minimum, int Maximum) Parse(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var minimum = root.GetProperty("minimum").GetInt32();
        var maximum = root.GetProperty("maximum").GetInt32();
        if (maximum <= minimum)
            throw new InvalidDataException("Maximum must exceed minimum.");
        return (minimum, maximum);
    }
}

/// <summary>Checks the candidate with the same loader the game uses.</summary>
public sealed class RelayRules : IValidator<DataDocument>
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(DataDocument document)
    {
        try
        {
            _ = RelayLoader.Parse(new DataFormat().Write(document));
            return [];
        }
        catch (Exception e) when (e is InvalidDataException or JsonException
            or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return [Issue.Error("Harbor Relay cannot load these settings: " + e.Message,
                null, "$.maximum")];
        }
    }
}
~~~

The rules callback returns null for files needing only their schema. The project combines extra rules with the schema; extra rules do not replace it. A rule runs on opening and on each edit, so keep it deterministic, quick and free of side effects. Point an Issue at the field or item when you know it. Avoid starting the game or writing generated files from Validate.

The examples below test a schema-valid candidate with minimum=2, maximum=1. The loader adapter refuses it and the previous maximum remains 5. A negative minimum is independently refused by the schema. The same checks apply when the project is served to an agent.

## Link your game's data

The [data guide's reference-map section](editing_data.md#link-your-data) explains map entries and resolver outcomes. This section supplies the adapter: pass a checked ReferenceMap to DataWorkspace, and use configure to add the game's panels once for each file editor as it is created. Switching back to a cached file does not configure it again.

Make samples/HarborRelay/fixture/data and fixture/schemas. Save fixture/data/beacons.json:

~~~json
{"beacons":[{"id":"north","name":"North light"},{"id":"south","name":"South light"}]}
~~~

Save fixture/data/relay.json:

~~~json
{"minimum":2,"maximum":5,"jobs":[{"id":"mail","name":"Morning mail","beacon":"north"}]}
~~~

Save fixture/schemas/beacons.schema.json:

~~~json
{"type":"object","required":["beacons"],"properties":{"beacons":{"type":"array","items":{"type":"object","required":["id","name"],"properties":{"id":{"type":"string"},"name":{"type":"string"}},"additionalProperties":false}}},"additionalProperties":false}
~~~

Save fixture/schemas/relay.schema.json:

~~~json
{"type":"object","required":["minimum","maximum","jobs"],"properties":{"minimum":{"type":"integer","minimum":0},"maximum":{"type":"integer","minimum":0},"jobs":{"type":"array","items":{"type":"object","required":["id","name","beacon"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"beacon":{"type":"string","description":"An id from beacons.json /beacons."}},"additionalProperties":false}}},"additionalProperties":false}
~~~

Save fixture/reference_map.json:

~~~json
{
  "game": "Harbor Relay",
  "labels": [
    {"file":"beacons.json","list":"/beacons","field":"name"},
    {"file":"relay.json","list":"/jobs","template":"{name} ({id})"}
  ],
  "references": [
    {
      "file":"relay.json","at":"/jobs/*/beacon",
      "evidence":"schemas/relay.schema.json: jobs beacon description",
      "target":{"file":"beacons.json","list":"/beacons","key":"id"}
    }
  ]
}
~~~

Save RelayWorkspace.cs beside the other C# files:

~~~csharp
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;

namespace HarborRelay;

/// <summary>Connects the game's project, map and shared action panel.</summary>
public static class RelayWorkspace
{
    /// <summary>Creates the workspace without opening a native window.</summary>
    public static DataWorkspace Create(string root)
    {
        var project = RelayProject.Open(root);
        var map = ReferenceMap.Read(File.ReadAllText(Path.Combine(root, "reference_map.json")));
        var actions = new RelayActions(project);
        return new DataWorkspace(project,
            configure: (editor, table) => editor.Add(actions.AsPanel()),
            map: map);
    }
}
~~~

Call Show("relay.json"), then draw the workspace in your existing editor host. The map labels a job "Morning mail (mail)" and its beacon cell follows to /beacons/0 in beacons.json. Back returns to the job. These are view operations and create no undo step. The catalogue's read-only policy also follows it into the workspace.

For richer maps, template combines label fields; parts resolves each component of a compound id; narrow limits targets to a container derived from source context. A target's where adds AND conditions in the closed forms {at, equalsSource} and {absent}. Types matter: text "2" is not numeric 2, and a present null is not an absent member. A missing or structured required source operand gives NeedsScope, while a missing candidate operand excludes that candidate. None of these features guesses what a game's ids mean.

For a field whose property key and stored value are both links, a custom caller can use Resolve(project, file, pointer, value, ReferenceIntent.Key) or ReferenceIntent.Value. Keep legacy callers' four-argument meaning unchanged. The stock workspace table and agent Get currently call that legacy form; supplying a map does not automatically change those callers to explicit Value intent.

A list index taken with `N#` is a number, numbers compare by exact value, two rules claiming one place for the same meaning are Ambiguous rather than first-wins, and reverse search checks each map entry with its own meaning, so a key-and-value place reports both. Reverse search covers declared links only: it cannot prove that removing an item is safe for scripts or undeclared links.

## Let AI agents work your data

The generic authoring CLI opens data and schema folders, optionally with a map and project-wide --read-only. It does not discover a game's rules callback or per-file ownership policy. Give your game its own small entry point, and construct AgentSession with the **same project factory** as the workspace. That keeps its loader and save policy in force over MCP too.

Save Program.cs in samples/HarborRelay:

~~~csharp
using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;

namespace HarborRelay;

/// <summary>The invented game's window-free agent entry point.</summary>
public static class Program
{
    /// <summary>Describes the fixture or serves its project over MCP stdio.</summary>
    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("--mcp" or "--describe"))
        {
            Console.Error.WriteLine("usage: HarborRelay (--mcp | --describe) <fixture-root>");
            return 2;
        }
        try
        {
            var project = RelayProject.Open(args[1]);
            var map = ReferenceMap.Read(File.ReadAllText(
                Path.Combine(args[1], "reference_map.json")));
            var agent = new AgentSession(project, map);
            if (args[0] == "--mcp")
                new McpServer(agent, "harbor-relay").Run(Console.In, Console.Out);
            else
                Console.Out.WriteLine(agent.Describe().ToJsonString());
            return 0;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException
            or Brutal.Authoring.InvalidDocumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Cannot open Harbor Relay: " + e.Message);
            return 2;
        }
    }
}
~~~

Save HarborRelay.csproj in the same folder. This references only the library's existing projects; there is no game-repository reference or new package.

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NoWarn>$(NoWarn);BRUTALCORE0011</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Brutal.Authoring.Editor.ImGui/Brutal.Authoring.Editor.ImGui.csproj" />
    <ProjectReference Include="../../src/Brutal.Authoring.Agent/Brutal.Authoring.Agent.csproj" />
  </ItemGroup>
</Project>
~~~

From the tools repository root, build and describe the self-authored fixtures without opening a window:

~~~powershell
dotnet build samples\HarborRelay\HarborRelay.csproj --nologo -v:q
dotnet run --no-build --project samples\HarborRelay\HarborRelay.csproj -- --describe samples\HarborRelay\fixture
~~~

Describe lists two files; beacons.json has the exporter's read-only reason. A job can resolve its beacon through the map. No data is saved by Describe.

For a quick stdio check, start the built program with --mcp and send one JSON-RPC message per line. This PowerShell pipeline closes stdin afterward, so the server exits. It builds nothing during the protocol exchange:

~~~powershell
@(
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"relay-guide-check","version":"1"}}}'
  '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"describe","arguments":{}}}'
) | dotnet samples\HarborRelay\bin\Debug\net10.0\HarborRelay.dll --mcp samples\HarborRelay\fixture
~~~

Replies have ids 1, 2 and 3. The notification has no reply. Tools include describe, find, get, problems, refs, repair, set, insert, remove, undo and save. A call's JSON is in result.content's text entry. Inspect both protocol/tool errors and the returned JSON's error/refused fields; isError alone is not a sufficient success check for every operation.

To register the server with an MCP client, add a **local stdio server** named harbor-relay in that client's MCP settings. Use dotnet as the executable and the already-built DLL, --mcp and an absolute fixture root as separate arguments. For example, these client-neutral connection fields describe a checkout at C:/work/brutal-authoring; replace both paths with yours. The enclosing settings format depends on the client.

~~~json
{
  "name": "harbor-relay",
  "transport": "stdio",
  "command": "dotnet",
  "args": [
    "C:/work/brutal-authoring/samples/HarborRelay/bin/Debug/net10.0/HarborRelay.dll",
    "--mcp",
    "C:/work/brutal-authoring/samples/HarborRelay/fixture"
  ]
}
~~~

Do not point that registration at the generic CLI if you need this game's rules. Build first, then launch the DLL directly: build logs and ordinary diagnostics must stay off MCP stdout. This example writes errors to stderr and starts no editor window. It neither edits your client's settings nor grants a remote service filesystem access by itself.

After connecting, list the tools and call describe. Preview a set of relay.json /maximum to the JSON text "6" using preview=true; then apply it and save when satisfied. A maximum of "1" is refused by the game rule. A beacon name can be previewed or changed in memory, but save("beacons.json") is refused with the exporter reason. These value arguments are JSON **text**: a numeric six is the string "6" in the tool arguments, while a JSON string value needs its inner quotes.

MCP keeps the AgentSession alive between calls, so undo and unsaved drafts work across that connection. Separate one-shot CLI processes do not share that memory: pair an edit with --save when using the generic CLI on disposable data. The generic CLI's mcp mode keeps a session too, but still lacks this adapter's game-specific factory. Preview changes neither memory nor disk; edits change memory; Save is explicit. Returned errors and refusal reasons tell the caller what to fix.

Repair opens a syntactically valid broken document for incremental fixes; malformed JSON still cannot open. While repairing, an edit (by a person or an agent) is refused only for problems it adds; a problem counts as already there only for the same item, place and message, so a game rule's problem on another item is never mistaken for an old one.

### A larger adapter, in outline

A bigger game uses the same pattern, in its own adapter outside the public library:

- Its project factory attaches a file-ownership reason and its own loader's validator to DataProject.Open. The ownership list says which source tool owns each file; it is not a blanket rule that every generated file is read-only.
- Its workspace setup supplies the link map and adds one shared action runner through configure. Panel commands and buttons use the same availability check: no second action while one is busy, and no disk-reading rebuild while drafts remain unsaved.
- The action runner starts the game's own scripts in the background with argument-list invocation, captures output and exit status, and bounds the wait. Those are adapter actions; the generic library does not know a game's build scripts.
- The adapter's --mcp branch constructs AgentSession from the same project factory and map, then serves McpServer on stdin/stdout before reaching the UI branch.

Reuse the shared project factory and keep rules and file ownership in the game adapter. Keep UI and agent entry points thin.

### Check the examples

The Harbor Relay C# and JSON blocks form one example. Check schema and loader refusals, read-only draft/save behavior, palette availability, per-file configuration, map labels, Follow/Back, agent preview/apply/undo/save and MCP responses when you adapt it.

Use PluginAcceptanceTests.cs for the memo example and keep the existing play checks alongside your own adapter tests.

Keep these tests with your adapter when you change it. Use invented fixtures and a temporary working copy. Do not point a verification run at a live game's exported files or start a native host just to check the project and agent seams.
