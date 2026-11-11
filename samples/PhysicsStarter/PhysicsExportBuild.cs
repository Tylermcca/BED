using System.Text;
using SceneStarter;
namespace PhysicsStarter;

internal static class PhysicsExportBuild
{
    internal static void Create(string input,string output,string runtime)
    {
        if(!Path.IsPathFullyQualified(input)||!Path.IsPathFullyQualified(output)||!Path.IsPathFullyQualified(runtime))throw new IOException("Use absolute physics build folders.");
        if(!Directory.Exists(output)||Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Use a fresh empty physics output folder.");
        var saved=SavedProjectManifest.Read(input,true);_=Scene2RuntimeInput.Open(input,true);var files=PhysicsRuntimeInventory.Read(runtime);
        files.Add("README.txt",Encoding.UTF8.GetBytes("Saved physics build for Windows x64. Requires .NET 10. Run PhysicsStarter.exe --project . --seconds 1. Keep every file and notice together. BED and original authoring files are not required.\n"));
        foreach(var file in saved)if(!files.TryAdd(file.Key,file.Value))throw new IOException("Saved input collides with physics runtime.");
        foreach(var (relative,bytes) in files)
        {
            var path=Path.GetFullPath(Path.Combine(output,relative));if(!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(output))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Physics output path escapes its folder.");
            for(string? at=path;at is not null;at=Path.GetDirectoryName(at))if(Path.Exists(at)&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked physics output.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);for(string? at=path;at is not null;at=Path.GetDirectoryName(at))if(Path.Exists(at)&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked physics output.");
            using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);file.Write(bytes);
        }
        _=Scene2RuntimeInput.Open(output,true);
    }
}
