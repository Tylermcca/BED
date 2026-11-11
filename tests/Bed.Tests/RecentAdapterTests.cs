using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class RecentAdapterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "recent-adapter-" + Guid.NewGuid().ToString("N"));
    private readonly HeadlessHost host = new(1200, 800) { Drawing = false };
    private string History => Path.Combine(root, "settings", "recent.json");
    private ProjectPaths Paths => ProjectPaths.In(root)!;
    public RecentAdapterTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "data")); Directory.CreateDirectory(Path.Combine(root, "schemas"));
        File.WriteAllText(Path.Combine(root, "schemas", "recipes.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(root, "data", "recipes.json"), """{"recipes":[{"id":"synthetic","makes":"bread","from":"flour"}]}""");
        File.WriteAllText(Path.Combine(root, "reference_map.json"), """{"game":"Starter","labels":[],"references":[]}""");
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
    }
    public void Dispose() { host.Dispose(); Directory.Delete(root, true); }
    // Same built-starter lookup convention as LauncherTests. Prepared only, never invoked by this worker.
    private static string StarterDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Bed.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Directory.EnumerateFiles(dir!.FullName, "StarterAdapter.dll", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                !f.Contains("ref" + Path.DirectorySeparatorChar) &&
                !Path.GetRelativePath(dir!.FullName, f).StartsWith("codex" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).First();
    }
    private static IProjectAdapter Starter()
    {
        var (loaded, problems) = Adapters.Load([StarterDll()]);
        Assert.Empty(problems); return Assert.Single(loaded);
    }
    [Fact] public void External_starter_is_remembered_and_exact_type_reopens_despite_global_plain_selection()
    {
        var starter = Starter();
        using (var first = new ProjectLauncher(new RecentProjects(History)) { Loaded = [starter] })
        {
            Assert.True(first.OpenFolder(root), first.Said);
            Assert.Same(starter, first.Using);
        }
        var recent = new RecentProjects(History); var entry = Assert.Single(recent.Items);
        Assert.Equal(Path.GetFullPath(starter.GetType().Assembly.Location), entry.AdapterPath);
        Assert.Equal(starter.GetType().FullName, entry.AdapterType);
        Assert.Equal(Paths.Map, entry.Map);
        using var again = new ProjectLauncher(recent) { NoAdapter = true };
        Assert.True(again.OpenRecent(entry), again.Said);
        Assert.Equal(entry.AdapterType, again.Using!.GetType().FullName);
        var file = again.Workspace!.Project.Find("recipes.json")!;
        Assert.False(file.Session!.Execute(new SetValue("/recipes/0/from", JsonSerializer.SerializeToElement("bread"))));
    }
    [Fact] public void Missing_adapter_opens_plain_over_global_selection_and_keeps_recovery_metadata()
    {
        var entry = Paths with { AdapterPath = Path.Combine(root, "gone.dll"), AdapterType = "StarterAdapter.StarterAdapter" };
        var recent = new RecentProjects(History); recent.Add(entry);
        using var launcher = new ProjectLauncher(recent) { Adapter = Starter() };
        Assert.True(launcher.OpenRecent(entry), launcher.Said);
        Assert.Null(launcher.Using); Assert.Contains("missing", launcher.Said); Assert.Contains("Data only", launcher.Said);
        Assert.Equal(entry.AdapterPath, Assert.Single(new RecentProjects(History).Items).AdapterPath);
        Assert.Equal(entry.AdapterType, Assert.Single(new RecentProjects(History).Items).AdapterType);
    }
    [Fact] public void Wrong_type_never_silently_selects_another_type_from_the_same_dll()
    {
        var entry = Paths with { AdapterPath = StarterDll(), AdapterType = "Synthetic.DoesNotExist" };
        using var launcher = new ProjectLauncher(new RecentProjects(History)) { Adapter = Starter() };
        Assert.True(launcher.OpenRecent(entry), launcher.Said);
        Assert.Null(launcher.Using); Assert.Contains("could not be loaded", launcher.Said);
        Assert.Equal(entry.AdapterType, Assert.Single(new RecentProjects(History).Items).AdapterType);
    }
    [Fact] public void Failed_load_is_reported_plain_and_retained()
    {
        var bad = Path.Combine(root, "not-an-assembly.dll"); File.WriteAllText(bad, "self-authored invalid assembly fixture");
        var entry = Paths with { AdapterPath = bad, AdapterType = "Synthetic.Invalid" };
        using var launcher = new ProjectLauncher(new RecentProjects(History));
        Assert.True(launcher.OpenRecent(entry), launcher.Said);
        Assert.Null(launcher.Using); Assert.Contains("could not be loaded", launcher.Said);
        Assert.Equal(bad, Assert.Single(new RecentProjects(History).Items).AdapterPath);
    }
    [Fact] public void Old_settings_keep_working_without_new_fields()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(History)!);
        File.WriteAllText(History, JsonSerializer.Serialize(new[] { new { Paths.Data, Paths.Schemas, Paths.Map } }));
        var recent = new RecentProjects(History); var old = Assert.Single(recent.Items);
        Assert.Null(old.AdapterPath); Assert.Null(old.AdapterType);
        using var launcher = new ProjectLauncher(recent) { Loaded = [Starter()] };
        Assert.True(launcher.OpenRecent(old), launcher.Said);
        Assert.NotNull(launcher.Using);
    }
    [Fact] public void Dirty_current_project_refuses_recent_open_without_replacing_its_workspace()
    {
        using var launcher = new ProjectLauncher(new RecentProjects(History));
        Assert.True(launcher.Open(Paths));
        var current = launcher.Workspace!;
        Assert.True(current.Project.Find("recipes.json")!.Session!.Execute(new SetValue("/recipes/0/id", JsonSerializer.SerializeToElement("changed"))));
        var missing = Paths with { AdapterPath = Path.Combine(root, "missing.dll"), AdapterType = "Missing.Type" };
        Assert.False(launcher.OpenRecent(missing));
        Assert.Same(current, launcher.Workspace);
        Assert.Contains("unsaved edits", launcher.Said);
        Assert.DoesNotContain("missing", launcher.Said);
    }
    private sealed class NestedAdapter : IProjectAdapter
    {
        public string Name => "Synthetic nested";
        public DataProject Open(ProjectPaths p) => DataProject.Open(Path.Combine(p.Data, "nested"), p.Schemas);
        public void Configure(EditorModel<DataDocument> e, DataTableView t) { }
    }
    [Fact] public void Saved_folders_are_the_ones_actually_opened_and_explicit_map_is_retained()
    {
        Directory.CreateDirectory(Path.Combine(Paths.Data, "nested"));
        using var launcher = new ProjectLauncher(new RecentProjects(History)) { Adapter = new NestedAdapter() };
        Assert.True(launcher.Open(Paths), launcher.Said);
        var saved = Assert.Single(new RecentProjects(History).Items);
        Assert.Equal(launcher.Workspace!.Project.DataFolder, saved.Data);
        Assert.Equal(launcher.Workspace.Project.SchemaFolder, saved.Schemas);
        Assert.Equal(Paths.Map, saved.Map);
    }
}
