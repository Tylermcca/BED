using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bed;
using Bed.Runtime;
namespace SceneStarter;

// Game-owned producer. The generated runtime descriptor is not a BED library format.
internal static class Scene2Compiler
{
    internal const string Descriptor=".bed/generated/scenes/runtime.json";
    internal const string Stamp=".bed/generated/scenes/build.stamp.json";
    internal static void Compile(string projectRoot,bool supportsPhysics=false)
    {
        var root=Path.GetFullPath(projectRoot);
        var config=Read(root,".bed/project.json",Scene2BuildSettings.MaxProjectBytes);
        var settings=Scene2BuildSettings.ReadProject(config)??throw new InvalidOperationException("Declare sceneBuild before compiling scene-v2.");
        string[] required=supportsPhysics&&settings.SupportsPhysics?PhysicsRuntimeInventory.BuildInputs(settings.Source):[settings.Source,".bed/project.json",".bed/cache/current.json"];
        var inputs=required;var stamp=Stamp;
        using(var json=JsonDocument.Parse(config))
            if(json.RootElement.TryGetProperty("play"u8,out var play)&&play.TryGetProperty("build"u8,out var build))
            {
                if(build.TryGetProperty("inputs"u8,out var declared))
                {
                    if(declared.ValueKind!=JsonValueKind.Array||declared.GetArrayLength()>256)throw new InvalidOperationException("Declare bounded build inputs.");
                    inputs=declared.EnumerateArray().Select(x=>x.GetString()??throw new InvalidOperationException("Build inputs must be strings.")).ToArray();
                    if(required.Any(x=>!inputs.Contains(x,StringComparer.Ordinal)))throw new InvalidOperationException("Scene build inputs must explicitly include its source, project settings and published cache pointer.");
                }
                if(build.TryGetProperty("stamp"u8,out var declaredStamp))stamp=declaredStamp.GetString()??throw new InvalidOperationException("Declare a build stamp path.");
            }
        if(!stamp.StartsWith(".bed/generated/scenes/",StringComparison.Ordinal)||new[]{settings.Generated,Descriptor}.Contains(stamp,StringComparer.OrdinalIgnoreCase)||settings.Generated.Equals(Descriptor,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use separate private generated scene, descriptor and stamp paths.");
        _=Safe(root,stamp);var captured=ProjectBuildStamp.Fingerprint(root,inputs);
        if(Read(root,".bed/project.json",Scene2BuildSettings.MaxProjectBytes)!=config)throw new IOException("Project settings changed while starting the build.");
        var source=new Scene2Format().Read(Read(root,settings.Source,32*1024*1024));
        if(source.Physics is not null&&(!supportsPhysics||!settings.SupportsPhysics))throw new InvalidOperationException("This sample has not enabled authored physics; use a physics-capable runtime.");
        if(!settings.Spaces.Contains(source.Space))throw new InvalidOperationException("The project does not declare this scene space.");
        var assets=ProjectAssets.Open(root);var flat=ScenePhysicsPreparation.Prepare(source,assets,supportsPhysics&&settings.SupportsPhysics);
        var read=SceneRuntimeReader.Read2(new Scene2Format().Write(flat),assets.ContainsAsset);
        if(read.AssetIssues.Count!=0)throw new InvalidOperationException("A scene asset is absent from the published generation.");
        foreach(var item in flat.Objects)
            if(item.Object.Asset is {} id)
            {
                if(item.Sprite is not null)_=assets.ReadTexture(Guid.ParseExact(id,"N"));
                else foreach(var part in assets.ReadMesh(id).Parts)if(part.Material?.Texture is {} texture)_=assets.ReadTexture(texture);
            }
        var scene=new Scene2Format().Write(flat);
        var descriptor=flat.Physics is null?JsonSerializer.Serialize(new{format="scene2-runtime.v1",scene=settings.Generated,sceneHash=Hash(Encoding.UTF8.GetBytes(scene)),assetsId=assets.Id,assetsRevision=assets.Revision}):JsonSerializer.Serialize(new{format="scene2-runtime.v2",scene=settings.Generated,sceneHash=Hash(Encoding.UTF8.GetBytes(scene)),assetsId=assets.Id,assetsRevision=assets.Revision,physicsProfile="jolt-2026.10.2-v1"});
        var currentAssets=ProjectAssets.Open(root);
        if(currentAssets.Id!=assets.Id||currentAssets.Revision!=assets.Revision||ProjectBuildStamp.Fingerprint(root,inputs)!=captured)throw new IOException("Saved build inputs changed during generation.");
        Write(root,settings.Generated,scene);Write(root,Descriptor,descriptor);
        ProjectBuildStamp.Write(root,stamp,inputs,captured,[settings.Generated,Descriptor]);
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Safe(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains('\\')||relative.Contains(':')||relative.StartsWith('/')||relative.Split('/').Any(p=>p is "" or "." or ".."))throw new IOException("Use a portable project-relative build path.");
        var path=Path.GetFullPath(Path.Combine(root,relative));
        if(!path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Build paths must stay inside the project.");
        for(string? at=path;at is not null;at=Path.GetDirectoryName(at))
            if(Path.Exists(at)&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked build paths are refused.");
        return path;
    }
    private static string Read(string root,string relative,int max)
    {
        using var file=new FileStream(Safe(root,relative),FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(file.Length>max)throw new IOException("Saved build input exceeds its byte budget.");
        var bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);return new UTF8Encoding(false,true).GetString(bytes);
    }
    private static void Write(string root,string relative,string text)
    {var path=Safe(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);_=Safe(root,relative);AtomicFile.Write(path,text);}
}
