using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PreviewSpace01Tests
{
    private const string Preview = "Source preview###panel.demo.preview";
    private const string Table = "Table###panel.editor.table";
    private sealed class Rig : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("bed-preview-space-").FullName;
        internal readonly HeadlessHost Host;
        internal readonly DataWorkspace Workspace;
        internal Rig(PanelSide side = PanelSide.Centre, string? saved = null, bool register = true)
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
            File.WriteAllText(Path.Combine(data, "items.json"), "{\"items\":[{\"id\":\"box\",\"amount\":2}]}");
            File.WriteAllText(Path.Combine(schemas, "items.schema.json"), "{}");
            Host = new(1280, 800) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            ImGui.GetStyle().FontSizeBase = 16;
            if (saved is not null) ImGui.LoadIniSettingsFromMemory(saved);
            Workspace = new(DataProject.Open(data, schemas));
            if (register) Workspace.FilePanels.Add((_, _) => new Panel("demo.preview", "Source preview", () => Ui.Text("Adapter picture"u8),
                Width: 640, Height: 480, Side: side));
            Assert.True(Workspace.Show("items.json"));
            Host.Frames(Workspace.Draw, 5);
        }
        internal void CentreTab()
        {
            var preview = ImGui.Internal.FindWindowByName(Preview);
            var table = ImGui.Internal.FindWindowByName(Table);
            var inspector = ImGui.Internal.FindWindowByName("Inspector###inspector"u8);
            Assert.False(preview.IsNull()); Assert.False(table.IsNull());
            Assert.NotEqual(default, preview.DockId);
            Assert.Equal(table.DockId, preview.DockId);
            Assert.NotEqual(inspector.DockId, preview.DockId);
            Assert.True(preview.Size.X > inspector.Size.X, $"Preview {preview.Size.X} must use the broad centre, inspector {inspector.Size.X}");
            Assert.False(Workspace.Window!.Editor.Session.Dirty);
            Assert.Empty(Workspace.Project.Dirty);
            Assert.False(ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable));
        }
        public void Dispose()
        {
            Workspace.Dispose(); Host.Dispose();
            if (!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "bed-preview-space-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected preview fixture path");
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Centre_request_opens_beside_Table_in_a_fresh_or_already_built_layout(bool late)
    {
        using var rig = new Rig(register: !late);
        if (late)
        {
            rig.Workspace.Window!.Editor.Add(new Panel("demo.preview", "Source preview", () => Ui.Text("Adapter picture"u8),
                Width: 640, Height: 480, Side: PanelSide.Centre));
            rig.Host.Frames(rig.Workspace.Draw, 4);
        }
        rig.CentreTab();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Saved_right_or_floating_placement_wins_until_Reset_layout(bool floating)
    {
        string saved;
        using (var first = new Rig(PanelSide.Right))
        {
            if (floating)
                first.Host.Frames(() =>
                {
                    first.Workspace.Draw();
                    ImGui.Internal.DockContextProcessUndockWindow(ImGui.GetCurrentContext(), ImGui.Internal.FindWindowByName(Preview), true);
                    ImGui.SetWindowPos(Preview, new(870, 370), ImGuiCond.Always);
                    ImGui.SetWindowSize(Preview, new(350, 300), ImGuiCond.Always);
                }, 3);
            saved = ImGui.SaveIniSettingsToMemory().ToString()!;
        }
        using var second = new Rig(saved: saved);
        var preview = ImGui.Internal.FindWindowByName(Preview);
        Assert.NotEqual(ImGui.Internal.FindWindowByName(Table).DockId, preview.DockId);
        if (floating)
        {
            Assert.Equal(default, preview.DockId);
            Assert.InRange(preview.Pos.X, 869, 871); Assert.InRange(preview.Pos.Y, 369, 371);
        }
        else Assert.NotEqual(default, preview.DockId);
        Assert.True(second.Workspace.Window!.Run("editor.reset_layout"));
        second.Host.Frames(second.Workspace.Draw, 5);
        second.CentreTab();
    }
}