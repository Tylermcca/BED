using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;
public sealed class PreviewResourceTests
{
    private sealed class Resource : IDisposable { internal int Disposals; public void Dispose()=>Disposals++; }
    private static AssetPreview Pixels(int width=1)
    {
        Assert.True(AssetPreview.TryCreate(width,1,new byte[width*4],4,64,32,128,out var value,out _));return value!;
    }
    private static AssetImportQueue<AssetPreview> Ready(string revision="one",int width=1)
    {
        var q=new AssetImportQueue<AssetPreview>();Complete(q,revision,width);return q;
    }
    private static void Complete(AssetImportQueue<AssetPreview> q,string revision,int width=1)
    {q.Request(revision);Assert.True(q.TryStart(out var ticket));Assert.True(q.Complete(ticket!,Pixels(width),out _));}
    [Fact]
    public void Slot_resize_retires_only_safe_slot_and_preserves_other_in_flight_image()
    {
        var allocated=new List<Resource>();var owner=new PreviewResources<Resource>(new(),_=>{var r=new Resource();allocated.Add(r);return r;});
        var q=Ready();var id=new object();owner.Safe(0);var a=owner.Acquire(id,q);owner.Submitted();Assert.False(owner.IsCurrent(a));
        owner.Safe(1);var b=owner.Acquire(id,q);owner.Submitted();Complete(q,"two",2);
        owner.Safe(0);var c=owner.Acquire(id,q);Assert.Equal(2,c.Width);Assert.Equal(1,a.Resource.Disposals);Assert.Equal(0,b.Resource.Disposals);
        owner.Submitted();owner.Safe(1);owner.Acquire(id,q);Assert.Equal(1,b.Resource.Disposals);
        owner.Close();owner.Close();Assert.All(allocated,r=>Assert.Equal(1,r.Disposals));
    }
    [Fact]
    public void Canceled_or_foreign_decode_never_allocates_and_frame_lease_expires()
    {
        int allocations=0;var owner=new PreviewResources<Resource>(new(),_=>{allocations++;return new();});var q=Ready();var id=new object();
        Assert.Throws<InvalidOperationException>(()=>owner.Acquire(id,q));owner.Safe(0);var lease=owner.Acquire(id,q);Assert.True(owner.IsCurrent(lease));
        q.Request("pending");Assert.Throws<InvalidOperationException>(()=>owner.Acquire(id,q));Assert.Equal(1,allocations);
        q.Cancel();Assert.Throws<InvalidOperationException>(()=>owner.Acquire(id,q));owner.Submitted();Assert.False(owner.IsCurrent(lease));owner.Close();
    }
    [Fact]
    public void Allocation_failure_preserves_last_complete_resource_without_relabelling()
    {
        var fail=false;var resource=new Resource();var owner=new PreviewResources<Resource>(new(),_=>fail?throw new IOException("allocation"):resource);
        var q=Ready();var id=new object();owner.Safe(0);owner.Acquire(id,q);owner.Submitted();Complete(q,"two");fail=true;
        owner.Safe(0);Assert.Throws<IOException>(()=>owner.Acquire(id,q));Assert.Equal(0,resource.Disposals);owner.Close();Assert.Equal(1,resource.Disposals);
    }
    [Fact]
    public void Superseded_during_allocation_disposes_candidate_not_old_image()
    {
        var q=Ready();var candidate=new Resource();var owner=new PreviewResources<Resource>(new(),_=>{q.Request("newer");return candidate;});owner.Safe(0);
        Assert.Throws<InvalidOperationException>(()=>owner.Acquire(new(),q));Assert.Equal(1,candidate.Disposals);owner.Close();Assert.Equal(1,candidate.Disposals);
    }
    [Fact]
    public void Same_frame_change_cannot_destroy_a_previously_collected_texture()
    {
        var owner=new PreviewResources<Resource>(new(),_=>new());var q=Ready();var id=new object();owner.Safe(0);var old=owner.Acquire(id,q);
        Complete(q,"two");Assert.Throws<InvalidOperationException>(()=>owner.Acquire(id,q));Assert.Equal(0,old.Resource.Disposals);owner.Close();
    }
    [Fact]
    public void Close_detaches_requests_but_retires_each_slot_only_after_its_fence()
    {
        var owner=new PreviewResources<Resource>(new(),_=>new());var q=Ready();var id=new object();owner.Safe(0);var a=owner.Acquire(id,q);owner.Submitted();
        owner.Safe(1);var b=owner.Acquire(id,q);owner.Submitted();owner.CloseView(id);Assert.Equal(0,a.Resource.Disposals);Assert.Equal(0,b.Resource.Disposals);
        owner.Safe(0);Assert.Equal(1,a.Resource.Disposals);Assert.Equal(0,b.Resource.Disposals);Assert.Throws<InvalidOperationException>(()=>owner.Acquire(id,q));
        owner.Submitted();owner.Safe(1);Assert.Equal(1,b.Resource.Disposals);owner.Close();
    }
    [Fact]
    public void New_device_owner_rejects_old_leases_and_two_view_budget_is_enforced()
    {
        var a=new PreviewResources<Resource>(new(),_=>new());var b=new PreviewResources<Resource>(new(),_=>new());a.Safe(0);b.Safe(0);
        var lease=a.Acquire(new(),Ready());Assert.False(b.IsCurrent(lease));a.Acquire(new(),Ready());Assert.Throws<InvalidOperationException>(()=>a.Acquire(new(),Ready()));
        a.Close();Assert.False(a.IsCurrent(lease));Assert.Throws<ObjectDisposedException>(()=>a.Safe(0));b.Close();
    }
    [Fact]
    public void Foreign_queue_with_same_numeric_generation_replaces_only_at_safe_boundary()
    {
        var owner=new PreviewResources<Resource>(new(),_=>new());var id=new object();owner.Safe(0);var first=owner.Acquire(id,Ready());owner.Submitted();
        owner.Safe(0);var second=owner.Acquire(id,Ready());Assert.NotSame(first.Resource,second.Resource);Assert.Equal(1,first.Resource.Disposals);owner.Close();
    }
    [Fact]
    public void Closed_admission_cannot_reappear_after_both_slots_retire_or_before_first_upload()
    {
        var owner=new PreviewResources<Resource>(new(),_=>new());var id=new object();var q=Ready();owner.Safe(0);owner.Acquire(id,q);owner.Submitted();
        owner.CloseView(id);owner.Safe(0);owner.Submitted();owner.Safe(1);
        Assert.Throws<InvalidOperationException>(()=>owner.Acquire(id,q));
        var neverOpened=new object();owner.CloseView(neverOpened);Assert.Throws<InvalidOperationException>(()=>owner.Acquire(neverOpened,Ready()));
        owner.Acquire(new object(),Ready());owner.Close();
    }
}
