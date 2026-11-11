using Bed.Editor;
using Brutal.ImGuiApi;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.ImGui;

/// <summary>A borrowed image for one captured frame. The host retains all texture, descriptor and device ownership.</summary>
public sealed record SceneImage(SceneRenderRequest Request, ImTextureRef Texture, Func<bool> IsCurrent)
{
    /// <summary>Plain presentation limitation such as a missing material placeholder; never an authority or success claim.</summary>
    public string Status {get;init;}="";
}

/// <summary>A borrowed sprite image for an exact admitted selection and sampling filter. Ownership stays with the host.</summary>
public sealed record SpriteImage(AssetCatalog.Selection Selection, SceneSpriteFilter Filter, ImTextureRef Texture, Func<bool> IsCurrent, string Status="");

/// <summary>A UI-thread, ImGui-context-scoped connection to the host's scene renderer. No connection means CPU presentation.
/// Dispose before renderer shutdown; retained requests/images never keep a native owner alive.</summary>
public sealed class ScenePresentation : IDisposable
{
    [ThreadStatic] private static List<ScenePresentation>? bindings;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ImGuiContextPtr context;
    private readonly Func<string, SceneRenderRequest, Func<SceneRenderIdentity>, AssetCatalog?, SceneImage?> render;
    private readonly Action<IReadOnlySet<string>>? retainViews;
    private readonly HashSet<string> activeViews = new(StringComparer.Ordinal);
    private bool closed;
    private Func<string,AssetCatalog,AssetCatalog.Selection,SceneSpriteFilter,SpriteImage?>? sprites;
    private ScenePresentation(Func<string, SceneRenderRequest, Func<SceneRenderIdentity>, AssetCatalog?, SceneImage?> render, Action<IReadOnlySet<string>>? retainViews)
    {
        this.render=render; this.retainViews=retainViews; context=Im.GetCurrentContext();
        if(context.Equals(default(ImGuiContextPtr)))throw new InvalidOperationException("A live ImGui context is required for scene presentation.");
        (bindings??=[]).Add(this);
    }
    /// <summary>Registers an image producer for the current UI thread/context. The callback uses only the captured request and current-identity predicate.</summary>
    public static ScenePresentation Bind(Func<string, SceneRenderRequest, Func<SceneRenderIdentity>, SceneImage?> render,
        Action<IReadOnlySet<string>>? retainViews = null)
    { ArgumentNullException.ThrowIfNull(render);return new((view,request,current,_)=>render(view,request,current),retainViews); }
    /// <summary>Registers the existing renderer with the view's optional admitted material catalog. Null means placeholder, never path lookup.</summary>
    public static ScenePresentation BindMaterials(Func<string,SceneRenderRequest,Func<SceneRenderIdentity>,AssetCatalog?,SceneImage?> render,
        Action<IReadOnlySet<string>>? retainViews=null)
    {ArgumentNullException.ThrowIfNull(render);return new(render,retainViews);}
    /// <summary>Adds a sprite producer to this binding, sharing its thread, context and frame collection. No callback grants asset admission.</summary>
    public ScenePresentation WithSprites(Func<string,AssetCatalog,AssetCatalog.Selection,SceneSpriteFilter,SpriteImage?> render)
    {Check();ArgumentNullException.ThrowIfNull(render);if(sprites is not null)throw new InvalidOperationException("A sprite producer is already bound.");sprites=render;return this;}
    /// <summary>Revalidates the exact borrowed sprite and admitted source immediately before the caller records an image quad.</summary>
    public bool TrySpriteImage(string viewId,AssetCatalog catalog,AssetCatalog.Selection selection,SceneSpriteFilter filter,out ImTextureRef texture,out string why)
    {
        texture=default;why="";
        try
        {
            Check();ArgumentException.ThrowIfNullOrWhiteSpace(viewId);ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(selection);
            if(!Enum.IsDefined(filter))throw new ArgumentException("Unknown sprite filter.");
            if(sprites is null){why="Image preview unavailable; showing sprite bounds.";return false;}
            if(!catalog.TryResolve(selection,out var lease,out why)||!catalog.TryResolveTexture(selection,lease!.SourceHash,out _,out why))return false;
            var sourceHash=lease.SourceHash;activeViews.Add(viewId);
            var image=sprites(viewId,catalog,selection,filter);
            Check();
            if(image is null||image.Selection!=selection||image.Filter!=filter||image.Texture._TexID==0||image.IsCurrent is null||!image.IsCurrent())
            {why="The sprite image is unavailable or out of date.";return false;}
            Check();
            if(!catalog.TryResolveTexture(selection,sourceHash,out _,out why))return false;
            texture=image.Texture;why=image.Status;return true;
        }
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or OverflowException)
        {why=error.Message;return false;}
    }
    /// <summary>Starts host collection after the safe frame slot is prepared.</summary>
    public void BeginFrame() { Check();activeViews.Clear(); }
    /// <summary>Lets the renderer retire views absent from this complete UI frame, with its own GPU synchronization.</summary>
    public void CompleteFrame() { Check();retainViews?.Invoke(new HashSet<string>(activeViews,StringComparer.Ordinal)); }
    private void Check()
    {
        if(closed||thread!=Environment.CurrentManagedThreadId||!context.Equals(Im.GetCurrentContext()))
            throw new InvalidOperationException("Scene presentation requires its live UI thread and context.");
    }
    /// <summary>The innermost live presenter for this context, or null for headless/OpenGL CPU fallback.</summary>
    public static ScenePresentation? Current
    {
        get
        {
            var context=Im.GetCurrentContext();
            return bindings?.LastOrDefault(b=>!b.closed&&b.context.Equals(context));
        }
    }
    /// <summary>Requests and revalidates a borrowed image immediately before recording its UI draw command. No stale result becomes current.</summary>
    public bool TryImage(string viewId, SceneRenderRequest request, Func<SceneRenderIdentity> current,
        out ImTextureRef texture, out string reason,AssetCatalog? catalog=null)
    {
        texture=default; reason="";
        if(closed||thread!=Environment.CurrentManagedThreadId||!context.Equals(Im.GetCurrentContext()))
        {reason="The scene renderer is no longer available.";return false;}
        try
        {
            if(request.Identity!=current()){reason="The scene frame changed.";return false;}
            activeViews.Add(viewId);
            var image=render(viewId,request,current,catalog);
            if(closed||image is null||!ReferenceEquals(image.Request,request)||request.Identity!=current()
                ||image.Texture._TexID==0||image.IsCurrent is null||!image.IsCurrent()||closed||request.Identity!=current())
            {reason="The scene image is unavailable or out of date.";return false;}
            texture=image.Texture;reason=image.Status;return true;
        }
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or OverflowException)
        {reason=error.Message;return false;}
    }
    /// <summary>Removes only this binding, on its UI thread. Native resources belong to the host and are not disposed here.</summary>
    public void Dispose()
    {
        if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Scene presentation belongs to the UI thread.");
        if(closed)return;closed=true;bindings?.Remove(this);
    }
}
