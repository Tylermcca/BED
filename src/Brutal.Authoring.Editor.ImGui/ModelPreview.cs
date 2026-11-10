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

    /// <summary>Decodes all ordered BEDMESH1/2 parts, retaining material identities for part-aware consumers.</summary>
    public bool TryParts(out IReadOnlyList<Brutal.Authoring.Editor.MeshPart>? parts, out string why)
    {
        parts=null;if(!TryCurrent(out var lease,out why))return false;
        if(lease!.Length>Brutal.Authoring.Editor.DerivedMeshCodec.MaxBytes){why="Derived model exceeds its byte bound.";return false;}
        var bytes=new byte[lease.Length];lease.CopyTo(bytes);
        if(bytes.Length<8||!bytes.AsSpan(0,8).SequenceEqual("BEDMESH1"u8)&&!bytes.AsSpan(0,8).SequenceEqual("BEDMESH2"u8)){why="This asset is not a BEDMESH1/2 model.";return false;}
        try{parts=Brutal.Authoring.Editor.DerivedMeshCodec.DecodeParts(bytes);why="";return true;}
        catch(InvalidDataException error){why=error.Message;return false;}
    }
    /// <summary>Resolves every material part through the current catalog and combines geometry for CPU picking and bounds.
    /// Single-part callers retain the same geometry/material result; multi-part models also retain their ordered Parts.</summary>
    public bool TryMesh(out Brutal.Authoring.Editor.MeshData? mesh,out string why)
    {
        mesh=null;if(!TryParts(out var parts,out why))return false;
        var resolved=new List<Brutal.Authoring.Editor.MeshData>();
        foreach(var part in parts!)
        {
            var source=part.Material;
            string? id=source?.Texture?.ToString("N"),hash=null;
            if(id is not null)
            {
                var texture=catalog.Select(id);var lease=catalog.Resolve(id).Current;
                if(texture is null||lease is null||!catalog.TryResolveTexture(texture,lease.SourceHash,out _,out why))
                {why="Model texture is not current: "+why;return false;}
                hash=lease.SourceHash;
            }
            resolved.Add(part.Geometry with {Material=source is null?null:new(source.BaseColor,id,hash)});
        }
        if(!catalog.TryResolve(selection!,out _,out why))return false;
        if(resolved.Count==1)mesh=resolved[0];
        else
        {
            var positions=new List<System.Numerics.Vector3>();var indices=new List<int>();
            foreach(var part in resolved){var offset=positions.Count;positions.AddRange(part.Positions);indices.AddRange(part.Indices.Select(i=>checked(i+offset)));}
            mesh=new(positions.ToArray(),indices.ToArray()){Parts=resolved.AsReadOnly()};
        }
        why="";return true;
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
