using System.Collections.Immutable;
using System.Numerics;
using Bed;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// INVENTED acceptance fixtures and expectations, derived from the public XML contracts and A016's
// left-handed/Y-up/screen-Y-down conventions. No ViewportTests.cs or production bodies were read.
[Collection(ImGuiCollection.Name)]
public sealed class ViewportAcceptanceTests
{
    private static readonly Vector2 Square = new(800, 800);
    private static Camera Straight => new(new Vector3(0, 0, 5), 0, 0, 5, MathF.PI / 2);
    private static readonly MeshData Triangle = new(
        [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)], [0, 1, 2]);
    private static Scene SceneOf(params SceneObject[] objects) => new(objects.ToImmutableArray());
    private static SceneObject Obj(string id, Vector3 p, string? parent = null) =>
        SceneObject.At(id, null, p) with { Parent = parent };
    private static void Near(float expected, float actual, float tolerance = 0.001f) =>
        Assert.True(float.IsFinite(actual) && MathF.Abs(expected - actual) <= tolerance,
            $"Expected {expected} +/- {tolerance}, got {actual}");
    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 0.001f)
    { Near(expected.X, actual.X, tolerance); Near(expected.Y, actual.Y, tolerance); Near(expected.Z, actual.Z, tolerance); }
    private static void Near(Vector2 expected, Vector2 actual, float tolerance = 0.001f)
    { Near(expected.X, actual.X, tolerance); Near(expected.Y, actual.Y, tolerance); }
    private static (Vector2 Screen, float Depth) Project(Camera c, Vector3 p, Vector2 size)
    { var q = c.Project(p, size); Assert.True(q.HasValue, $"Expected visible point {p}"); return q.Value; }
    private static ViewportItem Item(string id, Matrix4x4 m, MeshData? mesh = null) => new(id, m, mesh ?? Triangle, 0xffffffff);

    [Fact] public void CameraZeroYawLooksAlongPositiveZ()
    { Near(Vector3.UnitZ, Straight.Forward); Near(Vector3.Zero, Straight.Eye); }

    [Fact] public void CameraQuarterYawLooksAlongPositiveX()
    { var c = new Camera(new(4, 0, 0), MathF.PI / 2, 0, 4); Near(Vector3.UnitX, c.Forward); Near(Vector3.Zero, c.Eye); }

    [Fact] public void CameraPositivePitchLooksDown()
    { var c = new Camera(Vector3.Zero, 0, MathF.PI / 6, 8); Near(new(0, -0.5f, MathF.Sqrt(3) / 2), c.Forward); Near(new(0, 4, -4 * MathF.Sqrt(3)), c.Eye); }

    [Fact] public void CameraViewTakesEyeToOriginAndTargetToPositiveDepth()
    { var c = new Camera(new(7, -2, 9), 0.6f, 0.3f, 11); Near(Vector3.Zero, Vector3.Transform(c.Eye, c.View)); Near(new(0, 0, 11), Vector3.Transform(c.Target, c.View)); }

    [Theory] [InlineData(0, 0, 400, 400)] [InlineData(2.5f, 2.5f, 600, 200)]
    [InlineData(2.5f, -2.5f, 600, 600)] [InlineData(-2.5f, 2.5f, 200, 200)]
    public void CameraNinetyDegreeWorkedProjection(float x, float y, float sx, float sy)
    { Near(new Vector2(sx, sy), Project(Straight, new(x, y, 5), Square).Screen); }

    [Fact] public void CameraProjectionUsesVerticalFieldOfViewAtWideAspect()
    { Near(new Vector2(1000, 200), Project(Straight, new(2.5f, 2.5f, 5), new(1600, 800)).Screen); }

    [Fact] public void CameraDepthOrdersVisiblePoints()
    { var near = Project(Straight, new(0, 0, 1), Square); var far = Project(Straight, new(0, 0, 100), Square); Assert.InRange(near.Depth, 0, 1); Assert.InRange(far.Depth, 0, 1); Assert.True(near.Depth < far.Depth); }

    [Theory] [InlineData(0, 0, -1)] [InlineData(3, 4, -5)] [InlineData(-20, 1, -0.1f)]
    public void CameraBehindPointsHaveNoProjection(float x, float y, float z)
    { Assert.Null(Straight.Project(new(x, y, z), Square)); }

    [Fact] public void CameraCentreRayStartsAtEyeAndLooksForward()
    { var ray = Straight.RayAt(new(400, 400), Square); Near(Vector3.Zero, ray.Origin); Near(Vector3.UnitZ, ray.Direction); }

    [Fact] public void CameraOffCentreRayMatchesWorkedSlope()
    { var ray = Straight.RayAt(new(600, 200), Square); Near(Vector3.Zero, ray.Origin); Near(Vector3.Normalize(new(1, 1, 2)), ray.Direction); }

    [Fact] public void CameraOffCentreRayDirectionMatchesWorkedSlope()
    { Near(Vector3.Normalize(new(1, 1, 2)), Straight.RayAt(new(600, 200), Square).Direction); }

    [Fact] public void CameraSeededGridRoundTripsAcrossPosesAndAspectRatios()
    {
        var rng = new Random(240031);
        for (int pose = 0; pose < 6; pose++)
        {
            var c = new Camera(new(pose - 2, pose * 0.5f, 3 - pose),
                (float)(rng.NextDouble() * 5 - 2.5), (float)(rng.NextDouble() - 0.5), 7 + pose, 0.8f + pose * 0.08f);
            var size = pose % 2 == 0 ? new Vector2(1200, 700) : new Vector2(650, 1000);
            // Make camera-space points by independent basis construction, not by inverting Project.
            var forward = Vector3.Normalize(c.Target - c.Eye);
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
            var up = Vector3.Cross(forward, right);
            for (int ix = -2; ix <= 2; ix++) for (int iy = -2; iy <= 2; iy++)
            {
                float depth = 4 + (float)rng.NextDouble() * 30;
                var p = c.Eye + forward * depth + right * (ix * depth * 0.08f) + up * (iy * depth * 0.08f);
                var q = Project(c, p, size); var ray = c.RayAt(q.Screen, size);
                Near(1, ray.Direction.Length());
                Assert.True(Vector3.Dot(p - ray.Origin, ray.Direction) > 0);
                Near(p, ray.At(Vector3.Distance(p, ray.Origin)), 0.008f);
            }
        }
    }

    [Theory] [InlineData(100, 89)] [InlineData(-100, -89)]
    public void CameraOrbitClampsPitchAtEightyNineDegrees(float delta, float degrees)
    { Near(degrees * MathF.PI / 180, Straight.Orbit(0, delta).Pitch); }

    [Fact] public void CameraOrbitAddsYawAndPreservesTargetAndDistance()
    { var c = Straight.Orbit(0.7f, 0.2f); Near(0.7f, c.Yaw); Near(0.2f, c.Pitch); Near(Straight.Target, c.Target); Near(5, c.Distance); }

    [Theory] [InlineData(0.000001f, 0.1f)] [InlineData(100000, 1000)] [InlineData(2, 10)] [InlineData(0.5f, 2.5f)]
    public void CameraZoomRespectsLimitsAndFactor(float factor, float distance)
    { var c = Straight.Zoom(factor); Near(distance, c.Distance); Near(Straight.Target, c.Target); Near(Straight.Yaw, c.Yaw); Near(Straight.Pitch, c.Pitch); }

    [Fact] public void CameraFrameCentresAnOffsetBox()
    { var c = Camera.Default.Frame(new(4, -3, 2), new(10, 5, 6)); Near(new(7, 1, 4), c.Target); }

    [Fact] public void CameraFrameContainsAllEightCorners()
    {
        Vector3 min = new(-4, -2, -3), max = new(7, 5, 8);
        var c = Camera.Default.Frame(min, max);
        foreach (float x in new[] { min.X, max.X }) foreach (float y in new[] { min.Y, max.Y }) foreach (float z in new[] { min.Z, max.Z })
        { var q = Project(c, new(x, y, z), Square); Assert.InRange(q.Screen.X, 0, 800); Assert.InRange(q.Screen.Y, 0, 800); Assert.InRange(q.Depth, 0, 1); }
    }

    [Fact] public void MeshBoundsUseAllPositionsIncludingUnreferencedVertices()
    { var mesh = new MeshData([new(1, 2, 3), new(-4, 6, -8), new(10, -2, 4)], []); Near(new(-4, -2, -8), mesh.Bounds.Min); Near(new(10, 6, 4), mesh.Bounds.Max); }

    [Fact] public void PlaceholderCubeIsOneMetreCentredAtOrigin()
    {
        Near(new(-0.5f), MeshData.Cube.Bounds.Min); Near(new(0.5f), MeshData.Cube.Bounds.Max);
        // The cube contract fixes its shape, not its vertex/index layout or tessellation.
        foreach (var d in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var hit = Picking.Pick([Item("cube", Matrix4x4.Identity, MeshData.Cube)], new(-2 * d, d));
            Assert.NotNull(hit); Near(1.5f, hit.Value.Distance);
        }
    }

    [Fact] public void RayAtUsesOriginAndDistance()
    { Near(new(2, 3, 11), new Ray(new(2, 3, 4), Vector3.UnitZ).At(7)); }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RayTriangleHitsBothWindings(bool reversed)
    { var a = new Vector3(-1, -1, 5); var b = new Vector3(1, -1, 5); var c = new Vector3(0, 1, 5); var hit = new Ray(Vector3.Zero, Vector3.UnitZ).Hit(a, reversed ? c : b, reversed ? b : c); Assert.NotNull(hit); Near(5, hit.Value); }

    [Theory] [InlineData(-1, -1)] [InlineData(1, -1)] [InlineData(0, 1)] [InlineData(0, -1)] [InlineData(0.5f, 0)]
    public void RayTriangleIncludesVerticesAndEdges(float x, float y)
    { var hit = new Ray(new(x, y, 0), Vector3.UnitZ).Hit(new(-1, -1, 5), new(1, -1, 5), new(0, 1, 5)); Assert.NotNull(hit); Near(5, hit.Value); }

    [Fact] public void RayTriangleOutsideIsAMiss()
    { Assert.Null(new Ray(new(1, 1, 0), Vector3.UnitZ).Hit(new(-1, -1, 5), new(1, -1, 5), new(0, 1, 5))); }

    [Fact] public void RayTriangleParallelIsAMiss()
    { Assert.Null(new Ray(Vector3.Zero, Vector3.UnitX).Hit(new(-1, -1, 5), new(1, -1, 5), new(0, 1, 5))); }

    [Fact] public void RayTriangleBehindIsAMiss()
    { Assert.Null(new Ray(new(0, 0, 6), Vector3.UnitZ).Hit(new(-1, -1, 5), new(1, -1, 5), new(0, 1, 5))); }

    [Fact] public void RayDegenerateTriangleIsAMiss()
    { Assert.Null(new Ray(Vector3.Zero, Vector3.UnitZ).Hit(new(0, 0, 5), new(1, 0, 5), new(2, 0, 5))); }

    [Theory] [InlineData(3, -1, 0, 3)] [InlineData(-2, 1, 4, 6)]
    public void RayHorizontalPlaneWorkedDistances(float originY, float directionY, float planeY, float distance)
    { var ray = new Ray(new(4, originY, 7), new(0, directionY, 0)); var hit = ray.HitPlaneY(planeY); Assert.NotNull(hit); Near(distance, hit.Value); Near(new(4, planeY, 7), ray.At(hit.Value)); }

    [Fact]
    public void RayHorizontalPlaneIncludesItsOrigin()
    { var ray = new Ray(new(4, 2, 7), -Vector3.UnitY); var hit = ray.HitPlaneY(2); Assert.NotNull(hit); Near(0, hit.Value); Near(new(4, 2, 7), ray.At(hit.Value)); }

    [Fact] public void RayHorizontalPlaneRejectsParallelAndBehind()
    { Assert.Null(new Ray(new(0, 2, 0), Vector3.UnitX).HitPlaneY(0)); Assert.Null(new Ray(new(0, 2, 0), Vector3.UnitY).HitPlaneY(0)); }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PickingOverlapsUsesNearestRegardlessOfEnumeration(bool reverse)
    { var items = new[] { Item("far", Matrix4x4.CreateTranslation(0, 0, 9)), Item("near", Matrix4x4.CreateTranslation(0, 0, 3)) }; var hit = Picking.Pick(reverse ? items.Reverse() : items, new(Vector3.Zero, Vector3.UnitZ)); Assert.NotNull(hit); Assert.Equal("near", hit.Value.Id); Near(3, hit.Value.Distance); }

    [Fact] public void PickingMissesOutsideTrianglesEvenInsideBounds()
    { Assert.Null(Picking.Pick([Item("tri", Matrix4x4.CreateTranslation(0, 0, 5))], new(new(0.9f, 0.9f, 0), Vector3.UnitZ))); }

    [Fact] public void PickingRotatedItemHasWorkedWorldDistance()
    { var m = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(7, 0, 0); var hit = Picking.Pick([Item("turned", m)], new(Vector3.Zero, Vector3.UnitX)); Assert.NotNull(hit); Assert.Equal("turned", hit.Value.Id); Near(7, hit.Value.Distance); }

    [Fact] public void PickingNonuniformScalePreservesComparableWorldDistance()
    { var m = Matrix4x4.CreateScale(2, 1, 0.5f) * Matrix4x4.CreateTranslation(2.5f, 2.5f, 5); var ray = new Ray(Vector3.Zero, Vector3.Normalize(new(1, 1, 2))); var hit = Picking.Pick([Item("scaled", m)], ray); Assert.NotNull(hit); Near(5 * MathF.Sqrt(6) / 2, hit.Value.Distance); Near(new(2.5f, 2.5f, 5), ray.At(hit.Value.Distance)); }

    [Theory] [InlineData(-2, 1, 1)] [InlineData(1, -3, 0.5f)] [InlineData(-1, -1, -2)]
    public void PickingMirroredMeshesRemainTwoSided(float x, float y, float z)
    { var hit = Picking.Pick([Item("mirror", Matrix4x4.CreateScale(x, y, z) * Matrix4x4.CreateTranslation(0, 0, 4))], new(Vector3.Zero, Vector3.UnitZ)); Assert.NotNull(hit); Near(4, hit.Value.Distance); }

    [Fact] public void PickingDifferentDepthScalesStillChoosesNearest()
    { var far = Item("far", Matrix4x4.CreateScale(10, 3, 0.01f) * Matrix4x4.CreateTranslation(0, 0, 9), MeshData.Cube); var near = Item("near", Matrix4x4.CreateScale(1, 1, 4) * Matrix4x4.CreateTranslation(0, 0, 5), MeshData.Cube); var hit = Picking.Pick([far, near], new(Vector3.Zero, Vector3.UnitZ)); Assert.NotNull(hit); Assert.Equal("near", hit.Value.Id); Near(3, hit.Value.Distance); }

    [Fact] public void PickingEmptyMeshesAndEmptyItemListsMiss()
    { Assert.Null(Picking.Pick([], new(Vector3.Zero, Vector3.UnitZ))); Assert.Null(Picking.Pick([Item("empty", Matrix4x4.Identity, new([], []))], new(Vector3.Zero, Vector3.UnitZ))); }

    [Fact] public void SceneViewportDeepParentTranslationsComposeInOrder()
    { var objects = new List<SceneObject>(); for (int i = 0; i < 64; i++) objects.Add(Obj($"n{i}", new(1, 2, -1), i == 0 ? null : $"n{i - 1}")); var s = SceneOf(objects.ToArray()); Near(new(64, 128, -64), Vector3.Transform(Vector3.Zero, SceneViewport.World(s, "n63"))); }

    [Fact] public void SceneViewportComposesScaleRotationThenParent()
    { var root = Obj("root", new(10, 0, 0)) with { Scale = new(2, 3, 4), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) }; var child = Obj("child", new(1, 2, 3), "root") with { Scale = new(2, 1, 1) }; var s = SceneOf(root, child); Near(new(22, 6, -2), Vector3.Transform(Vector3.Zero, SceneViewport.World(s, "child"))); Near(new(22, 6, -6), Vector3.Transform(Vector3.UnitX, SceneViewport.World(s, "child"))); }

    [Theory] [InlineData(2, 3, 4)] [InlineData(-2, 3, 0.5f)]
    public void SceneViewportMoveConvertsWorldPointThroughRotatedScaledParent(float sx, float sy, float sz)
    { var root = Obj("root", new(10, -2, 4)) with { Scale = new(sx, sy, sz), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) }; var child = Obj("child", new(9, 8, 7), "root"); var s = SceneOf(root, child); Vector3 wanted = new(22, 10, -2); var command = new SceneViewport().MoveTo(s, "child", wanted); Assert.NotNull(command); var result = command.Apply(s); Assert.True(result.Ok); var moved = result.Document!; Near(new(6 / sx, 12 / sy, 12 / sz), moved.Find("child")!.Position); var actual = Vector3.Transform(Vector3.Zero, SceneViewport.World(moved, "child")); Near(wanted, actual); Near(Project(Camera.Default, wanted, Square).Screen, Project(Camera.Default, actual, Square).Screen); Assert.Equal(root, moved.Find("root")); Assert.Equal(child, s.Find("child")); }

    [Fact] public void SceneViewportMoveThroughDeepRotatedParentsReprojectsExactly()
    { var s = SceneOf(Obj("r", new(5, 1, 2)) with { Scale = new(2, 2, 2), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) }, Obj("p", new(1, 0, 2), "r") with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) }, Obj("c", Vector3.Zero, "p")); Vector3 target = new(13, 7, -4); var cmd = new SceneViewport().MoveTo(s, "c", target); Assert.NotNull(cmd); var after = cmd.Apply(s); Assert.True(after.Ok); Near(target, Vector3.Transform(Vector3.Zero, SceneViewport.World(after.Document!, "c"))); Near(new(3, -2, 2), after.Document!.Find("c")!.Position); }

    [Fact] public void SceneViewportSuppliesFoundMeshesAndPlaceholderCubes()
    { var s = SceneOf(Obj("empty", Vector3.Zero), Obj("missing", Vector3.One) with { Asset = "missing" }, Obj("found", new(2)) with { Asset = "present" }); var requests = new List<string>(); var source = new SceneViewport(asset => { requests.Add(asset); return asset == "present" ? Triangle : null; }); var items = source.Items(s).ToArray(); Assert.Equal(new[] { "empty", "missing", "found" }, items.Select(x => x.Id)); Assert.Same(MeshData.Cube, items[0].Mesh); Assert.Same(MeshData.Cube, items[1].Mesh); Assert.Same(Triangle, items[2].Mesh); Assert.Equal(new[] { "missing", "present" }, requests.Distinct()); }

    [Fact] public void SceneViewportColoursDistinguishEmptyMissingAndFoundAssets()
    { var s = SceneOf(Obj("empty", Vector3.Zero), Obj("missing", Vector3.Zero) with { Asset = "missing" }, Obj("found", Vector3.Zero) with { Asset = "present" }); var items = new SceneViewport(a => a == "present" ? Triangle : null).Items(s).ToArray(); Assert.Equal(3, items.Select(i => i.Colour).Distinct().Count()); Assert.All(items, i => Assert.Equal(255u, i.Colour >> 24)); }

    [Fact] public void SceneViewportCyclesTerminateWithFiniteTransform()
    { var s = SceneOf(Obj("a", new(1, 0, 0), "b"), Obj("b", new(0, 2, 0), "a")); var world = SceneViewport.World(s, "a"); var p = Vector3.Transform(Vector3.Zero, world); Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)); }

    private sealed class TrackingSource : IViewportSource<Scene>
    {
        private readonly SceneViewport inner = new();
        public List<(Scene Document, string Id, Vector3 World)> Moves { get; } = [];
        public IEnumerable<ViewportItem> Items(Scene document) => inner.Items(document);
        public ICommand<Scene>? MoveTo(Scene document, string id, Vector3 world)
        { Moves.Add((document, id, world)); return inner.MoveTo(document, id, world); }
    }

    // This harness draws only the public view into an explicitly sized test window. All positions used for input
    // come from LastRect/ScreenOf after real frames; expected selection/document state is never pre-created.
    private sealed class ViewHarness : IDisposable
    {
        public HeadlessHost Host { get; } = new(1280, 800) { Drawing = false };
        public EditorModel<Scene> Editor { get; }
        public DocumentSession<Scene> Session => Editor.Session;
        public ViewportView<Scene> View { get; }
        public TrackingSource Source { get; } = new();
        public ViewHarness()
        {
            var s = SceneOf(Obj("first", Vector3.Zero), Obj("second", new(-3, 0, 0)));
            Editor = new(new(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), s), SceneEditing.Outline);
            View = new(Editor, Source) { Camera = new(new(0, 0, 0), 0, MathF.PI / 4, 12), Grid = Grid.Uniform(1) };
            Host.Frames(Draw);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(20, 20), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(1000, 700), ImGuiCond.Always);
            ImGui.Begin("Acceptance viewport"u8); View.Draw(); ImGui.End();
        }
        public Vector2 Screen(Vector3 p) { var q = View.ScreenOf(p); Assert.NotNull(q); return q.Value; }
        public void Click(Vector3 p) { var q = Screen(p); Host.Click(Draw, q.X, q.Y); }
        public void Drag(Vector3 from, Vector3 to, int button = 0)
        { var a = Screen(from); var b = Screen(to); Host.Drag(Draw, new(a.X, a.Y), new(b.X, b.Y), button); }
        public void Modifier(ImGuiKey key, bool down) { ImGui.GetIO().AddKeyEvent(key, down); Host.Frame(Draw); }
        public void Dispose() => Host.Dispose();
    }

    [Fact] public void ViewportLastRectAndScreenOfTrackDrawnContent()
    { using var h = new ViewHarness(); var r = h.View.LastRect; Assert.True(r.Size.X > 500 && r.Size.Y > 400); var local = Project(h.View.Camera, Vector3.Zero, r.Size).Screen; Near(r.Origin + local, h.Screen(Vector3.Zero)); }

    [Fact] public void ViewportClickSelectsAndEmptySpaceClears()
    { using var h = new ViewHarness(); h.Click(Vector3.Zero); Assert.Equal(new[] { "first" }, h.Editor.Selection); var r = h.View.LastRect; h.Host.Click(h.Draw, r.Origin.X + 10, r.Origin.Y + 10); Assert.Empty(h.Editor.Selection); Assert.Empty(h.Session.Journal); Assert.False(h.Session.Dirty); }

    [Fact] public void ViewportCtrlClickAddsAndTogglesSelection()
    { using var h = new ViewHarness(); h.Click(Vector3.Zero); h.Modifier(ImGuiKey.Mod_Ctrl, true); h.Click(new(-3, 0, 0)); Assert.Equal(new[] { "first", "second" }, h.Editor.Selection); h.Click(Vector3.Zero); Assert.Equal(new[] { "second" }, h.Editor.Selection); h.Modifier(ImGuiKey.Mod_Ctrl, false); Assert.Empty(h.Session.Journal); }

    [Fact] public void ViewportDragSnapsToGrid()
    { using var h = new ViewHarness(); h.Click(Vector3.Zero); h.Drag(Vector3.Zero, new(1.3f, 0, 1.7f)); Near(new(1, 0, 2), h.Session.Current.Find("first")!.Position); Assert.Single(h.Session.Journal); }

    [Fact] public void ViewportShiftDragFreesEnabledGrid()
    { using var h = new ViewHarness(); h.Click(Vector3.Zero); var camera=h.View.Camera; h.Modifier(ImGuiKey.Mod_Shift, true); h.Drag(Vector3.Zero, new(1.3f, 0, 1.7f)); h.Modifier(ImGuiKey.Mod_Shift, false); Near(new(1.3f, 0, 1.7f), h.Session.Current.Find("first")!.Position, 0.03f); Assert.Single(h.Session.Journal); Assert.Equal(camera,h.View.Camera); }

    [Fact] public void ViewportShiftDragEnablesDisabledGrid()
    { using var h = new ViewHarness(); h.View.Grid = Grid.Uniform(1, enabled: false); h.Click(Vector3.Zero); var camera=h.View.Camera; h.Modifier(ImGuiKey.Mod_Shift, true); h.Drag(Vector3.Zero, new(1.3f, 0, 1.7f)); Near(new(1, 0, 2), h.Session.Current.Find("first")!.Position); Assert.Equal(camera,h.View.Camera); }

    [Fact] public void ViewportDragCommitsExactlyOneUndoStepAndCanRedo()
    { using var h = new ViewHarness(); var original = h.Session.Current; h.Click(Vector3.Zero); h.Drag(Vector3.Zero, new(2, 0, 1)); Assert.Equal(1, h.Session.History.UndoCount); Assert.Single(h.Session.Journal); Near(new(2, 0, 1), h.Session.Current.Find("first")!.Position); Assert.True(h.Session.Undo()); Assert.Equal(original, h.Session.Current); Assert.False(h.Session.History.CanUndo); Assert.True(h.Session.Redo()); Near(new(2, 0, 1), h.Session.Current.Find("first")!.Position); }

    [Fact] public void ViewportDragSelectsAnUnselectedObjectBeforeMoving()
    { using var h = new ViewHarness(); Assert.Empty(h.Editor.Selection); h.Drag(Vector3.Zero, new(2, 0, 1)); Assert.Equal(new[] { "first" }, h.Editor.Selection); Near(new(2, 0, 1), h.Session.Current.Find("first")!.Position); Assert.Single(h.Session.Journal); }

    [Fact] public void ViewportDragCallsSourceWithActualWorldPointAndOriginalDocumentOnce()
    {
        using var h = new ViewHarness(); var before = h.Session.Current;
        h.Click(Vector3.Zero); h.Drag(Vector3.Zero, new(2, 0, 1));
        var move = Assert.Single(h.Source.Moves); Assert.Same(before, move.Document);
        Assert.Equal("first", move.Id); Near(new(2, 0, 1), move.World);
        Near(new(2, 0, 1), h.Session.Current.Find("first")!.Position);
    }

    [Fact] public void ViewportPlayingPreventsJournalledMovesEvenWhenDrawnDirectly()
    {
        using var h = new ViewHarness(); h.Editor.Select("first");
        h.Editor.Add(new PlayMode<Scene>("acceptance.play", "Play", _ => new IdlePlay()));
        Assert.True(h.Editor.Play("acceptance.play")); var before = h.Session.Current;
        h.Drag(Vector3.Zero, new(2, 0, 1)); Assert.Same(before, h.Session.Current);
        Assert.Empty(h.Session.Journal); Assert.False(h.Session.Dirty); Assert.Equal(0, h.Session.History.UndoCount);
    }

    [Fact] public void ViewportRightDragOrbitsWithoutEditing()
    { using var h = new ViewHarness(); var before = h.View.Camera; var s = h.Session.Current; var r = h.View.LastRect; h.Host.Drag(h.Draw, new(r.Origin.X + r.Size.X / 2, r.Origin.Y + r.Size.Y / 2), new(r.Origin.X + r.Size.X / 2 + 90, r.Origin.Y + r.Size.Y / 2 + 40), button: 1); Assert.NotEqual(before.Yaw, h.View.Camera.Yaw); Assert.NotEqual(before.Pitch, h.View.Camera.Pitch); Near(before.Target, h.View.Camera.Target); Assert.Same(s, h.Session.Current); Assert.Empty(h.Session.Journal); Assert.Equal(0, h.Session.History.UndoCount); }

    [Fact] public void ViewportWheelZoomsWithoutEditing()
    { using var h = new ViewHarness(); var p = h.Screen(Vector3.Zero); h.Host.MoveMouse(h.Draw, p.X, p.Y); float before = h.View.Camera.Distance; ImGui.GetIO().AddMouseWheelEvent(0, 1); h.Host.Frames(h.Draw); Assert.True(h.View.Camera.Distance < before); Assert.Empty(h.Session.Journal); Assert.False(h.Session.Dirty); Assert.False(h.Session.History.CanUndo); }

    private sealed class IdlePlay : IPlaySession
    { public string Status => "Running"; public void Draw() { } public void Dispose() { } }

    [Fact] public void ViewportPlayingLocksSelectionAndDragging()
    { using var h = new ViewHarness(); h.Editor.Select("second"); h.Editor.Add(new PlayMode<Scene>("acceptance.play", "Play", _ => new IdlePlay())); Assert.True(h.Editor.Play("acceptance.play")); var original = h.Session.Current; h.Click(Vector3.Zero); h.Drag(Vector3.Zero, new(2, 0, 1)); Assert.Equal(new[] { "second" }, h.Editor.Selection); Assert.Same(original, h.Session.Current); Assert.Empty(h.Session.Journal); Assert.False(h.Session.History.CanUndo); }

    [Fact] public void ViewportPlayingLocksOrbitAndZoom()
    { using var h = new ViewHarness(); h.Editor.Add(new PlayMode<Scene>("acceptance.play", "Play", _ => new IdlePlay())); Assert.True(h.Editor.Play("acceptance.play")); var camera = h.View.Camera; h.Drag(Vector3.Zero, new(2, 0, 1), 1); var p = h.Screen(Vector3.Zero); h.Host.MoveMouse(h.Draw, p.X, p.Y); ImGui.GetIO().AddMouseWheelEvent(0, 1); h.Host.Frames(h.Draw); Assert.Equal(camera, h.View.Camera); Assert.Empty(h.Session.Journal); }

    [Fact] public void ViewportPanelUsesRequestedIdentityAndDrawCallback()
    {
        using var h = new ViewHarness(); var panel = h.View.AsPanel("acceptance.view", "Scene view");
        Assert.Equal("acceptance.view", panel.Id); Assert.Equal("Scene view", panel.Title); Assert.True(panel.OpenAtStart);
        var before = h.View.LastRect;
        h.Host.Frame(() => { ImGui.SetNextWindowPos(new float2(70, 80), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(600, 450), ImGuiCond.Always);
            ImGui.Begin("Panel callback probe"u8); panel.Draw(); ImGui.End(); });
        Assert.True(h.View.LastRect.Origin.X > before.Origin.X);
        Assert.True(h.View.LastRect.Size.X < before.Size.X);
    }

    [Fact] public void ConfigureEnablesDockingAndKeyboardNavigation()
    { using var host = new HeadlessHost(); EditorWindow<Scene>.Configure(ImGui.GetIO()); var io = ImGui.GetIO(); Assert.True((io.ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0); Assert.True((io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0); Assert.True(io.ConfigNavCursorVisibleAlways); Assert.False(io.ConfigNavEscapeClearFocusItem); }

    private sealed class DockHarness : IDisposable
    {
        public HeadlessHost Host { get; } = new(1280, 800) { Drawing = false };
        public EditorModel<Scene> Editor { get; }
        public EditorWindow<Scene> Window { get; }
        public ViewportView<Scene> View { get; }
        public List<(Vector2 Position, Vector2 Size, ImGuiID Dock)> PanelFrames { get; } = [];
        public DockHarness(bool extraPanel = false)
        {
            Editor = new(new(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), SceneOf(Obj("subject", Vector3.Zero))), SceneEditing.Outline);
            var inspector = new Inspector<Scene>(Editor).Add(SceneEditing.ObjectType());
            View = new(Editor, new SceneViewport()); Editor.Add(View.AsPanel());
            if (extraPanel) Editor.Add(new Panel("acceptance.tab", "Probe tab", () =>
            { var p = ImGui.GetWindowPos(); var s = ImGui.GetWindowSize(); PanelFrames.Add((new(p.X, p.Y), new(s.X, s.Y), ImGui.GetWindowDockID())); ImGui.Text("Acceptance panel contents"u8); }));
            Window = new(Editor, inspector); EditorWindow<Scene>.Configure(ImGui.GetIO()); Host.Frames(Window.DrawDocked, 6);
        }
        public void Dispose() => Host.Dispose();
    }

    [Fact] public void DockedViewportOccupiesCentreAndFitsDisplay()
    { using var h = new DockHarness(); var r = h.View.LastRect; Assert.True(r.Origin.X > 100); Assert.True(r.Origin.Y > 20); Assert.True(r.Size.X > 300 && r.Size.Y > 300); Assert.True(r.Origin.X + r.Size.X < h.Host.Width - 100); Assert.True(r.Origin.Y + r.Size.Y <= h.Host.Height); Assert.Empty(h.Editor.Session.Journal); }

    [Fact] public void DockedPanelCallbackObservesCentreDock()
    { using var h = new DockHarness(extraPanel: true); Assert.NotEmpty(h.PanelFrames); var frame = h.PanelFrames[^1]; Assert.NotEqual(default, frame.Dock); Assert.True(frame.Position.X > 100 && frame.Position.X + frame.Size.X < h.Host.Width - 100); Assert.True(frame.Size.X > 300 && frame.Size.Y > 300); Assert.Empty(h.Editor.Session.Journal); }

    private static (Vector2 Position, Vector2 Size, ImGuiID Dock) ActualWindow(string title)
    {
        // A lookup observes an existing window. Begin(title) here would create the expected state and hide regressions.
        var observed = new List<string>();
        foreach (var w in ImGui.GetCurrentContext().Windows)
        {
            string name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)w.Name) ?? "";
            observed.Add(name);
            if (name.Split("##")[0] != title) continue;
            Assert.True(w.Active, $"Expected an active {title} window");
            return (new(w.Pos.X, w.Pos.Y), new(w.Size.X, w.Size.Y), w.DockId);
        }
        Assert.Fail($"Expected existing {title}; observed: {string.Join("; ", observed)}");
        return default;
    }

    [Fact] public void DockedOutlinerAndInspectorOccupyOppositeSidesOfViewport()
    {
        using var h = new DockHarness(); var left = ActualWindow("Outliner"); var right = ActualWindow("Inspector");
        var centre = h.View.LastRect; Assert.NotEqual(default, left.Dock); Assert.NotEqual(default, right.Dock);
        Assert.True(left.Position.X < centre.Origin.X);
        Assert.True(left.Position.X + left.Size.X <= centre.Origin.X + 3);
        Assert.True(right.Position.X >= centre.Origin.X + centre.Size.X - 3);
        Assert.True(right.Position.X + right.Size.X <= h.Host.Width);
        Assert.True(left.Size.Y > 300 && right.Size.Y > 300); Assert.NotEqual(left.Dock, right.Dock);
    }

    [Fact] public void DockedPanelAndViewportAreTabsInSameCentreNode()
    {
        using var h = new DockHarness(extraPanel: true);
        var viewport = ActualWindow("Viewport"); var panel = ActualWindow("Probe tab");
        Assert.NotEqual(default, viewport.Dock); Assert.Equal(viewport.Dock, panel.Dock);
        Near(viewport.Position, panel.Position); Near(viewport.Size, panel.Size);
    }

    private sealed class CallbackPlay(Action draw) : IPlaySession
    { public string Status => "Running"; public void Draw() => draw(); public void Dispose() { } }

    [Fact] public void DockedPlayUsesCentreGameTabAndPausesAuthoringPanels()
    {
        using var h = new DockHarness(extraPanel: true);
        int authoringFrames = h.PanelFrames.Count;
        var gameFrames = new List<(Vector2 Position, Vector2 Size, ImGuiID Dock)>();
        h.Editor.Add(new PlayMode<Scene>("acceptance.play", "Acceptance play", _ => new CallbackPlay(() =>
        {
            var p = ImGui.GetWindowPos(); var size = ImGui.GetWindowSize();
            gameFrames.Add((new(p.X, p.Y), new(size.X, size.Y), ImGui.GetWindowDockID()));
        })));
        var before = h.Editor.Session.Current;
        Assert.True(h.Editor.Play("acceptance.play")); h.Host.Frames(h.Window.DrawDocked, 5);
        Assert.Equal(authoringFrames, h.PanelFrames.Count);
        Assert.NotEmpty(gameFrames); var game = gameFrames[^1]; Assert.NotEqual(default, game.Dock);
        Assert.True(game.Position.X > 100 && game.Position.X + game.Size.X < h.Host.Width - 100);
        Assert.True(game.Size.X > 300 && game.Size.Y > 300);
        Assert.Same(before, h.Editor.Session.Current); Assert.Empty(h.Editor.Session.Journal);
        Assert.True(h.Editor.Stop()); h.Host.Frames(h.Window.DrawDocked, 5);
        Assert.True(h.PanelFrames.Count > authoringFrames);
    }

    [Fact] public void DockedPaletteOpensAndClosesWithoutEditing()
    { using var h = new DockHarness(); Assert.True(h.Window.HandleKey(Chord.Parse("Ctrl+Shift+P"))); h.Host.Frames(h.Window.DrawDocked); Assert.True(h.Window.PaletteOpen); Assert.NotEmpty(h.Window.PaletteEntries("")); Assert.True(h.Window.HandleKey(Chord.Parse("Escape"))); h.Host.Frames(h.Window.DrawDocked); Assert.False(h.Window.PaletteOpen); Assert.Empty(h.Editor.Session.Journal); }
}
