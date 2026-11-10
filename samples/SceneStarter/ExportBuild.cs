using System.Security.Cryptography;
using System.Text.Json;
namespace SceneStarter;

/// <summary>Copies the exact saved-project manifest and fixed pure runtime into a fresh output folder.</summary>
public static class ExportBuild
{
    public static void Create(string inputDirectory,string outputDirectory,string runtimeDirectory)
    {
        if(!Path.IsPathFullyQualified(inputDirectory)||!Path.IsPathFullyQualified(outputDirectory)||!Path.IsPathFullyQualified(runtimeDirectory))
            throw new ArgumentException("Use admitted absolute input, output and runtime directories.");
        if(!Directory.Exists(outputDirectory)||Directory.EnumerateFileSystemEntries(outputDirectory).Any())throw new IOException("Build output must be a fresh empty directory.");
        var saved=ReadInput(inputDirectory);
        _=SavedScene.Open(inputDirectory);
        var files=ExportInventory.RuntimeFiles.ToDictionary(name=>name,name=>ReadFile(Path.Combine(runtimeDirectory,name),64L*1024*1024),StringComparer.Ordinal);
        files.Add("README.txt",System.Text.Encoding.UTF8.GetBytes("""
            Exported level build

            This folder contains your saved level and prepared models and textures.
            Keep the whole folder together. BED can be closed.

            Requires the .NET 10 runtime for Windows x64 (Microsoft.NETCore.App).
            Open this folder in PowerShell and run:

            .\SceneStarter.exe --project . --seconds 0

            The program reports the loaded level and exits. Use --seconds 5 to keep it running for five seconds.
            Keep LICENSE.txt and NOTICE.txt with the build when sharing it.
            """+"\n"));
        foreach(var pair in saved)if(!files.TryAdd(pair.Key,pair.Value))throw new InvalidOperationException("Saved input collides with a runtime file.");
        foreach(var (relative,bytes) in files)
        {
            var path=Path.Combine(outputDirectory,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);file.Write(bytes);
        }
        _=SavedScene.Open(outputDirectory);
    }
    public static IReadOnlyDictionary<string,byte[]> ReadInput(string inputDirectory)
    {
        var manifest=ReadFile(Path.Combine(inputDirectory,"manifest.json"),1024*1024);
        using var json=JsonDocument.Parse(manifest,new JsonDocumentOptions{MaxDepth=8});var root=json.RootElement;
        Only(root,"version","snapshotHash","sourceManifestHash","format","files");
        if(root.GetProperty("version").GetInt32()!=2)throw new InvalidOperationException("Unsupported Saved-input manifest version.");
        Hex(root.GetProperty("snapshotHash").GetString());Hex(root.GetProperty("sourceManifestHash").GetString());
        if(root.GetProperty("format").ValueKind!=JsonValueKind.String)throw new InvalidOperationException("Missing Saved-input format.");
        var entries=root.GetProperty("files");if(entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>1024)throw new InvalidOperationException("Saved-input file count exceeds its budget.");
        var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var entry in entries.EnumerateArray())
        {
            Only(entry,"path","length","sha256");var relative=entry.GetProperty("path").GetString();
            if(!AllowedPath(relative)||files.ContainsKey(relative!))throw new InvalidOperationException("Unexpected or repeated Saved-input path.");
            var hash=entry.GetProperty("sha256").GetString();Hex(hash);
            var length=entry.GetProperty("length").GetInt64();if(length<0||length>64L*1024*1024||(total+=length)>256L*1024*1024)throw new InvalidOperationException("Saved-input bytes exceed their budget.");
            var bytes=ReadFile(Path.Combine(inputDirectory,relative!),64L*1024*1024);
            if(bytes.LongLength!=length||Convert.ToHexStringLower(SHA256.HashData(bytes))!=hash)throw new InvalidOperationException("Saved-input bytes differ from their manifest.");
            files.Add(relative!,bytes);
        }
        // The runtime validates the pointer and every artifact, while this check binds their entire closure to transport hashes.
        var assets=Brutal.Authoring.Runtime.ProjectAssets.Open(inputDirectory);var prefix=".bed/cache/generations/"+assets.Id+"/";
        var expected=new[]{"data/level.json",".bed/cache/current.json",prefix+"manifest.json",prefix+"owner.txt"}.Concat(assets.ArtifactNames.Select(name=>prefix+name)).Order(StringComparer.Ordinal);
        if(!expected.SequenceEqual(files.Keys.Order(StringComparer.Ordinal)))throw new InvalidOperationException("Saved-input generation closure is incomplete or contains extra files.");
        return files;
    }
    private static bool AllowedPath(string? path)
    {
        if(path is not {Length:>0 and <=240}||path.Contains('\\')||Path.IsPathRooted(path)||path.Split('/').Any(part=>part.Length==0||part is "." or ".."||part.Contains(':')))return false;
        return path=="data/level.json"||path==".bed/cache/current.json"||path.StartsWith(".bed/cache/generations/",StringComparison.Ordinal);
    }
    private static byte[] ReadFile(string path,long maximum)
    {
        for(var at=Path.GetFullPath(path);at is not null;at=Path.GetDirectoryName(at))
            if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Saved input contains a link.");
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>maximum)throw new IOException("Saved-input file exceeds its byte budget.");
        var bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);return bytes;
    }
    private static void Hex(string? value)
    {if(value is not {Length:64}||value.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))throw new InvalidOperationException("Invalid Saved-input hash.");}
    private static void Only(JsonElement item,params string[] names)
    {
        if(item.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("Expected a Saved-input manifest object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in item.EnumerateObject())if(!names.Contains(property.Name,StringComparer.Ordinal)||!seen.Add(property.Name))throw new InvalidOperationException("Unknown or repeated Saved-input field.");
        if(seen.Count!=names.Length)throw new InvalidOperationException("Missing Saved-input field.");
    }
}
