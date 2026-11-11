using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DroppedStartTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dropped-start-" + Guid.NewGuid().ToString("N"));
    private readonly HeadlessHost host = new(1400, 900) { Drawing = false };

    public DroppedStartTests()
    {
        Directory.CreateDirectory(root);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
    }

    private ProjectLauncher Launcher() => new(new RecentProjects(Path.Combine(root, "settings", "recent.json")),
        creator: StartWindowTestProjects.Creator(root), projectsFolder: Path.Combine(root, "projects"));

    private string Project(string name, string data = "data", string schemas = "schemas")
    {
        var at = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(at, data)); Directory.CreateDirectory(Path.Combine(at, schemas));
        File.WriteAllText(Path.Combine(at, data, "items.json"), """{"name":"Original"}""");
        File.WriteAllText(Path.Combine(at, schemas, "items.schema.json"),
            """{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}""");
        return at;
    }

    private static Dictionary<string, byte[]> Bytes(string at) => Directory.GetFiles(at, "*", SearchOption.AllDirectories)
        .ToDictionary(file => Path.GetRelativePath(at, file), File.ReadAllBytes);

    private static void Unchanged(string at, Dictionary<string, byte[]> before)
    {
        var after = Bytes(at); Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, bytes) in before) Assert.Equal(bytes, after[name]);
    }

    private (WindowDrop Drop, UiSnapshot Frame) Capture(ProjectLauncher launcher, params string[] paths)
    {
        host.MoveMouse(launcher.Draw, 20, 20); host.Frames(launcher.Draw, 3);
        var frame = Ui.Snapshot();
        using var queue = new WindowDropQueue();
        Assert.True(queue.Post(paths, 20, 20)); Assert.True(queue.TryTakeDrop(out var drop)); Assert.NotNull(drop);
        var target = drop.TargetWindow(frame); Assert.NotNull(target);
        Assert.Equal(UiRole.Window, target.Value.Role); Assert.Equal("BED", target.Value.Label);
        Assert.False(queue.TryTakeDrop(out _));
        return (drop, frame);
    }

    private string DrawText(ProjectLauncher launcher)
    {
        host.Frames(launcher.Draw, 2);
        host.Frame(() => { ImGui.LogToClipboard(); launcher.Draw(); ImGui.LogFinish(); });
        return ImGui.GetClipboardText().ToString() ?? "";
    }

    [Fact]
    public void A_queued_standard_folder_opens_once_through_the_real_route_and_keeps_source_bytes()
    {
        var folder = Project("Invented project"); var before = Bytes(folder);
        using var launcher = Launcher(); launcher.FileDialogs = new NoDialogs();
        var (drop, _) = Capture(launcher, folder);
        Assert.True(launcher.DropOnStart(drop.Paths), launcher.Said);
        Assert.NotNull(launcher.Workspace); Assert.Equal(Path.Combine(folder, "data"), launcher.Current!.Data);
        Assert.Single(launcher.Recent.Items); host.Frames(launcher.Draw, 3);
        Assert.Contains(Ui.Snapshot().Nodes, node => node.Role == UiRole.Window && node.Label == "Files");
        Assert.False(launcher.DropOnStart(drop.Paths));
        Assert.Single(launcher.Recent.Items); Unchanged(folder, before);
    }

    [Fact]
    public void A_nonstandard_folder_uses_the_existing_adapter_provider_without_plain_data_fallback()
    {
        var folder = Project("Provider project", "records", "rules"); var before = Bytes(folder);
        var adapter = new InventedAdapter();
        using var launcher = Launcher(); launcher.Loaded = [adapter]; launcher.FileDialogs = new NoDialogs();
        var (drop, _) = Capture(launcher, folder);
        Assert.True(launcher.DropOnStart(drop.Paths), launcher.Said);
        Assert.Same(adapter, launcher.Using); Assert.Equal(1, adapter.Opens);
        Assert.Equal(Path.Combine(folder, "records"), launcher.Current!.Data);
        Assert.Equal(folder, launcher.Current.Folder); Assert.Single(launcher.Recent.Items);
        host.Frames(launcher.Draw, 2);
        Assert.Contains(Ui.Snapshot().Nodes, node => node.Role == UiRole.Window && node.Label == "Files");
        Unchanged(folder, before);
    }

    [Fact]
    public void An_unrecognised_folder_waits_for_manual_choice_then_opens_only_after_real_keyboard_confirmation()
    {
        var folder = Project("Loose data", "loose", "rules"); var loose = Path.Combine(folder, "loose");
        var before = Bytes(folder);
        using var launcher = Launcher(); launcher.FileDialogs = new NoDialogs();
        var (drop, _) = Capture(launcher, loose);
        Assert.False(launcher.DropOnStart(drop.Paths));
        Assert.Null(launcher.Workspace); Assert.Null(launcher.Current); Assert.Empty(launcher.Recent.Items);
        var text = DrawText(launcher);
        Assert.Contains(loose, text); Assert.Contains("Rules folder (schema)", text);
        Assert.Contains("Choose the two folders separately", launcher.Said);
        Assert.Contains(Ui.Snapshot().Nodes, node => node.Role == UiRole.Status && node.IdKind == UiIdKind.Semantic && node.Label.Contains("Choose the two folders separately"));
        Unchanged(folder, before);
        // The chosen Project folder is focused. Traverse the existing manual inputs to Rules, then Open.
        for (var i = 0; i < 5; i++) host.Press(launcher.Draw, ImGuiKey.Tab);
        host.Press(launcher.Draw, ImGuiKey.A, ctrl: true); host.Type(launcher.Draw, Path.Combine(folder, "rules"));
        for (var i = 0; i < 4; i++) host.Press(launcher.Draw, ImGuiKey.Tab);
        host.Press(launcher.Draw, ImGuiKey.Enter); host.Frames(launcher.Draw, 2);
        Assert.NotNull(launcher.Workspace); Assert.Equal(loose, launcher.Current!.Data);
        Assert.Single(launcher.Recent.Items); Unchanged(folder, before);
    }

    [Fact]
    public void Escape_from_manual_choice_opens_nothing_and_changes_no_source()
    {
        var folder = Project("Loose again", "loose", "rules"); var before = Bytes(folder);
        using var launcher = Launcher(); var (drop, _) = Capture(launcher, Path.Combine(folder, "loose"));
        Assert.False(launcher.DropOnStart(drop.Paths)); host.Frames(launcher.Draw, 2);
        host.Press(launcher.Draw, ImGuiKey.Escape);
        Assert.Contains("Open recent", DrawText(launcher));
        Assert.Null(launcher.Workspace); Assert.Empty(launcher.Recent.Items); Unchanged(folder, before);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_stale_start_target_cannot_replace_even_a_clean_workspace_or_its_draft(bool dirty)
    {
        var first = Project("First"); var second = Project("Second");
        var firstBefore = Bytes(first); var secondBefore = Bytes(second);
        using var launcher = Launcher(); var (drop, oldFrame) = Capture(launcher, second);
        Assert.True(launcher.OpenFolder(first)); var workspace = launcher.Workspace; var current = launcher.Current;
        var file = workspace!.Project.Find("items.json")!; Assert.True(file.Open());
        if (dirty) Assert.True(file.Session!.Execute(new SetValue("/name", JsonSerializer.SerializeToElement("Draft"))));
        var revision = file.Session!.Revision; var journal = file.Session.Journal.Count; var recent = launcher.Recent.Items.ToArray();
        Assert.NotNull(drop.TargetWindow(oldFrame)); // Deliberately stale recorded start window.
        Assert.False(launcher.DropOnStart(drop.Paths)); Assert.Contains("already open", launcher.Said);
        Assert.Same(workspace, launcher.Workspace); Assert.Same(current, launcher.Current);
        Assert.Equal(recent, launcher.Recent.Items); Assert.Equal(revision, file.Session.Revision);
        Assert.Equal(journal, file.Session.Journal.Count); Assert.Equal(dirty, file.Dirty);
        Assert.Equal(dirty ? "Draft" : "Original", file.Session.Current.At("/name")!.Value.GetString());
        Unchanged(first, firstBefore); Unchanged(second, secondBefore);
    }

    [Theory]
    [InlineData("file")] [InlineData("missing")] [InlineData("multiple")]
    public void Unsupported_drops_show_a_plain_recorded_reason_without_opening_or_mutating(string kind)
    {
        var folder = Project("Source"); var before = Bytes(folder);
        string[] paths = kind switch
        {
            "file" => [Path.Combine(folder, "data", "items.json")],
            "missing" => [Path.Combine(root, "missing")],
            _ => [folder, folder],
        };
        using var launcher = Launcher(); var (drop, _) = Capture(launcher, paths);
        Assert.False(launcher.DropOnStart(drop.Paths));
        Assert.Null(launcher.Workspace); Assert.Null(launcher.Current); Assert.Empty(launcher.Recent.Items);
        var expected = kind == "file" ? "not files" : kind == "missing" ? "wasn't found" : "one project folder";
        Assert.Contains(expected, DrawText(launcher));
        Assert.Contains(Ui.Snapshot().Nodes, node => node.Role == UiRole.Status && node.IdKind == UiIdKind.Semantic && node.Label.Contains(expected));
        Unchanged(folder, before);
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public string? PickFolder(string title, string? start) => throw new InvalidOperationException("A drop must not open a native picker.");
        public string? OpenFile(string title, IReadOnlyList<FileDialogFilter> filters, string? start) => throw new InvalidOperationException();
        public string? SaveFile(string title, IReadOnlyList<FileDialogFilter> filters, string? suggestedName) => throw new InvalidOperationException();
    }

    private sealed class InventedAdapter : IProjectAdapter
    {
        public string Name => "Invented drop provider";
        public int Opens;
        public ProjectPaths? FindProject(string folder) => Directory.Exists(Path.Combine(folder, "records")) ? new("records", "rules") : null;
        public DataProject Open(ProjectPaths paths) { Opens++; return DataProject.Open(paths.Data, paths.Schemas); }
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    }

    public void Dispose()
    {
        host.Dispose();
        var full = Path.GetFullPath(root); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(full) != temp || !Path.GetFileName(full).StartsWith("dropped-start-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test cleanup boundary.");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }
}
