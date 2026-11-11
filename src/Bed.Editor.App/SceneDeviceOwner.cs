using Bed.Editor;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
namespace Bed.Editor.App;

// Borrowed host handles. No independent queue submission or host-device disposal.
internal sealed class SceneDeviceOwner(DeviceEx device, PhysicalDevice physical, Queue queue)
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private bool closed;
    private SceneMeshResources<SceneMeshBuffer>? meshResources;
    private SceneMeshResources<SceneMeshBuffer> Meshes=>meshResources??=new(g=>new SceneMeshBuffer(device,g));
    private static readonly SceneIndexedGeometry FallbackMesh=new(new MeshPayload(
        [new(-.65f,-.6f,.4f),new(.65f,-.6f,.4f),new(0,.65f,.4f)],[],[],[0,1,2],new(System.Numerics.Vector4.One)));
    private readonly object deviceGeneration=new();
    private readonly SceneTextureBudget materialBudget=new();
    private SceneMaterialCache<SceneMaterialTexture>? materials;
    private SceneMaterialCache<SceneMaterialTexture> Materials
    {
        get
        {
            if(materials is null)
            {
                materials=new(deviceGeneration,materialBudget,pixels=>new SceneMaterialTexture(device,physical,materialBudget,pixels),
                    texture=>texture.Submitted(),texture=>texture.UploadCompleted());
                if(slot>=0)materials.Safe(slot);
            }
            return materials;
        }
    }
    internal SceneMaterialCache<SceneMaterialTexture>.Lease Material(string view,AssetCatalog catalog,AssetCatalog.Selection selection,string sourceHash)
    {Check();_ = SafeSlot;return Materials.Acquire(view,catalog,selection,sourceHash);}
    internal bool TryMaterialView(SceneMaterialCache<SceneMaterialTexture>.Lease lease,out VkImageView view)
    {Check();view=default;if(materials is null||!materials.IsCurrent(lease))return false;view=lease.Resource.View;return true;}

    private SpriteTextureCache<PreviewTexture>? spriteTextures;
    private SpriteTextureCache<PreviewTexture> Sprites
    {
        get
        {
            if(spriteTextures is null)
            {
                spriteTextures=new(deviceGeneration,materialBudget,(pixels,filter)=>new PreviewTexture(device,physical,backend??throw new InvalidOperationException("Scene backend is not attached."),pixels,filter,materialBudget,8192),
                    texture=>texture.Submitted(),texture=>texture.UploadCompleted());
                if(slot>=0)spriteTextures.Safe(slot);
            }
            return spriteTextures;
        }
    }
    internal SceneMaterialCache<PreviewTexture>.Lease Sprite(string view,AssetCatalog catalog,AssetCatalog.Selection selection,SceneSpriteFilter filter)
    {Check();_=SafeSlot;return Sprites.Acquire(view,catalog,selection,filter);}
    internal bool SpriteCurrent(SceneMaterialCache<PreviewTexture>.Lease lease,SceneSpriteFilter filter)
    {Check();return spriteTextures?.IsCurrent(lease,filter)==true;}

    private PreviewResources<PreviewTexture>? previews;
    private PreviewResources<PreviewTexture> Previews=>previews??=new(deviceGeneration,pixels=>new(device,physical,backend??throw new InvalidOperationException("Scene backend is not attached."),pixels));
    internal PreviewResources<PreviewTexture>.Lease Preview(object admission, AssetImportQueue<Bed.Editor.ImGui.AssetPreview> decoded)
    { Check();_ = SafeSlot; return Previews.Acquire(admission,decoded); }
    internal bool TryPreviewTexture(PreviewResources<PreviewTexture>.Lease lease, out Brutal.ImGuiApi.ImTextureRef texture)
    { Check();texture=default;if(previews is null || !previews.IsCurrent(lease))return false;texture=lease.Resource.Texture;return true; }
    internal void ClosePreview(object admission){Check();previews?.CloseView(admission);}

    private Brutal.ImGuiApi.Backends.VulkanBackend? backend;
    private readonly HashSet<SceneTarget> requested=new();
    internal void Attach(Brutal.ImGuiApi.Backends.VulkanBackend value){Check();if(backend is not null)throw new InvalidOperationException("Backend already attached.");backend=value;}
    internal void Record(CommandBuffer cb){Check();if(materials is not null)foreach(var texture in materials.Collected())texture.Record(cb);if(spriteTextures is not null)foreach(var texture in spriteTextures.Collected())texture.Record(cb);foreach(var target in requested)target.Record(cb);foreach(var texture in Previews.Collected())texture.Record(cb);requested.Clear();}
    private readonly Dictionary<(string View,int Slot),SceneTarget> targets=new();
    private int slot = -1;
    internal DeviceEx Device { get { Check(); return device; } }
    internal PhysicalDevice Physical { get { Check(); return physical; } }
    internal Queue Queue { get { Check(); return queue; } }
    internal int SafeSlot { get { Check(); if(slot<0) throw new InvalidOperationException("Prepare the frame slot before scene collection."); return slot; } }
    internal void Safe(int frame) { Check(); slot=frame; requested.Clear();Previews.Safe(frame);materials?.Safe(frame);spriteTextures?.Safe(frame);Meshes.Safe(frame); }
    internal void Submitted() { Check();foreach(var texture in Previews.Collected())texture.Submitted();Previews.Submitted();materials?.Submitted();spriteTextures?.Submitted();meshResources?.Submitted();slot=-1; }
    internal SceneTarget Target(string view,int width,int height)
    {
        Check();ArgumentException.ThrowIfNullOrWhiteSpace(view);var key=(view,SafeSlot);
        if(targets.TryGetValue(key,out var old)&&old.Width==width&&old.Height==height){old.ResetRequest();old.Meshes=new Dictionary<(string Item,int Part),SceneMeshBuffer>{{("fallback",0),Meshes.Acquire(view,FallbackMesh)}};requested.Add(old);return old;}
        if(!targets.Keys.Any(k=>k.View==view)&&targets.Keys.Select(k=>k.View).Distinct().Count()>=2)
            throw new InvalidOperationException("Initial scene target budget is two views, one attachment set per frame slot.");
        var next=new SceneTarget(device,physical,backend??throw new InvalidOperationException("Scene backend is not attached."),width,height);targets[key]=next;old?.Dispose();next.Meshes=new Dictionary<(string Item,int Part),SceneMeshBuffer>{{("fallback",0),Meshes.Acquire(view,FallbackMesh)}};requested.Add(next);return next;
    }
    internal SceneTarget Target(string view, SceneRenderRequest request, Func<SceneRenderIdentity> current,AssetCatalog? catalog=null)
    {
        Check(); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(current);
        if(request.Identity != current())throw new InvalidOperationException("The scene render request is stale.");
        var identity=request.Identity;
        var width=checked((int)MathF.Ceiling(identity.Extent.X*identity.FramebufferScale.X));
        var height=checked((int)MathF.Ceiling(identity.Extent.Y*identity.FramebufferScale.Y));
        var target=Target(view,width,height); // Enforce the two-view/extent bound before any material allocation.
        var geometry=new Dictionary<(string Item,int Part),SceneMeshBuffer>();
        foreach(var item in request.Items)
            for(var part=0;part<item.Payloads.Count;part++)
                if(item.Payloads[part].Indices.Count>0)geometry.Add((item.Id,part),Meshes.Acquire(view,new SceneIndexedGeometry(item.Payloads[part])));
        target.Meshes=geometry;
        var bindings=new Dictionary<(string Guid,string Hash),SceneSampledMaterial>();
        var reasons=new HashSet<string>(StringComparer.Ordinal);
        foreach(var payload in request.Items.SelectMany(item=>item.Payloads))
        {
            var material=payload.Material;if(material.TextureAssetGuid is not { } guid)continue;
            var key=(guid,material.TextureSourceHash!);if(bindings.ContainsKey(key))continue;
            try
            {
                if(!SceneMaterialDraws.TryTexture(catalog,material,out var selected,out var why)){reasons.Add(why);continue;}
                var lease=Material(view,catalog!,selected!,key.Item2);
                bindings.Add(key,new(lease.Resource,()=>materials!.IsCurrent(lease)));
            }
            catch(Exception error) when(error is InvalidOperationException or ArgumentException or OverflowException)
            { reasons.Add(error.Message); }
        }
        target.Use(request,current,bindings,reasons.Count==0?"":"Material texture placeholder: "+string.Join(" ",reasons)); return target;
    }
    internal void RetainViews(IReadOnlySet<string> active)
    {
        Check();materials?.RetainViews(active);spriteTextures?.RetainViews(active);meshResources?.RetainViews(active);var expired=targets.Keys.Where(key=>!active.Contains(key.View)).ToArray();
        if(expired.Length==0)return;
        // A closed view can occupy either in-flight slot. Retire both only once all submissions finish.
        device.WaitIdle();
        foreach(var key in expired)
        {
            var target=targets[key];targets.Remove(key);requested.Remove(target);target.Dispose();
        }
    }
    // Only host shutdown after WaitIdle calls this; per-slot resize retires after that slot's fence.
    internal void Close() { Check();previews?.Close();materials?.Close();spriteTextures?.Close();meshResources?.Dispose();foreach(var target in targets.Values)target.Dispose();targets.Clear();closed=true;slot=-1; }
    private void Check()
    {
        if(Environment.CurrentManagedThreadId!=thread) throw new InvalidOperationException("Scene device access belongs to the render thread.");
        ObjectDisposedException.ThrowIf(closed,this);
    }
}


