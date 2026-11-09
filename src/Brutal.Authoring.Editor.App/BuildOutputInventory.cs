using System.Collections.Immutable;
namespace Brutal.Authoring.Editor.App;

/// <summary>Bounded read-only post-build capture. Does not start a process, publish, or infer undeclared outputs.</summary>
public static class BuildOutputInventory
{
    /// <summary>Requires the exact declared file set with no links or unrelated directories, then pins and stages captured bytes.</summary>
    public static (GameContentManifest Manifest,ExportStaging Staging) Capture(string output,BuildOutputDeclaration declaration,
        Guid attempt,string? priorGoodManifestHash=null,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var allowed=declaration.Paths.ToHashSet(StringComparer.Ordinal);
        var captured=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        var pending=new Stack<string>();pending.Push(Path.GetFullPath(output));var entries=0;long bytes=0;
        while(pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryAttributes=File.GetAttributes(directory);
            if((directoryAttributes&FileAttributes.ReparsePoint)!=0||(directoryAttributes&FileAttributes.Directory)==0)throw new IOException("Build output path contains a link or non-directory.");
            foreach(var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(++entries>4096)throw new IOException("Build output inventory exceeded its entry budget.");
                var attr=File.GetAttributes(path);if((attr&FileAttributes.ReparsePoint)!=0)throw new IOException("Build output contains a link.");
                var relative=Path.GetRelativePath(output,path).Replace('\\','/');
                if((attr&FileAttributes.Directory)!=0)
                {
                    if(!allowed.Any(k=>k.StartsWith(relative+"/",StringComparison.Ordinal)))throw new IOException("Build directory is outside the allowlist: "+relative);
                    pending.Push(path);continue;
                }
                if(!allowed.Contains(relative))throw new IOException("Build output is outside the allowlist: "+relative);
                using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(file.Length>64L*1024*1024||(bytes+=file.Length)>320L*1024*1024)throw new IOException("Build output budget exceeded: "+relative);
                var payload=new byte[checked((int)file.Length)];file.ReadExactly(payload);captured.Add(relative,payload);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return declaration.Pin(attempt,captured,priorGoodManifestHash);
    }
}
