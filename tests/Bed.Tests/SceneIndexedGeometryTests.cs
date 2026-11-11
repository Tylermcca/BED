using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Bed.Editor;
using Bed.Editor.App;
using Xunit;
using Xunit.Abstractions;
namespace Bed.Tests;
public sealed class SceneIndexedGeometryTests(ITestOutputHelper output)
{
    private static MeshPayload Payload(float x=1)=>new([Vector3.Zero,new(x,0,0),Vector3.UnitY],[],[Vector2.Zero,Vector2.UnitX,Vector2.UnitY],[0,1,2],new(Vector4.One));
    private sealed class Resource : IDisposable {public int Disposals;public void Dispose()=>Disposals++;}
    [Fact]
    public void Indexed_bytes_preserve_positions_top_left_uv_indices_and_change_key_only_with_geometry()
    {
        var p=Payload();var g=new SceneIndexedGeometry(p);Assert.Equal(3,g.IndexCount);Assert.Equal(72,g.ByteCount);
        var vertices=MemoryMarshal.Cast<byte,SceneIndexedGeometry.Vertex>(g.CopyVertices());Assert.Equal(p.Positions,vertices.ToArray().Select(v=>v.Position));Assert.Equal(p.Uv0,vertices.ToArray().Select(v=>v.Uv));
        Assert.Equal(p.Indices,MemoryMarshal.Cast<byte,int>(g.CopyIndices()).ToArray());Assert.Equal(g.Key,new SceneIndexedGeometry(Payload()).Key);Assert.NotEqual(g.Key,new SceneIndexedGeometry(Payload(2)).Key);
        var recolor=new MeshPayload(p.Positions,[],p.Uv0,p.Indices,new(new(1,0,0,1)));Assert.Equal(g.Key,new SceneIndexedGeometry(recolor).Key);
        var copy=g.CopyVertices();copy[0]=255;Assert.NotEqual(copy,g.CopyVertices());Assert.Equal(Vector3.Zero,p.Positions[0]);
    }
    [Fact]
    public void Shared_upload_survives_both_fences_and_closed_views_without_reupload_or_early_disposal()
    {
        var uploads=0;using var cache=new SceneMeshResources<Resource>(_=>{uploads++;return new();});var g=new SceneIndexedGeometry(Payload());
        cache.Safe(0);var first=cache.Acquire("scene",g);Assert.Same(first,cache.Acquire("preview",g));cache.Submitted();
        cache.Safe(1);Assert.Same(first,cache.Acquire("scene",new SceneIndexedGeometry(Payload())));cache.Submitted();Assert.Equal(1,uploads);
        cache.RetainViews(new HashSet<string>());Assert.Equal(0,first.Disposals);cache.Safe(0);Assert.Equal(0,first.Disposals);cache.Submitted();cache.Safe(1);Assert.Equal(1,first.Disposals);Assert.Equal(0,cache.Bytes);
    }
    [Fact]
    public void Changed_content_keeps_old_inflight_buffer_and_budget_refusal_does_not_replace_it()
    {
        var g=new SceneIndexedGeometry(Payload());using var cache=new SceneMeshResources<Resource>(_=>new(),g.ByteCount*2);
        cache.Safe(0);var old=cache.Acquire("scene",g);cache.Submitted();cache.Safe(1);var next=cache.Acquire("scene",new SceneIndexedGeometry(Payload(2)));
        Assert.NotSame(old,next);Assert.Throws<InvalidOperationException>(()=>cache.Acquire("other",new SceneIndexedGeometry(Payload(3))));cache.Submitted();Assert.Equal(0,old.Disposals);
        cache.Safe(0);Assert.Equal(1,old.Disposals);Assert.Equal(0,next.Disposals);Assert.Same(next,cache.Acquire("scene",new SceneIndexedGeometry(Payload(2))));
    }
    [Fact]
    public void Removal_during_collection_cannot_free_buffers_before_submission_or_shutdown()
    {
        var cache=new SceneMeshResources<Resource>(_=>new());cache.Safe(0);var resource=cache.Acquire("scene",new(Payload()));cache.RetainViews(new HashSet<string>());Assert.Equal(0,resource.Disposals);
        cache.Submitted();Assert.Equal(0,resource.Disposals);cache.Safe(0);Assert.Equal(1,resource.Disposals);cache.Dispose();Assert.Equal(1,resource.Disposals);
    }
    [Fact]
    public void Record_100k_triangle_cpu_preparation_comparison_not_gpu_frame_time()
    {
        const int columns=500,rows=100;var p=new Vector3[(columns+1)*(rows+1)];for(var y=0;y<=rows;y++)for(var x=0;x<=columns;x++)p[y*(columns+1)+x]=new(x/100f,y/100f,0);
        var indices=new int[columns*rows*6];var at=0;for(var y=0;y<rows;y++)for(var x=0;x<columns;x++){var a=y*(columns+1)+x;indices[at++]=a;indices[at++]=a+1;indices[at++]=a+columns+1;indices[at++]=a+1;indices[at++]=a+columns+2;indices[at++]=a+columns+1;}
        var item=new ViewportItem("synthetic",Matrix4x4.Identity,new(p,indices),0xffffffff);var identity=new SceneRenderIdentity(0,0,1,1,1,Vector2.Zero,new(1280,720),Vector2.One);
        var request=SceneRenderRequest.Capture(identity,new(Vector3.Zero,0,0,10),[item]);Assert.Equal(100000,indices.Length/3);
        var legacy=SceneMaterialDraws.Build(request);Assert.Equal(100000,legacy.Count);var packed=new SceneIndexedGeometry(request.Items[0].Payload);Assert.Equal(300000,packed.IndexCount);
        var before=request.Pick(new(640,360));var source=request.Items[0].Payload.Positions.ToArray();
        Measure("before: CPU triangle packet preparation",()=>{_ = SceneMaterialDraws.Build(request);});
        Measure("after: CPU indexed pack and hash (upload cached separately)",()=>{_ = new SceneIndexedGeometry(request.Items[0].Payload);});
        Assert.Equal(source,request.Items[0].Payload.Positions);Assert.Equal(before.ItemId,request.Pick(new(640,360)).ItemId);
        output.WriteLine("100000 triangles; 1 indexed mesh draw in source versus 100000 triangle draws. CPU preparation only; no Vulkan frame/device/driver timing claim.");
    }
    [Fact]
    public void Indexed_vertices_preserve_legacy_clip_positions_and_uv_for_mirrored_world_and_camera()
    {
        var mesh=new MeshData([new(-1,0,0),new(1,0,0),new(0,1,0)],[0,2,1]){Uv0=[Vector2.Zero,Vector2.UnitX,Vector2.UnitY],Uv0Origin=MeshUv0Origin.ObjBottomLeft};
        var item=new ViewportItem("mesh",Matrix4x4.CreateScale(-2,3,1)*Matrix4x4.CreateTranslation(1,0,0),mesh,0xff4080c0);
        var request=SceneRenderRequest.Capture(new(4,5,6,7,8,Vector2.Zero,new(800,600),Vector2.One),new(Vector3.Zero,.2f,.1f,8),[item]);
        var packed=new SceneIndexedGeometry(request.Items[0].Payload);var v=MemoryMarshal.Cast<byte,SceneIndexedGeometry.Vertex>(packed.CopyVertices()).ToArray();var ids=MemoryMarshal.Cast<byte,int>(packed.CopyIndices()).ToArray();
        var matrix=item.World*(request.Camera.View*request.Camera.Projection(request.Identity.Extent));
        Vector4 Clip(int id){var c=Vector4.Transform(new Vector4(v[id].Position,1),matrix);return new(c.X,-c.Y,c.Z,c.W);}
        var old=Assert.Single(SceneMaterialDraws.Build(request)).Packet;Assert.Equal(old.A,Clip(ids[0]));Assert.Equal(old.B,Clip(ids[1]));Assert.Equal(old.C,Clip(ids[2]));
        Assert.Equal(new Vector2(old.UvAB.X,old.UvAB.Y),v[ids[0]].Uv);Assert.Equal(new Vector2(old.UvAB.Z,old.UvAB.W),v[ids[1]].Uv);Assert.Equal(new Vector2(old.UvCMode.X,old.UvCMode.Y),v[ids[2]].Uv);
        Assert.Equal(new[]{0,2,1},mesh.Indices);Assert.Equal(MeshUv0Origin.ObjBottomLeft,mesh.Uv0Origin);
    }
    private void Measure(string name,Action action)
    {
        action();var times=new double[5];var bytes=new long[5];for(var i=0;i<5;i++){var b=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();action();times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;bytes[i]=GC.GetAllocatedBytesForCurrentThread()-b;}
        Array.Sort(times);Array.Sort(bytes);output.WriteLine($"{name}: n=5 median_ms={times[2]:F3}, median_bytes={bytes[2]}");
    }
}
