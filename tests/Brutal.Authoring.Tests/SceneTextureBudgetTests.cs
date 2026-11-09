using Brutal.Authoring.Editor.App;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class SceneTextureBudgetTests
{
    [Fact]
    public void Image_replacement_overlap_counts_until_retirement()
    {
        var b=new SceneTextureBudget();
        using var old=b.Reserve(SceneTextureBudget.ImageLimit-4,0);
        using var replacement=b.Reserve(4,0);
        Assert.Equal(SceneTextureBudget.ImageLimit,b.ImageBytes);
        Assert.Throws<InvalidOperationException>(()=>b.Reserve(1,0));
        old.Dispose();Assert.Equal(4,b.ImageBytes);
        using var next=b.Reserve(SceneTextureBudget.ImageLimit-4,0);
        Assert.Equal(SceneTextureBudget.ImageLimit,b.ImageBytes);
    }
    [Fact]
    public void Staging_is_held_until_explicit_completion_and_released_once()
    {
        var b=new SceneTextureBudget();using var a=b.Reserve(64,SceneTextureBudget.StagingLimit);
        Assert.Throws<InvalidOperationException>(()=>b.Reserve(1,1));
        Assert.Equal(64,b.ImageBytes);Assert.Equal(SceneTextureBudget.StagingLimit,b.StagingBytes);
        a.ReleaseStaging();a.ReleaseStaging();Assert.Equal(0,b.StagingBytes);Assert.Equal(64,b.ImageBytes);
        a.Dispose();a.Dispose();Assert.Equal(0,b.ImageBytes);Assert.Equal(0,b.StagingBytes);
    }
    [Fact]
    public void Failed_reservation_has_no_partial_charge_and_large_values_cannot_overflow()
    {
        var b=new SceneTextureBudget();
        Assert.Throws<InvalidOperationException>(()=>b.Reserve(long.MaxValue,1));
        Assert.Throws<InvalidOperationException>(()=>b.Reserve(1,long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(()=>b.Reserve(0,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>b.Reserve(1,-1));
        Assert.Equal(0,b.ImageBytes);Assert.Equal(0,b.StagingBytes);
    }
    [Fact]
    public void Each_view_has_a_separate_descriptor_cap_and_retirement_reopens_capacity()
    {
        var b=new SceneTextureBudget();var view=new object();var other=new object();
        var leases=Enumerable.Range(0,SceneTextureBudget.DescriptorLimit).Select(_=>b.ReserveDescriptor(view)).ToArray();
        try
        {
            Assert.Throws<InvalidOperationException>(()=>b.ReserveDescriptor(view));
            using var second=b.ReserveDescriptor(other);
            Assert.Equal(1024,b.DescriptorCount(view));Assert.Equal(1,b.DescriptorCount(other));
            leases[0].Dispose();leases[0].Dispose();using var replacement=b.ReserveDescriptor(view);
            Assert.Equal(1024,b.DescriptorCount(view));
        }
        finally {foreach(var lease in leases)lease.Dispose();}
        Assert.Equal(0,b.DescriptorCount(view));Assert.Equal(0,b.DescriptorCount(other));
    }
    [Fact]
    public void Allocation_failure_unwind_releases_both_charges()
    {
        var b=new SceneTextureBudget();
        Assert.Throws<InvalidOperationException>((Action)(()=>
        {
            using var charge=b.Reserve(12,12);
            throw new InvalidOperationException("Synthetic construction failure");
        }));
        Assert.Equal(0,b.ImageBytes);Assert.Equal(0,b.StagingBytes);
    }
    [Fact]
    public void Foreign_thread_cannot_allocate_or_release_owner_resources()
    {
        var b=new SceneTextureBudget();using var charge=b.Reserve(4,4);
        Exception? allocation=null,release=null;
        var thread=new Thread(()=>
        {
            allocation=Record.Exception(()=>b.Reserve(4,4));
            release=Record.Exception(()=>charge.Dispose());
        });thread.Start();thread.Join();
        Assert.IsType<InvalidOperationException>(allocation);Assert.IsType<InvalidOperationException>(release);
        Assert.Equal(4,b.ImageBytes);Assert.Equal(4,b.StagingBytes);
    }
}
