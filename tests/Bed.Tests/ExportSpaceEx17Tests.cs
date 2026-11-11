using System.Collections.Immutable;
using System.Reflection;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ExportSpaceEx17Tests
{
    private sealed class CopyHandle:IVerifiedGameBuild
    {
        public string OutputDirectory=>Path.GetTempPath();public int Calls;
        public bool CopyTo(string folder,out string reason,CancellationToken cancellation=default){Calls++;reason="Copied fixture";return true;}
    }
    private sealed class Operation(GameContentManifest manifest):IGameBuildOperation
    {
        public GameContentManifest OutputManifest=>manifest;
        public Task<GameBuildReceipt> Completion=>Task.FromResult(new GameBuildReceipt("fixture","fixture",Path.GetTempPath(),0,true,"Built fixture",[]));
        public ImmutableArray<GameBuildLog> DrainLogs()=>[];public void Cancel(){}public void Dispose(){}
    }
    private sealed class Host(GameContentManifest manifest):IGameExportHost
    {
        public readonly CopyHandle Copy=new();
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)=>new Operation(manifest);
        public Task<GameExportSmokeResult> Smoke(GameExportPlan p,GameBuildReceipt r,GameContentManifest m,GameLaunchSpec s,CancellationToken cancellation)=>Task.FromResult(new GameExportSmokeResult(Copy,"Ready to copy"));
    }
    private static (ExportBuildDialog Dialog,Host Host) Ready()
    {
        var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,false);var host=new Host(plan.Admission.Source);
        var declaration=new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,(root,_)=>new(Path.Combine(root,"fixture.exe"),root,[]));
        var dialog=new ExportBuildDialog(host,new(@"C:\fixture\builder.exe",@"C:\fixture",["{input}","{output}"]),declaration,Path.GetTempPath(),()=>saved,()=>false){Open=true};
        Assert.True(dialog.Start());dialog.Pump();Assert.True(dialog.CanCopy);return(dialog,host);
    }
    private static void Space(object target,Func<string,long?> available)=>target.GetType().GetProperty("AvailableSpace",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,available);
    private static UiNode CopyButton()=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Copy build to folder...");
    private static void Destination(HeadlessHost ui,ExportBuildDialog dialog,string folder)
    {
        var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Destination folder");
        var action=Ui.RequestAction(field.Id,UiActions.SetValue,folder);ui.Frames(dialog.Draw,2);Assert.Equal(UiActionOutcome.Applied,action.Outcome);
    }
    [Theory]
    [InlineData(500_000_000L,false)]
    [InlineData(500_000_001L,true)]
    public void Large_build_warning_precedes_copy_and_does_not_refuse_when_room_is_available(long size,bool warning)
    {
        using var ui=new HeadlessHost(1100,850){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Ready();using var dialog=rig.Dialog;Space(dialog,_=>size);
        // Test the presentation boundary without allocating an impossible >500 MB inventory;
        // real admission keeps its smaller existing byte budget unchanged.
        typeof(ExportBuildDialog).GetField("verifiedSummary",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(dialog,((int Files,long Bytes)?)(1,size));
        ui.Frames(dialog.Draw,3);var nodes=Ui.Snapshot().Nodes.ToArray();
        var index=Array.FindIndex(nodes,n=>n.Label=="Warning: this verified build is over 500 MB. Copying may take a while.");
        Assert.Equal(warning,index>=0);Assert.True(CopyButton().Enabled);Assert.Equal(0,rig.Host.Copy.Calls);
        if(warning)Assert.True(index<Array.FindIndex(nodes,n=>n.Label=="Copy build to folder..."));
        Assert.True(dialog.Copy(Path.Combine(Path.GetTempPath(),"large-fixture-copy")));Assert.Equal(1,rig.Host.Copy.Calls);
    }
    [Fact]
    public void Insufficient_room_is_plain_text_and_disables_real_UI_copy_and_host_copy()
    {
        using var ui=new HeadlessHost(1100,850){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Ready();using var dialog=rig.Dialog;Space(dialog,_=>0);ui.Frames(dialog.Draw,3);
        Destination(ui,dialog,Path.Combine(Path.GetTempPath(),"no-room-fixture"));
        var button=CopyButton();Assert.False(button.Enabled);Assert.Contains("Not enough room",button.DisabledReason);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.StartsWith("Not enough room on the destination drive:")&&n.Label.Contains("0 bytes are available."));
        ui.Click(dialog.Draw,button.Bounds.X+button.Bounds.Width/2,button.Bounds.Y+button.Bounds.Height/2);
        Assert.Equal(0,rig.Host.Copy.Calls);Assert.False(dialog.Copy(Path.Combine(Path.GetTempPath(),"no-room-fixture")));
        Assert.Contains("Not enough room",dialog.Status);Assert.Equal(0,rig.Host.Copy.Calls);Assert.False(dialog.Busy);
    }
    [Fact]
    public void Fresh_copy_rechecks_space_even_when_the_display_cache_still_has_room()
    {
        using var ui=new HeadlessHost(1100,850){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Ready();using var dialog=rig.Dialog;long available=long.MaxValue;Space(dialog,_=>available);
        ui.Frames(dialog.Draw,3);Destination(ui,dialog,Path.Combine(Path.GetTempPath(),"changed-room-fixture"));Assert.True(CopyButton().Enabled);
        available=0;Assert.False(dialog.Copy(Path.Combine(Path.GetTempPath(),"changed-room-fixture")));
        Assert.Contains("Not enough room",dialog.Status);Assert.Equal(0,rig.Host.Copy.Calls);
    }
    [Fact]
    public void Unknown_space_is_reported_without_claiming_the_drive_is_full()
    {
        using var ui=new HeadlessHost(1100,850){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var rig=Ready();using var dialog=rig.Dialog;Space(dialog,_=>null);ui.Frames(dialog.Draw,3);
        Destination(ui,dialog,Path.Combine(Path.GetTempPath(),"unknown-room-fixture"));Assert.True(CopyButton().Enabled);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("free space could not be checked"));
        Assert.True(dialog.Copy(Path.Combine(Path.GetTempPath(),"unknown-room-fixture")));Assert.Equal(1,rig.Host.Copy.Calls);
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task Actual_verified_copy_rechecks_room_before_any_destination_or_staging_write(bool existingEmpty,bool losesRoom)
    {
        using var owned=new CapturedExportTests.Owned();var plan=CapturedExportTests.Plan(CapturedExportTests.Input(true),true);
        var runtime=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
        using var build=new ExportBuildHost(4096,64,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),owned.Root,false,plan);
        var receipt=await build.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);
        var smoke=await VerifiedGameBuild.SmokeAsync(plan,receipt,build.OutputManifest!,new(Path.Combine(receipt.OutputDirectory,"SceneStarter.exe"),receipt.OutputDirectory,["--project",".","--seconds","0"]),TimeSpan.FromSeconds(8));
        Assert.NotNull(smoke.Build);var calls=0;Space(smoke.Build,_=>losesRoom&&++calls==1?long.MaxValue:0);
        var target=Path.Combine(owned.Root,"destination");if(existingEmpty)Directory.CreateDirectory(target);
        var before=Directory.EnumerateFileSystemEntries(owned.Root).Order().ToArray();
        Assert.False(smoke.Build.CopyTo(target,out var reason));Assert.Contains("Not enough room",reason);
        Assert.Equal(existingEmpty,Directory.Exists(target));if(existingEmpty)Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.Equal(before,Directory.EnumerateFileSystemEntries(owned.Root).Order().ToArray());
        Assert.DoesNotContain(Directory.EnumerateDirectories(owned.Root),path=>Path.GetFileName(path).StartsWith(".bed-export-copy-"));
        if(losesRoom)Assert.Equal(2,calls);
        Space(smoke.Build,_=>long.MaxValue);Assert.True(smoke.Build.CopyTo(target,out reason),reason);
    }
    [Fact]
    public void Native_free_space_query_resolves_existing_ancestor_without_creating_the_destination()
    {
        using var owned=new CapturedExportTests.Owned();var target=Path.Combine(owned.Root,"absent","nested");
        var method=typeof(ExportBuildDialog).Assembly.GetType("Bed.ExportIO.ExportDiskSpace")!.GetMethod("AvailableBytes",BindingFlags.Static|BindingFlags.NonPublic)!;
        var bytes=Assert.IsType<long>(method.Invoke(null,[target]));Assert.InRange(bytes,0,long.MaxValue);Assert.False(Path.Exists(target));
        Assert.Null(method.Invoke(null,["relative-folder"]));
    }
}
