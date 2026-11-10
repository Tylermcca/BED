using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SampleExportDeclarationTests
{
    private const string Id="11111111111111111111111111111111";
    private const string Text="""{"kind":"scene","version":1,"objects":[{"id":"kept","asset":"11111111111111111111111111111111","position":[3,4,5]}]}""";
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("sample-export-declaration-").FullName;
        private readonly HeadlessHost host=new(){Drawing=false};
        public readonly DataWorkspace Workspace;
        public readonly AssetCatalog Catalog=new();
        public readonly AssetRecord Record;
        public readonly AssetCatalog.Input Asset;
        public readonly IProjectAdapter Adapter;
        public readonly GameExportDeclaration Declaration;
        private readonly byte[] source=[1,2,3],derived=DerivedMeshCodec.Encode(new MeshData([System.Numerics.Vector3.Zero,System.Numerics.Vector3.UnitX,System.Numerics.Vector3.UnitY],[0,1,2]));
        public Rig(AssetOrigin origin=AssetOrigin.Ours)
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;var assets=Directory.CreateDirectory(Path.Combine(Root,"assets")).FullName;
            File.WriteAllText(Path.Combine(data,"level.json"),Text);
            Record=new(Id,"self-authored.bin",Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{scale=2}),
                new("Fixture author",origin==AssetOrigin.Licensed?"MIT":"self-authored","script",Declared:origin),AiUse.Forbidden);
            Asset=new(Record,source,derived,[],true,"self-authored","");Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[Asset],out var why),why);
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.BindSceneAssets(new AssetBrowser(assets),Catalog));
            var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);Adapter=Assert.Single(loaded.Loaded);
            using(var publisher=new AssetCatalogPublisher(Root))Assert.True(publisher.Publish(publisher.Prepare("fixture",new Dictionary<string,byte[]>{{Id+".bin",derived}}),out _));
            Declaration=Assert.IsType<GameExportDeclaration>(Adapter.Export(Workspace));
        }
        public PlaySnapshot Capture()=>PlaySnapshot.Create(new("level.json",12,Text,"unsaved draft"),PlayInputChoice.Saved,Declaration.ConvertSaved).Snapshot!;
        public void ChangePolicy()=>Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new(Record with{AiUse=AiUse.Allowed},source,derived,[],true,"self-authored","")],out _));
        public void Dispose(){Workspace.Dispose();Catalog.Dispose();host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Fact] public void Producing_record_accessor_is_owned_and_does_not_change_catalog_policy()
    {
        using var rig=new Rig();var before=rig.Catalog.Select(Id)!;var first=rig.Asset.ProducingRecord;var second=rig.Asset.ProducingRecord;
        Assert.NotSame(rig.Record,first);Assert.NotSame(first,second);Assert.Equal(2,first.Settings.GetProperty("scale"u8).GetInt32());
        first=first with{AiUse=AiUse.Allowed,Provenance=first.Provenance with{Author="changed copy"}};
        Assert.Equal(AiUse.Forbidden,rig.Asset.ProducingRecord.AiUse);Assert.Equal("Fixture author",rig.Asset.ProducingRecord.Provenance.Author);
        Assert.True(rig.Catalog.TryResolve(before,out _,out _));Assert.Equal(before.PolicyRevision,rig.Catalog.Select(Id)!.PolicyRevision);
    }
    [Fact] public void Adapter_declares_real_runtime_check_and_catalog_origin_for_exact_saved_asset_pins()
    {
        using var rig=new Rig();var saved=rig.Capture();Assert.NotNull(saved);var plan=rig.Declaration.Prepare(saved);
        Assert.Null(plan.SavedPayloadOutput);Assert.Equal(saved.CopyPayload(),plan.Admission.SavedInput.CopyPayload());
        var asset=Assert.Single(plan.Admission.Source.Nodes,n=>n.Source?.AssetGuid==Id);Assert.Equal(rig.Record.SourceHash,asset.Source!.SourceHash);
        Assert.Equal(rig.Record.Guid,Assert.Single(plan.Admission.SourceRecords).Guid);Assert.Equal(rig.Record.SourceHash,plan.Admission.SourceRecords[0].SourceHash);Assert.True(plan.Admission.Origin.Accepted);
        var check=Assert.Single(plan.Admission.Checks);Assert.Equal("scene-starter.runtime-input",check.CheckId);Assert.Equal(ExportCheckVerdict.Passed,check.Verdict);Assert.Equal(plan.Admission.Source.Hash,check.ManifestHash);
        Assert.Contains("README.txt",plan.Outputs.Paths);Assert.Contains("SceneStarter.exe",plan.Outputs.Paths);Assert.Contains("data/level.json",plan.Outputs.Paths);Assert.Contains(".bed/cache/current.json",plan.Outputs.Paths);Assert.DoesNotContain("scene.json",plan.Outputs.Paths);Assert.DoesNotContain("payload.bin",plan.Outputs.Paths);
        var spec=Assert.IsType<GameBuildSpec>(rig.Adapter.Build(rig.Workspace));Assert.True(spec.AcceptsSavedInput);Assert.Equal(new[]{"--build","{input}","{output}"},spec.Arguments);
        Assert.Empty(rig.Workspace.Project.Dirty);Assert.Equal(Text,File.ReadAllText(Path.Combine(rig.Root,"data","level.json")));
    }
    [Fact] public async Task Actual_adapter_build_smoke_and_copy_preserve_its_saved_generation_at_every_declared_path()
    {
        using var rig=new Rig();var plan=rig.Declaration.Prepare(rig.Capture());var runtime=Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter");
        using var operation=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),rig.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);Assert.Equal(0,receipt.ExitCode);
        foreach(var node in plan.Admission.Source.Nodes)
        {var expected=new byte[node.Length];node.CopyTo(expected);Assert.Equal(expected,File.ReadAllBytes(Path.Combine(receipt.OutputDirectory,node.Output.Destination)));}
        Assert.Equal(plan.Outputs.Paths.Order(),receipt.Outputs.Select(o=>o.Path).Order());
        var smoke=await VerifiedGameBuild.SmokeAsync(plan,receipt,operation.OutputManifest!,rig.Declaration.Smoke(receipt.OutputDirectory,operation.OutputManifest!),TimeSpan.FromSeconds(8));
        Assert.NotNull(smoke.Build);Assert.Contains(smoke.Logs,line=>line.Text=="SceneStarter loaded saved level: 1 objects, 1 meshes.");
        var destination=Path.Combine(rig.Root,"standalone");Assert.True(smoke.Build.CopyTo(destination,out var why),why);
        Assert.Equal("kept",Assert.Single(SceneStarter.SavedScene.Open(destination).Level.Scene.Objects).Id);
        Assert.Equal(Text,File.ReadAllText(Path.Combine(destination,"data","level.json")));
    }
    [Fact] public async Task Textured_crate_export_retains_the_same_published_model_and_texture_without_source_files()
    {
        using var owned=new CapturedExportTests.Owned();using var headless=new HeadlessHost(){Drawing=false};
        var data=Directory.CreateDirectory(Path.Combine(owned.Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(owned.Root,"schemas")).FullName;
        var assets=Directory.CreateDirectory(Path.Combine(owned.Root,"assets")).FullName;
        string[] names=["textured-crate.obj","textured-crate.mtl","textured-crate.png","textured-crate.obj.asset.json","textured-crate.png.asset.json"];
        var before=names.ToDictionary(name=>name,name=>File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"samples","scene-starter","assets",name)));
        foreach(var (name,bytes) in before)File.WriteAllBytes(Path.Combine(assets,name),bytes);
        const string model="c8a7e000000000000000000000000001",texture="c8a7e000000000000000000000000002";
        var text=Text.Replace(Id,model);File.WriteAllText(Path.Combine(data,"level.json"),text);
        using var admitted=new AssetCatalogProvisioner().Open(new(data,schemas))!;Assert.Empty(admitted.Problems);
        using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.BindSceneAssets(admitted.Browser,admitted.Catalog));
        var adapters=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(adapters.Problems);
        var declaration=Assert.Single(adapters.Loaded).Export(workspace)!;
        var saved=PlaySnapshot.Create(new("level.json",1,text,"unsaved draft"),PlayInputChoice.Saved,declaration.ConvertSaved).Snapshot!;
        Assert.NotNull(saved);var plan=declaration.Prepare(saved);Assert.Equal(2,plan.Admission.SourceRecords.Length);
        var runtime=Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter");
        using var operation=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),owned.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);
        var original=Brutal.Authoring.Runtime.ProjectAssets.Open(owned.Root);var exported=SceneStarter.SavedScene.Open(receipt.OutputDirectory);
        Assert.Equal(original.Id,exported.Assets.Id);Assert.Equal(original.ReadAsset(model),exported.Assets.ReadAsset(model));Assert.Equal(original.ReadAsset(texture),exported.Assets.ReadAsset(texture));
        Assert.Equal(8,exported.Assets.ReadTexture(Guid.ParseExact(texture,"N")).Width);
        var smoke=await VerifiedGameBuild.SmokeAsync(plan,receipt,operation.OutputManifest!,declaration.Smoke(receipt.OutputDirectory,operation.OutputManifest!),TimeSpan.FromSeconds(8));Assert.NotNull(smoke.Build);
        Assert.Contains(smoke.Logs,line=>line.Text=="SceneStarter loaded saved level: 1 objects, 1 meshes.");
        Assert.DoesNotContain(receipt.Outputs,item=>item.Path.StartsWith("assets/",StringComparison.Ordinal)||item.Path is "scene.json" or "payload.bin");
        foreach(var (name,bytes) in before)Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(assets,name)));
    }
    [Fact] public void Saved_generation_change_after_capture_refuses_before_build_admission()
    {
        using var rig=new Rig();var saved=rig.Capture();File.AppendAllText(Path.Combine(rig.Root,".bed","cache","current.json")," ");
        var error=Assert.Throws<InvalidOperationException>(()=>rig.Declaration.Prepare(saved));Assert.Contains("Saved export files changed",error.Message);
    }
    [Fact] public void Policy_change_after_capture_refuses_before_an_export_plan_is_admitted()
    {
        using var rig=new Rig();var saved=rig.Capture();rig.ChangePolicy();
        var error=Assert.Throws<InvalidOperationException>(()=>rig.Declaration.Prepare(saved));Assert.Contains("catalog changed",error.Message);Assert.Empty(rig.Workspace.Project.Dirty);
    }
    [Theory][InlineData(AssetOrigin.Undeclared)][InlineData(AssetOrigin.Licensed)]
    public void Missing_origin_or_unretained_attribution_is_not_relabelled_game_owned(AssetOrigin origin)
    {
        using var rig=new Rig(origin);var saved=rig.Capture();Assert.NotNull(saved);
        var error=Assert.Throws<InvalidOperationException>(()=>rig.Declaration.Prepare(saved));Assert.NotEmpty(error.Message);
        if(origin==AssetOrigin.Licensed)Assert.Contains("retain additional asset attributions",error.Message);
        Assert.Empty(rig.Workspace.Project.Dirty);
    }
    private sealed class NoExportAdapter:IProjectAdapter
    {
        public string Name=>"Existing adapter";
        public DataProject Open(ProjectPaths project)=>throw new NotSupportedException();
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
    [Fact] public void Existing_adapter_gets_no_invented_build_or_export_declaration()
    {
        using var rig=new Rig();IProjectAdapter existing=new NoExportAdapter();
        Assert.Null(existing.Build(rig.Workspace));Assert.Null(existing.Export(rig.Workspace));Assert.Empty(rig.Workspace.Project.Dirty);
    }
}
