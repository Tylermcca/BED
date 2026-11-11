using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

// PREPARED, UNRUN. Self-authored temporary folders and one sample adapter, never real project data.
[Collection(ImGuiCollection.Name)]
public sealed class FolderProjectA365Tests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "folder-project-" + Guid.NewGuid().ToString("N"));
    private readonly HeadlessHost host = new(1200, 800) { Drawing = false };
    public FolderProjectA365Tests() { Directory.CreateDirectory(root); EditorWindow<DataDocument>.Configure(ImGui.GetIO()); }
    public void Dispose() { host.Dispose(); Directory.Delete(root, true); }
    private string At(string name) => Path.Combine(root, name);
    private void Data(string folder)
    {
        Directory.CreateDirectory(At(folder));
        File.WriteAllText(Path.Combine(At(folder), "wares.json"), "{\"name\":\"Acme\"}");
        File.WriteAllText(Path.Combine(At(folder), "wares.schema.json"), "{\"type\":\"object\"}");
    }
    private string Map(string name)
    {
        File.WriteAllText(At(name), "{\"game\":\"Acme\",\"labels\":[],\"references\":[]}");
        return At(name);
    }
    private ProjectLauncher Launcher(params IProjectAdapter[] adapters) => new(new RecentProjects(At("recent.json"))) { Loaded = adapters };

    [Fact] public void Standard_layout_precedes_every_folder_provider()
    {
        Data("data"); Data("schemas");
        var provider = new Sample { Result = new("missing", "missing") };
        using var launcher = Launcher(provider);
        Assert.True(launcher.OpenFolder(root), launcher.Said);
        Assert.Equal(0, provider.Finds);
        Assert.Equal(At("data"), launcher.Current!.Data);
    }
    [Fact] public void Schema_beside_data_is_recognised_and_recent_rediscovery_tracks_move()
    {
        Data("content"); var provider = new Sample();
        using var launcher = Launcher(provider);
        Assert.True(launcher.OpenFolder(root), launcher.Said);
        Assert.Same(provider, launcher.Using);
        Assert.Equal(launcher.Current!.Data, launcher.Current.Schemas);
        var recent = Assert.Single(launcher.Recent.Items);
        Assert.Equal(root, recent.Folder); Assert.Equal("", recent.Data); Assert.Equal("", recent.Schemas);
        Assert.True(launcher.Close());
        Data("moved"); provider.Result = new("moved", "moved");
        // A host-supplied provider is already loaded, rather than loaded again from this test assembly.
        Assert.True(launcher.OpenRecent(recent with { AdapterPath = null }), launcher.Said);
        Assert.Equal(At("moved"), launcher.Current!.Data);
        Assert.Equal(2, provider.Finds);
    }
    [Fact] public void Selected_invalid_provider_refuses_without_trying_another()
    {
        Data("content"); var bad = new Sample { Result = new("missing", "missing") }; var next = new Sample();
        using var launcher = Launcher(next); launcher.Adapter = bad;
        Assert.False(launcher.OpenFolder(root)); Assert.Null(launcher.Workspace);
        Assert.Equal(1, bad.Finds); Assert.Equal(0, next.Finds); Assert.Empty(launcher.Recent.Items);
    }
    [Fact] public void Root_map_beats_provider_and_explicit_map_beats_root()
    {
        Data("content"); var providerMap = Map("provider.json"); var rootMap = Map("reference_map.json"); var explicitMap = Map("chosen.json");
        var provider = new Sample { Result = new("content", "content", providerMap) };
        using var launcher = Launcher(provider);
        Assert.True(launcher.OpenFolder(root), launcher.Said); Assert.Equal(rootMap, launcher.Current!.Map);
        Assert.Null(Assert.Single(launcher.Recent.Items).Map);
        Assert.True(launcher.Close());
        Assert.True(launcher.OpenFolder(root, explicitMap), launcher.Said); Assert.Equal(explicitMap, launcher.Current!.Map);
        Assert.Equal(explicitMap, Assert.Single(launcher.Recent.Items).Map);
    }
    [Fact] public void Missing_remembered_provider_nonstandard_folder_refuses_and_keeps_metadata()
    {
        Data("content"); var recent = new ProjectPaths("", "") { Folder = root, AdapterPath = At("missing.dll"), AdapterType = "Acme.Adapter" };
        using var launcher = Launcher(); launcher.Recent.Add(recent);
        Assert.False(launcher.OpenRecent(recent)); Assert.Null(launcher.Workspace);
        Assert.Contains("which isn't available", launcher.Said); Assert.Equal(recent, Assert.Single(launcher.Recent.Items));
    }
    [Fact] public void First_non_null_provider_is_used_without_a_second_recognition_gate()
    {
        Data("content"); var decline = new Sample { Result = null }; var accept = new Sample(); var unused = new Sample();
        using var launcher = Launcher(decline, accept, unused);
        Assert.True(launcher.OpenFolder(root), launcher.Said); Assert.Same(accept, launcher.Using);
        Assert.Equal(1, decline.Finds); Assert.Equal(1, accept.Finds); Assert.Equal(0, unused.Finds);
    }
    [Fact] public void Explicit_paths_bypass_folder_discovery_and_remain_explicit()
    {
        Data("content"); var provider = new Sample(); using var launcher = Launcher(provider); launcher.Adapter = provider;
        Assert.True(launcher.Open(new(At("content"), At("content"))), launcher.Said);
        Assert.Equal(0, provider.Finds); Assert.Null(Assert.Single(launcher.Recent.Items).Folder);
        Assert.Equal(At("content"), Assert.Single(launcher.Recent.Items).Data);
    }
    [Fact] public void Default_method_keeps_existing_adapters_compatible()
    {
        IProjectAdapter legacy = new Legacy(); Assert.Null(legacy.FindProject(root));
    }
    [Fact] public void Selected_provider_wins_over_standard_Acme_layout()
    {
        Data("data"); Data("schemas"); Data("data/game");
        var providerMap = Map("provider.json");
        var selected = new Sample { Result = new("data/game", "schemas", providerMap) };
        var unused = new Sample { Result = new("missing", "missing") };
        using var launcher = Launcher(unused); launcher.Adapter = selected;
        Assert.True(launcher.OpenFolder(root), launcher.Said);
        Assert.Same(selected, launcher.Using);
        Assert.Equal(At(Path.Combine("data", "game")), launcher.Current!.Data);
        Assert.Equal(At("schemas"), launcher.Current.Schemas);
        Assert.Equal(providerMap, launcher.Current.Map);
        Assert.Equal(root, launcher.Current.Folder);
        Assert.Equal(1, selected.Finds); Assert.Equal(0, unused.Finds);
    }
    [Fact] public void Declining_selected_provider_falls_back_once_to_standard_layout()
    {
        Data("data"); Data("schemas"); Data("data/game");
        var selected = new Sample { Result = null };
        var unused = new Sample { Result = new("data/game", "schemas") };
        using var launcher = Launcher(unused); launcher.Adapter = selected;
        Assert.True(launcher.OpenFolder(root), launcher.Said);
        Assert.Same(selected, launcher.Using);
        Assert.Equal(At("data"), launcher.Current!.Data);
        Assert.Equal(At("schemas"), launcher.Current.Schemas);
        Assert.Null(launcher.Current.Folder); // Standard folders keep their existing recent-project representation.
        Assert.Equal(1, selected.Finds); Assert.Equal(0, unused.Finds);
    }
    [Fact] public void Invalid_selected_answer_does_not_fall_back_to_available_standard_layout()
    {
        Data("data"); Data("schemas");
        var selected = new Sample { Result = new("missing", "missing") };
        var unused = new Sample();
        using var launcher = Launcher(unused); launcher.Adapter = selected;
        Assert.False(launcher.OpenFolder(root));
        Assert.Null(launcher.Workspace); Assert.Empty(launcher.Recent.Items);
        Assert.Contains("returned a data or schema folder that doesn't exist", launcher.Said);
        Assert.Equal(1, selected.Finds); Assert.Equal(0, unused.Finds);
    }
    [Fact] public void No_adapter_option_keeps_standard_layout_without_provider_calls()
    {
        Data("data"); Data("schemas"); Data("data/game");
        var selected = new Sample { Result = new("data/game", "schemas") };
        using var launcher = Launcher(selected); launcher.Adapter = selected; launcher.NoAdapter = true;
        Assert.True(launcher.OpenFolder(root), launcher.Said);
        Assert.Null(launcher.Using);
        Assert.Equal(At("data"), launcher.Current!.Data);
        Assert.Equal(0, selected.Finds);
    }
    [Fact] public void Selected_provider_keeps_root_then_explicit_map_precedence()
    {
        Data("data"); Data("schemas"); Data("data/game");
        var providerMap = Map("provider.json"); var rootMap = Map("reference_map.json"); var explicitMap = Map("chosen.json");
        var selected = new Sample { Result = new("data/game", "schemas", providerMap) };
        using var launcher = Launcher(); launcher.Adapter = selected;
        Assert.True(launcher.OpenFolder(root), launcher.Said);
        Assert.Equal(rootMap, launcher.Current!.Map); Assert.Equal(At(Path.Combine("data", "game")), launcher.Current.Data);
        Assert.True(launcher.Close());
        Assert.True(launcher.OpenFolder(root, explicitMap), launcher.Said);
        Assert.Equal(explicitMap, launcher.Current!.Map); Assert.Equal(At(Path.Combine("data", "game")), launcher.Current.Data);
        Assert.Equal(2, selected.Finds);
    }
    private sealed class Sample : IProjectAdapter
    {
        public string Name => "Acme folder adapter";
        public ProjectPaths? Result { get; set; } = new("content", "content");
        public int Finds { get; private set; }
        public ProjectPaths? FindProject(string folder) { Finds++; return Result; }
        public DataProject Open(ProjectPaths paths) => DataProject.Open(paths.Data, paths.Schemas);
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    }
    private sealed class Legacy : IProjectAdapter
    {
        public string Name => "Acme legacy adapter";
        public DataProject Open(ProjectPaths paths) => DataProject.Open(paths.Data, paths.Schemas);
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    }
}
