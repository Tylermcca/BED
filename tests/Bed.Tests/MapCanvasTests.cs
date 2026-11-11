using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

/// <summary>The general flat map, on a small self-authored plan of beacons, drawn headless and driven by real mouse and keys.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class MapCanvasTests : IDisposable
{
    private const double OnePixel = 0.2;   // in metres here: the mouse lands on whole pixels
    private const string Shape = """{"type":"object"}""";
    private readonly HeadlessHost host = new(900, 700) { Drawing = false };
    private DocumentSession<DataDocument> session = null!;
    private EditorModel<DataDocument> editor = null!;
    private MapCanvas<DataDocument> canvas = null!;
    private Plan plan = null!;

    public void Dispose() => host.Dispose();

    private static MapKey Key(string id) => new("plan", "beacon", id);

    /// <summary>A plan of beacons: <c>{"beacons":[{"id","x","y","height","links":[]}]}</c>.</summary>
    private sealed class Plan : IMapSource<DataDocument>
    {
        public long FrameVersion { get; set; }
        public bool Throws { get; set; }
        public MapSnapshot Read(DataDocument d)
        {
            if (Throws) throw new InvalidOperationException("the plan is unreadable");
            var beacons = d.At("/beacons")!.Value.EnumerateArray().ToList();
            var markers = beacons.Select((b, i) => new MapMarker(Key(b.GetProperty("id"u8).GetString()!), $"/beacons/{i}", b.GetProperty("id"u8).GetString()!,
                new MapPoint(b.GetProperty("x"u8).GetDouble(), b.GetProperty("y"u8).GetDouble()), !b.TryGetProperty("fixed"u8, out _))).ToList();
            var edges = beacons.SelectMany(b => b.GetProperty("links"u8).EnumerateArray().Select(l => new MapEdge(Key(b.GetProperty("id"u8).GetString() + ">" + l.GetString()), Key(b.GetProperty("id"u8).GetString()!), Key(l.GetString()!)))).ToList();
            return new(new MapFrame("plan", FrameVersion, "metres", "x", "y"), markers, edges);
        }

        public MapEdit<DataDocument>? MoveBy(DataDocument d, MapKey key, MapPoint delta)
        {
            var i = d.At("/beacons")!.Value.EnumerateArray().ToList().FindIndex(b => b.GetProperty("id"u8).GetString() == key.Id);
            var at = $"/beacons/{i}";
            var (x, y) = (d.At(at + "/x")!.Value.GetDouble(), d.At(at + "/y")!.Value.GetDouble());
            return new([new SetValues([new SetValue(at + "/x", JsonSerializer.SerializeToElement(x + delta.X)), new SetValue(at + "/y", JsonSerializer.SerializeToElement(y + delta.Y))])], [at]);
        }
    }

    private void Open(string document, MapViewOptions? options = null)
    {
        var schemas = new JsonSchemas().Add("plan.schema.json", Shape);
        session = new(new DataFormat(), new DataValidator(schemas, "plan.schema.json"), DataKit.Catalog(), new DataDocument(JsonDocument.Parse(document).RootElement.Clone()));
        editor = new(session, new FlatOutline<DataDocument>(d => d.At("/beacons")!.Value.EnumerateArray().Select((b, i) => ($"/beacons/{i}", b.GetProperty("id"u8).GetString()!)).ToArray()));
        plan = new Plan();
        canvas = new(editor, plan, options);
        host.Frames(Draw, 3);
    }

    private const string Three = """{"beacons":[{"id":"a","x":0,"y":0,"height":7,"links":["b"]},{"id":"b","x":100,"y":0,"height":8,"links":["a"]},{"id":"c","x":0,"y":50,"height":9,"links":[]}]}""";

    private void Draw()
    {
        ImGui.SetNextWindowPos(new float2(0, 0));
        ImGui.SetNextWindowSize(new float2(900, 700));
        ImGui.Begin("plan"u8, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus);
        canvas.Draw();
        ImGui.End();
    }

    private double X(int i) => session.Current.At($"/beacons/{i}/x")!.Value.GetDouble();
    private double Y(int i) => session.Current.At($"/beacons/{i}/y")!.Value.GetDouble();

    private float2 Begin(string id, float2 by)
    {
        var start = canvas.ScreenOf(Key(id))!.Value;
        var io = ImGui.GetIO();
        io.AddMousePosEvent(start.X, start.Y);
        host.Frame(Draw);
        io.AddMouseButtonEvent(0, true);
        host.Frame(Draw);
        var to = start + by;
        io.AddMousePosEvent(to.X, to.Y);
        host.Frames(Draw, 2);
        return to;
    }

    private void LetGo()
    {
        ImGui.GetIO().AddMouseButtonEvent(0, false);
        host.Frames(Draw, 2);
    }

    [Fact]
    public void A_drag_moves_by_how_far_the_mouse_went_as_one_step_when_let_go_and_keeps_the_other_fields()
    {
        Open(Three);
        var (ax, ay) = (X(0), Y(0));
        var a = canvas.ScreenOf(Key("a"))!.Value;
        var c = canvas.ScreenOf(Key("c"))!.Value;
        var unit = (a.Y - c.Y) / 50;   // pixels per metre, from two known marks
        Begin("a", new float2(unit * 20, -unit * 10));
        Assert.True(canvas.Dragging);
        Assert.Equal(ax, X(0));   // a ghost: nothing yet
        Assert.Equal(0, session.History.UndoCount);
        LetGo();
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(ax + 20, X(0), OnePixel);
        Assert.Equal(ay + 10, Y(0), OnePixel);   // up the screen is up the plan
        Assert.Equal(7, session.Current.At("/beacons/0/height")!.Value.GetInt32());
        Assert.Equal("/beacons/0", editor.Primary);
        Assert.True(session.Undo());
        Assert.Equal(ax, X(0));
    }

    [Fact]
    public void One_unit_is_the_same_size_both_ways_unless_asked_otherwise()
    {
        Open(Three);
        var (a, b, c) = (canvas.ScreenOf(Key("a"))!.Value, canvas.ScreenOf(Key("b"))!.Value, canvas.ScreenOf(Key("c"))!.Value);
        Assert.Equal((b.X - a.X) / 100, (a.Y - c.Y) / 50, 3);

        Open(Three, new MapViewOptions(PreserveAspect: false));
        (a, b, c) = (canvas.ScreenOf(Key("a"))!.Value, canvas.ScreenOf(Key("b"))!.Value, canvas.ScreenOf(Key("c"))!.Value);
        Assert.NotEqual((b.X - a.X) / 100, (a.Y - c.Y) / 50, 1);
    }

    [Fact]
    public void The_wheel_keeps_what_is_under_the_mouse_and_the_view_holds_still_until_fit()
    {
        Open(Three);
        var b = canvas.ScreenOf(Key("b"))!.Value;
        host.MoveMouse(Draw, b.X, b.Y);
        ImGui.GetIO().AddMouseWheelEvent(0, 3);
        host.Frames(Draw, 2);
        Assert.True(canvas.Zoom > 1);
        var now = canvas.ScreenOf(Key("b"))!.Value;
        Assert.InRange(MathF.Abs(now.X - b.X) + MathF.Abs(now.Y - b.Y), 0, 1);

        // An edit doesn't reframe it: what you were looking at stays where it was.
        var c = canvas.ScreenOf(Key("c"))!.Value;
        Assert.True(session.Execute(new SetValue("/beacons/0/x", JsonSerializer.SerializeToElement(-500))));
        host.Frames(Draw, 2);
        Assert.Equal(c, canvas.ScreenOf(Key("c"))!.Value);

        canvas.Fit();
        host.Frames(Draw, 2);
        Assert.Equal(1, canvas.Zoom);
        Assert.NotEqual(c, canvas.ScreenOf(Key("c"))!.Value);
    }

    [Fact]
    public void The_right_button_pans_and_moves_nothing()
    {
        Open(Three);
        var b = canvas.ScreenOf(Key("b"))!.Value;
        host.MoveMouse(Draw, b.X, b.Y);
        ImGui.GetIO().AddMouseWheelEvent(0, 2);
        host.Frames(Draw, 2);
        var before = canvas.ScreenOf(Key("b"))!.Value;
        host.Drag(Draw, before, before + new float2(60, 30), button: 1);
        var after = canvas.ScreenOf(Key("b"))!.Value;
        Assert.True(after.X > before.X + 30, $"{before} to {after}");
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_click_selects_and_changes_nothing()
    {
        Open(Three);
        var b = canvas.ScreenOf(Key("b"))!.Value;
        host.Click(Draw, b.X, b.Y);
        host.Frames(Draw, 2);
        Assert.Equal("/beacons/1", editor.Primary);
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void Clicking_the_same_place_again_reaches_what_is_beneath()
    {
        Open("""{"beacons":[{"id":"a","x":0,"y":0,"links":[]},{"id":"b","x":0,"y":0,"links":[]},{"id":"far","x":100,"y":100,"links":[]}]}""");
        var at = canvas.ScreenOf(Key("a"))!.Value;
        Assert.Equal(["a", "b"], canvas.Hits(at).Select(m => m.Key.Id));
        host.Click(Draw, at.X, at.Y);
        host.Frames(Draw, 2);
        Assert.Equal("/beacons/0", editor.Primary);
        host.Click(Draw, at.X, at.Y);
        host.Frames(Draw, 2);
        Assert.Equal("/beacons/1", editor.Primary);
        host.Click(Draw, at.X, at.Y);
        host.Frames(Draw, 2);
        Assert.Equal("/beacons/0", editor.Primary);
    }

    [Fact]
    public void Escape_drops_a_drag_with_no_step()
    {
        Open(Three);
        Begin("a", new float2(80, 40));
        host.Press(Draw, ImGuiKey.Escape);
        Assert.False(canvas.Dragging);
        LetGo();
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal("Move cancelled.", canvas.Said);
    }

    [Fact]
    public void The_document_changing_under_a_drag_drops_it_even_when_the_thing_is_gone()
    {
        Open(Three);
        Begin("c", new float2(80, 40));
        Assert.True(session.Execute(new RemoveItem("/beacons/2")));
        LetGo();
        Assert.Contains("the map changed", canvas.Said);
        Assert.Equal(1, session.History.UndoCount);   // only the removal
        Assert.Equal(0, X(0));
    }

    [Fact]
    public void A_frame_changing_under_a_drag_drops_it()
    {
        Open(Three);
        Begin("a", new float2(80, 40));
        plan.FrameVersion++;
        host.Frames(Draw, 2);
        Assert.False(canvas.Dragging);
        LetGo();
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_release_that_went_elsewhere_drops_the_drag()
    {
        Open(Three);
        Begin("a", new float2(80, 40));
        ImGui.GetIO().AddMouseButtonEvent(0, false);
        host.Frame(() => { });
        host.Frames(Draw, 2);
        Assert.False(canvas.Dragging);
        Assert.Equal(0, session.History.UndoCount);
    }

    private sealed class Quiet : IPlaySession
    {
        public string Status => "";
        public void Draw() { }
        public void Dispose() { }
    }

    [Fact]
    public void A_game_playing_stops_a_drag_and_starts_none()
    {
        Open(Three);
        editor.Add(new PlayMode<DataDocument>("t.play", "Trial", _ => new Quiet()));
        Begin("a", new float2(80, 40));
        Assert.True(editor.Play("t.play"));
        host.Frames(Draw, 2);
        Assert.False(canvas.Dragging);
        Assert.Contains("playing", canvas.Said);
        LetGo();

        Begin("b", new float2(80, 40));
        Assert.False(canvas.Dragging);
        LetGo();
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_focus_refuses_a_move_outside_it_and_says_why()
    {
        Open(Three);
        Assert.True(editor.FocusOn("/beacons/1"));
        Begin("a", new float2(80, 40));
        LetGo();
        Assert.Equal(0, session.History.UndoCount);
        Assert.Contains("outside the place", canvas.Said);
    }

    [Fact]
    public void A_marker_that_cannot_move_is_selected_and_stays()
    {
        Open("""{"beacons":[{"id":"a","x":0,"y":0,"fixed":true,"links":[]},{"id":"b","x":100,"y":100,"links":[]}]}""");
        Begin("a", new float2(80, 40));
        Assert.False(canvas.Dragging);
        LetGo();
        Assert.Equal("/beacons/0", editor.Primary);
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_grid_snaps_a_move_and_alt_inverts_it_and_it_never_touches_anything_else()
    {
        Open(Three, new MapViewOptions(new MapGrid(new MapPoint(10, 0), new MapPoint(0, 0))));
        var a = canvas.ScreenOf(Key("a"))!.Value;
        var c = canvas.ScreenOf(Key("c"))!.Value;
        var unit = (a.Y - c.Y) / 50;
        Begin("a", new float2(unit * 23, -unit * 4));
        LetGo();
        Assert.Equal(20, X(0), 6);   // to the grid
        Assert.Equal(4, Y(0), OnePixel);    // a step of 0 leaves that axis free
        Assert.Equal(50, Y(2));      // showing it snapped nothing

        var io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.Mod_Alt, true);
        io.AddKeyEvent(ImGuiKey.LeftAlt, true);
        Begin("c", new float2(unit * 23, 0));
        LetGo();
        io.AddKeyEvent(ImGuiKey.LeftAlt, false);
        io.AddKeyEvent(ImGuiKey.Mod_Alt, false);
        Assert.Equal(23, X(2), OnePixel);   // free
    }

    [Fact]
    public void A_small_move_near_a_large_origin_keeps_its_size()
    {
        Open("""{"beacons":[{"id":"a","x":1000000000,"y":1000000000,"links":[]},{"id":"b","x":1000000010,"y":1000000010,"links":[]}]}""");
        var (a, b) = (canvas.ScreenOf(Key("a"))!.Value, canvas.ScreenOf(Key("b"))!.Value);
        var unit = (b.X - a.X) / 10;
        Assert.True(unit > 1);
        Begin("a", new float2(unit * 2, 0));
        LetGo();
        Assert.Equal(1000000002, X(0), 2);
        Assert.Equal(1000000000, Y(0), 2);
    }

    [Fact]
    public void Lines_with_a_missing_end_and_positions_that_are_not_numbers_are_listed_not_hidden()
    {
        Open("""{"beacons":[{"id":"a","x":0,"y":0,"links":["nowhere"]},{"id":"b","x":10,"y":0,"links":[]}]}""");
        Assert.Contains(canvas.Problems, p => p.Contains("a>nowhere", StringComparison.Ordinal));
    }

    [Fact]
    public void A_source_that_throws_says_so_and_the_frame_goes_on()
    {
        Open(Three);
        Begin("a", new float2(80, 40));
        plan.Throws = true;
        host.Frames(Draw, 2);
        Assert.Contains("the plan is unreadable", canvas.Said);
        Assert.False(canvas.Dragging);
        plan.Throws = false;
        LetGo();
        Assert.Equal(0, session.History.UndoCount);
        Assert.NotNull(canvas.ScreenOf(Key("a")));
    }

    [Fact]
    public void ExecuteAt_refuses_an_old_revision_and_an_object_it_cannot_find()
    {
        Open(Three);
        var revision = session.Revision;
        var move = new ICommand<DataDocument>[] { new SetValue("/beacons/0/x", JsonSerializer.SerializeToElement(1)) };
        Assert.False(editor.ExecuteAt(revision, move, "/beacons/99"));
        Assert.Contains(editor.Refused, i => i.Message.Contains("/beacons/99", StringComparison.Ordinal));

        Assert.True(session.Execute(new SetValue("/beacons/1/x", JsonSerializer.SerializeToElement(2))));
        Assert.False(editor.ExecuteAt(revision, move, "/beacons/0"));
        Assert.Contains(editor.Refused, i => i.Message.Contains("changed", StringComparison.Ordinal));
        Assert.Equal(0, X(0));

        Assert.True(editor.ExecuteAt(session.Revision, move, "/beacons/0"));
        Assert.Equal(1, X(0));
        Assert.Empty(editor.Refused);
    }
}
