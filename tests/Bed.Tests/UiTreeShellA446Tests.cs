using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiTreeShellA446Tests
{
    private static EditorWindow<DataDocument> Window()
    {
        var schemas = new JsonSchemas().Add("sample.schema.json", """{"type":"object"}""");
        var editor = new EditorModel<DataDocument>(DataKit.Session(new DataFormat().Read("{}"), schemas, "sample.schema.json"), DataEditing.Outline);
        return new(editor);
    }
    [Theory]
    [InlineData(EditorThemePreference.Light)] [InlineData(EditorThemePreference.Dark)]
    public void Docked_shell_records_menus_toolbar_and_existing_unavailable_reasons(EditorThemePreference theme)
    {
        var beforeTheme = EditorTheme.Preference;
        try
        {
            using var host = new HeadlessHost(1400,900) { Drawing = false };
            EditorWindow<DataDocument>.Configure(Im.GetIO()); EditorTheme.Preference = theme;
            var window = Window(); host.Frames(window.DrawDocked,4);
            var nodes = Ui.Snapshot().Nodes;
            Assert.Equal(new[] { "File", "Edit", "View", "Tools", "Help" }, nodes.Where(n => n.Role == UiRole.Menu).Select(n => n.Label));
            var save = Assert.Single(nodes, n => n.Role == UiRole.Button && n.Label == "Save");
            Assert.False(save.Enabled); Assert.False(string.IsNullOrWhiteSpace(save.DisabledReason));
            var file = Assert.Single(nodes,n => n.Role == UiRole.Menu && n.Label == "File");
            host.Click(window.DrawDocked,file.Bounds.X+5,file.Bounds.Y+5);
            var missing = Assert.Single(Ui.Snapshot().Nodes,n => n.Role == UiRole.MenuItem && n.Label == "New project...");
            Assert.False(missing.Enabled); Assert.Equal("This editor has no project host.",missing.DisabledReason);
            Assert.Empty(window.Editor.Session.Journal); Assert.Equal(theme,EditorTheme.Preference);
        }
        finally { EditorTheme.Preference = beforeTheme; }
    }
    [Fact]
    public void Palette_and_child_windows_preserve_nesting_and_disabled_reasons()
    {
        using var host = new HeadlessHost(1200,800) { Drawing = false };
        EditorWindow<DataDocument>.Configure(Im.GetIO());
        var window = Window(); host.Frames(window.Draw,4);
        var nodes=Ui.Snapshot().Nodes;
        var child=Assert.Single(nodes,n=>n.Role==UiRole.Window && n.Label=="inspector");
        Assert.True(child.Parent>=0); Assert.Equal(UiRole.Window,nodes[child.Parent].Role);
        Assert.True(window.Run("editor.palette")); host.Frames(window.Draw,3);
        nodes=Ui.Snapshot().Nodes;
        Assert.Contains(nodes,n=>n.Role==UiRole.Dialog && n.Label=="palette");
        Assert.Contains(nodes,n=>n.Role==UiRole.TextField && n.Label=="Find command");
        Assert.Contains(nodes,n=>n.Role==UiRole.ListItem && !n.Enabled && !string.IsNullOrWhiteSpace(n.DisabledReason));
        host.Press(window.Draw,ImGuiKey.Escape);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Dialog && n.Label=="palette");
        Assert.Empty(window.Editor.Session.Journal);
    }
}
