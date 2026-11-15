using System.Numerics;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DockSee01Tests
{
    [Theory]
    [InlineData(EditorThemePreference.Light)]
    [InlineData(EditorThemePreference.Dark)]
    [InlineData(EditorThemePreference.HighContrast)]
    public void Dock_tabs_draw_hover_and_drag_feedback_without_platform_windows(EditorThemePreference theme)
    {
        var previous = EditorTheme.Preference;
        try
        {
            EditorTheme.Preference = theme;
            using var host = new HeadlessHost(1280, 800) { Drawing = false };
            EditorWindow<Scene>.Configure(ImGui.GetIO());
            var scene = new Scene([SceneObject.At("box", null, Vector3.Zero)]);
            var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline);
            editor.Add(new Panel("editor.table", "Table", () => Ui.Text("Project values"u8)));
            editor.Add(new Panel("demo.preview", "Source preview", () => Ui.Text("Drag a tab to move a panel"u8)));
            var window = new EditorWindow<Scene>(editor, new Inspector<Scene>(editor).Add(SceneEditing.ObjectType()));
            host.Frames(window.DrawDocked, 5);
            var preview = ImGui.Internal.FindWindowByName("Source preview###panel.demo.preview"u8);
            var table = ImGui.Internal.FindWindowByName("Table###panel.editor.table"u8);
            Assert.False(preview.IsNull()); Assert.Equal(table.DockId, preview.DockId);
            var tab = preview.Pos + new Brutal.Numerics.float2(30, ImGui.GetFontSize() / 2 + 4);
            host.MoveMouse(window.DrawDocked, tab.X, tab.Y); host.Frames(window.DrawDocked, 2);
            host.Drawing = true; host.Frame(window.DrawDocked);
            Save(host, theme, "hover");
            // Hold a real native tab drag over the centre: native drop markers/preview are part of the captured frame.
            var io = ImGui.GetIO(); io.AddMouseButtonEvent(0, true); host.Drawing = false; host.Frame(window.DrawDocked);
            host.MoveMouse(window.DrawDocked, tab.X + 40, tab.Y + 60);
            host.MoveMouse(window.DrawDocked, preview.Pos.X + preview.Size.X / 2, preview.Pos.Y + preview.Size.Y / 2);
            host.Frames(window.DrawDocked, 3); Assert.True(ImGui.GetCurrentContext().DragDropActive); host.Drawing = true; host.Frame(window.DrawDocked);
            Save(host, theme, "drag");
            io.AddMouseButtonEvent(0, false); host.Drawing = false; host.Frames(window.DrawDocked, 3);
            Assert.False(io.ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable));
            Assert.False(editor.Session.Dirty); Assert.Equal(0, editor.Session.History.UndoCount);
        }
        finally { EditorTheme.Preference = previous; }
    }

    [Theory]
    [InlineData(EditorThemePreference.Light, 1f)]
    [InlineData(EditorThemePreference.Dark, 1f)]
    [InlineData(EditorThemePreference.HighContrast, 1f)]
    [InlineData(EditorThemePreference.Light, 2f)]
    [InlineData(EditorThemePreference.Dark, 2f)]
    [InlineData(EditorThemePreference.HighContrast, 2f)]
    public void Outlines_selected_edges_hover_and_drop_feedback_survive_theme_refresh_and_scale(EditorThemePreference theme, float dpi)
    {
        var previous = EditorTheme.Preference;
        try
        {
            EditorTheme.Preference = theme;
            using var host = new HeadlessHost(200, 100) { Drawing = false };
            var style = ImGui.GetStyle(); style.FontScaleMain = 1.5f;
            new DpiScale().Apply(dpi);
            EditorTheme.Refresh(); EditorTheme.Refresh();
            Assert.Equal(dpi, style.TabBorderSize); Assert.Equal(dpi, style.WindowBorderSize);
            Assert.Equal(dpi, style.TabBarBorderSize); Assert.Equal(3 * dpi, style.TabBarOverlineSize);
            Assert.Equal(4 * dpi, style.DockingSeparatorSize); Assert.Equal(1.5f, style.FontScaleMain);
            Assert.NotEqual(style.Colors[(int)ImGuiCol.Tab], style.Colors[(int)ImGuiCol.TabSelected]);
            Assert.NotEqual(style.Colors[(int)ImGuiCol.Tab], style.Colors[(int)ImGuiCol.TabHovered]);
            Assert.NotEqual(style.Colors[(int)ImGuiCol.TabSelected], style.Colors[(int)ImGuiCol.TabHovered]);
            Assert.Equal(style.Colors[(int)ImGuiCol.TabSelectedOverline], style.Colors[(int)ImGuiCol.TabDimmedSelectedOverline]);
            Assert.True(style.Colors[(int)ImGuiCol.DockingPreview].W >= .85f);
            Assert.Equal(1f, style.Colors[(int)ImGuiCol.DragDropTarget].W);
        }
        finally { EditorTheme.Preference = previous; }
    }
    private static void Save(HeadlessHost host, EditorThemePreference theme, string state)
    {
        var stage = Environment.GetEnvironmentVariable("BED_DOCK_SEE_STAGE");
        if (stage is not ("before" or "after")) return;
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Bed.slnx"))) repo = repo.Parent;
        var path = Path.Combine(repo!.FullName, ".validation", "dock-see01"); Directory.CreateDirectory(path);
        host.Save(Path.Combine(path, $"{stage}-{theme}-{state}.png"));
    }
}