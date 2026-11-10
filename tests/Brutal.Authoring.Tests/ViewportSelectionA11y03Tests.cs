using System.Globalization;
using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Brutal.Authoring.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ViewportSelectionA11y03Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host;
        public readonly EditorModel<Scene> Editor;
        public readonly ViewportView<Scene> View;
        public Rig(bool parent=false)
        {
            var crate=SceneObject.At("named_crate",null,new(1,2,3));crate=crate with{Properties=crate.Properties.SetItem("name",System.Text.Json.JsonSerializer.SerializeToElement("Survey crate")),Parent=parent?"parent":null};
            var scene=new Scene(parent?[crate,SceneObject.At("other",null,new(4,5,6)),SceneObject.At("parent",null,new(10,0,0)) with{Scale=new(2)}]:[crate,SceneObject.At("other",null,new(4,5,6))]);
            Editor=new(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
            View=new(Editor,new SceneViewport()){Camera=new(new Vector3(1,2,3),0,0,10)};Host=new(1000,800){Drawing=false};
            try{EditorWindow<Scene>.Configure(ImGui.GetIO());Host.Frames(Draw,4);}catch{Host.Dispose();throw;}
        }
        public void Draw(){ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(900,700));Ui.Begin("View"u8);ImGui.SetKeyboardFocusHere();View.Draw();Ui.End();}
        public void Key(ImGuiKey key,bool ctrl=false){Host.Press(Draw,key,ctrl:ctrl);Host.Frames(Draw,2);}
        public UiNode Canvas=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas);
        public UiNode Status=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&(n.Label.StartsWith("Selected:",StringComparison.Ordinal)||n.Label.StartsWith("No object",StringComparison.Ordinal)||n.Label.Contains("objects selected.",StringComparison.Ordinal)));
        public void Dispose()=>Host.Dispose();
    }
    [Fact]
    public void Keyboard_selection_and_arrow_moves_expose_name_and_world_position_with_a_stable_canvas_status()
    {
        using var r=new Rig();Assert.Equal("No object selected.",r.Canvas.Value);var canvasId=r.Canvas.Id;var statusId=r.Status.Id;
        r.Key(ImGuiKey.Enter);Assert.Equal("named_crate",r.Editor.Primary);Assert.Contains("Survey crate",r.Canvas.Value);Assert.Contains("X 1, Y 2, Z 3 metres",r.Canvas.Value);
        Assert.Equal(r.Canvas.Value,r.Status.Label);Assert.Equal(statusId,r.Status.Id);Assert.Equal(UiIdKind.Semantic,r.Status.IdKind);Assert.Equal(canvasId,r.Canvas.Id);
        var revision=r.Editor.Session.Revision;r.Key(ImGuiKey.RightArrow);
        Assert.Equal(new Vector3(1.5f,2,3),r.Editor.Session.Current.Find("named_crate")!.Position);Assert.Equal(revision+1,r.Editor.Session.Revision);
        Assert.Equal("Selected: Survey crate. World position: X 1.5, Y 2, Z 3 metres.",r.Status.Label);Assert.Equal(statusId,r.Status.Id);
        var summary=r.Status.Label;r.Host.Frames(r.Draw,5);Assert.Equal(summary,r.Status.Label);Assert.Equal(statusId,r.Status.Id);
        r.Key(ImGuiKey.RightArrow,ctrl:true);Assert.Equal(summary,r.Status.Label);Assert.Equal(revision+1,r.Editor.Session.Revision);
        r.Key(ImGuiKey.Enter);Assert.Contains("Selected: other.",r.Status.Label);Assert.Contains("X 4, Y 5, Z 6",r.Status.Label);
        r.Editor.Select();r.Host.Frames(r.Draw,2);Assert.Equal("No object selected.",r.Status.Label);Assert.Equal(statusId,r.Status.Id);
    }
    [Fact]
    public void Selection_status_is_native_polite_atomic_while_canvas_value_is_not_a_second_live_region()
    {
        var api=new AccessKitNativeTreeApi();var status=api.NewNode(AccessKitTreeEncoder.NativeRole(AccessKitProjectedRole.Status));
        try{var live=NodeLive(status);Assert.True(live.HasValue);Assert.Equal((byte)1,live.Value);Assert.True(IsAtomic(status));}
        finally{api.FreeNode(status);}
        var canvas=api.NewNode(AccessKitTreeEncoder.NativeRole(AccessKitProjectedRole.Canvas));
        try{Assert.False(NodeLive(canvas).HasValue);Assert.False(IsAtomic(canvas));}
        finally{api.FreeNode(canvas);}
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct OptionalLive{[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]public bool HasValue;public byte Value;}
    [System.Runtime.InteropServices.DllImport("accesskit.dll",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl,EntryPoint="accesskit_node_live",ExactSpelling=true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.ApplicationDirectory|System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern OptionalLive NodeLive(nint node);
    [System.Runtime.InteropServices.DllImport("accesskit.dll",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl,EntryPoint="accesskit_node_is_live_atomic",ExactSpelling=true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.ApplicationDirectory|System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]private static extern bool IsAtomic(nint node);
    private sealed class Sink:IAccessKitUpdateSink
    {
        public readonly List<AccessKitProjectedUpdate> Updates=[];
        public bool TryApply(AccessKitProjectedUpdate update)
        {
            // Exercise the actual pinned native encoder without attaching a window or screen reader.
            using var encoded=AccessKitTreeEncoder.Encode(update,new AccessKitNativeTreeApi());Assert.True(encoded.IsOwned);
            Updates.Add(update);return true;
        }
    }
    [Fact]
    public void Real_viewport_projects_to_native_Canvas_and_announces_each_changed_position_once_only()
    {
        using var r=new Rig();var sink=new Sink();var projection=new AccessKitProjection();var statusId=r.Status.Id;var canvasId=r.Canvas.Id;
        void Frame(int count=1)
        {
            for(var i=0;i<count;i++){r.Host.Frame(r.Draw);projection.Submit(Ui.Snapshot(),"Viewport status fixture",new(0,0,1000,800),1,1,sink);}
        }
        void Press(ImGuiKey key){var io=ImGui.GetIO();io.AddKeyEvent(key,true);Frame();io.AddKeyEvent(key,false);Frame(3);}
        int Announcements()=>sink.Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.SourceId==statusId);
        Frame();Assert.Equal(1,Announcements());var canvas=Assert.Single(sink.Updates[0].ChangedNodes,n=>n.SourceId==canvasId);
        Assert.Equal(AccessKitProjectedRole.Canvas,canvas.Role);Assert.Equal((byte)51,AccessKitTreeEncoder.NativeRole(canvas.Role));Assert.Equal("Viewport",canvas.Label);Assert.Equal("No object selected.",canvas.Value);
        Press(ImGuiKey.Enter);Assert.Equal(2,Announcements());Assert.Equal("named_crate",r.Editor.Primary);
        Frame(5);Assert.Equal(2,Announcements());
        Press(ImGuiKey.RightArrow);Assert.Equal(3,Announcements());Assert.Contains("X 1.5, Y 2, Z 3",r.Status.Label);
        Frame(5);Assert.Equal(3,Announcements());
        Press(ImGuiKey.RightArrow);Assert.Equal(4,Announcements());Assert.Contains("X 2, Y 2, Z 3",r.Status.Label);
        Frame(5);Assert.Equal(4,Announcements());
        var statuses=sink.Updates.SelectMany(u=>u.ChangedNodes).Where(n=>n.SourceId==statusId).ToArray();
        Assert.Single(statuses.Select(n=>n.Id).Distinct());Assert.All(statuses,n=>{Assert.Equal(AccessKitProjectedRole.Status,n.Role);Assert.True(n.StableIdentity);});
        var current=sink.Updates.SelectMany(u=>u.ChangedNodes).Last(n=>n.SourceId==canvasId);Assert.Equal(r.Status.Label,current.Value);
    }
    [Fact]
    public void Parent_transform_and_multiple_selection_report_the_primary_world_position_with_invariant_numbers()
    {
        using var r=new Rig(parent:true);var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            Assert.True(r.Editor.Session.Execute(new MoveObject("named_crate",new(1.25f,2.5f,-3.75f))));r.Editor.Select("named_crate","other");r.Host.Frames(r.Draw,2);
            Assert.Contains("2 objects selected. Primary:",r.Status.Label);Assert.Equal(r.Canvas.Value,r.Status.Label);Assert.Contains("metres.",r.Status.Label);
            r.Editor.Select("named_crate");r.Host.Frames(r.Draw,2);Assert.Contains("X 12.5, Y 5, Z -7.5",r.Status.Label);
            Assert.True(r.Editor.Session.Undo());r.Host.Frames(r.Draw,2);Assert.Contains("X 12, Y 4, Z 6",r.Status.Label);
        }
        finally{CultureInfo.CurrentCulture=previous;}
    }
}
