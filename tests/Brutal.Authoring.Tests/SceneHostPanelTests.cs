using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneHostPanelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "scene-panels-" + Guid.NewGuid().ToString("N"));
    public void Dispose() => Directory.Delete(root, true);

    private DataProject Project()
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        File.WriteAllText(Path.Combine(data, "room.json"), new SceneFormat().Write(new Scene([SceneObject.At("item", null, Vector3.Zero)])));
        var project = DataProject.Open(data, schemas);
        Assert.True(DocumentHost.For(project).BindScene("room.json", "scene", out _));
        return project;
    }

    [Fact]
    public void Real_host_panels_share_selection_inspector_edit_and_undo_without_writing()
    {
        using var workspace = new DataWorkspace(Project());
        Assert.True(workspace.Show("room.json"), workspace.Said);
        var window = workspace.SceneWindow!;
        var view = workspace.SceneViewport!;
        Assert.Same(window.Editor, view.Editor);
        using var host = new HeadlessHost(1600, 900);
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        // Exercise the actual workspace route, including its Files window, not a standalone scene sample.
        host.Frames(workspace.Draw, 4);
        var (origin, size) = view.LastRect;
        Assert.True(size.X > 640 && size.Y > 540);
        Assert.True(origin.X > 240 && origin.X + size.X < 1360);
        var center = view.ScreenOf(Vector3.Zero)!.Value;
        host.Click(workspace.Draw, center.X, center.Y);
        Assert.Equal(["item"], window.Editor.Selection);
        Assert.NotNull(window.Inspector);
        Assert.True(window.Inspector.Set("position.x", 2.0));
        Assert.Equal(2f, view.Editor.Session.Current.Find("item")!.Position.X);
        Assert.True(window.Run("editor.undo"));
        Assert.Equal(Vector3.Zero, view.Editor.Session.Current.Find("item")!.Position);
        Assert.Equal(Vector3.Zero, new SceneFormat().Read(File.ReadAllText(window.Editor.Session.FilePath!)).Find("item")!.Position);
    }

    [Fact]
    public void Reopening_and_second_workspace_do_not_duplicate_viewport_or_create_another_editor()
    {
        var project = Project();
        using var first = new DataWorkspace(project);
        using var second = new DataWorkspace(project);
        Assert.True(first.Show("room.json"));
        var view = first.SceneViewport;
        Assert.True(first.Show("room.json"));
        Assert.True(second.Show("room.json"));
        Assert.Same(view, second.SceneViewport);
        Assert.Same(first.SceneWindow!.Editor, second.SceneWindow!.Editor);
        Assert.Single(first.SceneWindow.Editor.Panels, p => p.Id == "editor.viewport");
        Assert.NotNull(second.SceneWindow.Inspector);
    }
}

