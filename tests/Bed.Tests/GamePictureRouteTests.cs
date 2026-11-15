using Bed;
using Bed.Data;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class GamePictureRouteTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void View_menu_reaches_a_real_headless_picture_and_stops_its_owned_run(bool island)
    {
        using var copy=new SpriteSampleTests.Copy(island);copy.OpenAndCompile();
        var adapter=Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded);
        using var host=new HeadlessHost(1200,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using var launcher=global::Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(copy.Settings,"recent.json"))){Loaded=[adapter]});
        Assert.True(launcher.OpenFolder(copy.Root),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("level.json"),workspace.Said);
        var saved=Directory.GetFiles(Path.Combine(copy.Root,"data"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        host.Frames(launcher.Draw,4);host.Press(launcher.Draw,ImGuiKey.V,alt:true);
        var menu=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Game picture");Assert.True(menu.Enabled);
        Assert.False(string.IsNullOrEmpty(menu.AccessKey));host.Press(launcher.Draw,Enum.Parse<ImGuiKey>(menu.AccessKey!.ToUpperInvariant()),alt:true);
        host.Frames(launcher.Draw,3);
        void Click(string label)
        {
            var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);Assert.True(button.Enabled);
            host.Click(launcher.Draw,button.Bounds.X+button.Bounds.Width/2,button.Bounds.Y+button.Bounds.Height/2);
        }
        Click("Start picture run");var session=Assert.IsType<ProjectPictureSession>(workspace.ProjectPlay);
        void Until(Func<bool> ready)
        {
            var end=Environment.TickCount64+15000;
            do{host.Frames(launcher.Draw,1);if(ready())return;Thread.Sleep(5);}while(Environment.TickCount64<end);
            Assert.True(ready(),session.Status);
        }
        Until(()=>session.Picture is not null&&!session.Busy);var first=session.Picture!;
        Assert.InRange(first.Width,1,1920);Assert.InRange(first.Height,1,1080);Assert.Equal(first.Width*first.Height*4,first.Rgba.Length);
        Assert.False(string.IsNullOrWhiteSpace(first.Description));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Game picture:")&&n.Label.Contains("pixels"));
        Click("Take picture");Until(()=>session.Picture!=first&&!session.Busy);Assert.Equal(first.Frame,session.Picture!.Frame);
        Click("Stop picture run");Until(()=>session.HasCompleted);Assert.Contains("stopped",session.Status,StringComparison.OrdinalIgnoreCase);
        foreach(var file in saved)Assert.Equal(file.Value,File.ReadAllBytes(file.Key));
    }
}
