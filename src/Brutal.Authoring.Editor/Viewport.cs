using System.Numerics;

namespace Brutal.Authoring.Editor;

/// <summary>Explicit producer-declared UV convention. Never inferred from spatial basis or asset names.</summary>
public enum MeshUv0Origin
{
    /// <summary>Already normalized top-left UVs, including glTF.</summary>
    TopLeft,
    /// <summary>Raw OBJ coordinates; capture changes V to one minus V exactly once.</summary>
    ObjBottomLeft
}

/// <summary>A triangle mesh in its own (model) space: positions, and three indices per triangle.</summary>
/// <param name="Positions">Vertex positions, in metres.</param>
/// <param name="Indices">Triangles, three indices each, wound clockwise seen from outside (left-handed).</param>
public sealed record MeshData(Vector3[] Positions, int[] Indices)
{
    /// <summary>Optional vertex-aligned UV0 in the explicitly declared origin. Null or empty means absent; no UVs are invented.</summary>
    public Vector2[]? Uv0 {get;init;}
    /// <summary>UV convention explicitly supplied by the producer; defaults to TopLeft.</summary>
    public MeshUv0Origin Uv0Origin {get;init;}
    /// <summary>Optional vertex-aligned normals; absent normals are not synthesized.</summary>
    public Vector3[]? Normals {get;init;}
    /// <summary>Explicit opaque material; null uses the viewport item display color at capture.</summary>
    public SceneMaterial? Material {get;init;}

    /// <summary>Optional ordered, non-nested material parts. Positions and Indices must contain their combined geometry for picking and bounds.</summary>
    public IReadOnlyList<MeshData>? Parts { get; init; }

    /// <summary>A cube one metre across, centred on the origin: the stand-in for an object with no mesh (an empty, a
    /// missing asset).</summary>
    public static readonly MeshData Cube = MakeCube();

    /// <summary>The smallest box around every position.</summary>
    public (Vector3 Min, Vector3 Max) Bounds => Positions.Length == 0 ? (Vector3.Zero, Vector3.Zero)
        : (Positions.Aggregate(Vector3.Min), Positions.Aggregate(Vector3.Max));

    private static MeshData MakeCube()
    {
        var p = new Vector3[8];
        for (var i = 0; i < 8; i++) p[i] = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
        // Each face as two triangles, clockwise seen from outside in a left-handed, Y-up frame.
        int[] faces =
        [
            0, 2, 3, 0, 3, 1,   // -Z
            4, 5, 7, 4, 7, 6,   // +Z
            0, 4, 6, 0, 6, 2,   // -X
            1, 3, 7, 1, 7, 5,   // +X
            0, 1, 5, 0, 5, 4,   // -Y
            2, 6, 7, 2, 7, 3,   // +Y
        ];
        return new MeshData(p, faces);
    }
}

/// <summary>A ray in world space.</summary>
/// <param name="Origin">The starting point in world coordinates.</param>
/// <param name="Direction">The direction vector, used as supplied. A ray parameter measures world distance only when this vector has unit length.</param>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    /// <summary>The point at distance <paramref name="t"/> along the ray.</summary>
    public Vector3 At(float t) => Origin + Direction * t;

    /// <summary>The distance to triangle (a, b, c), either side facing, or null when it misses (Möller–Trumbore, BORROWED).</summary>
    public float? Hit(Vector3 a, Vector3 b, Vector3 c)
    {
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(Direction, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return null;   // parallel
        var inv = 1 / det;
        var s = Origin - a;
        var u = Vector3.Dot(s, p) * inv;
        if (u is < 0 or > 1) return null;
        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(Direction, q) * inv;
        if (v < 0 || u + v > 1) return null;
        var t = Vector3.Dot(e2, q) * inv;
        return t > 1e-6f ? t : null;
    }

    /// <summary>The distance to the horizontal plane at height <paramref name="y"/> (0 when the ray starts on it), or null
    /// when it's parallel or behind.</summary>
    public float? HitPlaneY(float y) => MathF.Abs(Direction.Y) < 1e-9f ? null : (y - Origin.Y) / Direction.Y is var t and >= 0 ? t : null;
}

/// <summary>
/// An orbiting camera in the renderer's conventions: left-handed, Y up, looking at <see cref="Target"/> from
/// <see cref="Distance"/> metres away. Screen coordinates are pixels from the top-left corner, Y down. BORROWED: the orbit
/// controls of every 3D editor (Blender, Unity, Godot); the matrices are System.Numerics' left-handed ones.
/// </summary>
/// <param name="Target">The point it orbits and looks at.</param>
/// <param name="Yaw">Turn around the up axis, in radians (0 looks along +Z).</param>
/// <param name="Pitch">Height angle, in radians, kept within ±89°.</param>
/// <param name="Distance">Metres from the target.</param>
/// <param name="FieldOfView">Vertical field of view, in radians.</param>
public sealed record Camera(Vector3 Target, float Yaw, float Pitch, float Distance, float FieldOfView = MathF.PI / 3)
{
    /// <summary>A camera looking down at the origin from a three-quarter view.</summary>
    public static readonly Camera Default = new(Vector3.Zero, MathF.PI / 4, MathF.PI / 6, 12);

    private const float MaxPitch = 89 * MathF.PI / 180;

    // Camera records are immutable; a with-expression has a new weak identity and cannot reuse
    // the old pose's matrix. Retain only the latest viewport size for each live camera.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Camera, ProjectMatrix> projectMatrices = new();
    private sealed class ProjectMatrix
    {
        private sealed record Entry(Vector2 Size, Matrix4x4 Matrix);
        private Entry? latest;
        internal Matrix4x4 At(Camera camera, Vector2 size)
        {
            var current = Volatile.Read(ref latest);
            if (current is not null && current.Size == size) return current.Matrix;
            var next = new Entry(size, camera.View * camera.Projection(size));
            Volatile.Write(ref latest, next);
            return next.Matrix;
        }
    }
    private Matrix4x4 ViewProjection(Vector2 size) => projectMatrices.GetValue(this, static _ => new()).At(this, size);

    /// <summary>The direction it looks: along +Z at yaw 0, tipped down by a positive pitch.</summary>
    public Vector3 Forward => new(MathF.Cos(Pitch) * MathF.Sin(Yaw), -MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw));

    /// <summary>Where the camera is: <see cref="Distance"/> metres back from the target along <see cref="Forward"/>.</summary>
    public Vector3 Eye => Target - Distance * Forward;

    /// <summary>The view matrix (world to camera).</summary>
    public Matrix4x4 View => Matrix4x4.CreateLookAtLeftHanded(Eye, Target, Vector3.UnitY);

    /// <summary>The projection matrix for a viewport of <paramref name="size"/> pixels.</summary>
    public Matrix4x4 Projection(Vector2 size) =>
        Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView, MathF.Max(1, size.X) / MathF.Max(1, size.Y), 0.05f, 2000f);

    /// <summary>Where <paramref name="world"/> lands on a viewport of <paramref name="size"/> pixels, and its depth (0 near,
    /// 1 far); null when it's behind the camera.</summary>
    public (Vector2 Screen, float Depth)? Project(Vector3 world, Vector2 size)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), ViewProjection(size));
        if (clip.W <= 1e-6f) return null;
        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        return (new Vector2((ndc.X + 1) / 2 * size.X, (1 - ndc.Y) / 2 * size.Y), ndc.Z);
    }

    /// <summary>The ray from the eye through pixel <paramref name="screen"/> of a viewport of <paramref name="size"/> pixels.</summary>
    public Ray RayAt(Vector2 screen, Vector2 size)
    {
        Matrix4x4.Invert(ViewProjection(size), out var inverse);
        var x = screen.X / size.X * 2 - 1;
        var y = 1 - screen.Y / size.Y * 2;
        var near = Vector4.Transform(new Vector4(x, y, 0, 1), inverse);
        var far = Vector4.Transform(new Vector4(x, y, 1, 1), inverse);
        var a = new Vector3(near.X, near.Y, near.Z) / near.W;
        var b = new Vector3(far.X, far.Y, far.Z) / far.W;
        return new Ray(Eye, Vector3.Normalize(b - a));   // from the eye itself, as promised (found by Codex in A024)
    }

    /// <summary>Turns around the target by the given angles, in radians.</summary>
    public Camera Orbit(float yaw, float pitch) => this with { Yaw = Yaw + yaw, Pitch = Math.Clamp(Pitch + pitch, -MaxPitch, MaxPitch) };

    /// <summary>Moves closer (factor below 1) or further away, between 0.1 and 1000 metres.</summary>
    public Camera Zoom(float factor) => this with { Distance = Math.Clamp(Distance * factor, 0.1f, 1000) };

    /// <summary>Looks at the middle of a box, from far enough to see all of it.</summary>
    public Camera Frame(Vector3 min, Vector3 max) => this with
    {
        Target = (min + max) / 2,
        Distance = MathF.Max(0.5f, Vector3.Distance(min, max) / 2 / MathF.Tan(FieldOfView / 2) * 1.2f),
    };
}

/// <summary>One thing a viewport draws: an object's id, its world transform, its mesh and colour (0xAABBGGRR).</summary>
public sealed record ViewportItem(string Id, Matrix4x4 World, MeshData Mesh, uint Colour);

/// <summary>
/// What a document shows in a 3D viewport, and how it's moved there: a game or plug-in supplies one per document type,
/// as it does an outline. The viewport never knows the game; moves go through the document's own commands.
/// </summary>
public interface IViewportSource<TDoc> where TDoc : class
{
    /// <summary>Everything to draw, in the document's current state.</summary>
    IEnumerable<ViewportItem> Items(TDoc document);

    /// <summary>The command that puts object <paramref name="id"/> at world position <paramref name="world"/>, or null when
    /// it can't be moved.</summary>
    ICommand<TDoc>? MoveTo(TDoc document, string id, Vector3 world);
}

/// <summary>An atomic optional transform proposal; errors return no editable preview or partial commit.</summary>
public sealed record ViewportTransformPlan<TDoc>(IReadOnlyList<ICommand<TDoc>> Commands, IReadOnlyList<string> Roots,
    IReadOnlyList<Issue> Issues) where TDoc : class
{
    /// <summary>Whether the entire plan is valid, including no-op.</summary>
    public bool Valid => !Issues.HasErrors();
}

/// <summary>Optional rotation/local-scale planning beside the unchanged move-only viewport contract.</summary>
public interface IViewportTransformSource<TDoc> where TDoc : class
{
    /// <summary>Plans signed rotation about an explicit world pivot and shared unit basis; no mutation.</summary>
    ViewportTransformPlan<TDoc> Rotate(TDoc document,IReadOnlyList<string> selected,Vector3 pivot,Quaternion basis,
        Vector3 axis,float radians,float snapStep);
    /// <summary>Plans multiplicative local-axis scale about each selected root's own origin; no mutation.</summary>
    ViewportTransformPlan<TDoc> Scale(TDoc document,IReadOnlyList<string> selected,Vector3 factors);
}

/// <summary>Picking (worksheet AT-031): which item a ray hits first, on the CPU, against each item's bounds and then
/// its triangles.</summary>
public static class Picking
{
    /// <summary>The nearest item <paramref name="ray"/> hits, and how far along; null when it hits nothing.</summary>
    public static (string Id, float Distance)? Pick(IEnumerable<ViewportItem> items, Ray ray)
    {
        (string Id, float Distance)? best = null;
        foreach (var item in items)
        {
            var m = item.Mesh;
            var world = m.Positions.Select(p => Vector3.Transform(p, item.World)).ToArray();
            if (world.Length == 0 || !HitsBox(ray, world.Aggregate(Vector3.Min), world.Aggregate(Vector3.Max))) continue;
            for (var i = 0; i + 2 < m.Indices.Length; i += 3)
                if (ray.Hit(world[m.Indices[i]], world[m.Indices[i + 1]], world[m.Indices[i + 2]]) is { } t && (best is null || t < best.Value.Distance))
                    best = (item.Id, t);
        }
        return best;
    }

    // Slab test: does the ray pass through the box at all?
    private static bool HitsBox(Ray r, Vector3 min, Vector3 max)
    {
        float lo = 0, hi = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = r.Origin[axis];
            var d = r.Direction[axis];
            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < min[axis] || o > max[axis]) return false;
                continue;
            }
            var t1 = (min[axis] - o) / d;
            var t2 = (max[axis] - o) / d;
            lo = MathF.Max(lo, MathF.Min(t1, t2));
            hi = MathF.Min(hi, MathF.Max(t1, t2));
            if (lo > hi) return false;
        }
        return true;
    }
}

/// <summary>The general scene in a viewport: each object at its world transform (its parents' composed), with the mesh
/// a game supplies for its asset, or the unit cube when there's none (an empty, or a missing asset kept as a placeholder).
/// Moving an object sets its local position so it lands at the world point, whatever its parent.</summary>
public sealed class SceneViewport(Func<string, MeshData?>? meshes = null) : IViewportSource<Scene>, IViewportTransformSource<Scene>
{
    /// <inheritdoc />
    public IEnumerable<ViewportItem> Items(Scene scene) => scene.Objects.Select(o => new ViewportItem(o.Id, World(scene, o.Id),
        o.Asset is { } a && meshes?.Invoke(a) is { } mesh ? mesh : MeshData.Cube,
        o.Asset is null ? 0xFF9A9A9Au : meshes?.Invoke(o.Asset) is null ? 0xFF5A5AE0u : 0xFFC8B48Cu));   // grey empty, red missing, sand found

    /// <summary>An object's world transform: scale, then rotation, then position, then its parent's, and so on up.</summary>
    public static Matrix4x4 World(Scene scene, string id)
    {
        return SceneViewIndex.For(scene).World(id);
    }

    /// <inheritdoc />
    public ViewportTransformPlan<Scene> Rotate(Scene document,IReadOnlyList<string> selected,Vector3 pivot,Quaternion basis,
        Vector3 axis,float radians,float snapStep)
    {
        var plan=SceneRotation.InBasis(document,selected,pivot,basis,axis,radians,snapStep);
        return new(plan.Commands,plan.Roots,plan.Issues);
    }

    /// <inheritdoc />
    public ViewportTransformPlan<Scene> Scale(Scene document,IReadOnlyList<string> selected,Vector3 factors)
    {
        var plan=SceneScale.Local(document,selected,factors);
        return new(plan.Commands,plan.Roots,plan.Issues);
    }

    /// <inheritdoc />
    public ICommand<Scene>? MoveTo(Scene scene, string id, Vector3 world)
    {
        if (SceneViewIndex.For(scene).Find(id) is not { } o) return null;
        var parent = o.Parent is { } p ? World(scene, p) : Matrix4x4.Identity;
        if (!Matrix4x4.Invert(parent, out var inverse)) return null;
        return new MoveObject(id, Vector3.Transform(world, inverse));
    }
}

/// <summary>A captured viewport translation, either one item or a selected-root batch.
/// Preview is transient; commit uses the captured document revision. Dispose (including Escape or host/source
/// replacement) cancels without touching history.</summary>
public sealed class ViewportGesture<TDoc> : IDisposable where TDoc : class
{
    private readonly EditorModel<TDoc> editor;
    private readonly IViewportSource<TDoc> source;
    private readonly TDoc document;
    private readonly long revision;
    private readonly string[] selection;
    private readonly string[] focus;
    private readonly (string Id, Vector3 Start)[]? group;

    private ViewportGesture(EditorModel<TDoc> editor, IViewportSource<TDoc> source, string id, Vector3 start,
        (string Id, Vector3 Start)[]? group = null)
    {
        this.editor = editor; this.source = source; document = editor.Session.Current;
        selection = [.. editor.Selection]; focus = [.. editor.Focus];
        revision = editor.Session.Revision; Id = id; Start = start; Target = start; this.group = group;
        RootIds = Array.AsReadOnly(group is null ? new[] { id } : group.Select(item => item.Id).ToArray());
        editor.Session.Changed += Cancel;
        editor.ViewChanged += ViewChanged;
    }

    /// <summary>Captured object identity.</summary>
    public string Id { get; }
    /// <summary>Captured root identities in document order; descendants of another selected root are excluded.</summary>
    public IReadOnlyList<string> RootIds { get; }
    /// <summary>Captured world position, never recomputed from a newer document.</summary>
    public Vector3 Start { get; }
    /// <summary>Transient proposed world position.</summary>
    public Vector3 Target { get; private set; }
    /// <summary>False after cancel, document change, Play or the first commit attempt.</summary>
    public bool Active { get; private set; } = true;
    /// <summary>The reason a gesture ended without a move.</summary>
    public string Reason { get; private set; } = "";

    /// <summary>Captures one existing item; refuses capture during Play or for a non-finite position.</summary>
    public static ViewportGesture<TDoc>? Begin(EditorModel<TDoc> editor, IViewportSource<TDoc> source, string id)
    {
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(source);
        var item = source.Items(editor.Session.Current).FirstOrDefault(i => i.Id == id);
        return editor.Playing is not null || item is null || !Finite(item.World.Translation)
            ? null : new(editor, source, id, item.World.Translation);
    }

    /// <summary>Captures selected roots for one atomic shared-world translation using the existing MoveTo callbacks.
    /// No outline/source interface changes. Cyclic/missing roots or non-finite starts refuse capture.</summary>
    public static ViewportGesture<TDoc>? BeginSelection(EditorModel<TDoc> editor, IViewportSource<TDoc> source)
    {
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(source);
        if (editor.Playing is not null || editor.Primary is not { } primary || editor.Selection.Count == 0) return null;
        var doc = editor.Session.Current;
        var items = source.Items(doc).GroupBy(item => item.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var selected = editor.Selection.ToHashSet(StringComparer.Ordinal);
        if (!items.TryGetValue(primary, out var first) || !Finite(first.World.Translation)) return null;
        var documentIds = editor.Outline.ObjectIds(doc).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Any(id => !documentIds.Contains(id, StringComparer.Ordinal))) return null;
        var roots = new List<(string Id, Vector3 Start)>();
        foreach (var id in documentIds.Where(selected.Contains))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var nested = false;
            for (var parent = editor.Outline.Parent(doc, id); parent is not null; parent = editor.Outline.Parent(doc, parent))
            {
                if (!seen.Add(parent)) return null;
                if (selected.Contains(parent)) nested = true;
            }
            if (nested) continue;
            if (!items.TryGetValue(id, out var item) || !Finite(item.World.Translation)) return null;
            roots.Add((id, item.World.Translation));
        }
        if (roots.Count == 0 || selected.Any(id => editor.Outline.Label(doc, id) is null)) return null;
        return new(editor, source, primary, first.World.Translation, roots.ToArray());
    }

    /// <summary>Checks the source identity and revision before displaying or committing a preview.
    /// Hosts must cancel when mutating a source's configuration in place.</summary>
    public bool Validate(IViewportSource<TDoc> currentSource)
    {
        if (!Active) return false;
        if (!ReferenceEquals(source, currentSource)) return Stop("The viewport source changed; try again.");
        if (editor.Session.Revision != revision) return Stop("The document changed; try again.");
        if (editor.Playing is not null) return Stop("Play started; the gesture was cancelled.");
        return true;
    }

    /// <summary>Changes only the transient preview. Invalid numeric input cancels the gesture.</summary>
    public bool Preview(Vector3 world, IViewportSource<TDoc> currentSource)
    {
        if (!Validate(currentSource)) return false;
        if (!Finite(world)) return Stop("The proposed position is not finite.");
        Target = world; return true;
    }

    /// <summary>Attempts one revision-guarded atomic edit; no-op gestures add no history or journal entry.</summary>
    public bool Commit(IViewportSource<TDoc> currentSource)
    {
        if (!Validate(currentSource)) return false;
        if (Target == Start) return Stop("No movement.");
        if (group is not null)
        {
            var delta = Target - Start; var commands = new List<ICommand<TDoc>>();
            foreach (var item in group)
            {
                var to = item.Start + delta;
                if (!Finite(to)) return Stop("The proposed position is not finite.");
                if (to == item.Start) continue;
                if (source.MoveTo(document, item.Id, to) is not { } next) return Stop($"'{item.Id}' cannot be moved here.");
                commands.Add(next);
            }
            if (!Validate(source)) return false;
            if (commands.Count == 0) return Stop("No movement.");
            Cancel();
            var done = editor.ExecuteAt(revision, commands, RootIds.ToArray());
            if (!done) Reason = string.Join(" ", (editor.Refused.Count > 0 ? editor.Refused : editor.Session.LastIssues).Select(i => i.Message));
            return done;
        }
        var command = source.MoveTo(document, Id, Target);
        Cancel();
        if (command is null) { Reason = "This object cannot be moved here."; return false; }
        var applied = editor.ExecuteAt(revision, [command], Id);
        if (!applied) Reason = string.Join(" ", (editor.Refused.Count > 0 ? editor.Refused : editor.Session.LastIssues).Select(i => i.Message));
        return applied;
    }

    /// <summary>Cancels without changing the document, dirty state, undo or journal.</summary>
    public void Cancel()
    {
        if (!Active) return;
        Active = false; editor.Session.Changed -= Cancel; editor.ViewChanged -= ViewChanged;
        if (Reason.Length == 0) Reason = "Gesture cancelled.";
    }
    private void ViewChanged()
    {
        if (editor.Playing is not null) Stop("Play started; the gesture was cancelled.");
        else if (!selection.SequenceEqual(editor.Selection) || !focus.SequenceEqual(editor.Focus))
            Stop("Selection or focus changed; the gesture was cancelled.");
    }

    /// <summary>Cancels on input-focus loss or a release that happened while the view was absent.</summary>
    public bool ValidateInput(bool focused, bool buttonDown, bool buttonReleased)
    {
        if (!Active) return false;
        return focused && (buttonDown || buttonReleased) || Stop("Input capture was lost; the gesture was cancelled.");
    }
    private bool Stop(string reason) { Reason = reason; Cancel(); return false; }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    /// <inheritdoc />
    public void Dispose() => Cancel();
}

/// <summary>A move constraint, expressed in the explicitly captured rotation basis.</summary>
public enum MoveHandleKind
{
    /// <summary>First axis.</summary>
    X,
    /// <summary>Second axis.</summary>
    Y,
    /// <summary>Third axis.</summary>
    Z,
    /// <summary>First and second axes.</summary>
    XY,
    /// <summary>First and third axes.</summary>
    XZ,
    /// <summary>Second and third axes.</summary>
    YZ,
}

/// <summary>One screen-sized move handle; screen positions are relative to the viewport.</summary>
/// <param name="Kind">Axis or plane constraint.</param>
/// <param name="Start">Axis hit segment start, outside the object-origin picking area.</param>
/// <param name="End">Axis hit segment end.</param>
/// <param name="Corners">Owned four-corner plane hit region, or empty for an axis.</param>
/// <param name="Axis">First captured world-space direction.</param>
/// <param name="SecondAxis">Second captured world direction, or zero for an axis.</param>
public sealed record MoveHandle(MoveHandleKind Kind, Vector2 Start, Vector2 End,
    IReadOnlyList<Vector2> Corners, Vector3 Axis, Vector3 SecondAxis)
{
    /// <summary>Whether this is a two-axis plane constraint.</summary>
    public bool IsPlane => Corners.Count == 4;

    /// <summary>Hit-tests only the visible constraint, never the origin/object picking area.</summary>
    public bool Hit(Vector2 point, float radius)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(radius) || radius < 0) return false;
        if (Corners.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return false;
        if (IsPlane)
        {
            bool positive = false, negative = false;
            for (var i = 0; i < 4; i++)
            {
                var a = Corners[(i + 1) % 4] - Corners[i]; var b = point - Corners[i];
                var cross = a.X * b.Y - a.Y * b.X;
                positive |= cross > 1e-4f; negative |= cross < -1e-4f;
            }
            return !(positive && negative);
        }
        var segment = End - Start; var length = segment.LengthSquared();
        if (length <= 0) return false;
        var t = Math.Clamp(Vector2.Dot(point - Start, segment) / length, 0, 1);
        return Vector2.DistanceSquared(point, Start + t * segment) <= radius * radius;
    }

    /// <summary>Computes a world delta from two rays against this captured axis/plane.
    /// Parallel, behind-camera or non-finite constraints refuse; no document is changed.</summary>
    public bool TryDelta(Vector3 pivot, Ray start, Ray current, out Vector3 delta)
    {
        delta = default;
        if (!MoveHandles.Finite(pivot) || !MoveHandles.Finite(Axis) || !MoveHandles.Finite(SecondAxis)) return false;
        if (!IsPlane)
        {
            if (!AxisAt(start, out var first) || !AxisAt(current, out var last)) return false;
            delta = Axis * (float)(last - first);
        }
        else
        {
            var normal = Vector3.Cross(Axis, SecondAxis);
            if (!PlaneAt(start, normal, out var first) || !PlaneAt(current, normal, out var last)) return false;
            delta = last - first;
        }
        return MoveHandles.Finite(delta);

        bool AxisAt(Ray ray, out double t)
        {
            t = 0;
            if (!MoveHandles.Finite(ray.Origin) || !MoveHandles.Finite(ray.Direction)) return false;
            var a = Dot(Axis, Axis); var b = Dot(Axis, ray.Direction); var c = Dot(ray.Direction, ray.Direction);
            var denominator = a * c - b * b;
            if (a <= 0 || c <= 0 || denominator <= 1e-8 * a * c) return false;
            var offset = pivot - ray.Origin; var d = Dot(Axis, offset); var e = Dot(ray.Direction, offset);
            t = (b * e - c * d) / denominator;
            var u = (a * e - b * d) / denominator;
            return double.IsFinite(t) && double.IsFinite(u) && u >= 0;
        }
        bool PlaneAt(Ray ray, Vector3 normal, out Vector3 point)
        {
            point = default;
            if (!MoveHandles.Finite(ray.Origin) || !MoveHandles.Finite(ray.Direction)) return false;
            var denominator = Dot(normal, ray.Direction);
            var magnitude = Math.Sqrt(Dot(normal, normal) * Dot(ray.Direction, ray.Direction));
            if (!double.IsFinite(magnitude) || magnitude <= 0 || Math.Abs(denominator) <= 1e-6 * magnitude) return false;
            var t = Dot(normal, pivot - ray.Origin) / denominator;
            if (!double.IsFinite(t) || t < 0) return false;
            point = ray.Origin + ray.Direction * (float)t;
            return MoveHandles.Finite(point);
        }
    }

    private static double Dot(Vector3 a, Vector3 b) => (double)a.X*b.X + (double)a.Y*b.Y + (double)a.Z*b.Z;
}

/// <summary>Pure projection, hit regions and delta snapping for move handles. No input dispatch or document mutation.</summary>
public static class MoveHandles
{
    /// <summary>Creates constant-pixel axis segments/plane regions for an explicit unit rotation basis.
    /// Scale is the current UI/DPI scale; camera zoom changes direction projection, never target size.
    /// Degenerate/behind-camera constraints are omitted, never made into phantom hit regions.</summary>
    public static IReadOnlyList<MoveHandle> Layout(Camera camera, Vector3 pivot, Vector2 size, Quaternion basis, float scale)
    {
        if (!Finite(pivot) || !Finite(camera.Target) || !float.IsFinite(camera.Distance) || camera.Distance <= 0
            || !float.IsFinite(camera.Yaw) || !float.IsFinite(camera.Pitch) || !float.IsFinite(scale) || scale <= 0 || !Unit(basis)
            || !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0
            || !float.IsFinite(camera.FieldOfView) || camera.FieldOfView <= 0 || camera.FieldOfView >= MathF.PI)
            return [];
        var depth = Vector3.Dot(pivot - camera.Eye, camera.Forward);
        if (!float.IsFinite(depth) || depth <= 1e-5f || camera.Project(pivot, size) is not { } centre
            || !Finite(centre.Screen)) return [];
        var axes = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }.Select(axis => Vector3.Transform(axis, basis)).ToArray();
        var directions = new Vector2?[3]; var handles = new List<MoveHandle>();
        for (var i = 0; i < 3; i++)
        {
            if (camera.Project(pivot + axes[i] * (depth * .01f), size) is not { } end || !Finite(end.Screen)) continue;
            var direction = end.Screen - centre.Screen;
            if (direction.LengthSquared() < .0001f) continue;
            direction = Vector2.Normalize(direction); directions[i] = direction;
            handles.Add(new((MoveHandleKind)i, centre.Screen + direction * (20 * scale),
                centre.Screen + direction * (88 * scale), [], axes[i], Vector3.Zero));
        }
        foreach (var (kind, a, b) in new[] { (MoveHandleKind.XY,0,1), (MoveHandleKind.XZ,0,2), (MoveHandleKind.YZ,1,2) })
        {
            if (directions[a] is not { } u || directions[b] is not { } v || MathF.Abs(u.X*v.Y - u.Y*v.X) < .15f) continue;
            Vector2 At(float x, float y) => centre.Screen + (u*x + v*y) * scale;
            var corners = Array.AsReadOnly(new[] { At(12,12), At(30,12), At(30,30), At(12,30) });
            handles.Add(new(kind, default, default, corners, axes[a], axes[b]));
        }
        return handles.AsReadOnly();
    }

    /// <summary>Snaps the intended constrained delta in the captured basis, using cell sizes and zero delta origin.
    /// The project's positional grid origin never offsets an otherwise unchanged gesture.</summary>
    public static bool TrySnapDelta(MoveHandleKind kind, Vector3 world, Quaternion basis, Grid grid, bool invert, out Vector3 result)
    {
        result = default;
        if (!Finite(world) || !Unit(basis) || grid is null || !Finite(grid.Size)) return false;
        var components = Vector3.Transform(world, Quaternion.Inverse(basis));
        components = kind switch
        {
            MoveHandleKind.X => new(components.X,0,0), MoveHandleKind.Y => new(0,components.Y,0),
            MoveHandleKind.Z => new(0,0,components.Z), MoveHandleKind.XY => new(components.X,components.Y,0),
            MoveHandleKind.XZ => new(components.X,0,components.Z), MoveHandleKind.YZ => new(0,components.Y,components.Z),
            _ => new(float.NaN),
        };
        result = Vector3.Transform((grid with { Origin = Vector3.Zero }).Snap(components, invert), basis);
        return Finite(result);
    }

    internal static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
    private static bool Unit(Quaternion q) => float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z)
        && float.IsFinite(q.W) && MathF.Abs(q.LengthSquared() - 1) <= 1e-4f;
}

/// <summary>A captured rotation ring with owned projected points and an explicit world-space plane.</summary>
/// <param name="Axis">X/Y/Z in the captured basis.</param>
/// <param name="Pivot">Captured world pivot.</param>
/// <param name="Normal">Captured world rotation axis.</param>
/// <param name="First">First direction in the ring plane.</param>
/// <param name="Second">Second direction, with First cross Second matching Normal.</param>
/// <param name="Radius">World radius chosen for the target screen size.</param>
/// <param name="Centre">Projected pivot, relative to the viewport.</param>
/// <param name="Points">Owned projected closed-ring samples.</param>
/// <param name="Scale">Captured UI scale.</param>
public sealed record RotationRing(MoveHandleKind Axis, Vector3 Pivot, Vector3 Normal, Vector3 First, Vector3 Second,
    float Radius, Vector2 Centre, IReadOnlyList<Vector2> Points, float Scale)
{
    /// <summary>Hit-tests the projected ring with a screen-sized tolerance, excluding the object-origin picking area.</summary>
    public bool Hit(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || Vector2.Distance(point,Centre) < 14*Scale) return false;
        var tolerance=8*Scale;
        for(var i=0;i<Points.Count;i++)
        {
            var a=Points[i]; var segment=Points[(i+1)%Points.Count]-a; var length=segment.LengthSquared();
            if(length<=0) continue;
            var t=Math.Clamp(Vector2.Dot(point-a,segment)/length,0,1);
            if(Vector2.DistanceSquared(point,a+t*segment)<=tolerance*tolerance) return true;
        }
        return false;
    }

    /// <summary>Signed in-plane angle from the captured First direction. Parallel, behind-plane or centre rays refuse.</summary>
    public bool TryAngle(Ray ray,out float radians)
    {
        radians=0;
        if(!MoveHandles.Finite(ray.Origin) || !MoveHandles.Finite(ray.Direction)) return false;
        double Dot(Vector3 a,Vector3 b)=>(double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;
        var denominator=Dot(Normal,ray.Direction);
        var magnitude=Math.Sqrt(Dot(Normal,Normal)*Dot(ray.Direction,ray.Direction));
        if(!double.IsFinite(magnitude) || magnitude<=0 || Math.Abs(denominator)<=1e-6*magnitude) return false;
        var t=Dot(Normal,Pivot-ray.Origin)/denominator;
        if(!double.IsFinite(t) || t<0) return false;
        var vector=ray.Origin+ray.Direction*(float)t-Pivot;
        if(!MoveHandles.Finite(vector) || Dot(vector,vector)<(double)Radius*Radius*1e-8) return false;
        radians=(float)Math.Atan2(Dot(vector,Second),Dot(vector,First));
        return float.IsFinite(radians);
    }
}

/// <summary>Pure constant-screen-size rotation-ring projection; no tool/input/source interface or document mutation.</summary>
public static class RotationRings
{
    /// <summary>Projects the three axis planes in an explicit unit rotation basis.
    /// Near-edge-on, behind-camera and non-finite rings are omitted rather than given unstable hit regions.</summary>
    public static IReadOnlyList<RotationRing> Layout(Camera camera,Vector3 pivot,Vector2 size,Quaternion basis,float scale)
    {
        if(!MoveHandles.Finite(pivot) || !MoveHandles.Finite(camera.Target) || !float.IsFinite(camera.Distance) || camera.Distance<=0
            || !float.IsFinite(camera.Yaw) || !float.IsFinite(camera.Pitch) || !float.IsFinite(camera.FieldOfView)
            || camera.FieldOfView<=0 || camera.FieldOfView>=MathF.PI || !float.IsFinite(scale) || scale<=0
            || !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X<=0 || size.Y<=0
            || !float.IsFinite(basis.X) || !float.IsFinite(basis.Y) || !float.IsFinite(basis.Z) || !float.IsFinite(basis.W)
            || MathF.Abs(basis.LengthSquared()-1)>1e-4f) return [];
        var depth=Vector3.Dot(pivot-camera.Eye,camera.Forward);
        if(!float.IsFinite(depth) || depth<=1e-5f || camera.Project(pivot,size) is not { } centre) return [];
        var view=Vector3.Normalize(pivot-camera.Eye);
        var axes=new[] { Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ }.Select(v=>Vector3.Transform(v,basis)).ToArray();
        var rings=new List<RotationRing>();
        for(var axis=0;axis<3;axis++)
        {
            var normal=axes[axis]; var first=axes[(axis+1)%3]; var second=axes[(axis+2)%3];
            if(MathF.Abs(Vector3.Dot(normal,view))<.05f) continue;
            var radius=2*depth*MathF.Tan(camera.FieldOfView/2)*(76*scale/size.Y);
            Vector2[]? points=null;
            for(var adjustment=0;adjustment<3;adjustment++)
            {
                points=new Vector2[64]; var valid=true;
                for(var i=0;i<points.Length;i++)
                {
                    var angle=MathF.Tau*i/points.Length;
                    var point=pivot+radius*(first*MathF.Cos(angle)+second*MathF.Sin(angle));
                    if(camera.Project(point,size) is not { } projected || !float.IsFinite(projected.Screen.X) || !float.IsFinite(projected.Screen.Y))
                    { valid=false; break; }
                    points[i]=projected.Screen;
                }
                if(!valid) { points=null; break; }
                var extent=points.Max(point=>Vector2.Distance(point,centre.Screen));
                if(!float.IsFinite(extent) || extent<=1e-5f) { points=null; break; }
                if(adjustment<2) radius*=76*scale/extent;
            }
            if(points is not null && float.IsFinite(radius) && radius>0)
                rings.Add(new((MoveHandleKind)axis,pivot,normal,first,second,radius,centre.Screen,Array.AsReadOnly(points),scale));
        }
        return rings.AsReadOnly();
    }
}

/// <summary>Pure signed-angle accumulation across the atan2 seam. Gesture lifetime remains the caller's responsibility.</summary>
public sealed class RotationRingDrag
{
    private readonly RotationRing ring;
    private float previous;
    private RotationRingDrag(RotationRing ring,float start) { this.ring=ring; previous=start; }
    /// <summary>Accumulated signed radians from the captured ray, before the planner applies its snap rule.</summary>
    public float Radians { get; private set; }
    /// <summary>Captures a real ray on the ring plane; no default pivot or axis is inferred.</summary>
    public static RotationRingDrag? Begin(RotationRing ring,Ray ray)=>ring.TryAngle(ray,out var angle) ? new(ring,angle) : null;
    /// <summary>Accumulates the shortest signed step; false means projection failed and the owner must cancel capture.</summary>
    public bool Update(Ray ray)
    {
        if(!ring.TryAngle(ray,out var angle)) return false;
        var step=angle-previous;
        if(step>MathF.PI) step-=MathF.Tau;
        if(step< -MathF.PI) step+=MathF.Tau;
        var next=Radians+step;
        if(!float.IsFinite(next)) return false;
        previous=angle; Radians=next; return true;
    }
}

/// <summary>A transient planned transform using the existing selected-root capture as its lifetime guard.</summary>
public sealed class ViewportTransformGesture<TDoc> : IDisposable where TDoc : class
{
    private readonly EditorModel<TDoc> editor;
    private readonly IViewportSource<TDoc> source;
    private readonly ViewportGesture<TDoc> guard;
    private readonly long revision;
    private IReadOnlyList<ICommand<TDoc>>? commands;
    private ViewportTransformGesture(EditorModel<TDoc> editor,IViewportSource<TDoc> source,ViewportGesture<TDoc> guard)
    {
        this.editor=editor; this.source=source; this.guard=guard;
        Document=editor.Session.Current; PreviewDocument=Document; revision=editor.Session.Revision;
        Selection=Array.AsReadOnly(editor.Selection.ToArray());
    }
    /// <summary>Immutable captured document supplied to every plan.</summary>
    public TDoc Document { get; }
    /// <summary>Detached transient candidate; the session stays unchanged.</summary>
    public TDoc PreviewDocument { get; private set; }
    /// <summary>Captured selection, not a live reference.</summary>
    public IReadOnlyList<string> Selection { get; }
    /// <summary>Captured roots, in outline order.</summary>
    public IReadOnlyList<string> Roots=>guard.RootIds;
    /// <summary>Whether the shared capture still permits input.</summary>
    public bool Active=>guard.Active;
    /// <summary>Latest projection/plan/commit refusal.</summary>
    public string Reason { get; private set; }="";
    /// <summary>Captures an existing selection; never creates a second document session.</summary>
    public static ViewportTransformGesture<TDoc>? Begin(EditorModel<TDoc> editor,IViewportSource<TDoc> source)
    {
        var guard=ViewportGesture<TDoc>.BeginSelection(editor,source);
        return guard is null ? null : new(editor,source,guard);
    }
    /// <summary>Checks source/revision/selection/focus/Play through the original capture.</summary>
    public bool Validate(IViewportSource<TDoc> current)=>guard.Validate(current);
    /// <summary>Checks current input ownership through the original capture.</summary>
    public bool ValidateInput(bool focused,bool down,bool released)=>guard.ValidateInput(focused,down,released);
    /// <summary>Runs a trusted optional-source planner with containment; a fault clears the current candidate.</summary>
    public bool Preview(Func<TDoc,IReadOnlyList<string>,ViewportTransformPlan<TDoc>> planner)
    {
        if(!guard.Validate(source)) { Reason=guard.Reason; return false; }
        try { return Preview(planner(Document,Selection)); }
        catch(Exception e) when(e is not OutOfMemoryException)
        { commands=null; PreviewDocument=Document; Reason="The source could not plan this transform: "+e.Message; return false; }
    }
    /// <summary>Applies pure command candidates only to the captured snapshot, never the editor.
    /// An invalid current plan clears the candidate; release cannot commit an older valid angle.</summary>
    public bool Preview(ViewportTransformPlan<TDoc> plan)
    {
        commands=null; PreviewDocument=Document;
        if(!guard.Validate(source)) { Reason=guard.Reason; return false; }
        if(plan is null) { Reason="The source returned no transform plan."; return false; }
        if(!plan.Valid) { Reason=string.Join(" ",plan.Issues.Select(i=>i.Message)); return false; }
        if(!plan.Roots.SequenceEqual(Roots)) { Reason="The transform plan changed its captured roots."; return false; }
        var candidate=Document;
        foreach(var command in plan.Commands)
        {
            var next=command.Apply(candidate);
            if(!next.Ok) { Reason=string.Join(" ",next.Issues.Select(i=>i.Message)); return false; }
            candidate=next.Document!;
        }
        if(!guard.Validate(source)) { Reason=guard.Reason; return false; }
        commands=plan.Commands.ToArray(); PreviewDocument=candidate; Reason=""; return true;
    }
    /// <summary>One atomic revision-guarded attempt; no-op/invalid plans add no history.</summary>
    public bool Commit()
    {
        if(!guard.Validate(source)) { Reason=guard.Reason; return false; }
        guard.Cancel();
        if(commands is null || commands.Count==0) { if(Reason.Length==0) Reason="No transform change."; return false; }
        var accepted=editor.ExecuteAt(revision,commands,Roots.ToArray());
        if(!accepted) Reason=string.Join(" ",(editor.Refused.Count>0 ? editor.Refused : editor.Session.LastIssues).Select(i=>i.Message));
        return accepted;
    }
    /// <summary>Cancels without editing.</summary>
    public void Dispose()=>guard.Dispose();
}

/// <summary>The implemented transform tools; additional tools are explicit extensions, not source-interface defaults.</summary>
public enum ViewportTool
{
    /// <summary>World/captured-basis translation.</summary>
    Move,
    /// <summary>Rotation about the visible captured pivot.</summary>
    Rotate,
    /// <summary>Local-axis factors about each selected root origin.</summary>
    Scale,
}


/// <summary>Local scale factor axes; Uniform multiplies all three local axes.</summary>
public enum ScaleHandleKind
{
    /// <summary>All three local axes.</summary>
    Uniform,
    /// <summary>Local X factor.</summary>
    X,
    /// <summary>Local Y factor.</summary>
    Y,
    /// <summary>Local Z factor.</summary>
    Z,
}

/// <summary>A captured screen-space scale handle. Its axis is the exact projected transformed local axis,
/// never a quaternion inferred by decomposing a possibly sheared matrix.</summary>
public sealed record ScaleHandle(ScaleHandleKind Kind,Vector2 Centre,Vector2 End,Vector2 Direction,float UiScale)
{
    /// <summary>Constant-size square target; the connecting line is not another input target.</summary>
    public bool Hit(Vector2 point)=>float.IsFinite(point.X) && float.IsFinite(point.Y)
        && MathF.Abs(point.X-End.X)<=8*UiScale && MathF.Abs(point.Y-End.Y)<=8*UiScale;

    /// <summary>Dimensionless factor from captured pointer displacement. One 80-pixel unit gives a factor delta of one.
    /// Snap rounds the delta away from zero at ties. Negative factors mirror; zero is sent to the source for refusal.</summary>
    public bool TryFactors(Vector2 start,Vector2 current,float snapStep,out Vector3 factors)
    {
        factors=Vector3.One;
        if(!float.IsFinite(start.X) || !float.IsFinite(start.Y) || !float.IsFinite(current.X)
            || !float.IsFinite(current.Y) || !float.IsFinite(snapStep) || !float.IsFinite(UiScale) || UiScale<=0) return false;
        var delta=Vector2.Dot(current-start,Direction)/(80*UiScale);
        if(snapStep>0) delta=MathF.Round(delta/snapStep,MidpointRounding.AwayFromZero)*snapStep;
        var factor=1+delta;
        if(!float.IsFinite(factor)) return false;
        factors=Kind switch
        {
            ScaleHandleKind.X=>new(factor,1,1), ScaleHandleKind.Y=>new(1,factor,1),
            ScaleHandleKind.Z=>new(1,1,factor), _=>new(factor),
        };
        return true;
    }
}

/// <summary>Fixed-screen-size scale handles over local matrix axes, with no document edit or TRS decomposition.</summary>
public static class ScaleHandles
{
    /// <summary>Projects local X/Y/Z and a centre uniform square. View-parallel axes have no phantom handle.</summary>
    public static IReadOnlyList<ScaleHandle> Layout(Camera camera,Matrix4x4 world,Vector2 size,float scale)
    {
        var pivot=world.Translation;
        if(!MoveHandles.Finite(pivot) || !MoveHandles.Finite(camera.Target)
            || !float.IsFinite(scale) || scale<=0 || !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X<=0 || size.Y<=0
            || !float.IsFinite(camera.Distance) || camera.Distance<=0 || !float.IsFinite(camera.Yaw) || !float.IsFinite(camera.Pitch)
            || !float.IsFinite(camera.FieldOfView) || camera.FieldOfView<=0 || camera.FieldOfView>=MathF.PI) return [];
        var depth=Vector3.Dot(pivot-camera.Eye,camera.Forward);
        if(!float.IsFinite(depth) || depth<=1e-5f || camera.Project(pivot,size) is not { } centre) return [];
        var result=new List<ScaleHandle>
        { new(ScaleHandleKind.Uniform,centre.Screen,centre.Screen,Vector2.Normalize(new Vector2(1,-1)),scale) };
        var axes=new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ};
        for(var i=0;i<3;i++)
        {
            var axis=Vector3.TransformNormal(axes[i],world);
            var length=axis.Length();
            if(!MoveHandles.Finite(axis) || !float.IsFinite(length) || length<=1e-8f) continue;
            axis/=length;
            // Measure direction locally at the visible origin, not at an endpoint that may cross the camera plane.
            var point=pivot+axis*(depth*.01f);
            if(camera.Project(point,size) is not { } projected) continue;
            var direction=projected.Screen-centre.Screen;
            var pixels=direction.Length();
            if(!float.IsFinite(pixels) || pixels<.05f) continue;
            direction/=pixels;
            result.Add(new((ScaleHandleKind)(i+1),centre.Screen,centre.Screen+direction*(88*scale),direction,scale));
        }
        return result.AsReadOnly();
    }
}
