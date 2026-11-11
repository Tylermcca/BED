using System.Runtime.InteropServices;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// The removed pre-project AI route is absent; retain real-keyboard Help/Escape/opener proof on human Getting started.
[Collection(ImGuiCollection.Name)]
public sealed class LauncherAiHelpA401Tests
{
    private const string StartWindow = "BED###launcher";
    
    private static readonly string[] CopyLabels = ["Copy bed-cli command"];
    private static string FocusWindow()
    {
        var window = ImGui.GetCurrentContext().NavWindow;
        return window.IsNull() ? "" : Marshal.PtrToStringUTF8((IntPtr)window.Name) ?? "";
    }

    private static void Reach(HeadlessHost host, Action draw, string title, string label)
    {
        var window = ImGui.Internal.FindWindowByName(title);
        Assert.False(window.IsNull(), "Missing window: " + title);
        var wanted = window.GetID(label).Value;
        for (var i = 0; i < 40; i++)
        {
            var context = ImGui.GetCurrentContext();
            if (FocusWindow() == title && context.NavId.Value == wanted) return;
            host.Press(draw, ImGuiKey.Tab);
        }
        Assert.Fail("Tab did not reach " + label + " in " + title);
    }

    private static string Text(HeadlessHost host, Action draw)
    {
        host.Frame(() => { ImGui.LogToClipboard(); draw(); ImGui.LogFinish(); });
        return ImGui.GetClipboardText().ToString() ?? "";
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Start_has_no_AI_route_and_human_help_closes_and_returns_keyboard_focus(float scale)
    {
        var root = Path.Combine(Path.GetTempPath(), "a401-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = new HeadlessHost(1600, 1000) { Drawing = false };
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json")));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            ImGui.GetStyle().FontScaleMain = scale;
            host.MoveMouse(launcher.Draw, -100, -100);
            host.Frames(launcher.Draw, 5);
            Assert.DoesNotContain("Help: Use with an AI assistant", Text(host, launcher.Draw));
            Assert.DoesNotContain(CopyLabels[0], Text(host, launcher.Draw));
            Reach(host, launcher.Draw, StartWindow, "Getting started");
            var helpId = ImGui.GetCurrentContext().NavId.Value;
            host.Press(launcher.Draw, ImGuiKey.Enter);
            Reach(host, launcher.Draw, StartWindow, "Close guide");
            Assert.Contains(GettingStartedPanel.Heading, Text(host, launcher.Draw));
            Assert.DoesNotContain(CopyLabels[0], Text(host, launcher.Draw));
            Assert.Null(launcher.Workspace);
            host.Press(launcher.Draw, ImGuiKey.Escape);
            host.Frames(launcher.Draw, 3);
            Assert.DoesNotContain(GettingStartedPanel.Heading, Text(host, launcher.Draw));
            Assert.DoesNotContain(CopyLabels[0], Text(host, launcher.Draw));
            Assert.Equal(StartWindow, FocusWindow());
            Assert.Equal(helpId, ImGui.GetCurrentContext().NavId.Value);
            Assert.Null(launcher.Workspace);
        }
        finally
        {
            var full = Path.GetFullPath(root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a401-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup boundary.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }

}
