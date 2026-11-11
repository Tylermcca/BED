using System.Numerics;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

public sealed class SceneGestureReparentTests
{
    private static EditorModel<Scene> Editor(Scene scene) => new(new DocumentSession<Scene>(
        new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), scene), SceneEditing.Outline);
    private static void Valid(SceneReparentPlan plan) => Assert.True(plan.Valid, string.Join(" ", plan.Issues.Select(i => i.Message)));
    private static void Near(Vector3 a, Vector3 b) => Assert.InRange(Vector3.Distance(a, b), 0, 1e-5f);
    private static void Near(Matrix4x4 expected, Matrix4x4 actual)
    {
        float[] Values(Matrix4x4 m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
            m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        var a = Values(expected); var b = Values(actual);
        for (var i = 0; i < a.Length; i++) Assert.InRange(MathF.Abs(a[i] - b[i]), 0, 1e-5f * MathF.Max(1, MathF.Abs(a[i])));
    }

    [Fact]
    public void Keep_local_matches_SetParent_and_moves_selected_ancestors_once_with_all_other_local_fields_exact()
    {
        var root = SceneObject.At("root", "invented.asset", new(1,2,3)) with
        { Parent = "old", Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY,.3f), Scale = new(-2,3,4) };
        var child = SceneObject.At("child", null, new(2,0,0)) with { Parent = "root" };
        var scene = new Scene([SceneObject.At("old", null, new(3,0,0)), root, child,
            SceneObject.At("destination", null, new(10,20,30))]);
        var e = Editor(scene); var plan = SceneReparent.To(scene, ["child","root","root"], "destination", preserveWorld:false);
        Valid(plan); Assert.Equal(["root"], plan.Roots); Assert.IsType<SetParent>(Assert.Single(plan.Commands));
        Assert.Same(scene,e.Session.Current); Assert.Empty(e.Session.Journal);
        Assert.True(e.ExecuteAt(e.Session.Revision,plan.Commands,plan.Roots.ToArray()));
        Assert.Equal(root with { Parent="destination" }, e.Session.Current.Find("root"));
        Assert.Equal(child,e.Session.Current.Find("child")); Near(new(11,22,33),SceneViewport.World(e.Session.Current,"root").Translation);
        Assert.Equal(new SetParent("root","destination").Apply(scene).Document!.Objects.ToArray(),e.Session.Current.Objects.ToArray());
        Assert.Single(e.Session.Journal); Assert.Equal(1,e.Session.History.UndoCount);
        var changed=e.Session.Current; Assert.True(e.Session.Undo()); Assert.Equal(scene,e.Session.Current);
        Assert.True(e.Session.Redo()); Assert.Equal(changed,e.Session.Current);
    }

    [Fact]
    public void Preserve_world_converts_through_rotated_scaled_destination_and_preserves_the_child_world_pose()
    {
        var root=SceneObject.At("root",null,new(1,2,3)) with
        { Parent="old", Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.3f), Scale=new(-2,3,4) };
        var child=SceneObject.At("child",null,new(2,0,0)) with { Parent="root" };
        var destination=SceneObject.At("destination",null,new(10,0,0)) with
        { Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2), Scale=new(2) };
        var scene=new Scene([SceneObject.At("old",null,new(4,5,6)),root,child,destination]); var e=Editor(scene);
        var beforeRoot=SceneViewport.World(scene,"root"); var beforeChild=SceneViewport.World(scene,"child");
        var plan=SceneReparent.To(scene,["child","root"],"destination",preserveWorld:true); Valid(plan);
        Assert.Equal(["root"],plan.Roots); Assert.True(e.ExecuteAt(e.Session.Revision,plan.Commands,plan.Roots.ToArray()));
        Near(new(-4.5f,3.5f,-2.5f),e.Session.Current.Find("root")!.Position);
        Near(new(-1,1.5f,2),e.Session.Current.Find("root")!.Scale);
        Assert.Equal("destination",e.Session.Current.Find("root")!.Parent);
        Assert.Equal(child,e.Session.Current.Find("child")); Assert.Equal(destination,e.Session.Current.Find("destination"));
        Near(beforeRoot,SceneViewport.World(e.Session.Current,"root")); Near(beforeChild,SceneViewport.World(e.Session.Current,"child"));
        Assert.Single(e.Session.Journal); Assert.Equal(1,e.Session.History.UndoCount);
        Assert.True(e.Session.Undo()); Assert.Equal(scene,e.Session.Current);
    }

    [Fact]
    public void Preserve_world_detach_uses_explicit_null_destination()
    {
        var parent=SceneObject.At("parent",null,new(5,6,7)) with
        { Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f),Scale=new(2) };
        var root=SceneObject.At("root",null,new(1,2,3)) with { Parent="parent",Scale=new(-1,2,3) };
        var scene=new Scene([parent,root]); var before=SceneViewport.World(scene,"root");
        var plan=SceneReparent.To(scene,["root"],null,preserveWorld:true); Valid(plan);
        var e=Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision,plan.Commands,plan.Roots.ToArray()));
        Assert.Null(e.Session.Current.Find("root")!.Parent); Near(new(-2,4,6),e.Session.Current.Find("root")!.Scale);
        Near(before,SceneViewport.World(e.Session.Current,"root")); Assert.Equal(parent,e.Session.Current.Find("parent"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Existing_parent_and_empty_selection_are_noops(bool preserve)
    {
        var scene=new Scene([SceneObject.At("parent",null,Vector3.Zero),
            SceneObject.At("root",null,Vector3.UnitX) with { Parent="parent" }]); var e=Editor(scene);
        foreach(var plan in new[] { SceneReparent.To(scene,["root"],"parent",preserve),SceneReparent.To(scene,[],"parent",preserve) })
        {
            Valid(plan); Assert.Empty(plan.Commands);
            Assert.True(e.ExecuteAt(e.Session.Revision,plan.Commands,plan.Roots.ToArray()));
            Assert.Same(scene,e.Session.Current); Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Cycles_self_parent_and_missing_identity_refuse_whole(bool preserve)
    {
        var scene=new Scene([SceneObject.At("root",null,Vector3.Zero),
            SceneObject.At("child",null,Vector3.UnitY) with { Parent="root" }]);
        foreach(var plan in new[] {
            SceneReparent.To(scene,["root"],"root",preserve),
            SceneReparent.To(scene,["root"],"child",preserve),
            SceneReparent.To(scene,["root"],"gone",preserve),
            SceneReparent.To(scene,["root","gone"],null,preserve) })
        { Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.NotEmpty(plan.Issues); }
    }

    [Fact]
    public void Unrepresentable_source_shear_refuses_even_after_an_earlier_root_was_planned()
    {
        var scene=new Scene([SceneObject.At("ordinary",null,Vector3.UnitY),
            SceneObject.At("first",null,Vector3.UnitX) with { Parent="ordinary" },
            SceneObject.At("stretched",null,Vector3.Zero) with { Scale=new(2,1,1) },
            SceneObject.At("last",null,Vector3.UnitX) with { Parent="stretched",Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/4) }]);
        var e=Editor(scene); var plan=SceneReparent.To(scene,["first","last"],null,preserveWorld:true);
        Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.Contains("shear",Assert.Single(plan.Issues).Message);
        Assert.Same(scene,e.Session.Current); Assert.Empty(e.Session.Journal); Assert.False(e.Session.Dirty);
    }

    [Fact]
    public void A_destination_causing_shear_refuses_preserve_world_but_explicit_keep_local_remains_available()
    {
        var scene=new Scene([SceneObject.At("root",null,Vector3.UnitX) with { Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/4) },
            SceneObject.At("destination",null,Vector3.Zero) with { Scale=new(2,1,1) }]);
        var world=SceneReparent.To(scene,["root"],"destination",preserveWorld:true);
        Assert.False(world.Valid); Assert.Empty(world.Commands); Assert.Contains("shear",Assert.Single(world.Issues).Message);
        var local=SceneReparent.To(scene,["root"],"destination",preserveWorld:false); Valid(local);
        Assert.IsType<SetParent>(Assert.Single(local.Commands));
    }

    [Fact]
    public void Same_handed_mirrored_parents_preserve_world_and_the_captured_local_scale_signs()
    {
        var root=SceneObject.At("root",null,new(1,2,3)) with { Parent="old",Scale=new(-1,2,3) };
        var scene=new Scene([
            SceneObject.At("old",null,new(4,5,6)) with { Scale=new(-2,2,2) },root,
            SceneObject.At("destination",null,Vector3.UnitY) with { Scale=new(-4,4,4),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f) }]);
        var plan=SceneReparent.To(scene,["root"],"destination",preserveWorld:true); Valid(plan);
        var e=Editor(scene); Assert.True(e.ExecuteAt(e.Session.Revision,plan.Commands,plan.Roots.ToArray()));
        Near(new(-.5f,1,1.5f),e.Session.Current.Find("root")!.Scale);
        Near(SceneViewport.World(scene,"root"),SceneViewport.World(e.Session.Current,"root"));
    }

    [Fact]
    public void Changing_parent_handedness_refuses_instead_of_guessing_a_local_reflection_axis()
    {
        var scene=new Scene([SceneObject.At("root",null,Vector3.UnitX),
            SceneObject.At("destination",null,Vector3.Zero) with { Scale=new(-1,1,1) }]);
        var plan=SceneReparent.To(scene,["root"],"destination",preserveWorld:true);
        Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.Contains("reflection",Assert.Single(plan.Issues).Message);
        Valid(SceneReparent.To(scene,["root"],"destination",preserveWorld:false));
    }

    [Fact]
    public void Invalid_destination_or_source_numeric_transforms_are_not_silently_normalized()
    {
        foreach(var destination in new[] {
            SceneObject.At("destination",null,Vector3.Zero) with { Scale=new(0,1,1) },
            SceneObject.At("destination",null,Vector3.Zero) with { Rotation=new(0,0,0,2) },
            SceneObject.At("destination",null,Vector3.Zero) with { Scale=new(float.Epsilon) } })
        {
            var scene=new Scene([SceneObject.At("root",null,Vector3.UnitX),destination]);
            var plan=SceneReparent.To(scene,["root"],"destination",preserveWorld:true);
            Assert.False(plan.Valid); Assert.Empty(plan.Commands); Assert.NotEmpty(plan.Issues);
        }
        var badRoot=new Scene([SceneObject.At("root",null,Vector3.UnitX) with { Rotation=new(0,0,0,2) },
            SceneObject.At("destination",null,Vector3.Zero)]);
        Assert.False(SceneReparent.To(badRoot,["root"],"destination",preserveWorld:true).Valid);
    }

    [Fact]
    public void Captured_revision_and_focus_still_guard_the_atomic_commit()
    {
        var scene=new Scene([SceneObject.At("root",null,Vector3.UnitX),SceneObject.At("destination",null,Vector3.UnitY)]);
        var plan=SceneReparent.To(scene,["root"],"destination",preserveWorld:true); Valid(plan);
        var e=Editor(scene); var revision=e.Session.Revision;
        Assert.True(e.Execute(new MoveObject("root",Vector3.UnitZ),"root")); var changed=e.Session.Current;
        Assert.False(e.ExecuteAt(revision,plan.Commands,plan.Roots.ToArray()));
        Assert.Same(changed,e.Session.Current); Assert.Single(e.Session.Journal);
        Assert.True(e.FocusOn("destination")); var count=e.Session.Journal.Count;
        Assert.False(e.ExecuteAt(e.Session.Revision,plan.Commands,plan.Roots.ToArray()));
        Assert.Same(changed,e.Session.Current); Assert.Equal(count,e.Session.Journal.Count);
    }
}
