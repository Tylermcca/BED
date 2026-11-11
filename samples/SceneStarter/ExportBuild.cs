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
        var files=ExportInventory.RuntimeFiles.ToDictionary(name=>name,name=>SavedProjectManifest.ReadFile(Path.Combine(runtimeDirectory,name),64L*1024*1024),StringComparer.Ordinal);
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
    public static IReadOnlyDictionary<string,byte[]> ReadInput(string inputDirectory)=>SavedProjectManifest.Read(inputDirectory);
}
