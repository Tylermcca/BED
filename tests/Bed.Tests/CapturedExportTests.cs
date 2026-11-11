using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;
public sealed class CapturedExportTests
{
    internal static PlaySnapshot Input(bool assets=false)
    {
        const string id="c0bec0bec0bec0bec0bec0bec0bec0be";
        var text=assets?"""{"kind":"scene","version":1,"objects":[{"id":"edited","asset":"c0bec0bec0bec0bec0bec0bec0bec0be","position":[9,8,7]}]}""":
            """{"kind":"scene","version":1,"objects":[{"id":"edited","position":[9,8,7]}]}""";
        byte[] derived=DerivedMeshCodec.Encode(new MeshData([System.Numerics.Vector3.Zero,System.Numerics.Vector3.UnitX,System.Numerics.Vector3.UnitY],[0,1,2]));
        var bytes=assets?JsonSerializer.SerializeToUtf8Bytes(new{scene=text,assets=new[]{new{guid=id,sourceHash=new string('a',64),contentRevision=new string('b',64),policyRevision=new string('c',64),derivedHash=Hash(derived),derived}}}):Encoding.UTF8.GetBytes(text);
        return PlaySnapshot.Create(new("level.json",19,text,"unsaved draft"),PlayInputChoice.Saved,_=>new(assets?SceneStarter.ScenePayload.AssetFormat:SceneStarter.ScenePayload.Format,bytes)).Snapshot!;
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static GameExportPlan Plan(PlaySnapshot input,bool sample)
    {
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        if(sample)
        {
            using var project=new Owned();
            using var json=JsonDocument.Parse(input.CopyPayload());
            var text=input.Format==SceneStarter.ScenePayload.AssetFormat?json.RootElement.GetProperty("scene").GetString()!:Encoding.UTF8.GetString(input.CopyPayload());
            var artifacts=new Dictionary<string,byte[]>();
            if(input.Format==SceneStarter.ScenePayload.AssetFormat)
                foreach(var asset in json.RootElement.GetProperty("assets").EnumerateArray())artifacts.Add(asset.GetProperty("guid").GetString()+".bin",asset.GetProperty("derived").GetBytesFromBase64());
            using(var publisher=new AssetCatalogPublisher(project.Root))Assert.True(publisher.Publish(publisher.Prepare("fixture",artifacts),out _));
            var runtime=Bed.Runtime.ProjectAssets.Open(project.Root);var prefix=".bed/cache/generations/"+runtime.Id+"/";
            files.Add("data/level.json",Encoding.UTF8.GetBytes(text));
            foreach(var relative in new[]{".bed/cache/current.json",prefix+"manifest.json",prefix+"owner.txt"}.Concat(runtime.ArtifactNames.Select(name=>prefix+name)))
                files.Add(relative,File.ReadAllBytes(Path.Combine(project.Root,relative)));
        }
        else files.Add("result.bin",input.CopyPayload());
        var source=files.Select((file,index)=>new GameContentNode(new("input-"+index,file.Key,input.Format),file.Value,Hash(file.Value),[],true,ExportSource.GameOwned)).ToArray();
        var admission=SavedBuildAdmission.Prepare(input,GameBuildProfile.Create("fixture","Fixture.csproj",GameBuildTarget.WindowsX64,source.Select(n=>n.Output)),"self-authored-fixture",true,
            _=>source,["scene.read"],m=>[new(m.Hash,"scene.read",ExportCheckVerdict.Passed,DateTimeOffset.UtcNow,TimeSpan.Zero)],[]);
        var paths=sample?SceneStarter.ExportInventory.RuntimeFiles.Concat(["README.txt"]).Concat(files.Keys):files.Keys;
        var mappings=paths.Select((path,index)=>new GameContentMapping("output-"+index,path,"fixture-runtime")).ToArray();
        return new(admission,admission.DeclareOutputs(GameBuildProfile.Create("fixture-output","Fixture.csproj",GameBuildTarget.WindowsX64,mappings),mappings.Select(map=>new BuildOutputItem(map,[])),[]));
    }
    internal sealed class Owned:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("captured-export-").FullName;
        public void Dispose(){var full=Path.GetFullPath(Root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("captured-export-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture root.");Directory.Delete(full,true);}
    }
    [Fact] public void Input_manifest_pins_exact_checked_payload_and_cannot_be_replaced_in_place()
    {
        using var owned=new Owned();var saved=Input(true);var admission=Plan(saved,false).Admission;var directory=BuildInputStaging.Write(owned.Root,admission);
        using(var json=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory,"manifest.json"))))
        {var root=json.RootElement;Assert.Equal(saved.Hash,root.GetProperty("snapshotHash").GetString());Assert.Equal(saved.Format,root.GetProperty("format").GetString());var entry=Assert.Single(root.GetProperty("files").EnumerateArray());Assert.Equal(2,root.GetProperty("version").GetInt32());Assert.Equal(admission.Source.Hash,root.GetProperty("sourceManifestHash").GetString());Assert.Equal("result.bin",entry.GetProperty("path").GetString());Assert.Equal(Hash(saved.CopyPayload()),entry.GetProperty("sha256").GetString());Assert.Equal(saved.Length,entry.GetProperty("length").GetInt64());}
        Assert.Equal(saved.CopyPayload(),File.ReadAllBytes(Path.Combine(directory,"result.bin")));BuildInputStaging.Verify(directory,admission);
        Assert.Throws<IOException>(()=>BuildInputStaging.Write(owned.Root,admission));
        File.WriteAllBytes(Path.Combine(directory,"result.bin"),[99]);Assert.Throws<IOException>(()=>BuildInputStaging.Verify(directory,admission));
    }
    [Theory][InlineData("extra")][InlineData("missing")][InlineData("changed")][InlineData("directory")][InlineData("manifest")]
    public void Saved_file_transport_rejects_additions_omissions_mutation_and_manifest_replacement(string mode)
    {
        using var owned=new Owned();var plan=Plan(Input(true),true);var directory=BuildInputStaging.Write(owned.Root,plan.Admission);
        var asset=Assert.Single(plan.Admission.Source.Nodes,n=>n.Output.Destination.EndsWith(".bin",StringComparison.Ordinal));
        var path=Path.Combine(directory,asset.Output.Destination);
        switch(mode)
        {
            case "extra":File.WriteAllText(Path.Combine(directory,"extra.txt"),"undeclared");break;
            case "missing":File.Delete(path);break;
            case "changed":File.WriteAllBytes(path,[99]);break;
            case "directory":Directory.CreateDirectory(Path.Combine(directory,"extra-directory"));break;
            case "manifest":File.AppendAllText(Path.Combine(directory,"manifest.json")," ");break;
        }
        Assert.Throws<IOException>(()=>BuildInputStaging.Verify(directory,plan.Admission));
    }
    [Fact] public void Sample_build_requires_every_generation_file_in_the_transport_manifest()
    {
        using var owned=new Owned();var plan=Plan(Input(true),true);var directory=BuildInputStaging.Write(owned.Root,plan.Admission);
        var path=Path.Combine(directory,"manifest.json");var manifest=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var files=manifest["files"]!.AsArray();
        var artifact=Assert.Single(files,n=>n!["path"]!.GetValue<string>().EndsWith(".bin",StringComparison.Ordinal));files.Remove(artifact);
        File.WriteAllText(path,manifest.ToJsonString());
        var error=Assert.Throws<InvalidOperationException>(()=>SceneStarter.ExportBuild.ReadInput(directory));Assert.Contains("closure",error.Message);
    }
    [Fact] public void Export_without_input_capability_refuses_before_creating_resources()
    {
        using var owned=new Owned();var plan=Plan(Input(),false);
        var error=Assert.Throws<InvalidOperationException>(()=>new ExportBuildHost(1024,16,TimeSpan.FromSeconds(5)).Start(new(@"C:\fixture\builder.exe",owned.Root,["{output}"]),owned.Root,false,plan));
        Assert.Contains("does not take BED's saved level",error.Message);Assert.Empty(Directory.EnumerateFileSystemEntries(owned.Root));
    }
    [Theory][InlineData("alter-captured","doesn't match the saved files")][InlineData("tamper-captured","Captured Saved input changed")]
    public async Task Changed_build_output_or_transport_never_acquires_a_verified_manifest(string mode,string reason)
    {
        using var owned=new Owned();var plan=Plan(Input(),false);var helper=Path.Combine(AppContext.BaseDirectory,"LinkedBuildTestGame","LinkedBuildTestGame.exe");
        using var operation=new ExportBuildHost(2048,32,TimeSpan.FromSeconds(5)).Start(new(helper,owned.Root,[mode,"{output}","{input}"]),owned.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(8));Assert.False(receipt.Succeeded);Assert.Equal(0,receipt.ExitCode);Assert.Contains(reason,receipt.Reason);Assert.Null(operation.OutputManifest);Assert.Empty(receipt.Outputs);Assert.Equal(Input().Hash,plan.Admission.SnapshotHash);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Actual_sample_build_copies_all_saved_files_then_unpacked_runtime_uses_the_Play_reader(bool assets)
    {
        using var owned=new Owned();var saved=Input(assets);var plan=Plan(saved,true);var runtime=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
        var canned=File.ReadAllBytes(Path.Combine(runtime,"scene.json"));
        using var operation=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),owned.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);Assert.Equal(0,receipt.ExitCode);
        foreach(var node in plan.Admission.Source.Nodes)
        {var expected=new byte[node.Length];node.CopyTo(expected);Assert.Equal(expected,File.ReadAllBytes(Path.Combine(receipt.OutputDirectory,node.Output.Destination)));}
        Assert.False(File.Exists(Path.Combine(receipt.OutputDirectory,"scene.json")));Assert.False(File.Exists(Path.Combine(receipt.OutputDirectory,"payload.bin")));
        Assert.Equal(canned,File.ReadAllBytes(Path.Combine(runtime,"scene.json")));Assert.Equal(plan.Admission.Source.Hash,operation.OutputManifest!.SourceManifestHash);
        var parsed=SceneStarter.SavedScene.Open(receipt.OutputDirectory);var item=Assert.Single(parsed.Level.Scene.Objects);Assert.Equal("edited",item.Id);Assert.Equal(new System.Numerics.Vector3(9,8,7),item.Position);Assert.Equal(assets?1:0,parsed.Meshes.Count);
        var start=new ProcessStartInfo(Path.Combine(receipt.OutputDirectory,"SceneStarter.exe")){WorkingDirectory=receipt.OutputDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"--project",".","--seconds","0"})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));Assert.Equal(0,process.ExitCode);Assert.Contains($"SceneStarter loaded saved level: 1 objects, {(assets?1:0)} meshes.",await output);Assert.Equal("",await error);}
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        Assert.Equal(plan.Outputs.Paths.Order(),receipt.Outputs.Select(item=>item.Path).Order());
    }
    [Fact] public async Task Deployed_runtime_without_a_canned_scene_builds_only_the_admitted_saved_files()
    {
        using var owned=new Owned();var runtime=Directory.CreateDirectory(Path.Combine(owned.Root,"deployed-runtime")).FullName;
        var original=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
        foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)File.Copy(Path.Combine(original,name),Path.Combine(runtime,name));
        var plan=Plan(Input(true),true);
        using var operation=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),owned.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);
        Assert.Equal("edited",Assert.Single(SceneStarter.SavedScene.Open(receipt.OutputDirectory).Level.Scene.Objects).Id);
        Assert.False(File.Exists(Path.Combine(runtime,"scene.json")));Assert.False(File.Exists(Path.Combine(receipt.OutputDirectory,"scene.json")));
        Assert.Equal(SceneStarter.ExportInventory.RuntimeFiles.Order(),Directory.GetFiles(runtime).Select(Path.GetFileName).Order());
        Assert.Equal(plan.Outputs.Paths.Order(),receipt.Outputs.Select(item=>item.Path).Order());
    }
}
