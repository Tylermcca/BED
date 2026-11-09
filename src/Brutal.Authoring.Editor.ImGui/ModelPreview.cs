namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Selection and stale-content state for a model preview. It does not decode opaque catalog bytes,
/// assume a model format, allocate graphics resources or create a second asset catalog.</summary>
public sealed class ModelPreview(AssetCatalog catalog)
{
    private AssetCatalog.Selection? selection;
    private AssetCatalog.Lease? lastGood;
    private string? invalidation;
    /// <summary>The exact selection captured by the caller. Refresh never silently selects a newer revision.</summary>
    public AssetCatalog.Selection? Selection => selection;
    /// <summary>Previously accepted pinned content, not evidence that it is current. Null after changing selection.</summary>
    public AssetCatalog.Lease? LastGood => lastGood;

    /// <summary>Selects a resolver identity. Changing identity or revision clears retained display content.</summary>
    public void Select(AssetCatalog.Selection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(selection != value) lastGood=null;
        selection=value;invalidation=null;
    }
    /// <summary>Clears selection and retained content; it does not close the shared project catalog.</summary>
    public void Clear(){selection=null;lastGood=null;invalidation=null;}
    /// <summary>Labels a retained last-good preview during a failed/pending reimport; it cannot be returned as current.</summary>
    public void Invalidate(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if(invalidation is null)TryCurrent(out _,out _);
        invalidation=reason;
    }

    /// <summary>Decodes only the declared BEDMESH1 model format; ordinary opaque leases remain valid through TryCurrent.</summary>
    public bool TryMesh(out Brutal.Authoring.Editor.MeshData? mesh,out string why)
    {
        mesh=null;if(!TryCurrent(out var lease,out why))return false;
        if(lease!.Length>Brutal.Authoring.Editor.DerivedMeshCodec.MaxBytes){why="Derived model exceeds its byte bound.";return false;}
        var bytes=new byte[lease.Length];lease.CopyTo(bytes);
        if(bytes.Length<8||!bytes.AsSpan(0,8).SequenceEqual("BEDMESH1"u8)){why="This asset is not a BEDMESH1 model.";return false;}
        try{mesh=Brutal.Authoring.Editor.DerivedMeshCodec.Decode(bytes);why="";return true;}
        catch(InvalidDataException error){why=error.Message;return false;}
    }
    /// <summary>Revalidates admission, content and policy through the same catalog immediately before consumption.
    /// Failure keeps last-good explicitly separate; the caller must display the returned reason, not call it current.
    /// Success is only a pinned-content fact, not model decoding, rendering or permission to export.</summary>
    public bool TryCurrent(out AssetCatalog.Lease? content,out string why)
    {
        content=null;
        if(invalidation is not null){why=invalidation;return false;}
        if(selection is null){why="Choose an asset to preview.";return false;}
        if(!catalog.TryResolve(selection,out content,out why))return false;
        lastGood=content;return true;
    }
}
