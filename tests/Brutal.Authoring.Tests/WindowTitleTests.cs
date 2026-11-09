using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class WindowTitleTests
{
    [Theory]
    [InlineData(null, null, false, "BED")]
    [InlineData(null, "orphan.json", true, "BED")]
    [InlineData("Workshop", null, false, "Workshop - BED")]
    [InlineData("Workshop", "items.json", false, "items.json - Workshop - BED")]
    [InlineData("Workshop", "items.json", true, "items.json* - Workshop - BED")]
    [InlineData("Café", "pièces.json", true, "pièces.json* - Café - BED")]
    public void Format_uses_current_project_file_and_saved_state(string? project, string? file, bool dirty, string expected)
        => Assert.Equal(expected, WindowTitle.Format(project, file, dirty));

    [Fact]
    public void Host_only_receives_changed_titles_including_save_file_switch_and_close()
    {
        var state = new WindowTitle("BED");
        var calls = new List<string>();
        Assert.False(state.Update("BED", calls.Add));
        Assert.True(state.Update(WindowTitle.Format("Workshop", "items.json"), calls.Add));
        Assert.False(state.Update(WindowTitle.Format("Workshop", "items.json"), calls.Add));
        Assert.True(state.Update(WindowTitle.Format("Workshop", "items.json", true), calls.Add));
        Assert.True(state.Update(WindowTitle.Format("Workshop", "items.json"), calls.Add));
        Assert.True(state.Update(WindowTitle.Format("Workshop", "other.json"), calls.Add));
        Assert.True(state.Update(WindowTitle.Format("Workshop"), calls.Add));
        Assert.True(state.Update(WindowTitle.Format(null), calls.Add));
        Assert.Equal(new[] { "items.json - Workshop - BED", "items.json* - Workshop - BED", "items.json - Workshop - BED", "other.json - Workshop - BED", "Workshop - BED", "BED" }, calls);
    }

    [Fact]
    public void Failed_native_update_can_be_retried_without_losing_state()
    {
        var state = new WindowTitle("BED");
        Assert.Throws<InvalidOperationException>(() => state.Update("Workshop - BED", _ => throw new InvalidOperationException("Synthetic host failure")));
        var calls = new List<string>();
        Assert.True(state.Update("Workshop - BED", calls.Add));
        Assert.Single(calls);
    }

    [Fact]
    public void No_project_uses_BED_without_reading_or_changing_recents()
    {
        var path = Path.Combine(Path.GetTempPath(), "title-" + Guid.NewGuid().ToString("N"), "recent.json");
        using var launcher = new ProjectLauncher(new RecentProjects(path));
        Assert.Equal("BED", WindowTitle.For(launcher));
        Assert.False(File.Exists(path));
        Assert.Null(launcher.Workspace);
    }
    [Fact]
    public void Actual_project_edit_undo_save_and_active_file_are_reflected()
    {
        var root = Path.Combine(Path.GetTempPath(), "a434-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "Workshop");
        var data = Path.Combine(project, "data");
        var schemas = Path.Combine(project, "schemas");
        try
        {
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(schemas);
            foreach (var file in new[] { "one", "two" })
            {
                File.WriteAllText(Path.Combine(data, file + ".json"), "{\"name\":\"Original\"}");
                File.WriteAllText(Path.Combine(schemas, file + ".schema.json"), "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");
            }
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json")));
            Assert.True(launcher.Open(new ProjectPaths(data, schemas)), launcher.Said);
            Assert.True(launcher.Workspace!.Show("one.json"));
            Assert.Equal("one.json - Workshop - BED", WindowTitle.For(launcher));
            var session = launcher.Workspace.Active!.Session!;
            Assert.True(session.Execute(new SetValue("/name", JsonSerializer.SerializeToElement("Changed"))));
            Assert.Equal("one.json* - Workshop - BED", WindowTitle.For(launcher));
            Assert.True(session.Undo());
            Assert.Equal("one.json - Workshop - BED", WindowTitle.For(launcher));
            Assert.True(session.Redo());
            Assert.True(launcher.Workspace.SaveAll(), launcher.Workspace.Said);
            Assert.Equal("one.json - Workshop - BED", WindowTitle.For(launcher));
            Assert.True(launcher.Workspace.Show("two.json"));
            Assert.Equal("two.json - Workshop - BED", WindowTitle.For(launcher));
            Assert.True(launcher.Close());
            Assert.Equal("BED", WindowTitle.For(launcher));
        }
        finally
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(root);
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a434-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup boundary.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }}
