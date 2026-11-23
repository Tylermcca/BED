using System.Numerics;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using LightsPlugin;
using Xunit;

namespace Bed.Tests;

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
        using var host = new HeadlessHost(1280, 1100);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
            window.Draw();
            ImGui.SetWindowSize("Untitled###editor", new Brutal.Numerics.float2(1100, 950), ImGuiCond.Always);
        }
        host.Frames(Draw);
        Assert.Empty(editor.Selection);
        var target = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "crate");
        Assert.True(target.Enabled);
        Assert.False(target.Bounds.IsEmpty);
        host.Click(Draw, target.Bounds.X + target.Bounds.Width / 2, target.Bounds.Y + target.Bounds.Height / 2);
        Assert.Single(editor.Selection);
        Assert.Equal("crate", editor.Primary);
        host.Frames(Draw, 2);
        var showCode = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Button && n.Label == "Show object code");
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Label.StartsWith("Object code:", StringComparison.Ordinal));
        Assert.False(showCode.Bounds.IsEmpty);
        host.Click(Draw, showCode.Bounds.X + showCode.Bounds.Width / 2, showCode.Bounds.Y + showCode.Bounds.Height / 2);
        host.Frames(Draw, 2);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "Object code: crate");
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
    public void Releasing_a_context_leaves_other_contexts_alone_and_allows_a_fresh_upload()
    {
        var previous = ImGui.GetCurrentContext();
        var first = ImGui.CreateContext();
        ImGui.SetCurrentContext(first);
        var firstPicture = new PictureTexture();
        firstPicture.Update(8, 8, new byte[8 * 8 * 4], 1);
        var second = ImGui.CreateContext();
        ImGui.SetCurrentContext(second);
        var secondPicture = new PictureTexture();
        try
        {
            secondPicture.Update(8, 8, new byte[8 * 8 * 4], 1);
            PictureTexture.ReleaseContext();PictureTexture.ReleaseContext();
            Assert.Equal(8, firstPicture.Width);Assert.Equal(0, secondPicture.Width);
            secondPicture.Update(8, 8, new byte[8 * 8 * 4], 1);
            Assert.Equal(8, secondPicture.Width);
        }
        finally
        {
            ImGui.SetCurrentContext(second);PictureTexture.ReleaseContext();ImGui.DestroyContext(second);
            ImGui.SetCurrentContext(first);PictureTexture.ReleaseContext();ImGui.DestroyContext(first);
            ImGui.SetCurrentContext(previous);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Closing_twelve_hosts_releases_live_and_retired_picture_pixels(bool drawing)
    {
        var waiting = PictureTexture.Waiting;
        var held = new List<PictureTexture>();
        var rgba = new byte[512 * 512 * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        { rgba[i] = 37; rgba[i + 1] = 148; rgba[i + 2] = 211; rgba[i + 3] = 255; }
        for (var run = 0; run < 12; run++)
        {
            var live = new PictureTexture();
            using (var host = new HeadlessHost(64, 64) { Drawing = drawing })
            {
                var retired = new PictureTexture();
                host.Frame(() =>
                {
                    live.Update(512, 512, rgba, 1);
                    retired.Update(512, 512, rgba, 1);
                    ImGui.GetBackgroundDrawList().AddImage(live.Ref, new(0, 0), new(64, 64));
                    ImGui.GetBackgroundDrawList().AddImage(retired.Ref, new(0, 0), new(16, 16));
                });
                Assert.Equal(ImTextureStatus.OK, live.State.Status);
                if (drawing) Assert.Equal(0xffd39425u, host.Pixels[32 * 64 + 32]);
                retired.Dispose();
                Assert.Equal(waiting + 1, PictureTexture.Waiting);
                held.Add(live);
                var snapshot = Ui.Snapshot();
                host.Dispose();host.Dispose();
                Assert.Equal(snapshot.Json, Ui.Snapshot().Json);   // closing must not run extra UI frames
            }
            Assert.All(held, texture => Assert.Equal((0, 0), (texture.Width, texture.Height)));
            Assert.Equal(waiting, PictureTexture.Waiting);
        }
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
