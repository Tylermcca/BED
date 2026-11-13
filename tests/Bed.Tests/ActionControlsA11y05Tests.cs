using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ActionControlsA11y05Tests
{
    [Fact]
    public void Launcher_controls_expose_named_actions_and_Open_uses_the_existing_return_path()
    {
        var root=Directory.CreateTempSubdirectory("own-launch-actions-").FullName;
        try{
            using var host=new HeadlessHost(1200,850){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"recent.json")),projectsFolder:root);
            host.Frames(launcher.Draw,3);
            foreach(var label in new[]{"New project...","Open project...","Try the example","Getting started","Options","About BED"})
            {var n=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);Assert.True(n.Actions.HasFlag(UiActions.Invoke));Assert.False(n.Bounds.IsEmpty);}
            var open=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Open project...");
            var ticket=Ui.RequestAction(open.Id,UiActions.Invoke);host.Frames(launcher.Draw,3);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Project folder");
            Assert.Null(launcher.Workspace);
        }finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Native_button_metadata_has_Click_without_a_false_selection_property()
    {
        var button=new AccessKitProjectedNode(1,AccessKitProjectedRole.Button,"Save",null,null,new(0,0,80,25),false,false,null,null,true,[]){Actions=UiActions.Focus|UiActions.Invoke};
        var api=new AccessKitNativeTreeA457Tests.Fake();
        using var encoded=AccessKitTreeEncoder.Encode(new(false,0,0,[button],[]),api);
        Assert.Contains((byte)0,api.Actions);Assert.Equal(0,api.SelectionCalls);
    }
    [Fact]
    public void Stepped_numeric_SetValue_uses_the_existing_commit_facts_once()
    {
        using var host=new HeadlessHost(750,500){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var number=2d;var commits=0;var edited=false;var deactivated=false;
        void Draw(){ImGui.SetNextWindowSize(new float2(600,350));Ui.Begin("Own numeric"u8);if(Ui.InputDouble("Quantity"u8,ref number,1,10,"%g"u8,ImGuiInputTextFlags.EnterReturnsTrue))commits++;edited=ImGui.IsItemEdited();deactivated=ImGui.IsItemDeactivatedAfterEdit();Ui.End();}
        host.Frames(Draw,3);var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField);Assert.False(node.Bounds.IsEmpty);
        var ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"4.5");host.Frame(Draw);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.Equal(4.5,number);Assert.Equal(1,commits);Assert.True(edited);Assert.True(deactivated);
        host.Frames(Draw,2);Assert.Equal(1,commits);
        node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField);ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"NaN");host.Frames(Draw,2);Assert.Equal(UiActionOutcome.NotAvailable,ticket.Outcome);Assert.Equal(4.5,number);Assert.Equal(1,commits);
    }
    [Fact]
    public void Named_table_value_action_edits_saves_reopens_and_schema_refusal_keeps_the_saved_value()
    {
        using var r=new TableRig();var node=r.Caption();Assert.Contains(":",node.Label);
        var ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"Changed");r.Host.Frames(r.Workspace.Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.Equal("Changed",r.Value);Assert.True(r.Workspace.Active!.Dirty);Assert.Equal(1,r.Workspace.Active.Session!.History.UndoCount);
        Assert.True(r.Workspace.Window!.Run("editor.save"));Assert.False(r.Workspace.Active.Dirty);
        var reopened=DataProject.Open(r.Data,r.Schemas).Find("items.json")!;Assert.True(reopened.Open());Assert.Equal("Changed",reopened.Session!.Current.At("/items/0/caption")!.Value.GetString());
        var saved=File.ReadAllBytes(reopened.Path);node=r.Caption();ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"");r.Host.Frames(r.Workspace.Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.Equal("Changed",r.Value);Assert.False(r.Workspace.Active.Dirty);Assert.Equal(saved,File.ReadAllBytes(reopened.Path));Assert.NotEmpty(r.Workspace.TableOf("items.json")!.Said);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label==r.Workspace.TableOf("items.json")!.Said);
    }
    [Fact]
    public void Queued_named_table_action_refuses_a_changed_row_identity_even_if_its_value_is_the_same()
    {
        using var r=new TableRig();var node=r.Caption();var ticket=Ui.RequestAction(node.Id,UiActions.SetValue,"Changed");
        Assert.True(r.Workspace.Active!.Session!.Execute(new SetValue("/items/0/id",JsonSerializer.SerializeToElement("renamed"))));
        r.Host.Frames(r.Workspace.Draw,2);Assert.Equal(UiActionOutcome.NotAvailable,ticket.Outcome);Assert.Equal("Original",r.Value);Assert.Equal(1,r.Workspace.Active.Session.History.UndoCount);
    }
    [Fact]
    public void A_native_save_refusal_has_a_complete_stable_status_name()
    {
        using var r=new TableRig(readOnly:true);
        Assert.False(r.Workspace.Window!.Run("editor.save"));
        var message=r.Workspace.Window.Status;Assert.NotEmpty(message);
        r.Host.Frames(r.Workspace.Draw,2);
        var status=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label==message);
        Assert.Equal(UiIdKind.Semantic,status.IdKind);
        r.Host.Frames(r.Workspace.Draw,3);
        Assert.Equal(status.Id,Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label==message).Id);
    }
    private sealed class TableRig:IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("own-table-actions-").FullName;
        public readonly HeadlessHost Host=new(1500,1100){Drawing=false};public readonly DataWorkspace Workspace;
        public string Data=>Path.Combine(root,"data");public string Schemas=>Path.Combine(root,"schemas");
        public string Value=>Workspace.Active!.Session!.Current.At("/items/0/caption")!.Value.GetString()!;
        public TableRig(bool readOnly=false)
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            File.WriteAllText(Path.Combine(Data,"items.json"),"""{"items":[{"id":"alpha","caption":"Original"}]}""");
            File.WriteAllText(Path.Combine(Schemas,"items.schema.json"),"""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","required":["id","caption"],"properties":{"id":{"type":"string"},"caption":{"type":"string","minLength":3}}}}}}""");
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());Workspace=new(DataProject.Open(Data,Schemas,readOnly));Assert.True(Workspace.Show("items.json"));Workspace.TableOf("items.json")!.List="/items";Host.Frames(Workspace.Draw,3);
        }
        public UiNode Caption()=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Value==Value);
        public void Dispose(){Workspace.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
}
