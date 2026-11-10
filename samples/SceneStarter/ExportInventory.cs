using System.Text.Json;
namespace SceneStarter;

// Sample-specific exact output contract. No wildcard approval of a build's arbitrary files.
public static class ExportInventory
{
    // Retained strict legacy scene-review fixture inventory. Ordinary exports use RuntimeFiles plus saved project paths.
    public static readonly IReadOnlyList<string> Required=Array.AsReadOnly(new[]{"SceneStarter.exe","SceneStarter.dll","SceneStarter.deps.json",
        "SceneStarter.runtimeconfig.json","Brutal.Authoring.dll","Brutal.Authoring.Runtime.dll","scene.json","LICENSE.txt","NOTICE.txt"});
    public static readonly IReadOnlyList<string> RuntimeFiles=Array.AsReadOnly(Required.Where(name=>name!="scene.json").ToArray());
    public static IReadOnlyDictionary<string,byte[]> Read(string folder)
    {
        var allowed=Required.ToHashSet(StringComparer.Ordinal);
        var root=Path.GetFullPath(folder);
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Export root is a link.");
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
        foreach(var path in Directory.EnumerateFileSystemEntries(root))
        {
            var name=Path.GetFileName(path);var attributes=File.GetAttributes(path);
            if((attributes&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0 || !allowed.Contains(name))
                throw new IOException("Unexpected export entry: "+name);
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(stream.Length<=0 || stream.Length>64*1024*1024 || (total+=stream.Length)>128*1024*1024)
                throw new IOException("Export entry is empty or exceeds the byte budget: "+name);
            var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);files.Add(name,bytes);
        }
        if(files.Count!=allowed.Count)throw new IOException("Missing export entries: "+string.Join(", ",allowed.Except(files.Keys)));
        using var deps=JsonDocument.Parse(files["SceneStarter.deps.json"]);
        var libraries=deps.RootElement.GetProperty("libraries").EnumerateObject().Select(p=>p.Name.Split('/')[0]).Order().ToArray();
        if(!libraries.SequenceEqual(new[]{"Brutal.Authoring","Brutal.Authoring.Runtime","SceneStarter"}))
            throw new IOException("Export dependencies must contain only SceneStarter and pure Brutal.Authoring core/Runtime.");
        foreach(var target in deps.RootElement.GetProperty("targets").EnumerateObject())
            foreach(var library in target.Value.EnumerateObject())
                if(library.Value.TryGetProperty("runtime",out var runtimeFiles))
                    foreach(var file in runtimeFiles.EnumerateObject())
                        if(file.Name is not ("SceneStarter.dll" or "Brutal.Authoring.dll" or "Brutal.Authoring.Runtime.dll"))
                            throw new IOException("Unexpected runtime dependency: "+file.Name);
        using var runtime=JsonDocument.Parse(files["SceneStarter.runtimeconfig.json"]);
        var framework=runtime.RootElement.GetProperty("runtimeOptions").GetProperty("framework");
        if(framework.GetProperty("name").GetString()!="Microsoft.NETCore.App" || framework.GetProperty("version").GetString() is not { } version || !version.StartsWith("10.",StringComparison.Ordinal))
            throw new IOException("Expected the declared .NET 10 framework-dependent runtime.");
        return files;
    }
}
