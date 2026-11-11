using System.Text;
namespace Bed.Editor.ImGui;
internal enum ReportEvent {Started,OperationAccepted,OperationRefused,WorkspaceStatusChanged,PanelFailed,VulkanFallback}
internal static class ProblemReportLog
{
    internal const int MaxBytes=1024*1024;
    private static readonly object gate=new();private static readonly Queue<string> lines=new();private static string? folder;
    internal static string DefaultFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"BED");
    internal static IDisposable Configure(string? destination)
    {lock(gate){var previous=folder;folder=destination;return new Scope(previous);}}
    private sealed class Scope(string? previous):IDisposable
    {private bool closed;public void Dispose(){lock(gate){if(closed)return;closed=true;folder=previous;}}}
    internal static void Record(ReportEvent item)
    {
        var error=item is ReportEvent.OperationRefused or ReportEvent.PanelFailed or ReportEvent.VulkanFallback;
        var area=item==ReportEvent.Started?"launcher":item==ReportEvent.VulkanFallback?"renderer":"editor";
        var line=$"{DateTime.UtcNow:O} {(error?"ERROR":"INFO")} [{area}] {item}";
        lock(gate)
        {
            lines.Enqueue(line);while(lines.Count>200)lines.Dequeue();
            if(folder is null)return;
            try
            {
                var logFolder=Path.Combine(folder,"logs");Directory.CreateDirectory(logFolder);var file=Path.Combine(logFolder,"bed.log");var old=file+".1";
                var bytes=Encoding.UTF8.GetBytes(line+"\n");
                FileStream? output=null;
                try
                {
                    // Recheck while holding exclusive write access, including across separate BED processes.
                    output=new FileStream(file,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read);
                    if(output.Length+bytes.Length>MaxBytes)
                    {
                        output.Dispose();output=null;File.Move(file,old,true);
                        if(new FileInfo(old).Length>MaxBytes)File.Delete(old);
                        output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                    }
                    output.Seek(0,SeekOrigin.End);output.Write(bytes);
                }
                finally{output?.Dispose();}
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){ }
        }
    }
    internal static string[] Read(string? preferences=null)
    {
        lock(gate)
        {
            var root=preferences??folder??DefaultFolder;var result=new Queue<string>();
            foreach(var name in new[]{"bed.log.1","bed.log"})try
            {
                using var stream=new FileStream(Path.Combine(root,"logs",name),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                if(stream.Length>MaxBytes)continue;
                using var reader=new StreamReader(stream,Encoding.UTF8);string? line;
                while((line=reader.ReadLine())is not null){result.Enqueue(line.Length>2000?line[..2000]:line);while(result.Count>200)result.Dequeue();}
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){ }
            return result.Count>0?result.ToArray():lines.ToArray();
        }
    }
}
