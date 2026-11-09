using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The map and the 3D viewport as keyboard controls (found by A217): Tab reaches each, and its main task, picking
/// and moving, is done with keys alone. Self-authored fixtures, driven headless by real key events.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class CanvasKeyboardTests : IDisposable
{
    private readonly HeadlessHost host = new(900, 700) { Drawing = false };

    public void Dispose() => host.Dispose();

    private static uint NavId => ImGui.GetCurrentContext().NavId.Value;

    private void TabTo(Action draw, string label)
    {
        var wanted = ImGui.Internal.FindWindowByName("Canvas keys").GetID(label).Value;
        for (var i = 0; i < 12 && NavId != wanted; i++) host.Press(draw, ImGuiKey.Tab);
        Assert.Equal(wanted, NavId);
        // The focus cursor shows: the nav cursor's colour drawn round the canvas.
        host.Drawing = true;
        host.Frame(draw);
        host.Drawing = false;
        var colour = ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)ImGuiCol.NavCursor]) & 0xffffff;
        Assert.True(host.Pixels.Count(p => (p & 0xffffff) == colour) > 400, "no focus cursor drawn on the canvas");
    }

    private sealed class Dots : IMapSource<DataDocument>
    {
        public MapSnapshot Read(DataDocument d) => new(new MapFrame("dots", 0, "m", "x", "y"),
            [.. d.At("/dots")!.Value.EnumerateArray().Select((p, i) => new MapMarker(new("dots", "dot", $"{i}"), $"/dots/{i}", $"dot {i}",
                new MapPoint(p.GetProperty("x"u8).GetDouble(), p.GetProperty("y"u8).GetDouble())))], []);

        public MapEdit<DataDocument>? MoveBy(DataDocument d, MapKey key, MapPoint by)
        {
            var at = $"/dots/{key.Id}";
            var (x, y) = (d.At(at + "/x")!.Value.GetDouble(), d.At(at + "/y")!.Value.GetDouble());
            return new([new SetValues([new SetValue(at + "/x", JsonSerializer.SerializeToElement(x + by.X)), new SetValue(at + "/y", JsonSerializer.SerializeToElement(y + by.Y))])], [at]);
        }
    }

    [Fact]
    public void The_map_takes_Tab_then_Enter_selects_arrows_move_a_grid_step_and_plus_zooms()
    {
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var session = new DocumentSession<DataDocument>(new DataFormat(), new DataValidator(new JsonSchemas().Add("s", """{"type":"object"}"""), "s"), DataKit.Catalog(),
            new DataDocument(JsonDocument.Parse("""{"dots":[{"x":0,"y":0},{"x":100,"y":50}]}""").RootElement.Clone()));
        var editor = new EditorModel<DataDocument>(session, new FlatOutline<DataDocument>(_ => [("/dots/0", "dot 0"), ("/dots/1", "dot 1")]));
        var map = new MapCanvas<DataDocument>(editor, new Dots(), new MapViewOptions(new MapGrid(new MapPoint(10, 10), new MapPoint(0, 0))));
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0));
            ImGui.SetNextWindowSize(new float2(900, 700));
            ImGui.Begin("Canvas keys"u8);
            ImGui.Button("Before"u8);
            map.Draw(450);
            ImGui.Button("After"u8);
            ImGui.End();
        }
        host.Frames(Draw, 3);
        TabTo(Draw, "##map");
        var focus = NavId;

        host.Press(Draw, ImGuiKey.Enter);
        Assert.Equal("/dots/0", editor.Primary);
        host.Press(Draw, ImGuiKey.Enter, shift: true);
        Assert.Equal("/dots/1", editor.Primary);   // Shift+Enter steps back, round the end
        host.Press(Draw, ImGuiKey.Enter);
        Assert.Equal("/dots/0", editor.Primary);

        host.Press(Draw, ImGuiKey.RightArrow);
        host.Press(Draw, ImGuiKey.UpArrow);
        Assert.Equal(10, session.Current.At("/dots/0/x")!.Value.GetDouble());
        Assert.Equal(10, session.Current.At("/dots/0/y")!.Value.GetDouble());   // up the screen is up the map
        Assert.Equal(2, session.History.UndoCount);   // one step a press
        Assert.Equal(focus, NavId);   // the arrows are the map's: focus stays on it

        host.Press(Draw, ImGuiKey.Equal);
        Assert.True(map.Zoom > 1);
        host.Press(Draw, ImGuiKey.RightArrow, ctrl: true);   // Ctrl pans: nothing moves
        Assert.Equal(10, session.Current.At("/dots/0/x")!.Value.GetDouble());
        host.Press(Draw, ImGuiKey._0);
        Assert.Equal(1, map.Zoom);
        Assert.Equal(focus, NavId);
    }

    [Fact]
    public void The_viewport_takes_Tab_then_Enter_picks_arrows_move_a_grid_cell_and_plus_minus_zoom()
    {
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        var scene = new Scene([SceneObject.At("crate", null, new System.Numerics.Vector3(0, 0.5f, 0)), SceneObject.At("barrel", null, new System.Numerics.Vector3(3, 0.5f, 2))]);
        var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline);
        var view = new ViewportView<Scene>(editor, new SceneViewport()) { Camera = new Camera(System.Numerics.Vector3.Zero, 0, MathF.PI / 6, 12) };   // facing +Z
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0));
            ImGui.SetNextWindowSize(new float2(900, 700));
            ImGui.Begin("Canvas keys"u8);
            ImGui.Button("Before"u8);
            view.Draw();
            ImGui.End();
        }
        host.Frames(Draw, 3);
        TabTo(Draw, "##viewport");
        var focus = NavId;

        var camera = view.Camera;
        host.Press(Draw, ImGuiKey.LeftArrow);   // nothing picked: the arrows orbit
        Assert.NotEqual(camera.Yaw, view.Camera.Yaw);
        view.Camera = camera;

        host.Press(Draw, ImGuiKey.Enter);
        Assert.Equal("crate", editor.Primary);
        host.Press(Draw, ImGuiKey.Enter);
        Assert.Equal("barrel", editor.Primary);
        host.Press(Draw, ImGuiKey.Enter, shift: true);
        Assert.Equal("crate", editor.Primary);

        host.Press(Draw, ImGuiKey.RightArrow);   // camera's right is +X
        host.Press(Draw, ImGuiKey.UpArrow);      // its facing is +Z
        var at = SceneViewport.World(editor.Session.Current, "crate").Translation;
        Assert.Equal(new System.Numerics.Vector3(0.5f, 0.5f, 0.5f), at);
        Assert.Equal(2, editor.Session.History.UndoCount);
        Assert.Equal(focus, NavId);

        host.Press(Draw, ImGuiKey.RightArrow, ctrl: true);   // Ctrl orbits: nothing moves
        Assert.Equal(at, SceneViewport.World(editor.Session.Current, "crate").Translation);
        var distance = view.Camera.Distance;
        host.Press(Draw, ImGuiKey.Equal);
        Assert.True(view.Camera.Distance < distance);
        host.Press(Draw, ImGuiKey.Minus);
        Assert.Equal(distance, view.Camera.Distance, 3);
        Assert.Equal(focus, NavId);

        view.Grid = Grid.Uniform(1);
        host.Press(Draw, ImGuiKey.RightArrow, alt: true);   // Alt frees it from the 1 m grid, as a drag: half a metre
        Assert.Equal(1f, SceneViewport.World(editor.Session.Current, "crate").Translation.X, 4);
        host.Press(Draw, ImGuiKey.RightArrow);   // and back on the grid
        Assert.Equal(2f, SceneViewport.World(editor.Session.Current, "crate").Translation.X, 4);
    }

    // No keyboard trap: Tab goes on from a focused canvas to the next control, and Escape clears the focus as ImGui's own
    // setting does (EditorWindow.Configure leaves Escape to the editor; here ImGui's default is put back for the press).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tab_leaves_a_focused_canvas_and_Escape_still_clears_its_focus(bool viewport)
    {
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var session = new DocumentSession<DataDocument>(new DataFormat(), new DataValidator(new JsonSchemas().Add("s", """{"type":"object"}"""), "s"), DataKit.Catalog(),
            new DataDocument(JsonDocument.Parse("""{"dots":[{"x":0,"y":0}]}""").RootElement.Clone()));
        var map = new MapCanvas<DataDocument>(new EditorModel<DataDocument>(session, new FlatOutline<DataDocument>(_ => [("/dots/0", "dot 0")])), new Dots());
        var scene = new Scene([SceneObject.At("crate", null, System.Numerics.Vector3.Zero)]);
        var view = new ViewportView<Scene>(new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline), new SceneViewport());
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0));
            ImGui.SetNextWindowSize(new float2(900, 700));
            ImGui.Begin("Canvas keys"u8);
            ImGui.Button("Before"u8);
            if (viewport) view.Draw(); else map.Draw(450);
            ImGui.Button("After"u8);
            ImGui.End();
        }
        host.Frames(Draw, 3);
        var label = viewport ? "##viewport" : "##map";
        TabTo(Draw, label);
        host.Press(Draw, ImGuiKey.Enter);   // selected something, so the arrows are the canvas's
        if (viewport)
        {
            host.Press(Draw, ImGuiKey.Tab);
            Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID("Move (G)").Value, NavId);
            host.Press(Draw, ImGuiKey.Tab);
            Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID("Rotate (R)").Value, NavId);
            host.Press(Draw, ImGuiKey.Tab);
            Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID("Scale (S)").Value, NavId);
        }
        host.Press(Draw, ImGuiKey.Tab);
        Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID("After").Value, NavId);
        if (viewport)
        {
            host.Press(Draw, ImGuiKey.Tab, shift: true);
            Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID("Scale (S)").Value, NavId);
            host.Press(Draw, ImGuiKey.Tab, shift: true);
            Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID("Rotate (R)").Value, NavId);
            host.Press(Draw, ImGuiKey.Tab, shift: true);
            Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID("Move (G)").Value, NavId);
        }
        host.Press(Draw, ImGuiKey.Tab, shift: true);
        Assert.Equal(ImGui.Internal.FindWindowByName("Canvas keys").GetID(label).Value, NavId);

        var io = ImGui.GetIO();
        io.ConfigNavEscapeClearFocusItem = true;
        try
        {
            host.Press(Draw, ImGuiKey.Escape);
            Assert.NotEqual(ImGui.Internal.FindWindowByName("Canvas keys").GetID(label).Value, NavId);
        }
        finally { io.ConfigNavEscapeClearFocusItem = false; }
    }
}
