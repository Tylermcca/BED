using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AdapterMapLoadingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "map-loading-" + Guid.NewGuid().ToString("N"));
    public AdapterMapLoadingTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        File.WriteAllText(Path.Combine(root, "schemas", "empty.schema.json"), "{\"type\":\"object\"}");
    }
    public void Dispose() => Directory.Delete(root, true);
    [Fact] public void Root_map_is_resolved_before_workspace_and_explicit_missing_map_refuses()
    {
        var map = Path.Combine(root, "reference_map.json");
        File.WriteAllText(map, "{\"game\":\"Synthetic\",\"labels\":[],\"references\":[]}");
        using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "settings", "recent.json")));
        Assert.True(launcher.Open(new(Path.Combine(root, "data"), Path.Combine(root, "schemas"))));
        Assert.NotNull(launcher.Workspace!.Map);
        Assert.Equal(Path.GetFullPath(map), launcher.Current!.Map);
        Assert.True(launcher.Close());
        Assert.False(launcher.Open(new(Path.Combine(root, "data"), Path.Combine(root, "schemas"), Path.Combine(root, "missing.json"))));
        Assert.Contains("There's no link map", launcher.Said);
        Assert.Null(launcher.Workspace);
    }
    [Fact] public void Malformed_root_map_refuses_instead_of_becoming_a_mapless_workspace()
    {
        File.WriteAllText(Path.Combine(root, "reference_map.json"), "not JSON");
        using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "settings", "recent.json")));
        Assert.False(launcher.OpenFolder(root));
        Assert.Contains("can't be read", launcher.Said);
        Assert.Null(launcher.Workspace);
    }
}
