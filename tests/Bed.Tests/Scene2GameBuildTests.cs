using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Xunit;
using Xunit.Abstractions;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class Scene2GameBuildTests(ITestOutputHelper output)
{
    private const string Model="11111111111111111111111111111111",Image="33333333333333333333333333333333",Overlay="44444444444444444444444444444444";
    private const string Generated=".bed/generated/scenes/level.json",Descriptor=".bed/generated/scenes/runtime.json";
    private static string Fixture(string name)
    {var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;return File.ReadAllText(Path.Combine(repo!.FullName,"tests/Bed.Tests/Fixtures/Scene2",name));}
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("scene2-game-").FullName;
        private readonly HeadlessHost headless=new(){Drawing=false};
        internal readonly AssetCatalog Catalog=new();
        internal readonly IProjectAdapter Adapter;
        internal readonly DataWorkspace Workspace;
        internal readonly GameExportDeclaration Declaration;
        internal readonly string Text;
        internal readonly bool Flat;
        internal readonly string Source,Name;
        internal Rig(bool flat=true,AssetOrigin origin=AssetOrigin.Ours,bool custom=false)
        {
            Flat=flat;Name=custom?"custom.json":"level.json";Source="data/"+Name;Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));var assets=Directory.CreateDirectory(Path.Combine(Root,"assets")).FullName;
            var scene=new Scene2Format().Read(Fixture(flat?"scene-2d.example.json":"scene-3d.example.json"));
            if(flat)
            {
                var definition=JsonNode.Parse(Fixture("prefab-definition.example.json"))!;definition["space"]="2d-xy";
                var node=definition["nodes"]![0]!;node["asset"]=Image;node["sprite"]=JsonNode.Parse("""{"size":[2,3],"pivot":[0.25,0.75],"order":20,"filter":"nearest"}""");
                var applied=Scene2Kit.Command("instantiate",JsonSerializer.SerializeToElement(new{key="22222222222222222222222222222222",baseId="piece_base",definition,
                    placement=new{id="piece",parent="room",position=new[]{2,3,0},scale=new[]{-2,3,1}},
                    nodeIds=new Dictionary<string,string>{{"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","piece_root"},{"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","piece_marker"}},
                    bindings=new Dictionary<string,string>{{"cccccccccccccccccccccccccccccccc","room"}}})).Apply(scene);
                Assert.True(applied.Ok,string.Join(" ",applied.Issues));scene=applied.Document!;
            }
            else
            {var json=JsonNode.Parse(new Scene2Format().Write(scene))!;json["objects"]![0]!["asset"]=Model;scene=new Scene2Format().Read(json.ToJsonString());}
            Text=new Scene2Format().Write(scene);File.WriteAllText(Path.Combine(Root,Source),Text);
            Directory.CreateDirectory(Path.Combine(Root,".bed"));File.WriteAllText(Path.Combine(Root,".bed/project.json"),Scene2BuildSettings.WriteProject("{}",new(Source,Generated,[SceneSpace.ThreeD,SceneSpace.TwoDXY])));
            var inputs=new List<AssetCatalog.Input>();
            foreach(var id in flat?new[]{Image,Overlay}:new[]{Model})
            {
                byte[] source=[1,2,3];var record=new AssetRecord(id,id+(flat?".png":".mesh"),Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{}),
                    new("Fixture author",origin==AssetOrigin.Licensed?"MIT":"self-authored","synthetic fixture",Declared:origin),AiUse.Forbidden);
                var derived=flat?new byte[]{47,83,127,128}:DerivedMeshCodec.Encode(new([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]));
                inputs.Add(new(record,source,derived,[],true,"self-authored-scene2","policy",texture:flat?new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb):null));
            }
            Assert.True(Catalog.Publish(Catalog.BeginRefresh(),inputs,out var why),why);
            var artifacts=new Dictionary<string,byte[]>();foreach(var id in inputs.Select(i=>i.Guid)){Assert.True(Catalog.TryResolve(Catalog.Select(id)!,out var lease,out _));artifacts.Add(id+".bin",TextureAssetGeneration.StoredArtifact(lease!));}
            using(var publisher=new AssetCatalogPublisher(Root))Assert.True(publisher.Publish(publisher.Prepare("self-authored-v2",artifacts),out why),why);
            Compile();var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);Adapter=Assert.Single(loaded.Loaded);
            var paths=new ProjectPaths(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")){Root=Root};_=Adapter.DefaultPlay(paths);
            Workspace=new(Adapter.Open(paths));Assert.True(Workspace.BindSceneAssets(new AssetBrowser(assets),Catalog));Declaration=Adapter.Export(Workspace)!;
        }
        internal void Compile(){using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(0,SceneStarter.Program.Run(["--compile-scene2",Root],output,error));Assert.Empty(error.ToString());}
        internal PlaySnapshot Capture(){var result=PlaySnapshot.Create(new(Name,1,Text,"unsaved draft"),PlayInputChoice.Saved,Declaration.ConvertSaved);Assert.Null(result.Error);Assert.NotNull(result.Snapshot);return result.Snapshot!;}
        public void Dispose(){Workspace.Dispose();Catalog.Dispose();headless.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Runtime_descriptor_loads_effective_saved_metadata_and_published_assets_without_an_editor(bool flat)
    {
        using var r=new Rig(flat);var before=Directory.GetFiles(r.Root,"*",SearchOption.AllDirectories).ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
        var loaded=SceneStarter.SavedScene.Open(r.Root);Assert.NotNull(loaded.Level2);Assert.Empty(loaded.Level2.Scene.Bases);Assert.Empty(loaded.Level2.Scene.Instances);
        Assert.Equal(Scene2Resolver.Resolve(new Scene2Format().Read(r.Text)).Objects.Select(x=>JsonSerializer.Serialize(x)),loaded.Level2.Scene.Objects.Select(x=>JsonSerializer.Serialize(x)));
        Assert.Equal(flat?2:0,loaded.Textures.Count);Assert.Equal(flat?0:1,loaded.Meshes.Count);
        if(flat){Assert.Equal(new byte[]{47,83,127,128},loaded.Textures[Image].Rgba);Assert.Equal(SceneSpriteFilter.Nearest,loaded.Level2.Scene.Objects.Single(o=>o.Object.Id=="piece_root").Sprite!.Filter);}
        Assert.Equal(before.OrderBy(x=>x.Key),Directory.GetFiles(r.Root,"*",SearchOption.AllDirectories).ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))).OrderBy(x=>x.Key));
        Assert.DoesNotContain(typeof(SceneStarter.Program).Assembly.GetReferencedAssemblies(),a=>a.Name!.Contains("Editor",StringComparison.Ordinal));
    }
    [Theory][InlineData("missing")][InlineData("hash")][InlineData("generation")][InlineData("duplicate")][InlineData("path")][InlineData("unflattened")][InlineData("device")][InlineData("format")]
    public void Runtime_descriptor_refuses_missing_changed_or_authoring_input(string change)
    {
        using var r=new Rig();var descriptor=Path.Combine(r.Root,Descriptor);var json=JsonNode.Parse(File.ReadAllText(descriptor))!;
        if(change=="missing")File.Delete(descriptor);
        else if(change=="hash")File.AppendAllText(Path.Combine(r.Root,Generated)," ");
        else if(change=="duplicate")File.WriteAllText(descriptor,"""{"format":"scene2-runtime.v1","format":"scene2-runtime.v1","scene":"x","sceneHash":"x","assetsId":"x","assetsRevision":"x"}""");
        else
        {
            if(change=="generation")json["assetsId"]=new string('f',32);
            if(change=="path")json["scene"]="../outside.json";
            if(change=="device")json["scene"]=".bed/generated/scenes/NUL.json";
            if(change=="format")json["format"]="scene2-runtime.v999";
            if(change=="unflattened"){File.WriteAllText(Path.Combine(r.Root,Generated),r.Text);json["sceneHash"]=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(r.Text)));}
            File.WriteAllText(descriptor,json.ToJsonString());
        }
        using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(3,SceneStarter.Program.Run(["--project",r.Root,"--seconds","0"],output,error));Assert.NotEmpty(error.ToString());
        if(change=="unflattened")Assert.Contains("build",error.ToString(),StringComparison.OrdinalIgnoreCase);
    }
    [Theory][InlineData("source")][InlineData("settings")][InlineData("generated")][InlineData("stamp")][InlineData("policy")]
    public void Captured_v2_export_refuses_changed_saved_inputs_outputs_or_producing_policy(string change)
    {
        using var r=new Rig();var saved=r.Capture();
        if(change=="policy")
        {var inputs=r.Catalog.CurrentInputs().Select(i=>new AssetCatalog.Input(i.ProducingRecord with{AiUse=AiUse.Allowed},new byte[]{1,2,3},new byte[]{47,83,127,128},[],true,"self-authored-scene2","policy",texture:new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb))).ToArray();Assert.True(r.Catalog.Publish(r.Catalog.BeginRefresh(),inputs,out _));}
        else File.AppendAllText(Path.Combine(r.Root,change switch{"source"=>"data/level.json","settings"=>".bed/project.json","generated"=>Generated,_=>".bed/generated/scenes/build.stamp.json"})," ");
        Assert.Throws<InvalidOperationException>(()=>r.Declaration.Prepare(saved));Assert.Empty(r.Workspace.Project.Dirty);
    }
    [Theory][InlineData(AssetOrigin.Undeclared)][InlineData(AssetOrigin.Licensed)]
    public void V2_textures_retain_producing_rights_and_unretained_notices_refuse(AssetOrigin origin)
    {using var r=new Rig(origin:origin);var saved=r.Capture();Assert.Throws<InvalidOperationException>(()=>r.Declaration.Prepare(saved));}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Copied_game_executes_the_checked_flattened_level_without_authoring_or_original_assets(bool flat)
    {
        using var r=new Rig(flat);var saved=r.Capture();var plan=r.Declaration.Prepare(saved);Assert.Equal(flat?2:1,plan.Admission.SourceRecords.Length);
        Assert.DoesNotContain(plan.Admission.Source.Nodes,n=>n.Output.Destination.StartsWith("data/",StringComparison.Ordinal)||n.Output.Destination.StartsWith("assets/",StringComparison.Ordinal)||n.Output.Destination==".bed/project.json");
        var runtime=Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter");
        using var operation=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),r.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);
        var smoke=await VerifiedGameBuild.SmokeAsync(plan,receipt,operation.OutputManifest!,r.Declaration.Smoke(receipt.OutputDirectory,operation.OutputManifest!),TimeSpan.FromSeconds(8));Assert.NotNull(smoke.Build);
        var destination=Path.Combine(Path.GetTempPath(),"scene2-standalone-"+Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(smoke.Build.CopyTo(destination,out var why),why);Assert.False(Directory.Exists(Path.Combine(destination,"data")));Assert.False(Directory.Exists(Path.Combine(destination,"assets")));
            File.Delete(Path.Combine(r.Root,"data/level.json"));Directory.Move(Path.Combine(r.Root,".bed/cache"),Path.Combine(r.Root,"cache-offline"));
            var info=new ProcessStartInfo(Path.Combine(destination,"SceneStarter.exe")){WorkingDirectory=Path.GetTempPath(),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"--project",destination,"--seconds","0"})info.ArgumentList.Add(arg);
            using var child=Process.Start(info)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));}
            finally{if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}}
            Assert.Equal(0,child.ExitCode);Assert.Empty(await stderr);Assert.Contains("loaded saved level-v2",await stdout);output.WriteLine(await stdout);
            var loaded=SceneStarter.SavedScene.Open(destination);Assert.NotNull(loaded.Level2);Assert.Equal(flat?SceneSpace.TwoDXY:SceneSpace.ThreeD,loaded.Level2.Scene.Space);
            Assert.Equal(Scene2Resolver.Resolve(new Scene2Format().Read(r.Text)).Objects.Select(x=>JsonSerializer.Serialize(x)),loaded.Level2.Scene.Objects.Select(x=>JsonSerializer.Serialize(x)));
            if(flat)Assert.Equal(new byte[]{47,83,127,128},loaded.Textures[Image].Rgba);
            Assert.Equal(new[]{"Bed.Runtime.dll","Bed.dll","SceneStarter.dll"}.Order(),Directory.GetFiles(destination,"*.dll").Select(Path.GetFileName).Order());
            var files=Directory.GetFiles(destination,"*",SearchOption.AllDirectories);Assert.DoesNotContain(files,x=>Path.GetFileName(x).Contains("Editor",StringComparison.Ordinal));
            output.WriteLine(JsonSerializer.Serialize(files.Order(StringComparer.Ordinal).Select(x=>new{path=Path.GetRelativePath(destination,x),length=new FileInfo(x).Length,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(x)))})));
        }
        finally{if(Directory.Exists(destination))Directory.Delete(destination,true);}
    }

    [Fact]
    public void V1_project_without_capability_keeps_its_path_even_with_leftover_v2_outputs()
    {
        using var r=new Rig();File.WriteAllText(Path.Combine(r.Root,".bed/project.json"),"{}");
        File.WriteAllText(Path.Combine(r.Root,"data/level.json"),"""{"kind":"scene","version":1,"objects":[{"id":"legacy"}]}""");
        var saved=SceneStarter.SavedScene.Open(r.Root);Assert.Null(saved.Level2);Assert.Equal("legacy",Assert.Single(saved.Level.Scene.Objects).Id);
    }
    [Fact]
    public async Task Ordinary_Build_and_Play_regenerates_timestamp_preserving_saved_changes_and_runs_its_own_game()
    {
        using var r=new Rig();var installed=Directory.CreateDirectory(Path.Combine(r.Root,"game")).FullName;
        foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name),Path.Combine(installed,name));
        var defaults=r.Adapter.DefaultPlay(new(Path.Combine(r.Root,"data"),Path.Combine(r.Root,"schemas")){Root=r.Root})!;
        var settings=new PlaySettings(defaults.Program,["--project",".","--seconds","0"],defaults.WorkingFolder,defaults.Build);
        Assert.True(PlaySettingsResolver.TryResolve(r.Root,settings,out var launch,out var why),why);Assert.Equal(Path.Combine(installed,"SceneStarter.exe"),launch!.Build!.Executable);
        var source=Path.Combine(r.Root,"data/level.json");var time=File.GetLastWriteTimeUtc(source);var json=JsonNode.Parse(r.Text)!;json["objects"]![0]!["position"]![0]=11;
        File.WriteAllText(source,json.ToJsonString());File.SetLastWriteTimeUtc(source,time);
        using var host=new ProjectPlayHost(launch,TimeProvider.System,TimeSpan.FromSeconds(8));Assert.False(host.Start(false));Assert.Equal(ProjectPlayState.NeedsBuild,host.State);
        Assert.True(host.Start(false,true),host.Status);
        for(var step=0;step<4&&host.State is ProjectPlayState.Building or ProjectPlayState.Running;step++){await host.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(12));host.Update();}
        Assert.Equal(ProjectPlayState.Stopped,host.State);Assert.Equal(0,host.ExitCode);var logs=host.DrainLogs();
        Assert.Contains(logs,x=>x.Text.Contains("generated the saved scene-v2",StringComparison.Ordinal));Assert.Contains(logs,x=>x.Text.Contains("loaded saved level-v2",StringComparison.Ordinal));
        Assert.Equal(11,SceneStarter.SavedScene.Open(r.Root).Level2!.Scene.Objects.Single(o=>o.Object.Id=="room").Object.Position.X);
    }
    [Fact]
    public async Task Changed_generation_after_build_refuses_the_smoke_command_before_it_runs()
    {
        using var r=new Rig();var plan=r.Declaration.Prepare(r.Capture());var runtime=Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter");
        using var operation=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),r.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);
        File.AppendAllText(Path.Combine(r.Root,".bed/cache/current.json")," ");Assert.Throws<InvalidOperationException>(()=>r.Declaration.Smoke(receipt.OutputDirectory,operation.OutputManifest!));
    }

    [Fact]
    public void Custom_declared_source_is_checked_but_only_its_generated_runtime_files_are_exported()
    {
        using var r=new Rig(custom:true);var plan=r.Declaration.Prepare(r.Capture());Assert.NotNull(r.Workspace.Project.Find("custom.json"));
        Assert.DoesNotContain(plan.Admission.Source.Nodes,n=>n.Output.Destination==r.Source);Assert.Contains(plan.Admission.Source.Nodes,n=>n.Output.Destination==Generated);
        var saved=r.Capture();File.AppendAllText(Path.Combine(r.Root,r.Source)," ");Assert.Throws<InvalidOperationException>(()=>r.Declaration.Prepare(saved));
    }
}
