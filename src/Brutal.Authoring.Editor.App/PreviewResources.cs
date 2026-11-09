using Brutal.Authoring.Editor.ImGui;
namespace Brutal.Authoring.Editor.App;

// Actual two-slot lifetime policy used by SceneDeviceOwner. Tests exercise this class without a Vulkan device.
// Safe is called only after the host has waited that slot's fence. Close requires host WaitIdle.
internal sealed class PreviewResources<T>(object deviceGeneration, Func<AssetPreview,T> create) where T : class, IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<object, View> views = new();
    private readonly HashSet<object> collected = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,object> closedViews = new();
    private int slot = -1;
    private long frame;
    private bool closed;
    private sealed class View
    {
        internal bool Closing;
        internal readonly Entry?[] Slots = new Entry?[2];
    }
    private sealed record Entry(AssetImportQueue<AssetPreview> Queue, AssetImportQueue<AssetPreview>.Result Result, T Resource);
    internal sealed record Lease(object DeviceGeneration, long Frame, T Resource, int Width, int Height);
    internal void Safe(int value)
    {
        Check(); if(value is <0 or >1) throw new ArgumentOutOfRangeException(nameof(value));
        slot=value; frame=checked(frame+1); collected.Clear();
        foreach(var pair in views.ToArray())
        {
            if(!pair.Value.Closing) continue;
            pair.Value.Slots[slot]?.Resource.Dispose();pair.Value.Slots[slot]=null;
            if(pair.Value.Slots.All(e=>e is null)) views.Remove(pair.Key);
        }
    }
    internal Lease Acquire(object viewId, AssetImportQueue<AssetPreview> queue)
    {
        Check(); ArgumentNullException.ThrowIfNull(viewId);ArgumentNullException.ThrowIfNull(queue);
        if(slot<0) throw new InvalidOperationException("Prepare the frame slot before preview collection.");
        if(closedViews.TryGetValue(viewId,out _)) throw new InvalidOperationException("Preview admission is closed.");
        var result=queue.LastGood;
        if(result is null || !queue.LastGoodIsCurrent) throw new InvalidOperationException("Preview decode is not current.");
        if(!views.TryGetValue(viewId,out var view))
        {
            if(views.Count>=2) throw new InvalidOperationException("Initial preview budget is two views.");
            view=new();views.Add(viewId,view);
        }
        if(view.Closing) throw new InvalidOperationException("Preview admission is closed.");
        var old=view.Slots[slot];
        if(old is null || !ReferenceEquals(old.Queue,queue) || !ReferenceEquals(old.Result,result))
        {
            // Earlier ImGui draw data may already reference this slot. Never replace it twice in one collection.
            if(collected.Contains(viewId)) throw new InvalidOperationException("Preview changed during this frame; collect it next frame.");
            T next;
            try { next=create(result.Value); }
            catch { if(view.Slots.All(e=>e is null)) views.Remove(viewId);throw; }
            if(!queue.LastGoodIsCurrent || !ReferenceEquals(queue.LastGood,result))
            { next.Dispose(); throw new InvalidOperationException("Preview decode was superseded during allocation."); }
            view.Slots[slot]=new(queue,result,next);old?.Resource.Dispose();
        }
        if(!queue.LastGoodIsCurrent || !ReferenceEquals(queue.LastGood,result)) throw new InvalidOperationException("Preview decode is no longer current.");
        collected.Add(viewId);
        return new(deviceGeneration,frame,view.Slots[slot]!.Resource,result.Value.Width,result.Value.Height);
    }
    internal bool IsCurrent(Lease lease)=>!closed && slot>=0 && ReferenceEquals(lease.DeviceGeneration,deviceGeneration) && lease.Frame==frame;
    internal IEnumerable<T> Collected()
    {
        Check();return collected.Select(id=>views[id].Slots[slot]!.Resource).ToArray();
    }
    internal void Submitted(){Check();slot=-1;collected.Clear();}
    // Detach future requests immediately, retire each slot only on its next fence-safe turn.
    internal void CloseView(object viewId){Check();ArgumentNullException.ThrowIfNull(viewId);closedViews.GetValue(viewId,_=>new());if(views.TryGetValue(viewId,out var view))view.Closing=true;}
    internal void Close()
    {
        if(closed)return;Check();closed=true;slot=-1;
        foreach(var view in views.Values) foreach(var entry in view.Slots) entry?.Resource.Dispose();
        views.Clear();collected.Clear();
    }
    private void Check()
    {
        if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Preview resources belong to the render thread.");
        ObjectDisposedException.ThrowIf(closed,this);
    }
}
