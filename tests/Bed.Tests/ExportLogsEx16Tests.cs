using System.Diagnostics;
using System.Reflection;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ExportLogsEx16Tests
{
    private static ExportBuildHost Host(string local)=>(ExportBuildHost)Activator.CreateInstance(typeof(ExportBuildHost),BindingFlags.NonPublic|BindingFlags.Instance,null,[4096,64,TimeSpan.FromSeconds(30),local],null)!;
    private sealed class RecordingHost(IGameExportHost actual):IGameExportHost
    {
        public IGameBuildOperation? Operation;public readonly List<string> Opened=[];
        public IGameBuildOperation Start(GameBuildSpec spec,string project,bool dirty,GameExportPlan plan)=>Operation=actual.Start(spec,project,dirty,plan);
        public Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec command,CancellationToken token)=>actual.Smoke(plan,receipt,outputs,command,token);
        public bool OpenLogFolder(string path,out string reason){Opened.Add(path);reason="";return true;}
    }
    [Fact]
    public async Task Real_build_check_and_background_copy_share_one_external_log_and_keyboard_button_opens_only_its_folder_request()
    {
        using var owned=new CapturedExportTests.Owned();var project=Directory.CreateDirectory(Path.Combine(owned.Root,"project")).FullName;
        var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,true);
        var runtime=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");var bridge=new RecordingHost(Host(Path.Combine(owned.Root,"local")));
        var declaration=new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,(folder,_)=>new(Path.Combine(folder,"SceneStarter.exe"),folder,["--project",".","--seconds","0"]));
        using var headless=new HeadlessHost(1100,900){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        using var dialog=new ExportBuildDialog(bridge,new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),declaration,project,()=>saved,()=>false){Open=true};
        headless.Frames(dialog.Draw,3);Assert.False(Node("Open log folder").Enabled);
        Assert.Equal(UiActionOutcome.NotAvailable,Ui.RequestAction(Node("Open log folder").Id,UiActions.Invoke).Outcome);
        headless.Press(dialog.Draw,ImGuiKey.Enter);Until(()=>dialog.CanCopy);
        var log=bridge.Operation!.LogPath!;Assert.True(File.Exists(log));Assert.DoesNotContain(Path.GetFullPath(project),log,StringComparison.OrdinalIgnoreCase);
        var receipt=await bridge.Operation.Completion;Assert.True(receipt.Succeeded,receipt.Reason);
        Assert.Equal(Path.GetDirectoryName(receipt.OutputDirectory),Path.GetDirectoryName(log));
        Assert.DoesNotContain(receipt.Outputs,n=>n.Path=="export.log"||n.Path=="run.owner"||n.Path=="active.lock");
        var destination=Path.Combine(owned.Root,"copied");var ticket=Ui.RequestAction(Node("Destination folder").Id,UiActions.SetValue,destination);
        headless.Frames(dialog.Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);
        ticket=Ui.RequestAction(Node("Copy build to folder...").Id,UiActions.Invoke);headless.Frames(dialog.Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);
        Until(()=>!dialog.Busy);Assert.Equal("Build copied to "+destination,dialog.Status);
        var text=File.ReadAllText(log);Assert.Contains("[export] Build command:",text);Assert.Contains("[stdout]",text);
        Assert.Contains("Checking the unpacked build...",text);Assert.Contains("loaded saved level",text);Assert.Contains("Build and unpacked smoke verified",text);
        Assert.Contains("Copy started: "+destination,text);Assert.Contains("Copy file: data/level.json",text);Assert.Contains("Build copied to "+destination,text);
        Assert.False(File.Exists(Path.Combine(destination,"export.log")));Assert.False(Directory.Exists(Path.Combine(project,".bed","cache","export")));
        Assert.Empty(Directory.GetFileSystemEntries(project));
        for(var i=0;i<12&&!Node("Open log folder").Focused;i++)headless.Press(dialog.Draw,ImGuiKey.Tab);
        Assert.True(Node("Open log folder").Focused);headless.Press(dialog.Draw,ImGuiKey.Enter);headless.Frames(dialog.Draw,2);
        Assert.Equal(new[]{log},bridge.Opened); // The only folder opener is this recorder: no Explorer/native window.
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Export log: "+log);
        UiNode Node(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label||n.Role==UiRole.TextField&&n.Label==label);
        void Until(Func<bool> done)
        {var watch=Stopwatch.StartNew();while(!done()&&watch.Elapsed<TimeSpan.FromSeconds(10)){headless.Frame(dialog.Draw);Thread.Sleep(1);}Assert.True(done(),dialog.Status+"; "+Ui.Snapshot().Text);}
    }
    [Fact]
    public void Dirty_input_refuses_before_log_allocation_and_an_unowned_log_can_never_open_a_folder()
    {
        using var owned=new CapturedExportTests.Owned();var project=Directory.CreateDirectory(Path.Combine(owned.Root,"project")).FullName;
        var local=Path.Combine(owned.Root,"local");var owner=Host(local);var plan=CapturedExportTests.Plan(CapturedExportTests.Input(),false);
        var spec=new GameBuildSpec(Path.Combine(project,"not-started.exe"),project,["{input}","{output}"]);
        Assert.Throws<InvalidOperationException>(()=>owner.Start(spec,project,true,plan));Assert.False(Directory.Exists(local));
        var foreign=Path.Combine(owned.Root,"export.log");File.WriteAllText(foreign,"user content");
        Assert.False(owner.OpenLogFolder(foreign,out var reason));Assert.Equal("The export log folder is unavailable.",reason);
        Assert.Equal("user content",File.ReadAllText(foreign));Assert.False(Directory.Exists(local));
    }
    [Fact]
    public async Task Actual_failed_build_retains_stdout_and_terminal_reason_outside_the_unchanged_project()
    {
        using var owned=new CapturedExportTests.Owned();var project=Directory.CreateDirectory(Path.Combine(owned.Root,"project")).FullName;
        var plan=CapturedExportTests.Plan(CapturedExportTests.Input(),false);var helper=Path.Combine(AppContext.BaseDirectory,"LinkedBuildTestGame","LinkedBuildTestGame.exe");
        using var operation=Host(Path.Combine(owned.Root,"local")).Start(new(helper,project,["nonzero","{output}","{input}"]),project,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(8));Assert.False(receipt.Succeeded);Assert.Equal(23,receipt.ExitCode);
        var text=File.ReadAllText(operation.LogPath!);Assert.Contains("[stdout] Synthetic build: nonzero",text);Assert.Contains(receipt.Reason,text);
        Assert.Empty(Directory.GetFileSystemEntries(project));
    }
    [Fact]
    public void Three_run_retention_matches_EX10_key_and_never_removes_another_project_or_unowned_folder()
    {
        using var owned=new CapturedExportTests.Owned();var local=Path.Combine(owned.Root,"local");var project=Directory.CreateDirectory(Path.Combine(owned.Root,"project")).FullName;
        var type=typeof(ExportBuildHost).Assembly.GetType("Bed.Editor.App.ExportRunStore",true)!;
        var store=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[local],null)!;
        var begin=type.GetMethod("Begin",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var paths=new List<string>();
        for(var i=0;i<6;i++)
        {
            var log=begin.Invoke(store,[project+Path.DirectorySeparatorChar])!;
            using var lease=(IDisposable)log;paths.Add((string)log.GetType().GetProperty("Path",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(log)!);Thread.Sleep(5);
        }
        Assert.All(paths.Take(3),p=>Assert.False(File.Exists(p)));Assert.All(paths.Skip(3),p=>Assert.True(File.Exists(p)));
        var projectFolder=Path.GetDirectoryName(Path.GetDirectoryName(paths[^1]))!;
        var pref=typeof(ExportBuildDialog).Assembly.GetType("Bed.Editor.ImGui.ExportFolderPreference",true)!;
        var history=Activator.CreateInstance(pref,BindingFlags.Instance|BindingFlags.NonPublic,null,[project,local],null)!;
        var prefFile=(string)pref.GetField("file",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
        Assert.Equal(Path.GetFileNameWithoutExtension(prefFile),Path.GetFileName(projectFolder));
        var unowned=Directory.CreateDirectory(Path.Combine(projectFolder,new string('a',32))).FullName;File.WriteAllText(Path.Combine(unowned,"keep.txt"),"user-owned");
        var other=begin.Invoke(store,[Path.Combine(owned.Root,"other-project")])!;
        string otherLog=(string)other.GetType().GetProperty("Path",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(other)!;((IDisposable)other).Dispose();
        var last=begin.Invoke(store,[project])!;((IDisposable)last).Dispose();
        Assert.Equal("user-owned",File.ReadAllText(Path.Combine(unowned,"keep.txt")));Assert.True(File.Exists(otherLog));
        Assert.Equal(3,Directory.GetDirectories(projectFolder).Count(p=>File.Exists(Path.Combine(p,"export.log"))));
        var inside=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[Path.Combine(project,"bad-local")],null)!;
        var failure=Assert.Throws<TargetInvocationException>(()=>begin.Invoke(inside,[project]));Assert.IsType<IOException>(failure.InnerException);
        Assert.False(Directory.Exists(Path.Combine(project,"bad-local")));
    }
}
