using Bed.Editor.ImGui;
using Bed.Editor.App;
using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class NativeActionsA11y05Tests
{
    private sealed class Capture(AccessKitWindowBridge bridge):IAccessKitUpdateSink
    {
        public AccessKitProjectedUpdate Last=null!;
        public bool TryApply(AccessKitProjectedUpdate update){Last=update;return bridge.TryApply(update);}
    }
    private static void Request(Action draw,ulong source,AccessKitNativeActionKind action,string? value=null)
    {
        var trees=new AccessKitNativeTreeA457Tests.Fake();var windows=new AccessKitWindowA457Tests.WindowFake(trees);
        using var bridge=new AccessKitWindowBridge(42,"Own action client",windows,trees);var capture=new Capture(bridge);
        new AccessKitProjection().Submit(Ui.Snapshot(),"Own action client",new(0,0,1500,1100),1,1,capture);bridge.RaisePendingEvents();windows.Activate();
        var node=Assert.Single(capture.Last.ChangedNodes,n=>n.SourceId==source);
        Assert.True(node.StableIdentity);Assert.False(node.Disabled);
        windows.Receive!(new(node.Id,action,value));bridge.DrainRequests();Assert.Equal("pending",bridge.LastActionOutcome);
        draw(); // The caller supplies a real frame; no direct command/session mutation from this client.
        bridge.DrainRequests();Assert.Equal("applied",bridge.LastActionOutcome);
    }
    [Fact]
    public void Projected_native_Click_opens_the_launcher_page_once_and_recent_names_survive_filtering()
    {
        var root=Directory.CreateTempSubdirectory("own-native-launch-").FullName;
        try{
            using var host=new HeadlessHost(1500,1100){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            var recent=new RecentProjects(Path.Combine(root,"recent.json"));
            foreach(var name in new[]{"first","second"}){var home=Directory.CreateDirectory(Path.Combine(root,name)).FullName;var data=Directory.CreateDirectory(Path.Combine(home,"data")).FullName;var schema=Directory.CreateDirectory(Path.Combine(home,"schemas")).FullName;recent.Add(new(data,schema));}
            using var launcher=new ProjectLauncher(recent,projectsFolder:root);host.Frames(launcher.Draw,3);
            var row=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="second");Assert.Equal(UiIdKind.ImGui,row.IdKind);Assert.True(row.Actions.HasFlag(UiActions.Select));
            Request(()=>host.Frame(launcher.Draw),row.Id,AccessKitNativeActionKind.Click);host.Frames(launcher.Draw,2);
            Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Id==row.Id).Selected);Assert.Null(launcher.Workspace);
            var search=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Search recent projects");
            Request(()=>host.Frame(launcher.Draw),search.Id,AccessKitNativeActionKind.SetValue,"second");host.Frames(launcher.Draw,2);
            Assert.Equal(row.Id,Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem).Id);
            var open=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Open project...");Request(()=>host.Frame(launcher.Draw),open.Id,AccessKitNativeActionKind.Click);host.Frames(launcher.Draw,2);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Project folder");Assert.Null(launcher.Workspace);
        }finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("readonly")]
    [InlineData("disabled")]
    [InlineData("stale")]
    public void Stepped_value_action_retains_readonly_disabled_and_stale_guards(string change)
    {
        using var host=new HeadlessHost(750,500){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());var number=2d;var readOnly=false;var disabled=false;var commits=0;
        void Draw(){ImGui.SetNextWindowSize(new float2(600,350));Ui.Begin("Own stepped guards"u8);Ui.BeginDisabled(disabled,"Own disabled reason");if(Ui.InputDouble("Quantity"u8,ref number,1,10,"%g"u8,readOnly?ImGuiInputTextFlags.ReadOnly:0))commits++;Ui.EndDisabled();Ui.End();}
        host.Frames(Draw,3);var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField);Assert.False(node.Bounds.IsEmpty);var ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"5");
        if(change=="readonly")readOnly=true;else if(change=="disabled")disabled=true;else number=3;
        host.Frames(Draw,2);Assert.Equal(UiActionOutcome.NotAvailable,ticket.Outcome);Assert.Equal(change=="stale"?3:2,number);Assert.Equal(0,commits);
    }
}
