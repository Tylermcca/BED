using Bed.Editor.ImGui;
using Bed.Data;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class RecentContextMenuTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "recent-context-a451-" + Guid.NewGuid().ToString("N"));
    private readonly HeadlessHost host = new(1400, 900) { Drawing = false };
    private readonly ProjectPaths alpha, beta;
    private readonly ProjectLauncher launcher;
    public RecentContextMenuTests()
    {
        Directory.CreateDirectory(root);
        alpha = MakeProject("alpha"); beta = MakeProject("beta");
        var recent = new RecentProjects(Path.Combine(root, "recent.json")); recent.Add(alpha); recent.Add(beta);
        launcher = new(recent, alpha, projectsFolder: Path.Combine(root, "personal"));
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(launcher.Draw, 4);
    }
    private ProjectPaths MakeProject(string name)
    {
        var folder = Path.Combine(root, name); Directory.CreateDirectory(Path.Combine(folder, "data")); Directory.CreateDirectory(Path.Combine(folder, "schemas"));
        File.WriteAllText(Path.Combine(folder, "data", "settings.json"), "{\"value\":1}");
        File.WriteAllText(Path.Combine(folder, "schemas", "settings.schema.json"), "{\"type\":\"object\"}");
        return ProjectPaths.In(folder)!;
    }
    private static UiNode Row(string label) => Ui.Snapshot().Nodes.Single(n => n.Role == UiRole.ListItem && n.Label == label);
    private static UiNode Menu(string label) => Ui.Snapshot().Nodes.Single(n => n.Role == UiRole.MenuItem && n.Label == label);
    private void Click(UiNode node) => host.Click(launcher.Draw, node.Bounds.X + node.Bounds.Width / 2, node.Bounds.Y + node.Bounds.Height / 2);
    private void KeyboardMenu(string row, bool shift)
    {
        Click(Row(row));
        host.Press(launcher.Draw, shift ? ImGuiKey.F10 : ImGuiKey.Menu, shift: shift);
        Assert.Equal(new[] { "Open", "Show in Explorer", "Pin", "Remove from list" }, Ui.Snapshot().Nodes.Where(n => n.Role == UiRole.MenuItem).Select(n => n.Label));
    }
    private Dictionary<string, byte[]> ProjectBytes() => new[] { alpha, beta }.SelectMany(p => Directory.GetFiles(Path.GetDirectoryName(p.Data)!, "*", SearchOption.AllDirectories))
        .ToDictionary(p => p, File.ReadAllBytes);
    private static void AssertBytes(Dictionary<string, byte[]> before) { foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path)); }
    public void Dispose()
    {
        launcher.Dispose(); host.Dispose();
        var owned = Path.GetFullPath(root); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!owned.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(owned).StartsWith("recent-context-a451-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture cleanup escaped its owned temporary root.");
        Directory.Delete(owned, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Menu_key_and_ShiftF10_open_the_same_target_and_use_existing_Open(bool shift)
    {
        var before = ProjectBytes(); KeyboardMenu("alpha", shift);
        Assert.True(Menu("Open").Enabled); Assert.True(Menu("Show in Explorer").Enabled); Assert.True(Menu("Remove from list").Enabled);
        Assert.True(Row("alpha").Selected);
        Click(Menu("Open"));
        Assert.NotNull(launcher.Workspace); Assert.Equal(alpha.Data, launcher.Current!.Data);
        AssertBytes(before);
    }

    [Fact]
    public void Right_click_selects_its_row_before_Remove_and_never_deletes_project_files()
    {
        var before = ProjectBytes(); Click(Row("alpha"));
        var target = Row("beta"); var stableId = target.Id; Assert.Equal(UiActions.Select | UiActions.Focus, target.Actions); host.MoveMouse(launcher.Draw, target.Bounds.X + 10, target.Bounds.Y + target.Bounds.Height / 2);
        ImGui.GetIO().AddMouseButtonEvent(1, true); host.Frame(launcher.Draw);
        ImGui.GetIO().AddMouseButtonEvent(1, false); host.Frame(launcher.Draw); host.Frame(launcher.Draw);
        Assert.True(Row("beta").Selected); Assert.False(Row("alpha").Selected);
        Assert.Equal(stableId, Row("beta").Id);
        Assert.Equal(UiIdKind.ImGui, Row("beta").IdKind);
        Assert.Equal(UiActions.None, Row("beta").Actions); // The open popup correctly gates its background row.
        Click(Menu("Remove from list"));
        Assert.Single(launcher.Recent.Items); Assert.Equal(alpha.Data, launcher.Recent.Items[0].Data);
        Assert.Null(launcher.Workspace); AssertBytes(before);
        Assert.Contains("files aren't deleted", launcher.Said);
    }

    [Fact]
    public void Explicit_root_recovery_is_not_disabled_by_stale_cached_data_paths()
    {
        var recovered = MakeProject("root-recovery"); var folder = Path.GetDirectoryName(recovered.Data)!;
        var bytes = File.ReadAllBytes(Path.Combine(recovered.Data, "settings.json"));
        launcher.Recent.Add(new(Path.Combine(root, "old-cache", "data"), Path.Combine(root, "old-cache", "schemas")) { Folder = folder });
        host.Frames(launcher.Draw, 2); KeyboardMenu("root-recovery", false);
        Assert.True(Menu("Open").Enabled); Assert.Null(Menu("Open").DisabledReason);
        Click(Menu("Open"));
        Assert.NotNull(launcher.Workspace); Assert.Equal(recovered.Data, launcher.Current!.Data);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(recovered.Data, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "old-cache")));
    }

    [Fact]
    public void Missing_folder_disables_Open_and_Explorer_with_reasons_but_remains_removable()
    {
        var missing = new ProjectPaths(Path.Combine(root, "missing", "data"), Path.Combine(root, "missing", "schemas"));
        launcher.Recent.Add(missing); host.Frames(launcher.Draw, 2); KeyboardMenu("missing", false);
        Assert.False(Menu("Open").Enabled); Assert.Contains("data folder", Menu("Open").DisabledReason);
        Assert.False(Menu("Show in Explorer").Enabled); Assert.Contains("not found", Menu("Show in Explorer").DisabledReason);
        Assert.True(Menu("Remove from list").Enabled);
        Click(Menu("Remove from list"));
        Assert.DoesNotContain(launcher.Recent.Items, p => p.Data == missing.Data); Assert.Null(launcher.Workspace);
        Assert.False(Directory.Exists(Path.Combine(root, "missing")));
    }

    [Fact]
    public void Missing_schema_folder_has_its_own_reason_and_does_not_disable_existing_Explorer_route()
    {
        var folder = Path.Combine(root, "no-schema"); Directory.CreateDirectory(Path.Combine(folder, "data"));
        launcher.Recent.Add(new(Path.Combine(folder, "data"), Path.Combine(folder, "schemas")));
        host.Frames(launcher.Draw, 2); KeyboardMenu("no-schema", true);
        Assert.False(Menu("Open").Enabled); Assert.Contains("rules folder (schemas)", Menu("Open").DisabledReason);
        Assert.True(Menu("Show in Explorer").Enabled); Assert.Null(Menu("Show in Explorer").DisabledReason);
        Assert.Null(launcher.Workspace); // Explorer is inspected only: no OS process/window is invoked.
    }
}
