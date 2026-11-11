using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Bed.Runtime;

/// <summary>Owned read-only bytes from one hash-verified published project cache generation.
/// Never imports, writes, acquires owner.lock, resolves source paths or grants export permission.</summary>
public sealed class ProjectAssets
{
    /// <summary>Plain refusal when BED has not published a complete saved generation.</summary>
    public const string MissingGeneration="Open the project in BED once to prepare its assets";
    /// <summary>Maximum artifacts loaded from one generation.</summary>
    public const int MaxArtifacts=4096;
    /// <summary>Maximum single derived artifact size, matching BEDMESH1's byte bound.</summary>
    public const int MaxArtifactBytes=64*1024*1024;
    /// <summary>Maximum total owned derived bytes in this runtime snapshot.</summary>
    public const long MaxTotalBytes=256L*1024*1024;
    private const int MaxManifestBytes=1024*1024,MaxPointerBytes=64*1024;
    private const string Marker="BED asset cache stage v1";
    private readonly Dictionary<string,byte[]> artifacts;
    private ProjectAssets(string id,long generation,string revision,Dictionary<string,byte[]> artifacts)
    {Id=id;Generation=generation;Revision=revision;this.artifacts=artifacts;ArtifactNames=Array.AsReadOnly(artifacts.Keys.Order(StringComparer.Ordinal).ToArray());}
    /// <summary>Exact saved allocation identity, not a live catalog admission.</summary>
    public string Id {get;}
    /// <summary>Published positive storage generation number.</summary>
    public long Generation {get;}
    /// <summary>Producer's exact complete-input revision, not a grant of authority.</summary>
    public string Revision {get;}
    /// <summary>Immutable list of exact declared names. No source-directory scan or basename lookup.</summary>
    public IReadOnlyList<string> ArtifactNames {get;}
    /// <summary>Copies one exact declared artifact; caller mutation cannot alter the snapshot.</summary>
    /// <param name="name">One name from <see cref="ArtifactNames"/>, including its extension; paths are refused.</param>
    /// <returns>A fresh owned byte array from this snapshot, without rereading disk.</returns>
    /// <exception cref="ArgumentException">The name is not a supported flat artifact name.</exception>
    /// <exception cref="InvalidDataException">This generation does not declare the name.</exception>
    public byte[] ReadArtifact(string name)
    {
        if(!ValidName(name))throw new ArgumentException("Use one exact declared artifact name.",nameof(name));
        return artifacts.TryGetValue(name,out var bytes)?bytes.ToArray():throw Bad("The saved generation has no artifact "+name+".");
    }
    /// <summary>Copies the producer's exact lowercase-GUID.bin entry. Never infers GUIDs from source paths.</summary>
    /// <param name="guid">Exactly 32 lowercase hexadecimal characters, without hyphens.</param>
    /// <returns>A fresh copy of the GUID.bin artifact, which may contain a model or a texture.</returns>
    /// <exception cref="ArgumentException">The GUID spelling is invalid.</exception>
    /// <exception cref="InvalidDataException">The exact asset is absent from this generation.</exception>
    public byte[] ReadAsset(string guid)
    {if(!IsId(guid))throw new ArgumentException("Use a 32-lowercase-hex asset GUID.",nameof(guid));return ReadArtifact(guid+".bin");}
    /// <summary>Whether this verified generation declares that exact GUID.bin asset.</summary>
    /// <param name="guid">The 32-lowercase-hex asset GUID, without hyphens.</param>
    /// <returns>False for an invalid GUID or an absent artifact; this does not decode or identify its kind.</returns>
    public bool ContainsAsset(string guid)=>IsId(guid)&&artifacts.ContainsKey(guid+".bin");
    /// <summary>Decodes this generation's exact GUID.bin through the bounded BEDMESH1/2 decoder.</summary>
    /// <param name="guid">The model's 32-lowercase-hex asset GUID, without hyphens.</param>
    /// <returns>Owned model parts. Resolve each part's optional texture through this same snapshot.</returns>
    /// <exception cref="ArgumentException">The GUID spelling is invalid.</exception>
    /// <exception cref="InvalidDataException">The asset is absent or is not a valid supported model.</exception>
    public BedMesh ReadMesh(string guid)=>BedMeshReader.Decode(ReadAsset(guid));
    /// <summary>Reads a texture only by its exact GUID in this same verified generation.</summary>
    /// <param name="guid">The texture GUID recorded by <see cref="BedMeshMaterial.Texture"/>.</param>
    /// <returns>Owned top-left RGBA8 pixels decoded from BEDTEX1; this does not upload or flip them.</returns>
    /// <exception cref="InvalidDataException">The texture is absent or its payload is invalid.</exception>
    public BedTexture ReadTexture(Guid guid) => BedTextureReader.Decode(ReadAsset(guid.ToString("N")));
    /// <summary>Reads only .bed/cache/current.json, its exact generation and declared artifacts.
    /// Bounds and every recorded hash are checked before the snapshot is returned; missing publication refuses plainly.</summary>
    /// <param name="projectRoot">The project or copied build root containing .bed/cache/current.json, not the assets folder.</param>
    /// <returns>An owned snapshot of one complete published generation; later publications do not change it.</returns>
    /// <remarks>All declared artifacts are loaded and hash-checked, within the published byte/count limits.
    /// Source files are unnecessary. This call never imports, repairs, writes or acquires owner.lock.</remarks>
    /// <exception cref="ArgumentException">The project root is empty or is not a valid filesystem path.</exception>
    /// <exception cref="InvalidDataException">The publication is missing, malformed, changed or over budget.</exception>
    /// <exception cref="IOException">Files cannot be read, or a cache path is linked.</exception>
    /// <exception cref="UnauthorizedAccessException">The current process cannot read a required path.</exception>
    public static ProjectAssets Open(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var root=Path.GetFullPath(projectRoot);var cache=Path.Combine(root,".bed","cache");
        var pointerPath=Path.Combine(cache,"current.json");Safe(pointerPath);
        if(!File.Exists(pointerPath))throw Bad(MissingGeneration);
        try
        {
            var pointerBytes=Read(pointerPath,MaxPointerBytes);
            using var pointer=JsonDocument.Parse(pointerBytes,new(){MaxDepth=8});Only(pointer.RootElement,"Id","ManifestHash");
            var id=Text(pointer.RootElement,"Id");var manifestHash=Text(pointer.RootElement,"ManifestHash");
            if(!IsId(id)||!IsHash(manifestHash))throw Bad("Invalid published cache pointer.");
            var directory=Path.Combine(cache,"generations",id);Safe(directory);
            var manifestPath=Path.Combine(directory,"manifest.json");
            if(!Directory.Exists(directory)||!File.Exists(manifestPath))throw Bad(MissingGeneration);
            var manifestBytes=Read(manifestPath,MaxManifestBytes);
            if(Hash(manifestBytes)!=manifestHash)throw Bad("Published manifest changed.");
            if(Encoding.UTF8.GetString(Read(Path.Combine(directory,"owner.txt"),128))!=Marker)throw Bad("Unowned cache allocation.");
            using var manifest=JsonDocument.Parse(manifestBytes,new(){MaxDepth=8});var header=manifest.RootElement;
            Only(header,"Id","Generation","Revision","Artifacts");
            if(Text(header,"Id")!=id||!header.GetProperty("Generation").TryGetInt64(out var generation)||generation<1)throw Bad("Invalid manifest identity or generation.");
            var revision=Text(header,"Revision");if(string.IsNullOrWhiteSpace(revision))throw Bad("Invalid manifest revision.");
            var list=header.GetProperty("Artifacts");
            if(list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()>MaxArtifacts)throw Bad("Invalid manifest artifact count.");
            var declarations=new List<(string Name,long Length,string Hash)>();
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"manifest.json","owner.txt"};long total=0;
            foreach(var entry in list.EnumerateArray())
            {
                Only(entry,"Name","Length","Sha256");var name=Text(entry,"Name");var hash=Text(entry,"Sha256");
                if(!ValidName(name)||!names.Add(name)||!IsHash(hash)||!entry.GetProperty("Length").TryGetInt64(out var length)||length<0||length>MaxArtifactBytes)
                    throw Bad("Invalid artifact declaration.");
                total+=length;if(total>MaxTotalBytes)throw Bad("Published artifacts exceed the runtime byte budget.");
                declarations.Add((name,length,hash));
            }
            // This is a flat existing publisher layout. Never recurse into staging, old generations or extra content.
            var count=0;
            foreach(var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if(++count>MaxArtifacts+2)throw Bad("Published generation exceeds its entry budget.");
                Safe(path);if(Directory.Exists(path)||!names.Contains(Path.GetFileName(path)))throw Bad("Undeclared published content.");
            }
            var result=new Dictionary<string,byte[]>(StringComparer.Ordinal);
            foreach(var entry in declarations)
            {
                var bytes=Read(Path.Combine(directory,entry.Name),entry.Length);
                if(bytes.LongLength!=entry.Length||Hash(bytes)!=entry.Hash)throw Bad("Incomplete or changed artifact: "+entry.Name);
                result.Add(entry.Name,bytes);
            }
            if(!Read(pointerPath,MaxPointerBytes).AsSpan().SequenceEqual(pointerBytes))throw Bad("The published generation changed while reading; open it again.");
            return new(id,generation,revision,result);
        }
        catch(JsonException error){throw Bad("Invalid published cache JSON: "+error.Message);}
        catch(InvalidOperationException error){throw Bad("Invalid published cache fields: "+error.Message);}
    }
    private static byte[] Read(string path,long maximum)
    {
        Safe(path);using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(stream.Length>maximum)throw Bad("Published cache file exceeds its byte budget.");
        var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);return bytes;
    }
    private static void Safe(string path)
    {
        for(var at=Path.GetFullPath(path);at is not null;at=Path.GetDirectoryName(at))
        {
            FileAttributes attributes;
            try{attributes=File.GetAttributes(at);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked cache paths are not supported.");
        }
    }
    private static void Only(JsonElement value,params string[] fields)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad("Expected a published cache object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!fields.Contains(property.Name,StringComparer.Ordinal)||!seen.Add(property.Name))throw Bad("Unknown or repeated cache field.");
        if(seen.Count!=fields.Length)throw Bad("Missing published cache field.");
    }
    private static string Text(JsonElement value,string name)
    {var item=value.GetProperty(name);return item.ValueKind==JsonValueKind.String&&item.GetString() is {} text?text:throw Bad("Invalid cache string: "+name);}
    private static bool IsId(string? value)=>Hex(value,32);
    private static bool IsHash(string? value)=>Hex(value,64);
    private static bool Hex(string? value,int length)=>value is not null&&value.Length==length&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidName(string? name)=>name is {Length:>0 and <=128}&&name is not "." and not ".."
        &&name.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-')&&!name.EndsWith('.')
        &&!name.Equals("owner.txt",StringComparison.OrdinalIgnoreCase)&&!name.Equals("manifest.json",StringComparison.OrdinalIgnoreCase)&&!DeviceName(name.Split('.')[0]);
    private static bool DeviceName(string stem)
    {var name=stem.ToUpperInvariant();return name is "CON" or "PRN" or "AUX" or "NUL"||name.Length==4&&(name.StartsWith("COM",StringComparison.Ordinal)||name.StartsWith("LPT",StringComparison.Ordinal))&&name[3] is >= '1' and <= '9';}
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static InvalidDataException Bad(string reason)=>new(reason);
}
