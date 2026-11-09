using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// A flat map over an editor model: markers and lines, from any game's data, through <see cref="IMapSource{TDoc}"/>. The wheel
/// zooms where the mouse is, and the right button pans. Click a marker to select it; click again in the same place to reach
/// one under it. Drag it to move it: a ghost follows and nothing changes until it's let go, as one undo step. Escape drops a
/// drag, and so do the document changing under it, the frame changing, a game starting, and losing the mouse. The view holds
/// still until <see cref="Fit"/> is asked for. MIXED: X4 Sector Creator and Galaxy Forge's draggable maps, the command-only
/// edits of these tools.
/// <para>It is also a keyboard control: Tab reaches it and shows the focus cursor round it. Then Enter selects the next
/// marker (Shift+Enter the one before), the arrows move the selected marker one grid step (or ten pixels' worth with no
/// grid; Alt inverts snapping, as when dragging), one undo step a press, Ctrl and the arrows pan, + and - zoom about the
/// middle, and 0 fits. With nothing selected, the arrows pan. BORROWED: arrow nudging from 2D editors; INVENTED: Enter to
/// step through the markers.</para>
/// </summary>
/// <typeparam name="TDoc">The document type.</typeparam>
public sealed class MapCanvas<TDoc> where TDoc : class
{
    private sealed record Gesture(MapKey Key, long Revision, string FrameId, long FrameVersion, MapPoint Start, float2 PressedAt);

    private const float HitPixels = 9, MarginPixels = 28, MovedPixels = 3;
    private static readonly ImColor8 LineColour = new(120, 130, 150, 255), DotColour = new(90, 170, 240, 255), SelectedColour = new(250, 210, 90, 255),
        GhostColour = new(250, 210, 90, 150), TextColour = new(235, 235, 235, 255), BackColour = new(18, 22, 30, 255);
    private readonly EditorModel<TDoc> editor;
    private readonly IMapSource<TDoc> source;
    private MapSnapshot? shown;
    private float2 origin, size;
    private (MapPoint Low, MapPoint High)? bounds;
    private string? fittedFrame;
    private float zoom = 1;
    private float2 pan;
    private Gesture? gesture;
    private float2 ghost;
    private (float2 At, int Next)? lastPress;

    /// <summary>A map of <paramref name="source"/>'s data in <paramref name="editor"/>'s document.</summary>
    public MapCanvas(EditorModel<TDoc> editor, IMapSource<TDoc> source, MapViewOptions? options = null)
    {
        this.editor = editor;
        this.source = source;
        Options = options ?? new MapViewOptions();
    }

    /// <summary>How it's shown.</summary>
    public MapViewOptions Options { get; set; }

    /// <summary>How far in the view is: 1 shows everything.</summary>
    public float Zoom => zoom;

    /// <summary>Why the last change was refused, or what it did. A game's own buttons beside the map say theirs here too.</summary>
    public string Said { get; set; } = "";

    /// <summary>What's wrong with what the source supplied, as last drawn: lines with an end missing, or positions that aren't numbers.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>Whether a drag is under way.</summary>
    public bool Dragging => gesture is not null;

    /// <summary>Shows everything again: where zoom is 1, the way it began.</summary>
    public void Fit()
    {
        bounds = null;
        (zoom, pan) = (1, default);
    }

    /// <summary>Where a marker was last drawn, in screen pixels, or null when it wasn't.</summary>
    public float2? ScreenOf(MapKey key) =>
        bounds is null || shown?.Markers.FirstOrDefault(m => m.Key == key) is not { } marker || !marker.Position.IsFinite ? null : ToScreen(marker.Position);

    /// <summary>What's within reach of a point in screen pixels, nearest first, and among equals in the key's order, so the
    /// same place gives the same order each time.</summary>
    public IReadOnlyList<MapMarker> Hits(float2 pixels)
    {
        if (bounds is null || shown is null) return [];
        return [.. shown.Markers.Where(m => m.Position.IsFinite).Select(m => (m, d: Distance(ToScreen(m.Position), pixels))).Where(x => x.d <= HitPixels)
            .OrderBy(x => x.d).ThenBy(x => x.m.Key.Scope, StringComparer.Ordinal).ThenBy(x => x.m.Key.Kind, StringComparer.Ordinal).ThenBy(x => x.m.Key.Id, StringComparer.Ordinal).Select(x => x.m)];
    }

    /// <summary>This map as a window panel, with <paramref name="below"/> drawn under it for a game's own buttons, in
    /// <paramref name="belowHeight"/> pixels.</summary>
    public Panel AsPanel(string id, string title, Action? below = null, float belowHeight = 0) => new(id, title, () =>
    {
        Draw(MathF.Max(ImGui.GetContentRegionAvail().Y - belowHeight, 64));
        below?.Invoke();
    }, Width: 700, Height: 600);

    private float2 Span
    {
        get
        {
            var (low, high) = bounds!.Value;
            return new float2((float)(high.X - low.X), (float)(high.Y - low.Y));
        }
    }

    private (float X, float Y) Scale
    {
        get
        {
            var span = Span;
            var (x, y) = ((size.X - 2 * MarginPixels) / span.X, (size.Y - 2 * MarginPixels) / span.Y);
            return Options.PreserveAspect ? (MathF.Min(x, y), MathF.Min(x, y)) : (x, y);
        }
    }

    // Measured from the low corner in doubles, then narrowed: a small move near a large origin keeps its size.
    private float2 ToScreen(MapPoint at)
    {
        var low = bounds!.Value.Low;
        var (sx, sy) = Scale;
        var span = Span;
        var centre = origin + size * 0.5f;
        var whole = centre + new float2((float)(at.X - low.X - span.X * 0.5f) * sx, -(float)(at.Y - low.Y - span.Y * 0.5f) * sy);   // up the screen is up the frame
        return centre + (whole - centre) * zoom + pan;
    }

    /// <summary>A movement in screen pixels, in the frame's units.</summary>
    private MapPoint ToFrame(float2 pixels)
    {
        var (sx, sy) = Scale;
        return new MapPoint(pixels.X / (double)(sx * zoom), -pixels.Y / (double)(sy * zoom));
    }

    private MapSnapshot? Read()
    {
        try { return source.Read(editor.Session.Current); }
        catch (Exception e)   // a game's code: the frame goes on, and it says why
        {
            Said = $"The map couldn't be read: {e.Message}";
            return null;
        }
    }

    private void FitNow(MapSnapshot snapshot)
    {
        var known = snapshot.Markers.Where(m => m.Position.IsFinite).Select(m => m.Position).ToList();
        if (known.Count == 0) { bounds = (new MapPoint(-1, -1), new MapPoint(1, 1)); return; }
        var (lowX, highX, lowY, highY) = (known.Min(p => p.X), known.Max(p => p.X), known.Min(p => p.Y), known.Max(p => p.Y));
        var padX = Math.Max((highX - lowX) * 0.05, highX > lowX ? 0 : 1);
        var padY = Math.Max((highY - lowY) * 0.05, highY > lowY ? 0 : 1);
        bounds = (new MapPoint(lowX - padX, lowY - padY), new MapPoint(highX + padX, highY + padY));
    }

    private static List<string> Diagnose(MapSnapshot snapshot)
    {
        var problems = new List<string>();
        var known = snapshot.Markers.Select(m => m.Key).ToHashSet();
        foreach (var m in snapshot.Markers.Where(m => !m.Position.IsFinite)) problems.Add($"{m.Label} has a position that isn't a number, so it isn't shown.");
        foreach (var twice in snapshot.Markers.GroupBy(m => m.Key).Where(g => g.Count() > 1)) problems.Add($"{twice.Key.Id} is on the map {twice.Count()} times, so none of them can be moved.");
        foreach (var e in snapshot.Edges)
            if (e.Problem is { } problem) problems.Add(problem);
            else if (e.From is not { } from || e.To is not { } to || !known.Contains(from) || !known.Contains(to)) problems.Add($"The line {e.Key.Id} has an end that isn't on the map.");
        return problems;
    }

    // Why nothing here can be moved, or null: found by A090, where these were silently free or gave NaN pixels.
    private string? Unusable(MapSnapshot snapshot)
    {
        var known = snapshot.Markers.Where(m => m.Position.IsFinite).Select(m => m.Position).ToList();
        if (known.Count > 0 && (!double.IsFinite(known.Max(p => p.X) - known.Min(p => p.X)) || !double.IsFinite(known.Max(p => p.Y) - known.Min(p => p.Y))))
            return "The positions are too far apart to show.";
        if (Options.Grid is { } grid && !(double.IsFinite(grid.Step.X) && double.IsFinite(grid.Step.Y) && grid.Step.X >= 0 && grid.Step.Y >= 0 && grid.Origin.IsFinite))
            return "The grid's step must be a number, 0 or more, and its origin a number, so nothing can be moved.";
        return null;
    }

    /// <summary>Why a drag under way can't finish, or null.</summary>
    private string? Lost(MapSnapshot snapshot)
    {
        var g = gesture!;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) return "Move cancelled.";
        if (editor.Playing is not null) return "Move cancelled: a game is playing.";
        if (editor.Session.Revision != g.Revision || snapshot.Frame.Id != g.FrameId || snapshot.Frame.Version != g.FrameVersion
            || !snapshot.Markers.Any(m => m.Key == g.Key && m.Movable))   // found by A090: one that had stopped being movable still moved
            return "Move cancelled: the map changed while dragging.";
        return !ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseReleased(ImGuiMouseButton.Left) ? "Move cancelled." : null;   // the release went elsewhere
    }

    private static double Snapped(double value, double step, double from) => step > 0 && double.IsFinite(step) ? from + Math.Round((value - from) / step) * step : value;

    private void Release(float2 mouse)
    {
        var g = gesture!;
        gesture = null;
        if (Distance(mouse, g.PressedAt) <= MovedPixels) return;   // a click isn't a move
        var moved = ToFrame(mouse - g.PressedAt);
        var to = new MapPoint(g.Start.X + moved.X, g.Start.Y + moved.Y);
        if (SnapGrid() is { } grid) to = new MapPoint(Snapped(to.X, grid.Step.X, grid.Origin.X), Snapped(to.Y, grid.Step.Y, grid.Origin.Y));
        TryMove(g.Key, g.Start, to, g.Revision);
    }

    // The grid a move snaps to, or null when it moves freely: Alt inverts the grid's setting, for a drag or an arrow.
    private MapGrid? SnapGrid() => Options.Grid is { } grid && grid.Enabled != ImGui.GetIO().KeyAlt ? grid : null;

    // One move of a marker from start to to, as one undo step made at the given revision, saying what happened: the drag's
    // release and an arrow key both come here. True when it moved.
    private bool TryMove(MapKey key, MapPoint start, MapPoint to, long revision)
    {
        var delta = new MapPoint(to.X - start.X, to.Y - start.Y);
        if (delta is { X: 0, Y: 0 }) return false;
        if (!delta.IsFinite) { Said = "That move isn't a number the map can use."; return false; }
        MapEdit<TDoc>? edit;
        try { edit = source.MoveBy(editor.Session.Current, key, delta); }
        catch (Exception e) { Said = $"That move couldn't be made: {e.Message}"; return false; }
        if (edit is null) { Said = "That can't be moved."; return false; }
        var ok = editor.ExecuteAt(revision, edit.Commands, [.. edit.Touches]);
        var why = editor.Refused.Count > 0 ? editor.Refused : editor.Session.LastIssues;
        Said = ok ? "Moved." : string.Join(" ", why.Select(i => i.Message).DefaultIfEmpty("That move was refused."));
        return ok;
    }

    /// <summary>Draws the map into the current window, <paramref name="height"/> pixels tall or filling what's left, and handles its input.</summary>
    public void Draw(float? height = null)
    {
        var room = ImGui.GetContentRegionAvail();
        origin = ImGui.GetCursorScreenPos();
        size = new float2(MathF.Max(room.X, 64), MathF.Max(height ?? room.Y, 64));
        ImGui.InvisibleButton("##map"u8, size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.EnableNav);
        var treeCanvas = Ui.RecordCanvas("Map", editor.Playing is null, "a game is playing");
        var hovered = ImGui.IsItemHovered();
        var focused = ImGui.IsItemFocused();
        var pressed = ImGui.IsItemActivated();
        var rightDrag = ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Right);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, BackColour);
        if (Read() is not { } snapshot)
        {
            (shown, gesture) = (null, null);
            return;
        }
        var unusable = Unusable(snapshot);
        Problems = unusable is null ? Diagnose(snapshot) : [.. Diagnose(snapshot), unusable];
        if (unusable?.StartsWith("The positions", StringComparison.Ordinal) == true)   // no scale can show them
        {
            (shown, gesture) = (null, null);
            bounds = null;
            return;
        }
        shown = snapshot;
        var frameKey = $"{snapshot.Frame.Id}\n{snapshot.Frame.Version}";
        if (bounds is null || fittedFrame != frameKey)
        {
            FitNow(snapshot);
            fittedFrame = frameKey;
        }

        var mouse = ImGui.GetMousePos();
        var wheel = hovered && gesture is null ? ImGui.GetIO().MouseWheel : 0;
        if (wheel != 0)   // what's under the mouse stays under it
        {
            var was = zoom;
            zoom = Math.Clamp(zoom * MathF.Pow(1.25f, wheel), 1, 1000);
            var centre = origin + size * 0.5f;
            pan = mouse - centre - (mouse - centre - pan) * (zoom / was);
        }
        if (rightDrag && gesture is null) pan += ImGui.GetIO().MouseDelta;
        var keyed = focused && gesture is null && Keyboard(snapshot, unusable);
        if (zoom <= 1) pan = default;

        var under = hovered ? Hits(mouse) : [];
        if (pressed && under.Count > 0 && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var again = lastPress is { } last && Distance(last.At, mouse) <= 2;
            var next = again ? lastPress!.Value.Next % under.Count : 0;   // the same place again reaches the one beneath
            var chosen = under[next];
            lastPress = (mouse, next + 1);
            editor.Select(chosen.OutlineId);
            if (unusable is not null) Said = unusable;   // what's stopping it, at the moment it's tried
            if (chosen.Movable && editor.Playing is null && unusable is null && snapshot.Markers.Count(m => m.Key == chosen.Key) == 1)
            {
                gesture = new Gesture(chosen.Key, editor.Session.Revision, snapshot.Frame.Id, snapshot.Frame.Version, chosen.Position, mouse);
                ghost = mouse;
            }
        }
        else if (pressed && ImGui.IsMouseDown(ImGuiMouseButton.Left)) lastPress = null;

        if (gesture is not null && (unusable ?? Lost(snapshot)) is { } lost)
        {
            gesture = null;
            Said = lost;
        }
        var movedThisFrame = false;
        if (gesture is not null)
        {
            ghost = mouse;
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                Release(mouse);
                movedThisFrame = true;
            }
        }
        var current = (movedThisFrame || keyed) && Read() is { } after ? shown = after : snapshot;

        var selected = current.Markers.FirstOrDefault(m => m.OutlineId == editor.Primary)?.Key;
        draw.PushClipRect(origin, origin + size, true);
        var places = current.Markers.Where(m => m.Position.IsFinite).GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.First().Position);
        foreach (var e in current.Edges)
        {
            if (e.From is not { } from || e.To is not { } to || !places.TryGetValue(from, out var a) || !places.TryGetValue(to, out var b)) continue;
            var lit = selected is { } s && (from == s || to == s);
            draw.AddLine(ToScreen(a), ToScreen(b), lit ? SelectedColour : LineColour, lit ? 2 : 1);
        }
        foreach (var m in current.Markers.Where(m => m.Position.IsFinite))
        {
            var at = ToScreen(m.Position);
            var picked = m.Key == selected;
            draw.AddCircleFilled(at, picked ? 7 : 5, picked ? SelectedColour : DotColour);
            var radius = picked ? 7 : 5;
            Ui.RecordCanvasItem(treeCanvas, m.Key.ToString(), m.Label, new(at.X-radius, at.Y-radius, radius*2, radius*2), picked);
            if (picked || (under.Count > 0 && under[0].Key == m.Key)) draw.AddText(at + new float2(9, -8), TextColour, m.Label);   // names when asked for: a crowd can't be read
        }
        if (gesture is not null) draw.AddCircleFilled(ghost, 7, GhostColour);
        if (focused && ImGui.GetCurrentContext().NavCursorVisible)   // inside the canvas, where nothing drawn over it hides it
            draw.AddRect(origin + new float2(2, 2), origin + size - new float2(2, 2), ImGui.GetColorU32(ImGuiCol.NavCursor), 0, ImDrawFlags.None, 2);
        draw.PopClipRect();
    }

    // The keys while it has the keyboard focus (see the class summary); true when the document changed. Arrows are its
    // own: ImGui's move to a neighbour is cancelled, as the table does for its cells.
    private bool Keyboard(MapSnapshot snapshot, string? unusable)
    {
        if (CanvasKeys.Zoom() is not 0 and var by)
        {
            var was = zoom;
            zoom = Math.Clamp(by > 0 ? zoom * 1.25f : zoom / 1.25f, 1, 1000);
            pan *= zoom / was;   // the middle stays in the middle
        }
        if (ImGui.IsKeyPressed(ImGuiKey._0, false) || ImGui.IsKeyPressed(ImGuiKey.Keypad0, false)) (zoom, pan) = (1, default);
        if (CanvasKeys.Pick() is not 0 and var pick) SelectNext(snapshot, pick);
        var (x, y) = CanvasKeys.Arrow();
        if (x == 0 && y == 0) return false;
        ImGui.Internal.NavMoveRequestCancel();
        var marker = snapshot.Markers.FirstOrDefault(m => m.OutlineId == editor.Primary);
        if (ImGui.GetIO().KeyCtrl || marker is null)
        {
            pan -= new float2(x, -y) * 40;   // the view moves the way the arrow points
            return false;
        }
        if (unusable is not null) { Said = unusable; return false; }
        if (!marker.Movable || editor.Playing is not null || snapshot.Markers.Count(m => m.Key == marker.Key) != 1) { Said = "That can't be moved."; return false; }
        var grid = SnapGrid();
        var unit = ToFrame(new float2(10, -10));   // ten pixels' worth, with no grid
        var step = new MapPoint(grid is { Step.X: > 0 } ? grid.Step.X : unit.X, grid is { Step.Y: > 0 } ? grid.Step.Y : unit.Y);
        var start = marker.Position;
        var to = new MapPoint(start.X + x * step.X, start.Y + y * step.Y);
        if (grid is not null) to = new MapPoint(Snapped(to.X, grid.Step.X, grid.Origin.X), Snapped(to.Y, grid.Step.Y, grid.Origin.Y));
        return TryMove(marker.Key, start, to, editor.Session.Revision);
    }

    // Enter steps through the markers shown, in the source's order.
    private void SelectNext(MapSnapshot snapshot, int step)
    {
        var markers = snapshot.Markers.Where(m => m.Position.IsFinite).ToList();
        if (markers.Count == 0) return;
        var at = markers.FindIndex(m => m.OutlineId == editor.Primary);
        var next = markers[CanvasKeys.Next(at, step, markers.Count)];
        editor.Select(next.OutlineId);
        Said = $"Selected {next.Label}.";
    }

    private static float Distance(float2 a, float2 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
