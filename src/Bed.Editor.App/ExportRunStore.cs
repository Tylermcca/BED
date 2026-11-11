using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Bed.Editor.App;

// Owns only <local>/exports/<EX10 project key>/<run id>; never a project or a copied destination.
internal sealed class ExportRunStore(string localFolder)
{
    private readonly string exports=Path.Combine(Path.GetFullPath(localFolder),"exports");
    private sealed record Owner(string Project,string Run,DateTimeOffset Created);
    internal ExportRunLog Begin(string project)
    {
        var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(project));
        if(exports.Equals(full,StringComparison.OrdinalIgnoreCase)||exports.StartsWith(full+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Export logs must be outside the project.");
        var key=Key(full);var folder=Path.Combine(exports,key);SafeDirectory(folder,true);
        var id=Guid.NewGuid().ToString("N");var run=Path.Combine(folder,id);
        if(!CreateDirectoryW(run,IntPtr.Zero))throw new IOException("Could not reserve an owned export run folder.");
        File.WriteAllText(Path.Combine(run,"run.owner"),JsonSerializer.Serialize(new Owner(key,id,DateTimeOffset.UtcNow)));
        var log=new ExportRunLog(run);Prune(folder,key);return log;
    }
    internal bool Owns(string path)
    {
        try
        {
            var full=Path.GetFullPath(path);if(Path.GetFileName(full)!="export.log"||!File.Exists(full))return false;
            var run=Path.GetDirectoryName(full)!;var folder=Path.GetDirectoryName(run)!;
            return Path.GetDirectoryName(folder)==exports&&Owned(run,Path.GetFileName(folder)) is not null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){return false;}
    }
    private static string Key(string project)
    {if(OperatingSystem.IsWindows())project=project.ToUpperInvariant();return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(project)));}
    private static Owner? Owned(string run,string key)
    {
        try
        {
            var id=Path.GetFileName(run);if(!Guid.TryParseExact(id,"N",out _)||(File.GetAttributes(run)&FileAttributes.ReparsePoint)!=0)return null;
            var marker=Path.Combine(run,"run.owner");if(!File.Exists(marker)||new FileInfo(marker).Length>1024)return null;
            var owner=JsonSerializer.Deserialize<Owner>(File.ReadAllText(marker));return owner is not null&&owner.Project==key&&owner.Run==id?owner:null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    private static void Prune(string folder,string key)
    {
        var runs=Directory.EnumerateDirectories(folder).Select(path=>(Path:path,Owner:Owned(path,key))).Where(p=>p.Owner is not null)
            .OrderByDescending(p=>p.Owner!.Created).ThenByDescending(p=>p.Owner!.Run,StringComparer.Ordinal).ToArray();
        foreach(var old in runs.Skip(3))
        {
            try
            {
                // Active phases hold this lock. Never remove a live build/check/copy or an unowned/linked folder.
                using(var lease=new FileStream(Path.Combine(old.Path,"active.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
                var pending=new Stack<string>();pending.Push(old.Path);
                while(pending.TryPop(out var at))foreach(var entry in Directory.EnumerateFileSystemEntries(at))
                {var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked export run cannot be retired.");if((attributes&FileAttributes.Directory)!=0)pending.Push(entry);}
                if(Path.GetDirectoryName(Path.GetFullPath(old.Path))!=Path.GetFullPath(folder))throw new IOException("Export retirement left its owned project folder.");
                Directory.Delete(old.Path,true);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){} // A live/locked run is retained until a later allocation.
        }
    }
    private static void SafeDirectory(string folder,bool create)
    {
        var current=Path.GetPathRoot(folder)!;
        foreach(var part in folder[current.Length..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries))
        {current=Path.Combine(current,part);if(Path.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Export log path contains a link.");}
        if(create)Directory.CreateDirectory(folder);
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern bool CreateDirectoryW(string path,IntPtr security);
}
