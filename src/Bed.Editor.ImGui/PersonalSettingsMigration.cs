namespace Bed.Editor.ImGui;

// Personal settings only. Existing BED files win; source files are never moved or removed.
internal static class PersonalSettingsMigration
{
    private static readonly object gate=new();
    internal static string Folder(string appData)
    {
        var target=Path.Combine(appData,"BED");
        lock(gate)
        {
            var marker=Path.Combine(target,".legacy-settings-copied");
            if(File.Exists(marker))return target;
            try
            {
                var source=Path.Combine(appData,"brutal-authoring");
                if(Linked(target)||Linked(source))return target;
                Directory.CreateDirectory(target);
                if(Directory.Exists(source))CopyFolder(source,target);
                using var done=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                done.Write("BED settings migration attempted once.\n"u8);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException)
            {System.Diagnostics.Trace.TraceWarning($"Personal settings migration: {e.Message}");}
            return target;
        }
    }
    private static bool Linked(string path)=>Path.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
    private static void CopyFolder(string source,string target)
    {
        string[] entries;
        try{entries=Directory.GetFileSystemEntries(source);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){return;}
        foreach(var path in entries)
        {
            string? temporary=null;
            try
            {
                if(Linked(path))continue;
                var destination=Path.Combine(target,Path.GetFileName(path));
                if(Directory.Exists(path))
                {
                    if(Linked(destination)||File.Exists(destination))continue;
                    Directory.CreateDirectory(destination);CopyFolder(path,destination);continue;
                }
                if(Path.Exists(destination))continue;
                temporary=Path.Combine(target,".migration-"+Guid.NewGuid().ToString("N")+".tmp");
                File.Copy(path,temporary,false);File.Move(temporary,destination,false);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException)
            {System.Diagnostics.Trace.TraceWarning($"A legacy personal setting could not be copied: {e.Message}");}
            finally{if(temporary is not null)try{File.Delete(temporary);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}}
        }
    }
}
