using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ViewportPickingA11y11Tests
{
    private sealed class Rig:IDisposable
    {
        internal readonly HeadlessHost Host=new(1000,800){Drawing=false};
        internal readonly EditorModel<Scene> Editor;
        internal readonly ViewportView<Scene> View;
        internal bool OtherPanel;
        internal Rig(bool empty=false)
        {
            SceneObject Named(string id,string name,Vector3 position)=>SceneObject.At(id,null,position) with{Properties=ImmutableSortedDictionary<string,JsonElement>.Empty.Add("name",JsonSerializer.SerializeToElement(name))};
            var scene=empty?Scene.Empty:new Scene([Named("child","Small part",new(1,0,0)) with{Parent="root"},Named("loose","Loose marker",new(-2,0,0)),Named("root","Main frame",new(10,0,0))]);
            Editor=new(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
            View=new(Editor,new SceneViewport()){Camera=new(Vector3.Zero,0,.3f,30)};EditorWindow<Scene>.Configure(ImGui.GetIO());Host.Frames(Draw,4);
        }
        internal void Draw(){ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(950,750));Ui.Begin("Picking fixture"u8);Ui.Button("Before"u8);View.Draw();Ui.Button("After"u8);Ui.End();
            if(OtherPanel){ImGui.SetNextWindowPos(new float2(700,20));ImGui.SetNextWindowSize(new float2(250,200));Ui.Begin("Other panel"u8,ImGuiWindowFlags.NoFocusOnAppearing);Ui.Button("Other control"u8);Ui.End();}}
        internal UiNode Canvas=>Ui.Snapshot().Nodes.Single(n=>n.Role==UiRole.Canvas);
        internal UiNode Status=>Ui.Snapshot().Nodes.Single(n=>n.Role==UiRole.Status&&(n.Label.StartsWith("Selected:",StringComparison.Ordinal)||n.Label=="No object selected."));
        internal void Key(ImGuiKey key,bool shift=false,bool ctrl=false)=>Host.Press(Draw,key,shift:shift,ctrl:ctrl);
        internal void Focus(){for(var i=0;!Canvas.Focused&&i<12;i++)Key(ImGuiKey.Tab);Assert.True(Canvas.Focused);}
        public void Dispose()=>Host.Dispose();
    }
    [Fact]
    public void Native_Tab_wraps_in_shared_Outliner_hierarchy_order_and_Enter_frames_without_editing()
    {
        using var r=new Rig();var original=r.Editor.Session.Current;var camera=r.View.Camera;r.Focus();Assert.Null(r.Editor.Primary);
        foreach(var pair in new[]{("loose","Loose marker"),("root","Main frame"),("child","Small part"),("loose","Loose marker")})
        {r.Key(ImGuiKey.Tab);Assert.Equal(pair.Item1,r.Editor.Primary);Assert.True(r.Canvas.Focused);Assert.True(ImGui.GetCurrentContext().NavCursorVisible);Assert.Contains("Selected: "+pair.Item2+".",r.Status.Label);}
        r.Key(ImGuiKey.Tab,shift:true);Assert.Equal("child",r.Editor.Primary);Assert.Equal(camera,r.View.Camera);
        r.Key(ImGuiKey.Enter);Assert.Equal("child",r.Editor.Primary);Assert.Equal(new Vector3(11,0,0),r.View.Camera.Target);Assert.NotEqual(camera,r.View.Camera);
        var framed=r.View.Camera;r.Key(ImGuiKey.KeypadEnter);Assert.Equal("child",r.Editor.Primary);Assert.Equal(framed,r.View.Camera);
        Assert.Same(original,r.Editor.Session.Current);Assert.Empty(r.Editor.Session.Journal);Assert.Equal(0,r.Editor.Session.History.UndoCount);Assert.False(r.Editor.Session.Dirty);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Escape_returns_Tab_to_native_controls_in_both_directions_without_a_keyboard_trap(bool reverse)
    {
        using var r=new Rig();r.Focus();r.Key(ImGuiKey.Tab);var selected=r.Editor.Primary;r.Key(ImGuiKey.Escape);r.Key(ImGuiKey.Tab,shift:reverse);
        Assert.False(r.Canvas.Focused);Assert.Equal(selected,r.Editor.Primary);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Focused&&n.Role==UiRole.Button&&n.Label==(reverse?"Before":"Move (G)"));
        if(!reverse)
        {r.Key(ImGuiKey.Enter);Assert.Equal(ViewportTool.Move,r.View.Tool);Assert.True(r.Canvas.Focused);r.Key(ImGuiKey.Escape);r.Key(ImGuiKey.Tab);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Focused&&n.Label=="Move (G)");r.Key(ImGuiKey.Tab);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Focused&&n.Label=="Rotate (R)");}
        Assert.Empty(r.Editor.Session.Journal);
    }
    private sealed class Sink:IAccessKitUpdateSink
    {
        internal readonly List<AccessKitProjectedUpdate> Updates=[];
        public bool TryApply(AccessKitProjectedUpdate update){Updates.Add(update);return true;}
    }
    [Fact]
    public void Each_keyboard_pick_announces_the_name_once_through_the_existing_polite_status()
    {
        using var r=new Rig();r.Focus();var projection=new AccessKitProjection();var sink=new Sink();var statusId=r.Status.Id;
        void Frame(){r.Host.Frame(r.Draw);projection.Submit(Ui.Snapshot(),"Picking fixture",new(0,0,1000,800),1,1,sink);}
        void Press(ImGuiKey key){ImGui.GetIO().AddKeyEvent(key,true);Frame();ImGui.GetIO().AddKeyEvent(key,false);for(var i=0;i<3;i++)Frame();}
        int Count()=>sink.Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.SourceId==statusId);
        Frame();Assert.Equal(1,Count());Press(ImGuiKey.Tab);Assert.Equal(2,Count());Assert.Contains("Loose marker",r.Status.Label);
        for(var i=0;i<5;i++)Frame();Assert.Equal(2,Count());Press(ImGuiKey.Tab);Assert.Equal(3,Count());Assert.Contains("Main frame",r.Status.Label);
        Press(ImGuiKey.Enter);Assert.Equal(3,Count());Assert.Equal("root",r.Editor.Primary);
    }
    [Fact]
    public void Empty_and_Play_owned_views_do_not_create_a_selection_or_edit()
    {
        using var r=new Rig(empty:true);r.Focus();r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);Assert.Null(r.Editor.Primary);Assert.Equal("No object selected.",r.Status.Label);Assert.Empty(r.Editor.Session.Journal);
        r.Editor.Add(new PlayMode<Scene>("fixture","Fixture",_=>new Preview()));Assert.True(r.Editor.Play("fixture"));var camera=r.View.Camera;r.Host.Frames(r.Draw,3);
        Assert.False(r.Canvas.Enabled);r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);Assert.Null(r.Editor.Primary);Assert.Equal(camera,r.View.Camera);Assert.Empty(r.Editor.Session.Journal);
    }
    [Fact]
    public void Cycling_stays_in_the_current_focus_and_readonly_transform_policy_allows_view_navigation()
    {
        using var r=new Rig();Assert.True(r.Editor.FocusOn("root"));r.View.TransformRefusal=()=>"Geometry is read-only.";r.Host.Frames(r.Draw,3);r.Focus();r.Editor.Select();
        r.Key(ImGuiKey.Tab);Assert.Equal("root",r.Editor.Primary);r.Key(ImGuiKey.Tab);Assert.Equal("child",r.Editor.Primary);r.Key(ImGuiKey.Tab);Assert.Equal("root",r.Editor.Primary);
        r.Key(ImGuiKey.Enter);Assert.Equal("root",r.Editor.Primary);Assert.Equal(new Vector3(10,0,0),r.View.Camera.Target);
        var selected=r.Editor.Primary;r.Key(ImGuiKey.Tab,ctrl:true);Assert.Equal(selected,r.Editor.Primary);Assert.Empty(r.Editor.Session.Journal);Assert.False(r.Editor.Session.Dirty);
    }
    [Fact]
    public void Ctrl_Tab_still_switches_windows_while_bare_Tab_is_owned_by_the_viewport()
    {
        using var r=new Rig();r.Focus();r.Key(ImGuiKey.Tab);var selected=r.Editor.Primary;r.OtherPanel=true;r.Host.Frames(r.Draw,3);
        Assert.True(r.Canvas.Focused);r.Key(ImGuiKey.Tab,ctrl:true);r.Host.Frames(r.Draw,3);
        Assert.Equal(ImGui.Internal.FindWindowByName("Other panel"u8).ID.Value,ImGui.GetCurrentContext().NavWindow.ID.Value);
        Assert.Equal(selected,r.Editor.Primary);Assert.Empty(r.Editor.Session.Journal);
    }
    private sealed class Preview:IPlaySession{public string Status=>"Running.";public void Draw(){}public void Dispose(){}}
}
