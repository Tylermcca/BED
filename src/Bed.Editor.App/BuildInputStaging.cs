using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
namespace Bed.Editor.App;

/// <summary>Every immutable admitted source file staged at its declared relative path, never live project fallback.</summary>
internal static class BuildInputStaging
{
    /// <summary>Transport manifest outside the game's output inventory.</summary>
    public const string ManifestPath="manifest.json";
    private static GameContentNode[] Files(SavedBuildAdmission input)
    {
        if(input.SavedInput.Choice!=PlayInputChoice.Saved||input.Source.InventoryRevision!=input.SnapshotHash)throw new InvalidOperationException("Build input requires this exact Saved admission.");
        var files=input.Source.Nodes.OrderBy(n=>n.Output.Destination,StringComparer.Ordinal).ToArray();
        if(files.Any(n=>n.Output.Destination.Equals(ManifestPath,StringComparison.OrdinalIgnoreCase)||n.Output.Destination.StartsWith(ManifestPath+"/",StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Captured source collides with the transport manifest.");
        return files;
    }
    private static byte[] Manifest(SavedBuildAdmission input,GameContentNode[] files)=>JsonSerializer.SerializeToUtf8Bytes(new
    {version=2,snapshotHash=input.SnapshotHash,sourceManifestHash=input.Source.Hash,format=input.SavedInput.Format,
     files=files.Select(n=>new{path=n.Output.Destination,length=(long)n.Length,sha256=n.Sha256}).ToArray()});
    /// <summary>Creates a fresh input directory containing all and only admitted captured files and their hash manifest.</summary>
    public static string Write(string ownedAttempt,SavedBuildAdmission input)
    {
        ArgumentNullException.ThrowIfNull(input);var files=Files(input);
        if(!Path.IsPathFullyQualified(ownedAttempt))throw new ArgumentException("Use an absolute owned staging directory.");Safe(ownedAttempt);
        var directory=Path.Combine(ownedAttempt,"input");if(Path.Exists(directory))throw new IOException("Build input already exists.");Directory.CreateDirectory(directory);
        foreach(var node in files)
        {
            var path=Path.Combine(directory,node.Output.Destination.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);Safe(path);
            var bytes=new byte[node.Length];node.CopyTo(bytes);using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);stream.Write(bytes);
        }
        using(var stream=new FileStream(Path.Combine(directory,ManifestPath),FileMode.CreateNew,FileAccess.Write,FileShare.Read))stream.Write(Manifest(input,files));
        return directory;
    }
    /// <summary>Checks complete transport paths and hashes after the child; additions, mutation and omissions refuse.</summary>
    public static void Verify(string directory,SavedBuildAdmission input)
    {
        var files=Files(input);var expected=files.ToDictionary(n=>n.Output.Destination,n=>(Length:(long)n.Length,Hash:n.Sha256),StringComparer.OrdinalIgnoreCase);
        var manifest=Manifest(input,files);expected.Add(ManifestPath,(manifest.LongLength,Convert.ToHexStringLower(SHA256.HashData(manifest))));
        var allowedDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var name in expected.Keys)for(var slash=name.IndexOf('/');slash>=0;slash=name.IndexOf('/',slash+1))allowedDirectories.Add(name[..slash]);
        var pending=new Stack<string>();pending.Push(directory);var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(pending.TryPop(out var folder))
        {
            Safe(folder);
            foreach(var path in Directory.EnumerateFileSystemEntries(folder))
            {
                Safe(path);var relative=Path.GetRelativePath(directory,path).Replace('\\','/');
                if(Directory.Exists(path)){if(!allowedDirectories.Contains(relative))throw new IOException("Undeclared captured input directory.");pending.Push(path);continue;}
                if(!expected.TryGetValue(relative,out var item)||!found.Add(relative))throw new IOException("Undeclared or duplicate captured input file.");
                using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(stream.Length!=item.Length||Convert.ToHexStringLower(SHA256.HashData(stream))!=item.Hash)throw new IOException("Captured Saved input changed during build.");
            }
        }
        if(found.Count!=expected.Count)throw new IOException("Captured Saved input is incomplete.");
    }
    internal static Dictionary<string,string> ExpectedOutputs(SavedBuildAdmission input)=>Files(input).ToDictionary(n=>n.Output.Destination,n=>n.Sha256,StringComparer.Ordinal);
    private static void Safe(string path)
    {
        for(var at=Path.GetFullPath(path);at is not null;at=Path.GetDirectoryName(at))
        {try{if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Captured input contains a link.");}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}
    }
}
