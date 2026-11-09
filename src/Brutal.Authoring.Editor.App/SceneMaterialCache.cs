using Brutal.Authoring.Editor.ImGui;

namespace Brutal.Authoring.Editor.App;

// Render-thread, same-device cache. Safe is a completed host fence; Close requires host WaitIdle.
internal sealed class SceneMaterialCache<T>(object deviceGeneration, SceneTextureBudget budget, Func<AssetPreview,T> create,
    Action<T> submitted, Action<T> uploadCompleted) where T:class,IDisposable
{
    private readonly int thread=Environment.CurrentManagedThreadId;
    private readonly Dictionary<Key,Entry> entries=new();
    private readonly Dictionary<string,View> views=new(StringComparer.Ordinal);
    private readonly List<(Binding Binding,HashSet<int> Slots)> retired=new();
    private readonly HashSet<Entry> collected=new();
    private readonly HashSet<(string View,string Asset)> used=new();
    private int slot=-1;private long frame;private bool closed;
    internal sealed record Key(Guid Admission,string Asset,string Content,string Policy);
    internal sealed class Entry(Key key,T resource,int width,int height)
    {
        internal readonly Key Key=key;internal readonly T Resource=resource;
        internal readonly int Width=width,Height=height;
        internal readonly HashSet<int> InFlight=new();internal int References;internal int? UploadSlot;internal bool Uploaded;
    }
    private sealed class View { internal readonly object DescriptorOwner=new();internal readonly Dictionary<string,Binding> Bindings=new(StringComparer.Ordinal); }
    private sealed record Binding(Entry Entry,SceneTextureBudget.Descriptor Descriptor);
    internal sealed record Lease(object DeviceGeneration,long Frame,T Resource,int Width,int Height,
        AssetCatalog Catalog,AssetCatalog.Selection Selection,string SourceHash,string ViewId);

    internal void Safe(int value)
    {
        Check();if(value is <0 or >1)throw new ArgumentOutOfRangeException(nameof(value));
        slot=value;frame=checked(frame+1);collected.Clear();used.Clear();
        foreach(var entry in entries.Values)
        {
            entry.InFlight.Remove(value);
            if(entry.UploadSlot==value){uploadCompleted(entry.Resource);entry.UploadSlot=null;}
        }
        foreach(var item in retired.ToArray())
        {
            item.Slots.Remove(value);if(item.Slots.Count!=0)continue;
            item.Binding.Descriptor.Dispose();item.Binding.Entry.References--;retired.Remove(item);
        }
        Reap();
    }
    internal Lease Acquire(string viewId,AssetCatalog catalog,AssetCatalog.Selection selection,string sourceHash)
    {
        Check();if(slot<0)throw new InvalidOperationException("Prepare the frame before material texture collection.");
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        if(!catalog.TryResolveTexture(selection,sourceHash,out var content,out var why))throw new InvalidOperationException(why);
        var key=new Key(catalog.Admission,selection.AssetGuid,content!.ContentRevision,content.PolicyRevision);
        if(!views.TryGetValue(viewId,out var view)){view=new();views.Add(viewId,view);}
        view.Bindings.TryGetValue(selection.AssetGuid,out var old);
        if(old is not null&&old.Entry.Key==key)
        {collected.Add(old.Entry);used.Add((viewId,selection.AssetGuid));return Make(old.Entry,catalog,selection,sourceHash,viewId);}
        if(used.Contains((viewId,selection.AssetGuid)))throw new InvalidOperationException("The material changed during this frame; collect it next frame.");
        var descriptor=budget.ReserveDescriptor(view.DescriptorOwner);
        Entry? added=null;
        try
        {
            if(!entries.TryGetValue(key,out var entry))
            {
                var bytes=new byte[content.Length];content.CopyTo(bytes);
                if(!AssetPreview.TryCreate(content.Width!.Value,content.Height!.Value,bytes,bytes.Length,int.MaxValue,int.MaxValue,int.MaxValue,out var pixels,out why))
                    throw new InvalidOperationException(why);
                entry=added=new(key,create(pixels!),pixels!.Width,pixels.Height);
            }
            if(!catalog.TryResolveTexture(selection,sourceHash,out var current,out why)||current!.ContentRevision!=key.Content||current.PolicyRevision!=key.Policy)
                throw new InvalidOperationException(why.Length==0?"Texture changed during allocation.":why);
            if(added is not null)entries.Add(key,added);
            entry.References++;view.Bindings[selection.AssetGuid]=new(entry,descriptor);
            if(old is not null)Retire(old);
            collected.Add(entry);used.Add((viewId,selection.AssetGuid));return Make(entry,catalog,selection,sourceHash,viewId);
        }
        catch{descriptor.Dispose();added?.Resource.Dispose();throw;}
    }
    private Lease Make(Entry entry,AssetCatalog catalog,AssetCatalog.Selection selection,string sourceHash,string viewId)=>
        new(deviceGeneration,frame,entry.Resource,entry.Width,entry.Height,catalog,selection,sourceHash,viewId);
    internal bool IsCurrent(Lease lease)
    {
        Check();return slot>=0&&ReferenceEquals(lease.DeviceGeneration,deviceGeneration)&&lease.Frame==frame
            &&views.TryGetValue(lease.ViewId,out var view)&&view.Bindings.TryGetValue(lease.Selection.AssetGuid,out var binding)
            &&ReferenceEquals(binding.Entry.Resource,lease.Resource)&&lease.Catalog.TryResolveTexture(lease.Selection,lease.SourceHash,out _,out _);
    }
    internal IReadOnlyList<T> Collected(){Check();return collected.Select(e=>e.Resource).ToArray();}
    internal void Submitted()
    {
        Check();if(slot<0)throw new InvalidOperationException("No material frame is collecting.");
        foreach(var entry in collected)
        {
            entry.InFlight.Add(slot);
            if(!entry.Uploaded){submitted(entry.Resource);entry.Uploaded=true;entry.UploadSlot=slot;}
        }
        slot=-1;collected.Clear();used.Clear();
    }
    private void Retire(Binding binding)
    {
        var slots=new HashSet<int>(binding.Entry.InFlight);
        if(slot>=0&&collected.Contains(binding.Entry))slots.Add(slot);
        retired.Add((binding,slots));
    }
    internal void RetainViews(IReadOnlySet<string> active)
    {
        Check();foreach(var name in views.Keys.ToArray())
        {
            var view=views[name];
            foreach(var id in view.Bindings.Keys.Where(id=>!active.Contains(name)||!used.Contains((name,id))).ToArray())
            {Retire(view.Bindings[id]);view.Bindings.Remove(id);}
            if(!active.Contains(name))views.Remove(name);
        }
    }
    private void Reap()
    {
        foreach(var pair in entries.Where(p=>p.Value.References==0&&p.Value.InFlight.Count==0&&!collected.Contains(p.Value)).ToArray())
        {pair.Value.Resource.Dispose();entries.Remove(pair.Key);}
    }
    internal void Close()
    {
        if(closed)return;Check();closed=true;
        foreach(var view in views.Values)foreach(var binding in view.Bindings.Values)binding.Descriptor.Dispose();
        foreach(var item in retired)item.Binding.Descriptor.Dispose();
        foreach(var entry in entries.Values)entry.Resource.Dispose();
        views.Clear();retired.Clear();entries.Clear();collected.Clear();slot=-1;
    }
    private void Check()
    {
        if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Material textures belong to the render thread.");
        ObjectDisposedException.ThrowIf(closed,this);
    }
}
