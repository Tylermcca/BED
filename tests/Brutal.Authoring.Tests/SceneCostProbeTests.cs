using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

// Measurement harness, not an achieved-performance assertion. Never included in an ordinary broad acceptance claim.
[Collection(ImGuiCollection.Name)]
public sealed class SceneCostProbeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void Record_synthetic_cpu_submission_and_pick_costs(int count)
    {
        var scene=new Scene([..Enumerable.Range(0,count).Select(i=>SceneObject.At("item_"+i,null,new Vector3(i%25,0,i/25)))]);
        var source=new SceneViewport();
        var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);
        var editor=new EditorModel<Scene>(session,SceneEditing.Outline);
        var view=new ViewportView<Scene>(editor,source);
        using var host=new HeadlessHost(1280,800){Drawing=false};
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0,0));
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(1280,800));
            Ui.Begin("Synthetic viewport");view.Draw();Ui.End();
        }
        host.Frames(Draw,3); // fixed warm-up, not selected on measured outcomes
        var items=source.Items(scene).ToArray();Assert.Equal(count,items.Length);
        var ray=new Ray(new Vector3(0,0,-10),Vector3.UnitZ);
        Assert.Equal("item_0",Picking.Pick(items,ray)!.Value.Id);
        output.WriteLine($"host={RuntimeInformation.OSDescription}; framework={RuntimeInformation.FrameworkDescription}; process={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; objects={count}; triangles={count*12}; backend=HeadlessHost CPU ImGui submission, Drawing=false; viewport=1280x800");
        Measure("items/world transforms",()=>{_ = source.Items(scene).ToArray();});
        Measure("pick/prebuilt items",()=>{_ = Picking.Pick(items,ray);});
        Measure("frame/submission+tree",()=>host.Frame(Draw));
        Assert.Empty(session.Journal);Assert.False(session.Dirty);
    }
    private void Measure(string phase,Action action)
    {
        const int samples=12;var ms=new double[samples];var bytes=new long[samples];
        for(var i=0;i<samples;i++)
        {
            var before=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
            action();ms[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            bytes[i]=GC.GetAllocatedBytesForCurrentThread()-before;
        }
        Array.Sort(ms);Array.Sort(bytes);
        output.WriteLine($"phase={phase}; n={samples}; median_ms={(ms[5]+ms[6])/2:F4}; max_ms={ms[^1]:F4}; median_managed_bytes={(bytes[5]+bytes[6])/2}; max_managed_bytes={bytes[^1]}");
    }
}
