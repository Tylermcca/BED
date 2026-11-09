using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

// INVENTED: narrow descriptor-cache witnesses, not frame timing or renderer tests.
// Reflection keeps this implementation seam private; no product API exists only for a test.
[Collection(ImGuiCollection.Name)]
public sealed class FileActionCacheTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("file-action-cache-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");

    public FileActionCacheTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        foreach (var name in new[] { "one", "two" })
        {
            File.WriteAllText(Path.Combine(Data, name + ".json"), "{}");
            File.WriteAllText(Path.Combine(Schemas, name + ".schema.json"), "{}");
        }
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DataWorkspace Workspace(bool readOnly = false, Func<bool>? close = null) =>
        new(DataProject.Open(Data, Schemas, readOnly)) { CloseProject = close };

    private static Func<object> Actions(DataWorkspace workspace) =>
        typeof(DataWorkspace).GetMethod("FileActions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<object>>(workspace);

    private static ITuple[] Rows(object actions) => ((IEnumerable)actions).Cast<ITuple>().ToArray();
    private static string[] Labels(object actions) => Rows(actions).Select(row => (string)row[1]!).ToArray();
    private static Action Run(object actions, string label) =>
        (Action)Assert.Single(Rows(actions), row => (string)row[1]! == label)[2]!;

    [Fact]
    public void Warm_read_only_action_lookup_allocates_no_descriptor_or_closure()
    {
        var workspace = Workspace(readOnly: true);
        var get = Actions(workspace);
        var first = get();
        for (var i = 0; i < 16; i++) _ = get();
        var changed = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 32; i++) if (!ReferenceEquals(first, get())) changed++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, changed);
        Assert.Equal(0L, allocated);
        Assert.Equal(new[] { "Save all", "Refresh" }, Labels(first));
    }

    [Fact]
    public void Writable_warm_lookup_returns_one_descriptor_set_and_changes_with_availability()
    {
        var workspace = Workspace();
        var get = Actions(workspace);
        var empty = get();
        Assert.Equal(new[] { "Save all", "Refresh", "New" }, Labels(empty));
        for (var i = 0; i < 32; i++) Assert.Same(empty, get());
        Assert.True(workspace.Show("one.json"));
        var shown = get();
        Assert.NotSame(empty, shown);
        Assert.Equal(new[] { "Save all", "Refresh", "New", "Rename", "Delete" }, Labels(shown));
        Assert.True(workspace.Show("two.json"));
        Assert.Same(shown, get()); // availability, not file identity; form callbacks use the live Active
        Run(shown, "Save all")();
        Assert.Equal("Nothing to save.", workspace.Said);
        Assert.True(workspace.DeleteFile("two.json"));
        var deleted = get();
        Assert.NotSame(shown, deleted);
        Assert.Equal(new[] { "Save all", "Refresh", "New", "Restore" }, Labels(deleted));
    }

    [Fact]
    public void Cached_restore_reads_the_latest_deleted_file_at_invocation()
    {
        using var host = new HeadlessHost() { Drawing = false };
        var workspace = Workspace();
        var get = Actions(workspace);
        Assert.True(workspace.DeleteFile("one.json"));
        var actions = get();
        var restore = Run(actions, "Restore");
        Assert.True(workspace.DeleteFile("two.json"));
        Assert.Same(actions, get());
        Assert.Same(restore, Run(get(), "Restore"));
        restore();
        Assert.NotNull(workspace.Project.Find("two.json"));
        Assert.Equal(new[] { "one.json" }, workspace.Project.Deleted);
        Assert.Same(actions, get());
        restore();
        Assert.NotNull(workspace.Project.Find("one.json"));
        Assert.Empty(workspace.Project.Deleted);
        Assert.NotSame(actions, get());
        Assert.DoesNotContain("Restore", Labels(get()));
    }

    [Fact]
    public void Close_callback_is_last_and_owned_by_its_workspace()
    {
        var calls = 0;
        var first = Workspace(close: () => { calls++; return false; });
        var second = Workspace(close: () => { calls += 10; return true; });
        var one = Actions(first); var two = Actions(second);
        Assert.Equal(new[] { "Save all", "Refresh", "New", "Close project" }, Labels(one()));
        var close = Run(one(), "Close project");
        Assert.Same(close, Run(one(), "Close project"));
        close();
        Assert.Equal(1, calls);
        Run(two(), "Close project")();
        Assert.Equal(11, calls);
        Assert.NotSame(one(), two());
    }
}
