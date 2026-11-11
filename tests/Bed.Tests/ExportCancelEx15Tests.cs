using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Xunit.Abstractions;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ExportCancelEx15Tests(ITestOutputHelper output)
{
    private sealed class ObservedHost(IGameExportHost inner):IGameExportHost
    {
        public IGameBuildOperation? Build;
        public Task<GameExportSmokeResult>? Check;
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)=>Build=inner.Start(spec,root,dirty,plan);
        public Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec smoke,CancellationToken cancellation)
            =>Check=inner.Smoke(plan,receipt,outputs,smoke,cancellation);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keyboard_cancel_stops_a_real_slow_build_or_real_slow_check_within_one_second(bool checking)
    {
        using var owned=new CapturedExportTests.Owned();
        var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,checking);
        var runtime=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
        var helper=Path.Combine(AppContext.BaseDirectory,"LinkedBuildTestGame","LinkedBuildTestGame.exe");
        var spec=checking?new GameBuildSpec(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"])
            :new GameBuildSpec(helper,owned.Root,["hang","{output}","{input}"]);
        var declaration=new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,
            (folder,_)=>new(Path.Combine(folder,"SceneStarter.exe"),folder,["--project",".","--seconds","60"]));
        var observed=new ObservedHost(new ExportBuildHost(4096,64,TimeSpan.FromSeconds(90)));
        using var host=new HeadlessHost(1000,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        using var dialog=new ExportBuildDialog(observed,spec,declaration,owned.Root,()=>saved,()=>false){Open=true};
        host.Frames(dialog.Draw,3);host.Press(dialog.Draw,ImGuiKey.Enter);
        Until(()=>checking?observed.Check is not null:Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Text&&n.Label=="Synthetic build: hang"),TimeSpan.FromSeconds(10));
        Assert.True(dialog.Busy);Assert.False(dialog.CanCopy);Assert.NotNull(observed.Build);
        if(checking){Assert.False(observed.Check!.IsCompleted);Assert.True(observed.Build.Completion.IsCompletedSuccessfully);}
        else Assert.False(observed.Build.Completion.IsCompleted);
        var elapsed=Stopwatch.StartNew();host.Press(dialog.Draw,ImGuiKey.Escape);
        Until(()=>!dialog.Busy,TimeSpan.FromSeconds(1));elapsed.Stop();
        Assert.InRange(elapsed.Elapsed.TotalMilliseconds,0,999);output.WriteLine((checking?"check":"build")+" cancellation ms: "+elapsed.Elapsed.TotalMilliseconds);
        Assert.False(dialog.CanCopy);Assert.True(dialog.Open);
        Assert.Equal("Export canceled. Your edits are unchanged.",dialog.Status);
        Assert.False(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Cancel export").Enabled);
        Assert.False(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Copy build to folder...").Enabled);
        if(checking)Assert.Null((await observed.Check!).Build);else Assert.False((await observed.Build.Completion).Succeeded);
        var destination=Path.Combine(owned.Root,"must-not-exist");Assert.False(dialog.Copy(destination));Assert.False(Path.Exists(destination));
        void Until(Func<bool> done,TimeSpan limit)
        {
            var watch=Stopwatch.StartNew();while(!done()&&watch.Elapsed<limit){host.Frame(dialog.Draw);Thread.Sleep(1);}
            Assert.True(done(),dialog.Status+"; "+Ui.Snapshot().Text);
        }
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task Real_background_copy_cancels_quickly_or_rolls_back_IO_failure_without_a_partial_destination(bool existingEmpty,bool ioFailure)
    {
        using var owned=new CapturedExportTests.Owned();var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,true);
        var runtime=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
        var spec=new GameBuildSpec(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]);
        var declaration=new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,
            (folder,_)=>new(Path.Combine(folder,"SceneStarter.exe"),folder,["--project",".","--seconds","0"]));
        var observed=new ObservedHost(new ExportBuildHost(4096,64,TimeSpan.FromSeconds(30)));
        using var headless=new HeadlessHost(1000,800){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        using var dialog=new ExportBuildDialog(observed,spec,declaration,owned.Root,()=>saved,()=>false){Open=true};
        headless.Frames(dialog.Draw,3);headless.Press(dialog.Draw,ImGuiKey.Enter);Until(()=>dialog.CanCopy,TimeSpan.FromSeconds(10));
        var verified=Assert.IsType<VerifiedGameBuild>((await observed.Check!).Build);
        var before=Directory.GetFiles(verified.OutputDirectory,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>SHA256.HashData(File.ReadAllBytes(p)));
        var untouched=Path.Combine(owned.Root,"pre-canceled");
        Assert.False(verified.CopyTo(untouched,out var beforeReason,new CancellationToken(true)));Assert.Contains("canceled",beforeReason);Assert.False(Path.Exists(untouched));
        var destination=Path.Combine(owned.Root,"copied game");if(existingEmpty)Directory.CreateDirectory(destination);
        // A per-instance observer holds the actual first filesystem write until the real UI token is canceled.
        // All inventory, streams, bytes, worker task and rollback remain production operations.
        using var wrote=new ManualResetEventSlim();
        var token=((CancellationTokenSource)typeof(ExportBuildDialog).GetField("cancellation",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!).Token;
        typeof(VerifiedGameBuild).GetProperty("CopyProgress",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(verified,(Action<long>)(_=>
        {wrote.Set();if(ioFailure)throw new IOException("Synthetic copy disk failure.");token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));}));
        Act("Destination folder",UiRole.TextField,UiActions.SetValue,destination+Path.DirectorySeparatorChar);
        Act("Copy build to folder...",UiRole.Button,UiActions.Invoke);
        Until(()=>wrote.IsSet,TimeSpan.FromSeconds(3));
        if(!ioFailure)
        {
        Assert.True(dialog.Busy);Assert.Equal("Copying verified build...",dialog.Status);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Cancel export").Enabled);
        var stage=Assert.Single(Directory.GetDirectories(owned.Root,".bed-export-copy-*"));
        Assert.NotEmpty(Directory.GetFiles(stage,"*",SearchOption.AllDirectories));
        if(existingEmpty)Assert.Empty(Directory.GetFileSystemEntries(destination));else Assert.False(Path.Exists(destination));
        var elapsed=Stopwatch.StartNew();headless.Press(dialog.Draw,ImGuiKey.Escape);Until(()=>!dialog.Busy,TimeSpan.FromSeconds(1));elapsed.Stop();
        output.WriteLine("copy cancellation ms (existing empty="+existingEmpty+"): "+elapsed.Elapsed.TotalMilliseconds);
        Assert.InRange(elapsed.Elapsed.TotalMilliseconds,0,999);Assert.True(token.IsCancellationRequested);
        Assert.False(dialog.CanCopy);Assert.Equal("Export canceled. Your edits are unchanged.",dialog.Status);
        }
        else
        {
            Until(()=>!dialog.Busy,TimeSpan.FromSeconds(1));Assert.True(dialog.CanCopy);
            Assert.Contains("Synthetic copy disk failure.",dialog.Status);
        }
        if(existingEmpty){Assert.True(Directory.Exists(destination));Assert.Empty(Directory.GetFileSystemEntries(destination));}else Assert.False(Path.Exists(destination));
        Assert.Empty(Directory.GetDirectories(owned.Root,".bed-export-copy-*"));
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.StartsWith("Copied to:",StringComparison.Ordinal));
        foreach(var (path,hash) in before)Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(before.Count,Directory.GetFiles(verified.OutputDirectory,"*",SearchOption.AllDirectories).Length);
        void Act(string label,UiRole role,UiActions action,string? value=null)
        {
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==role&&n.Label==label);Assert.True(node.Enabled,node.DisabledReason);
            var ticket=Ui.RequestAction(node.Id,action,value);headless.Frames(dialog.Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);
        }
        void Until(Func<bool> done,TimeSpan timeout)
        {
            var watch=Stopwatch.StartNew();while(!done()&&watch.Elapsed<timeout){headless.Frame(dialog.Draw);Thread.Sleep(1);}
            Assert.True(done(),dialog.Status+"; "+Ui.Snapshot().Text);
        }
    }

}
