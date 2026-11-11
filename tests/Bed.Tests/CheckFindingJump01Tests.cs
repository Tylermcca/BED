using System.Runtime.InteropServices;
using System.Text.Json;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class CheckFindingJump01Tests
{
    private sealed class Rig : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("finding-jump01-").FullName;
        public readonly HeadlessHost Host = new(1500, 1000) { Drawing = false };
        public readonly DataWorkspace Workspace;
        private readonly Dictionary<string, byte[]> original;
        public Rig()
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas, "from.schema.json"), "{}");
            File.WriteAllText(Path.Combine(schemas, "target.schema.json"), "{}");
            File.WriteAllText(Path.Combine(data, "from.json"), """{"items":[{"name":"Home"},{"name":"Return here"}]}""");
            var nested = Enumerable.Range(0, 125).Select(i => new { caption = "Entry " + i }).ToArray();
            File.WriteAllText(Path.Combine(data, "target.json"), JsonSerializer.Serialize(new { items = new[] { new { name = "First", details = new Dictionary<string, object> { ["a/b~c"] = "Exact field", ["nested"] = nested } } } }));
            File.WriteAllText(Path.Combine(data, "level.json"), new SceneFormat().Write(new Scene([SceneObject.At("beacon", null, new(0, 0, 0)), SceneObject.At("dock", null, new(3, 0, 0))])));
            original = Directory.GetFiles(data).ToDictionary(p => p, File.ReadAllBytes);
            Workspace = new(DataProject.Open(data, schemas));
            Assert.True(Workspace.Documents.BindScene("level.json", "scene", out var why), why);
            Assert.True(Workspace.Show("from.json"), Workspace.Said);
            Workspace.Window!.Editor.Select("/items/1");
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Host.Frames(Workspace.Draw, 4);
        }
        public async Task<CheckFinding> Run(CheckPlace? place)
        {
            var finding = new CheckFinding(Issue.Error("Inspect this field"), place, "Correct the named field.");
            Workspace.Checks.Add(new FindingCheck(finding));
            Assert.True(Workspace.RunCheck("fixture.finding"), Workspace.Said);
            Workspace.Window!.Editor.SetOpen("project.checks", true);
            await Workspace.Checks.Last("fixture.finding")!.Ended.WaitAsync(TimeSpan.FromSeconds(30));
            Host.Frames(Workspace.Draw, 4);
            return finding;
        }
        public UiNode Row() => Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label.StartsWith("Error: Inspect this field", StringComparison.Ordinal));
        public void Key(ImGuiKey key) { Host.Press(Workspace.Draw, key); Host.Frames(Workspace.Draw, 3); }
        public void FocusChecks(string fragment = "project.checks")
        {
            var io = ImGui.GetIO(); io.AddKeyEvent(ImGuiKey.Mod_Ctrl, true);
            try
            {
                var reached = false;
                for (var i = 0; i < 20; i++)
                {
                    Key(ImGuiKey.Tab);
                    var target = ImGui.GetCurrentContext().NavWindowingTarget;
                    var title = target.IsNull() ? "" : Marshal.PtrToStringUTF8((IntPtr)target.Name) ?? "";
                    if (title.Contains(fragment, StringComparison.Ordinal)) { reached = true; break; }
                }
                Assert.True(reached, "Native Ctrl+Tab did not reach " + fragment);
            }
            finally { io.AddKeyEvent(ImGuiKey.Mod_Ctrl, false); Host.Frames(Workspace.Draw, 3); }
        }
        public void NoEdits()
        {
            Assert.Empty(Workspace.Project.Dirty);
            foreach (var file in Workspace.Project.Files.Where(f => f.Session is not null))
            { Assert.Empty(file.Session!.Journal); Assert.False(file.Session.History.CanUndo); }
            foreach (var file in Workspace.Project.Files)
                if (Workspace.Documents.TryGetOpenScene(file.Name, out var scene))
                { Assert.Empty(scene!.Session.Journal); Assert.False(scene.Session.Dirty); Assert.False(scene.Session.History.CanUndo); }
            foreach (var (path, bytes) in original) Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        public void Dispose() { Workspace.Dispose(); Host.Dispose(); Directory.Delete(root, true); }
    }
    private sealed class FindingCheck(CheckFinding finding) : ICheck
    {
        public CheckDefinition Definition { get; } = new("fixture.finding", "Fixture finding", "Self-authored finding.", TimeSpan.FromSeconds(30));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
            => Task.FromResult(new CheckReport(true, [finding], "One finding."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finding_opens_exact_escaped_field_by_mouse_or_native_keyboard_and_Back_returns(bool keyboard)
    {
        using var r = new Rig();
        const string at = "/items/0/details/a~1b~0c";
        await r.Run(new("target.json", "/items/0", at));
        r.FocusChecks();
        if (keyboard)
        {
            for (var i = 0; !r.Row().Focused && i < 24; i++) r.Key(ImGuiKey.Tab);
            Assert.True(r.Row().Focused); Assert.True(r.Row().Enabled);
            r.Key(ImGuiKey.Enter);
        }
        else
        {
            var row = r.Row();
            r.Host.Click(r.Workspace.Draw, row.Bounds.X + 20, row.Bounds.Y + row.Bounds.Height / 2);
            r.Host.Frames(r.Workspace.Draw, 4);
        }
        Assert.Equal("target.json", r.Workspace.Active!.Name);
        Assert.Equal("/items/0", r.Workspace.Window!.Editor.Primary);
        Assert.Contains(at, r.Workspace.Said, StringComparison.Ordinal);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField && n.Label == "a/b~c" && n.Focused);
        r.NoEdits();
        r.FocusChecks("project.files");
        UiNode BackButton() => Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Button && n.Label == "Back to previous location");
        if (keyboard)
        {
            for (var i = 0; !BackButton().Focused && i < 24; i++) r.Key(ImGuiKey.Tab);
            Assert.True(BackButton().Focused); r.Key(ImGuiKey.Enter);
        }
        else
        {
            var back = BackButton(); r.Host.Click(r.Workspace.Draw, back.Bounds.X + 20, back.Bounds.Y + back.Bounds.Height / 2);
            r.Host.Frames(r.Workspace.Draw, 3);
        }
        Assert.Equal("from.json", r.Workspace.Active.Name); Assert.Equal("/items/1", r.Workspace.Window!.Editor.Primary); r.NoEdits();
    }

    [Fact]
    public async Task Deep_field_is_unfolded_and_revealed_on_its_later_page()
    {
        using var r = new Rig(); const string at = "/items/0/details/nested/124/caption";
        var f = await r.Run(new("target.json", "/items/0", at));
        Assert.True(r.Workspace.Reveal(f), r.Workspace.Said); r.Host.Frames(r.Workspace.Draw, 4);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField && n.Value == "Entry 124");
        Assert.Contains(at, r.Workspace.Said, StringComparison.Ordinal); r.NoEdits();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("missing")]
    [InlineData("escape")]
    [InlineData("field")]
    public void Unlocatable_finding_explains_why_without_editing(string kind)
    {
        using var r = new Rig();
        var place = kind switch
        {
            "none" => null,
            "missing" => new CheckPlace("absent.json", At: "/items/0/name"),
            "escape" => new CheckPlace("target.json", At: "/items/0/a~2b"),
            _ => new CheckPlace("target.json", "/items/0", "/items/0/details/missing")
        };
        var ok = r.Workspace.Reveal(new(Issue.Error("Missing"), place, "Locate it."));
        Assert.Equal(kind == "field", ok); Assert.False(string.IsNullOrWhiteSpace(r.Workspace.Said));
        if (kind == "field") Assert.Contains("could not be shown", r.Workspace.Said, StringComparison.Ordinal);
        else Assert.Equal("from.json", r.Workspace.Active!.Name);
        r.NoEdits();
    }

    [Fact]
    public void Same_file_Back_restores_multiple_selection_list_and_filter()
    {
        using var r = new Rig();
        var window = r.Workspace.Window!;
        window.Editor.Select("/items/0", "/items/1");
        var table = r.Workspace.TableOf("from.json")!; table.List = "/items"; table.Filter = "Return";
        Assert.True(r.Workspace.Reveal(new(Issue.Error("Name"), new("from.json", At: "/items/0/name"), "Review.")));
        Assert.Equal("", table.Filter); Assert.Equal("/items/0", window.Editor.Primary);
        Assert.True(r.Workspace.Back()); Assert.Equal(new[] { "/items/0", "/items/1" }, window.Editor.Selection);
        Assert.Equal("/items", table.List); Assert.Equal("Return", table.Filter); r.NoEdits();
    }

    [Fact]
    public void File_wide_findings_record_Back_and_scene_pointers_select_the_correct_object()
    {
        using var r = new Rig();
        Assert.True(r.Workspace.Reveal(new(Issue.Error("Whole file"), new("target.json"), "Review.")));
        Assert.True(r.Workspace.Back()); Assert.Equal("from.json", r.Workspace.Active!.Name);
        Assert.Equal("/items/1", r.Workspace.Window!.Editor.Primary);
        Assert.True(r.Workspace.Reveal(new(Issue.Error("Object field"), new("level.json", At: "/objects/1/position/0"), "Review.")));
        Assert.Equal("dock", r.Workspace.SceneWindow!.Editor.Primary); Assert.Contains("selected dock", r.Workspace.Said, StringComparison.Ordinal);
        Assert.True(r.Workspace.Back()); Assert.Equal("/items/1", r.Workspace.Window!.Editor.Primary);
        Assert.True(r.Workspace.Reveal(new(Issue.Error("Absent field"), new("level.json", At: "/objects/0/missing"), "Review.")));
        Assert.Contains("is absent", r.Workspace.Said, StringComparison.Ordinal); Assert.Equal("beacon", r.Workspace.SceneWindow!.Editor.Primary);
        Assert.True(r.Workspace.Back());
        Assert.True(r.Workspace.Show("level.json")); r.Workspace.SceneWindow!.Editor.Select("beacon");
        Assert.True(r.Workspace.Reveal(new(Issue.Error("Whole file"), new("target.json"), "Review.")));
        Assert.True(r.Workspace.Back()); Assert.Equal("beacon", r.Workspace.SceneWindow!.Editor.Primary); r.NoEdits();
    }
}
