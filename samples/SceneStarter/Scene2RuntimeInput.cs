using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bed;
using Bed.Runtime;
namespace SceneStarter;

// Game-owned runtime descriptor and checked cache closure, shared as source with this game's adapter.
internal sealed record Scene2RuntimeInput(string ScenePath,byte[] SceneBytes,byte[] DescriptorBytes,Scene2RuntimeReadResult Level,
    ProjectAssets Assets,Dictionary<string,BedMesh> Meshes,Dictionary<string,BedTexture> Textures)
{
    internal const string Descriptor=".bed/generated/scenes/runtime.json";
    internal static Scene2RuntimeInput Open(string projectRoot,bool supportsPhysics=false)
    {
        var root=Path.GetFullPath(projectRoot);var descriptor=Read(root,Descriptor,64*1024);
        using var json=JsonDocument.Parse(descriptor,new JsonDocumentOptions{MaxDepth=8});var header=json.RootElement;
        var physics=supportsPhysics&&Text(header,"format")=="scene2-runtime.v2";
        Only(header,physics?["format","scene","sceneHash","assetsId","assetsRevision","physicsProfile"]:["format","scene","sceneHash","assetsId","assetsRevision"]);
        if(physics&&Text(header,"physicsProfile")!="jolt-2026.10.2-v1")throw new InvalidDataException("Unsupported physics integration profile.");
        if(!physics&&Text(header,"format")!="scene2-runtime.v1")throw new InvalidDataException("The built level data is from another version; build the saved level again.");
        var scenePath=Text(header,"scene");
        if(!scenePath.StartsWith(".bed/generated/scenes/",StringComparison.Ordinal)||scenePath==Descriptor)throw new InvalidDataException("The runtime scene must be a separate private generated file.");
        var expected=Text(header,"sceneHash");Hex(expected,64);var assetId=Text(header,"assetsId");Hex(assetId,32);
        var revision=Text(header,"assetsRevision");if(revision.Length is <1 or >65536)throw new InvalidDataException("Invalid runtime asset revision.");
        var bytes=Read(root,scenePath,32*1024*1024);if(Hash(bytes)!=expected)throw new InvalidDataException("Generated scene hash changed; build the saved scene again.");
        var assets=ProjectAssets.Open(root);
        if(assets.Id!=assetId||assets.Revision!=revision)throw new InvalidDataException("The generated scene belongs to another published asset generation; build again.");
        var level=SceneRuntimeReader.Read2(new UTF8Encoding(false,true).GetString(bytes),assets.ContainsAsset);
        if(level.Scene.Physics is not null&&!physics)throw new InvalidDataException("This sample has not enabled authored physics; use a physics-capable runtime.");
        if(physics&&(level.Scene.Physics is null||level.Scene.Objects.Any(o=>o.Body?.Shape is SceneMeshBoundsShape)))throw new InvalidDataException("Physics runtime requires explicit world settings and prepared primitive bodies.");
        if(level.AssetIssues.Count!=0)throw new InvalidDataException("A runtime scene asset is absent from its published generation.");
        var config=Path.Combine(root,".bed","project.json");
        if(File.Exists(config))
        {
            var capability=Scene2BuildSettings.ReadProject(new UTF8Encoding(false,true).GetString(Read(root,".bed/project.json",Scene2BuildSettings.MaxProjectBytes)));
            if(capability is null||capability.Generated!=scenePath||!capability.Spaces.Contains(level.Scene.Space)||physics&&!capability.SupportsPhysics)throw new InvalidDataException("The generated scene differs from the declared project capability.");
        }
        var meshes=new Dictionary<string,BedMesh>(StringComparer.Ordinal);var textures=new Dictionary<string,BedTexture>(StringComparer.Ordinal);
        void Texture(string id){if(!textures.ContainsKey(id))textures.Add(id,assets.ReadTexture(Guid.ParseExact(id,"N")));}
        foreach(var item in level.Scene.Objects)
            if(item.Object.Asset is {} id)
            {
                if(item.Sprite is not null)Texture(id);
                else if(!meshes.ContainsKey(id))
                {var mesh=assets.ReadMesh(id);meshes.Add(id,mesh);foreach(var part in mesh.Parts)if(part.Material?.Texture is {} texture)Texture(texture.ToString("N"));}
            }
        if(!Read(root,Descriptor,64*1024).AsSpan().SequenceEqual(descriptor)||Hash(Read(root,scenePath,32*1024*1024))!=expected)throw new IOException("Runtime files changed while loading.");
        return new(scenePath,bytes,descriptor,level,assets,meshes,textures);
    }
    internal static byte[] Read(string root,string relative,int maximum)
    {
        if(relative.Length is <1 or >1024||relative.Contains('\\')||relative.Contains(':')||relative.StartsWith('/')||relative.Split('/').Any(InvalidSegment))throw new IOException("Use a portable relative runtime path.");
        var path=Path.GetFullPath(Path.Combine(root,relative));var prefix=Path.EndsInDirectorySeparator(root)?root:root+Path.DirectorySeparatorChar;
        if(!path.StartsWith(prefix,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Runtime paths must stay inside the project.");
        for(string? at=path;at is not null;at=Path.GetDirectoryName(at))
            if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked runtime inputs are refused.");
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(file.Length>maximum)throw new IOException("Runtime input exceeds its byte budget.");var bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);return bytes;
    }
    private static bool InvalidSegment(string part)
    {
        if(part is "" or "." or ".."||part.EndsWith('.')||part.EndsWith(' ')||part.Any(c=>char.IsControl(c)||c is '<' or '>' or ':' or '"' or '|' or '?' or '*'))return true;
        var stem=part.Split('.')[0].ToUpperInvariant();return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"||stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&char.IsNumber(stem[3]);
    }
    private static string Text(JsonElement value,string name)=>value.GetProperty(name).ValueKind==JsonValueKind.String?value.GetProperty(name).GetString()!:throw new InvalidDataException("Expected a runtime descriptor string.");
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Hex(string value,int length){if(value.Length!=length||value.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))throw new InvalidDataException("Invalid runtime descriptor identity/hash.");}
    private static void Only(JsonElement value,params string[] keys)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Expected a runtime descriptor object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!keys.Contains(property.Name,StringComparer.Ordinal)||!seen.Add(property.Name))throw new InvalidDataException("Unknown or repeated runtime descriptor field.");
        if(seen.Count!=keys.Length)throw new InvalidDataException("Missing runtime descriptor field.");
    }
}
