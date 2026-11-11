using System.Diagnostics;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
using BedProgram=Bed.Launcher.Program;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlayStartPerf06Tests(ITestOutputHelper output)
{
    private sealed class TimedHost(IProjectPlayHost inner):IProjectPlayHost
    {
        public long KeyAt;
        public TimeSpan? ToProcess;
        public TimedSession? Session;
        public IProjectPlaySession Start(PlayLaunch launch)=>Session=new(inner.Start(launch),this);
    }
    private sealed class TimedSession(IProjectPlaySession inner,TimedHost timer):IProjectPlaySession
    {
        public readonly List<ProjectPlayOutput> Logs=[];
        public bool Start(bool dirty,bool build=false)
        {
            var started=inner.Start(dirty,build);
            if(started&&inner.Status=="Running.")timer.ToProcess=Stopwatch.GetElapsedTime(timer.KeyAt);
            return started;
        }
        public bool CanBuildAndPlay=>inner.CanBuildAndPlay;
        public bool HasCompleted=>inner.HasCompleted;
        public int? ExitCode=>inner.ExitCode;
        public string Status=>inner.Status;
        public void RequestStop()=>inner.RequestStop();
        public void Update()=>inner.Update();
        public IReadOnlyList<ProjectPlayOutput> DrainLogs(){var lines=inner.DrainLogs();Logs.AddRange(lines);return lines;}
        public void Draw()=>inner.Draw();
        public void Dispose()=>inner.Dispose();
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Saved_fresh_F5_reports_process_start_time_without_reading_unchanged_asset_build_inputs(bool manyAssets)
    {
        var root=Directory.CreateTempSubdirectory("play-perf06-").FullName;
        try
        {
            var project=Directory.CreateDirectory(Path.Combine(root,"project")).FullName;
            var data=Directory.CreateDirectory(Path.Combine(project,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(project,"schemas")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(project,"assets")).FullName;
            var sample=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter");
            foreach(var relative in new[]{"reference_map.json","data/level.json","schemas/scene.schema.json"})File.Copy(Path.Combine(sample,relative),Path.Combine(project,relative));
            if(manyAssets)
            {
                for(var i=0;i<256;i++)
                {
                    var path=Path.Combine(assets,$"mesh-{i:D3}.obj");File.WriteAllText(path,$"v {i} 0 0\nv {i+1} 0 0\nv {i} 1 0\nf 1 2 3\n");
                    var record=new AssetRecord((i+1).ToString("x32"),Path.GetFileName(path),AssetRecord.HashOf(path),"obj",1,JsonSerializer.SerializeToElement(new{scale=1}),
                        new("fixture","self-authored","generated",Declared:AssetOrigin.Ours),AiUse.Forbidden);File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());
                }
                // This project has 256 prepared assets and the same saved two-object level with no placed assets.
                File.WriteAllText(Path.Combine(data,"level.json"),SamplePlayJourneyTests.Saved);
            }
            else foreach(var path in Directory.GetFiles(Path.Combine(sample,"assets"),"*",SearchOption.AllDirectories))
            {
                var destination=Path.Combine(assets,Path.GetRelativePath(Path.Combine(sample,"assets"),path));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(path,destination);
            }
            var game=Directory.CreateDirectory(Path.Combine(project,"game")).FullName;
            foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name),Path.Combine(game,name));
            // F5 runs the fresh program. A build declaration supplies a real source glob but is never invoked or approved here.
            PlaySettingsStore.Save(project,new("game/SceneStarter.exe",["--project",".","--seconds","0"],build:new("dotnet",["build","Fixture.csproj"],["assets/**/*"])));
            File.SetLastWriteTimeUtc(Path.Combine(game,"SceneStarter.exe"),DateTime.UtcNow.AddMinutes(1));
            var adapters=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));Assert.Empty(adapters.Problems);
            using var host=new HeadlessHost(1400,950){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
            using var launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(root,"local","recent.json"))){Loaded=adapters.Loaded});
            var timer=new TimedHost(Assert.IsType<ProjectPlayBridge>(launcher.ProjectPlayHost));launcher.ProjectPlayHost=timer;
            Assert.True(launcher.OpenFolder(project),launcher.Said);var workspace=launcher.Workspace!;
            Assert.True(workspace.Show("level.json"),workspace.Said);host.Frames(launcher.Draw,4);
            var window=workspace.SceneWindow!;Assert.False(window.Editor.Session.Dirty);
            var assetCount=workspace.SceneAssets!.CurrentInputs().Count;Assert.Equal(manyAssets?256:4,assetCount);
            var level=Path.Combine(data,"level.json");var saved=File.ReadAllBytes(level);var stamp=File.GetLastWriteTimeUtc(level);
            var settings=File.ReadAllBytes(Path.Combine(project,".bed","project.json"));var pointer=File.ReadAllBytes(Path.Combine(project,".bed","cache","current.json"));
            var originalAssets=Directory.GetFiles(assets,"*",SearchOption.AllDirectories).ToDictionary(path=>path,File.ReadAllBytes);
            var locks=new List<FileStream>();
            try
            {
                foreach(var path in originalAssets.Keys)locks.Add(new(path,FileMode.Open,FileAccess.Read,FileShare.None));
                // Any attempted source/sidecar read (including SHA-256) would now fail. Metadata-only freshness remains available.
                Assert.Throws<IOException>(()=>File.ReadAllBytes(originalAssets.Keys.First()));
                timer.KeyAt=Stopwatch.GetTimestamp();host.Press(launcher.Draw,ImGuiKey.F5);
                Assert.NotNull(timer.ToProcess);Assert.NotNull(timer.Session);Assert.False(workspace.ProjectPlaySavePending);Assert.False(workspace.ProjectPlayApprovalPending);
                output.WriteLine($"F5 to actual owned process creation, {(manyAssets?"256 generated OBJ assets":"saved level sample")}: {timer.ToProcess.Value.TotalMilliseconds:F3} ms ({assetCount} assets). No asset source/sidecar read was possible.");
                var deadline=Stopwatch.StartNew();while(timer.Session is {HasCompleted:false}&&deadline.Elapsed<TimeSpan.FromSeconds(8)){host.Frame(launcher.Draw);Thread.Sleep(5);}host.Frames(launcher.Draw,2);
                Assert.True(timer.Session!.HasCompleted,timer.Session.Status);Assert.Equal(0,timer.Session.ExitCode);
                Assert.Contains(timer.Session.Logs,line=>!line.Error&&line.Text.StartsWith("SceneStarter loaded saved level:",StringComparison.Ordinal));
            }
            finally{foreach(var held in locks)held.Dispose();}
            Assert.False(window.Editor.Session.Dirty);Assert.Equal(saved,File.ReadAllBytes(level));Assert.Equal(stamp,File.GetLastWriteTimeUtc(level));
            Assert.Equal(settings,File.ReadAllBytes(Path.Combine(project,".bed","project.json")));Assert.Equal(pointer,File.ReadAllBytes(Path.Combine(project,".bed","cache","current.json")));
            foreach(var (path,bytes) in originalAssets)Assert.Equal(bytes,File.ReadAllBytes(path));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
}
