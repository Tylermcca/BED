using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
using BedProgram=Bed.Launcher.Program;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class BigEditPlayPerf11Tests(ITestOutputHelper output)
{
    private sealed class CountPreparation(IAssetCatalogProvisioner inner):IAssetCatalogProvisioner
    {
        internal int Calls;
        public ProjectAssetCatalog? Open(ProjectPaths paths,string? assetsFolder=null)
        {Calls++;return inner.Open(paths,assetsFolder);}
    }
    private sealed class ObservePlay(IProjectPlayHost inner,Stopwatch clock):IProjectPlayHost
    {
        internal double Started;
        internal int Calls;
        internal readonly List<ProjectPlayOutput> Logs=[];
        public IProjectPlaySession Start(PlayLaunch launch){Calls++;return new Session(inner.Start(launch),this,clock);}
        private sealed class Session(IProjectPlaySession inner,ObservePlay owner,Stopwatch clock):IProjectPlaySession
        {
            public bool Start(bool dirty,bool build=false){var result=inner.Start(dirty,build);owner.Started=clock.Elapsed.TotalMilliseconds;return result;}
            public bool CanBuildAndPlay=>inner.CanBuildAndPlay;
            public bool HasCompleted=>inner.HasCompleted;
            public int? ExitCode=>inner.ExitCode;
            public string Status=>inner.Status;
            public void RequestStop()=>inner.RequestStop();
            public void Update()=>inner.Update();
            public IReadOnlyList<ProjectPlayOutput> DrainLogs(){var logs=inner.DrainLogs();owner.Logs.AddRange(logs);return logs;}
            public void Draw()=>inner.Draw();
            public void Dispose()=>inner.Dispose();
        }
    }
    [Fact]
    public async Task F5_after_a_five_thousand_object_move_saves_then_starts_a_real_saved_level_reader_without_asset_repreparation()
    {
        var temporary=Directory.CreateTempSubdirectory("big-edit-play-perf11-").FullName;
        try
        {
            var project=ReleasePlayProbeTests.CopyProject(temporary);var path=Path.Combine(project,"data","level.json");var format=new SceneFormat();
            var initial=new Scene(Enumerable.Range(0,5000).Select(i=>SceneObject.At("object_"+i,null,new Vector3(i%100,i/100,0))).ToImmutableArray());
            File.WriteAllText(path,format.Write(initial));
            // A tiny self-authored hidden game reads the real saved level and reports its exact hash.
            // Its build is fixture setup, outside every F5 timing and outside the configured Play chain.
            var source=Directory.CreateDirectory(Path.Combine(temporary,"reader source")).FullName;
            File.WriteAllText(Path.Combine(source,"Reader.csproj"),"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><AssemblyName>Perf11Reader</AssemblyName><DebugType>none</DebugType></PropertyGroup></Project>
                """);
            File.WriteAllText(Path.Combine(source,"Program.cs"),"""
                var bytes=File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("BED_PROJECT")!,"data","level.json"));
                using var level=System.Text.Json.JsonDocument.Parse(bytes);
                var objects=level.RootElement.GetProperty("objects");
                Console.WriteLine("Loaded "+objects.GetArrayLength()+" saved objects; sha256="+Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
                return objects.GetArrayLength()==5000?0:7;
                """);
            var build=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"build",Path.Combine(source,"Reader.csproj"),"-c","Release","-o",Path.Combine(project,"game"),"--nologo","-v","quiet"})build.ArgumentList.Add(argument);
            using(var child=Process.Start(build)!)
            {
                var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
                try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.True(child.ExitCode==0,await stdout+await stderr);}
                finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
            }
            PlaySettingsStore.Save(project,new("game/Perf11Reader.exe",[]));
            var settingsPath=Path.Combine(project,".bed","project.json");var settings=File.ReadAllBytes(settingsPath);
            var sources=Directory.GetFiles(Path.Combine(project,"assets"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
            using var host=new HeadlessHost(1400,900){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
            var adapters=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));Assert.Empty(adapters.Problems);
            using var launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(temporary,"local","recent.json"))){Loaded=adapters.Loaded});
            var preparation=new CountPreparation(launcher.AssetCatalogProvisioner!);launcher.AssetCatalogProvisioner=preparation;
            var clock=new Stopwatch();var play=new ObservePlay(launcher.ProjectPlayHost!,clock);launcher.ProjectPlayHost=play;
            Assert.True(launcher.OpenFolder(project),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("level.json"));
            var window=workspace.SceneWindow!;var editor=window.Editor;var session=editor.Session;
            host.Frames(workspace.Draw,3);Assert.Equal(1,preparation.Calls);
            var cache=Path.Combine(project,".bed","cache");
            var published=Directory.GetFiles(cache,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>(Bytes:File.ReadAllBytes(p),Stamp:File.GetLastWriteTimeUtc(p)));
            var catalog=workspace.SceneAssets;var times=new List<double>();
            Assert.True(editor.Select(session.Current.Objects.Select(o=>o.Id).ToArray()));
            for(var run=0;run<3;run++)
            {
                Assert.Equal(5000,editor.Selection.Count);
                var plan=SceneTranslation.World(session.Current,editor.Selection,Vector3.UnitX);
                Assert.True(plan.Valid);Assert.True(editor.ExecuteAt(session.Revision,plan.Commands,plan.Roots.ToArray()));
                var draft=session.Current;var revision=session.Revision;var journal=session.Journal.ToArray();var count=play.Calls;
                Assert.True(session.Dirty);clock.Restart();host.Press(workspace.Draw,ImGuiKey.F5);
                var promptAt=clock.Elapsed.TotalMilliseconds;
                Assert.True(workspace.ProjectPlaySavePending);Assert.Equal(count,play.Calls);
                var save=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Save"&&n.Focused);
                Assert.True(save.Enabled);var answerAt=clock.Elapsed.TotalMilliseconds;
                host.Press(workspace.Draw,ImGuiKey.Enter);Assert.False(workspace.ProjectPlaySavePending);Assert.False(workspace.ProjectPlayApprovalPending);
                Assert.Equal(count+1,play.Calls);times.Add(play.Started);
                var game=workspace.ProjectPlay!;var expectedHash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
                var expected="Loaded 5000 saved objects; sha256="+expectedHash;
                var wait=Stopwatch.StartNew();
                while((!game.HasCompleted||game.ExitCode is null||!play.Logs.Any(l=>l.Text==expected))&&wait.Elapsed<TimeSpan.FromSeconds(8)){host.Frame(workspace.Draw);Thread.Sleep(1);}
                var loadedAt=clock.Elapsed.TotalMilliseconds;Assert.True(game.HasCompleted,game.Status);Assert.Equal(0,game.ExitCode);
                Assert.Contains(play.Logs,l=>!l.Error&&l.Text==expected);
                Assert.Same(draft,session.Current);Assert.Equal(revision,session.Revision);Assert.Equal(journal,session.Journal);
                Assert.Equal(run+1,session.History.UndoCount);Assert.False(session.Dirty);Assert.Equal(format.Write(draft),File.ReadAllText(path));
                Assert.Equal(1,preparation.Calls);Assert.Same(catalog,workspace.SceneAssets);
                Assert.Equal(published.Keys.Order(),Directory.GetFiles(cache,"*",SearchOption.AllDirectories).Order());
                foreach(var (file,before) in published){Assert.Equal(before.Bytes,File.ReadAllBytes(file));Assert.Equal(before.Stamp,File.GetLastWriteTimeUtc(file));}
                output.WriteLine($"run {run+1}: F5->prompt {promptAt:F3} ms; Save-key->OS process started {play.Started-answerAt:F3} ms; F5->OS start {play.Started:F3} ms; saved-level loaded/exit observed {loadedAt:F3} ms. No human decision delay; real native F5 and Enter.");
            }
            Assert.Equal(settings,File.ReadAllBytes(settingsPath));foreach(var (file,bytes) in sources)Assert.Equal(bytes,File.ReadAllBytes(file));
            output.WriteLine($"F5->OS start median: {times.Order().ElementAt(1):F3} ms. Full 5k workspace UI, CPU rasterization off; move/planning and fixture build excluded. One startup asset preparation, none during three Save-and-Play requests; same catalog, exact published cache names/bytes/timestamps. Reader is a self-authored console game, not a render/readiness benchmark.");
        }
        finally{Directory.Delete(temporary,true);}
    }
}
