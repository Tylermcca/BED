using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bed.Editor;

// Shared outbound boundary, not an asset reader. Opaque frames/checks can include any project asset,
// so a refusal covers the whole request. Only sidecars are read, never the source asset bytes.
internal static class AssetAiAccess
{
    internal static string RootFor(string data)
    {
        var parent=Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)))!;
        for(var current=parent;current is not null;current=Path.GetDirectoryName(current))
            if(File.Exists(Path.Combine(current,".bed","project.json")))return current;
        return parent;
    }
    internal static JsonObject? Refusal(params string?[] paths)
    {
        try
        {
            foreach(var path in paths.Where(p=>p is not null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var full=Path.GetFullPath(path!);
                // Refuse links before enumeration; following a link could silently bypass the asset scope.
                for(var ancestor=full;ancestor is not null;ancestor=Path.GetDirectoryName(ancestor))
                    if((File.Exists(ancestor)||Directory.Exists(ancestor))&&(File.GetAttributes(ancestor)&FileAttributes.ReparsePoint)!=0)
                        return Denied("AI access is refused because a linked path's asset permissions cannot be checked.");
                if(Directory.Exists(full))
                {
                    var pending=new Stack<string>();pending.Push(full);
                    while(pending.TryPop(out var directory))
                        foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
                        {
                            var attributes=File.GetAttributes(entry);
                            if((attributes&FileAttributes.ReparsePoint)!=0)
                                return Denied("AI access is refused because a linked path's asset permissions cannot be checked.");
                            if((attributes&FileAttributes.Directory)!=0)
                            {
                                if(Path.GetFileName(entry)!=".git")pending.Push(entry);
                            }
                            else if(entry.EndsWith(AssetRecord.Suffix,StringComparison.OrdinalIgnoreCase)&&Record(entry) is {} refusal)return refusal;
                        }
                }
                else if(File.Exists(full))
                {
                    var sidecar=full.EndsWith(AssetRecord.Suffix,StringComparison.OrdinalIgnoreCase)?full:AssetRecord.SidecarFor(full);
                    if(File.Exists(sidecar)&&Record(sidecar) is {} refusal)return refusal;
                }
            }
            return null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or DocumentFormatException or ArgumentException)
        { return Denied("AI permission is unknown because an asset record could not be checked. No project content was sent."); }
    }
    private static JsonObject? Record(string path)
    {
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return Denied("AI permission is unknown for a linked asset record. No project content was sent.");
        if(new FileInfo(path).Length>1024*1024)return Denied("AI permission is unknown because an asset record is too large to check. No project content was sent.");
        var record=AssetRecord.Read(File.ReadAllText(path));
        return record.AiUse switch
        {
            AiUse.Allowed=>null,
            AiUse.Forbidden=>Denied("AI access is refused: an asset in this project is marked forbidden for AI use. No project content was sent."),
            _=>Denied("AI permission is unknown for an asset in this project. No project content was sent; its owner must check the terms in BED first.")
        };
    }
    private static JsonObject Denied(string message)=>new(){["error"]=message,["next"]="Review asset permissions in BED. This AI connection cannot grant itself permission."};
    internal static JsonObject Guard(Func<JsonObject> action,params string?[] paths)
    {
        if(Refusal(paths) is {} before)return before;
        try
        {
            var result=action();
            return Refusal(paths)??result;
        }
        catch(Exception e)when(e is not OutOfMemoryException)
        {if(Refusal(paths) is {} denied)return denied;throw;}
    }
    internal static void Require(params string?[] paths)
    {if(Refusal(paths) is {} denied)throw new InvalidOperationException(denied["error"]!.GetValue<string>());}
}
