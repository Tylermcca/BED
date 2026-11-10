using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneViewFlagsUi19Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1600,1100){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly string Project,Local;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;public ViewportView<Scene> View=>Workspace.SceneViewport!;
        public Rig(string root,string user="local")
        {
            Project=Directory.CreateDirectory(Path.Combine(root,"project")).FullName;Local=Path.Combine(root,user);
            var data=Directory.CreateDirectory(Path.Combine(Project,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Project,"schemas")).FullName;
            var scene=new Scene([SceneObject.At("parent",null,Vector3.Zero),SceneObject.At("child",null,new(2,0,0)) with{Parent="parent"},SceneObject.At("free",null,new(-4,0,0))]);
            foreach(var name in new[]{"a.json","b.json"})if(!File.Exists(Path.Combine(data,name)))File.WriteAllText(Path.Combine(data,name),new SceneFormat().Write(scene));
            Workspace=new(DataProject.Open(data,schemas));foreach(var name in new[]{"a.json","b.json"})Assert.True(Workspace.Documents.BindScene(name,"scene",out _));
            Workspace.BindPlaySettings(Project,null,Local);EditorWindow<Scene>.Configure(ImGui.GetIO());Show("a.json");View.Camera=new(Vector3.Zero,0,.25f,16);Frames(3);
        }
        public void Frames(int n=2)=>Host.Frames(Workspace.Draw,n);
        public void Show(string name){Assert.True(Workspace.Show(name),Workspace.Said);Frames(3);}
        public UiNode Row(string id)
        {
            var tree=Ui.Snapshot();return Assert.Single(tree.Nodes,n=>n.Role==UiRole.ListItem&&(n.Label==id||n.Label=="> "+id||n.Label.StartsWith(id+" [",StringComparison.Ordinal)||n.Label.StartsWith("> "+id+" [",StringComparison.Ordinal))&&Inside(tree,n,"Outliner"));
        }
        private static bool Inside(UiSnapshot tree,UiNode node,string title)
        {for(var i=node.Parent;i>=0;i=tree.Nodes[i].Parent)if(tree.Nodes[i].Role==UiRole.Window&&tree.Nodes[i].Label==title)return true;return false;}
        public void Native(string id,ImGuiKey key)
        {var row=Row(id);Host.Click(Workspace.Draw,row.Bounds.X+row.Bounds.Width/2,row.Bounds.Y+row.Bounds.Height/2);Host.Press(Workspace.Draw,key);Frames();}
        public void Select(params string[] ids){Assert.True(Window.Editor.Select(ids));Frames();}
        public void Canvas()
        {
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            try
            {
                var found=false;
                for(var i=0;i<16;i++)
                {
                    Host.Press(Workspace.Draw,ImGuiKey.Tab);var target=ImGui.GetCurrentContext().NavWindowingTarget;
                    var title=target.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)target.Name)??"";
                    if(title.Contains("panel.editor.viewport",StringComparison.Ordinal)){found=true;break;}
                }
                Assert.True(found);
            }
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Frames(3);}
            for(var i=0;!Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Canvas&&n.Focused)&&i<12;i++)Host.Press(Workspace.Draw,ImGuiKey.Tab);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas&&n.Focused);
        }
        public string[] Rendered()=>View.RenderRequest!.CopyItems().Select(i=>i.Id).ToArray();
        public void Clean(){Assert.False(Window.Editor.Session.Dirty);Assert.Equal(0,Window.Editor.Session.History.UndoCount);Assert.Empty(Window.Editor.Session.Journal);}
        public void Dispose(){Workspace.Dispose();Host.Dispose();}
    }
    [Fact] public void Native_H_hides_the_branch_from_draw_and_pick_keeps_named_rows_and_invalidates_old_pick_results()
    {
        using var files=new CapturedExportTests.Owned();using var r=new Rig(files.Root);
        Assert.Equal(new[]{"parent","child","free"},r.Rendered());var old=r.View.RenderRequest!;
        var point=r.View.Camera.Project(Vector3.Zero,r.View.LastRect.Size)!.Value.Screen+r.View.LastRect.Origin;
        var stale=old.Pick(point);Assert.Equal("parent",stale.ItemId);
        r.Native("parent",ImGuiKey.H);Assert.Equal(new[]{"free"},r.Rendered());Assert.Contains("[hidden]",r.Row("parent").Label);Assert.Contains("[hidden]",r.Row("child").Label);
        Assert.Null(r.View.RenderRequest!.Pick(point).ItemId);Assert.False(r.View.TrySelectRenderResult(stale));Assert.Equal(new[]{"parent"},r.Window.Editor.Selection);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Parent>=0&&Ui.Snapshot().Nodes[n.Parent].Role==UiRole.Canvas&&n.Label=="parent");r.Clean();
        r.Native("parent",ImGuiKey.H);Assert.Equal(new[]{"parent","child","free"},r.Rendered());Assert.DoesNotContain("hidden",r.Row("parent").Label);r.Clean();
    }
    [Fact] public void Locked_child_blocks_parent_moves_single_batch_guarded_and_Inspector_transforms_and_native_arrows()
    {
        using var files=new CapturedExportTests.Owned();using var r=new Rig(files.Root);r.Native("child",ImGuiKey.L);Assert.Contains("[locked]",r.Row("child").Label);
        var editor=r.Window.Editor;var original=editor.Session.Current;
        Assert.True(editor.Execute(new MoveObject("free",new(-5,0,0)),"free"));Assert.True(editor.Session.Undo());
        Assert.Same(original,editor.Session.Current);r.Select("parent");
        Assert.False(editor.Execute(new MoveObject("parent",Vector3.UnitZ),"parent"));Assert.Contains(editor.Refused,i=>i.Message.Contains("child",StringComparison.Ordinal)&&i.Message.Contains("locked",StringComparison.Ordinal));
        Assert.False(editor.ExecuteBatch([new RotateObject("parent",Quaternion.CreateFromAxisAngle(Vector3.UnitY,.5f))],"parent"));
        Assert.False(editor.ExecuteAt(editor.Session.Revision,[new ScaleObject("parent",new(2))],"parent"));
        r.Select("child");Assert.False(r.Window.Inspector!.Set("position.x",8d));Assert.False(r.Window.Inspector.Set("rotation.yaw",30d));Assert.False(r.Window.Inspector.Set("scale.x",2d));
        r.Canvas();r.Host.Press(r.Workspace.Draw,ImGuiKey.RightArrow);r.Frames();Assert.Contains("locked",r.View.Said);Assert.Empty(r.View.MoveTargets);
        foreach(var key in new[]{ImGuiKey.R,ImGuiKey.X,ImGuiKey.RightArrow,ImGuiKey.S,ImGuiKey.RightArrow})r.Host.Press(r.Workspace.Draw,key);
        Assert.True(r.View.SetTool(ViewportTool.Move));r.Frames();
        var screen=r.View.Camera.Project(new(2,0,0),r.View.LastRect.Size)!.Value.Screen+r.View.LastRect.Origin;
        r.Host.Drag(r.Workspace.Draw,new(screen.X,screen.Y),new(screen.X+60,screen.Y));r.Frames();
        Assert.Empty(r.View.MoveTargets);Assert.Empty(r.View.RotationTargets);Assert.Empty(r.View.ScaleTargets);
        Assert.Same(original,editor.Session.Current);Assert.False(editor.Session.Dirty);Assert.Equal(0,editor.Session.History.UndoCount);r.Native("child",ImGuiKey.L);Assert.DoesNotContain("locked",r.Row("child").Label);
        Assert.True(editor.Execute(new MoveObject("parent",Vector3.UnitZ),"parent"));Assert.Equal(1,editor.Session.History.UndoCount);
        r.Native("parent",ImGuiKey.L);Assert.Contains("[locked]",r.Row("child").Label);Assert.True(editor.Session.Undo());Assert.Same(original,editor.Session.Current);Assert.True(editor.Session.Redo());
    }
    [Fact] public void View_choices_persist_per_level_and_user_without_changing_either_saved_level_or_history()
    {
        using var files=new CapturedExportTests.Owned();string first,second,project;
        using(var r=new Rig(files.Root))
        {
            project=r.Project;first=File.ReadAllText(Path.Combine(project,"data","a.json"));second=File.ReadAllText(Path.Combine(project,"data","b.json"));
            r.Native("parent",ImGuiKey.H);r.Native("child",ImGuiKey.L);r.Clean();
            Assert.Single(Directory.GetFiles(r.Local,"*.json",SearchOption.AllDirectories));Assert.False(Directory.Exists(Path.Combine(project,".bed")));
            r.Show("b.json");Assert.Equal(3,r.Rendered().Length);Assert.DoesNotContain("[hidden]",r.Row("parent").Label);r.Clean();
        }
        using(var reopened=new Rig(files.Root))
        {Assert.Equal(new[]{"free"},reopened.Rendered());Assert.Contains("[hidden]",reopened.Row("child").Label);Assert.Contains("[locked]",reopened.Row("child").Label);reopened.Clean();}
        using(var otherUser=new Rig(files.Root,"other-user"))
        {Assert.Equal(3,otherUser.Rendered().Length);Assert.DoesNotContain("[locked]",otherUser.Row("child").Label);Assert.DoesNotContain("[hidden]",otherUser.Row("parent").Label);otherUser.Clean();}
        Assert.Equal(first,File.ReadAllText(Path.Combine(project,"data","a.json")));Assert.Equal(second,File.ReadAllText(Path.Combine(project,"data","b.json")));
    }
    [Fact] public void Native_typing_keeps_H_and_L_in_the_field_and_Play_or_closed_windows_refuse_view_changes()
    {
        using var files=new CapturedExportTests.Owned();using var r=new Rig(files.Root);r.Select("parent");
        var search=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Search objects");r.Host.Click(r.Workspace.Draw,search.Bounds.X+20,search.Bounds.Y+search.Bounds.Height/2);
        r.Host.Press(r.Workspace.Draw,ImGuiKey.H);r.Host.Press(r.Workspace.Draw,ImGuiKey.L);r.Host.Type(r.Workspace.Draw,"hl");r.Frames();Assert.Equal(3,r.Rendered().Length);r.Clean();
        r.Window.ProjectPlaying=()=>true;Assert.False(r.Window.Run("editor.hide_objects"));Assert.Contains("Stop Play",r.Window.Status);r.Window.ProjectPlaying=()=>false;
        var old=r.Window;r.Workspace.Dispose();Assert.False(old.Run("editor.lock_objects"));Assert.Contains("Open this level",old.Status);
    }
}
