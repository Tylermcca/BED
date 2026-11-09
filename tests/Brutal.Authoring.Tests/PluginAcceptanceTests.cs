using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests
{
// INVENTED independent fixtures; BORROWED promises from public XML/doc comments.
// PluginTests.cs and HeadlessHostTests.cs were not read.
[Collection(ImGuiCollection.Name)]
public sealed class PluginAcceptanceTests
{
    private readonly ITestOutputHelper output;
    public PluginAcceptanceTests(ITestOutputHelper output) => this.output = output;
    private const string Good = """
        {"id":"demo","name":"Demonstration","version":"1.2.3","api":"0.1","license":"MIT","permissions":[],"description":"A self-authored test extension."}
        """;
    private static PluginManifest Manifest() => PluginManifest.Read(Good);
    private static JsonObject ManifestNode() => JsonNode.Parse(Good)!.AsObject();
    private static SceneObject Item(string id = "item") => SceneObject.At(id, null, Vector3.Zero);
    private static readonly SceneFormat Format = new();
    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value);
    private sealed record ProbeA(int Value = 1) : ICommand<Scene>
    {
        public CommandResult<Scene> Apply(Scene d) => new SetSceneProperty("item", "value", Element(Value)).Apply(d);
    }
    private sealed record ProbeB(int Value = 2) : ICommand<Scene>
    {
        public CommandResult<Scene> Apply(Scene d) => new SetSceneProperty("item", "value", Element(Value)).Apply(d);
    }
    private sealed class Plug(PluginManifest manifest, Action<PluginRegistrar<Scene>> register) : IEditorPlugin<Scene>
    {
        public PluginManifest Manifest => manifest;
        public void Register(PluginRegistrar<Scene> r) => register(r);
    }
    private sealed record Rig(EditorModel<Scene> Editor, Inspector<Scene> Inspector, Shortcuts Keys, PluginHost<Scene> Host);
    private static Rig Open(ApiVersion? api = null)
    {
        var s = new DocumentSession<Scene>(Format, new SceneValidator(), SceneKit.Catalog(), new Scene([Item()]));
        var e = new EditorModel<Scene>(s, SceneEditing.Outline);
        var i = new Inspector<Scene>(e).Add(SceneEditing.ObjectType());
        var k = new Shortcuts();
        return new(e, i, k, new PluginHost<Scene>(e, i, k, api));
    }
    private static ObjectType<Scene, SceneObject> Type(string id) => new(id, "Probe", (d, key) => d.Find(key), []);
    private static void StageGood(PluginRegistrar<Scene> r) => r
        .Command<ProbeA>("demo.good", 1, "Good")
        .ObjectType(Type("demo.object"))
        .Workspace(new Workspace("demo.work", "Work", ["demo.good"]))
        .Panel(new Panel("demo.panel", "Probe panel", () => { }))
        .Maker("demo.good", _ => new ProbeA())
        .Shortcut("demo.good", "Alt+J");
    private static string Snapshot(Rig r) => string.Join("|",
        string.Join(",", r.Editor.Session.Catalog.Commands.Select(c => $"{c.Id}:{c.Type}:{c.Label}")),
        string.Join(",", r.Inspector.Types.Select(t => t.Id)),
        string.Join(",", r.Editor.Workspaces.Select(w => w.Id)),
        string.Join(",", r.Editor.Panels.Select(p => $"{p.Id}:{r.Editor.IsOpen(p.Id)}")),
        string.Join(",", r.Keys.InEffect().OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}")),
        r.Editor.HasMaker("demo.good"), r.Editor.HasMaker("demo.missing"),
        Format.Write(r.Editor.Session.Current), r.Editor.Session.Revision, JournalFile.Write(r.Editor.Session.Journal));
    private static void Refuses(Action<PluginRegistrar<Scene>> bad, Action<Rig>? prepare = null)
    {
        var r = Open(); prepare?.Invoke(r);
        var before = Snapshot(r);
        Assert.False(r.Host.Add(new Plug(Manifest(), add => { StageGood(add); bad(add); })));
        var status = Assert.Single(r.Host.Plugins);
        Assert.False(status.Enabled); Assert.NotEmpty(status.Issues);
        Assert.Equal(before, Snapshot(r));
    }

    // Plugins.cs core:5-24,76-77.
    [Theory] [InlineData("1", "1.0.0")] [InlineData("1.2", "1.2.0")] [InlineData("1.2.3", "1.2.3")]
    public void VersionMissingPartsAreZero(string text, string expected) => Assert.Equal(expected, ApiVersion.Parse(text).ToString());
    [Theory] [InlineData("")] [InlineData("a")] [InlineData("1..2")] [InlineData("-1.2")] [InlineData("1.2.3.4")] [InlineData("1.2.3-alpha")] [InlineData("1.2.3+build")]
    public void BadVersionHasAnExplanation(string text) => Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<FormatException>(() => ApiVersion.Parse(text)).Message));
    [Theory]
    [InlineData("0.1.5","0.1.0",true)] [InlineData("0.1.0","0.1.1",false)] [InlineData("0.2.0","0.1.0",false)]
    [InlineData("1.2.0","1.1.0",true)] [InlineData("1.1.9","1.2.0",false)] [InlineData("2.0.0","1.0.0",false)]
    public void HostVersionCompatibilityMatchesMajorAndZeroMajorMinor(string host, string required, bool expected) =>
        Assert.Equal(expected, ApiVersion.Parse(host).Satisfies(ApiVersion.Parse(required)));
    [Fact]
    public void VersionComparisonIsNumericAcrossAllParts()
    {
        Assert.True(new ApiVersion(1, 10, 0) > new ApiVersion(1, 2, 99));
        Assert.True(new ApiVersion(2, 0, 0) >= new ApiVersion(1, 99, 99));
        Assert.True(new ApiVersion(1, 2, 3) < new ApiVersion(1, 2, 4));
    }
    // Claude, on accept: "license" left this list when the user made licences optional (2026-10-31); LicenceTests covers it.
    [Theory] [InlineData("id")] [InlineData("name")] [InlineData("version")] [InlineData("api")]
    public void MissingRequiredManifestFieldNamesItsPath(string field)
    {
        var n = ManifestNode(); n.Remove(field);
        Assert.Equal("$." + field, Assert.Throws<DocumentFormatException>(() => PluginManifest.Read(n.ToJsonString())).Path);
    }
    [Theory]
    [InlineData("id","42")] [InlineData("name","false")] [InlineData("version","1")] [InlineData("api","[]")]
    [InlineData("license","true")] [InlineData("description","27")] [InlineData("permissions","{}")]
    public void WrongManifestFieldTypeNamesItsPath(string field, string value)
    {
        var n = ManifestNode(); n[field] = JsonNode.Parse(value);
        Assert.Equal("$." + field, Assert.Throws<DocumentFormatException>(() => PluginManifest.Read(n.ToJsonString())).Path);
    }
    [Fact]
    public void UnknownManifestFieldIsNotGuessed()
    {
        var n = ManifestNode(); n["licence"] = "MIT";
        Assert.Equal("$.licence", Assert.Throws<DocumentFormatException>(() => PluginManifest.Read(n.ToJsonString())).Path);
    }
    [Theory] [InlineData("Demo")] [InlineData("two words")] [InlineData("")] [InlineData("demo-")]
    public void BadManifestIdIsRefused(string id)
    {
        var n = ManifestNode(); n["id"] = id;
        Assert.Equal("$.id", Assert.Throws<DocumentFormatException>(() => PluginManifest.Read(n.ToJsonString())).Path);
    }
    [Fact]
    public void ManifestFieldsAndOptionalDefaultsArePreserved()
    {
        var m = Manifest();
        Assert.Equal("demo", m.Id); Assert.Equal("Demonstration", m.Name);
        Assert.Equal(new ApiVersion(1, 2, 3), m.Version); Assert.Equal(new ApiVersion(0, 1, 0), m.Api);
        Assert.Equal("MIT", m.License); Assert.Empty(m.Permissions); Assert.Contains("self-authored", m.Description);
        var n = ManifestNode(); n.Remove("description"); n.Remove("permissions");
        var minimal = PluginManifest.Read(n.ToJsonString());
        Assert.Empty(minimal.Permissions); Assert.Equal("", minimal.Description);
    }
    [Theory] [InlineData("MIT")] [InlineData("Apache-2.0 OR MIT")] [InlineData("(MIT OR Apache-2.0) AND BSD-3-Clause")]
    public void SpdxExpressionsHaveDocumentedShape(string license)
    {
        var n = ManifestNode(); n["license"] = license;
        Assert.Equal(license, PluginManifest.Read(n.ToJsonString()).License);
    }
    [Theory] [InlineData("")] [InlineData("MIT OR")] [InlineData("OR MIT")] [InlineData("MIT AND OR Apache-2.0")] [InlineData("(MIT OR Apache-2.0")]
    public void MalformedSpdxExpressionNamesLicense(string license)
    {
        var n = ManifestNode(); n["license"] = license;
        Assert.Equal("$.license", Assert.Throws<DocumentFormatException>(() => PluginManifest.Read(n.ToJsonString())).Path);
    }
    [Fact]
    public void EveryKnownPermissionCanStateAReason()
    {
        var n = ManifestNode();
        n["permissions"] = new JsonArray(PluginManifest.KnownPermissions.Select(p => (JsonNode)new JsonObject { ["name"] = p, ["reason"] = "Read the user's approved input." }).ToArray());
        var permissions = PluginManifest.Read(n.ToJsonString()).Permissions;
        Assert.Equal(PluginManifest.KnownPermissions, permissions.Select(p => p.Name));
        Assert.All(permissions, p => Assert.Equal("Read the user's approved input.", p.Reason));
    }
    [Theory]
    [InlineData("""[{"name":"unknown","reason":"A reason."}]""", "$.permissions[0].name")]
    [InlineData("""[{"name":"network"}]""", "$.permissions[0].reason")]
    [InlineData("""[{"name":"network","reason":""}]""", "$.permissions[0].reason")]
    [InlineData("""[{"name":"network","reason":42}]""", "$.permissions[0].reason")]
    [InlineData("""[{"name":"network","reason":"A reason.","extra":true}]""", "$.permissions[0].extra")]
    public void BadPermissionNamesItsOwnField(string text, string path)
    {
        var n = ManifestNode(); n["permissions"] = JsonNode.Parse(text);
        Assert.Equal(path, Assert.Throws<DocumentFormatException>(() => PluginManifest.Read(n.ToJsonString())).Path);
    }
    [Fact]
    public void OwnershipRequiresAWholeDotSeparatedPrefix()
    {
        var m = Manifest();
        Assert.True(m.Owns("demo.command"));
        Assert.False(m.Owns("demonstration.command")); Assert.False(m.Owns("demo")); Assert.False(m.Owns("demo."));
    }
    [Theory] [InlineData("0.2.0","0.1.0")] [InlineData("0.1.0","0.2.0")] [InlineData("1.0.0","2.0.0")] [InlineData("2.0.0","1.0.0")]
    public void IncompatibleApiIsExplainedBeforeRegistration(string required, string host)
    {
        var r = Open(ApiVersion.Parse(host)); var called = false;
        var m = Manifest() with { Api = ApiVersion.Parse(required) };
        var before = Snapshot(r);
        Assert.False(r.Host.Add(new Plug(m, _ => called = true)));
        Assert.False(called); Assert.Equal(before, Snapshot(r));
        var issue = Assert.Single(Assert.Single(r.Host.Plugins).Issues);
        Assert.Equal(Severity.Error, issue.Severity);
        Assert.Contains(required, issue.Message); Assert.Contains(host, issue.Message);
        output.WriteLine("Exact version refusal: " + issue.Message);
    }
    [Fact]
    public void CompatibleApiRegistersEveryKindAndRetainsStatusOrder()
    {
        var r = Open(new(0, 1, 3));
        Assert.True(r.Host.Add(new Plug(Manifest(), StageGood)));
        Assert.True(Assert.Single(r.Host.Plugins).Enabled);
        Assert.Empty(r.Host.Plugins[0].Issues);
        Assert.Contains(r.Editor.Session.Catalog.Commands, c => c.Id == "demo.good");
        Assert.Contains(r.Inspector.Types, t => t.Id == "demo.object");
        Assert.Contains(r.Editor.Workspaces, w => w.Id == "demo.work");
        Assert.Contains(r.Editor.Panels, p => p.Id == "demo.panel");
        Assert.True(r.Editor.HasMaker("demo.good")); Assert.IsType<ProbeA>(r.Editor.Make("demo.good"));
        Assert.Equal(Chord.Parse("Alt+J"), r.Keys.For("demo.good"));
        Assert.False(r.Host.Add(new Plug(Manifest(), StageGood)));
        Assert.Equal(new[] { true, false }, r.Host.Plugins.Select(p => p.Enabled));
    }

    // PluginHost docs: staging, all kinds of additions, owned/well-formed/unused ids.
    [Fact] public void ForeignCommandRefusesEverything() => Refuses(r => r.Command<ProbeB>("other.cmd", 1, "Bad"));
    [Fact] public void MalformedCommandRefusesEverything() => Refuses(r => r.Command<ProbeB>("demo.bad-id", 1, "Bad"));
    [Fact] public void DuplicateStagedCommandIdRefusesEverything() => Refuses(r => r.Command<ProbeB>("demo.good", 1, "Bad"));
    [Fact] public void DuplicateStagedCommandTypeRefusesEverything() => Refuses(r => r.Command<ProbeA>("demo.alias", 1, "Bad"));
    [Fact] public void ExistingCommandTypeRefusesEverything() => Refuses(r => r.Command<MoveObject>("demo.move", 1, "Bad"));
    [Fact] public void ExistingOwnedCommandIdRefusesEverything() => Refuses(r => r.Command<ProbeB>("demo.taken", 1, "Bad"),
        x => x.Editor.Session.Catalog.Register<ProbeB>("demo.taken", 1, "Existing"));
    [Fact] public void ForeignObjectTypeRefusesEverything() => Refuses(r => r.ObjectType(Type("other.object")));
    [Fact] public void MalformedObjectTypeRefusesEverything() => Refuses(r => r.ObjectType(Type("demo.")));
    [Fact] public void DuplicateStagedObjectTypeRefusesEverything() => Refuses(r => r.ObjectType(Type("demo.object")));
    [Fact] public void ExistingObjectTypeRefusesEverything() => Refuses(r => r.ObjectType(Type("demo.taken")),
        x => x.Inspector.Add(Type("demo.taken")));
    [Fact] public void ForeignWorkspaceRefusesEverything() => Refuses(r => r.Workspace(new("other.work", "Bad", [])));
    [Fact] public void MalformedWorkspaceRefusesEverything() => Refuses(r => r.Workspace(new("demo.", "Bad", [])));
    [Fact] public void DuplicateStagedWorkspaceRefusesEverything() => Refuses(r => r.Workspace(new("demo.work", "Bad", [])));
    [Fact] public void ExistingWorkspaceRefusesEverything() => Refuses(r => r.Workspace(new("demo.taken", "Bad", [])),
        x => x.Editor.Add(new Workspace("demo.taken", "Existing", [])));
    [Fact] public void UnknownWorkspaceCommandRefusesEverything() => Refuses(r => r.Workspace(new("demo.unknown", "Bad", ["demo.absent"])));
    [Fact] public void ForeignPanelRefusesEverything() => Refuses(r => r.Panel(new("other.panel", "Bad", () => { })));
    [Fact] public void MalformedPanelRefusesEverything() => Refuses(r => r.Panel(new("demo.", "Bad", () => { })));
    [Fact] public void DuplicateStagedPanelRefusesEverything() => Refuses(r => r.Panel(new("demo.panel", "Bad", () => { })));
    [Fact] public void ExistingPanelRefusesEverything() => Refuses(r => r.Panel(new("demo.taken", "Bad", () => { })),
        x => x.Editor.Add(new Panel("demo.taken", "Existing", () => { })));
    [Fact] public void ForeignMakerRefusesEverything() => Refuses(r => r.Maker("scene.move", _ => new MoveObject("item", Vector3.Zero)));
    [Fact] public void UnknownMakerCommandRefusesEverything() => Refuses(r => r.Maker("demo.absent", _ => new ProbeA()));
    [Fact] public void ForeignShortcutRefusesEverything() => Refuses(r => r.Shortcut("scene.move", "Alt+K"));
    [Fact] public void UnknownShortcutCommandRefusesEverything() => Refuses(r => r.Shortcut("demo.absent", "Alt+K"));
    [Fact] public void BadShortcutChordRefusesEverything() => Refuses(r => r.Shortcut("demo.good", "Ctrl+NotAKey"));
    [Fact] public void ThrowingRegisterRefusesEveryStagedAddition() => Refuses(_ => throw new InvalidOperationException("invented registration failure"));

    [Fact]
    public void UserShortcutChoiceWinsOverPluginDefault()
    {
        var r = Open(); r.Keys.Set("demo.good", "Ctrl+F9");
        Assert.True(r.Host.Add(new Plug(Manifest(), StageGood)));
        Assert.Equal(Chord.Parse("Ctrl+F9"), r.Keys.For("demo.good"));
    }
    [Fact]
    public void PanelOpenAtStartAndPaletteToggleAreViewState()
    {
        var r = Open(); Assert.True(r.Host.Add(new Plug(Manifest(), StageGood)));
        var w = new EditorWindow<Scene>(r.Editor, r.Inspector, r.Keys);
        Assert.True(r.Editor.IsOpen("demo.panel"));
        Assert.True(w.Run(EditorWindow<Scene>.PanelAction + "demo.panel"));
        Assert.False(r.Editor.IsOpen("demo.panel"));
        Assert.True(w.Run(EditorWindow<Scene>.PanelAction + "demo.panel"));
        Assert.True(r.Editor.IsOpen("demo.panel"));
        Assert.Empty(r.Editor.Session.Journal); Assert.False(r.Editor.Session.Dirty);
    }
    [Fact]
    public void ClosedAtStartPanelCanBeShownAndUnknownPanelCannotToggle()
    {
        var r = Open(); r.Editor.Add(new Panel("demo.hidden", "Hidden", () => { }, OpenAtStart: false));
        Assert.False(r.Editor.IsOpen("demo.hidden"));
        Assert.True(r.Editor.SetOpen("demo.hidden", true)); Assert.True(r.Editor.IsOpen("demo.hidden"));
        Assert.False(r.Editor.SetOpen("demo.hidden", true)); Assert.False(r.Editor.SetOpen("demo.unknown", true));
    }
    [Fact]
    public void MakerIsUsedByPaletteRunAndJournalsTheCommand()
    {
        var r = Open(); Assert.True(r.Host.Add(new Plug(Manifest(), StageGood)));
        var w = new EditorWindow<Scene>(r.Editor, r.Inspector, r.Keys);
        Assert.True(w.Run("demo.good"));
        Assert.Equal(1, r.Editor.Session.Current.Find("item")!.Properties["value"].GetInt32());
        Assert.Equal("demo.good", Assert.Single(r.Editor.Session.Journal).Command);
        Assert.True(r.Editor.Session.Undo()); Assert.Empty(r.Editor.Session.Current.Find("item")!.Properties);
    }
    [Fact]
    public void NullMakerLeavesCommandUnavailableWithAReason()
    {
        var r = Open();
        Assert.True(r.Host.Add(new Plug(Manifest(), add => add.Command<ProbeA>("demo.empty", 1, "Unavailable").Maker("demo.empty", _ => null))));
        Assert.True(r.Editor.HasMaker("demo.empty")); Assert.Null(r.Editor.Make("demo.empty")); Assert.False(r.Editor.HasMaker("absent"));
        var w = new EditorWindow<Scene>(r.Editor, r.Inspector, r.Keys);
        var choice = Assert.Single(w.PaletteEntries("demo.empty"));
        Assert.False(string.IsNullOrWhiteSpace(choice.Unavailable));
        Assert.False(w.Run("demo.empty")); Assert.Empty(r.Editor.Session.Journal);
    }

    [Fact]
    public void ThrowingPanelClosesItselfAndReportsTheError()
    {
        var r = Open(); r.Editor.Add(new Panel("demo.broken", "Broken probe", () => throw new InvalidOperationException("panel failure sentinel")));
        var w = new EditorWindow<Scene>(r.Editor, r.Inspector, r.Keys);
        using var h = new HeadlessHost(1280, 900);
        h.Frames(w.Draw);
        Assert.False(r.Editor.IsOpen("demo.broken"));
        Assert.Contains("panel failure sentinel", w.Status);
        Assert.Empty(r.Editor.Session.Journal);
    }
    [Fact]
    public void PanelRequestedSizeIsUsedOnFirstShow()
    {
        var r = Open(); float2 measured = default; var draws = 0;
        r.Editor.Add(new Panel("demo.sized", "Sized probe", () => { if (draws++ == 0) measured = ImGui.GetWindowSize(); }, Width: 333, Height: 222));
        var w = new EditorWindow<Scene>(r.Editor, r.Inspector, r.Keys);
        using var h = new HeadlessHost(1600, 1000);
        h.Frames(w.Draw);
        Assert.True(draws > 0); Assert.InRange(measured.X, 332, 334); Assert.InRange(measured.Y, 221, 223);
    }
    [Fact]
    public void ClosedPanelIsNotDrawnUntilShown()
    {
        var r = Open(); var draws = 0;
        r.Editor.Add(new Panel("demo.hidden", "Hidden probe", () => draws++, OpenAtStart: false));
        var w = new EditorWindow<Scene>(r.Editor, r.Inspector, r.Keys);
        using var h = new HeadlessHost();
        h.Frames(w.Draw); Assert.Equal(0, draws);
        Assert.True(w.Run(EditorWindow<Scene>.PanelAction + "demo.hidden"));
        h.Frames(w.Draw); Assert.True(draws > 0);
    }
    private sealed class Widgets
    {
        public int Frames, Clicks, Enters;
        public bool Checked;
        public readonly ImInputString Text = new(128, ""u8);
        public float2 ButtonPoint, CheckPoint, TextPoint;
        private static float2 Centre() { var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax(); return new((a.X + b.X) / 2, (a.Y + b.Y) / 2); }
        public void Draw()
        {
            Frames++;
            ImGui.SetNextWindowPos(new float2(20, 20), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(360, 220), ImGuiCond.Always);
            if (ImGui.Begin("Acceptance widgets"u8))
            {
                if (ImGui.Button("Count"u8)) Clicks++;
                ButtonPoint = Centre();
                ImGui.Checkbox("Enabled"u8, ref Checked); CheckPoint = Centre();
                if (ImGui.InputText("Name"u8, Text, ImGuiInputTextFlags.EnterReturnsTrue)) Enters++;
                Text.EvaluateLength(); // Refresh the managed view of the editable native buffer.
                var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax();
                TextPoint = new(a.X + 20, (a.Y + b.Y) / 2);
            }
            ImGui.End();
        }
    }
    [Fact]
    public void FrameAndFramesRunRealWidgetCallbacks()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets();
        h.Frame(ui.Draw); h.Frames(ui.Draw, 4);
        Assert.Equal(5, ui.Frames); Assert.Equal(500, h.Width); Assert.Equal(320, h.Height);
        Assert.Equal(500 * 320, h.Pixels.Length); Assert.True(h.Covered() > 0);
    }
    [Fact]
    public void ClickReachesButtonOnlyInsideItsRectangle()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets(); h.Frames(ui.Draw);
        h.Click(ui.Draw, 480, 300); Assert.Equal(0, ui.Clicks);
        h.Click(ui.Draw, ui.ButtonPoint.X, ui.ButtonPoint.Y); Assert.Equal(1, ui.Clicks);
        h.Click(ui.Draw, ui.ButtonPoint.X, ui.ButtonPoint.Y); Assert.Equal(2, ui.Clicks);
    }
    [Fact]
    public void ClickChangesARealCheckbox()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets(); h.Frames(ui.Draw);
        h.Click(ui.Draw, ui.CheckPoint.X, ui.CheckPoint.Y); Assert.True(ui.Checked);
        h.Click(ui.Draw, ui.CheckPoint.X, ui.CheckPoint.Y); Assert.False(ui.Checked);
    }
    [Fact]
    public void TypeAndPressReachFocusedTextField()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets(); h.Frames(ui.Draw);
        h.Click(ui.Draw, ui.TextPoint.X, ui.TextPoint.Y);
        h.Type(ui.Draw, "abc"); Assert.Equal("abc", ui.Text.ToString());
        h.Press(ui.Draw, ImGuiKey.Backspace); Assert.Equal("ab", ui.Text.ToString());
        h.Press(ui.Draw, ImGuiKey.Enter); Assert.Equal(1, ui.Enters);
    }
    [Fact]
    public void PressModifiersReachTextSelectionAndTyping()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets(); h.Frames(ui.Draw);
        h.Click(ui.Draw, ui.TextPoint.X, ui.TextPoint.Y); h.Type(ui.Draw, "before");
        h.Press(ui.Draw, ImGuiKey.A, ctrl: true); h.Type(ui.Draw, "after");
        Assert.Equal("after", ui.Text.ToString());
    }
    [Fact]
    public void DrawingFalseKeepsButtonCheckboxAndTextLogicWorking()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets(); h.Frames(ui.Draw);
        var pixels = h.Pixels.ToArray(); h.Drawing = false;
        h.Click(ui.Draw, ui.ButtonPoint.X, ui.ButtonPoint.Y);
        h.Click(ui.Draw, ui.CheckPoint.X, ui.CheckPoint.Y);
        h.Click(ui.Draw, ui.TextPoint.X, ui.TextPoint.Y); h.Type(ui.Draw, "logic");
        Assert.Equal(1, ui.Clicks); Assert.True(ui.Checked); Assert.Equal("logic", ui.Text.ToString());
        Assert.Equal(pixels, h.Pixels);
        h.Drawing = true; h.Frame(ui.Draw); Assert.True(h.Covered() > 0);
    }
    [Fact]
    public void CoveredMatchesIndependentPixelCountsAndEmptyRegions()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets();
        h.Background = 0xFF143628; h.Frames(ui.Draw);
        Assert.Equal(h.Pixels.Count(p => p != h.Background), h.Covered());
        var count = 0;
        for (var y = 20; y < 100; y++) for (var x = 20; x < 200; x++) if (h.Pixels[y * h.Width + x] != h.Background) count++;
        Assert.Equal(count, h.Covered(20, 20, 200, 100));
        Assert.Equal(0, h.Covered(0, 0, 0, 0)); Assert.Equal(0, h.Covered(450, 270, 500, 320));
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
    private static byte[] DecodePng(byte[] png, int width, int height)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        var offset = 8; var names = new List<string>(); using var compressed = new MemoryStream();
        while (offset < png.Length)
        {
            Assert.InRange(offset + 12, 0, png.Length);
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            Assert.InRange(size, 0, png.Length - offset - 12);
            var name = Encoding.ASCII.GetString(png, offset + 4, 4); names.Add(name);
            Assert.Equal(Crc(png.AsSpan(offset + 4, 4 + size)), BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + size, 4)));
            var content = png.AsSpan(offset + 8, size);
            if (name == "IHDR")
            {
                Assert.Equal(13, size);
                Assert.Equal((uint)width, BinaryPrimitives.ReadUInt32BigEndian(content[..4]));
                Assert.Equal((uint)height, BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4, 4)));
                Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, content[8..].ToArray());
            }
            if (name == "IDAT") compressed.Write(content);
            if (name == "IEND") { Assert.Equal(0, size); Assert.Equal(png.Length, offset + 12); }
            offset += size + 12;
        }
        Assert.Equal("IHDR", names[0]); Assert.Equal("IEND", names[^1]); Assert.Contains("IDAT", names);
        compressed.Position = 0; using var z = new ZLibStream(compressed, CompressionMode.Decompress); using var raw = new MemoryStream();
        z.CopyTo(raw); var bytes = raw.ToArray();
        Assert.Equal(height * (1 + width * 4), bytes.Length);
        var rgba = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            var start = y * (1 + width * 4); Assert.Equal(0, bytes[start]);
            rgba.AddRange(bytes.AsSpan(start + 1, width * 4).ToArray());
        }
        return rgba.ToArray();
    }
    [Fact]
    public void PngSignatureDimensionsCrcAndZlibPixelsAreIndependent()
    {
        uint[] pixels = [0x11223344, 0xFF102030, 0x00010203, 0x8090A0B0];
        var bytes = DecodePng(Png.Encode(2, 2, pixels), 2, 2);
        Assert.Equal(new byte[] { 0x44,0x33,0x22,0x11, 0x30,0x20,0x10,0xFF, 0x03,0x02,0x01,0x00, 0xB0,0xA0,0x90,0x80 }, bytes);
    }
    [Fact]
    public void PngOneByThreePreservesTopToBottomOrder()
    {
        uint[] pixels = [0xFF0000FF, 0xFF00FF00, 0xFFFF0000];
        Assert.Equal(new byte[] { 255,0,0,255, 0,255,0,255, 0,0,255,255 }, DecodePng(Png.Encode(1, 3, pixels), 1, 3));
    }
    [Fact]
    public void SavedFrameIsAValidPngOfTheCurrentPixels()
    {
        using var h = new HeadlessHost(500, 320); var ui = new Widgets(); h.Frames(ui.Draw);
        var path = Path.Combine(Path.GetTempPath(), "a019-frame-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            h.Save(path); var rgba = DecodePng(File.ReadAllBytes(path), h.Width, h.Height);
            Assert.Equal(h.Pixels.SelectMany(p => new[] { (byte)p, (byte)(p >> 8), (byte)(p >> 16), (byte)(p >> 24) }), rgba);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
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

    [Fact]
    public void GuideMemoInspectorUsesThePluginsOwnCommand()
    {
        var r = Open();
        var item = r.Editor.Session.Current.Find("item")!;
        Assert.True(r.Editor.Session.Execute(new SetSceneProperty("item", "kind", Element("memo"))));
        var inspector = new Inspector<Scene>(r.Editor);
        var host = new PluginHost<Scene>(r.Editor, inspector, r.Keys);
        var manifest = Manifest() with { Id = "memos" };
        Assert.True(host.Add(new MemoExample.MemoPlugin(manifest)));
        r.Editor.Select("item");
        Assert.Equal("memos.note", inspector.SelectedType!.Id);
        Assert.True(inspector.Set("name", "A name"));
        Assert.Equal("memos.rename", r.Editor.Session.Journal[^1].Command);
        Assert.False(inspector.Set("name", ""));
        Assert.Equal("A name", r.Editor.Session.Current.Find("item")!.Properties["name"].GetString());
    }
}
}
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
