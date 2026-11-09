using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using LightsPlugin;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The editor window driven like a person would drive it, with no window: scripted clicks and keys, and frames
/// drawn on the CPU. Set BRUTAL_AUTHORING_SHOTS to a folder to keep the PNGs.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class HeadlessHostTests
{
    private static readonly string? Shots = Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS");

    private static void Shot(HeadlessHost host, string name)
    {
        if (Shots is { Length: > 0 } dir) host.Save(Path.Combine(Directory.CreateDirectory(dir).FullName, name + ".png"));
    }

    private static (EditorModel<Scene> Editor, EditorWindow<Scene> Window) Scene()
    {
        var room = new Scene([
            SceneObject.At("room", null, Vector3.Zero),
            SceneObject.At("table", "props/table.glb", new Vector3(1, 0, 2)) with { Parent = "room" },
            SceneObject.At("crate", "props/crate.glb", new Vector3(9, 0, 9)),
        ]);
        var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), room), SceneEditing.Outline);
        var inspector = new Inspector<Scene>(editor).Add(SceneEditing.ObjectType());
        var shortcuts = new Shortcuts();
        var window = new EditorWindow<Scene>(editor, inspector, shortcuts, SceneEditing.CommandFor);
        Assert.True(new PluginHost<Scene>(editor, inspector, shortcuts).Add(new Lights()));
        return (editor, window);
    }

    [Fact]
    public void FramesAreDrawnWithTextAndPanels()
    {
        var (_, window) = Scene();
        using var host = new HeadlessHost(1280, 800);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
            window.Draw();
        }
        host.Frames(Draw);
        Shot(host, "01_editor");
        Assert.True(host.Covered() > 1280 * 800 / 10, "the editor should cover a good part of the frame");
        Assert.True(host.Covered(10, 10, 910, 40) > 200, "the title bar and toolbar should be drawn");
        var png = Png.Encode(host.Width, host.Height, host.Pixels);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [Fact]
    public void ClickingAnObjectInTheOutlinerSelectsIt()
    {
        var (editor, window) = Scene();
        using var host = new HeadlessHost(1280, 800);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
            window.Draw();
        }
        host.Frames(Draw);
        Assert.Empty(editor.Selection);
        var target = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "crate");
        Assert.True(target.Enabled);
        Assert.False(target.Bounds.IsEmpty);
        host.Click(Draw, target.Bounds.X + target.Bounds.Width / 2, target.Bounds.Y + target.Bounds.Height / 2);
        Assert.Single(editor.Selection);
        Assert.Equal("crate", editor.Primary);
        Shot(host, "02_selected");
    }

    [Fact]
    public void TheCommandPaletteOpensFromTheKeyboardAndTypingFilters()
    {
        var (_, window) = Scene();
        using var host = new HeadlessHost(1280, 800);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
            window.Draw();
        }
        host.Frames(Draw);
        Assert.True(window.HandleKey(Chord.Parse("Ctrl+Shift+P")));
        host.Frames(Draw, 2);
        Assert.True(window.PaletteOpen);
        host.Type(Draw, "light");
        host.Frames(Draw, 2);
        Shot(host, "03_palette");
        Assert.True(window.PaletteOpen);
    }

    [Fact]
    public void The_clipboard_is_the_hosts_own_and_starts_empty()
    {
        using (var host = new HeadlessHost(64, 64))
        {
            Assert.Equal("", ImGui.GetClipboardText().ToString() ?? "");   // never the person's clipboard
            ImGui.SetClipboardText("copied in a test");
            Assert.Equal("copied in a test", ImGui.GetClipboardText().ToString());
        }
        using var next = new HeadlessHost(64, 64);
        Assert.Equal("", ImGui.GetClipboardText().ToString() ?? "");
    }
}
