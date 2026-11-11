using Bed.Editor.ImGui;
namespace Bed.Editor.App;

// The existing material cache supplies fence retirement; filter variants share its byte ledger.
internal sealed class SpriteTextureCache<T>(object deviceGeneration,SceneTextureBudget budget,Func<AssetPreview,SceneSpriteFilter,T> create,Action<T> submitted,Action<T> uploadCompleted) where T:class,IDisposable
{
    private readonly int thread=Environment.CurrentManagedThreadId;
    private SceneMaterialCache<T>? nearest,linear;
    private readonly Dictionary<string,HashSet<string>> used=new(StringComparer.Ordinal);
    private readonly Dictionary<string,object> descriptorOwners=new(StringComparer.Ordinal);
    private IReadOnlySet<string> activeViews=new HashSet<string>();
    private int slot=-1;private bool closed;
    internal void Safe(int value)
    {Check();if(value is <0 or >1)throw new ArgumentOutOfRangeException(nameof(value));slot=value;used.Clear();nearest?.Safe(value);linear?.Safe(value);
        foreach(var key in descriptorOwners.Keys.Where(k=>!activeViews.Contains(k)&&budget.DescriptorCount(descriptorOwners[k])==0).ToArray())descriptorOwners.Remove(key);
    }
    private SceneMaterialCache<T> Cache(SceneSpriteFilter filter)
    {
        if(!Enum.IsDefined(filter))throw new ArgumentException("Unsupported sprite filter.");
        ref var cache=ref(filter==SceneSpriteFilter.Nearest?ref nearest:ref linear);
        if(cache is null){cache=new(deviceGeneration,budget,p=>create(p,filter),submitted,uploadCompleted,view=>{if(!descriptorOwners.TryGetValue(view,out var owner))descriptorOwners.Add(view,owner=new());return owner;});cache.Safe(slot);}
        return cache;
    }
    internal SceneMaterialCache<T>.Lease Acquire(string view,AssetCatalog catalog,AssetCatalog.Selection selection,SceneSpriteFilter filter)
    {
        Check();if(slot<0)throw new InvalidOperationException("Prepare the sprite frame before collection.");
        ArgumentException.ThrowIfNullOrWhiteSpace(view);var content=SpriteTextureInput.Resolve(catalog,selection);
        if(!used.TryGetValue(view,out var images))used.Add(view,images=new(StringComparer.Ordinal));
        if(!images.Contains(selection.AssetGuid)&&images.Count>=1024)throw new InvalidOperationException("Sprite view exceeds 1,024 unique textures.");
        var lease=Cache(filter).Acquire(view,catalog,selection,content.SourceHash);images.Add(selection.AssetGuid);return lease;
    }
    internal bool IsCurrent(SceneMaterialCache<T>.Lease lease,SceneSpriteFilter filter)
    {Check();return Enum.IsDefined(filter)&&(filter==SceneSpriteFilter.Nearest?nearest:linear)?.IsCurrent(lease)==true;}
    internal IReadOnlyList<T> Collected(){Check();return (nearest?.Collected()??[]).Concat(linear?.Collected()??[]).ToArray();}
    internal void Submitted(){Check();if(slot<0)throw new InvalidOperationException("No sprite frame is collecting.");nearest?.Submitted();linear?.Submitted();slot=-1;}
    internal void RetainViews(IReadOnlySet<string> active){Check();activeViews=new HashSet<string>(active,StringComparer.Ordinal);nearest?.RetainViews(active);linear?.RetainViews(active);}
    internal void Close(){if(closed)return;Check();nearest?.Close();linear?.Close();used.Clear();descriptorOwners.Clear();slot=-1;closed=true;}
    private void Check(){if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Sprite textures belong to the render thread.");ObjectDisposedException.ThrowIf(closed,this);}
}
