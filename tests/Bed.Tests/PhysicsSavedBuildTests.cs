using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PhysicsSavedBuildTests(ITestOutputHelper output)
{
    private const string Model="66666666666666666666666666666666";
    private const string Generated=".bed/generated/scenes/level.json",Descriptor=".bed/generated/scenes/runtime.json";
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("physics-saved-").FullName;
        internal readonly string Logs=Directory.CreateTempSubdirectory("physics-export-logs-").FullName;
        internal readonly AssetCatalog Catalog=new();private readonly HeadlessHost context=new(){Drawing=false};
        internal readonly IProjectAdapter Adapter;internal readonly DataWorkspace Workspace;internal readonly GameExportDeclaration Declaration;internal readonly string Text;
        internal string Config=>Path.Combine(Root,".bed/project.json");internal string Source=>Path.Combine(Root,"data/level.json");
        internal Rig(bool flat=false,bool compile=true,bool degenerate=false)
        {
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));var assets=Directory.CreateDirectory(Path.Combine(Root,"assets")).FullName;Directory.CreateDirectory(Path.Combine(Root,".bed"));
            var objects=new[]{new Scene2Object(SceneObject.At("floor",null,new(0,-.5f,0)),null){Body=new(SceneBodyMotion.Static,new SceneBoxShape(new(20,.5f,20)),.6f)},new Scene2Object(SceneObject.At("falling",flat?null:Model,new(0,3,0)),null){Body=new(SceneBodyMotion.Dynamic,flat?new SceneBoxShape(new(.5f)):new SceneMeshBoundsShape(),.4f,2)}};
            var scene=new Scene2(flat?SceneSpace.TwoDXY:SceneSpace.ThreeD,objects.ToImmutableArray(),[],[],null){Physics=new(new(0,-10,0),.01f)};Text=new Scene2Format().Write(scene);File.WriteAllText(Source,Text);File.WriteAllText(Config,Scene2BuildSettings.WriteProject("{}",new("data/level.json",Generated,[scene.Space]){SupportsPhysics=true}));
            byte[] source=[1,2,3];var record=new AssetRecord(Model,"original.obj",Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","original primitive",Declared:AssetOrigin.Ours),AiUse.Forbidden);var mesh=degenerate?new MeshData([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]):MeshData.Cube;var derived=DerivedMeshCodec.Encode(mesh);Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new AssetCatalog.Input(record,source,derived,[],true,"fixture","own")],out _));
            using(var publisher=new AssetCatalogPublisher(Root))Assert.True(publisher.Publish(publisher.Prepare("original-physics",new Dictionary<string,byte[]>{{Model+".bin",derived}}),out _));
            var inventory=typeof(PhysicsStarter.Program).Assembly.GetType("SceneStarter.PhysicsRuntimeInventory")!;var files=(string[])inventory.GetField("Files",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            foreach(var file in files){var path=Path.Combine(Root,"game",file);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","PhysicsStarter",file),path);}
            var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);Adapter=Assert.Single(loaded.Loaded);_=Adapter.DefaultPlay(new(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")){Root=Root});Workspace=new(Adapter.Open(new(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")){Root=Root}));Assert.True(Workspace.BindSceneAssets(new AssetBrowser(assets),Catalog));Declaration=Adapter.Export(Workspace)!;if(compile)Compile();
        }
        internal int CompileCode(out string error){using var stdout=new StringWriter();using var stderr=new StringWriter();var code=PhysicsStarter.Program.Run(["--compile-scene2",Root],stdout,stderr);error=stderr.ToString();return code;}
        internal void Compile(){Assert.Equal(0,CompileCode(out var why));Assert.Empty(why);}
        internal PlaySnapshot Capture(){var result=PlaySnapshot.Create(new("level.json",1,Text,"unsaved"),PlayInputChoice.Saved,Declaration.ConvertSaved);Assert.Null(result.Error);return result.Snapshot!;}
        public void Dispose(){Workspace.Dispose();Catalog.Dispose();context.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Logs),StringComparison.OrdinalIgnoreCase);Directory.Delete(Logs,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Copied_physics_game_runs_prepared_bodies_without_editor_authoring_or_original_cache(bool flat)
    {
        using var r=new Rig(flat);var generated=new Scene2Format().Read(File.ReadAllText(Path.Combine(r.Root,Generated)));Assert.All(generated.Objects.Where(o=>o.Body is not null),o=>Assert.IsNotType<SceneMeshBoundsShape>(o.Body!.Shape));Assert.Equal(new Vector3(.5f),Assert.IsType<SceneBoxShape>(generated.Objects[1].Body!.Shape).HalfExtents);
        var plan=r.Declaration.Prepare(r.Capture());Assert.Contains("runtimes/win-x64/native/jolt.dll",plan.Outputs.Paths);
        using var build=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(10),r.Logs).Start(r.Adapter.Build(r.Workspace)!,r.Root,false,plan);var receipt=await build.Completion.WaitAsync(TimeSpan.FromSeconds(15));Assert.True(receipt.Succeeded,receipt.Reason);var smoke=await VerifiedGameBuild.SmokeAsync(plan,receipt,build.OutputManifest!,r.Declaration.Smoke(receipt.OutputDirectory,build.OutputManifest!),TimeSpan.FromSeconds(10));Assert.NotNull(smoke.Build);
        var destination=Directory.CreateTempSubdirectory("physics-copied-").FullName;
        try
        {
            Assert.True(smoke.Build.CopyTo(destination,out var why),why);Assert.False(Directory.Exists(Path.Combine(destination,"data")));Assert.False(Directory.Exists(Path.Combine(destination,"assets")));Assert.DoesNotContain(Directory.GetFiles(destination,"*",SearchOption.AllDirectories),p=>Path.GetFileName(p).Contains("Editor",StringComparison.Ordinal));Assert.True(File.Exists(Path.Combine(destination,"licenses/BRUTAL-JOLT-THIRD-PARTY-NOTICES.txt")));
            File.Delete(r.Source);var cache=Path.GetFullPath(Path.Combine(r.Root,".bed/cache"));var offline=Path.GetFullPath(Path.Combine(r.Root,"cache-offline"));Assert.StartsWith(r.Root+Path.DirectorySeparatorChar,cache,StringComparison.OrdinalIgnoreCase);Assert.StartsWith(r.Root+Path.DirectorySeparatorChar,offline,StringComparison.OrdinalIgnoreCase);Directory.Move(cache,offline);
            var info=new ProcessStartInfo(Path.Combine(destination,"PhysicsStarter.exe")){WorkingDirectory=Path.GetTempPath(),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"--project",destination,"--seconds","0.1"})info.ArgumentList.Add(arg);using var child=Process.Start(info)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));}finally{if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}}Assert.Equal(0,child.ExitCode);Assert.Empty(await stderr);Assert.Contains("loaded saved physics: 2 bodies",await stdout);output.WriteLine(await stdout);
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase);Directory.Delete(destination,true);}
    }
    [Fact]
    public async Task Common_Build_and_Play_uses_physics_program_and_content_stamps_even_with_preserved_source_mtime()
    {
        using var r=new Rig();var defaults=r.Adapter.DefaultPlay(new(Path.Combine(r.Root,"data"),Path.Combine(r.Root,"schemas")){Root=r.Root})!;Assert.Equal("game/PhysicsStarter.exe",defaults.Program);Assert.Contains("game/runtimes/win-x64/native/jolt.dll",defaults.Build!.Inputs);var settings=new PlaySettings(defaults.Program,["--project",".","--seconds","0.02"],defaults.WorkingFolder,defaults.Build);Assert.True(PlaySettingsResolver.TryResolve(r.Root,settings,out var launch,out var why),why);
        var time=File.GetLastWriteTimeUtc(r.Source);var source=JsonNode.Parse(r.Text)!;source["objects"]![1]!["body"]!["massKg"]=7;File.WriteAllText(r.Source,source.ToJsonString());File.SetLastWriteTimeUtc(r.Source,time);using var host=new ProjectPlayHost(launch!,TimeProvider.System,TimeSpan.FromSeconds(10));Assert.False(host.Start(false));Assert.Equal(ProjectPlayState.NeedsBuild,host.State);Assert.True(host.Start(false,true),host.Status);for(var phase=0;phase<4&&host.State is ProjectPlayState.Building or ProjectPlayState.Running;phase++){await host.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(15));host.Update();}Assert.Equal(ProjectPlayState.Stopped,host.State);Assert.Equal(0,host.ExitCode);Assert.Contains(host.DrainLogs(),log=>log.Text.Contains("loaded saved physics"));Assert.Equal(7,new Scene2Format().Read(File.ReadAllText(Path.Combine(r.Root,Generated))).Objects[1].Body!.MassKg);
    }
    [Theory][InlineData("catalog")][InlineData("binary")][InlineData("source")][InlineData("descriptor")]
    public void Captured_physics_export_refuses_stale_capture_policy_runtime_or_saved_content(string change)
    {
        using var r=new Rig();var saved=r.Capture();if(change=="catalog")r.Catalog.BeginRefresh();else File.AppendAllText(Path.Combine(r.Root,change switch{"binary"=>"game/PhysicsStarter.dll","source"=>"data/level.json",_=>Descriptor})," ");Assert.Throws<InvalidOperationException>(()=>r.Declaration.Prepare(saved));
    }
    [Fact]
    public void Degenerate_model_fit_and_old_runtime_or_missing_capability_refuse_without_publishing_current_output()
    {
        using(var r=new Rig(compile:false,degenerate:true)){Assert.Equal(1,r.CompileCode(out var error));Assert.Contains("nondegenerate",error);Assert.False(File.Exists(Path.Combine(r.Root,Descriptor)));Assert.False(File.Exists(Path.Combine(r.Root,".bed/generated/scenes/build.stamp.json")));}
        using(var r=new Rig()){using var stdout=new StringWriter();using var stderr=new StringWriter();Assert.NotEqual(0,SceneStarter.Program.Run(["--project",r.Root,"--seconds","0"],stdout,stderr));Assert.Equal(3,SceneStarter.Program.Run(["--compile-scene2",r.Root],stdout,stderr));var settings=JsonNode.Parse(File.ReadAllText(r.Config))!;settings["sceneBuild"]!.AsObject().Remove("physics");File.WriteAllText(r.Config,settings.ToJsonString());Assert.Equal(1,r.CompileCode(out var why));Assert.Contains("physics",why);}
    }
}
