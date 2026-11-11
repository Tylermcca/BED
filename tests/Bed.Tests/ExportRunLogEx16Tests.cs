using System.Reflection;
using System.Text;
using Bed.Editor;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;

public sealed class ExportRunLogEx16Tests
{
    private static readonly Type Log=typeof(ExportBuildHost).Assembly.GetType("Bed.Editor.App.ExportRunLog",throwOnError:true)!;
    private static object New(string folder)=>Activator.CreateInstance(Log,BindingFlags.Instance|BindingFlags.NonPublic,null,[folder],null)!;
    private static void Call(object log,string method,object value)=>Log.GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(log,[value]);
    [Fact]
    public void Full_UTF8_output_survives_beyond_the_UI_line_limit_and_appends_are_visible_before_return()
    {
        using var owned=new CapturedExportTests.Owned();var log=New(owned.Root);using var active=(IDisposable)log;var path=Path.Combine(owned.Root,"export.log");
        Call(log,"Phase","Building saved files");
        for(var i=0;i<700;i++)Call(log,"Output",new GameBuildLog(i%3==0,"line "+i+" café"));
        Call(log,"Phase","Checked and copied");
        var lines=File.ReadAllLines(path,new UTF8Encoding(false,true));Assert.Equal(702,lines.Length);
        Assert.Equal("[export] Building saved files",lines[0]);Assert.Equal("[export] Checked and copied",lines[^1]);
        for(var i=0;i<700;i++)Assert.Equal((i%3==0?"[stderr] ":"[stdout] ")+"line "+i+" café",lines[i+1]);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }
    [Fact]
    public void Concurrent_process_streams_remain_complete_lines_and_existing_log_is_never_replaced()
    {
        using var owned=new CapturedExportTests.Owned();var log=New(owned.Root);using var active=(IDisposable)log;
        Parallel.For(0,500,i=>Call(log,"Output",new GameBuildLog(i%2==1,"record-"+i)));
        var path=Path.Combine(owned.Root,"export.log");var before=File.ReadAllBytes(path);var lines=File.ReadAllLines(path);
        Assert.Equal(500,lines.Length);Assert.Equal(500,lines.Distinct().Count());
        for(var i=0;i<500;i++)Assert.Contains((i%2==1?"[stderr] ":"[stdout] ")+"record-"+i,lines);
        Assert.IsType<IOException>(Assert.Throws<TargetInvocationException>(()=>New(owned.Root)).InnerException);
        Assert.Equal(before,File.ReadAllBytes(path));
    }
}
