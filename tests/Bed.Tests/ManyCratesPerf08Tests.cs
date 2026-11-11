using System.Diagnostics;
using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

// Bounded measurement, not a GPU timing or an achieved frame-rate assertion.
[Collection(ImGuiCollection.Name)]
public sealed class ManyCratesPerf08Tests(ITestOutputHelper output)
{
    private const string Model="c8a7e000000000000000000000000001",Texture="c8a7e000000000000000000000000002";
    // Observe the production cache's allocation boundary without creating a window/device.
    private sealed class Upload(SceneIndexedGeometry geometry):IDisposable
    {
        public readonly byte[] Vertices=geometry.CopyVertices(),Indices=geometry.CopyIndices();
        public int Disposals;
        public void Dispose()=>Disposals++;
    }
    [Fact]
    public void Five_hundred_catalog_crates_draw_offscreen_and_keep_one_immutable_indexed_upload_across_frames()
    {
        var root=Directory.CreateTempSubdirectory("many-crates-perf08-").FullName;
        try
        {
            var sample=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter");
            var sourceFiles=Directory.GetFiles(Path.Combine(sample,"assets"),"*",SearchOption.AllDirectories);
            foreach(var source in sourceFiles)
            {
                var destination=Path.Combine(root,"assets",Path.GetRelativePath(Path.Combine(sample,"assets"),source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,destination);
            }
            var originals=Directory.GetFiles(Path.Combine(root,"assets"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var scene=new Scene([..Enumerable.Range(0,500).Select(i=>SceneObject.At("crate_"+i,Model,new((i%25-12)*2,0,(i/25-10)*2)))]);
            var level=Path.Combine(data,"level.json");var saved=new SceneFormat().Write(scene);File.WriteAllText(level,saved);
            using var assets=new AssetCatalogProvisioner().Open(new(data,schemas))!;Assert.Empty(assets.Problems);
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.BindSceneAssets(assets.Browser,assets.Catalog));
            Assert.True(workspace.Documents.BindScene("level.json","scene",out var why),why);Assert.True(workspace.Show("level.json"),workspace.Said);
            var view=workspace.SceneViewport!;view.Camera=new(Vector3.Zero,.3f,.65f,65);
            using var host=new HeadlessHost(960,640){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
            void Draw()
            {
                ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(960,640));
                Ui.Begin("500 catalog crates"u8);view.Draw();Ui.End();
            }
            host.Frames(Draw,3); // fixed warm-up
            Assert.Equal(500,view.RenderRequest!.Items.Count);
            Assert.All(view.RenderRequest.Items,item=>
            {
                var part=Assert.Single(item.Payloads);Assert.Equal(20,part.Positions.Count);Assert.Equal(36,part.Indices.Count);Assert.Equal(Texture,part.Material.TextureAssetGuid);
            });
            Assert.Equal(500,Ui.Snapshot().Nodes.Count(n=>n.Role==UiRole.ListItem&&n.Label.StartsWith("crate_",StringComparison.Ordinal)));
            var uploads=new List<Upload>();using var cache=new SceneMeshResources<Upload>(g=>{var upload=new Upload(g);uploads.Add(upload);return upload;});
            var collection=new List<double>();var frames=new List<double>();var frameUploads=new List<int>();var drawCounts=new List<int>();
            Upload? first=null;
            for(var frame=0;frame<8;frame++)
            {
                // A new capture and camera matrix every frame must not change immutable model-space upload bytes.
                view.Camera=view.Camera.Orbit(.001f,0);host.Drawing=true;
                var clock=Stopwatch.GetTimestamp();host.Frame(Draw);frames.Add(Stopwatch.GetElapsedTime(clock).TotalMilliseconds);
                var request=view.RenderRequest!;var previousUploads=uploads.Count;var draws=0;
                clock=Stopwatch.GetTimestamp();cache.Safe(frame%2);
                foreach(var item in request.Items)
                    foreach(var part in item.Payloads)
                    {
                        if(part.Indices.Count==0)continue;
                        var resource=cache.Acquire("scene",new SceneIndexedGeometry(part));first??=resource;Assert.Same(first,resource);draws++;
                    }
                cache.Submitted();collection.Add(Stopwatch.GetElapsedTime(clock).TotalMilliseconds);
                frameUploads.Add(uploads.Count-previousUploads);drawCounts.Add(draws);
                Assert.Equal(500,draws);Assert.Equal(frame==0?1:0,frameUploads[^1]);Assert.Equal(0,first!.Disposals);
            }
            var mesh=Assert.Single(uploads);Assert.Equal(400,mesh.Vertices.Length);Assert.Equal(144,mesh.Indices.Length);Assert.Equal(544,cache.Bytes);
            Assert.All(drawCounts,n=>Assert.Equal(500,n));Assert.Equal(new[]{1,0,0,0,0,0,0,0},frameUploads);
            // This is an actual rasterized frame, not only request capture/Draw=false submission.
            Assert.True(host.Pixels.Distinct().Take(32).Count()>16);Assert.Contains("Material textures are not displayed.",view.RenderStatus);
            Assert.Equal(saved,File.ReadAllText(level));Assert.Empty(workspace.Project.Dirty);
            foreach(var (path,bytes) in originals)Assert.Equal(bytes,File.ReadAllBytes(path));
            output.WriteLine("500 real catalog crates, 6000 triangles; viewport=960x640; 3 fixed warm-up frames, 8 drawn samples, moving camera.");
            Report("CPU HeadlessHost offscreen frame (fallback, no material sampling)",frames);
            Report("Production indexed pack/hash/cache collection (managed upload sink, no driver)",collection);
            output.WriteLine("Indexed native draw plan: "+string.Join(",",drawCounts)+" draws/frame (one part per crate, not one per triangle).");
            output.WriteLine("Mesh resource allocations: "+string.Join(",",frameUploads)+"; first=1 shared vertex/index pair, 544 bytes; later=0 new pairs/0 bytes.");
            output.WriteLine("No Vulkan device/command submission/GPU frame-time claim. SceneRendererLifecycleTests pins the compiled DrawIndexed consumer separately.");
            cache.RetainViews(new HashSet<string>());cache.Safe(0);cache.Submitted();Assert.Equal(0,mesh.Disposals);
            cache.Safe(1);cache.Submitted();Assert.Equal(1,mesh.Disposals);Assert.Equal(0,cache.Bytes);
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);
        }
    }
    private void Report(string phase,List<double> samples)
    {
        output.WriteLine(phase+": ms=["+string.Join(",",samples.Select(ms=>ms.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)))+"]");
        var ordered=samples.Order().ToArray();output.WriteLine($"  median={(ordered[3]+ordered[4])/2:F3} ms; max={ordered[^1]:F3} ms");
    }
}
