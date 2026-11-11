using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

// INVENTED synthetic fixtures. Prepared only; no runtime or UI validation claimed.
[Collection(ImGuiCollection.Name)]
public sealed class RecentProjectMetadataA437Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("recent-metadata-").FullName;
    private string Settings => Path.Combine(root, "recent.json");
    public void Dispose() => Directory.Delete(root, true);
    private ProjectPaths Project(string name = "sample")
    {
        var data = Directory.CreateDirectory(Path.Combine(root, name, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, name, "schemas")).FullName;
        File.WriteAllText(Path.Combine(data, "settings.json"), "{\"name\":\"Sample\"}");
        File.WriteAllText(Path.Combine(schemas, "settings.schema.json"), "{\"type\":\"object\"}");
        return new(data, schemas);
    }
    private HeadlessHost Host()
    {
        var host = new HeadlessHost(1200, 800) { Drawing = false };
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        return host;
    }
    private sealed class Connection : IProjectAdapter
    {
        public string Name => "Sample";
        public bool Recognises(ProjectPaths paths) => true;
        public DataProject Open(ProjectPaths paths) => DataProject.Open(paths.Data, paths.Schemas);
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Successful_open_records_utc_and_actual_kind_without_changing_path_equality(bool connected)
    {
        using var host = Host();
        var paths = Project(); var recent = new RecentProjects(Settings);
        using var launcher = new ProjectLauncher(recent);
        if (connected) launcher.Adapter = new Connection();
        var before = DateTimeOffset.UtcNow;
        Assert.True(launcher.Open(paths), launcher.Said);
        var after = DateTimeOffset.UtcNow;
        var item = Assert.Single(recent.Items);
        Assert.Equal((paths.Data, paths.Schemas), (item.Data, item.Schemas)); // a connected open also records its adapter (existing launcher behaviour)
        var metadata = recent.Metadata(paths);
        Assert.NotNull(metadata.LastOpenedUtc);
        Assert.InRange(metadata.LastOpenedUtc.Value, before, after);
        Assert.Equal(TimeSpan.Zero, metadata.LastOpenedUtc.Value.Offset);
        Assert.Equal(connected ? "Sample connection" : "Data only", metadata.Kind);
        Assert.Equal(metadata, new RecentProjects(Settings).Metadata(paths));
    }

    [Fact]
    public void Failed_initial_and_replacement_opens_do_not_create_or_refresh_metadata()
    {
        using var host = Host(); var paths = Project(); var recent = new RecentProjects(Settings);
        using var launcher = new ProjectLauncher(recent);
        var missing = new ProjectPaths(Path.Combine(root, "absent"), paths.Schemas);
        Assert.False(launcher.Open(missing)); Assert.Empty(recent.Items); Assert.False(File.Exists(Settings));
        Assert.Null(recent.Metadata(missing).LastOpenedUtc);
        Assert.True(launcher.Open(paths), launcher.Said);
        var metadata = recent.Metadata(paths); var bytes = File.ReadAllBytes(Settings);
        Assert.False(launcher.Open(missing));
        Assert.Equal(metadata, recent.Metadata(paths)); Assert.Equal(bytes, File.ReadAllBytes(Settings));
        Assert.Equal(paths, launcher.Current); Assert.Single(recent.Items);
    }

    [Fact]
    public void Legacy_array_and_plain_add_keep_unknown_time_and_kind_and_all_provenance()
    {
        var paths = Project() with { Map = Path.Combine(root, "map.json"), AdapterPath = Path.Combine(root, "sample.dll"), AdapterType = "Sample.Connection" };
        File.WriteAllText(Settings, JsonSerializer.Serialize(new[] { paths }));
        var before = File.ReadAllBytes(Settings); var recent = new RecentProjects(Settings);
        Assert.Equal(paths, Assert.Single(recent.Items));
        Assert.Equal(new RecentProjectMetadata(null, "Unknown"), recent.Metadata(paths));
        Assert.Equal(before, File.ReadAllBytes(Settings));
        recent.Add(paths);
        Assert.Equal(paths, Assert.Single(new RecentProjects(Settings).Items));
        Assert.Equal(new RecentProjectMetadata(null, "Unknown"), recent.Metadata(paths));
        using var json = JsonDocument.Parse(File.ReadAllText(Settings));
        Assert.False(json.RootElement[0].TryGetProperty("LastOpenedUtc", out _));
    }

    [Theory]
    [InlineData("not a time")]
    [InlineData("2026-01-02T03:04:05")]
    [InlineData("")]
    public void Bad_optional_time_does_not_drop_a_usable_legacy_entry(string malformed)
    {
        var paths = Project(); var node = JsonSerializer.SerializeToNode(paths)!.AsObject();
        node["LastOpenedUtc"] = malformed; node["Kind"] = 17;
        File.WriteAllText(Settings, new JsonArray(node).ToJsonString());
        var recent = new RecentProjects(Settings);
        Assert.Equal(paths, Assert.Single(recent.Items));
        Assert.Equal(new RecentProjectMetadata(null, "Unknown"), recent.Metadata(paths));
    }

    [Fact]
    public void Offset_timestamp_is_read_in_utc_and_plain_add_retains_it()
    {
        var paths = Project(); var node = JsonSerializer.SerializeToNode(paths)!.AsObject();
        node["LastOpenedUtc"] = "2026-01-02T03:04:05+02:00"; node["Kind"] = "Sample connection";
        File.WriteAllText(Settings, new JsonArray(node).ToJsonString());
        var recent = new RecentProjects(Settings); var expected = new DateTimeOffset(2026, 1, 2, 1, 4, 5, TimeSpan.Zero);
        Assert.Equal(expected, recent.Metadata(paths).LastOpenedUtc);
        recent.Add(paths);
        Assert.Equal(new RecentProjectMetadata(expected, "Sample connection"), new RecentProjects(Settings).Metadata(paths));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_folder_kind_is_a_read_time_projection_and_forget_does_not_delete(bool folderBased)
    {
        var paths = Project();
        if (folderBased) paths = new("", "") { Folder = Path.GetDirectoryName(paths.Data) };
        var recent = new RecentProjects(Settings); recent.Opened(paths, "Sample connection");
        var old = recent.Metadata(paths); var bytes = File.ReadAllBytes(Settings);
        var folder = folderBased ? paths.Folder! : paths.Schemas;
        var moved = folder + "-moved"; Directory.Move(folder, moved);
        Assert.Equal("Folder not found", recent.Metadata(paths).Kind);
        Assert.Equal(old.LastOpenedUtc, recent.Metadata(paths).LastOpenedUtc);
        Assert.Equal(bytes, File.ReadAllBytes(Settings));
        Directory.Move(moved, folder);
        Assert.Equal(old, recent.Metadata(paths));
        recent.Remove(paths);
        Assert.Empty(new RecentProjects(Settings).Items); Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(folderBased ? Path.Combine(folder, "data") : paths.Data, "settings.json")));
    }

    [Fact]
    public void Concurrent_store_merge_retains_other_entry_metadata_and_removal()
    {
        var one = Project("one"); var two = Project("two");
        var first = new RecentProjects(Settings); var second = new RecentProjects(Settings);
        first.Opened(one, "One connection"); var oneMetadata = first.Metadata(one);
        second.Opened(two, "Data only");
        var merged = new RecentProjects(Settings);
        Assert.Equal(new[] { two, one }, merged.Items); Assert.Equal(oneMetadata, merged.Metadata(one));
        Assert.Equal("Data only", merged.Metadata(two).Kind); Assert.NotNull(merged.Metadata(two).LastOpenedUtc);
        first.Remove(two);
        var remaining = new RecentProjects(Settings);
        Assert.Equal(one, Assert.Single(remaining.Items)); Assert.Equal(oneMetadata, remaining.Metadata(one));
    }

    [Fact]
    public void Unreadable_settings_remain_exact_even_after_success_event_and_forget()
    {
        var paths = Project(); const string invalid = "{ broken settings";
        File.WriteAllText(Settings, invalid); var bytes = File.ReadAllBytes(Settings);
        var recent = new RecentProjects(Settings); recent.Opened(paths, "Data only");
        Assert.Equal(paths, Assert.Single(recent.Items)); Assert.NotNull(recent.Metadata(paths).LastOpenedUtc);
        Assert.Equal(bytes, File.ReadAllBytes(Settings));
        recent.Remove(paths); Assert.Empty(recent.Items); Assert.Equal(bytes, File.ReadAllBytes(Settings));
    }
}
