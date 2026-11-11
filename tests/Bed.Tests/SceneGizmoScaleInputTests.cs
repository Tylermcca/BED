using System.Numerics;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneGizmoScaleInputTests
{
    private static void Near(Vector3 a,Vector3 b)=>Assert.InRange(Vector3.Distance(a,b),0,1e-4f);

    [Theory]
    [InlineData(1f,4f)] [InlineData(1f,40f)] [InlineData(2f,4f)] [InlineData(2f,40f)]
    public void Screen_targets_follow_exact_local_axis_directions_at_zoom_and_ui_scale(float scale,float distance)
    {
        var camera=Camera.Default with { Distance=distance };
        var world=Matrix4x4.CreateScale(-2,3,4)*Matrix4x4.CreateRotationZ(.4f);
        var handles=ScaleHandles.Layout(camera,world,new(1100,760),scale);
        Assert.Equal(4,handles.Count);
        foreach(var handle in handles)
        {
            Assert.True(handle.Hit(handle.End));
            Assert.False(handle.Hit(handle.End+new Vector2(9*scale,0)));
            if(handle.Kind==ScaleHandleKind.Uniform) { Assert.Equal(handle.Centre,handle.End); continue; }
            Assert.InRange(MathF.Abs(Vector2.Distance(handle.Centre,handle.End)-88*scale),0,.0001f);
            var local=handle.Kind==ScaleHandleKind.X ? Vector3.UnitX : handle.Kind==ScaleHandleKind.Y ? Vector3.UnitY : Vector3.UnitZ;
            var p=camera.Project(Vector3.TransformNormal(local,world)*.001f,new(1100,760))!.Value.Screen;
            Assert.True(Vector2.Dot(Vector2.Normalize(p-handle.Centre),handle.Direction)>.999f);
        }
    }

    [Theory]
    [InlineData(ScaleHandleKind.Uniform)] [InlineData(ScaleHandleKind.X)]
    [InlineData(ScaleHandleKind.Y)] [InlineData(ScaleHandleKind.Z)]
    public void Factors_snap_delta_and_allow_mirrors_but_do_not_clamp_zero(ScaleHandleKind kind)
    {
        var handle=new ScaleHandle(kind,Vector2.Zero,new(88,0),Vector2.UnitX,2);
        Assert.True(handle.TryFactors(Vector2.Zero,new(24,0),.1f,out var factors));
        var expected=kind switch { ScaleHandleKind.X=>new Vector3(1.2f,1,1),ScaleHandleKind.Y=>new(1,1.2f,1),
            ScaleHandleKind.Z=>new(1,1,1.2f),_=>new(1.2f) };
        Near(expected,factors);
        Assert.True(handle.TryFactors(Vector2.Zero,new(-320,0),0,out factors));
        var axis=kind==ScaleHandleKind.Y ? factors.Y : kind==ScaleHandleKind.Z ? factors.Z : factors.X;
        Assert.Equal(-1,axis);
        Assert.True(handle.TryFactors(Vector2.Zero,new(-160,0),.1f,out factors));
        Assert.Contains(0f,new[]{factors.X,factors.Y,factors.Z});
        Assert.False(handle.TryFactors(Vector2.Zero,new(float.NaN,0),0,out _));
        Assert.False(handle.TryFactors(Vector2.Zero,Vector2.Zero,float.PositiveInfinity,out _));
    }

    [Fact]
    public void Behind_camera_and_view_parallel_axes_have_no_phantom_target()
    {
        var camera=Camera.Default with { Yaw=0,Pitch=0 };
        var handles=ScaleHandles.Layout(camera,Matrix4x4.Identity,new(1100,760),1);
        Assert.DoesNotContain(handles,h=>h.Kind==ScaleHandleKind.Z);
        Assert.Contains(handles,h=>h.Kind==ScaleHandleKind.Uniform);
        Assert.Empty(ScaleHandles.Layout(camera,Matrix4x4.CreateTranslation(camera.Eye-camera.Forward),new(1100,760),1));
        Assert.Empty(ScaleHandles.Layout(camera,Matrix4x4.Identity,new(1100,760),float.NaN));
    }

    private sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host=new(1200,850) { Drawing=false };
        public readonly Scene Original=new([SceneObject.At("root",null,Vector3.Zero),
            SceneObject.At("child",null,new(2,0,0)) with { Parent="root" },
            SceneObject.At("other",null,new(4,0,0))]);
        public readonly EditorModel<Scene> Editor;
        public readonly ViewportView<Scene> View;
        public Rig(float scale=1,bool moveOnly=false)
        {
            Editor=new(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Original),SceneEditing.Outline);
            Editor.Select("root","child","other");
            View=new(Editor,moveOnly ? new MoveOnlySource() : new SceneViewport());
            EditorWindow<Scene>.Configure(ImGui.GetIO()); EditorWindow<Scene>.SetTextScale(scale);
            Host.Frames(Draw,4);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0)); ImGui.SetNextWindowSize(new float2(1200,850));
            ImGui.Begin("Scale test"u8,ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
            View.Draw(); ImGui.End();
        }
        public void Focus()
        {
            // Tab now cycles objects once already focused; acquiring focus must not alter the captured selection.
            if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Canvas&&n.Focused))return;
            for(var i=0;i<8;i++)
            {
                Host.Press(Draw,ImGuiKey.Tab);
                if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Canvas && n.Focused)) return;
            }
            Assert.Fail("Tab must reach the actual viewport through its real toolbar controls.");
        }
        public void ScaleMode()
        {
            Focus(); Host.Press(Draw,ImGuiKey.S); Host.Frames(Draw,2);
            Assert.Equal(ViewportTool.Scale,View.Tool);
        }
        public (float2 From,float2 To) Drag(ScaleHandleKind kind,float factor)
        {
            var handle=View.ScaleTargets.Single(h=>h.Kind==kind);
            var from=View.LastRect.Origin+handle.End;
            var to=from+handle.Direction*(factor-1)*80*EditorWindow<Scene>.TextScale;
            return(new(from.X,from.Y),new(to.X,to.Y));
        }
        public void Begin(float2 from)
        {
            Host.MoveMouse(Draw,from.X,from.Y); ImGui.GetIO().AddMouseButtonEvent(0,true); Host.Frames(Draw,2);
        }
        public void Release()
        {
            ImGui.GetIO().AddMouseButtonEvent(0,false); Host.Frames(Draw,2);
        }
        public void Dispose()=>Host.Dispose();
    }
    private sealed class MoveOnlySource : IViewportSource<Scene>
    {
        public IEnumerable<ViewportItem> Items(Scene scene)=>new SceneViewport().Items(scene);
        public ICommand<Scene>? MoveTo(Scene scene,string id,Vector3 target)=>new SceneViewport().MoveTo(scene,id,target);
    }

    [Theory]
    [InlineData(1f,ScaleHandleKind.X,2f)] [InlineData(2f,ScaleHandleKind.X,2f)]
    [InlineData(1f,ScaleHandleKind.Uniform,2f)] [InlineData(2f,ScaleHandleKind.Uniform,2f)]
    [InlineData(1f,ScaleHandleKind.X,-1f)] [InlineData(2f,ScaleHandleKind.X,-1f)]
    public void Real_drag_previews_local_factors_and_releases_one_root_filtered_undo(float scale,ScaleHandleKind kind,float factor)
    {
        using var r=new Rig(scale); r.ScaleMode(); var (from,to)=r.Drag(kind,factor);
        r.Begin(from); r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status && n.Label.StartsWith("Scale local"));
        r.Release();
        var expected=kind==ScaleHandleKind.Uniform ? new Vector3(factor) : new Vector3(factor,1,1);
        Near(expected,r.Editor.Session.Current.Find("root")!.Scale);
        Near(expected,r.Editor.Session.Current.Find("other")!.Scale);
        Assert.Equal(r.Original.Find("root")!.Position,r.Editor.Session.Current.Find("root")!.Position);
        Assert.Equal(r.Original.Find("other")!.Position,r.Editor.Session.Current.Find("other")!.Position);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child"));
        Near(new(2*expected.X,0,0),SceneViewport.World(r.Editor.Session.Current,"child").Translation);
        Assert.Single(r.Editor.Session.Journal); Assert.Equal(1,r.Editor.Session.History.UndoCount);
        Assert.True(r.Editor.Session.Undo()); Assert.Equal(r.Original,r.Editor.Session.Current);
    }

    [Fact]
    public void Zero_latest_factor_refuses_without_committing_an_earlier_valid_preview()
    {
        using var r=new Rig(); r.ScaleMode(); var (from,valid)=r.Drag(ScaleHandleKind.X,2);
        r.Begin(from); r.Host.MoveMouse(r.Draw,valid.X,valid.Y); r.Host.Frames(r.Draw,2);
        var (_,zero)=r.Drag(ScaleHandleKind.X,0);
        r.Host.MoveMouse(r.Draw,zero.X,zero.Y); r.Host.Frames(r.Draw,2);
        Assert.Contains("zero",r.View.Said);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status && n.Label.Contains("zero"));
        r.Release(); Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
        Assert.False(r.Editor.Session.Dirty);
    }

    [Theory]
    [InlineData(1f)] [InlineData(2f)]
    public void Keyboard_axis_uniform_precision_and_ctrl_orbit_keep_one_edit_per_press(float scale)
    {
        using var r=new Rig(scale); r.Focus();
        r.Host.Press(r.Draw,ImGuiKey.X); // A prior Move constraint must not leak into a fresh Scale tool.
        r.Host.Press(r.Draw,ImGuiKey.S); r.Host.Frames(r.Draw,2);
        Assert.Equal(ViewportTool.Scale,r.View.Tool);
        r.Host.Press(r.Draw,ImGuiKey.RightArrow); r.Host.Frames(r.Draw,2);
        Near(new(1.1f),r.Editor.Session.Current.Find("root")!.Scale);
        r.Host.Press(r.Draw,ImGuiKey.Y); r.Host.Press(r.Draw,ImGuiKey.LeftArrow); r.Host.Frames(r.Draw,2);
        Near(new(1.1f,.99f,1.1f),r.Editor.Session.Current.Find("root")!.Scale);
        r.Host.Press(r.Draw,ImGuiKey.RightArrow,alt:true); r.Host.Frames(r.Draw,2);
        Near(new(1.1f,.9999f,1.1f),r.Editor.Session.Current.Find("root")!.Scale);
        Assert.Equal(r.Original.Find("child"),r.Editor.Session.Current.Find("child"));
        Assert.Equal(3,r.Editor.Session.History.UndoCount);
        var camera=r.View.Camera; r.Host.Press(r.Draw,ImGuiKey.RightArrow,ctrl:true);
        Assert.NotEqual(camera,r.View.Camera); Assert.Equal(3,r.Editor.Session.History.UndoCount);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status && n.Label.StartsWith("Scale local Y:"));
        r.Host.Press(r.Draw,ImGuiKey.S); r.Host.Press(r.Draw,ImGuiKey.RightArrow); r.Host.Frames(r.Draw,2);
        Near(new(1.21f,1.09989f,1.21f),r.Editor.Session.Current.Find("root")!.Scale);
        Assert.Equal(4,r.Editor.Session.History.UndoCount);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status && n.Label.StartsWith("Scale local Uniform:"));
    }

    [Fact]
    public void Escape_cancels_and_the_same_live_drag_is_a_positive_control()
    {
        using var r=new Rig(); r.ScaleMode(); var (from,to)=r.Drag(ScaleHandleKind.X,2);
        r.Begin(from); r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        r.Host.Press(r.Draw,ImGuiKey.Escape); r.Release();
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal); Assert.False(r.Editor.Session.Dirty);
        r.Host.Drag(r.Draw,from,to);
        Near(new(2,1,1),r.Editor.Session.Current.Find("root")!.Scale); Assert.Single(r.Editor.Session.Journal);
    }

    [Theory]
    [InlineData("revision")] [InlineData("selection")] [InlineData("camera")] [InlineData("snap")]
    public void Changed_capture_context_cannot_commit_a_stale_scale(string change)
    {
        using var r=new Rig(); r.ScaleMode(); var (from,to)=r.Drag(ScaleHandleKind.X,2);
        r.Begin(from); r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        switch(change)
        {
            case "revision": Assert.True(r.Editor.Execute(new MoveObject("root",Vector3.UnitY),"root")); break;
            case "selection": r.Editor.Select("other"); break;
            case "camera": r.View.Camera=r.View.Camera.Orbit(.1f,0); break;
            case "snap": r.View.ScaleSnap=.2f; break;
        }
        r.Host.Frames(r.Draw,1); r.Release();
        Near(Vector3.One,r.Editor.Session.Current.Find("root")!.Scale);
        Assert.Equal(change=="revision" ? 1 : 0,r.Editor.Session.Journal.Count);
    }

    [Fact]
    public void Move_only_sources_explain_disabled_scale_and_have_no_fake_handles()
    {
        using var r=new Rig(moveOnly:true); r.Focus(); r.Host.Press(r.Draw,ImGuiKey.S); r.Host.Frames(r.Draw,2);
        Assert.Equal(ViewportTool.Move,r.View.Tool); Assert.Empty(r.View.ScaleTargets); Assert.Empty(r.Editor.Session.Journal);
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Scale (S)");
        Assert.False(button.Enabled); Assert.Contains("moving",button.DisabledReason);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Scale local X");
    }

    [Fact]
    public void Scale_toolbar_returns_focus_and_noop_release_has_no_history()
    {
        using var r=new Rig();
        var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Scale (S)");
        var b=button.Bounds; r.Host.Click(r.Draw,b.X+b.Width/2,b.Y+b.Height/2); r.Host.Frames(r.Draw,2);
        Assert.Equal(ViewportTool.Scale,r.View.Tool);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas && n.Focused);
        var (from,_)=r.Drag(ScaleHandleKind.Uniform,1); r.Begin(from); r.Release();
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
    }

    [Fact]
    public void Play_cancels_the_scale_and_disables_tool_actions()
    {
        using var r=new Rig(); r.ScaleMode(); var (from,to)=r.Drag(ScaleHandleKind.X,2);
        r.Begin(from); r.Host.MoveMouse(r.Draw,to.X,to.Y); r.Host.Frames(r.Draw,2);
        r.Editor.Add(new PlayMode<Scene>("synthetic","Synthetic",_=>new SyntheticPlay())); Assert.True(r.Editor.Play("synthetic"));
        r.Host.Frames(r.Draw,2); r.Release();
        Assert.Empty(r.View.ScaleTargets);
        Assert.False(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label=="Scale (S)").Enabled);
        Assert.True(r.Editor.Stop()); r.Host.Frames(r.Draw,2);
        Assert.Same(r.Original,r.Editor.Session.Current); Assert.Empty(r.Editor.Session.Journal);
    }
    private sealed class SyntheticPlay : IPlaySession
    {
        public string Status=>"Synthetic lifecycle fixture";
        public void Draw() { }
        public void Dispose() { }
    }
}
