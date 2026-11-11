using System.Numerics;
using Bed;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ViewportPolishS5hTests
{
    private static (EditorModel<Scene> Editor,ViewportView<Scene> View) Model()
    {
        var scene=new Scene([SceneObject.At("selected",null,new Vector3(20,4,8)) with {Scale=new Vector3(4,2,3)},SceneObject.At("other",null,new Vector3(-100,0,0))]);
        var model=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
        model.Select("selected");return(model,new(model,new SceneViewport()));
    }
    private static void Draw(ViewportView<Scene> view)
    {
        ImGui.SetNextWindowPos(new float2(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(900,700),ImGuiCond.Always);
        Ui.Begin("View");ImGui.SetKeyboardFocusHere();view.Draw();Ui.End();
    }
    [Fact]
    public void Focused_F_frames_selected_transformed_mesh_and_tree_explains_navigation_and_grid()
    {
        var (editor,view)=Model();using var host=new HeadlessHost(1000,800){Drawing=false};
        Action draw=()=>Draw(view);host.Frames(draw,4);var initial=view.Camera;
        host.Press(draw,ImGuiKey.F,ctrl:true);Assert.Equal(initial,view.Camera);
        host.Press(draw,ImGuiKey.F);host.Frames(draw,2);
        Assert.Equal(new Vector3(20,4,8),view.Camera.Target);
        foreach(var point in MeshData.Cube.Positions)
        {
            var world=point*new Vector3(4,2,3)+new Vector3(20,4,8);
            var projected=view.Camera.Project(world,view.LastRect.Size);Assert.NotNull(projected);
            Assert.InRange(projected.Value.Screen.X,0,view.LastRect.Size.X);Assert.InRange(projected.Value.Screen.Y,0,view.LastRect.Size.Y);
        }
        var status=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.Contains("F: frame selected"));
        Assert.Contains("Right-drag: orbit",status.Label);Assert.Contains("Shift+right-drag: pan",status.Label);
        Assert.Contains("Wheel or +/-: zoom",status.Label);Assert.Contains("Grid: 0.5 m",status.Label);
        Assert.False(status.Bounds.IsEmpty);Assert.Equal(0,editor.Session.History.UndoCount);Assert.False(editor.Session.Dirty);
        editor.Select();var framed=view.Camera;host.Press(draw,ImGuiKey.F);Assert.Equal(framed,view.Camera);
        Assert.Equal("Select an object to frame.",view.Said);
    }
    [Fact]
    public void Shift_right_drag_pans_without_orbiting_or_editing_and_plain_right_drag_orbits()
    {
        var (editor,view)=Model();using var host=new HeadlessHost(1000,800){Drawing=false};Action draw=()=>Draw(view);host.Frames(draw,4);
        var before=view.Camera;var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Shift,true);host.Frames(draw,1);
        var p=view.LastRect.Origin+new Vector2(400,200);
        host.Drag(draw,new float2(p.X,p.Y),new float2(p.X+45,p.Y+20),button:1);
        io.AddKeyEvent(ImGuiKey.Mod_Shift,false);host.Frames(draw,1);
        Assert.NotEqual(before.Target,view.Camera.Target);Assert.Equal(before.Yaw,view.Camera.Yaw);Assert.Equal(before.Pitch,view.Camera.Pitch);Assert.Equal(before.Distance,view.Camera.Distance);
        var panned=view.Camera;host.Drag(draw,new float2(p.X,p.Y),new float2(p.X+30,p.Y+15),button:1);
        Assert.Equal(panned.Target,view.Camera.Target);Assert.NotEqual(panned.Yaw,view.Camera.Yaw);
        Assert.False(editor.Session.Dirty);Assert.Equal(0,editor.Session.History.UndoCount);
    }
}
