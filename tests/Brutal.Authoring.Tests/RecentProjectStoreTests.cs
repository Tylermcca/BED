using System.Text.Json;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

// INVENTED: small public-facade witnesses for the extracted persistence owner.
// No launcher, native context, application, process or actual game project is involved.
public sealed class RecentProjectStoreTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("recent-store-").FullName;
    private string Settings => Path.Combine(root, "settings", "recent.json");
    private ProjectPaths Project(string name, string? map = null) =>
        new(Path.Combine(root, name, "data"), Path.Combine(root, name, "schemas"), map);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Two_instances_merge_latest_disk_state_and_removal_does_not_resurrect_it()
    {
        var first = new RecentProjects(Settings);
        var second = new RecentProjects(Settings);
        var one = Project("one"); var two = Project("two");
        first.Add(one);
        second.Add(two);
        Assert.Equal(new[] { two, one }, new RecentProjects(Settings).Items);
        first.Remove(two); // first's original in-memory list never saw two
        Assert.Equal(new[] { one }, new RecentProjects(Settings).Items);
        Assert.Equal(new[] { one }, first.Items);
        Assert.Equal(Settings, first.File);
        Assert.Equal(10, first.Limit);
    }

    [Fact]
    public void An_unreadable_json_list_stays_byte_exact_while_local_edits_remain_visible()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        const string broken = "{ self-authored incomplete settings";
        File.WriteAllText(Settings, broken);
        var before = File.ReadAllBytes(Settings);
        var recent = new RecentProjects(Settings);
        Assert.Empty(recent.Items);
        var one = Project("one");
        recent.Add(one);
        Assert.Equal(one, Assert.Single(recent.Items));
        Assert.Equal(before, File.ReadAllBytes(Settings));
        recent.Remove(one);
        Assert.Empty(recent.Items);
        Assert.Equal(before, File.ReadAllBytes(Settings));
    }

    [Fact]
    public void Readding_same_folders_moves_to_front_and_replaces_map_without_duplicate()
    {
        var recent = new RecentProjects(Settings, 2);
        var one = Project("one"); var two = Project("two"); var three = Project("three");
        recent.Add(one); recent.Add(two); recent.Add(three);
        Assert.Equal(new[] { three, two }, recent.Items);
        var updated = two with { Map = Path.Combine(root, "new-map.json") };
        recent.Add(updated);
        Assert.Equal(new[] { updated, three }, recent.Items);
        Assert.Equal(recent.Items, new RecentProjects(Settings, 2).Items);
        Assert.False(File.Exists(Settings + ".tmp"));
    }

    [Fact]
    public void Read_skips_bad_entries_and_keeps_first_duplicate_up_to_limit()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        var one = Project("one"); var two = Project("two");
        var text = "[null,17," + JsonSerializer.Serialize(one) + "," +
            JsonSerializer.Serialize(one with { Map = "later-map" }) + "," +
            JsonSerializer.Serialize(new ProjectPaths("", "bad")) + "," +
            JsonSerializer.Serialize(two) + "]";
        File.WriteAllText(Settings, text);
        var recent = new RecentProjects(Settings, 2);
        Assert.Equal(new[] { one, two }, recent.Items);
        var before = File.ReadAllBytes(Settings);
        recent.Add(new ProjectPaths("", "bad"));
        Assert.Equal(before, File.ReadAllBytes(Settings));
        Assert.Equal(new[] { one, two }, recent.Items);
    }
}
