using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

// Public launcher seam; empty data folders deliberately avoid Show/Make/Refocus and native frame state.
[Collection(ImGuiCollection.Name)]
public sealed class ProjectLauncherLoadingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "launcher-loading-" + Guid.NewGuid().ToString("N"));
    private string History => Path.Combine(root, "settings", "recent.json");
    private ProjectPaths Paths { get; }
    public ProjectLauncherLoadingTests()
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "sample", "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "sample", "schemas")).FullName;
        File.WriteAllText(Path.Combine(schemas, "empty.schema.json"), "{\"type\":\"object\"}");
        Paths = new(data, schemas);
    }
    public void Dispose() => Directory.Delete(root, recursive: true);
    private ProjectLauncher Launcher() => new(new RecentProjects(History));
    private sealed class Probe(string name, List<string> calls) : IProjectAdapter
    {
        public Func<ProjectPaths, bool>? Recognise { get; init; }
        public Func<ProjectPaths, DataProject>? Load { get; init; }
        public string Name => name;
        public bool Recognises(ProjectPaths paths) { calls.Add(name + ":recognise"); return Recognise?.Invoke(paths) ?? true; }
        public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
        public DataProject Open(ProjectPaths paths) { calls.Add(name + ":open"); return Load?.Invoke(paths) ?? DataProject.Open(paths.Data, paths.Schemas); }
    }

    [Fact]
    public void Empty_project_load_keeps_exact_message_full_paths_and_recent_facade()
    {
        using var launcher = Launcher();
        Assert.True(launcher.Open(Paths));
        Assert.Equal("Opened sample: 0 files; choose another in the Files panel.", launcher.Said);
        Assert.Equal(Paths, launcher.Current);
        Assert.Null(launcher.Using);
        Assert.Empty(launcher.Workspace!.Project.Files);
        Assert.Null(launcher.Workspace.Active);
        Assert.Equal(Paths, Assert.Single(new RecentProjects(History).Items));
    }

    [Fact]
    public void Folder_validation_precedes_adapter_selection_and_keeps_exact_refusal()
    {
        var calls = new List<string>();
        using var launcher = Launcher(); launcher.Loaded = [new Probe("Never", calls)];
        Assert.False(launcher.Open(Paths with { Data = "" }));
        Assert.Equal("Choose the data folder: the folder of your game's JSON files.", launcher.Said);
        Assert.Empty(calls); Assert.Null(launcher.Workspace); Assert.False(File.Exists(History));
        Assert.False(launcher.Open(Paths with { Schemas = "" }));
        Assert.Equal("Choose the schema folder: the folder of JSON Schemas that describe the data.", launcher.Said);
        Assert.Empty(calls);
    }

    [Fact]
    public void Failed_recognition_then_first_match_preserves_call_order_and_diagnostic_bytes()
    {
        var calls = new List<string>();
        var broken = new Probe("Broken", calls) { Recognise = _ => throw new InvalidOperationException("synthetic recognition") };
        var chosen = new Probe("Chosen", calls);
        using var launcher = Launcher(); launcher.Loaded = [broken, chosen, new Probe("Later", calls)];
        Assert.True(launcher.Open(Paths)); Assert.Same(chosen, launcher.Using);
        Assert.Equal(["Broken:recognise", "Chosen:recognise", "Chosen:open"], calls);
        Assert.Equal("Opened sample with Chosen: 0 files; choose another in the Files panel. Broken couldn't tell whether this is its project (synthetic recognition), so it wasn't used.", launcher.Said);
    }

    [Fact]
    public void Null_adapter_result_preserves_existing_workspace_paths_and_history()
    {
        using var launcher = Launcher(); Assert.True(launcher.Open(Paths));
        var workspace = launcher.Workspace; var current = launcher.Current; var history = File.ReadAllBytes(History);
        launcher.Adapter = new NullAdapter();
        Assert.False(launcher.Open(Paths));
        Assert.Equal("Empty gave back no project from Open. That's a fault in the adapter, not your data: choose None (plain data) to open it without.", launcher.Said);
        Assert.Same(workspace, launcher.Workspace); Assert.Same(current, launcher.Current); Assert.Null(launcher.Using);
        Assert.Equal(history, File.ReadAllBytes(History));
    }
    private sealed class NullAdapter : IProjectAdapter
    {
        public string Name => "Empty";
        public DataProject Open(ProjectPaths paths) => null!;
        public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Existing_exception_categories_keep_their_exact_messages(bool io)
    {
        using var launcher = Launcher();
        launcher.Adapter = new Probe("Faulty", []) { Load = _ =>
        {
            if (io) throw new IOException("synthetic load");
            throw new InvalidOperationException("synthetic load");
        } };
        Assert.False(launcher.Open(Paths));
        Assert.Equal(io ? "The project can't be opened: synthetic load"
            : "Faulty couldn't open the project: synthetic load. That's a fault in the adapter, not your data: choose None (plain data) to open it without.", launcher.Said);
        Assert.Null(launcher.Workspace); Assert.False(File.Exists(History));
    }

    [Fact]
    public void Explicit_selection_and_none_do_not_probe_the_automatic_list()
    {
        var calls = new List<string>(); var selected = new Probe("Selected", calls);
        using var launcher = Launcher(); launcher.Adapter = selected; launcher.Loaded = [new Probe("Automatic", calls)];
        Assert.True(launcher.Open(Paths)); Assert.Equal(["Selected:open"], calls);
        launcher.NoAdapter = true; calls.Clear();
        Assert.True(launcher.Open(Paths)); Assert.Empty(calls); Assert.Null(launcher.Using);
        Assert.Equal("Opened sample: 0 files; choose another in the Files panel.", launcher.Said);
    }

    [Fact]
    public void Missing_map_refusal_occurs_before_adapter_and_without_changing_state()
    {
        var calls = new List<string>(); using var launcher = Launcher(); launcher.Loaded = [new Probe("Never", calls)];
        var missing = Path.Combine(root, "absent-map.json");
        Assert.False(launcher.Open(Paths with { Map = missing }));
        Assert.Equal($"There's no link map at {missing}. Leave it empty if the project has none.", launcher.Said);
        Assert.Empty(calls); Assert.Null(launcher.Workspace); Assert.False(File.Exists(History));
    }
}
