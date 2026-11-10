using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class OutlinerKeyboardUi13Tests
{
    private sealed class Rig:IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("outliner-ui13-").FullName;
        public readonly HeadlessHost Host=new(1600,1000){Drawing=false};
        public readonly DataWorkspace Workspace;
        private readonly string path,original;
        public EditorModel<Scene> Editor=>Workspace.SceneWindow!.Editor;
        public Rig(int count=4,bool nested=false)
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var objects=Enumerable.Range(0,count).Select(i=>SceneObject.At(Id(i),null,new(i,0,0))).ToArray();
            if(nested)objects[1]=objects[1] with{Parent=Id(0)};
            path=Path.Combine(data,"level.json");original=new SceneFormat().Write(new Scene([..objects]));File.WriteAllText(path,original);
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Documents.BindScene("level.json","scene",out var why),why);Assert.True(Workspace.Show("level.json"),Workspace.Said);
            Workspace.SceneViewport!.Camera=new(new Vector3(1.5f,0,0),0,0,10);
            EditorWindow<Scene>.Configure(ImGui.GetIO());Host.Frames(Workspace.Draw,4);Outliner();
        }
        public static string Id(int index)=>"object_"+index.ToString("D2",System.Globalization.CultureInfo.InvariantCulture);
        public void Key(ImGuiKey key,bool ctrl=false,bool shift=false){Host.Press(Workspace.Draw,key,ctrl:ctrl,shift:shift);Host.Frames(Workspace.Draw,2);}
        public void Outliner()
        {
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            try
            {
                var found=false;
                for(var i=0;i<16;i++)
                {
                    Key(ImGuiKey.Tab);var target=ImGui.GetCurrentContext().NavWindowingTarget;
                    var title=target.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)target.Name)??"";
                    if(title.Contains("###outliner",StringComparison.Ordinal)){found=true;break;}
                }
                Assert.True(found,"Native Ctrl+Tab did not reach Outliner.");
            }
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Host.Frames(Workspace.Draw,3);}
        }
        public UiNode Row(int index)
        {
            var snapshot=Ui.Snapshot();return Assert.Single(snapshot.Nodes,n=>n.Role==UiRole.ListItem&&(n.Label==Id(index)||n.Label=="> "+Id(index))&&Inside(snapshot,n,"Outliner"));
        }
        public void First()
        {
            for(var i=0;!Row(0).Focused&&i<20;i++)Key(ImGuiKey.Tab);
            Assert.True(Row(0).Focused,"First row="+Row(0)+"; focused="+string.Join(",",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Label))+"; nav="+ImGui.GetCurrentContext().NavId.Value);Key(ImGuiKey.Enter);Assert.Equal(new[]{Id(0)},Editor.Selection);
        }
        public void Selection(params int[] indexes)
        {
            Assert.Equal(indexes.Select(Id),Editor.Selection);var snapshot=Ui.Snapshot();
            foreach(var row in snapshot.Nodes.Where(n=>n.Role==UiRole.ListItem&&Inside(snapshot,n,"Outliner")))Assert.Equal(Editor.Selection.Contains(row.Label.StartsWith("> ",StringComparison.Ordinal)?row.Label[2..]:row.Label),row.Selected);
            var canvas=Assert.Single(snapshot.Nodes,n=>n.Role==UiRole.Canvas&&n.Label=="Viewport");var canvasIndex=snapshot.Nodes.ToList().IndexOf(canvas);
            if(indexes.All(i=>i<4))Assert.Equal(indexes.Select(Id).Order(),snapshot.Nodes.Where(n=>n.Parent==canvasIndex&&n.Selected).Select(n=>n.Label).Order());
            if(indexes.Length>1)Assert.Contains(snapshot.Nodes,n=>n.Role==UiRole.Text&&n.Label==indexes.Length+" selected"&&Inside(snapshot,n,"Inspector"));
        }
        private static bool Inside(UiSnapshot snapshot,UiNode node,string window)
        {
            for(var parent=node.Parent;parent>=0;parent=snapshot.Nodes[parent].Parent)
                if(snapshot.Nodes[parent].Role==UiRole.Window&&snapshot.Nodes[parent].Label==window)return true;
            return false;
        }
        public void Unchanged(){Assert.Equal(original,File.ReadAllText(path));Assert.False(Editor.Session.Dirty);Assert.Empty(Editor.Session.Journal);Assert.False(Editor.Session.History.CanUndo);}
        public void Dispose(){Workspace.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Native_shift_arrows_extend_and_shrink_range_and_CtrlSpace_toggles_once_with_viewport_and_Inspector_following()
    {
        using var r=new Rig();r.First();var ids=Enumerable.Range(0,4).Select(i=>r.Row(i).Id).ToArray();
        r.Key(ImGuiKey.DownArrow,shift:true);r.Selection(0,1);Assert.True(r.Row(1).Focused);
        r.Key(ImGuiKey.DownArrow,shift:true);r.Selection(0,1,2);Assert.True(r.Row(2).Focused);
        r.Key(ImGuiKey.UpArrow,shift:true);r.Selection(0,1);Assert.True(r.Row(1).Focused);
        r.Key(ImGuiKey.Space,ctrl:true);r.Selection(0);Assert.True(r.Row(1).Focused);
        r.Key(ImGuiKey.Space,ctrl:true);r.Selection(0,1);
        r.Key(ImGuiKey.UpArrow,shift:true);r.Selection(1,0);Assert.Equal(Rig.Id(0),r.Editor.Primary);
        Assert.Equal(ids,Enumerable.Range(0,4).Select(i=>r.Row(i).Id));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Shift+Up/Down extends selection. Ctrl+Space toggles the focused object.");
        r.Unchanged();
    }
    [Fact]
    public void External_selection_resets_anchor_and_CtrlSpace_can_leave_no_selection_without_a_document_edit()
    {
        using var r=new Rig();r.First();r.Key(ImGuiKey.DownArrow,shift:true);r.Key(ImGuiKey.DownArrow,shift:true);r.Selection(0,1,2);
        // Simulate selection supplied by the viewport/another host view, then continue actual Outliner keys.
        Assert.True(r.Editor.Select(Rig.Id(3)));r.Key(ImGuiKey.UpArrow,shift:true);r.Selection(3,2,1);Assert.Equal(Rig.Id(1),r.Editor.Primary);
        r.Key(ImGuiKey.Enter);r.Selection(1);r.Key(ImGuiKey.Space,ctrl:true);r.Selection();Assert.True(r.Row(1).Focused);
        r.Key(ImGuiKey.Space,ctrl:true);r.Selection(1);r.Unchanged();
    }
    [Fact]
    public void Tree_order_and_focused_subtree_bound_range_selection_without_adding_outside_objects()
    {
        using var r=new Rig(nested:true);r.First();r.Key(ImGuiKey.DownArrow,shift:true);r.Selection(0,1);
        r.Key(ImGuiKey.DownArrow,shift:true);r.Selection(0,1,2);
        Assert.True(r.Editor.FocusOn(Rig.Id(0)));r.Host.Frames(r.Workspace.Draw,3);r.Outliner();
        r.First();r.Key(ImGuiKey.DownArrow,shift:true);r.Selection(0,1);r.Key(ImGuiKey.DownArrow,shift:true);r.Selection(0,1);
        var nodes=Ui.Snapshot().Nodes;var outline=Assert.Single(nodes,n=>n.Role==UiRole.Window&&n.Label=="Outliner");var parent=nodes.ToList().IndexOf(outline);
        Assert.DoesNotContain(nodes,n=>n.Parent==parent&&n.Role==UiRole.ListItem&&n.Label==Rig.Id(2));
        Assert.DoesNotContain(nodes,n=>n.Role==UiRole.ListItem&&n.Label==Rig.Id(2)&&n.Selected);r.Unchanged();
    }
    [Fact]
    public void Shift_range_crosses_clipped_rows_and_scrolls_its_keyboard_cursor_without_drawing_the_whole_list()
    {
        using var r=new Rig(80);r.First();
        for(var i=0;i<40;i++)r.Key(ImGuiKey.DownArrow,shift:true);
        Assert.Equal(Enumerable.Range(0,41).Select(Rig.Id),r.Editor.Selection);var row=r.Row(40);Assert.True(row.Focused);Assert.True(row.Selected);
        var outline=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Outliner");
        Assert.InRange(row.Bounds.Y,outline.Bounds.Y,outline.Bounds.Y+outline.Bounds.Height-row.Bounds.Height);
        var nodes=Ui.Snapshot().Nodes;var outlineIndex=nodes.ToList().IndexOf(outline);
        Assert.InRange(nodes.Count(n=>n.Role==UiRole.ListItem&&n.Parent==outlineIndex),1,79);
        r.Key(ImGuiKey.UpArrow,shift:true);Assert.Equal(40,r.Editor.Selection.Count);Assert.True(r.Row(39).Focused);r.Unchanged();
    }
}
