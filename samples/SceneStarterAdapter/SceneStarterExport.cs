using System.Security.Cryptography;
using System.Text;
using Bed;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Runtime;
namespace SceneStarterAdapter;

public sealed partial class SceneStarterAdapter
{
    private static readonly string[] ExportRuntime=["SceneStarter.exe","SceneStarter.dll","SceneStarter.deps.json","SceneStarter.runtimeconfig.json","Bed.dll","Bed.Runtime.dll","LICENSE.txt","NOTICE.txt","README.txt"];
    private const string SavedProjectFormat="scene-starter.saved-project.v1";
    /// <summary>Packages only the installed runtime and explicitly captured saved project files.</summary>
    public GameBuildSpec Build(DataWorkspace workspace)
    {
        var root=savedProjectRoot??Path.GetDirectoryName(Path.GetFullPath(workspace.Project.DataFolder))!;
        var physics=SceneSettings(root)?.SupportsPhysics==true;var program=physics?"PhysicsStarter":"SceneStarter";
        var directory=Path.Combine(root,"game");var exportRuntime=Path.Combine(directory,"export");
        if(File.Exists(Path.Combine(exportRuntime,program+".exe")))directory=exportRuntime;
        if(!File.Exists(Path.Combine(directory,program+".exe")))directory=Path.Combine(AppContext.BaseDirectory,"samples",program);
        return new(Path.Combine(directory,program+".exe"),directory,["--build","{input}","{output}"]);
    }
    /// <summary>Captures the saved level and exactly the currently published generation, with producing asset policy.</summary>
    public GameExportDeclaration Export(DataWorkspace workspace)
    {
        Captured? captured=null,admitted=null;string? admittedSourceHash=null;
        return new(input=>
        {
            captured=null;admitted=null;admittedSourceHash=null;
            try
            {
                var root=savedProjectRoot??Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.Project.DataFolder)))!;
                captured=CaptureProject(root,input.Text,workspace.SceneAssets);
                return new(SavedProjectFormat,Encoding.UTF8.GetBytes(input.Text));
            }
            catch(Exception error)when(error is IOException or InvalidOperationException or ArgumentException or DocumentFormatException or InvalidDocumentException)
            {return new(SavedProjectFormat,null,error.Message);}
        },saved=>
        {
            var capture=captured??throw new InvalidOperationException("Capture Saved input before exporting.");
            if(saved.Format!=SavedProjectFormat||Hash(saved.CopyPayload())!=capture.PayloadHash)throw new InvalidOperationException("The export capture belongs to another Saved level.");
            capture.Current();
            var project=capture.Physics?"samples/PhysicsStarter/PhysicsStarter.csproj":"samples/SceneStarter/SceneStarter.csproj";
            var sourceProfile=GameBuildProfile.Create(capture.Physics?"physics-starter-source":"scene-starter-source",project,GameBuildTarget.WindowsX64,capture.Nodes.Select(n=>n.Output));
            var admission=SavedBuildAdmission.Prepare(saved,sourceProfile,"scene-starter",true,_=>capture.Nodes,
                ["scene-starter.runtime-input"],manifest=>
                {
                    capture.Current();
                    return [new(manifest.Hash,"scene-starter.runtime-input",ExportCheckVerdict.Passed,DateTimeOffset.UtcNow,TimeSpan.Zero)];
                },capture.Records);
            if(!admission.Origin.Notices.IsEmpty)throw new InvalidOperationException("This sample build cannot yet retain additional asset attributions.");
            var runtimeFiles=capture.Physics?SceneStarter.PhysicsRuntimeInventory.Files.Append("README.txt"):ExportRuntime;
            var outputs=runtimeFiles.Select((path,index)=>new GameContentMapping("runtime-"+index,path,"scene-starter.runtime"))
                .Concat(capture.Nodes.Select(n=>n.Output)).ToArray();
            var profile=GameBuildProfile.Create(capture.Physics?"physics-starter-runtime":"scene-starter-runtime",project,GameBuildTarget.WindowsX64,outputs);
            capture.Current();admitted=capture;admittedSourceHash=admission.Source.Hash;return new(admission,admission.DeclareOutputs(profile,outputs.Select(m=>new BuildOutputItem(m,[])),[]));
        },(directory,outputs)=>
        {
            if(admitted is null||outputs.SourceManifestHash!=admittedSourceHash)throw new InvalidOperationException("The unpacked build belongs to another Saved capture.");
            admitted.Current();return new(Path.Combine(directory,admitted.Physics?"PhysicsStarter.exe":"SceneStarter.exe"),directory,["--project",".","--seconds","0"]);
        });
    }
    private sealed record Captured(string PayloadHash,GameContentNode[] Nodes,AssetRecord[] Records,Action Current,bool Physics);
    private static Captured CaptureProject(string root,string savedText,AssetCatalog? catalog)
    {
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);var guards=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        void Read(string relative,int maximum,bool guard=false)
        {
            var path=Path.Combine(root,relative);
            for(var at=Path.GetFullPath(path);at is not null;at=Path.GetDirectoryName(at))
                if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked export inputs are not supported.");
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(stream.Length>maximum)throw new IOException("Saved export input exceeds its byte budget.");
            var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);(guard?guards:files).Add(relative,bytes);
        }
        ProjectAssets runtime;Action freshness=()=>{};var physics=false;
        if(SceneSettings(root) is {} settings)
        {
            physics=settings.SupportsPhysics;
            Read(settings.Source,32*1024*1024,true);Read(".bed/project.json",Scene2BuildSettings.MaxProjectBytes,true);
            var text=new UTF8Encoding(false,true).GetString(guards[settings.Source]);
            if(text!=savedText)throw new InvalidOperationException("The saved level changed; capture Saved input again.");
            var defaults=new PlaySettings(build:new(physics?"game/PhysicsStarter.exe":"game/SceneStarter.exe",["--compile-scene2","."],physics?SceneStarter.PhysicsRuntimeInventory.BuildInputs(settings.Source):[settings.Source,".bed/project.json",".bed/cache/current.json"]){Stamp=".bed/generated/scenes/build.stamp.json"});
            var play=PlaySettings.ReadProject(new UTF8Encoding(false,true).GetString(guards[".bed/project.json"]),defaults)!;
            var build=play.Build??throw new InvalidOperationException("Declare the scene build and its content stamp before export.");
            var stamp=build.Stamp??throw new InvalidOperationException("Declare a content stamp before exporting scene-v2.");
            freshness=()=>{if(!ProjectBuildStamp.Current(root,stamp,build.Inputs,out var why))throw new InvalidOperationException("Build the saved scene before exporting: "+why);};
            freshness();Read(stamp,ProjectBuildStamp.MaxStampBytes,true);
            var prepared=SceneStarter.Scene2RuntimeInput.Open(root,physics);runtime=prepared.Assets;
            if(prepared.ScenePath!=settings.Generated||new Scene2Format().Write(SceneStarter.ScenePhysicsPreparation.Prepare(new Scene2Format().Read(text),runtime,physics))!=new UTF8Encoding(false,true).GetString(prepared.SceneBytes))
                throw new InvalidOperationException("The generated scene differs from Saved input; build again before exporting.");
            files.Add(prepared.ScenePath,prepared.SceneBytes);files.Add(SceneStarter.Scene2RuntimeInput.Descriptor,prepared.DescriptorBytes);
        }
        else
        {
            runtime=ProjectAssets.Open(root);Read("data/level.json",1024*1024);
            var text=new UTF8Encoding(false,true).GetString(files["data/level.json"]);
            if(text!=savedText)throw new InvalidOperationException("The saved level changed; capture Saved input again.");
            var level=SceneRuntimeReader.Read(text,assetExists:runtime.ContainsAsset);
            if(level.AssetIssues.Count!=0)throw new InvalidOperationException("The saved level references an asset outside its published generation.");
            foreach(var id in level.Scene.Objects.Select(o=>o.Asset).OfType<string>().Distinct(StringComparer.Ordinal))
                foreach(var part in runtime.ReadMesh(id).Parts)
                    if(part.Material?.Texture is {} texture)_=runtime.ReadTexture(texture);
        }
        if(runtime.ArtifactNames.Count+files.Count+3>1024)throw new InvalidOperationException("Saved export exceeds the 1024-file source inventory bound.");
        var prefix=".bed/cache/generations/"+runtime.Id+"/";
        Read(".bed/cache/current.json",64*1024);Read(prefix+"manifest.json",1024*1024);Read(prefix+"owner.txt",128);
        var records=new List<AssetRecord>();var selections=new List<(string Id,AssetCatalog.Selection Selection,AssetRecord Record)>();
        var before=catalog?.CurrentInputs().ToArray()??[];
        var sources=new Dictionary<string,ExportSource>(StringComparer.Ordinal);
        foreach(var name in runtime.ArtifactNames)
        {
            if(!name.EndsWith(".bin",StringComparison.Ordinal)||!runtime.ContainsAsset(name[..^4]))throw new InvalidOperationException("Published export artifacts need producing asset GUIDs.");
            var id=name[..^4];var matches=before.Where(i=>i.Guid==id).ToArray();
            if(matches.Length!=1||catalog?.Select(id) is not {} selection)throw new InvalidOperationException("A published export asset is missing or ambiguous in the admitted catalog.");
            var record=matches[0].ProducingRecord;records.Add(record);selections.Add((id,selection,record));
            var relative=prefix+name;Read(relative,ProjectAssets.MaxArtifactBytes);
            if(!files[relative].AsSpan().SequenceEqual(runtime.ReadArtifact(name)))throw new InvalidOperationException("Published export bytes changed during capture.");
            sources.Add(relative,ExportSource.Catalog(id,record.SourceHash));
        }
        void Current()
        {
            freshness();
            foreach(var (relative,bytes) in files.Concat(guards))
            {
                var path=Path.Combine(root,relative);
                for(var at=Path.GetFullPath(path);at is not null;at=Path.GetDirectoryName(at))
                    if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked export inputs are not supported.");
                using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
                if(stream.Length!=bytes.Length||Convert.ToHexStringLower(SHA256.HashData(stream))!=Hash(bytes))throw new InvalidOperationException("Saved export files changed; capture Saved input again.");
            }
            foreach(var (id,selection,record) in selections)
            {
                if(!catalog!.TryResolve(selection,out var lease,out _))throw new InvalidOperationException("The asset catalog changed; capture Saved input again before export.");
                var producing=catalog.CurrentInputs().Where(input=>input.Guid==id).ToArray();
                if(lease!.SourceHash!=record.SourceHash||producing.Length!=1||producing[0].ProducingRecord.Write()!=record.Write())throw new InvalidOperationException("The producing asset policy changed; capture Saved input again.");
                var raw=new byte[lease.Length];lease.CopyTo(raw);
                var stored=files[prefix+id+".bin"];
                if(lease.Texture is {} info)
                {
                    var texture=BedTextureReader.Decode(stored);
                    if(info.Format!=AssetCatalog.TextureEncoding.Rgba8Srgb||info.Width!=texture.Width||info.Height!=texture.Height||!raw.AsSpan().SequenceEqual(texture.Rgba))throw new InvalidOperationException("Published texture differs from its producing catalog.");
                }
                else if(!raw.AsSpan().SequenceEqual(stored))throw new InvalidOperationException("Published asset differs from its producing catalog.");
            }
            var now=ProjectAssets.Open(root);
            if(now.Id!=runtime.Id||now.Revision!=runtime.Revision)throw new InvalidOperationException("The published generation changed; capture Saved input again.");
        }
        Current();
        var nodes=files.OrderBy(p=>p.Key,StringComparer.Ordinal).Select((file,index)=>new GameContentNode(
            new("saved-"+index,file.Key,SavedProjectFormat),file.Value,Hash(file.Value),[],true,sources.GetValueOrDefault(file.Key,ExportSource.GameOwned))).ToArray();
        return new(Hash(Encoding.UTF8.GetBytes(savedText)),nodes,records.ToArray(),Current,physics);
    }
}
