using System.Numerics;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class SceneGestureRotationTests
{
    private static EditorModel<Scene> Editor(Scene scene) => new(new DocumentSession<Scene>(
        new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline);
    private static SceneRotationPlan Turn(Scene scene, string[] ids, float angle, float snap = 0) =>
        SceneRotation.InBasis(scene, ids, Vector3.Zero, Quaternion.Identity, Vector3.UnitZ, angle, snap);
    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-5f) =>
        Assert.InRange(Vector3.Distance(expected, actual), 0, tolerance);
    private static void Near(Matrix4x4 expected, Matrix4x4 actual)
    {
        float[] Values(Matrix4x4 m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
            m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        var a = Values(expected); var b = Values(actual);
        for (var i = 0; i < a.Length; i++) Assert.InRange(MathF.Abs(a[i] - b[i]), 0, 1e-5f * MathF.Max(1, MathF.Abs(a[i])));
    }
    private static void Valid(SceneRotationPlan plan) => Assert.True(plan.Valid, string.Join(" ", plan.Issues.Select(i => i.Message)));

    [Fact]
    public void Explicit_pivot_rotates_selected_roots_once_and_one_undo_restores_the_exact_scene()
    {
        var parent = SceneObject.At("parent", null, new(2, 1, 0));
        var child = SceneObject.At("child", null, new(2, 0, 0)) with { Parent = "parent" };
        var other = SceneObject.At("other", null, new(0, 1, 0));
        var untouched = SceneObject.At("untouched", null, new(20, 30, 40));
        var scene = new Scene([parent, child, other, untouched]); var e = Editor(scene);
        var pivot = new Vector3(1, 1, 0);
        var plan = SceneRotation.InBasis(scene, ["child", "other", "parent", "parent"], pivot, Quaternion.Identity, Vector3.UnitZ, MathF.PI / 2);
        Valid(plan); Assert.Equal(["parent", "other"], plan.Roots);
        Assert.Same(scene, e.Session.Current); Assert.Empty(e.Session.Journal);
        Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Near(new(1, 2, 0), e.Session.Current.Find("parent")!.Position);
        Near(new(1, 0, 0), e.Session.Current.Find("other")!.Position);
        Near(new(1, 4, 0), SceneViewport.World(e.Session.Current, "child").Translation);
        Assert.Equal(child, e.Session.Current.Find("child")); Assert.Equal(untouched, e.Session.Current.Find("untouched"));
        Assert.Single(e.Session.Journal); Assert.Equal(1, e.Session.History.UndoCount);
        var rotated = e.Session.Current; Assert.True(e.Session.Undo()); Assert.Equal(scene, e.Session.Current);
        Assert.True(e.Session.Redo()); Assert.Equal(rotated, e.Session.Current);
    }

    [Fact]
    public void Captured_shared_basis_maps_the_axis_into_world_space_without_choosing_an_object_default()
    {
        var pivot = new Vector3(3, 4, 5);
        var scene = new Scene([SceneObject.At("first", null, pivot + Vector3.UnitY),
            SceneObject.At("second", null, pivot + 2 * Vector3.UnitY) with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f) }]);
        var basis = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var plan = SceneRotation.InBasis(scene, ["first", "second"], pivot, basis, Vector3.UnitZ, MathF.PI / 2);
        Valid(plan); var e = Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Near(pivot + Vector3.UnitZ, e.Session.Current.Find("first")!.Position);
        Near(pivot + 2 * Vector3.UnitZ, e.Session.Current.Find("second")!.Position);
        Near(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, e.Session.Current.Find("first")!.Rotation));
        Assert.Equal(Vector3.One, e.Session.Current.Find("first")!.Scale);
    }

    [Theory]
    [InlineData(.5f, 1f)] [InlineData(-.5f, -1f)]
    public void Signed_midpoint_snap_is_away_from_zero_in_radians(float angle, float expected)
    {
        var scene = new Scene([SceneObject.At("root", null, Vector3.UnitX)]);
        var plan = Turn(scene, ["root"], angle, 1); Valid(plan);
        var e = Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Near(new(MathF.Cos(expected), MathF.Sin(expected), 0), e.Session.Current.Find("root")!.Position);
    }

    [Theory]
    [InlineData(0f)] [InlineData(-2f)]
    public void Nonpositive_snap_step_is_off(float step)
    {
        var scene = new Scene([SceneObject.At("root", null, Vector3.UnitX)]);
        var plan = Turn(scene, ["root"], .4f, step); Valid(plan);
        var e = Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Near(new(MathF.Cos(.4f), MathF.Sin(.4f), 0), e.Session.Current.Find("root")!.Position);
    }

    [Fact]
    public void Snapped_zero_and_empty_selection_are_pure_noops()
    {
        var scene = new Scene([SceneObject.At("root", null, Vector3.UnitX)]); var e = Editor(scene);
        foreach (var plan in new[] { Turn(scene, ["root"], .4f, 1), Turn(scene, [], 1) })
        {
            Valid(plan); Assert.Empty(plan.Commands);
            Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
            Assert.Same(scene, e.Session.Current); Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
        }
    }

    [Theory]
    [InlineData(-2f,3f,4f)] [InlineData(2f,-3f,4f)] [InlineData(2f,3f,-4f)]
    [InlineData(-2f,-3f,4f)] [InlineData(-2f,-3f,-4f)]
    public void Rotating_mirrors_preserves_each_captured_scale_sign_and_exact_magnitudes(float x, float y, float z)
    {
        var item = SceneObject.At("root", null, Vector3.UnitX) with
        { Scale = new(x, y, z), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .3f) };
        var scene = new Scene([item]); var plan = Turn(scene, ["root"], .7f); Valid(plan);
        var e = Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Equal(item.Scale, e.Session.Current.Find("root")!.Scale);
        Assert.InRange(MathF.Abs(e.Session.Current.Find("root")!.Rotation.Length() - 1), 0, 1e-5f);
        Near(SceneViewport.World(scene, "root") * Matrix4x4.CreateRotationZ(.7f), SceneViewport.World(e.Session.Current, "root"));
    }

    [Fact]
    public void Rotated_uniform_mirrored_parent_is_supported_without_changing_that_parent()
    {
        var parent = SceneObject.At("parent", null, new(4, 5, 6)) with
        { Scale = new(-2, 2, 2), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .4f) };
        var child = SceneObject.At("child", null, new(1, 2, 3)) with { Parent = "parent", Scale = new(-1, 2, -3) };
        var scene = new Scene([parent, child]); var before = SceneViewport.World(scene, "child");
        var plan = Turn(scene, ["child"], .6f); Valid(plan);
        var e = Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Equal(parent, e.Session.Current.Find("parent")); Assert.Equal(child.Scale, e.Session.Current.Find("child")!.Scale);
        Near(before * Matrix4x4.CreateRotationZ(.6f), SceneViewport.World(e.Session.Current, "child"));
    }

    [Fact]
    public void A_nonuniform_parent_quarter_turn_reconstructs_representable_scale_without_losing_signs()
    {
        var parent = SceneObject.At("parent", null, Vector3.Zero) with { Scale = new(2, 1, 1) };
        var child = SceneObject.At("child", null, Vector3.UnitX) with { Parent = "parent", Scale = new(-3, 4, 5) };
        var scene = new Scene([parent, child]); var plan = Turn(scene, ["child"], MathF.PI / 2); Valid(plan);
        var e = Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Near(new(-6, 2, 5), e.Session.Current.Find("child")!.Scale);
        Near(new(0, 2, 0), e.Session.Current.Find("child")!.Position);
        Assert.Equal(parent, e.Session.Current.Find("parent"));
        Near(SceneViewport.World(scene, "child") * Matrix4x4.CreateRotationZ(MathF.PI / 2), SceneViewport.World(e.Session.Current, "child"));
    }

    [Theory]
    [InlineData(0f)] [InlineData(10000000f)]
    public void Nonrepresentable_shear_refuses_the_whole_batch_and_large_translation_cannot_hide_it(float translation)
    {
        var scene = new Scene([SceneObject.At("free", null, Vector3.UnitX),
            SceneObject.At("parent", null, new(translation, translation, 0)) with { Scale = new(2, 1, 1) },
            SceneObject.At("child", null, Vector3.UnitX) with { Parent = "parent" }]);
        var e = Editor(scene); var plan = Turn(scene, ["free", "child"], MathF.PI / 4);
        Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.Contains("shear", Assert.Single(plan.Issues).Message);
        Assert.Same(scene, e.Session.Current); Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
    }

    [Theory]
    [InlineData(.000002f, true)] [InlineData(.00008f, false)]
    public void Relative_orthogonality_and_reconstruction_tolerance_are_bounded(float stretch, bool accepted)
    {
        var scene = new Scene([SceneObject.At("parent", null, Vector3.Zero) with { Scale = new(1 + stretch, 1, 1) },
            SceneObject.At("child", null, Vector3.UnitX) with { Parent = "parent" }]);
        var plan = Turn(scene, ["child"], MathF.PI / 4);
        Assert.Equal(accepted, plan.Valid);
        if (!accepted) Assert.Empty(plan.Commands);
        else
        {
            var e = Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
            Near(SceneViewport.World(scene, "child") * Matrix4x4.CreateRotationZ(MathF.PI / 4), SceneViewport.World(e.Session.Current, "child"));
        }
    }

    [Fact]
    public void Invalid_basis_axis_pivot_angle_and_step_refuse_instead_of_being_normalized()
    {
        var scene = new Scene([SceneObject.At("root", null, Vector3.Zero)]);
        foreach (var basis in new[] { default(Quaternion), new Quaternion(0, 0, 0, 2), new Quaternion(float.NaN, 0, 0, 1) })
        {
            var plan = SceneRotation.InBasis(scene, ["root"], Vector3.Zero, basis, Vector3.UnitZ, 1);
            Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.Contains("unit quaternion", Assert.Single(plan.Issues).Message);
        }
        foreach (var plan in new[] {
            SceneRotation.InBasis(scene, ["root"], Vector3.Zero, Quaternion.Identity, new(0,0,2), 1),
            SceneRotation.InBasis(scene, ["root"], new(float.NaN,0,0), Quaternion.Identity, Vector3.UnitZ, 1),
            Turn(scene, ["root"], float.PositiveInfinity), Turn(scene, ["root"], 1, float.NaN) })
        { Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.NotEmpty(plan.Issues); }
    }

    [Fact]
    public void Invalid_source_rotations_zero_scale_and_unrepresentable_parent_inverse_refuse_all_roots()
    {
        foreach (var parent in new[] {
            SceneObject.At("parent", null, Vector3.Zero) with { Rotation = new(0,0,0,2) },
            SceneObject.At("parent", null, Vector3.Zero) with { Scale = new(0,1,1) },
            SceneObject.At("parent", null, Vector3.Zero) with { Scale = new(float.Epsilon) } })
        {
            var scene = new Scene([SceneObject.At("free", null, Vector3.Zero), parent,
                SceneObject.At("child", null, Vector3.UnitX) with { Parent = "parent" }]);
            var plan = Turn(scene, ["free", "child"], .4f);
            Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.NotEmpty(plan.Issues);
        }
        var invalidRoot = new Scene([SceneObject.At("root", null, Vector3.Zero) with { Rotation = new(0,0,0,2) }]);
        Assert.False(Turn(invalidRoot, ["root"], .4f).Valid);
    }

    [Fact]
    public void Missing_identity_cycle_and_stale_revision_never_leave_partial_edits()
    {
        var scene = new Scene([SceneObject.At("root", null, Vector3.UnitX)]);
        var missing = Turn(scene, ["root", "gone"], 1); Assert.False(missing.Valid); Assert.Empty(missing.Commands);
        var cycle = scene with { Objects = scene.Objects.SetItem(0, scene.Objects[0] with { Parent = "root" }) };
        Assert.False(Turn(cycle, ["root"], 1).Valid);
        var plan = Turn(scene, ["root"], 1); Valid(plan); var e = Editor(scene); var revision = e.Session.Revision;
        Assert.True(e.Execute(new MoveObject("root", Vector3.UnitY), "root")); var changed = e.Session.Current;
        Assert.False(e.ExecuteAt(revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Same(changed, e.Session.Current); Assert.Single(e.Session.Journal); Assert.Equal(1, e.Session.History.UndoCount);
    }
}
