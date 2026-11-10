using System.Security.Cryptography;
using System.Text;
using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Runtime;
namespace SceneStarterAdapter;

public sealed partial class SceneStarterAdapter
{
    private static readonly string[] ExportRuntime=["SceneStarter.exe","SceneStarter.dll","SceneStarter.deps.json","SceneStarter.runtimeconfig.json","Brutal.Authoring.dll","Brutal.Authoring.Runtime.dll","LICENSE.txt","NOTICE.txt","README.txt"];
    private const string SavedProjectFormat="scene-starter.saved-project.v1";
    /// <summary>Packages only the installed runtime and explicitly captured saved project files.</summary>
    public GameBuildSpec Build(DataWorkspace workspace)
    {
        var root=savedProjectRoot??Path.GetDirectoryName(Path.GetFullPath(workspace.Project.DataFolder))!;
        var directory=Path.Combine(root,"game");
        var exportRuntime=Path.Combine(directory,"export");
        if(File.Exists(Path.Combine(exportRuntime,"SceneStarter.exe")))directory=exportRuntime;
        // Published projects carry the same runtime used by Play. Source-only fixtures retain their development runtime.
        if(!File.Exists(Path.Combine(directory,"SceneStarter.exe")))directory=Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter");
        return new(Path.Combine(directory,"SceneStarter.exe"),directory,["--build","{input}","{output}"]);
    }
    /// <summary>Captures the saved level and exactly the currently published generation, with producing asset policy.</summary>
    public GameExportDeclaration Export(DataWorkspace workspace)
    {
        Captured? captured=null;
        return new(input=>
        {
            captured=null;
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
            var sourceProfile=GameBuildProfile.Create("scene-starter-source","samples/SceneStarter/SceneStarter.csproj",GameBuildTarget.WindowsX64,capture.Nodes.Select(n=>n.Output));
            var admission=SavedBuildAdmission.Prepare(saved,sourceProfile,"scene-starter",true,_=>capture.Nodes,
                ["scene-starter.runtime-input"],manifest=>
                {
                    capture.Current();
                    return [new(manifest.Hash,"scene-starter.runtime-input",ExportCheckVerdict.Passed,DateTimeOffset.UtcNow,TimeSpan.Zero)];
                },capture.Records);
            if(!admission.Origin.Notices.IsEmpty)throw new InvalidOperationException("This sample build cannot yet retain additional asset attributions.");
            var outputs=ExportRuntime.Select((path,index)=>new GameContentMapping("runtime-"+index,path,"scene-starter.runtime"))
                .Concat(capture.Nodes.Select(n=>n.Output)).ToArray();
            var profile=GameBuildProfile.Create("scene-starter-runtime","samples/SceneStarter/SceneStarter.csproj",GameBuildTarget.WindowsX64,outputs);
            capture.Current();return new(admission,admission.DeclareOutputs(profile,outputs.Select(m=>new BuildOutputItem(m,[])),[]));
        },(directory,_)=>new(Path.Combine(directory,"SceneStarter.exe"),directory,["--project",".","--seconds","0"]));
    }
    private sealed record Captured(string PayloadHash,GameContentNode[] Nodes,AssetRecord[] Records,Action Current);
    private static Captured CaptureProject(string root,string savedText,AssetCatalog? catalog)
    {
        var runtime=ProjectAssets.Open(root);
        if(runtime.ArtifactNames.Count>1020)throw new InvalidOperationException("Saved export exceeds the 1024-file source inventory bound.");
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        void Read(string relative,int maximum)
        {
            var path=Path.Combine(root,relative);
            for(var at=Path.GetFullPath(path);at is not null;at=Path.GetDirectoryName(at))
                if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked export inputs are not supported.");
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(stream.Length>maximum)throw new IOException("Saved export input exceeds its byte budget.");
            var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);files.Add(relative,bytes);
        }
        Read("data/level.json",1024*1024);
        var text=new UTF8Encoding(false,true).GetString(files["data/level.json"]);
        if(text!=savedText)throw new InvalidOperationException("The saved level changed; capture Saved input again.");
        var level=SceneRuntimeReader.Read(text,assetExists:runtime.ContainsAsset);
        if(level.AssetIssues.Count!=0)throw new InvalidOperationException("The saved level references an asset outside its published generation.");
        foreach(var id in level.Scene.Objects.Select(o=>o.Asset).OfType<string>().Distinct(StringComparer.Ordinal))
            foreach(var part in runtime.ReadMesh(id).Parts)
                if(part.Material?.Texture is {} texture)_=runtime.ReadTexture(texture);
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
            foreach(var (relative,bytes) in files)
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
        return new(Hash(Encoding.UTF8.GetBytes(savedText)),nodes,records.ToArray(),Current);
    }
}
