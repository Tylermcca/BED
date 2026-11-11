using System.Diagnostics;
using System.Text.Json;
namespace Bed.Editor.App;

// Scoped to the optional offscreen startup measurement. No ordinary startup logging or per-asset work.
internal sealed class StartupPreparationTiming:IDisposable
{
    private readonly long start=Stopwatch.GetTimestamp();
    private StartupPreparationTiming() { }
    internal static StartupPreparationTiming? Create()=>Environment.GetEnvironmentVariable("BED_HEADLESS_STARTUP_TIMING")=="1"?new():null;
    public void Dispose()=>Console.Out.WriteLine("BED_STARTUP_PHASE "+JsonSerializer.Serialize(new{name="asset_preparation_ms",milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds}));
}
