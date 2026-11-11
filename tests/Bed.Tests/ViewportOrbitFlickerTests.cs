using System.Numerics;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// Camera moves flickered the real-window view: the GPU image accepted mid-frame was then skipped by the host's
// submit-time check (SceneTarget.Record draws only when request.Identity == current()) because camera input ran
// after the image was requested, so moving frames showed an empty cleared target. The host's check runs after the
// UI frame; these tests perform the same check at the same point.
[Collection(ImGuiCollection.Name)]
public sealed class ViewportOrbitFlickerTests
{
    private static ImTextureRef Texture => new() { _TexID = new ImTextureID((nint)0x5ce4e) };

    private sealed class Rig : IDisposable
    {
        public readonly ViewportView<Scene> View;
        public readonly HeadlessHost Host=new(1000,800){Drawing=false};
        private readonly ScenePresentation presenter;
        private (SceneRenderRequest Request,Func<SceneRenderIdentity> Current)? accepted;
        private readonly bool keyboardFocus;
        public int Stale, Moved;
        public Rig(bool keyboardFocus=false)
        {
            this.keyboardFocus=keyboardFocus;
            var scene=new Scene([SceneObject.At("crate",null,Vector3.Zero)]);
            var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
            View=new(editor,new SceneViewport());
            presenter=ScenePresentation.Bind((_,request,current)=>{accepted=(request,current);return new(request,Texture,()=>true);});
            Host.Frames(Draw,4);
        }
        private void Draw()
        {
            presenter.BeginFrame();
            Im.SetNextWindowPos(new float2(0,0),ImGuiCond.Always);Im.SetNextWindowSize(new float2(900,700),ImGuiCond.Always);
            Ui.Begin("View");if(keyboardFocus)Im.SetKeyboardFocusHere();View.Draw();Ui.End();
            presenter.CompleteFrame();
        }
        public void Frame()
        {
            accepted=null;var before=View.Camera;Host.Frame(Draw);
            if(View.Camera!=before)Moved++;
            if(accepted is { } a&&a.Request.Identity!=a.Current())Stale++;   // SceneTarget.Record would draw nothing
        }
        public void Key(ImGuiKey key,bool ctrl=false)
        {
            var io=Im.GetIO();
            if(ctrl){io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);Frame();}
            io.AddKeyEvent(key,true);Frame();io.AddKeyEvent(key,false);Frame();
            if(ctrl){io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Frame();}
        }
        public void Dispose(){presenter.Dispose();Host.Dispose();}
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Right_drag_navigation_keeps_every_accepted_scene_image_current_at_submit(bool fly)
    {
        using var rig=new Rig();
        var io=Im.GetIO();var p=rig.View.LastRect.Origin+rig.View.LastRect.Size/2;
        io.AddMousePosEvent(p.X,p.Y);rig.Frame();
        io.AddMouseButtonEvent(1,true);rig.Frame();
        if(fly)io.AddKeyEvent(ImGuiKey.W,true);
        for(var i=1;i<=10;i++){io.AddMousePosEvent(p.X+i*6,p.Y+i*3);rig.Frame();}
        if(fly)io.AddKeyEvent(ImGuiKey.W,false);
        io.AddMouseButtonEvent(1,false);rig.Frame();rig.Frame();
        Assert.True(rig.Moved>=5,$"the camera should move while right-dragging (moved {rig.Moved} frames)");
        Assert.Equal(0,rig.Stale);
    }

    [Theory]
    [InlineData("zoom-in")][InlineData("zoom-out")][InlineData("ctrl-orbit")][InlineData("frame-F")][InlineData("frame-Enter")]
    public void Keyboard_camera_keys_keep_every_accepted_scene_image_current_at_submit(string key)
    {
        using var rig=new Rig(keyboardFocus:true);
        if(key.StartsWith("frame",StringComparison.Ordinal))
        {rig.View.Editor.Select("crate");rig.View.Camera=Camera.Default with{Target=new Vector3(40,5,-30)};rig.Frame();}
        switch(key)
        {
            case "zoom-in":rig.Key(ImGuiKey.Equal);break;
            case "zoom-out":rig.Key(ImGuiKey.Minus);break;
            case "ctrl-orbit":rig.Key(ImGuiKey.LeftArrow,ctrl:true);break;
            case "frame-F":rig.Key(ImGuiKey.F);break;
            default:rig.Key(ImGuiKey.Enter);break;
        }
        rig.Frame();
        Assert.True(rig.Moved>=1,$"{key} should move the camera");
        Assert.Equal(0,rig.Stale);
    }
}
