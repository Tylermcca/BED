using System.Diagnostics;
using System.Text.Json;
namespace Bed.Editor.ImGui;

// Internal opt-in measurement for real --shot subprocesses. No flag means no record or clock work per frame.
// This changes neither command-line arguments nor the normal stdout contract.
internal sealed class HeadlessStartupTiming
{
    private readonly long began=Stopwatch.GetTimestamp();
    private long previous;
    private readonly double beforeHost;
    private readonly Dictionary<string,double> phases=new(StringComparer.Ordinal);
    private bool completed;
    private int frames;
    private HeadlessStartupTiming()
    {
        using var process=Process.GetCurrentProcess();
        beforeHost=Math.Max(0,(DateTime.UtcNow-process.StartTime.ToUniversalTime()).TotalMilliseconds);
        began=previous=Stopwatch.GetTimestamp();
    }
    internal static HeadlessStartupTiming? Create()=>Environment.GetEnvironmentVariable("BED_HEADLESS_STARTUP_TIMING")=="1"?new():null;
    internal static IDisposable? Preparation(string name)=>Environment.GetEnvironmentVariable("BED_HEADLESS_STARTUP_TIMING")=="1"?new PreparationScope(name):null;
    private sealed class PreparationScope(string name):IDisposable
    {
        private readonly long start=Stopwatch.GetTimestamp();
        public void Dispose()=>Console.Out.WriteLine("BED_STARTUP_PHASE "+JsonSerializer.Serialize(new{name,milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds}));
    }
    internal void Mark(string phase)
    {
        if(completed)return;
        var now=Stopwatch.GetTimestamp();var elapsed=Stopwatch.GetElapsedTime(previous,now).TotalMilliseconds;previous=now;
        phases[phase]=phases.GetValueOrDefault(phase)+elapsed;
    }
    internal void EndFrame(bool drawn,int vertices,int width,int height)
    {
        frames++;
        var elapsed=beforeHost+Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        // A first draw may contain only auto-sizing window chrome. Opt-in receipts retain each
        // frame so a project benchmark can identify when its visible file controls arrive.
        Console.Out.WriteLine("BED_STARTUP_FRAME "+JsonSerializer.Serialize(new {processToFrameMs=elapsed,frames,drawn,vertices}));
        if(completed||!drawn||vertices==0)return;
        completed=true;
        Console.Out.WriteLine("BED_STARTUP_TIMING "+JsonSerializer.Serialize(new
            {processId=Environment.ProcessId,processToFirstFrameMs=elapsed,beforeHeadlessHostMs=beforeHost,frames,vertices,width,height,phases}));
    }
}
