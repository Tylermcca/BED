using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class Scene2ExportRuntimeTests
{
    private sealed class Operation:IGameBuildOperation
    {
        public Task<GameBuildReceipt> Completion {get;}=new TaskCompletionSource<GameBuildReceipt>().Task;
        public GameContentManifest? OutputManifest=>null;
        public ImmutableArray<GameBuildLog> DrainLogs()=>[];
        public void Cancel(){}public void Dispose(){}
    }
    private sealed class ExportHost:IGameExportHost
    {
        public readonly List<string> Inputs=[];
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)
        {Assert.False(dirty);Inputs.Add(Encoding.UTF8.GetString(plan.Admission.SavedInput.CopyPayload()));return new Operation();}
        public Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec smoke,CancellationToken token)=>throw new InvalidOperationException("No fake verification.");
    }
    private sealed class Rig:IDisposable
    {
        public readonly Scene2HostCommandTests.Rig Files=new();
        public readonly HeadlessHost Host=new(1400,1000){Drawing=false};
        public readonly DataWorkspace Workspace;
        public readonly ExportHost Export=new();
        public bool Current=true;
        public EditorWindow<Scene2> Window=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Workspace)!;
        public Rig()
        {
            Workspace=new(Files.Project);typeof(DataWorkspace).GetProperty("Scene2OwnerCurrent",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Workspace,(Func<DataWorkspace,bool>)(_=>Current));
            Assert.True(Workspace.Show("level.json"));
            Workspace.BindExport(new(Path.Combine(Files.Root,"builder.exe"),Files.Root,["{input}","{output}"]),
                new(input=>new("fixture",Encoding.UTF8.GetBytes(input.Text)),saved=>CapturedExportTests.Plan(saved,false),(root,_)=>new(Path.Combine(root,"game.exe"),root,[])),Export);
            EditorWindow<Scene2>.Configure(Brutal.ImGuiApi.ImGui.GetIO());Frames();
        }
        public void Frames()=>Host.Frames(Workspace.Draw,3);
        public void Edit(double value=2){if(!Window.Editor.Selection.Contains("owner"))Assert.True(Window.Editor.Select("owner"));Frames();Assert.True(Window.Inspector!.Set("position.0",value));Frames();}
        public void Open(){Assert.NotNull(Window.ExportBuildAction);Window.ExportBuildAction();Frames();}
        public void Choose(string label)
        {
            var snapshot=Ui.Snapshot();var dialog=Shown("Export saved files")?"Export saved files":"Export build";
            bool InDialog(UiNode node)
            {
                for(var parent=node.Parent;parent>=0;parent=snapshot.Nodes[parent].Parent)
                    if(snapshot.Nodes[parent].Role==UiRole.Window)return snapshot.Nodes[parent].Label==dialog;
                return false;
            }
            var node=Assert.Single(snapshot.Nodes,n=>n.Role==UiRole.Button&&n.Label==label&&InDialog(n));
            var ticket=Ui.RequestAction(node.Id,UiActions.Invoke);Frames();Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);
        }
        public bool Shown(string label)=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Window&&n.Label==label);
        public void Dispose(){Workspace.Dispose();Host.Dispose();Files.Dispose();}
    }
    [Theory][InlineData("Save")][InlineData("Don't Save")][InlineData("Cancel")]
    public void Native_v2_export_uses_its_saved_authority_and_own_save_decision(string choice)
    {
        using var r=new Rig();r.Edit();r.Open();Assert.True(r.Shown("Export saved files"));r.Choose(choice);
        if(choice=="Cancel"){Assert.Empty(r.Export.Inputs);Assert.True(r.Files.Entry.Session.Dirty);Assert.Equal(r.Files.Original,File.ReadAllText(r.Files.Disk));return;}
        Assert.True(r.Shown("Export build"));r.Choose("Build saved files");
        var input=Assert.Single(r.Export.Inputs);Assert.Equal(File.ReadAllText(r.Files.Disk),input);
        Assert.Equal(choice=="Don't Save",r.Files.Entry.Session.Dirty);Assert.Equal(choice=="Don't Save",input==r.Files.Original);
        Assert.Equal(1,r.Files.Entry.Session.History.UndoCount);Assert.Null(r.Files.Project.Find("level.json")!.Session);
    }
    [Fact]
    public void V2_save_refusal_and_stale_prompt_never_capture_a_build()
    {
        using var r=new Rig();r.Edit();r.Open();r.Files.Blocked=true;r.Choose("Save");
        Assert.True(r.Files.Entry.Session.Dirty);Assert.Empty(r.Export.Inputs);Assert.True(r.Shown("Export saved files"));
        r.Files.Blocked=false;r.Edit(3);r.Choose("Don't Save");Assert.False(r.Shown("Export build"));Assert.Empty(r.Export.Inputs);Assert.Contains("changed",r.Workspace.Said,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void V2_export_capture_refuses_outside_disk_changes()
    {
        using var r=new Rig();r.Open();Assert.True(r.Shown("Export build"));File.AppendAllText(r.Files.Disk," ");r.Choose("Build saved files");
        Assert.Empty(r.Export.Inputs);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("changed outside BED",StringComparison.Ordinal));
    }
    [Fact]
    public void Retained_old_v2_export_action_and_lost_workspace_owner_refuse()
    {
        using var r=new Rig();var old=r.Window.ExportBuildAction!;
        Assert.True(r.Files.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(r.Workspace.Show("level.json"));r.Frames();old();r.Frames();
        Assert.False(r.Shown("Export build"));Assert.Empty(r.Export.Inputs);Assert.Contains("no longer current",r.Workspace.Said);
        r.Current=false;r.Window.ExportBuildAction!();r.Frames();Assert.False(r.Shown("Export build"));Assert.Empty(r.Export.Inputs);Assert.Contains("no longer current",r.Workspace.Said);
    }
}
