using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The 3D viewport (AT-030/031): the camera and picking maths, then the viewport in the editor window driven by
/// scripted clicks and drags with no window.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class ViewportTests
{
    private static readonly string? Shots = Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS");

    [Fact]
    public void ProjectingAPointAndCastingARayBackThroughItMeet()
    {
        var cam = Camera.Default;
        var size = new Vector2(800, 600);
        foreach (var p in new[] { Vector3.Zero, new Vector3(2, 1, -3), new Vector3(-4, 0.5f, 5) })
        {
            var (screen, depth) = cam.Project(p, size)!.Value;
            Assert.InRange(depth, 0, 1);
            var ray = cam.RayAt(screen, size);
            var closest = ray.Origin + ray.Direction * Vector3.Dot(p - ray.Origin, ray.Direction);
            Assert.True(Vector3.Distance(closest, p) < 1e-3f, $"{p} came back as {closest}");
        }
    }

    [Fact]
    public void TheCameraIsLeftHandedYUpAndTheTargetIsInTheMiddle()
    {
        var size = new Vector2(800, 600);
        var front = new Camera(Vector3.Zero, 0, 0, 10);   // at -Z looking along +Z
        Assert.True(Vector3.Distance(front.Eye, new Vector3(0, 0, -10)) < 1e-4f);
        Assert.True(Vector2.Distance(front.Project(Vector3.Zero, size)!.Value.Screen, size / 2) < 1e-3f);
        Assert.True(front.Project(Vector3.UnitX, size)!.Value.Screen.X > 400);   // +X is to the right (left-handed)
        Assert.True(front.Project(Vector3.UnitY, size)!.Value.Screen.Y < 300);   // +Y is up (screen Y down)
        Assert.Null(front.Project(new Vector3(0, 0, -20), size));               // behind the camera
        Assert.Equal(89 * MathF.PI / 180, front.Orbit(0, 10).Pitch, 4);         // pitch stops short of straight up
    }

    [Fact]
    public void PickingFindsTheNearestObjectAlongTheRay()
    {
        var items = new[]
        {
            new ViewportItem("far", Matrix4x4.CreateTranslation(0, 0, 5), MeshData.Cube, 0),
            new ViewportItem("near", Matrix4x4.CreateTranslation(0, 0, 0), MeshData.Cube, 0),
            new ViewportItem("aside", Matrix4x4.CreateTranslation(5, 0, 0), MeshData.Cube, 0),
        };
        var ray = new Ray(new Vector3(0, 0, -10), Vector3.UnitZ);
        var hit = Picking.Pick(items, ray)!.Value;
        Assert.Equal("near", hit.Id);
        Assert.Equal(9.5f, hit.Distance, 3);   // the cube's front face at z = -0.5
        Assert.Null(Picking.Pick(items, new Ray(new Vector3(0, 10, -10), Vector3.UnitZ)));
    }

    [Fact]
    public void AChildIsDrawnAtItsParentsTransformAndMovedInWorldSpace()
    {
        var scene = new Scene([
            SceneObject.At("room", null, new Vector3(10, 0, 0)) with { Scale = new Vector3(2, 2, 2) },
            SceneObject.At("lamp", null, new Vector3(1, 0, 0)) with { Parent = "room" },
        ]);
        Assert.Equal(new Vector3(12, 0, 0), SceneViewport.World(scene, "lamp").Translation);   // 10 + 2 × 1
        var move = Assert.IsType<MoveObject>(new SceneViewport().MoveTo(scene, "lamp", new Vector3(14, 0, 0)));
        Assert.Equal(new Vector3(2, 0, 0), move.Position);   // local to the room: (14 - 10) / 2
    }

    private static (EditorModel<Scene> Editor, EditorWindow<Scene> Window, ViewportView<Scene> View) Setup()
    {
        var scene = new Scene([
            SceneObject.At("crate", "props/crate.glb", new Vector3(0, 0.5f, 0)),
            SceneObject.At("barrel", null, new Vector3(3, 0.5f, 2)),
        ]);
        var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline);
        var view = new ViewportView<Scene>(editor, new SceneViewport(a => a == "props/crate.glb" ? MeshData.Cube : null));
        editor.Add(view.AsPanel());
        var window = new EditorWindow<Scene>(editor, new Inspector<Scene>(editor).Add(SceneEditing.ObjectType()), new Shortcuts(), SceneEditing.CommandFor);
        return (editor, window, view);
    }

    private static void Draw(EditorWindow<Scene> w)
    {
        ImGui.SetNextWindowPos(new Brutal.Numerics.float2(10, 10), ImGuiCond.Always);
        w.Draw();   // the editor is 900 wide; the viewport panel opens beside it (docking comes next)
    }

    private static Brutal.Numerics.float2 F(Vector2 v) => new(v.X, v.Y);

    [Fact]
    public void ClickingAnObjectInTheViewportSelectsItAndEmptySpaceClears()
    {
        var (editor, window, view) = Setup();
        using var host = new HeadlessHost(1700, 800) { Drawing = Shots is { Length: > 0 } };
        host.Frames(() => Draw(window));
        var crate = view.ScreenOf(new Vector3(0, 0.5f, 0))!.Value;
        host.Click(() => Draw(window), crate.X, crate.Y);
        Assert.Equal(["crate"], editor.Selection);
        if (Shots is { Length: > 0 } d) host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "20_viewport_selected.png"));
        var corner = view.LastRect.Origin + new Vector2(5, 5);   // sky, nothing there
        host.Click(() => Draw(window), corner.X, corner.Y);
        Assert.Empty(editor.Selection);
    }

    [Fact]
    public void DraggingASelectedObjectMovesItOnTheGridAsOneUndoStep()
    {
        var (editor, window, view) = Setup();
        using var host = new HeadlessHost(1700, 800) { Drawing = Shots is { Length: > 0 } };
        host.Frames(() => Draw(window));
        var from = view.ScreenOf(new Vector3(0, 0.5f, 0))!.Value;
        host.Click(() => Draw(window), from.X, from.Y);   // select first
        var to = view.ScreenOf(new Vector3(2.1f, 0.5f, -1.4f))!.Value;
        host.Drag(() => Draw(window), F(from), F(to));
        var moved = editor.Session.Current.Find("crate")!.Position;
        Assert.Equal(new Vector3(2, 0.5f, -1.5f), moved);   // snapped to the half-metre grid, height kept
        Assert.StartsWith("Moved crate", view.Said);
        if (Shots is { Length: > 0 } d) host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "21_viewport_moved.png"));
        Assert.True(editor.Session.Undo());
        Assert.Equal(new Vector3(0, 0.5f, 0), editor.Session.Current.Find("crate")!.Position);
        Assert.False(editor.Session.Undo());   // the whole drag was one step
    }

    [Fact]
    public void RightDragOrbitsAndTheWheelZoomsWithoutEditing()
    {
        var (editor, window, view) = Setup();
        using var host = new HeadlessHost(1700, 800) { Drawing = false };
        host.Frames(() => Draw(window));
        var before = view.Camera;
        var mid = view.LastRect.Origin + view.LastRect.Size / 2;
        host.Drag(() => Draw(window), F(mid), F(mid + new Vector2(80, 0)), button: 1);
        Assert.True(view.Camera.Yaw > before.Yaw);
        Assert.Empty(editor.Session.Journal);   // looking around never edits
    }

    [Fact]
    public void WhilePlayingTheViewportCantMoveThings()
    {
        var (editor, window, view) = Setup();
        editor.Add(new PlayMode<Scene>("test.walk", "Walk", _ => new Idle()));
        using var host = new HeadlessHost(1700, 800);
        host.Frames(() => Draw(window));
        var from = view.ScreenOf(new Vector3(0, 0.5f, 0))!.Value;
        host.Click(() => Draw(window), from.X, from.Y);
        Assert.True(editor.Play("test.walk"));
        var to = view.ScreenOf(new Vector3(2, 0.5f, 2))!.Value;
        host.Drag(() => Draw(window), F(from), F(to));   // the viewport steps aside while playing: nothing to drag
        Assert.Equal(new Vector3(0, 0.5f, 0), editor.Session.Current.Find("crate")!.Position);
        Assert.False(editor.Execute(new SceneViewport().MoveTo(editor.Session.Current, "crate", Vector3.One)!, "crate"));
        Assert.Contains("is playing; stop it to edit", editor.Refused[0].Message);
    }

    [Fact]
    public void TheDockedLayoutPutsTheViewportInTheMiddleAndItStillWorks()
    {
        var (editor, window, view) = Setup();
        using var host = new HeadlessHost(1600, 900);
        EditorWindow<Scene>.Configure(ImGui.GetIO());   // turns docking on
        host.Frames(window.DrawDocked, 4);
        var (origin, size) = view.LastRect;
        Assert.True(size.X > 1600 * 0.4f && size.Y > 900 * 0.6f, $"the viewport is only {size}");
        Assert.True(origin.X > 1600 * 0.15f && origin.X + size.X < 1600 * 0.85f, "it sits between the outliner and the inspector");
        var crate = view.ScreenOf(new Vector3(0, 0.5f, 0))!.Value;
        host.Click(window.DrawDocked, crate.X, crate.Y);
        Assert.Equal(["crate"], editor.Selection);
        host.Frames(window.DrawDocked, 2);
        if (Shots is { Length: > 0 } d) host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "22_docked.png"));
        Assert.True(host.Covered(0, 0, 1600, 30) > 500, "the toolbar runs along the top");
    }

    private sealed class Idle : IPlaySession
    {
        public string Status => "";
        public void Draw() { }
        public void Dispose() { }
    }
}
