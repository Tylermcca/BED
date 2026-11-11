using Bed.Editor;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class MenuShortcutTooltipTests
{
    [Theory]
    [InlineData("Ctrl+S")][InlineData("Ctrl+Alt+S")]
    public void Real_menu_exposes_current_shortcut_before_hover_and_preserves_click_route(string chord)
    {
        var root=Directory.CreateTempSubdirectory("menu-tooltip-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"""{"type":"object","properties":{"value":{"type":"integer"}}}""");
            var path=Path.Combine(data,"item.json");const string original="""{"value":1}""";File.WriteAllText(path,original);
            using var host=new HeadlessHost(1200,800){Drawing=false};
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.Show("item.json"));
            var window=workspace.Window!;window.Shortcuts.Set("editor.save",chord);
            var calls=0;window.SaveAction=()=>{calls++;return(true,"Save callback observed");};
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());host.Frames(workspace.Draw,4);
            UiNode Node(string label,UiRole role)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role==role);
            var file=Node("File",UiRole.Menu).Bounds;host.Click(workspace.Draw,file.X+file.Width/2,file.Y+file.Height/2);
            host.MoveMouse(workspace.Draw,1100,750);host.Frames(workspace.Draw,2);
            var save=Node("Save",UiRole.MenuItem);Assert.True(save.Enabled);Assert.False(save.Hovered);
            Assert.Equal($"Save ({Chord.Parse(chord)})",save.Tooltip);
            var missing=Node("Recent projects",UiRole.MenuItem);Assert.False(missing.Enabled); // UI25 made Open file... work; this host still has no recent-project list
            Assert.Equal(missing.DisabledReason,missing.Tooltip);Assert.False(string.IsNullOrWhiteSpace(missing.Tooltip));
            Assert.Equal(0,calls);Assert.Equal(original,File.ReadAllText(path));
            host.Click(workspace.Draw,save.Bounds.X+save.Bounds.Width/2,save.Bounds.Y+save.Bounds.Height/2);
            Assert.Equal(1,calls);Assert.Equal("Save callback observed",window.Status);
            Assert.Equal(original,File.ReadAllText(path));Assert.Empty(window.Editor.Session.Journal);
        }
        finally {Directory.Delete(root,true);}
    }
}
