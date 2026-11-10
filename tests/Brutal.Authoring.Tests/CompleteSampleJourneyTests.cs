using System.Diagnostics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram=Brutal.Authoring.Launcher.Program;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class CompleteSampleJourneyTests
{
    // Observe the real supervisor and smoke; return their exact operations and copy authority unchanged.
    private sealed class ObservedExport(IGameExportHost inner):IGameExportHost
    {
        public GameExportPlan? Plan;
        public IGameBuildOperation? Operation;
        public GameBuildReceipt? Receipt;
        public GameExportSmokeResult? SmokeResult;
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)
        {Plan=plan;return Operation=inner.Start(spec,root,dirty,plan);}
        public async Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec command,CancellationToken cancellation)
        {Receipt=receipt;return SmokeResult=await inner.Smoke(plan,receipt,outputs,command,cancellation);}
    }
    [Fact]
    public async Task Temp_sample_opens_adds_crate_moves_saves_Plays_exports_and_runs_standalone_with_zero_exits()
    {
        using var owned=new CapturedExportTests.Owned();
        var root=Directory.CreateDirectory(Path.Combine(owned.Root,"project")).FullName;
        var sample=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter");
        var originals=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var relative in new[]{"reference_map.json","data/level.json","schemas/scene.schema.json"}
            .Concat(Directory.GetFiles(Path.Combine(sample,"assets"),"*",SearchOption.AllDirectories).Select(file=>Path.GetRelativePath(sample,file))))
        {
            var bytes=File.ReadAllBytes(Path.Combine(sample,relative));originals.Add(relative,bytes);
            var copiedInput=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(copiedInput)!);File.WriteAllBytes(copiedInput,bytes);
        }
        Directory.CreateDirectory(Path.Combine(root,"game"));
        foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)
            File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name),Path.Combine(root,"game",name));
        PlaySettingsStore.Save(root,new("game/SceneStarter.exe",["--project",".","--seconds","0"]));
        var settings=File.ReadAllBytes(Path.Combine(root,".bed","project.json"));
        using var host=new HeadlessHost(1600,1000){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var adapters=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));Assert.Empty(adapters.Problems);
        using var launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(owned.Root,"local","recent.json"))){Loaded=adapters.Loaded});
        var play=new SamplePlayJourneyTests.ObservedHost(Assert.IsType<ProjectPlayBridge>(launcher.ProjectPlayHost));launcher.ProjectPlayHost=play;
        var export=new ObservedExport(Assert.IsType<ExportBuildHost>(launcher.GameExportHost));launcher.GameExportHost=export;
        Assert.True(launcher.OpenFolder(root),launcher.Said);Assert.Equal("SceneStarter",launcher.Using!.Name);
        var workspace=launcher.Workspace!;host.Frames(launcher.Draw,4);
        void Frames()=>host.Frames(launcher.Draw,2);
        void Key(ImGuiKey key,bool ctrl=false){host.Press(launcher.Draw,key,ctrl:ctrl);Frames();}
        void Click(UiNode node)
        {Assert.True(node.Enabled,node.DisabledReason);Assert.False(node.Bounds.IsEmpty);host.Click(launcher.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);Frames();}
        void Button(string label)=>Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label));
        void Until(Func<bool> done,string step)
        {var timer=Stopwatch.StartNew();while(!done()&&timer.Elapsed<TimeSpan.FromSeconds(10)){host.Frame(launcher.Draw);Thread.Sleep(5);}Assert.True(done(),step+": "+workspace.Said+"; "+string.Join(" | ",Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Text).Select(n=>n.Label)));}
        Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.Contains("level.json",StringComparison.Ordinal)));
        var window=workspace.SceneWindow!;Assert.NotNull(window);var initialIds=window.Editor.Session.Current.Objects.Select(o=>o.Id).ToHashSet();
        Assert.Equal(originals["data/level.json"],File.ReadAllBytes(Path.Combine(root,"data","level.json")));
        var pointer=File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json"));
        const string crate="c8a7e000000000000000000000000001",texture="c8a7e000000000000000000000000002";
        Assert.NotNull(workspace.SceneAssets!.Select(crate));Assert.NotNull(workspace.SceneAssets.Select(texture));
        Key(ImGuiKey.Insert);Assert.True(window.Editor.IsOpen("scene.assets"));
        Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="textured-crate.obj"));
        Button("Add selected object");Assert.False(window.Editor.IsOpen("scene.assets"));
        var added=Assert.Single(window.Editor.Session.Current.Objects,o=>!initialIds.Contains(o.Id));Assert.Equal(crate,added.Asset);Assert.Equal(added.Id,window.Editor.Primary);
        void Text(string label,string value,ImGuiKey finish=ImGuiKey.Escape)
        {
            Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==label));
            Key(ImGuiKey.A,ctrl:true);host.Type(launcher.Draw,value);Key(ImGuiKey.Enter);Key(finish);
        }
        Text("Position X","4.5");
        var moved=window.Editor.Session.Current.Find(added.Id)!;Assert.Equal(4.5f,moved.Position.X);Assert.True(window.Editor.Session.Dirty);
        Key(ImGuiKey.S,ctrl:true);Until(()=>!window.Editor.Session.Dirty,"Save");
        var saved=File.ReadAllBytes(Path.Combine(root,"data","level.json"));Assert.NotEqual(originals["data/level.json"],saved);
        var expected=window.Editor.Session.Current;var savedLevel=SceneStarter.SavedScene.Open(root);
        Assert.Equal(new SceneFormat().Write(expected),new SceneFormat().Write(savedLevel.Level.Scene));
        Button("Play");Assert.False(workspace.ProjectPlaySavePending);
        Until(()=>play.Session is {HasCompleted:true,ExitCode:not null},"Play exit");
        Assert.Equal(0,play.Session!.ExitCode);Assert.Equal(1,play.Starts);Assert.Equal(0,play.Session.Stops);
        var loadedLine=$"SceneStarter loaded saved level: {expected.Objects.Length} objects, {savedLevel.Meshes.Count} meshes.";
        Assert.Equal(File.ReadAllText(Path.Combine(root,"data","level.json")),play.Session.SavedAtStart);
        Assert.Contains(play.Session.Logs,line=>!line.Error&&line.Text==loadedLine);
        Assert.Equal(saved,File.ReadAllBytes(Path.Combine(root,"data","level.json")));Assert.False(window.Editor.Session.Dirty);
        Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="File"));Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Export build..."));
        Button("Build saved files");
        Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label=="Copy build to folder..."&&n.Enabled),"Export build and smoke");
        Assert.NotNull(export.Operation);Assert.NotNull(export.Receipt);Assert.True(export.Receipt.Succeeded,export.Receipt.Reason);Assert.Equal(0,export.Receipt.ExitCode);
        Assert.NotNull(export.SmokeResult!.Build);
        var destination=Path.Combine(owned.Root,"standalone");
        // Escape now closes the export dialog. Tab commits the field and reaches its copy control.
        Text("Destination folder",destination,ImGuiKey.Tab);
        Assert.Equal(destination,Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Destination folder").Value);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Copy build to folder...").Focused);
        Button("Copy build to folder...");
        Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Status&&n.Label=="Export status: Build copied to "+destination),"Export copy");
        Assert.True(File.Exists(Path.Combine(destination,"SceneStarter.exe")));Assert.False(File.Exists(Path.Combine(destination,"scene.json")));Assert.False(File.Exists(Path.Combine(destination,"payload.bin")));
        foreach(var node in export.Plan!.Admission.Source.Nodes)
        {var bytes=new byte[node.Length];node.CopyTo(bytes);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(destination,node.Output.Destination)));}
        var standalone=SceneStarter.SavedScene.Open(destination);Assert.Equal(savedLevel.Assets.Id,standalone.Assets.Id);
        Assert.Equal(moved.Position,standalone.Level.Scene.Find(added.Id)!.Position);
        Assert.Equal(savedLevel.Assets.ReadAsset(crate),standalone.Assets.ReadAsset(crate));Assert.Equal(savedLevel.Assets.ReadAsset(texture),standalone.Assets.ReadAsset(texture));
        var start=new ProcessStartInfo(Path.Combine(destination,"SceneStarter.exe")){WorkingDirectory=destination,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"--project",".","--seconds","0"})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));Assert.Equal(0,process.ExitCode);
            Assert.Contains(loadedLine,await output);Assert.Equal("",await error);
        }
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        foreach(var (relative,bytes) in originals.Where(pair=>pair.Key!="data/level.json"))Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(root,relative)));
        Assert.Equal(saved,File.ReadAllBytes(Path.Combine(root,"data","level.json")));Assert.Equal(settings,File.ReadAllBytes(Path.Combine(root,".bed","project.json")));
        Assert.Equal(pointer,File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json")));Assert.False(window.Editor.Session.Dirty);
    }
}
