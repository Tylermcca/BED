using System.Security.Cryptography;
using System.Text.Json;
namespace SceneStarter;

internal static class SavedProjectManifest
{
    internal static IReadOnlyDictionary<string,byte[]> Read(string inputDirectory,bool physics=false)
    {
        var manifest=ReadFile(Path.Combine(inputDirectory,"manifest.json"),1024*1024);
        using var json=JsonDocument.Parse(manifest,new JsonDocumentOptions{MaxDepth=8});var root=json.RootElement;
        Only(root,"version","snapshotHash","sourceManifestHash","format","files");
        if(root.GetProperty("version"u8).GetInt32()!=2)throw new InvalidOperationException("Unsupported Saved-input manifest version.");
        Hex(root.GetProperty("snapshotHash"u8).GetString());Hex(root.GetProperty("sourceManifestHash"u8).GetString());
        if(root.GetProperty("format"u8).ValueKind!=JsonValueKind.String)throw new InvalidOperationException("Missing Saved-input format.");
        var entries=root.GetProperty("files"u8);if(entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>1024)throw new InvalidOperationException("Saved-input file count exceeds its budget.");
        var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var entry in entries.EnumerateArray())
        {
            Only(entry,"path","length","sha256");var relative=entry.GetProperty("path"u8).GetString();
            if(!AllowedPath(relative)||files.ContainsKey(relative!))throw new InvalidOperationException("Unexpected or repeated Saved-input path.");
            var hash=entry.GetProperty("sha256"u8).GetString();Hex(hash);
            var length=entry.GetProperty("length"u8).GetInt64();if(length<0||length>64L*1024*1024||(total+=length)>256L*1024*1024)throw new InvalidOperationException("Saved-input bytes exceed their budget.");
            var bytes=ReadFile(Path.Combine(inputDirectory,relative!),64L*1024*1024);
            if(bytes.LongLength!=length||Convert.ToHexStringLower(SHA256.HashData(bytes))!=hash)throw new InvalidOperationException("Saved-input bytes differ from their manifest.");
            files.Add(relative!,bytes);
        }
        // The runtime validates the pointer and every artifact, while this check binds their entire closure to transport hashes.
        var assets=Bed.Runtime.ProjectAssets.Open(inputDirectory);var prefix=".bed/cache/generations/"+assets.Id+"/";
        var levelFiles=files.ContainsKey(Scene2RuntimeInput.Descriptor)?new[]{Scene2RuntimeInput.Descriptor,Scene2RuntimeInput.Open(inputDirectory,physics).ScenePath}:new[]{"data/level.json"};
        var expected=levelFiles.Concat(new[]{".bed/cache/current.json",prefix+"manifest.json",prefix+"owner.txt"}).Concat(assets.ArtifactNames.Select(name=>prefix+name)).Order(StringComparer.Ordinal);
        if(!expected.SequenceEqual(files.Keys.Order(StringComparer.Ordinal)))throw new InvalidOperationException("Saved-input generation closure is incomplete or contains extra files.");
        return files;
    }
    private static bool AllowedPath(string? path)
    {
        if(path is not {Length:>0 and <=240}||path.Contains('\\')||Path.IsPathRooted(path)||path.Split('/').Any(part=>part.Length==0||part is "." or ".."||part.Contains(':')))return false;
        return path.StartsWith(".bed/generated/scenes/",StringComparison.Ordinal)||path=="data/level.json"||path==".bed/cache/current.json"||path.StartsWith(".bed/cache/generations/",StringComparison.Ordinal);
    }
    internal static byte[] ReadFile(string path,long maximum)
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
