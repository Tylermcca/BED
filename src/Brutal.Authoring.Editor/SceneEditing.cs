using System.Numerics;
using System.Text.Json;

namespace Brutal.Authoring.Editor;

/// <summary>
/// The general <see cref="Scene"/> in the editor: an outline that nests objects by their parents (so any object with
/// children is a place to focus on), an inspector type for scene objects, and palette commands that act on the
/// selection. A game adds its own object types beside this one for the properties it gives meaning to.
/// </summary>
public static class SceneEditing
{
    /// <summary>The outline: every object in document order, labelled by its <c>name</c> property when it has a text
    /// one (otherwise its id), nested by <see cref="SceneObject.Parent"/>.</summary>
    public static IDocumentOutline<Scene> Outline { get; } = new SceneOutline();

    /// <summary>
    /// The inspector type for scene objects (<c>scene.object</c>): id (read-only), asset, parent, position, rotation (yaw,
    /// pitch and roll in degrees) and scale, one row per axis. Each edit is the scene's own command, so it's validated,
    /// undoable and journaled; editing one axis of several objects keeps each one's other axes. Rotation rows read the
    /// quaternion back as angles, so near straight up or down (pitch ±90°) yaw and roll share one turn and may read
    /// differently than typed; the rotation itself is exact.
    /// </summary>
    public static ObjectType<Scene, SceneObject> ObjectType() => new("scene.object", "Object", (d, id) => d.Find(id),
    [
        new("id", "Id", PropertyKind.Text, o => o.Id, Help: "Its stable id. Other objects refer to it by this."),
        new("asset", "Asset", PropertyKind.Text, o => o.Asset ?? "", (o, v) => new SetAsset(o.Id, Text(v)),
            Help: "The asset it shows, as a path or asset id. Leave empty for an empty (a group or a marker)."),
        new("parent", "Inside", PropertyKind.Text, o => o.Parent ?? "", (o, v) => new SetParent(o.Id, Text(v)),
            Help: "The id of the object it sits inside. Leave empty for the top level."),
        Axis("position.x", "Position X", o => o.Position.X, (o, x) => new MoveObject(o.Id, o.Position with { X = x }), "Metres."),
        Axis("position.y", "Position Y", o => o.Position.Y, (o, y) => new MoveObject(o.Id, o.Position with { Y = y }), "Metres."),
        Axis("position.z", "Position Z", o => o.Position.Z, (o, z) => new MoveObject(o.Id, o.Position with { Z = z }), "Metres."),
        Axis("rotation.yaw", "Yaw", o => Angles(o.Rotation).Yaw, (o, a) => Turn(o, a, null, null), "Degrees, turning about the up axis."),
        Axis("rotation.pitch", "Pitch", o => Angles(o.Rotation).Pitch, (o, a) => Turn(o, null, a, null), "Degrees, tipping nose up or down."),
        Axis("rotation.roll", "Roll", o => Angles(o.Rotation).Roll, (o, a) => Turn(o, null, null, a), "Degrees, rolling about the forward axis."),
        Axis("scale.x", "Scale X", o => o.Scale.X, (o, x) => new ScaleObject(o.Id, o.Scale with { X = x }), "1 is its own size.", 1),
        Axis("scale.y", "Scale Y", o => o.Scale.Y, (o, y) => new ScaleObject(o.Id, o.Scale with { Y = y }), "1 is its own size.", 1),
        Axis("scale.z", "Scale Z", o => o.Scale.Z, (o, z) => new ScaleObject(o.Id, o.Scale with { Z = z }), "1 is its own size.", 1),
    ]);

    /// <summary>
    /// For the editor window's <c>CommandFor</c> hook: palette commands that act on the selection.
    /// <c>scene.remove</c> removes the one selected object; <c>scene.duplicate</c> duplicates the selection as a group.
    /// Anything else needs more input than a selection gives, and returns null.
    /// </summary>
    public static ICommand<Scene>? CommandFor(CommandInfo command, EditorModel<Scene> editor) => (command.Id, editor.Selection.Count) switch
    {
        ("scene.remove", 1) => new RemoveObject(editor.Selection[0]),
        ("scene.duplicate", > 0) => new DuplicateGroup([.. editor.Selection]),
        _ => null,
    };

    /// <summary>Yaw, pitch and roll in degrees for a rotation, the inverse of
    /// <see cref="Quaternion.CreateFromYawPitchRoll(float, float, float)"/>.</summary>
    public static (double Yaw, double Pitch, double Roll) Angles(Quaternion q)
    {
        var sinPitch = Math.Clamp(2.0 * (q.W * q.X - q.Y * q.Z), -1, 1);
        var yaw = Math.Atan2(2.0 * (q.W * q.Y + q.X * q.Z), 1 - 2.0 * (q.X * q.X + q.Y * q.Y));
        var roll = Math.Atan2(2.0 * (q.W * q.Z + q.X * q.Y), 1 - 2.0 * (q.X * q.X + q.Z * q.Z));
        return (Degrees(yaw), Degrees(Math.Asin(sinPitch)), Degrees(roll));
    }

    private static double Degrees(double radians) => Math.Round(radians * 180 / Math.PI, 4);

    private static RotateObject Turn(SceneObject o, float? yaw, float? pitch, float? roll)
    {
        var a = Angles(o.Rotation);
        float R(double degrees) => (float)(degrees * Math.PI / 180);
        return new RotateObject(o.Id, Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(
            R(yaw ?? a.Yaw), R(pitch ?? a.Pitch), R(roll ?? a.Roll))));
    }

    private static PropertyDescriptor<Scene, SceneObject> Axis(string id, string label, Func<SceneObject, double> get,
        Func<SceneObject, float, ICommand<Scene>> edit, string help, double? defaultValue = null) =>
        new(id, label, PropertyKind.Number, o => get(o), (o, v) => edit(o, Convert.ToSingle(v)),
            defaultValue is { } d ? (_, _) => d : null, Help: help);

    private static string? Text(object? v) => v is string s && s.Trim() is { Length: > 0 } t ? t : null;

    private sealed class SceneOutline : IDocumentOutline<Scene>
    {
        public IEnumerable<string> ObjectIds(Scene document) => document.Objects.Select(o => o.Id);

        public string? Label(Scene document, string objectId) => document.Find(objectId) is not { } o ? null
            : o.Properties.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } name ? name : o.Id;

        public string? Parent(Scene document, string objectId) => document.Find(objectId)?.Parent;
    }
}

/// <summary>A pure translation proposal. Execute all commands with the captured revision and root identities,
/// never one command at a time. A refused plan contains no commands.</summary>
public sealed record SceneTranslationPlan(IReadOnlyList<ICommand<Scene>> Commands, IReadOnlyList<string> Roots,
    IReadOnlyList<Issue> Issues)
{
    /// <summary>Whether the complete proposal is valid, including an empty no-op.</summary>
    public bool Valid => !Issues.HasErrors();
}

/// <summary>Pure selected-root translation math over an immutable captured scene. No preview/session mutation.</summary>
public static class SceneTranslation
{
    /// <summary>Moves selected roots by one shared world delta. Descendants of selected ancestors move through
    /// their parent only. Order is document order; missing IDs, invalid scenes, singular parents and non-finite
    /// results refuse the whole proposal. The caller retains its capture revision for EditorModel.ExecuteAt.</summary>
    public static SceneTranslationPlan World(Scene scene, IEnumerable<string> selected, Vector3 delta)
    {
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(selected);
        SceneTranslationPlan Refuse(string message, string? id = null) => new([], [], [Issue.Error(message, id)]);
        if (!Finite(delta)) return Refuse("The translation delta must be finite.");
        var errors = new SceneValidator().Validate(scene);
        if (errors.HasErrors()) return new([], [], errors);
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids.Order(StringComparer.Ordinal))
            if (scene.Find(id) is null) return Refuse($"The selected object '{id}' no longer exists.", id);
        bool HasSelectedAncestor(SceneObject o)
        {
            for (var parent = o.Parent; parent is not null; parent = scene.Find(parent)!.Parent)
                if (ids.Contains(parent)) return true;
            return false;
        }
        var roots = scene.Objects.Where(o => ids.Contains(o.Id) && !HasSelectedAncestor(o)).ToArray();
        var commands = new List<ICommand<Scene>>();
        if (delta != Vector3.Zero)
            foreach (var root in roots)
            {
                var parent = root.Parent is { } id ? SceneViewport.World(scene, id) : Matrix4x4.Identity;
                if (!Matrix4x4.Invert(parent, out var inverse))
                    return Refuse($"The parent transform of '{root.Id}' is singular; translation cannot be represented.", root.Id);
                var position = root.Position + Vector3.TransformNormal(delta, inverse);
                if (!Finite(position)) return Refuse($"The translated position of '{root.Id}' is not finite.", root.Id);
                if (position != root.Position) commands.Add(new MoveObject(root.Id, position));
            }
        return new(commands.AsReadOnly(), Array.AsReadOnly(roots.Select(o => o.Id).ToArray()), []);
    }

    /// <summary>Converts axis/plane components in one explicitly captured rotation basis to a shared world delta.
    /// World mode supplies identity; local mode supplies the primary object's world rotation with scale removed.
    /// The caller chooses/captures that basis. Non-unit or non-finite rotations refuse, never silently normalize.</summary>
    public static SceneTranslationPlan InBasis(Scene scene, IEnumerable<string> selected, Vector3 components, Quaternion basis)
    {
        if (!float.IsFinite(basis.X) || !float.IsFinite(basis.Y) || !float.IsFinite(basis.Z) || !float.IsFinite(basis.W)
            || MathF.Abs(basis.LengthSquared() - 1) > 1e-4f)
            return new([], [], [Issue.Error("The captured translation basis must be a finite unit rotation with scale removed.")]);
        return World(scene, selected, Vector3.Transform(components, basis));
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

/// <summary>A pure selected-root scale proposal. Execute the entire batch against the captured revision.
/// Refusal never returns a partial list of commands.</summary>
public sealed record SceneScalePlan(IReadOnlyList<ICommand<Scene>> Commands, IReadOnlyList<string> Roots,
    IReadOnlyList<Issue> Issues)
{
    /// <summary>Whether the whole proposal is valid, including an empty no-op.</summary>
    public bool Valid => !Issues.HasErrors();
}

/// <summary>Pure scale arithmetic over the captured scene; no viewport, document or history mutation.</summary>
public static class SceneScale
{
    /// <summary>Multiplies each selected root's own local scale axes, about that root's origin.
    /// Equal factors give uniform scale; a negative factor mirrors that axis. Position and rotation stay exact.
    /// A selected descendant follows its selected ancestor once, without another local-scale command.
    /// This explicit local operation does not infer a shared pivot, world basis or TRS decomposition.
    /// Zero/non-finite factors or results, missing identities and invalid hierarchies refuse the whole plan.
    /// The caller must commit once using EditorModel.ExecuteAt with its captured revision and these roots.</summary>
    public static SceneScalePlan Local(Scene scene, IEnumerable<string> selected, Vector3 factors)
    {
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(selected);
        SceneScalePlan Refuse(string message, string? id = null) => new([], [], [Issue.Error(message, id)]);
        if (!Usable(factors)) return Refuse("Every scale factor must be finite and other than zero.");
        var errors = new SceneValidator().Validate(scene);
        if (errors.HasErrors()) return new([], [], errors);
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids.Order(StringComparer.Ordinal))
            if (scene.Find(id) is null) return Refuse($"The selected object '{id}' no longer exists.", id);
        bool HasSelectedAncestor(SceneObject item)
        {
            for (var parent = item.Parent; parent is not null; parent = scene.Find(parent)!.Parent)
                if (ids.Contains(parent)) return true;
            return false;
        }
        var roots = scene.Objects.Where(item => ids.Contains(item.Id) && !HasSelectedAncestor(item)).ToArray();
        var commands = new List<ICommand<Scene>>();
        foreach (var root in roots)
        {
            if (!Usable(root.Scale)) return Refuse($"The scale of '{root.Id}' must be finite and other than zero.", root.Id);
            var scale = root.Scale * factors;
            if (!Usable(scale)) return Refuse($"The proposed scale of '{root.Id}' is zero or not finite.", root.Id);
            var command = new ScaleObject(root.Id, scale);
            // Keep the existing command's admission rules, including its numeric-range limits.
            var proposed = command.Apply(scene);
            if (proposed.Issues.HasErrors()) return new([], [], proposed.Issues);
            if (scale != root.Scale) commands.Add(command);
        }
        return new(commands.AsReadOnly(), Array.AsReadOnly(roots.Select(root => root.Id).ToArray()), []);
    }

    private static bool Usable(Vector3 v) => v.X != 0 && v.Y != 0 && v.Z != 0
        && float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

/// <summary>A pure, atomic selected-root rotation proposal. Commit with the captured revision and roots.
/// A refused proposal contains no commands, even when an earlier root was representable.</summary>
public sealed record SceneRotationPlan(IReadOnlyList<ICommand<Scene>> Commands, IReadOnlyList<string> Roots,
    IReadOnlyList<Issue> Issues)
{
    /// <summary>Whether the whole proposal is valid, including a snapped no-op.</summary>
    public bool Valid => !Issues.HasErrors();
}

/// <summary>Selected-root rotation about an explicitly captured world pivot and shared orthonormal basis.
/// No handle, document, gesture-lifetime or host mutation.</summary>
public static class SceneRotation
{
    private const float RelativeTolerance = 1e-5f;

    /// <summary>Rotates around the unit axis expressed in the caller's captured rotation basis.
    /// The pivot is in world coordinates; radians are signed. Positive snap steps round AwayFromZero;
    /// nonpositive finite steps disable snapping. Descendants of selected ancestors follow once.
    /// Parent inversion and signed TRS reconstruction must preserve the target within 1e-5 relative,
    /// otherwise the whole proposal refuses. Captured scale signs are retained, never guessed.
    /// Caller rotations/axes are checked and never silently normalized. Commit once through ExecuteAt.</summary>
    public static SceneRotationPlan InBasis(Scene scene, IEnumerable<string> selected, Vector3 pivot,
        Quaternion basis, Vector3 axis, float radians, float snapStep = 0)
    {
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(selected);
        SceneRotationPlan Refuse(string message, string? id = null) => new([], [], [Issue.Error(message, id)]);
        if (!Finite(pivot) || !float.IsFinite(radians) || !float.IsFinite(snapStep))
            return Refuse("The captured pivot, rotation angle and snap step must be finite.");
        if (!Unit(basis)) return Refuse("The captured rotation basis must be a finite unit quaternion with scale removed.");
        if (!Finite(axis) || MathF.Abs(axis.LengthSquared() - 1) > 1e-4f)
            return Refuse("The rotation axis in the captured basis must be a finite unit vector.");
        var errors = new SceneValidator().Validate(scene);
        if (errors.HasErrors()) return new([], [], errors);
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids.Order(StringComparer.Ordinal))
            if (scene.Find(id) is null) return Refuse($"The selected object '{id}' no longer exists.", id);
        bool HasSelectedAncestor(SceneObject item)
        {
            for (var parent = item.Parent; parent is not null; parent = scene.Find(parent)!.Parent)
                if (ids.Contains(parent)) return true;
            return false;
        }
        var roots = scene.Objects.Where(item => ids.Contains(item.Id) && !HasSelectedAncestor(item)).ToArray();
        // Validate the complete participating parent chain, rather than normalizing malformed source rotations.
        foreach (var root in roots)
            for (SceneObject? item = root; item is not null; item = item.Parent is { } id ? scene.Find(id) : null)
            {
                if (!Finite(item.Position) || !UsableScale(item.Scale))
                    return Refuse($"The transform of '{item.Id}' needs finite positions and nonzero finite scale axes.", item.Id);
                if (!UnitSource(item.Rotation))
                    return Refuse($"The rotation of '{item.Id}' must be a finite unit quaternion.", item.Id);
            }
        var angle = snapStep > 0 ? (float)(Math.Round((double)radians / snapStep, MidpointRounding.AwayFromZero) * snapStep) : radians;
        if (!float.IsFinite(angle)) return Refuse("The snapped rotation angle is not finite.");
        var rootIds = Array.AsReadOnly(roots.Select(root => root.Id).ToArray());
        if (angle == 0 || roots.Length == 0) return new([], rootIds, []);
        var worldAxis = Vector3.Transform(axis, basis);
        var delta = Matrix4x4.CreateTranslation(-pivot)
            * Matrix4x4.CreateFromAxisAngle(worldAxis, angle) * Matrix4x4.CreateTranslation(pivot);
        if (!Finite(delta)) return Refuse("The rotation about this pivot cannot be represented with finite numbers.");
        var commands = new List<ICommand<Scene>>();
        foreach (var root in roots)
        {
            var parent = root.Parent is { } id ? SceneViewport.World(scene, id) : Matrix4x4.Identity;
            if (!Finite(parent) || !Matrix4x4.Invert(parent, out var inverse) || !Finite(inverse))
                return Refuse($"The parent transform of '{root.Id}' is singular; rotation cannot be represented.", root.Id);
            var targetWorld = SceneViewport.World(scene, root.Id) * delta;
            var target = targetWorld * inverse;
            if (!Finite(targetWorld) || !Finite(target))
                return Refuse($"The rotated transform of '{root.Id}' is not finite.", root.Id);
            if (!SignedTrs(target, root.Scale, root.Rotation, out var position, out var rotation, out var scale))
                return Refuse($"The rotated transform of '{root.Id}' contains shear or cannot be reconstructed faithfully as position, rotation and scale.", root.Id);
            var rebuilt = Compose(position, rotation, scale);
            if (!Close(rebuilt * parent, targetWorld))
                return Refuse($"The rotated transform of '{root.Id}' cannot preserve its world pose through this parent.", root.Id);
            var turn = new RotateObject(root.Id, rotation);
            var turnCheck = turn.Apply(scene);
            if (!turnCheck.Ok) return new([], [], turnCheck.Issues);
            var resize = new ScaleObject(root.Id, scale);
            var scaleCheck = resize.Apply(scene);
            if (!scaleCheck.Ok) return new([], [], scaleCheck.Issues);
            if (position != root.Position) commands.Add(new MoveObject(root.Id, position));
            if (rotation != root.Rotation) commands.Add(turn);
            if (scale != root.Scale) commands.Add(resize);
        }
        return new(commands.AsReadOnly(), rootIds, []);
    }

    internal static bool SignedTrs(Matrix4x4 target, Vector3 oldScale, Quaternion oldRotation,
        out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = target.Translation; rotation = default; scale = default;
        if (MathF.Abs(target.M14) > RelativeTolerance || MathF.Abs(target.M24) > RelativeTolerance
            || MathF.Abs(target.M34) > RelativeTolerance || MathF.Abs(target.M44 - 1) > RelativeTolerance) return false;
        // System.Numerics uses row vectors: these rows are the basis columns of the equivalent
        // column-vector transform. Test their relative orthogonality, independent of translation/scale magnitude.
        var x = new Vector3(target.M11, target.M12, target.M13);
        var y = new Vector3(target.M21, target.M22, target.M23);
        var z = new Vector3(target.M31, target.M32, target.M33);
        scale = new(MathF.CopySign(Length(x), oldScale.X), MathF.CopySign(Length(y), oldScale.Y), MathF.CopySign(Length(z), oldScale.Z));
        if (!UsableScale(scale)) return false;
        x /= scale.X; y /= scale.Y; z /= scale.Z;
        if (MathF.Abs(Vector3.Dot(x, y)) > RelativeTolerance || MathF.Abs(Vector3.Dot(x, z)) > RelativeTolerance
            || MathF.Abs(Vector3.Dot(y, z)) > RelativeTolerance) return false;
        var matrix = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        if (MathF.Abs(matrix.GetDeterminant() - 1) > RelativeTolerance) return false;
        rotation = Quaternion.CreateFromRotationMatrix(matrix);
        if (!UnitSource(rotation)) return false;
        if (Quaternion.Dot(rotation, oldRotation) < 0) rotation = new(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
        // Keep the exact captured magnitudes when differences are only reconstruction roundoff.
        if (Close(Compose(position, rotation, oldScale), target)) scale = oldScale;
        return Close(Compose(position, rotation, scale), target);
    }

    internal static Matrix4x4 Compose(Vector3 p, Quaternion q, Vector3 s) =>
        Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(p);

    internal static bool Close(Matrix4x4 actual, Matrix4x4 target)
    {
        bool Row(Vector3 a, Vector3 b)
        {
            var size = Math.Max(Math.Abs((double)b.X), Math.Max(Math.Abs((double)b.Y), Math.Abs((double)b.Z)));
            var error = Math.Max(Math.Abs((double)a.X - b.X), Math.Max(Math.Abs((double)a.Y - b.Y), Math.Abs((double)a.Z - b.Z)));
            return error <= RelativeTolerance * size;
        }
        bool Scalar(float a, float b) => Math.Abs((double)a - b) <= RelativeTolerance * Math.Max(1, Math.Abs((double)b));
        return Finite(actual) && Finite(target)
            && Row(new(actual.M11, actual.M12, actual.M13), new(target.M11, target.M12, target.M13))
            && Row(new(actual.M21, actual.M22, actual.M23), new(target.M21, target.M22, target.M23))
            && Row(new(actual.M31, actual.M32, actual.M33), new(target.M31, target.M32, target.M33))
            && Scalar(actual.M41, target.M41) && Scalar(actual.M42, target.M42) && Scalar(actual.M43, target.M43)
            && Scalar(actual.M14, target.M14) && Scalar(actual.M24, target.M24) && Scalar(actual.M34, target.M34)
            && Scalar(actual.M44, target.M44);
    }

    private static float Length(Vector3 v) => (float)Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);
    private static bool Unit(Quaternion q) => Finite(q) && MathF.Abs(q.LengthSquared() - 1) <= 1e-4f;
    // Retain RotateObject's existing strict input threshold; do not widen it in the planner.
    internal static bool UnitSource(Quaternion q) => Finite(q) && float.IsFinite(q.Length()) && MathF.Abs(q.Length() - 1) <= 1e-3f;
    private static bool Finite(Quaternion q) => float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W);
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    internal static bool UsableScale(Vector3 s) => Finite(s) && s.X != 0 && s.Y != 0 && s.Z != 0;
    internal static bool Finite(Matrix4x4 m) =>
        Finite(new Vector3(m.M11, m.M12, m.M13)) && Finite(new Vector3(m.M21, m.M22, m.M23))
        && Finite(new Vector3(m.M31, m.M32, m.M33)) && Finite(m.Translation)
        && float.IsFinite(m.M14) && float.IsFinite(m.M24) && float.IsFinite(m.M34) && float.IsFinite(m.M44);
}

/// <summary>A pure reparent proposal with an explicit pose choice. Commit the whole batch with the captured revision.</summary>
public sealed record SceneReparentPlan(IReadOnlyList<ICommand<Scene>> Commands, IReadOnlyList<string> Roots,
    IReadOnlyList<Issue> Issues)
{
    /// <summary>Whether all hierarchy and transform checks passed, including an empty no-op.</summary>
    public bool Valid => !Issues.HasErrors();
}

/// <summary>Deliberate reparent planning; the original SetParent command keeps its local-pose behavior.</summary>
public static class SceneReparent
{
    /// <summary>Reparents selected roots once, to an existing parent or null for the top level.
    /// Keep-local preserves every local field exactly. Preserve-world reconstructs signed local TRS through
    /// the new parent's inverse using the same 1e-5 relative reconstruction policy as SceneRotation.
    /// Cycles, missing identities, singular transforms and shear refuse the whole batch.
    /// A parent handedness change currently refuses rather than guessing which local scale axis to reflect.
    /// No document, selection, history or files are changed by planning.</summary>
    public static SceneReparentPlan To(Scene scene, IEnumerable<string> selected, string? parent, bool preserveWorld)
    {
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(selected);
        SceneReparentPlan Refuse(string message, string? id = null) => new([], [], [Issue.Error(message, id)]);
        var errors = new SceneValidator().Validate(scene);
        if (errors.HasErrors()) return new([], [], errors);
        if (parent is not null && scene.Find(parent) is null)
            return Refuse($"The destination parent '{parent}' no longer exists.", parent);
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids.Order(StringComparer.Ordinal))
            if (scene.Find(id) is null) return Refuse($"The selected object '{id}' no longer exists.", id);
        bool HasSelectedAncestor(SceneObject item)
        {
            for (var id = item.Parent; id is not null; id = scene.Find(id)!.Parent)
                if (ids.Contains(id)) return true;
            return false;
        }
        var roots = scene.Objects.Where(item => ids.Contains(item.Id) && !HasSelectedAncestor(item)).ToArray();
        var changes = roots.Where(root => root.Parent != parent).ToArray();
        var hierarchy = scene;
        foreach (var root in changes) hierarchy = new SetParent(root.Id, parent).Apply(hierarchy).Document!;
        var hierarchyErrors = new SceneValidator().Validate(hierarchy);
        if (hierarchyErrors.HasErrors()) return new([], [], hierarchyErrors);
        var rootIds = Array.AsReadOnly(roots.Select(root => root.Id).ToArray());
        if (changes.Length == 0) return new([], rootIds, []);
        var commands = new List<ICommand<Scene>>();
        if (!preserveWorld)
        {
            foreach (var root in changes) commands.Add(new SetParent(root.Id, parent));
            return new(commands.AsReadOnly(), rootIds, []);
        }
        string? ChainProblem(SceneObject? start)
        {
            for (var item = start; item is not null; item = item.Parent is { } id ? scene.Find(id) : null)
            {
                var p = item.Position;
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
                    || !SceneRotation.UsableScale(item.Scale))
                    return $"The transform of '{item.Id}' needs finite positions and nonzero finite scale axes.";
                if (!SceneRotation.UnitSource(item.Rotation))
                    return $"The rotation of '{item.Id}' must be a finite unit quaternion.";
            }
            return null;
        }
        if (ChainProblem(parent is null ? null : scene.Find(parent)) is { } badParent) return Refuse(badParent, parent);
        var newParentWorld = parent is null ? Matrix4x4.Identity : SceneViewport.World(scene, parent);
        if (!SceneRotation.Finite(newParentWorld) || !Matrix4x4.Invert(newParentWorld, out var inverse) || !SceneRotation.Finite(inverse))
            return Refuse("The destination parent is singular or outside the supported numeric range; its world pose cannot be preserved.", parent);
        foreach (var root in changes)
        {
            if (ChainProblem(root) is { } invalid) return Refuse(invalid, root.Id);
            var world = SceneViewport.World(scene, root.Id);
            var target = world * inverse;
            if (!SceneRotation.Finite(world) || !SceneRotation.Finite(target))
                return Refuse($"The preserved transform of '{root.Id}' is not finite.", root.Id);
            var oldSign = Math.Sign((double)root.Scale.X * root.Scale.Y * root.Scale.Z);
            var determinant = (double)target.M11 * (target.M22 * (double)target.M33 - target.M23 * (double)target.M32)
                - target.M12 * (target.M21 * (double)target.M33 - target.M23 * (double)target.M31)
                + target.M13 * (target.M21 * (double)target.M32 - target.M22 * (double)target.M31);
            if (determinant != 0 && Math.Sign(determinant) != oldSign)
                return Refuse($"Preserving '{root.Id}' here would change its local reflection signs; choose keep-local or a parent with the same handedness.", root.Id);
            if (!SceneRotation.SignedTrs(target, root.Scale, root.Rotation, out var position, out var rotation, out var scale)
                || !SceneRotation.Close(SceneRotation.Compose(position, rotation, scale) * newParentWorld, world))
                return Refuse($"The preserved transform of '{root.Id}' contains shear or cannot be reconstructed faithfully as position, rotation and scale.", root.Id);
            var turn = new RotateObject(root.Id, rotation); var rotationCheck = turn.Apply(scene);
            if (!rotationCheck.Ok) return new([], [], rotationCheck.Issues);
            var resize = new ScaleObject(root.Id, scale); var scaleCheck = resize.Apply(scene);
            if (!scaleCheck.Ok) return new([], [], scaleCheck.Issues);
            commands.Add(new SetParent(root.Id, parent));
            if (position != root.Position) commands.Add(new MoveObject(root.Id, position));
            if (rotation != root.Rotation) commands.Add(turn);
            if (scale != root.Scale) commands.Add(resize);
        }
        return new(commands.AsReadOnly(), rootIds, []);
    }
}
