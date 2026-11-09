using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Launcher;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class CompareNavigationA370Tests : IDisposable
{
    private const string Before = """{"rows":[{"id":"keep","value":1},{"id":"gone","value":2}]}""";
    private const string After = """{"rows":[{"id":"keep","value":3},{"id":"new","value":4}]}""";
    private readonly string root = Directory.CreateTempSubdirectory("compare-navigation-").FullName;
    public void Dispose() => Directory.Delete(root, recursive: true);
    private static DataDocument Doc(string text) => new DataFormat().Read(text);

    [Fact]
    public void Counts_include_one_of_each_without_changing_results()
    {
        var before = Doc(Before); var after = Doc(After);
        var result = StructuralCompare.Compare(before, after);
        var changes = result.Changes.ToArray();
        Assert.True(result.Complete);
        Assert.Equal(new CompareNavigation.Counts("File", 1, 1, 1), CompareNavigation.Total(result));
        Assert.Equal(new CompareNavigation.Counts("/rows", 1, 1, 1), Assert.Single(CompareNavigation.ByList(before, after, result)));
        Assert.Equal(changes, result.Changes);
        Assert.Equal(Before, before.Root.GetRawText()); Assert.Equal(After, after.Root.GetRawText());
    }

    [Fact]
    public void Deepest_list_and_file_fields_have_separate_summary_buckets()
    {
        var before = Doc("""{"title":"a","rows":[{"id":"a","children":[{"id":"b","v":1}]}]}""");
        var after = Doc("""{"title":"b","rows":[{"id":"a","children":[{"id":"b","v":2}]}]}""");
        var result = StructuralCompare.Compare(before, after);
        var groups = CompareNavigation.ByList(before, after, result);
        Assert.Equal(2, CompareNavigation.Total(result).Changed);
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.List == "/rows/0/children" && g.Changed == 1);
        Assert.Contains(groups, g => g.List == "(outside lists)" && g.Changed == 1);
    }

    [Fact]
    public void Removed_nested_item_resolves_moved_parent_by_key_not_old_index()
    {
        var before = Doc("""{"rows":[{"id":"a","children":[{"id":"gone"}]},{"id":"b","children":[]}]}""");
        var after = Doc("""{"rows":[{"id":"b","children":[]},{"id":"a","children":[]}]}""");
        var result = StructuralCompare.Compare(before, after);
        var removed = Assert.Single(result.Changes);
        Assert.Equal("removed", removed.Kind);
        Assert.Equal("/rows/1/children", CompareNavigation.Target(before, after, result, removed));
        var positional = StructuralCompare.Compare(before, after, new Dictionary<string, string?> { ["/rows"] = null });
        var syntheticRemoved = new StructuralCompare.Change("removed", "/rows/0/children/0", null, "{}", null);
        Assert.Null(CompareNavigation.Target(before, after, positional, syntheticRemoved));
    }

    private (DataWorkspace Workspace, CompareFilePanel Panel) Open()
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        File.WriteAllText(Path.Combine(data, "items.json"), Before);
        File.WriteAllText(Path.Combine(schemas, "items.schema.json"), """{"type":"object"}""");
        var workspace = new DataWorkspace(DataProject.Open(data, schemas));
        CompareFilePanel? panel = null;
        workspace.FilePanels.Add((file, editor) => (panel = new CompareFilePanel(file, editor, workspace.Project.Schemas,
            pointer => workspace.GoTo(new ReferenceTarget(file.Name, pointer)))).Panel());
        Assert.True(workspace.Show("items.json"));
        Assert.NotNull(panel);
        Assert.True(workspace.Window!.Editor.Session.Execute(new SetValue("/rows", JsonDocument.Parse(After).RootElement.GetProperty("rows").Clone())));
        return (workspace, panel!);
    }

    [Fact]
    public async Task Palette_actions_visit_changed_removed_added_and_previous_without_saving()
    {
        var (workspace, panel) = Open();
        using (workspace)
        {
            var window = workspace.Window!; var editor = window.Editor;
            var saved = File.ReadAllBytes(workspace.Active!.Path);
            var draft = editor.Session.Current; var journal = editor.Session.Journal.ToArray();
            Assert.True(window.Run("editor.panel_do.editor.compare/editor.compare.saved"));
            await panel.Pending!.WaitAsync(TimeSpan.FromSeconds(10));
            var table = workspace.TableOf("items.json")!;
            table.Filter = "no matching item";
            Assert.True(window.Run("editor.panel_do.editor.compare/next"));
            Assert.Equal("/rows/0", editor.Primary);
            Assert.Equal("/rows", table.Shown); Assert.Equal("", table.Filter);
            Assert.True(window.Run("editor.panel_do.editor.compare/next"));
            Assert.Equal("/rows", editor.Primary); // not the old /rows/1, now a different item
            Assert.Contains("removed", panel.NavigationStatus);
            Assert.True(window.Run("editor.panel_do.editor.compare/next"));
            Assert.Equal("/rows/1", editor.Primary);
            Assert.True(window.Run("editor.panel_do.editor.compare/previous"));
            Assert.Equal("/rows", editor.Primary);
            Assert.Same(draft, editor.Session.Current); Assert.Equal(journal, editor.Session.Journal);
            Assert.True(editor.Session.Dirty); Assert.Equal(saved, File.ReadAllBytes(workspace.Active!.Path));
            Assert.True(editor.Session.Execute(new SetValue("/rows/0/value", JsonDocument.Parse("9").RootElement.Clone())));
            var selected = editor.Primary;
            Assert.False(panel.Move(1));
            Assert.Equal(selected, editor.Primary);
            Assert.Contains("changed since this compare", panel.NavigationStatus);
        }
    }

    [Fact]
    public void Comparison_shortcuts_are_rebindable_and_preserve_the_keys_panel()
    {
        var shortcuts = new Shortcuts();
        Assert.Equal("editor.panel_do.editor.compare/next", shortcuts.Action(Chord.Parse("Alt+F5")));
        Assert.Equal("editor.panel_do.editor.compare/previous", shortcuts.Action(Chord.Parse("Shift+Alt+F5")));
        Assert.Equal("editor.panel.bed.keys", shortcuts.Action(Chord.Parse("F1")));
        shortcuts.Set("editor.panel_do.editor.compare/next", "Ctrl+F8");
        Assert.Null(shortcuts.Action(Chord.Parse("Alt+F5")));
        Assert.Equal("editor.panel_do.editor.compare/next", shortcuts.Action(Chord.Parse("Ctrl+F8")));
    }
}
