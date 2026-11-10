namespace Brutal.Authoring.Editor.App;

/// <summary>Render-thread mesh ownership shared across the two fence slots. Resources are never replaced in flight.</summary>
public sealed class SceneMeshResources<T>(Func<SceneIndexedGeometry,T> create,long byteLimit=128L*1024*1024) : IDisposable where T:class,IDisposable
{
    private sealed class Entry(T resource,long bytes){internal readonly T Resource=resource;internal readonly long Bytes=bytes;internal readonly HashSet<int> Slots=[];internal readonly HashSet<string> Views=[];}
    private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    private readonly Dictionary<string,HashSet<string>> used=new(StringComparer.Ordinal);
    private readonly int thread=Environment.CurrentManagedThreadId;private int slot=-1;private bool closed;
    /// <summary>Owned native bytes, including retired in-flight buffers.</summary>
    public long Bytes {get;private set;}
    /// <summary>Called only after the host waits this slot's fence.</summary>
    public void Safe(int value)
    {Check();if(value is <0 or >1)throw new ArgumentOutOfRangeException(nameof(value));slot=value;used.Clear();foreach(var e in entries.Values)e.Slots.Remove(value);Reap();}
    /// <summary>Shares identical immutable upload bytes across items/views. No source identity is inferred.</summary>
    public T Acquire(string view,SceneIndexedGeometry geometry)
    {
        Check();if(slot<0)throw new InvalidOperationException("Prepare the fence slot first.");ArgumentException.ThrowIfNullOrWhiteSpace(view);
        if(!entries.TryGetValue(geometry.Key,out var e))
        {
            if(geometry.ByteCount>byteLimit-Bytes)throw new InvalidOperationException("Scene mesh cache exceeds its byte budget.");
            var resource=create(geometry);e=new(resource,geometry.ByteCount);entries.Add(geometry.Key,e);Bytes+=geometry.ByteCount;
        }
        if(!used.TryGetValue(view,out var keys))used.Add(view,keys=[]);keys.Add(geometry.Key);e.Views.Add(view);return e.Resource;
    }
    /// <summary>Marks successful submission and replaces each collected view's retained mesh set.</summary>
    public void Submitted()
    {
        Check();if(slot<0)throw new InvalidOperationException("No frame is collecting.");
        foreach(var pair in used)foreach(var e in entries){if(pair.Value.Contains(e.Key))e.Value.Slots.Add(slot);else e.Value.Views.Remove(pair.Key);}
        slot=-1;used.Clear();Reap();
    }
    /// <summary>Detaches closed views immediately; their in-flight buffers retire only at a fence.</summary>
    public void RetainViews(IReadOnlySet<string> active)
    {Check();foreach(var e in entries.Values)e.Views.RemoveWhere(v=>!active.Contains(v));Reap();}
    private void Reap(){foreach(var pair in entries.Where(p=>p.Value.Views.Count==0&&p.Value.Slots.Count==0&&!used.Values.Any(keys=>keys.Contains(p.Key))).ToArray()){pair.Value.Resource.Dispose();Bytes-=pair.Value.Bytes;entries.Remove(pair.Key);}}
    /// <summary>Host shutdown after WaitIdle, or constructor failure before any submission.</summary>
    public void Dispose(){if(closed)return;Check();foreach(var e in entries.Values)e.Resource.Dispose();entries.Clear();used.Clear();Bytes=0;closed=true;}
    private void Check(){ObjectDisposedException.ThrowIf(closed,this);if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Scene meshes belong to the render thread.");}
}
