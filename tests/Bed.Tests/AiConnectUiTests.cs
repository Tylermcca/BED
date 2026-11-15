using Brutal.ImGuiApi.Internal;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class AiConnectUiTests
{
    [Fact]
    public void Opening_and_tabbing_only_preview_then_Enter_on_Connect_writes_to_fake_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "bed-connect-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "bed-cli.exe"), "not executed");
            var locations = new AiConnectionLocations(Path.Combine(root, "profile"), Path.Combine(root, "roaming"));
            var panel = new AiConnectPanel(AiSetupSnippets.Create(root), null, locations);
            using var host = new HeadlessHost(1200, 900) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            void Draw()
            {
                ImGui.SetNextWindowPos(new float2(0, 0), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new float2(760, 750), ImGuiCond.Always);
                if (ImGui.Begin("Connect proof", ImGuiWindowFlags.NoSavedSettings)) panel.Draw();
                ImGui.End();
            }
            host.Frames(Draw, 5);
            var window = ImGui.Internal.FindWindowByName("Connect proof");
            var wanted = window.GetID("Connect").Value;
            for (var i = 0; i < 25 && ImGui.GetCurrentContext().NavId.Value != wanted; i++) host.Press(Draw, ImGuiKey.Tab);
            Assert.False(Directory.Exists(locations.AppData));
            Assert.Equal(wanted, ImGui.GetCurrentContext().NavId.Value);
            host.Press(Draw, ImGuiKey.Enter);
            Assert.True(File.Exists(locations.Targets[0].File));
        }
        finally { Directory.Delete(root, true); }
    }
}
