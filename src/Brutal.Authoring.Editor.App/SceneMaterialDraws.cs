using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.Authoring.Editor;
namespace Brutal.Authoring.Editor.App;

/// <summary>The exact 128-byte vertex-stage packet; positions and UVs come only from the captured CPU payload.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceneMaterialPacket
{
    /// <summary>Captured Vulkan clip positions, in source triangle order.</summary>
    public Vector4 A,B,C;
    /// <summary>Linear opaque vertex colors; a material repeats its tint at all vertices.</summary>
    public Vector4 ColorA,ColorB,ColorC;
    /// <summary>Top-left UV0 for vertices A and B, packed XY/ZW.</summary>
    public Vector4 UvAB;
    /// <summary>Top-left UV0 for C in XY, sampling mode in Z: flat 0, sampled 1, placeholder 2.</summary>
    public Vector4 UvCMode;
}
/// <summary>One source-ordered triangle and its immutable material identity. No native handle or asset lookup.</summary>
public sealed record SceneMaterialDraw(string ItemId,SceneMaterial Material,SceneMaterialPacket Packet);
/// <summary>Pure draw packing; no geometry enumeration outside the immutable captured request.</summary>
public static class SceneMaterialDraws
{
    /// <summary>Captures clip-space positions, linear tint and top-left UVs without mutating the request or flipping UVs again.</summary>
    public static IReadOnlyList<SceneMaterialDraw> Build(SceneRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);var result=new List<SceneMaterialDraw>();
        var vp=request.Camera.View*request.Camera.Projection(request.Identity.Extent);
        foreach(var item in request.Items)
        {
            var p=item.Payload;var matrix=item.World*vp;var tint=p.Material.BaseColor;
            Vector4 Clip(int i){var c=Vector4.Transform(new Vector4(p.Positions[i],1),matrix);return new(c.X,-c.Y,c.Z,c.W);}
            Vector2 Uv(int i)=>p.Uv0.Count==0?Vector2.Zero:p.Uv0[i];
            for(var i=0;i<p.Indices.Count;i+=3)
            {
                var a=p.Indices[i];var b=p.Indices[i+1];var c=p.Indices[i+2];var ua=Uv(a);var ub=Uv(b);var uc=Uv(c);
                result.Add(new(item.Id,p.Material,new(){A=Clip(a),B=Clip(b),C=Clip(c),ColorA=tint,ColorB=tint,ColorC=tint,
                    UvAB=new(ua,ub.X,ub.Y),UvCMode=new(uc,p.Material.TextureAssetGuid is null?0:1,0)}));
            }
        }
        return result.AsReadOnly();
    }
    /// <summary>Re-resolves only the currently admitted GUID and exact declared source hash, with explicit decoded metadata.</summary>
    public static bool TryTexture(AssetCatalog? catalog,SceneMaterial material,out AssetCatalog.Selection? selection,out string why)
    {
        selection=null;if(catalog is null){why="No admitted material catalog.";return false;}
        if(material.TextureAssetGuid is not { } guid){why="This material has no texture.";return false;}
        selection=catalog.Select(guid);
        if(selection is null){why="The declared material texture is missing, stale or refused.";return false;}
        if(!catalog.TryResolveTexture(selection,material.TextureSourceHash!,out _,out why)){selection=null;return false;}
        return true;
    }
    /// <summary>Refuses missing/stale bindings visibly without sampling a previous image.</summary>
    public static SceneMaterialPacket WithAvailability(SceneMaterialDraw draw,bool available)
    {var value=draw.Packet;if(draw.Material.TextureAssetGuid is not null&&!available)value.UvCMode.Z=2;return value;}
}
