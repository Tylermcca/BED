using System.Collections.Immutable;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ExportDialogTests
{
    private sealed class CopyHandle:IVerifiedGameBuild
    {public string OutputDirectory=>Path.GetTempPath();public int Calls;public string? Folder;public bool Throw;public bool CopyTo(string folder,out string reason,CancellationToken cancellation=default){Calls++;Folder=folder;if(Throw)throw new IOException("Access is denied.");reason="Copied fixture";return true;}}
    private sealed class Operation:IGameBuildOperation
    {
        public readonly TaskCompletionSource<GameBuildReceipt> Done=new();public Task<GameBuildReceipt> Completion=>Done.Task;
        public GameContentManifest? OutputManifest{get;set;} public bool Canceled;public ImmutableArray<GameBuildLog> DrainLogs()=>[];public void Cancel()=>Canceled=true;public void Dispose(){}
    }
    private sealed class Host:IGameExportHost
    {
        public readonly Operation Operation=new();public readonly TaskCompletionSource<GameExportSmokeResult> Checked=new();public readonly CopyHandle Copy=new();public int Starts;public CancellationToken Token;
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan){Starts++;Operation.OutputManifest=plan.Admission.Source;return Operation;}
        public Task<GameExportSmokeResult> Smoke(GameExportPlan p,GameBuildReceipt r,GameContentManifest m,GameLaunchSpec s,CancellationToken cancellation){Token=cancellation;return Checked.Task;}
    }
    private static (ExportBuildDialog Dialog,Host Host) Make(bool dirty=false)
    {
        var host=new Host();var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,false);
        var declaration=new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,(root,_)=>new(Path.Combine(root,"sample.exe"),root,[]));
        return(new(host,new(@"C:\fixture\builder.exe",@"C:\fixture",["{input}","{output}"]),declaration,Path.GetTempPath(),()=>saved,()=>dirty),host);
    }
    private static GameContentManifest SummaryFixture()
    {
        // Independent, source-owned sizes: 2 + 3 + 7 = 12 bytes across three files.
        var contents=new[]{new byte[2],new byte[3],new byte[7]};
        var mappings=contents.Select((_,i)=>new GameContentMapping("file-"+i,$"file-{i}.bin","fixture")).ToArray();
        var profile=GameBuildProfile.Create("summary","Fixture.csproj",GameBuildTarget.WindowsX64,mappings);
        var nodes=contents.Select((bytes,i)=>new GameContentNode(mappings[i],bytes,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),[],true,ExportSource.GameOwned));
        return GameContentManifest.Create(profile,"self-authored","summary-v1",true,nodes);
    }
    [Fact]
    public void Summary_appears_only_after_verification_and_successful_copy_names_the_actual_destination()
    {
        using var host=new HeadlessHost(1000,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Make();using var dialog=rig.Dialog;dialog.Open=true;Assert.True(dialog.Start());
        rig.Host.Operation.OutputManifest=SummaryFixture();host.Frames(dialog.Draw,3);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Verified build:",StringComparison.Ordinal));
        FinishBuild(rig.Host);host.Frames(dialog.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Verified build:",StringComparison.Ordinal));
        rig.Host.Checked.SetResult(new(rig.Host.Copy,"Ready to copy"));host.Frames(dialog.Draw,3);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Verified build: 3 files, 12 bytes.");
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Copied to:",StringComparison.Ordinal));
        var destination=Path.Combine(Path.GetTempPath(),"self-authored-summary-destination");Assert.True(dialog.Copy(destination));host.Frames(dialog.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Copied to: "+Path.GetFullPath(destination));
        Assert.Equal(destination,rig.Host.Copy.Folder);Assert.Equal(1,rig.Host.Copy.Calls);
        dialog.Cancel();host.Frames(dialog.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Copied to:",StringComparison.Ordinal)||n.Label.StartsWith("Verified build:",StringComparison.Ordinal));
    }
    [Fact]
    public void Failed_copy_does_not_claim_a_destination_and_late_canceled_smoke_does_not_publish_a_summary()
    {
        using var host=new HeadlessHost(1000,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Make();using var dialog=rig.Dialog;dialog.Open=true;Assert.True(dialog.Start());rig.Host.Operation.OutputManifest=SummaryFixture();
        FinishBuild(rig.Host);dialog.Pump();rig.Host.Checked.SetResult(new(rig.Host.Copy,"Ready to copy"));host.Frames(dialog.Draw,3);
        rig.Host.Copy.Throw=true;Assert.False(dialog.Copy("failed-folder"));host.Frames(dialog.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Verified build: 3 files, 12 bytes.");
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Copied to:",StringComparison.Ordinal));
        var canceled=Make();using var late=canceled.Dialog;late.Open=true;Assert.True(late.Start());canceled.Host.Operation.OutputManifest=SummaryFixture();
        FinishBuild(canceled.Host);late.Pump();late.Cancel();canceled.Host.Checked.SetResult(new(canceled.Host.Copy,"Late success"));host.Frames(late.Draw,3);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Verified build:",StringComparison.Ordinal));Assert.False(late.CanCopy);
    }
    private static void FinishBuild(Host host)=>host.Operation.Done.SetResult(new("fixture","fixture",Path.GetTempPath(),0,true,"Built fixture",[]));
    [Fact] public void Copy_stays_unavailable_until_build_and_actual_host_smoke_complete()
    {
        var rig=Make();using var dialog=rig.Dialog;Assert.False(dialog.CanCopy);Assert.False(dialog.Copy("unused"));Assert.True(dialog.Start());Assert.True(dialog.Busy);Assert.False(dialog.CanCopy);
        FinishBuild(rig.Host);dialog.Pump();Assert.False(dialog.CanCopy);Assert.True(dialog.Busy);
        rig.Host.Checked.SetResult(new(rig.Host.Copy,"Ready to copy"));dialog.Pump();Assert.True(dialog.CanCopy);Assert.False(dialog.Busy);Assert.True(dialog.Copy("chosen-folder"));Assert.Equal(1,rig.Host.Copy.Calls);
    }
    [Fact] public void Cancellation_cannot_enable_copy_from_a_late_smoke_result()
    {
        var rig=Make();using var dialog=rig.Dialog;Assert.True(dialog.Start());FinishBuild(rig.Host);dialog.Pump();dialog.Cancel();Assert.True(rig.Host.Token.IsCancellationRequested);
        rig.Host.Checked.SetResult(new(rig.Host.Copy,"Late success"));dialog.Pump();Assert.False(dialog.CanCopy);Assert.False(dialog.Copy("unused"));Assert.Equal(0,rig.Host.Copy.Calls);
    }
    [Fact] public void Dirty_files_refuse_without_calling_build_host()
    {var rig=Make(true);using var dialog=rig.Dialog;Assert.False(dialog.Start());Assert.Equal(0,rig.Host.Starts);Assert.Contains("Save changes",dialog.Status);Assert.False(dialog.CanCopy);dialog.Dispose();dialog.Dispose();}
    [Fact] public void Disabled_copy_control_keeps_its_real_mouse_click_inert()
    {
        using var host=new HeadlessHost(900,700){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());var rig=Make();using var dialog=rig.Dialog;dialog.Open=true;host.Frames(dialog.Draw,3);
        var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Copy build to folder...");var before=dialog.Status;
        host.Click(dialog.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);host.Frames(dialog.Draw,2);
        Assert.False(dialog.CanCopy);Assert.Equal(before,dialog.Status);Assert.Equal(0,rig.Host.Copy.Calls);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Cancel export");
    }
    [Fact] public void Real_workspace_file_menu_opens_export_without_launching_or_changing_saved_scene()
    {
        var root=Directory.CreateTempSubdirectory("export-menu-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            const string text="""{"kind":"scene","version":1,"objects":[{"id":"root"}]}""";var path=Path.Combine(data,"level.json");File.WriteAllText(path,text);
            using var host=new HeadlessHost(1100,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            var project=DataProject.Open(data,schemas);Assert.True(DocumentHost.For(project).BindScene("level.json","scene",out var why),why);
            using var workspace=new DataWorkspace(project);var bridge=new Host();var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,false);
            var declaration=new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,(folder,_)=>new(Path.Combine(folder,"sample.exe"),folder,[]));
            workspace.BindExport(new(@"C:\fixture\builder.exe",root,["{input}","{output}"]),declaration,bridge);Assert.True(workspace.Show("level.json"));host.Frames(workspace.Draw,3);
            void Click(string label){var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label);host.Click(workspace.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);host.Frames(workspace.Draw,2);}
            Click("File");Click("Export build...");host.Frames(workspace.Draw,2);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Build saved files");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Copy build to folder...");
            Assert.Equal(0,bridge.Starts);Assert.Equal(text,File.ReadAllText(path));Assert.False(Directory.Exists(Path.Combine(data,".bed")));Assert.Empty(project.Dirty);
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }

    private static void WaitCopy(HeadlessHost host,ExportBuildDialog dialog)
    {
        var deadline=System.Diagnostics.Stopwatch.StartNew();
        while(dialog.Busy&&deadline.Elapsed<TimeSpan.FromSeconds(1)){host.Frame(dialog.Draw);Thread.Sleep(1);}
        host.Frames(dialog.Draw,2);Assert.False(dialog.Busy);
    }

    private static UiNode Node(string label) => Assert.Single(Ui.Snapshot().Nodes, n => n.Label == label);
    private static void Action(HeadlessHost host, ExportBuildDialog dialog, string label, UiActions action, string? value = null)
    {
        var ticket = Ui.RequestAction(Node(label).Id, action, value);
        host.Frames(dialog.Draw, 2); Assert.Equal(UiActionOutcome.Applied, ticket.Outcome);
    }

    [Fact]
    public void Keyboard_focus_starts_on_build_Tab_reaches_destination_and_close_and_Escape_closes()
    {
        using var host = new HeadlessHost(900,700) { Drawing=false }; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig = Make(); using var dialog = rig.Dialog; dialog.Open=true; host.Frames(dialog.Draw,3);
        Assert.True(Node("Build saved files").Focused);
        Assert.False(Node("Cancel export").Enabled); Assert.False(Node("Copy build to folder...").Enabled);
        Assert.Equal("Build and verify the unpacked result before copying.", Node("Copy build to folder...").DisabledReason);
        Assert.Equal(UiRole.TextField, Node("Destination folder").Role);
        Assert.Equal(UiActions.SetValue|UiActions.Focus, Node("Destination folder").Actions);
        host.Press(dialog.Draw,ImGuiKey.Tab); Assert.True(Node("Destination folder").Focused);
        host.Press(dialog.Draw,ImGuiKey.Tab); Assert.True(Node("Close").Focused);
        host.Press(dialog.Draw,ImGuiKey.Tab,shift:true); Assert.True(Node("Destination folder").Focused);
        host.Press(dialog.Draw,ImGuiKey.Escape); Assert.False(dialog.Open); Assert.Equal(0,rig.Host.Starts);
    }

    [Fact]
    public void Keyboard_build_moves_to_cancel_and_keyboard_cancel_cannot_enable_copy()
    {
        using var host = new HeadlessHost(900,700) { Drawing=false }; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Make(); using var dialog=rig.Dialog; dialog.Open=true; host.Frames(dialog.Draw,3);
        host.Press(dialog.Draw,ImGuiKey.Enter); host.Frames(dialog.Draw,2);
        Assert.Equal(1,rig.Host.Starts); Assert.True(dialog.Busy); Assert.True(Node("Cancel export").Focused);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Export status: Building saved files...");
        Assert.False(Node("Build saved files").Enabled);
        host.Press(dialog.Draw,ImGuiKey.Space); host.Frames(dialog.Draw,2);
        Assert.True(rig.Host.Operation.Canceled); Assert.False(dialog.CanCopy); Assert.True(Node("Close").Focused);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Export status: Export canceled. Your edits are unchanged.");
        Assert.Equal(UiActionOutcome.NotAvailable,Ui.RequestAction(Node("Copy build to folder...").Id,UiActions.Invoke).Outcome);
        host.Press(dialog.Draw,ImGuiKey.Escape); Assert.False(dialog.Open); Assert.Equal(0,rig.Host.Copy.Calls);
    }

    [Fact]
    public void Verified_build_focuses_labelled_destination_Tab_reaches_copy_and_Enter_copies_typed_folder()
    {
        using var host=new HeadlessHost(900,700){Drawing=false}; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Make(); using var dialog=rig.Dialog; dialog.Open=true; Assert.True(dialog.Start()); host.Frames(dialog.Draw,3);
        FinishBuild(rig.Host); host.Frames(dialog.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Export status: Checking the unpacked build...");
        Assert.True(Node("Cancel export").Enabled); Assert.False(Node("Copy build to folder...").Enabled);
        rig.Host.Checked.SetResult(new(rig.Host.Copy,"Ready to copy")); host.Frames(dialog.Draw,3);
        Assert.True(Node("Destination folder").Focused); Assert.True(Node("Copy build to folder...").Enabled);
        host.Type(dialog.Draw,"chosen-folder"); host.Frames(dialog.Draw,2);
        Assert.Equal("chosen-folder",Node("Destination folder").Value);
        host.Press(dialog.Draw,ImGuiKey.Tab); Assert.True(Node("Copy build to folder...").Focused);
        host.Press(dialog.Draw,ImGuiKey.Enter); WaitCopy(host,dialog);
        Assert.Equal(1,rig.Host.Copy.Calls); Assert.Equal("chosen-folder",rig.Host.Copy.Folder);
        Assert.True(Node("Close").Focused); Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Export status: Copied fixture");
    }

    [Fact]
    public void Tree_actions_report_empty_destination_and_copy_failure_in_plain_text_without_losing_verified_build()
    {
        using var host=new HeadlessHost(900,700){Drawing=false}; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Make(); using var dialog=rig.Dialog; dialog.Open=true; Assert.True(dialog.Start()); FinishBuild(rig.Host); dialog.Pump();
        rig.Host.Checked.SetResult(new(rig.Host.Copy,"Ready to copy")); host.Frames(dialog.Draw,3);
        Action(host,dialog,"Copy build to folder...",UiActions.Invoke);
        Assert.Equal(0,rig.Host.Copy.Calls); Assert.True(dialog.CanCopy);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Export status: Choose a destination folder before copying.");
        Action(host,dialog,"Destination folder",UiActions.SetValue,"new-folder"); rig.Host.Copy.Throw=true;
        Action(host,dialog,"Copy build to folder...",UiActions.Invoke); WaitCopy(host,dialog);
        Assert.Equal(1,rig.Host.Copy.Calls); Assert.True(dialog.CanCopy);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Export status: The build could not be copied: Access is denied.");
        Assert.True(Node("Destination folder").Focused);
    }

    [Fact]
    public void Escape_cancels_smoke_and_a_late_cancellation_fault_preserves_the_plain_cancel_message()
    {
        using var host=new HeadlessHost(900,700){Drawing=false}; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Make(); using var dialog=rig.Dialog; dialog.Open=true; Assert.True(dialog.Start()); FinishBuild(rig.Host); host.Frames(dialog.Draw,3);
        host.Press(dialog.Draw,ImGuiKey.Escape); Assert.True(dialog.Open); Assert.True(rig.Host.Token.IsCancellationRequested);
        rig.Host.Checked.SetCanceled(); host.Frames(dialog.Draw,2);
        Assert.False(dialog.Busy); Assert.False(dialog.CanCopy);
        Assert.Equal("Export canceled. Your edits are unchanged.",dialog.Status);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Export status: Export canceled. Your edits are unchanged.");
    }

    private static void BindFolderHistory(ExportBuildDialog dialog,string project,string local)
        => typeof(ExportBuildDialog).GetMethod("BindDestinationPreference",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(dialog,[project,local]);
    private static void Ready(ExportBuildDialog dialog,Host host)
    {
        Assert.True(dialog.Start());FinishBuild(host);dialog.Pump();host.Checked.SetResult(new(host.Copy,"Ready to copy"));dialog.Pump();Assert.True(dialog.CanCopy);
    }

    [Fact]
    public void Export_folder_is_suggested_after_reopen_only_for_the_same_project_and_kept_out_of_project_files()
    {
        var root=Directory.CreateTempSubdirectory("export-history-").FullName;
        try
        {
            var local=Path.Combine(root,"user");Directory.CreateDirectory(local);var settings=Path.Combine(local,"settings.json");
            const string unrelated="{\"theme\":\"Dark\"}";File.WriteAllText(settings,unrelated);
            var project=Directory.CreateDirectory(Path.Combine(root,"project")).FullName;
            var folder=Path.Combine(root,"chosen build");
            var first=Make();using(var dialog=first.Dialog){BindFolderHistory(dialog,project,local);Ready(dialog,first.Host);Assert.True(dialog.Copy(folder));}
            Assert.Single(Directory.GetFiles(Path.Combine(local,"export-folders"),"*.json"));
            Assert.Empty(Directory.GetFileSystemEntries(project));Assert.Equal(unrelated,File.ReadAllText(settings));
            using var host=new HeadlessHost(900,700){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            var reopened=Make();using(var dialog=reopened.Dialog)
            {
                BindFolderHistory(dialog,project+Path.DirectorySeparatorChar,local);dialog.Open=true;host.Frames(dialog.Draw,3);
                Assert.Equal(folder,Node("Destination folder").Value);Assert.False(dialog.CanCopy);Assert.Equal(0,reopened.Host.Copy.Calls);
            }
            var other=Make();using(var dialog=other.Dialog)
            {
                BindFolderHistory(dialog,Path.Combine(root,"other-project"),local);dialog.Open=true;host.Frames(dialog.Draw,3);
                Assert.Equal("",Node("Destination folder").Value);
            }
            Assert.Empty(Directory.GetFileSystemEntries(project));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void Failed_copy_keeps_previous_successful_folder_and_unreadable_preferences_do_not_fail_a_copy()
    {
        var root=Directory.CreateTempSubdirectory("export-history-failure-").FullName;
        try
        {
            var project=Path.Combine(root,"project");var local=Path.Combine(root,"local");var good=Path.Combine(root,"good");
            var first=Make();using(var dialog=first.Dialog)
            {
                BindFolderHistory(dialog,project,local);Ready(dialog,first.Host);Assert.True(dialog.Copy(good));
                first.Host.Copy.Throw=true;Assert.False(dialog.Copy(Path.Combine(root,"failed")));
            }
            using var host=new HeadlessHost(900,700){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            var reopened=Make();using(var dialog=reopened.Dialog)
            {BindFolderHistory(dialog,project,local);dialog.Open=true;host.Frames(dialog.Draw,3);Assert.Equal(good,Node("Destination folder").Value);}
            var blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"a file blocks the optional settings directory");
            var unwritable=Make();using(var dialog=unwritable.Dialog)
            {BindFolderHistory(dialog,project,blocked);Ready(dialog,unwritable.Host);Assert.True(dialog.Copy(Path.Combine(root,"still-successful")));}
            Assert.Equal("a file blocks the optional settings directory",File.ReadAllText(blocked));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void Malformed_or_oversized_local_history_is_ignored_and_launcher_wires_its_own_preferences_folder()
    {
        var root=Directory.CreateTempSubdirectory("export-history-invalid-").FullName;
        try
        {
            var local=Path.Combine(root,"local");var project=Path.Combine(root,"project");
            var initial=Make();using(var dialog=initial.Dialog){BindFolderHistory(dialog,project,local);Ready(dialog,initial.Host);Assert.True(dialog.Copy(Path.Combine(root,"chosen")));}
            var file=Assert.Single(Directory.GetFiles(Path.Combine(local,"export-folders")));
            using var host=new HeadlessHost(900,700){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            foreach(var invalid in new[]{"broken",new string('x',20*1024),"\"relative-folder\""})
            {
                File.WriteAllText(file,invalid);var rig=Make();using var dialog=rig.Dialog;BindFolderHistory(dialog,project,local);dialog.Open=true;host.Frames(dialog.Draw,3);
                Assert.Equal("",Node("Destination folder").Value);Assert.False(dialog.CanCopy);
            }
            var method=typeof(ProjectLauncher).GetMethod("OpenWith",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            Assert.Contains(SceneFrameLifecycleTests.Calls(method),c=>c.Method.Name=="BindExportPreferences"&&c.Method.DeclaringType==typeof(DataWorkspace));
        }
        finally{Directory.Delete(root,true);}
    }
}
