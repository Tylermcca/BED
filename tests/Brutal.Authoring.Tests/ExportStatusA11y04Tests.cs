using System.Collections.Immutable;
using System.Diagnostics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

// Controlled completions isolate announcement transitions; real build/copy behavior is covered by EX15/probe journeys.
[Collection(ImGuiCollection.Name)]
public sealed class ExportStatusA11y04Tests
{
    private sealed class Sink:IAccessKitUpdateSink
    {
        public List<AccessKitProjectedUpdate> Updates {get;}=[];
        public bool TryApply(AccessKitProjectedUpdate update){Updates.Add(update);return true;}
    }
    private sealed class CopyAuthority:IVerifiedGameBuild
    {
        public string OutputDirectory=>Path.GetTempPath();
        public ManualResetEventSlim? Wait;public bool Fail;
        public bool CopyTo(string destination,out string reason,CancellationToken cancellation=default)
        {Wait?.Wait(cancellation);if(Fail)throw new IOException("Fixture disk failure; choose a writable destination.");reason="Build copied to "+destination;return true;}
    }
    private sealed class Operation:IGameBuildOperation
    {
        public readonly TaskCompletionSource<GameBuildReceipt> Done=new();
        public Task<GameBuildReceipt> Completion=>Done.Task;
        public GameContentManifest? OutputManifest {get;init;}
        public ImmutableArray<GameBuildLog> DrainLogs()=>[];
        public void Cancel(){}public void Dispose(){}
    }
    private sealed class ExportHost(GameContentManifest manifest):IGameExportHost
    {
        public Operation Operation=new(){OutputManifest=manifest};
        public TaskCompletionSource<GameExportSmokeResult> Checked=new();
        public readonly CopyAuthority Copy=new();
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)=>Operation;
        public Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec command,CancellationToken cancellation)=>Checked.Task;
    }
    private sealed class Rig:IDisposable
    {
        public readonly CapturedExportTests.Owned Owned=new();
        public readonly HeadlessHost Host=new(1000,800){Drawing=false};
        public readonly Sink Sink=new();private readonly AccessKitProjection projection=new();
        public readonly ExportHost Export;public readonly ExportBuildDialog Dialog;
        public Rig()
        {
            var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,false);
            Export=new(plan.Admission.Source);
            Dialog=new(Export,new(Path.Combine(Owned.Root,"fixture.exe"),Owned.Root,["{input}","{output}"]),
                new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,(folder,_)=>new(Path.Combine(folder,"fixture.exe"),folder,[])),
                Owned.Root,()=>saved,()=>false){Open=true};
            Frame(4);
        }
        public void Frame(int count=1)
        {
            for(var i=0;i<count;i++)
            {
                Host.Frame(Dialog.Draw);projection.Submit(Ui.Snapshot(),"Export proof",new(0,0,1000,800),1,1,Sink);
            }
        }
        public UiNode Status=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.StartsWith("Export status: ",StringComparison.Ordinal));
        public AccessKitProjectedNode[] Changes=>Sink.Updates.SelectMany(u=>u.ChangedNodes).Where(n=>n.Role==AccessKitProjectedRole.Status).ToArray();
        public void Once(string message,Action change)
        {
            var identity=Status.Id;var native=Changes[0].Id;var before=Changes.Length;
            change();Frame(4);
            Assert.Equal("Export status: "+message,Status.Label);Assert.Equal(identity,Status.Id);Assert.Equal(UiIdKind.Semantic,Status.IdKind);Assert.True(Status.TextEvidence?.WrapWidth>0);
            Assert.Equal(before+1,Changes.Length);Assert.Equal(native,Changes[^1].Id);Assert.Equal(Status.Label,Changes[^1].Label);Assert.True(Changes[^1].StableIdentity);
            Frame(6);Assert.Equal(before+1,Changes.Length);
        }
        public void Built(bool success=true,string reason="Built fixture")=>Export.Operation.Done.SetResult(new("fixture","fixture",Owned.Root,success?0:7,success,reason,[]));
        public void Ready()
        {
            Once("Building saved files...",()=>Assert.True(Dialog.Start()));
            Once("Checking the unpacked build...",()=>Built());
            Once("Build and unpacked smoke verified; ready to copy.",()=>Export.Checked.SetResult(new(Export.Copy,"Build and unpacked smoke verified; ready to copy.")));
            Assert.True(Dialog.CanCopy);
        }
        public void Act(string label,UiRole role,UiActions action,string? value=null)
        {
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==role&&n.Label==label);
            var ticket=Ui.RequestAction(node.Id,action,value);Frame(2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);
        }
        public void Until(Func<bool> complete)
        {var watch=Stopwatch.StartNew();while(!complete()&&watch.Elapsed<TimeSpan.FromSeconds(1)){Frame();Thread.Sleep(1);}Assert.True(complete(),Dialog.Status);}
        public void Dispose(){Dialog.Dispose();Owned.Dispose();Host.Dispose();}
    }
    [Fact]
    public void Build_check_ready_background_copy_and_copied_each_change_one_polite_status_and_ordinary_frames_stay_quiet()
    {
        using var r=new Rig();r.Ready();
        var destination=Path.Combine(r.Owned.Root,"copied");using var release=new ManualResetEventSlim();r.Export.Copy.Wait=release;
        r.Act("Destination folder",UiRole.TextField,UiActions.SetValue,destination);
        var before=r.Changes.Length;var id=r.Status.Id;
        r.Act("Copy build to folder...",UiRole.Button,UiActions.Invoke);
        Assert.Equal("Export status: Copying verified build...",r.Status.Label);Assert.Equal(id,r.Status.Id);
        Assert.Equal(before+1,r.Changes.Length);r.Frame(6);Assert.Equal(before+1,r.Changes.Length);
        release.Set();r.Until(()=>!r.Dialog.Busy);
        Assert.Equal("Export status: Build copied to "+destination,r.Status.Label);Assert.Equal(id,r.Status.Id);
        Assert.Equal(before+2,r.Changes.Length);r.Frame(6);Assert.Equal(before+2,r.Changes.Length);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.StartsWith("Export status: ",StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_build_or_failed_check_announces_the_plain_reason_once(bool checking)
    {
        using var r=new Rig();r.Once("Building saved files...",()=>Assert.True(r.Dialog.Start()));
        const string reason="Fixture failed: fix the saved level, then build again.";
        if(checking)
        {
            r.Once("Checking the unpacked build...",()=>r.Built());
            r.Once(reason,()=>r.Export.Checked.SetResult(new(null,reason)));
        }
        else r.Once(reason,()=>r.Built(false,reason));
        Assert.False(r.Dialog.CanCopy);Assert.False(r.Dialog.Busy);
    }
    [Fact]
    public void Copy_failure_announces_its_reason_once_and_keeps_the_verified_build_available()
    {
        using var r=new Rig();r.Ready();r.Export.Copy.Fail=true;
        r.Once("The build could not be copied: Fixture disk failure; choose a writable destination.",
            ()=>Assert.False(r.Dialog.Copy(Path.Combine(r.Owned.Root,"copy"))));
        Assert.True(r.Dialog.CanCopy);Assert.False(r.Dialog.Busy);
    }
    [Fact]
    public void Cancel_is_one_change_and_a_late_check_completion_cannot_repeat_or_replace_it()
    {
        using var r=new Rig();r.Once("Building saved files...",()=>Assert.True(r.Dialog.Start()));
        r.Once("Checking the unpacked build...",()=>r.Built());
        r.Once("Export canceled. Your edits are unchanged.",r.Dialog.Cancel);
        var count=r.Changes.Length;r.Export.Checked.SetResult(new(r.Export.Copy,"Late ready"));r.Frame(6);
        Assert.Equal(count,r.Changes.Length);Assert.False(r.Dialog.CanCopy);
        Assert.Equal("Export status: Export canceled. Your edits are unchanged.",r.Status.Label);
    }
}
