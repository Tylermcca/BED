using System.Numerics;
using System.Reflection;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ScenePresentationBindingTests
{
    private static ImTextureRef Texture => new() { _TexID = new ImTextureID((nint)0x123456) };
    private static SceneRenderRequest Request(SceneRenderIdentity? identity=null) => SceneRenderRequest.Capture(identity ?? new(0,1,2,3,4,Vector2.Zero,new(400,300),Vector2.One),Camera.Default,
        [new ViewportItem("crate",Matrix4x4.Identity,MeshData.Cube,0xff808080)]);
    private static EditorModel<Scene> Editor() => new(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new Scene([SceneObject.At("crate",null,Vector3.Zero)])),SceneEditing.Outline);
    private static void Window(ViewportView<Scene> view, float width=600)
    { Im.SetNextWindowPos(new(20,20));Im.SetNextWindowSize(new(width,450));Ui.Begin("Presentation"u8);view.Draw();Ui.End(); }
    private static bool HasImage()
    {
        var data=Im.GetDrawData();
        for(var l=0;l<data.CmdListsCount;l++)
            for(var c=0;c<data.CmdLists[l].CmdBuffer.Count;c++)
                if(data.CmdLists[l].CmdBuffer[c].GetTexID().Value==Texture._TexID.Value)return true;
        return false;
    }

    [Fact]
    public void Production_view_submits_exact_request_adds_borrowed_image_and_keeps_canvas_drop_and_CPU_click_authority()
    {
        using var host=new HeadlessHost(800,600){Drawing=false};var editor=Editor();var view=new ViewportView<Scene>(editor,new SceneViewport());
        SceneRenderRequest? received=null;string? viewId=null;var drops=0;
        view.DropTarget=()=>{drops++;Assert.Equal(Im.GetID("##viewport"u8).Value,Im.GetItemID().Value);};
        using var presenter=ScenePresentation.Bind((id,request,current)=>
        {viewId??=id;Assert.Equal(viewId,id);Assert.Equal(request.Identity,current());received=request;return new(request,Texture,()=>true);});
        void Draw(){presenter.BeginFrame();Window(view);presenter.CompleteFrame();}
        host.Frames(Draw,3);
        Assert.Same(view.RenderRequest,received);Assert.True(HasImage());Assert.Equal("",view.RenderStatus);Assert.True(drops>=3);
        Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="crate");
        var centre=view.ScreenOf(Vector3.Zero)!.Value;host.Click(Draw,centre.X,centre.Y);
        Assert.Equal(new[]{"crate"},editor.Selection);Assert.Empty(editor.Session.Journal);Assert.False(editor.Session.Dirty);
        var empty=view.LastRect.Origin+new Vector2(5,5);host.Click(Draw,empty.X,empty.Y);Assert.Empty(editor.Selection);
        Assert.True(HasImage());
    }

    [Theory]
    [InlineData("identity")][InlineData("request")][InlineData("lease")][InlineData("zero")][InlineData("late")][InlineData("throw")][InlineData("scale")]
    public void Stale_wrong_empty_or_refused_images_never_enter_the_UI_draw_list_and_CPU_fallback_has_a_reason(string mode)
    {
        using var host=new HeadlessHost(800,600){Drawing=false};var view=new ViewportView<Scene>(Editor(),new SceneViewport());
        using var presenter=ScenePresentation.Bind((_,request,_)=>
        {
            if(mode=="throw")throw new InvalidOperationException("Synthetic target budget refusal");
            if(mode=="identity")view.SourceGeneration++;
            if(mode=="scale") {var viewport=Im.GetWindowViewport();viewport.FramebufferScale=new(MathF.Max(1,viewport.FramebufferScale.X)*2,MathF.Max(1,viewport.FramebufferScale.Y)*2);}
            return new(mode=="request"?Request(request.Identity):request,mode=="zero"?default:Texture,
                ()=>{if(mode=="late")view.TargetGeneration++;return mode!="lease";});
        });
        host.Frames(()=>Window(view),3);
        Assert.False(HasImage());Assert.StartsWith("CPU view:",view.RenderStatus);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.Contains("CPU view:"));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="crate");
    }

    [Fact]
    public void Current_scope_is_context_local_nested_and_closed_bindings_cannot_produce_an_image()
    {
        using var host=new HeadlessHost{Drawing=false};var request=Request();var calls=0;
        var first=ScenePresentation.Bind((_,r,_)=>{calls++;return new(r,Texture,()=>true);});
        using var second=ScenePresentation.Bind((_,r,_)=>new(r,Texture,()=>true));
        Assert.Same(second,ScenePresentation.Current);second.Dispose();Assert.Same(first,ScenePresentation.Current);
        var context=Im.GetCurrentContext();var other=Im.CreateContext();Im.SetCurrentContext(other);
        try {Assert.Null(ScenePresentation.Current);Assert.False(first.TryImage("view",request,()=>request.Identity,out _,out _));}
        finally{Im.DestroyContext(other);Im.SetCurrentContext(context);}
        Assert.True(first.TryImage("view",request,()=>request.Identity,out var image,out _));Assert.Equal(Texture._TexID,image._TexID);
        first.Dispose();Assert.Null(ScenePresentation.Current);Assert.False(first.TryImage("view",request,()=>request.Identity,out _,out _));Assert.Equal(1,calls);
    }

    [Fact]
    public void Resize_presenter_replacement_and_close_change_target_lifetime_and_refuse_old_selection_results()
    {
        using var host=new HeadlessHost(900,600){Drawing=false};var editor=Editor();var view=new ViewportView<Scene>(editor,new SceneViewport());
        var first=ScenePresentation.Bind((_,r,_)=>new(r,Texture,()=>true));host.Frames(()=>Window(view),3);
        var old=view.RenderRequest!;var miss=old.Pick(new(-1000,-1000));editor.Select("crate");
        host.Frame(()=>Window(view,700));var resized=view.RenderRequest!;
        Assert.NotEqual(old.Identity.TargetGeneration,resized.Identity.TargetGeneration);Assert.False(view.TrySelectRenderResult(miss));Assert.Equal(new[]{"crate"},editor.Selection);
        first.Dispose();using var second=ScenePresentation.Bind((_,r,_)=>new(r,Texture,()=>true));
        Assert.False(view.TrySelectRenderResult(resized.Pick(new(-1000,-1000))));host.Frame(()=>Window(view,700));
        Assert.NotEqual(resized.Identity.TargetGeneration,view.RenderRequest!.Identity.TargetGeneration);
        second.Dispose();host.Frame(()=>Window(view,700));Assert.False(HasImage());Assert.Equal("",view.RenderStatus);
    }

    [Fact]
    public void View_request_uses_its_native_viewport_framebuffer_scale_not_the_main_IO_assumption()
    {
        using var host=new HeadlessHost(800,600){Drawing=false};var view=new ViewportView<Scene>(Editor(),new SceneViewport());
        using var presenter=ScenePresentation.Bind((_,request,_)=>new(request,Texture,()=>true));
        host.Frame(()=>
        {
            Im.SetNextWindowSize(new(600,450));Ui.Begin("Scale proof"u8);
            var viewport=Im.GetWindowViewport();viewport.FramebufferScale=new(2,3);
            view.Draw();Ui.End();
        });
        Assert.Equal(new Vector2(2,3),view.RenderRequest!.Identity.FramebufferScale);Assert.True(HasImage());
    }

    [Fact]
    public void Retained_view_refuses_delayed_selection_after_context_replacement_or_destruction()
    {
        var host=new HeadlessHost(800,600){Drawing=false};var editor=Editor();var view=new ViewportView<Scene>(editor,new SceneViewport());
        ScenePickResult result;
        try
        {
            host.Frames(()=>Window(view),3);var request=view.RenderRequest!;result=request.Pick(request.Identity.Origin+request.Identity.Extent/2);
            var context=Im.GetCurrentContext();var other=Im.CreateContext();Im.SetCurrentContext(other);
            try{Assert.False(view.TrySelectRenderResult(result));Assert.Empty(editor.Selection);}
            finally{Im.DestroyContext(other);Im.SetCurrentContext(context);}
        }
        finally{host.Dispose();}
        Assert.False(view.TrySelectRenderResult(result));Assert.Empty(editor.Selection);Assert.Empty(editor.Session.Journal);
    }

    [Fact]
    public void Completed_frame_reports_only_currently_requested_views_for_renderer_owned_retirement()
    {
        using var host=new HeadlessHost{Drawing=false};var request=Request();var retained=new List<IReadOnlySet<string>>();
        using var presenter=ScenePresentation.Bind((_,r,_)=>new(r,Texture,()=>true),views=>retained.Add(views));
        presenter.BeginFrame();Assert.True(presenter.TryImage("first",request,()=>request.Identity,out _,out _));
        Assert.True(presenter.TryImage("second",request,()=>request.Identity,out _,out _));presenter.CompleteFrame();
        presenter.BeginFrame();presenter.CompleteFrame();
        Assert.Equal(new[]{"first","second"},retained[0].Order());Assert.Empty(retained[1]);
        Assert.Equal(2,retained[0].Count); // the retained snapshot does not alias the next frame's mutable set
    }

    [Fact]
    public void Vulkan_host_collects_and_completes_presenter_before_render_and_detaches_before_scene_shutdown()
    {
        var loop=typeof(EditorApp).GetMethod("Loop",BindingFlags.Static|BindingFlags.NonPublic)!;
        var calls=SceneFrameLifecycleTests.Calls(loop);
        var safe=Assert.Single(calls,c=>c.Method.Name=="PrepareSceneFrame");
        var begin=Assert.Single(calls,c=>c.Method.Name=="BeginFrame"&&c.Method.DeclaringType==typeof(ScenePresentation));
        var run=Assert.Single(calls,c=>c.Method.DeclaringType==SceneFrameLifecycleTests.FrameType&&c.Method.Name=="Run");
        var done=SceneFrameLifecycleTests.FrameCallback(loop,"CompleteFrame","ScenePresentation");
        var submit=SceneFrameLifecycleTests.FrameCallback(loop,"Draw","WindowRenderer");
        var detach=Assert.Single(calls,c=>c.Method.Name=="Dispose"&&c.Method.DeclaringType==typeof(ScenePresentation));
        var shutdown=Assert.Single(calls,c=>c.Method.Name=="ShutdownScenes");
        // Completion is a constructor-wired callback; its execution order lives in EditorFrame.Run.
        Assert.True(safe.Offset<begin.Offset&&begin.Offset<run.Offset&&run.Offset<detach.Offset&&detach.Offset<shutdown.Offset);
        Assert.True(done.Offset<submit.Offset);
        Assert.True(SceneFrameLifecycleTests.FrameAction("draw")<SceneFrameLifecycleTests.FrameAction("complete"));
        Assert.True(SceneFrameLifecycleTests.FrameAction("complete")<SceneFrameLifecycleTests.FrameAction("render"));
        var openGl=typeof(EditorApp).GetMethod("RunOpenGl",BindingFlags.Static|BindingFlags.NonPublic)!;
        Assert.DoesNotContain(SceneFrameLifecycleTests.Calls(openGl),c=>c.Method.Name=="Bind"&&c.Method.DeclaringType==typeof(ScenePresentation));
    }
}
