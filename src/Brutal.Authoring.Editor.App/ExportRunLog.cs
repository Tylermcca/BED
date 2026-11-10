using System.Text;
using Brutal.Authoring.Editor;
namespace Brutal.Authoring.Editor.App;

// One owner-chosen run folder, outside the exact game output inventory. Every append is flushed before returning.
internal sealed class ExportRunLog:IDisposable
{
    private readonly object gate=new();
    private FileStream? active;
    internal string Path {get;}
    internal ExportRunLog(string runFolder)
    {
        if(!System.IO.Path.IsPathFullyQualified(runFolder)||!Directory.Exists(runFolder)
            ||(File.GetAttributes(runFolder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose an owned export run folder for the log.");
        Path=System.IO.Path.Combine(runFolder,"export.log");
        using var stream=new FileStream(Path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        active=new FileStream(System.IO.Path.Combine(runFolder,"active.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
    }
    internal IDisposable Acquire()=>new FileStream(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,"active.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
    public void Dispose(){lock(gate){active?.Dispose();active=null;}}
    internal void Output(GameBuildLog line)=>Write(line.Error?"stderr":"stdout",line.Text);
    internal void Phase(string text)=>Write("export",text);
    private void Write(string kind,string text)
    {
        lock(gate)
        {
            using var stream=new FileStream(Path,FileMode.Append,FileAccess.Write,FileShare.Read);
            using var writer=new StreamWriter(stream,new UTF8Encoding(false));
            writer.Write('[');writer.Write(kind);writer.Write("] ");writer.WriteLine(text);
        }
    }
}
