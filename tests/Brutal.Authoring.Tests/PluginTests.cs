using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using LightsPlugin;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Plug-ins (FUT-002, goal 3): the manifest, API versions, and the host's all-or-nothing registration, with the
/// sample Lights plug-in added from outside the library.</summary>
public sealed class PluginTests
{
    private const string Good = """{"id":"acme_tools","name":"Acme tools","version":"2.1.0","api":"0.1","license":"Apache-2.0 OR MIT"}""";

    private static (EditorModel<Scene> Editor, Inspector<Scene> Inspector, Shortcuts Shortcuts, PluginHost<Scene> Host) Host(ApiVersion? api = null)
    {
        var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), Scene.Empty), SceneEditing.Outline);
        var inspector = new Inspector<Scene>(editor).Add(SceneEditing.ObjectType());
        var shortcuts = new Shortcuts();
        return (editor, inspector, shortcuts, new PluginHost<Scene>(editor, inspector, shortcuts, api));
    }

    private sealed class Fake(string manifest, Action<PluginRegistrar<Scene>> register) : IEditorPlugin<Scene>
    {
        public PluginManifest Manifest { get; } = PluginManifest.Read(manifest);
        public void Register(PluginRegistrar<Scene> add) => register(add);
    }

    private sealed record Nudge(string Id) : ICommand<Scene>
    {
        public CommandResult<Scene> Apply(Scene d) => new MoveObject(Id, (d.Find(Id)?.Position ?? Vector3.Zero) + Vector3.UnitX).Apply(d);
    }

    private sealed record Other(string Id) : ICommand<Scene>
    {
        public CommandResult<Scene> Apply(Scene d) => CommandResult<Scene>.Done(d);
    }

    [Fact]
    public void AManifestReadsWithItsDefaults()
    {
        var m = PluginManifest.Read(Good);
        Assert.Equal("acme_tools", m.Id);
        Assert.Equal(new ApiVersion(2, 1, 0), m.Version);
        Assert.Equal(new ApiVersion(0, 1, 0), m.Api);
        Assert.Empty(m.Permissions);
        Assert.Equal("", m.Description);
        Assert.True(m.Owns("acme_tools.paint"));
        Assert.False(m.Owns("acme_tools."));
        Assert.False(m.Owns("acme_toolsx.paint"));
    }

    [Theory]
    [InlineData("\"id\":\"acme_tools\"", "\"id\":\"Acme\"", "$.id")]
    [InlineData("\"version\":\"2.1.0\"", "\"version\":\"2.01\"", "$.version")]
    [InlineData("\"api\":\"0.1\"", "\"api\":\"latest\"", "$.api")]
    [InlineData("\"license\":\"Apache-2.0 OR MIT\"", "\"license\":\"whatever you like\"", "$.license")]
    [InlineData("\"license\":\"Apache-2.0 OR MIT\"", "\"license\":\"(MIT OR Apache-2.0\"", "$.license")]   // found by Codex in A019
    [InlineData("\"license\":\"Apache-2.0 OR MIT\"", "\"license\":\"MIT) OR (Apache-2.0\"", "$.license")]
    [InlineData("\"name\":\"Acme tools\"", "\"name\":\" \"", "$.name")]
    [InlineData("\"name\":\"Acme tools\"", "\"name\":\"Acme tools\",\"homepage\":\"x\"", "$.homepage")]
    [InlineData("\"name\":\"Acme tools\"", "\"name\":\"Acme tools\",\"permissions\":[{\"name\":\"root\",\"reason\":\"why not\"}]", "$.permissions[0].name")]
    [InlineData("\"name\":\"Acme tools\"", "\"name\":\"Acme tools\",\"permissions\":[{\"name\":\"network\",\"reason\":\"\"}]", "$.permissions[0].reason")]
    public void ABadManifestIsRefusedAtItsPath(string from, string to, string path)
    {
        Assert.Contains(from, Good);
        Assert.Equal(path, Assert.Throws<DocumentFormatException>(() => PluginManifest.Read(Good.Replace(from, to))).Path);
    }

    [Fact]
    public void PermissionsCarryTheirReasons()
    {
        var m = PluginManifest.Read(Good.Replace("}", ",\"permissions\":[{\"name\":\"network\",\"reason\":\"Fetches textures from your asset server.\"}]}"));
        Assert.Equal([new PluginPermission("network", "Fetches textures from your asset server.")], m.Permissions);
    }

    [Theory]
    [InlineData("0.1.0", "0.1", true)]
    [InlineData("0.1.3", "0.1.2", true)]
    [InlineData("0.1.0", "0.1.2", false)]
    [InlineData("0.2.0", "0.1", false)]   // before 1.0 a minor version may break
    [InlineData("1.4.0", "1.2", true)]
    [InlineData("1.2.0", "1.3", false)]
    [InlineData("2.0.0", "1.9", false)]
    public void VersionsFollowSemverWithTheZeroMajorRule(string host, string required, bool ok) =>
        Assert.Equal(ok, ApiVersion.Parse(host).Satisfies(ApiVersion.Parse(required)));

    [Fact]
    public void TheLightsPluginAddsACommandATypeAWorkspaceAndAShortcut()
    {
        var (editor, inspector, shortcuts, host) = Host();
        Assert.True(host.Add(new Lights()), string.Join("; ", host.Plugins.SelectMany(p => p.Issues)));
        Assert.True(editor.Session.Execute(new AddLight("lamp", new Vector3(0, 3, 0), 2)));
        var light = Assert.Single(inspector.Types, t => t.Id == "lights.light");
        var rows = light.Rows(editor.Session.Current, ["lamp"]);
        Assert.Equal(2.0, rows.Single(r => r.Id == "intensity").Value);
        Assert.Equal(10.0, rows.Single(r => r.Id == "range").Value);   // its default
        Assert.Empty(light.Rows(editor.Session.Current, ["missing"]));
        editor.Use("lights.lighting");
        Assert.Equal("lights.add", editor.Search("").First().Command.Id);
        Assert.Equal(Chord.Parse("Alt+L"), shortcuts.For("lights.add"));
        // Journals made with the plug-in replay with it.
        var replay = DocumentSession<Scene>.Replay(Scene.Empty, editor.Session.Journal, new SceneFormat(), new SceneValidator(), editor.Session.Catalog);
        Assert.NotNull(replay.Current.Find("lamp"));
    }

    [Fact]
    public void ThePluginsInspectorEditsGoThroughTheSession()
    {
        var (editor, inspector, _, host) = Host();
        Assert.True(host.Add(new Lights()));
        Assert.True(editor.Session.Execute(new AddLight("lamp", Vector3.Zero)));
        var edits = inspector.Types.Single(t => t.Id == "lights.light").EditCommands(editor.Session.Current, ["lamp"], "range", 25.0, reset: false);
        Assert.True(editor.Session.ExecuteBatch(edits!));
        Assert.Equal(25.0, editor.Session.Current.Find("lamp")!.Properties["range"].GetDouble());
        Assert.True(editor.Session.Undo());
        Assert.False(editor.Session.Current.Find("lamp")!.Properties.ContainsKey("range"));
    }

    [Fact]
    public void ThePluginsManifestFileIsTheOneItCarries()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var file = Path.Combine(d.FullName, "samples", "LightsPlugin", "plugin.json");
            if (!File.Exists(file)) continue;
            var onDisk = PluginManifest.Read(File.ReadAllText(file));
            var carried = new Lights().Manifest;
            Assert.Equal((onDisk.Id, onDisk.Version, onDisk.Api, onDisk.License), (carried.Id, carried.Version, carried.Api, carried.License));
            return;
        }
        Assert.Fail("can't find samples/LightsPlugin/plugin.json");
    }

    [Fact]
    public void APluginForANewerApiIsRefusedClearlyAndAddsNothing()
    {
        var (editor, _, _, host) = Host(new ApiVersion(0, 1, 0));
        var p = new Fake(Good.Replace("\"api\":\"0.1\"", "\"api\":\"0.1.4\""), add => add.Command<Nudge>("acme_tools.nudge", 1, "Nudge"));
        Assert.False(host.Add(p));
        var why = Assert.Single(host.Plugins.Single().Issues).Message;
        Assert.Equal("'Acme tools' (acme_tools 2.1.0) needs authoring API 0.1.4, newer than this editor's 0.1.0; update the editor or use an older Acme tools", why);
        Assert.DoesNotContain(editor.Session.Catalog.Commands, c => c.Id == "acme_tools.nudge");
        Assert.False(host.Plugins.Single().Enabled);
    }

    [Fact]
    public void APluginForARetiredApiIsRefusedClearly()
    {
        var (_, _, _, host) = Host(new ApiVersion(1, 0, 0));
        Assert.False(host.Add(new Fake(Good, _ => { })));
        Assert.Contains("no longer supports", host.Plugins.Single().Issues[0].Message);
    }

    [Fact]
    public void APluginThatThrowsIsDisabledAndTheEditorCarriesOn()
    {
        var (editor, _, _, host) = Host();
        var before = editor.Session.Catalog.Commands.Count;
        Assert.False(host.Add(new Fake(Good, add =>
        {
            add.Command<Nudge>("acme_tools.nudge", 1, "Nudge");
            throw new InvalidOperationException("the config file is missing");
        })));
        Assert.Contains("failed while registering: the config file is missing", host.Plugins[0].Issues[0].Message);
        Assert.Equal(before, editor.Session.Catalog.Commands.Count);
        Assert.True(host.Add(new Lights()));   // the next one is fine
    }

    [Fact]
    public void RegistrationIsAllOrNothing()
    {
        var (editor, inspector, shortcuts, host) = Host();
        var p = new Fake(Good, add => add
            .Command<Nudge>("acme_tools.nudge", 1, "Nudge")                    // fine
            .Command<Other>("scene.other", 1, "Other")                         // not its name
            .ObjectType(SceneEditing.ObjectType())                             // scene.object: not its name, and taken
            .Workspace(new Workspace("acme_tools.ws", "Acme", ["acme_tools.nudge", "acme_tools.missing"]))
            .Shortcut("scene.move", "Alt+M")                                   // not its command
            .Shortcut("acme_tools.nudge", "Hyper+N"));                         // not a chord
        Assert.False(host.Add(p));
        var issues = host.Plugins.Single().Issues.Select(i => i.Message).ToList();
        Assert.Contains("command 'scene.other' must be named 'acme_tools.<name>'", issues);
        Assert.Contains("object type 'scene.object' must be named 'acme_tools.<name>'", issues);
        Assert.Contains("object type 'scene.object' is already registered", issues);
        Assert.Contains("workspace 'acme_tools.ws' leads with 'acme_tools.missing', which isn't a command in the editor", issues);
        Assert.Contains(issues, i => i.StartsWith("a shortcut may only go to one of 'Acme tools''s own commands"));
        Assert.Contains(issues, i => i.StartsWith("shortcut for 'acme_tools.nudge'"));
        Assert.DoesNotContain(editor.Session.Catalog.Commands, c => c.Id == "acme_tools.nudge");   // even the good part waits
        Assert.Empty(editor.Workspaces);
        Assert.Single(inspector.Types);
        Assert.Null(shortcuts.For("acme_tools.nudge"));
    }

    [Fact]
    public void TheSameIdOrCommandTypeCanOnlyBeAddedOnce()
    {
        var (_, _, _, host) = Host();
        Assert.True(host.Add(new Lights()));
        Assert.False(host.Add(new Lights()));
        Assert.Contains("already enabled", host.Plugins[1].Issues[0].Message);
        Assert.False(host.Add(new Fake(Good, add => add.Command<MoveObject>("acme_tools.move", 1, "Move"))));
        Assert.Contains("MoveObject is already in the editor as 'scene.move'", host.Plugins[2].Issues[0].Message);
    }
}

/// <summary>Panels added by plug-ins, drawn headless in the editor window.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class PluginPanelTests
{
    private static (EditorModel<Scene> Editor, Brutal.Authoring.Editor.ImGui.EditorWindow<Scene> Window, PluginHost<Scene> Host) Setup()
    {
        var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), Scene.Empty), SceneEditing.Outline);
        var inspector = new Inspector<Scene>(editor).Add(SceneEditing.ObjectType());
        var shortcuts = new Shortcuts();
        return (editor, new Brutal.Authoring.Editor.ImGui.EditorWindow<Scene>(editor, inspector, shortcuts, SceneEditing.CommandFor),
            new PluginHost<Scene>(editor, inspector, shortcuts));
    }

    [Fact]
    public void APluginPanelDrawsAndCanBeShownAndHiddenFromThePalette()
    {
        var (editor, window, host) = Setup();
        Assert.True(host.Add(new Lights()));
        Assert.True(editor.IsOpen("lights.overview"));
        Assert.True(editor.Session.Execute(new AddLight("lamp", Vector3.Zero)));
        EditorWindowTests.Frames(window.Draw);
        var toggle = Assert.Single(window.PaletteEntries("lights"), e => e.Id == "editor.panel.lights.overview");
        Assert.Equal("Hide Lights", toggle.Label);
        Assert.True(window.Run(toggle.Id));
        Assert.False(editor.IsOpen("lights.overview"));
        Assert.Equal("Show Lights", window.PaletteEntries("lights").Single(e => e.Id == toggle.Id).Label);
        EditorWindowTests.Frames(window.Draw);
        Assert.Single(editor.Session.Journal);   // showing and hiding panels never edits
    }

    [Fact]
    public void APanelThatThrowsIsClosedAndReportedAndTheWindowCarriesOn()
    {
        var (editor, window, _) = Setup();
        editor.Add(new Panel("test.broken", "Broken", () => throw new InvalidOperationException("no data")));
        EditorWindowTests.Frames(window.Draw);
        Assert.False(editor.IsOpen("test.broken"));
        Assert.Equal("The Broken panel failed and was closed: no data", window.Status);
    }

    [Fact]
    public void APluginsPanelMustBeItsOwnAndNew()
    {
        var (editor, _, host) = Setup();
        editor.Add(new Panel("lights.overview", "Taken", () => { }));
        Assert.False(host.Add(new Lights()));
        Assert.Contains("panel 'lights.overview' is already added", host.Plugins[0].Issues.Select(i => i.Message));
        Assert.Equal("Taken", Assert.Single(editor.Panels, p => p.Id != BedAbout.Panel.Id && p.Id != KeysPanel.Id && p.Id != GettingStartedPanel.Id).Title);
        Assert.Throws<ArgumentException>(() => editor.Add(new Panel("lights.overview", "Again", () => { })));
        Assert.False(editor.SetOpen("nope", true));
    }
}
