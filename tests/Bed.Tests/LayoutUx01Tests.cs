using System.Numerics;
using System.Reflection;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class LayoutUx01Tests
{
    private static string Repo(){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p is not null&&!File.Exists(Path.Combine(p.FullName,"Bed.slnx")))p=p.Parent;return p!.FullName;}
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("layout-ux-").FullName;internal readonly HeadlessHost Host;internal readonly DataWorkspace Workspace;internal readonly string Mode;
        internal Rig(string mode,string? layout=null)
        {
            Mode=mode;var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(data,"items.json"),"{\"items\":[{\"id\":\"box\",\"amount\":2}]}");File.WriteAllText(Path.Combine(schemas,"items.schema.json"),"{}");
            if(mode!="data")File.WriteAllText(Path.Combine(data,"level.json"),mode=="v1"?new SceneFormat().Write(new([SceneObject.At("crate",null,Vector3.Zero)])):new Scene2Format().Write(new(mode=="xy"?SceneSpace.TwoDXY:SceneSpace.ThreeD,[new(SceneObject.At("crate",null,Vector3.Zero),null)],[],[],null)));
            Host=new(1280,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());ImGui.GetStyle().FontSizeBase=16;if(layout is not null)ImGui.LoadIniSettingsFromMemory(layout);Workspace=new(DataProject.Open(data,schemas));
            if(mode!="data")Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));Assert.True(Workspace.Show(mode=="data"?"items.json":"level.json"));Host.Frames(Workspace.Draw,5);
        }
        internal void Capture(string stage){Host.Drawing=true;Host.Frame(Workspace.Draw);var folder=Path.Combine(Repo(),".validation/UX01");Directory.CreateDirectory(folder);Host.Save(Path.Combine(folder,stage+"-"+Mode+".png"));Host.Drawing=false;}
        public void Dispose(){Workspace.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Theory][InlineData("data")][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public void Capture_current_layout_for_visual_review(string mode)
    {
        using var r=new Rig(mode);foreach(var name in new[]{"Files","Outliner","Inspector"})Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label==name&&!n.Bounds.IsEmpty);r.Capture(Environment.GetEnvironmentVariable("BED_UX01_CAPTURE_STAGE")??"after");
    }
    [Theory][InlineData(null)][InlineData("unknown")][InlineData("7")][InlineData("bed")][InlineData("engine")]
    public void Keymap_personal_setting_round_trips_preserves_other_keys_and_never_rewrites_on_load(string? initial)
    {
        var root=Directory.CreateTempSubdirectory("keymap-ux-").FullName;var previous=Wer;
        try
        {
            using var host=new HeadlessHost(1000,800){Drawing=false};
            var file=Path.Combine(root,"settings.json");
            var json=new System.Text.Json.Nodes.JsonObject { ["unowned"]="keep" };
            if(initial is not null)json["viewportKeymap"]=initial=="7"?System.Text.Json.Nodes.JsonValue.Create(7):System.Text.Json.Nodes.JsonValue.Create(initial);
            var before=json.ToJsonString();File.WriteAllText(file,before);
            using(var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"recent.json"))))
            {
                host.Frames(launcher.Draw,3);Assert.Equal(initial=="engine",Wer);Assert.Equal(before,File.ReadAllText(file));
                var note=(string)Controls.GetProperty("Note",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                Assert.Equal(initial is null or "unknown" or "7",note.Length>0);
                Controls.GetMethod("Choose",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[!Wer]);host.Frames(launcher.Draw,2);
                using var stored=System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                Assert.Equal("keep",stored.RootElement.GetProperty("unowned").GetString());
                Assert.Equal(Wer?"engine":"bed",stored.RootElement.GetProperty("viewportKeymap").GetString());
            }
            var wanted=Wer;Wer=!wanted;
            using var reopened=new ProjectLauncher(new RecentProjects(Path.Combine(root,"recent.json")));
            host.Frames(reopened.Draw,3);Assert.Equal(wanted,Wer);
        }
        finally {Wer=previous;Directory.Delete(root,true);}
    }
    private static Type Controls=>typeof(EditorWindow<Scene>).Assembly.GetType("Bed.Editor.ImGui.ViewportControls",true)!;
    private static bool Wer{get=>(bool)Controls.GetProperty("Wer",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;set=>Controls.GetProperty("Wer",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,value);}
    [Theory][InlineData("data")][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public unsafe void Default_tree_has_bottom_project_strip_left_tree_center_view_right_inspector_and_centered_playback(string mode)
    {
        using var r=new Rig(mode);var nodes=Ui.Snapshot().Nodes;UiNode Window(string name)=>Assert.Single(nodes,n=>n.Role==UiRole.Window&&n.Label==name);
        var files=Window("Files");var tree=Window("Outliner");var inspector=Window("Inspector");var surface=Window(mode=="data"?"Table":mode=="xy"?"Flat XY":"Viewport");
        Assert.True(tree.Bounds.X+tree.Bounds.Width<=surface.Bounds.X+.5f);Assert.True(surface.Bounds.X+surface.Bounds.Width<=inspector.Bounds.X+.5f);
        Assert.True(files.Bounds.Y>=tree.Bounds.Y+tree.Bounds.Height-.5f);Assert.True(files.Bounds.Y>=surface.Bounds.Y+surface.Bounds.Height-.5f);Assert.True(files.Bounds.Width>250);if(mode is "data" or "v1")Assert.InRange(files.Bounds.Width,1278,1280);
        var nativeFiles=ImGui.Internal.FindWindowByName("Files###panel.project.files");var nativeTree=ImGui.Internal.FindWindowByName("Outliner###outliner");Assert.True(nativeFiles.Ptr!=null&&nativeTree.Ptr!=null);Assert.NotEqual(nativeFiles.DockId,nativeTree.DockId);
        var controls=new[]{"Play","Pause","Step","Stop"}.Select(name=>Assert.Single(nodes,n=>n.Role==UiRole.Button&&n.Label==name)).ToArray();var left=controls[0].Bounds.X;var right=controls[^1].Bounds.X+controls[^1].Bounds.Width;Assert.InRange((left+right)/2,638,642);Assert.All(controls,n=>Assert.True(n.Bounds.Y+n.Bounds.Height<=tree.Bounds.Y));
        Assert.Empty(r.Workspace.Project.Dirty);
    }
    [Fact]
    public void Existing_floating_layout_is_restored_until_explicit_Reset_layout()
    {
        string saved;
        using(var first=new Rig("data"))
        {
            first.Host.Frames(()=>{first.Workspace.Draw();var files=ImGui.Internal.FindWindowByName("Files###panel.project.files");ImGui.Internal.DockContextProcessUndockWindow(ImGui.GetCurrentContext(),files,true);ImGui.SetWindowPos("Files###panel.project.files",new(870,370),ImGuiCond.Always);ImGui.SetWindowSize("Files###panel.project.files",new(350,300),ImGuiCond.Always);},3);
            saved=ImGui.SaveIniSettingsToMemory().ToString()!;
        }
        using var second=new Rig("data",saved);var files=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Files");Assert.InRange(files.Bounds.X,869,871);Assert.InRange(files.Bounds.Y,369,371);
        Assert.True(second.Workspace.Window!.Run("editor.reset_layout"));second.Host.Frames(second.Workspace.Draw,5);files=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Files");Assert.InRange(files.Bounds.X,0,1);Assert.True(files.Bounds.Width>250);if(second.Mode is "data" or "v1")Assert.InRange(files.Bounds.Width,1278,1280);Assert.True(files.Bounds.Y>500);
    }
    [Fact]
    public void Camera_aliases_and_fly_keys_change_only_the_view_and_WER_remains_scoped_to_the_canvas()
    {
        var previous=Wer;
        try
        {
            using var host=new HeadlessHost(1000,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());var model=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new([SceneObject.At("crate",null,Vector3.Zero)])),SceneEditing.Outline);model.Select("crate");var view=new ViewportView<Scene>(model,new SceneViewport());
            void Draw(){ImGui.SetNextWindowPos(new(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new(950,750),ImGuiCond.Always);Ui.Begin("Camera proof"u8);ImGui.SetKeyboardFocusHere();view.Draw();Ui.End();}
            host.Frames(Draw,4);var p=view.LastRect.Origin+new Vector2(400,200);var before=view.Camera;
            host.Drag(Draw,new(p.X,p.Y),new(p.X+30,p.Y+20),button:2);Assert.NotEqual(before.Target,view.Camera.Target);Assert.Equal(before.Yaw,view.Camera.Yaw);
            var io=ImGui.GetIO();before=view.Camera;io.AddKeyEvent(ImGuiKey.Mod_Alt,true);host.Frame(Draw);host.Drag(Draw,new(p.X,p.Y),new(p.X+30,p.Y+10));io.AddKeyEvent(ImGuiKey.Mod_Alt,false);host.Frame(Draw);Assert.NotEqual(before.Yaw,view.Camera.Yaw);Assert.Equal(before.Target,view.Camera.Target);
            before=view.Camera;io.AddKeyEvent(ImGuiKey.Mod_Alt,true);host.Frame(Draw);host.Drag(Draw,new(p.X,p.Y),new(p.X,p.Y+35),button:1);io.AddKeyEvent(ImGuiKey.Mod_Alt,false);host.Frame(Draw);Assert.NotEqual(before.Distance,view.Camera.Distance);Assert.Equal(before.Target,view.Camera.Target);
            Wer=true;host.Press(Draw,ImGuiKey.E);Assert.Equal(ViewportTool.Rotate,view.Tool);host.Press(Draw,ImGuiKey.R);Assert.Equal(ViewportTool.Scale,view.Tool);host.Press(Draw,ImGuiKey.W);Assert.Equal(ViewportTool.Move,view.Tool);
            Assert.True(view.SetTool(ViewportTool.Scale));before=view.Camera;io.AddMousePosEvent(p.X,p.Y);io.AddMouseButtonEvent(1,true);io.AddKeyEvent(ImGuiKey.W,true);host.Frames(Draw,5);io.AddKeyEvent(ImGuiKey.W,false);io.AddMouseButtonEvent(1,false);host.Frames(Draw,2);Assert.NotEqual(before.Target,view.Camera.Target);Assert.Equal(ViewportTool.Scale,view.Tool);
            Assert.False(model.Session.Dirty);Assert.Empty(model.Session.Journal);
        }
        finally{Wer=previous;}
    }

    [Fact]
    public void WER_choice_is_keyboard_accessible_in_preferences_and_changes_flat_tools_without_editing()
    {
        var previous=Wer;
        try
        {
            Wer=false;using var r=new Rig("xy");var window=(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(r.Workspace)!;
            r.Host.Press(r.Workspace.Draw,ImGuiKey.Comma,ctrl:true);r.Host.Frames(r.Workspace.Draw,3);var combo=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Viewport keymap"&&n.Role==UiRole.Combo);for(var i=0;i<8&&!Ui.Snapshot().Nodes.Single(n=>n.Id==combo.Id).Focused;i++)r.Host.Press(r.Workspace.Draw,ImGuiKey.Tab);
            Assert.True(Ui.Snapshot().Nodes.Single(n=>n.Id==combo.Id).Focused);r.Host.Press(r.Workspace.Draw,ImGuiKey.Enter);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="W/E/R"&&n.Role==UiRole.ListItem);r.Host.Press(r.Workspace.Draw,ImGuiKey.DownArrow);r.Host.Press(r.Workspace.Draw,ImGuiKey.Enter);r.Host.Frames(r.Workspace.Draw,2);Assert.True(Wer);
            var close=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Close options");var closed=Ui.RequestAction(close.Id,UiActions.Invoke);r.Host.Frames(r.Workspace.Draw,3);Assert.Equal(UiActionOutcome.Applied,closed.Outcome);
            var nodes=Ui.Snapshot().Nodes;var artwork=Assert.Single(nodes,n=>n.Role==UiRole.ListItem&&n.Label=="crate"&&n.Parent>=0&&nodes[n.Parent].Role==UiRole.Canvas);r.Host.Click(r.Workspace.Draw,artwork.Bounds.X+artwork.Bounds.Width/2,artwork.Bounds.Y+artwork.Bounds.Height/2);Assert.Contains("crate",window.Editor.Selection);
            r.Host.Press(r.Workspace.Draw,ImGuiKey.E);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Rotate Z ring");r.Host.Press(r.Workspace.Draw,ImGuiKey.R);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Scale width and height handle");Assert.False(window.Editor.Session.Dirty);
        }
        finally{Wer=previous;}
    }
    private sealed class Frames:IProjectFrameControlSession
    {
        internal bool Paused,Pending,Available=true;internal int Steps,Pauses,Resumes;
        public ProjectFrameState FrameControl=>new(Available,Paused,Pending,Steps,Available?"Ready":"No helper");
        public bool RequestFrameControl(ProjectFrameCommand command){if(command==ProjectFrameCommand.Pause){Paused=true;Pauses++;}else if(command==ProjectFrameCommand.Resume){Paused=false;Resumes++;}else Steps++;return true;}
    }
    [Fact]
    public void Centered_playback_invokes_the_existing_owned_handlers_once_and_keeps_pending_controls_disabled()
    {
        using var host=new HeadlessHost(1280,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());var model=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Scene.Empty),SceneEditing.Outline);var play=0;var stop=0;var running=false;var frames=new Frames();
        var window=new EditorWindow<Scene>(model){ProjectPlayAction=()=>{play++;running=true;return true;},ProjectStopAction=()=>{stop++;running=false;return true;},ProjectPlaying=()=>running};
        typeof(EditorWindow<Scene>).GetField("ProjectFrames",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,(Func<IProjectFrameControlSession?>)(()=>running?frames:null));
        host.Frames(window.DrawDocked,4);
        UiNode Button(string name)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==name);
        void Press(string name){var node=Button(name);Assert.True(node.Enabled,node.DisabledReason);var action=Ui.RequestAction(node.Id,UiActions.Invoke);host.Frames(window.DrawDocked,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);}
        Assert.False(Button("Pause").Enabled);Assert.False(Button("Step").Enabled);Press("Play");Assert.Equal(1,play);Assert.False(Button("Step").Enabled);Press("Pause");Assert.Equal(1,frames.Pauses);Press("Step");Assert.Equal(1,frames.Steps);frames.Pending=true;host.Frames(window.DrawDocked,2);Assert.False(Button("Step").Enabled);frames.Pending=false;host.Frames(window.DrawDocked,2);Press("Resume");Assert.Equal(1,frames.Resumes);Press("Stop");Assert.Equal(1,stop);Assert.False(model.Session.Dirty);Assert.Empty(model.Session.Journal);
    }

}
