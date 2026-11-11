using System.Numerics;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class SceneGestureScaleTests
{
    private static EditorModel<Scene> Editor(Scene scene) => new(new DocumentSession<Scene>(
        new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline);

    [Theory]
    [InlineData(2, 2, 2)] [InlineData(3, 1, 1)] [InlineData(1, .5f, 1)]
    [InlineData(-1, 1, 1)] [InlineData(-2, -3, -4)]
    public void Local_factors_preserve_origins_rotations_and_unselected_objects(float x, float y, float z)
    {
        var parent = SceneObject.At("parent", null, new(10, 20, 30)) with
        { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f), Scale = new(-2, 3, 4) };
        var child = SceneObject.At("child", null, new(1, 2, 3)) with
        { Parent = "parent", Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .3f), Scale = new(2, 4, -8) };
        var other = SceneObject.At("other", null, new(4, 5, 6));
        var scene = new Scene([parent, child, other]); var e = Editor(scene);
        var factors = new Vector3(x, y, z);
        var plan = SceneScale.Local(scene, ["child"], factors);
        Assert.True(plan.Valid); Assert.Equal(["child"], plan.Roots);
        Assert.Single(plan.Commands); Assert.Same(scene, e.Session.Current); Assert.Empty(e.Session.Journal);
        Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Equal(child with { Scale = child.Scale * factors }, e.Session.Current.Find("child"));
        Assert.Equal(parent, e.Session.Current.Find("parent")); Assert.Equal(other, e.Session.Current.Find("other"));
        Assert.Equal(SceneViewport.World(scene, "child").Translation, SceneViewport.World(e.Session.Current, "child").Translation);
        Assert.Single(e.Session.Journal); Assert.Equal(1, e.Session.History.UndoCount);
        Assert.True(e.Session.Undo()); Assert.Equal(scene, e.Session.Current);
        Assert.True(e.Session.Redo()); Assert.Equal(child.Scale * factors, e.Session.Current.Find("child")!.Scale);
    }

    [Fact]
    public void Selected_roots_are_scaled_once_in_document_order_and_descendants_keep_their_local_pose()
    {
        var parent = SceneObject.At("parent", null, new(1, 2, 3));
        var child = SceneObject.At("child", null, new(4, 0, 0)) with { Parent = "parent" };
        var grandchild = SceneObject.At("grandchild", null, Vector3.UnitY) with { Parent = "child" };
        var other = SceneObject.At("other", null, new(8, 9, 10));
        var scene = new Scene([parent, child, grandchild, other]); var e = Editor(scene);
        var plan = SceneScale.Local(scene, ["grandchild", "other", "child", "parent", "other"], new(2));
        Assert.True(plan.Valid); Assert.Equal(["parent", "other"], plan.Roots); Assert.Equal(2, plan.Commands.Count);
        Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Equal(child, e.Session.Current.Find("child")); Assert.Equal(grandchild, e.Session.Current.Find("grandchild"));
        Assert.Equal(new Vector3(9, 2, 3), SceneViewport.World(e.Session.Current, "child").Translation);
        Assert.Equal(other.Position, e.Session.Current.Find("other")!.Position);
        Assert.Equal(new Vector3(2), e.Session.Current.Find("other")!.Scale);
        Assert.Single(e.Session.Journal); Assert.Equal(1, e.Session.History.UndoCount);
        Assert.True(e.Session.Undo()); Assert.Equal(scene, e.Session.Current);
    }

    [Fact]
    public void Zero_nonfinite_and_overflow_or_underflow_refuse_the_whole_proposal()
    {
        var scene = new Scene([SceneObject.At("first", null, Vector3.Zero),
            SceneObject.At("large", null, Vector3.One) with { Scale = new(float.MaxValue, 1, 1) }]);
        foreach (var factors in new[] { Vector3.Zero, new Vector3(1, -0f, 1), new Vector3(float.NaN, 1, 1),
            new Vector3(1, float.PositiveInfinity, 1), new Vector3(2, 1, 1) })
        {
            var plan = SceneScale.Local(scene, ["first", "large"], factors);
            Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.NotEmpty(plan.Issues);
        }
        var tiny = new Scene([SceneObject.At("tiny", null, Vector3.Zero) with { Scale = new(float.Epsilon, 1, 1) }]);
        var underflow = SceneScale.Local(tiny, ["tiny"], new(.5f, 1, 1));
        Assert.False(underflow.Valid); Assert.Empty(underflow.Commands);
        var invalid = scene with { Objects = scene.Objects.SetItem(1, scene.Objects[1] with { Scale = new(1, 0, 1) }) };
        var zeroSource = SceneScale.Local(invalid, ["large"], Vector3.One);
        Assert.False(zeroSource.Valid); Assert.Empty(zeroSource.Commands);
    }

    [Fact]
    public void Existing_command_numeric_range_is_not_silently_widened()
    {
        var scene = new Scene([SceneObject.At("large", null, Vector3.Zero)]);
        var plan = SceneScale.Local(scene, ["large"], new(float.MaxValue));
        Assert.False(plan.Valid); Assert.Empty(plan.Commands);
        Assert.Contains("finite", Assert.Single(plan.Issues).Message);
    }

    [Fact]
    public void Missing_selection_and_invalid_hierarchy_refuse_without_partial_commands()
    {
        var scene = new Scene([SceneObject.At("first", null, Vector3.Zero)]);
        var missing = SceneScale.Local(scene, ["first", "missing"], new(2));
        Assert.False(missing.Valid); Assert.Empty(missing.Commands);
        var cycle = scene with { Objects = scene.Objects.SetItem(0, scene.Objects[0] with { Parent = "first" }) };
        var invalid = SceneScale.Local(cycle, ["first"], new(2));
        Assert.False(invalid.Valid); Assert.Empty(invalid.Commands);
    }

    [Fact]
    public void Identity_and_empty_selection_produce_no_history()
    {
        var scene = new Scene([SceneObject.At("first", null, Vector3.Zero) with { Scale = new(-2, 3, 4) }]);
        var e = Editor(scene);
        foreach (var plan in new[] { SceneScale.Local(scene, ["first"], Vector3.One), SceneScale.Local(scene, [], new(2)) })
        {
            Assert.True(plan.Valid); Assert.Empty(plan.Commands);
            Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
            Assert.Same(scene, e.Session.Current); Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
        }
    }

    [Fact]
    public void A_plan_cannot_commit_after_a_revision_change_or_outside_focus()
    {
        var scene = new Scene([SceneObject.At("first", null, Vector3.Zero), SceneObject.At("other", null, Vector3.One)]);
        var e = Editor(scene); var revision = e.Session.Revision;
        var plan = SceneScale.Local(scene, ["first", "other"], new(2));
        Assert.True(plan.Valid); Assert.True(e.Execute(new MoveObject("first", Vector3.UnitX), "first"));
        var changed = e.Session.Current; var count = e.Session.Journal.Count;
        Assert.False(e.ExecuteAt(revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Same(changed, e.Session.Current); Assert.Equal(count, e.Session.Journal.Count);
        Assert.True(e.FocusOn("first"));
        Assert.False(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Same(changed, e.Session.Current); Assert.Equal(count, e.Session.Journal.Count);
    }
}
