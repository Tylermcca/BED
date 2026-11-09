using Brutal.Authoring.Editor;
using Xunit;
using Reason = Brutal.Authoring.Editor.AssetWatchReconciler.Reason;

namespace Brutal.Authoring.Tests;

public sealed class AssetWatchTests
{
    [Fact]
    public void Manual_refresh_is_explicit_and_idle_drains_do_not_create_work()
    {
        var r=new AssetWatchReconciler();Assert.False(r.TryTake(out _));
        Assert.True(r.RequestRefresh());Assert.True(r.TryTake(out var plan));
        Assert.Equal(Reason.Manual,plan!.Reasons);Assert.True(plan.RequiresFullReconciliation);
        Assert.Equal(1,plan.HintCount);Assert.False(plan.CountSaturated);Assert.False(r.TryTake(out _));
    }
    [Fact]
    public void Burst_is_one_constant_size_plan_with_saturated_diagnostic_count()
    {
        var r=new AssetWatchReconciler(3);
        for(var i=0;i<20;i++)Assert.True(r.Notify(Reason.Changed));
        Assert.True(r.TryTake(out var plan));Assert.Equal(3,plan!.HintCount);Assert.True(plan.CountSaturated);
        Assert.Equal(Reason.Changed,plan.Reasons);Assert.False(r.TryTake(out _));
    }
    [Fact]
    public void Delete_rename_create_and_overflow_are_not_collapsed_into_a_claimed_asset_identity()
    {
        var r=new AssetWatchReconciler();
        foreach(var reason in new[]{Reason.Created,Reason.Deleted,Reason.Renamed,Reason.Overflow})r.Notify(reason);
        Assert.True(r.TryTake(out var plan));
        Assert.Equal(Reason.Created|Reason.Deleted|Reason.Renamed|Reason.Overflow,plan!.Reasons);
        Assert.Equal(4,plan.HintCount);Assert.True(plan.RequiresFullReconciliation);
    }
    [Fact]
    public void Notifications_after_drain_remain_for_next_explicit_batch()
    {
        var r=new AssetWatchReconciler();r.Notify(Reason.Changed);r.TryTake(out var first);
        r.Notify(Reason.Deleted);r.RequestRefresh();Assert.True(r.TryTake(out var next));
        Assert.Equal(Reason.Changed,first!.Reasons);Assert.Equal(Reason.Deleted|Reason.Manual,next!.Reasons);
        Assert.Equal(2,next.HintCount);Assert.False(r.TryTake(out _));
    }
    [Fact]
    public void Closed_admission_ignores_old_callbacks_and_does_not_touch_new_instance()
    {
        var old=new AssetWatchReconciler();old.Notify(Reason.Changed);old.Close();
        var reopened=new AssetWatchReconciler();reopened.RequestRefresh();
        Assert.False(old.Notify(Reason.Overflow));Assert.False(old.TryTake(out _));
        Assert.True(reopened.TryTake(out var plan));Assert.Equal(Reason.Manual,plan!.Reasons);
    }
    [Fact]
    public void Invalid_hints_and_limits_are_refused_without_pending_work()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new AssetWatchReconciler(0));
        var r=new AssetWatchReconciler();
        Assert.Throws<ArgumentOutOfRangeException>(()=>r.Notify(Reason.None));
        Assert.Throws<ArgumentOutOfRangeException>(()=>r.Notify((Reason)64));Assert.False(r.TryTake(out _));
    }
}
