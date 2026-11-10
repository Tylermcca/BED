using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AssetStartupTests
{
    private const string Id = "11111111111111111111111111111111";
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("asset-start-").FullName;
        public string Assets => Path.Combine(Root,"assets");
        public string Source => Path.Combine(Assets,"piece.obj");
        public ProjectPaths Paths => new(Path.Combine(Root,"data"),Path.Combine(Root,"schemas"));
        public Fixture()
        {
            Directory.CreateDirectory(Paths.Data);Directory.CreateDirectory(Paths.Schemas);Directory.CreateDirectory(Assets);
            File.WriteAllText(Path.Combine(Paths.Data,"level.json"),new SceneFormat().Write(Scene.Empty));
            File.WriteAllText(Path.Combine(Paths.Schemas,"level.schema.json"),"{}");
            File.WriteAllText(Source,"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            var record=new AssetRecord(Id,"piece.obj",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Source))),"obj",1,
                JsonSerializer.SerializeToElement(new {scale=2}),new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllText(AssetRecord.SidecarFor(Source),record.Write());
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void Real_startup_derives_scaled_mesh_and_closes_admission_without_source_writes()
    {
        using var f=new Fixture();var sidecar=File.ReadAllText(AssetRecord.SidecarFor(f.Source));var source=File.ReadAllBytes(f.Source);
        var admitted=new AssetCatalogProvisioner().Open(f.Paths)!;
        Assert.Empty(admitted.Problems);var selection=admitted.Catalog.Select(Id);Assert.NotNull(selection);
        Assert.True(admitted.Catalog.TryResolve(selection,out var lease,out var why),why);
        var bytes=new byte[lease!.Length];lease.CopyTo(bytes);var mesh=DerivedMeshCodec.Decode(bytes);
        Assert.Contains(new Vector3(2,0,0),mesh.Positions);Assert.Equal(MeshUv0Origin.ObjBottomLeft,mesh.Uv0Origin);
        Assert.Equal(sidecar,File.ReadAllText(AssetRecord.SidecarFor(f.Source)));Assert.Equal(source,File.ReadAllBytes(f.Source));
        Assert.Equal(new[]{"cache"},Directory.GetFileSystemEntries(Path.Combine(f.Root,".bed")).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(f.Root,".bed","cache","current.json")));
        admitted.Dispose();Assert.False(admitted.Catalog.TryResolve(selection,out _,out _));
    }
    [Fact]
    public void Ordinary_no_UV_model_remains_valid_with_absent_stream_and_visible_diagnostic_data()
    {
        using var f=new Fixture();using var admitted=new AssetCatalogProvisioner().Open(f.Paths)!;
        Assert.Empty(admitted.Problems);
        Assert.Equal(AssetCatalog.State.Valid,admitted.Catalog.Resolve(Id).State);
        Assert.Contains(admitted.Warnings,w=>w.Contains("UV0 is absent",StringComparison.Ordinal));
        var selected=admitted.Catalog.Select(Id);Assert.NotNull(selected);
        Assert.True(admitted.Catalog.TryResolve(selected,out var lease,out var why),why);
        var bytes=new byte[lease!.Length];lease.CopyTo(bytes);
        var mesh=DerivedMeshCodec.Decode(bytes);Assert.Null(mesh.Uv0);
        Assert.Equal(3,mesh.Positions.Length);Assert.Equal(new[]{0,1,2},mesh.Indices);
    }
    [Fact]
    public void Malformed_sidecar_and_changed_source_are_reported_without_admission()
    {
        using var f=new Fixture();File.AppendAllText(f.Source,"# changed\n");File.WriteAllText(Path.Combine(f.Assets,"broken.obj")+AssetRecord.Suffix,"not json");
        using var admitted=new AssetCatalogProvisioner().Open(f.Paths)!;
        Assert.Null(admitted.Catalog.Select(Id));Assert.Contains(admitted.Problems,p=>p.Contains("changed since import"));
        Assert.Contains(admitted.Browser.General,i=>i.Message.Contains("broken.obj"));
    }
    [Fact]
    public void Missing_assets_is_optional_and_explicit_nested_root_is_used()
    {
        using var f=new Fixture();var nested=new ProjectPaths(Path.Combine(f.Root,"nested","data"),f.Paths.Schemas);
        Assert.Null(new AssetCatalogProvisioner().Open(nested));
        using var admitted=new AssetCatalogProvisioner().Open(nested,f.Assets)!;Assert.NotNull(admitted.Catalog.Select(Id));
    }
    [Fact]
    public void Startup_glTF_pins_actual_external_reads_and_decodes_the_derived_mesh()
    {
        using var f=new Fixture();var path=GltfReadSetTests.WriteFixture(f.Assets,3);var source=File.ReadAllBytes(path);
        const string other="22222222222222222222222222222222";
        var record=new AssetRecord(other,Path.GetFileName(path),Convert.ToHexStringLower(SHA256.HashData(source)),"gltf",1,
            JsonSerializer.SerializeToElement(new{}),new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
        File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());
        using var admitted=new AssetCatalogProvisioner().Open(f.Paths)!;
        Assert.Empty(admitted.Problems);Assert.NotNull(admitted.Catalog.Select(Id));
        var selection=admitted.Catalog.Select(other)!;Assert.NotNull(selection);
        Assert.True(admitted.Catalog.TryResolve(selection,out var lease,out _));var bytes=new byte[lease!.Length];lease.CopyTo(bytes);
        Assert.Equal(3,DerivedMeshCodec.Decode(bytes).Positions.Length);
        var read=Assert.Single(admitted.Catalog.CurrentInputs().Single(i=>i.Guid==other).SourceFiles);
        Assert.Equal("vertices.bin",read.RelativePath);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(f.Assets,"vertices.bin")))),read.SourceHash);
    }
    [Fact]
    public void Unknown_importer_is_failed_with_its_reason_not_a_raw_source_lease()
    {
        using var f=new Fixture();var sidecar=AssetRecord.SidecarFor(f.Source);
        var record=AssetRecord.Read(File.ReadAllText(sidecar)) with{Importer="unknown"};File.WriteAllText(sidecar,record.Write());
        using var admitted=new AssetCatalogProvisioner().Open(f.Paths)!;
        Assert.Equal(AssetCatalog.State.Failed,admitted.Catalog.Resolve(Id).State);
        Assert.Null(admitted.Catalog.Select(Id));Assert.Contains(admitted.Problems,p=>p.Contains("declared OBJ or glTF"));
        Assert.Equal(record.Write(),File.ReadAllText(sidecar));
    }
    [Fact]
    public void Material_is_preserved_now_that_startup_encodes_its_binding_instead_of_dropping_it()
    {
        using var f=new Fixture();var path=GltfReadSetTests.WriteFixture(f.Assets,3);
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        json["materials"]=System.Text.Json.Nodes.JsonNode.Parse("[{\"name\":\"paint\"}]");
        json["meshes"]![0]!["primitives"]![0]!["material"]=0;File.WriteAllText(path,json.ToJsonString());
        const string other="22222222222222222222222222222222";
        var record=new AssetRecord(other,Path.GetFileName(path),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),"gltf",1,
            JsonSerializer.SerializeToElement(new{}),new("self","self-authored","test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
        File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());using var admitted=new AssetCatalogProvisioner().Open(f.Paths)!;
        Assert.NotNull(admitted.Catalog.Select(Id));Assert.NotNull(admitted.Catalog.Select(other));
        Assert.Equal(AssetCatalog.State.Valid,admitted.Catalog.Resolve(other).State);Assert.Empty(admitted.Problems);
        var lease=admitted.Catalog.Resolve(other).Current!;var bytes=new byte[lease.Length];lease.CopyTo(bytes);
        var part=Assert.Single(DerivedMeshCodec.DecodeParts(bytes));Assert.NotNull(part.Material);
        Assert.Equal(0,part.Material.Ordinal);Assert.Equal(Vector4.One,part.Material.BaseColor);Assert.Null(part.Material.Texture);
        Assert.Equal(record.Write(),File.ReadAllText(AssetRecord.SidecarFor(path)));
    }
    private sealed class ObservedProvisioner : IAssetCatalogProvisioner
    {
        public readonly List<ProjectAssetCatalog> Admissions = [];
        public ProjectAssetCatalog? Open(ProjectPaths project,string? assetsFolder=null)
        {
            var result=new AssetCatalogProvisioner().Open(project,assetsFolder);
            if(result is not null)Admissions.Add(result);
            return result;
        }
    }
    [Fact]
    public void Cancelled_dirty_replacement_retains_admission_and_successful_replacement_closes_only_old_one()
    {
        using var first=new Fixture();using var second=new Fixture();
        using var host=new HeadlessHost(1000,700){Drawing=false};
        var observed=new ObservedProvisioner();
        using var launcher=Brutal.Authoring.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(first.Root,"recent.json")))) ;
        launcher.AssetCatalogProvisioner=observed;Assert.True(launcher.OpenFolder(first.Root),launcher.Said);
        var original=Assert.Single(observed.Admissions);var selected=original.Catalog.Select(Id)!;
        var file=launcher.Workspace!.Project.Find("level.json")!;Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new Brutal.Authoring.Data.SetValue("/extra",JsonSerializer.SerializeToElement(1))));
        Assert.False(launcher.OpenFolder(second.Root));Assert.True(launcher.SaveChangesPending);Assert.Single(observed.Admissions);
        Assert.False(launcher.AnswerSaveChanges(SaveChangesChoice.Cancel));Assert.True(original.Catalog.TryResolve(selected,out _,out _));
        Assert.False(launcher.OpenFolder(second.Root));Assert.True(launcher.AnswerSaveChanges(SaveChangesChoice.DontSave));
        Assert.Equal(2,observed.Admissions.Count);Assert.False(original.Catalog.TryResolve(selected,out _,out _));
        var current=observed.Admissions[1];Assert.NotNull(current.Catalog.Select(Id));
        Assert.True(launcher.Close());Assert.Null(current.Catalog.Select(Id));
    }
    [Fact]
    public void Program_startup_and_Files_keys_open_a_real_asset_picker_without_injected_catalog()
    {
        using var f=new Fixture();File.WriteAllText(Path.Combine(f.Assets,"broken.obj")+AssetRecord.Suffix,"not json");
        using var host=new HeadlessHost(1600,1000){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        using var launcher=Brutal.Authoring.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(f.Root,"recent.json"))));
        var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);
        Assert.Empty(loaded.Problems);launcher.Adapter=Assert.Single(loaded.Loaded);
        Assert.True(launcher.OpenFolder(f.Root),launcher.Said);host.Frames(launcher.Draw,4);
        void Key(ImGuiKey key){host.Press(launcher.Draw,key);host.Frames(launcher.Draw,2);}
        void Reach(string label,UiRole role,bool contains=false)
        {
            for(var i=0;i<60;i++)
            {if(Ui.Snapshot().Nodes.Any(n=>n.Focused&&n.Role==role&&(contains?n.Label.Contains(label):n.Label==label)))return;Key(ImGuiKey.Tab);}
            Assert.Fail("Keys did not reach "+label);
        }
        Assert.Null(launcher.Workspace!.SceneWindow);Reach("level.json",UiRole.ListItem,true);Key(ImGuiKey.Enter);
        var scene=launcher.Workspace.SceneWindow!;Assert.NotNull(scene);Key(ImGuiKey.Insert);
        Assert.True(scene.Editor.IsOpen("scene.assets"));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("broken.obj"));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("UV0 is absent"));
        Reach("Problems only",UiRole.Checkbox);Key(ImGuiKey.Tab);Key(ImGuiKey.Enter);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Focused&&n.Label=="piece.obj");
        Key(ImGuiKey.Enter);Key(ImGuiKey.Escape);Reach("Add selected object",UiRole.Button);Key(ImGuiKey.Enter);
        Assert.Equal(Id,Assert.Single(scene.Editor.Session.Current.Objects).Asset);Assert.Equal(1,scene.Editor.Session.History.UndoCount);
        Assert.Equal(new SceneFormat().Write(Scene.Empty),File.ReadAllText(Path.Combine(f.Paths.Data,"level.json")));
    }
}
