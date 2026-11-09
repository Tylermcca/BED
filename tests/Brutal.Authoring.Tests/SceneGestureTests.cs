using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneGestureTests
{
    private static EditorModel<Scene> Editor() => new(new DocumentSession<Scene>(new SceneFormat(),
        new SceneValidator(), SceneKit.Catalog(), new Scene([SceneObject.At("cube", null, Vector3.Zero)])), SceneEditing.Outline);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Preview_and_cancel_or_return_to_start_do_not_edit(bool noOp)
    {
        var e = Editor(); var source = new SceneViewport(); var before = e.Session.Current;
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.UnitX, source));
        Assert.Same(before, e.Session.Current); Assert.False(e.Session.Dirty);
        Assert.Empty(e.Session.Journal); Assert.False(e.Session.History.CanUndo);
        if (noOp) { Assert.True(g.Preview(Vector3.Zero, source)); Assert.False(g.Commit(source)); }
        else { g.Cancel(); Assert.False(g.Commit(source)); }
        Assert.False(g.Active); Assert.Same(before, e.Session.Current);
        Assert.Empty(e.Session.Journal); Assert.Equal(0, e.Session.Revision);
    }

    [Fact]
    public void Many_previews_commit_once_and_undo_redo_exactly()
    {
        var e = Editor(); var source = new SceneViewport();
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        for (var i = 1; i <= 8; i++) Assert.True(g.Preview(new(i, 0, 0), source));
        Assert.True(g.Commit(source)); Assert.False(g.Commit(source));
        Assert.Single(e.Session.Journal); Assert.Equal(1, e.Session.History.UndoCount);
        Assert.Equal(new Vector3(8, 0, 0), e.Session.Current.Find("cube")!.Position);
        Assert.True(e.Session.Undo()); Assert.Equal(Vector3.Zero, e.Session.Current.Find("cube")!.Position);
        Assert.False(e.Session.Dirty); Assert.True(e.Session.Redo());
        Assert.Equal(new Vector3(8, 0, 0), e.Session.Current.Find("cube")!.Position);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Intervening_edit_even_if_undone_cancels_capture(bool undo)
    {
        var e = Editor(); var source = new SceneViewport();
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.UnitX, source));
        Assert.True(e.Execute(new MoveObject("cube", Vector3.UnitY), "cube"));
        if (undo) Assert.True(e.Session.Undo());
        var before = e.Session.Current; var count = e.Session.Journal.Count;
        Assert.False(g.Active); Assert.False(g.Commit(source)); Assert.Same(before, e.Session.Current);
        Assert.Equal(count, e.Session.Journal.Count);
    }

    [Fact]
    public void Undo_of_prior_edit_cancels_capture()
    {
        var e = Editor(); var source = new SceneViewport();
        Assert.True(e.Execute(new MoveObject("cube", Vector3.UnitY), "cube"));
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.UnitX, source)); Assert.True(e.Session.Undo());
        Assert.False(g.Active); Assert.False(g.Commit(source));
        Assert.Equal(Vector3.Zero, e.Session.Current.Find("cube")!.Position);
    }

    [Fact]
    public void Play_then_stop_never_revives_old_gesture()
    {
        var e = Editor(); var source = new SceneViewport();
        e.Add(new PlayMode<Scene>("trial", "Trial", _ => new Trial()));
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.UnitX, source)); Assert.True(e.Play("trial"));
        Assert.False(g.Active); Assert.Null(ViewportGesture<Scene>.Begin(e, source, "cube"));
        Assert.True(e.Stop()); Assert.False(g.Commit(source)); Assert.Empty(e.Session.Journal);
        Assert.Equal(Vector3.Zero, e.Session.Current.Find("cube")!.Position);
    }

    [Fact]
    public void Source_replacement_and_invalid_number_refuse_without_history()
    {
        var e = Editor(); var source = new SceneViewport();
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.False(g.Preview(Vector3.One, new SceneViewport())); Assert.False(g.Active);
        using var other = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.False(other.Preview(new(float.NaN, 0, 0), source)); Assert.False(other.Commit(source));
        Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
        Assert.Null(ViewportGesture<Scene>.Begin(e, source, "absent"));
    }

    [Fact]
    public void Removed_object_cannot_be_committed_and_dispose_is_idempotent()
    {
        var e = Editor(); var source = new SceneViewport();
        var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.One, source)); Assert.True(e.Execute(new RemoveObject("cube"), "cube"));
        Assert.False(g.Commit(source)); g.Dispose(); g.Dispose();
        Assert.Single(e.Session.Journal); Assert.Empty(e.Session.Current.Objects);
    }

    [Fact]
    public void Escape_during_pointer_drag_cancels_release_without_an_undo_step()
    {
        var e = Editor(); var view = new ViewportView<Scene>(e, new SceneViewport());
        using var host = new HeadlessHost(900, 700) { Drawing = false };
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(900, 700));
            ImGui.Begin("View"u8, ImGuiWindowFlags.NoDecoration); view.Draw(); ImGui.End();
        }
        host.Frames(Draw, 4);
        var from = view.ScreenOf(Vector3.Zero)!.Value;
        var to = view.ScreenOf(new Vector3(2, 0, 1))!.Value;
        host.Click(Draw, from.X, from.Y); Assert.Equal(["cube"], e.Selection);
        host.MoveMouse(Draw, from.X, from.Y);
        ImGui.GetIO().AddMouseButtonEvent(0, true); host.Frames(Draw, 2);
        host.MoveMouse(Draw, to.X, to.Y); host.Frames(Draw, 2);
        Assert.Equal(Vector3.Zero, e.Session.Current.Find("cube")!.Position);
        host.Press(Draw, ImGuiKey.Escape);
        ImGui.GetIO().AddMouseButtonEvent(0, false); host.Frames(Draw, 2);
        Assert.Equal(Vector3.Zero, e.Session.Current.Find("cube")!.Position);
        Assert.Empty(e.Session.Journal); Assert.False(e.Session.History.CanUndo); Assert.False(e.Session.Dirty);
        // The same move succeeds without Escape, so this is not a missed hit or inert input fixture.
        host.Drag(Draw, new float2(from.X, from.Y), new float2(to.X, to.Y));
        Assert.NotEqual(Vector3.Zero, e.Session.Current.Find("cube")!.Position);
        Assert.Single(e.Session.Journal); Assert.Equal(1, e.Session.History.UndoCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Selection_or_focus_change_then_return_does_not_revive(bool focus)
    {
        var e = Editor(); var source = new SceneViewport(); e.Select("cube");
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.One, source));
        if (focus) { Assert.True(e.FocusOn("cube")); Assert.True(e.FocusOut()); }
        else { e.Select(); e.Select("cube"); }
        Assert.False(g.Active); Assert.False(g.Commit(source));
        Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
    }

    [Theory]
    [InlineData(false, true, false)] [InlineData(true, false, false)]
    public void Lost_input_capture_cancels_without_edit(bool focus, bool down, bool released)
    {
        var e = Editor(); var source = new SceneViewport();
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.One, source));
        Assert.False(g.ValidateInput(focus, down, released)); Assert.False(g.Commit(source));
        Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
    }

    [Fact]
    public void Normal_release_is_still_allowed()
    {
        var e = Editor(); var source = new SceneViewport();
        using var g = ViewportGesture<Scene>.Begin(e, source, "cube")!;
        Assert.True(g.Preview(Vector3.One, source)); Assert.True(g.ValidateInput(true, false, true));
        Assert.True(g.Commit(source)); Assert.Single(e.Session.Journal);
    }

    [Fact]
    public void Translation_moves_selected_parent_once_and_keeps_child_local_transform()
    {
        var scene = new Scene([
            SceneObject.At("parent", null, new(2, 0, 0)),
            SceneObject.At("child", null, new(0, 3, 0)) with { Parent = "parent" },
            SceneObject.At("other", null, new(10, 0, 0))]);
        var plan = SceneTranslation.World(scene, ["child", "parent", "other", "parent"], new(1, 2, 3));
        Assert.True(plan.Valid); Assert.Equal(["parent", "other"], plan.Roots); Assert.Equal(2, plan.Commands.Count);
        var e = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline);
        Assert.True(e.ExecuteAt(e.Session.Revision, plan.Commands, plan.Roots.ToArray()));
        Assert.Equal(scene.Find("child"), e.Session.Current.Find("child"));
        Assert.Equal(SceneViewport.World(scene, "child").Translation + new Vector3(1, 2, 3), SceneViewport.World(e.Session.Current, "child").Translation);
        Assert.Equal(new Vector3(11, 2, 3), e.Session.Current.Find("other")!.Position);
        Assert.Equal(1, e.Session.History.UndoCount); Assert.True(e.Session.Undo()); Assert.Equal(scene, e.Session.Current);
    }

    [Theory]
    [InlineData(2f)] [InlineData(-2f)]
    public void Translation_converts_world_delta_through_rotated_nonuniform_parent(float xScale)
    {
        var parent = SceneObject.At("parent", null, new(5, 7, 9)) with
        { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f), Scale = new(xScale, 3, 4) };
        var scene = new Scene([parent, SceneObject.At("child", null, new(1, 2, 3)) with { Parent = "parent" }]);
        var delta = new Vector3(4, -2, 6); var before = SceneViewport.World(scene, "child").Translation;
        var plan = SceneTranslation.World(scene, ["child"], delta); Assert.True(plan.Valid);
        var command = Assert.IsType<MoveObject>(Assert.Single(plan.Commands));
        var after = scene with { Objects = scene.Objects.SetItem(1, scene.Find("child")! with { Position = command.Position }) };
        Assert.InRange(Vector3.Distance(before + delta, SceneViewport.World(after, "child").Translation), 0, .00001f);
        Assert.Equal(parent, after.Find("parent"));
        Assert.Equal(scene.Find("child")!.Rotation, after.Find("child")!.Rotation);
        Assert.Equal(scene.Find("child")!.Scale, after.Find("child")!.Scale);
    }

    [Fact]
    public void Translation_refuses_entire_batch_for_singular_parent_or_missing_selection()
    {
        var scene = new Scene([SceneObject.At("free", null, Vector3.Zero),
            SceneObject.At("parent", null, Vector3.Zero) with { Scale = new(0, 1, 1) },
            SceneObject.At("child", null, Vector3.Zero) with { Parent = "parent" }]);
        var plan = SceneTranslation.World(scene, ["free", "child"], Vector3.One);
        Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.Contains("singular", Assert.Single(plan.Issues).Message);
        var missing = SceneTranslation.World(scene, ["free", "gone"], Vector3.One);
        Assert.False(missing.Valid); Assert.Empty(missing.Commands);
        Assert.False(SceneTranslation.World(scene, ["free"], new(float.PositiveInfinity, 0, 0)).Valid);
        var noop = SceneTranslation.World(scene, ["child"], Vector3.Zero);
        Assert.True(noop.Valid); Assert.Empty(noop.Commands);
        var empty = SceneTranslation.World(scene, [], Vector3.One);
        Assert.True(empty.Valid); Assert.Empty(empty.Commands);
    }

    [Theory]
    [InlineData(2f, 0f, 0f)] [InlineData(2f, 3f, 0f)] [InlineData(0f, 3f, 4f)]
    public void Basis_axis_and_plane_components_preserve_spacing_in_shared_primary_basis(float x, float y, float z)
    {
        var scene = new Scene([SceneObject.At("first", null, new(1, 2, 3)),
            SceneObject.At("primary", null, new(4, 6, 8)) with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) }]);
        var basis = scene.Find("primary")!.Rotation;
        var plan = SceneTranslation.InBasis(scene, ["first", "primary"], new(x, y, z), basis);
        Assert.True(plan.Valid); Assert.Equal(2, plan.Commands.Count);
        var first = Assert.IsType<MoveObject>(plan.Commands[0]); var primary = Assert.IsType<MoveObject>(plan.Commands[1]);
        var expected = new Vector3(z, y, -x);
        Assert.InRange(Vector3.Distance(scene.Find("first")!.Position + expected, first.Position), 0, .00001f);
        Assert.InRange(Vector3.Distance(scene.Find("primary")!.Position + expected, primary.Position), 0, .00001f);
        Assert.InRange(Vector3.Distance(new Vector3(3, 4, 5), primary.Position - first.Position), 0, .00001f);
        var world = SceneTranslation.InBasis(scene, ["first"], new(x, y, z), Quaternion.Identity);
        Assert.Equal(new Vector3(1+x, 2+y, 3+z), Assert.IsType<MoveObject>(Assert.Single(world.Commands)).Position);
    }

    [Fact]
    public void Basis_invalid_rotation_is_not_normalized_or_committed()
    {
        var scene = Editor().Session.Current;
        foreach (var q in new[] { new Quaternion(0, 0, 0, 2), new Quaternion(float.NaN, 0, 0, 1), default(Quaternion) })
        {
            var plan = SceneTranslation.InBasis(scene, ["cube"], Vector3.One, q);
            Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.Single(plan.Issues);
        }
    }

    private sealed class Trial : IPlaySession
    {
        public string Status => "Trial";
        public void Draw() { }
        public void Dispose() { }
    }
}
