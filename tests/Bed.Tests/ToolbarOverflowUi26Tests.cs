using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using ImGui=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ToolbarOverflowUi26Tests
{
    [Theory][InlineData(1f,false)][InlineData(2f,false)][InlineData(2f,true)]
    public void Narrow_toolbar_commands_fit_and_native_overflow_save_uses_the_same_action(float scale,bool project)
    {
        var root=Directory.CreateTempSubdirectory("toolbar-ui26-").FullName;
        try
        {
        using var host=new HeadlessHost(330,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;
        var schemas=new JsonSchemas().Add("data.schema.json","""{"type":"object","properties":{"value":{"type":"number"}}}""");
        var path=Path.Combine(root,"data.json");File.WriteAllText(path,"""{"value":1}""");
        var session=DataKit.Open(path,schemas,"data.schema.json");Assert.True(session.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(2))));
        var editor=new EditorModel<DataDocument>(session,DataEditing.Outline);var window=new EditorWindow<DataDocument>(editor);var saves=0;
        window.SaveAction=()=>{saves++;return(true,"Saved through the existing callback.");};
        if(project){window.ProjectPlayAction=()=>true;typeof(EditorWindow<DataDocument>).GetProperty("ProjectBuildAndPlayAction",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(window,(Func<bool>)(()=>true));editor.Add(new Panel("project.checks","Checks",()=>{}));}
        host.Frames(window.DrawDocked,4);
        UiNode Node(string label,UiRole role)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role==role);
        var more=Node("More",UiRole.Menu);Assert.InRange(more.Bounds.X,0,329);Assert.True(more.Bounds.X+more.Bounds.Width<=330);
        host.Click(window.DrawDocked,more.Bounds.X+more.Bounds.Width/2,more.Bounds.Y+more.Bounds.Height/2);host.Frames(window.DrawDocked,2);
        foreach(var label in new[]{"File","Edit","View","Tools","Help"})_ = Node(label,UiRole.Menu);
        var save=Node("Save",UiRole.MenuItem);Assert.True(save.Enabled);Assert.Contains("Ctrl+S",save.Tooltip);
        foreach(var label in (project?new[]{"Save","Undo","Redo","Checks","Play","Build and Play","All objects >"}:new[]{"Save","Undo","Redo","All objects >"}))
        {var n=Node(label,UiRole.MenuItem);Assert.True(n.Bounds.X>=0&&n.Bounds.X+n.Bounds.Width<=330,$"{label}: {n.Bounds}");}
        Assert.False(Node("Redo",UiRole.MenuItem).Enabled);
        host.Click(window.DrawDocked,save.Bounds.X+save.Bounds.Width/2,save.Bounds.Y+save.Bounds.Height/2);Assert.Equal(1,saves);Assert.Single(session.Journal);
        host.Press(window.DrawDocked,ImGuiKey.S,ctrl:true);Assert.Equal(2,saves);Assert.Single(session.Journal);
        host.Press(window.DrawDocked,ImGuiKey.F,alt:true);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Save as...");
        Assert.Equal("""{"value":1}""",File.ReadAllText(path));
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory][InlineData(1024,2f,false)][InlineData(1024,2f,true)][InlineData(800,1.5f,true)]
    public void Menus_that_fit_stay_visible_while_toolbar_overflow_keeps_its_native_actions(int width,float scale,bool project)
    {
        var root=Directory.CreateTempSubdirectory("toolbar-ui26-medium-").FullName;
        try
        {
            using var host=new HeadlessHost(width,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;
            var schemas=new JsonSchemas().Add("data.schema.json","""{"type":"object","properties":{"value":{"type":"number"}}}""");
            var path=Path.Combine(root,"data.json");File.WriteAllText(path,"""{"value":1}""");var session=DataKit.Open(path,schemas,"data.schema.json");Assert.True(session.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(2))));
            var editor=new EditorModel<DataDocument>(session,DataEditing.Outline);var window=new EditorWindow<DataDocument>(editor);var saves=0;
            window.SaveAction=()=>{saves++;return(true,"Saved by existing handler.");};
            if(project){window.ProjectPlayAction=()=>true;typeof(EditorWindow<DataDocument>).GetProperty("ProjectBuildAndPlayAction",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(window,(Func<bool>)(()=>true));editor.Add(new Panel("project.checks","Checks",()=>{}));}
            host.Frames(window.DrawDocked,4);
            var menus=new[]{"File","Edit","View","Tools","Help"}.Select(label=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Menu&&n.Label==label)).ToArray();
            Assert.All(menus,n=>{Assert.Equal(menus[0].Parent,n.Parent);UiTreeAssertionsA448.FullyShown(n,n.Label);Assert.True(n.Bounds.X>=0&&n.Bounds.X+n.Bounds.Width<=width);});
            var overflow=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Menu&&n.Label=="More").ToArray();
            if(overflow.Length>0)
            {
                var more=Assert.Single(overflow);Assert.Equal("M",more.AccessKey);host.Press(window.DrawDocked,ImGuiKey.M,alt:true);host.Frames(window.DrawDocked,2);
                var save=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Save");Assert.Contains("Ctrl+S",save.Tooltip);var clip=ImGui.GetCurrentContext().NavWindow.InnerClipRect;Assert.True(save.Bounds.X>=clip.Min.X-1&&save.Bounds.X+save.Bounds.Width<=clip.Max.X+1);Assert.True(save.Bounds.Y>=clip.Min.Y-1&&save.Bounds.Y+save.Bounds.Height<=clip.Max.Y+1);
                host.Click(window.DrawDocked,save.Bounds.X+save.Bounds.Width/2,save.Bounds.Y+save.Bounds.Height/2);Assert.Equal(1,saves);
            }
            else Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Save");
            host.Press(window.DrawDocked,ImGuiKey.F,alt:true);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Save as...");
            Assert.Equal("""{"value":1}""",File.ReadAllText(path));Assert.Single(session.Journal);
        }
        finally{Directory.Delete(root,true);}
    }

}
