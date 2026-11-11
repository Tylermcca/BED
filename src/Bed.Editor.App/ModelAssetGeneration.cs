using System.Numerics;
using Bed.Editor;
using Bed.Import;
namespace Bed.Editor.App;

/// <summary>Shared startup/reimport model producer; preserves source part order, material ordinal and explicit texture GUID.</summary>
public static class ModelAssetGeneration
{
    /// <summary>Transforms each imported part once and encodes with the approved BEDMESH1/2 producer. Does not save or publish.</summary>
    public static byte[] Encode(ImportedAsset asset, Matrix4x4 transform, MeshUv0Origin origin, IReadOnlyList<MaterialTextureAdmission.Binding> bindings)
    {
        if (asset.Meshes.Count == 0) throw new InvalidOperationException("No mesh geometry was imported.");
        var byOrdinal = bindings.ToDictionary(b => b.Ordinal);
        var parts = asset.Meshes.Select(m =>
        {
            var geometry = new MeshData(m.Positions.Select(p => Vector3.Transform(p, transform)).ToArray(), m.Triangles.ToArray())
            {
                Normals = m.Normals.Length > 0 ? m.Normals.Select(n => Vector3.Normalize(Vector3.TransformNormal(n, transform))).ToArray() : null,
                Uv0 = m.Uv0.Length > 0 ? m.Uv0.ToArray() : null, Uv0Origin = origin,
            };
            MeshPartMaterial? material = null;
            if (m.MaterialInfo is { } source)
            {
                if (!byOrdinal.TryGetValue(source.Ordinal, out var admitted)) throw new InvalidOperationException("Material binding is missing.");
                material = new(source.Ordinal, admitted.Material.BaseColor,
                    admitted.Material.TextureAssetGuid is { } guid ? Guid.ParseExact(guid, "N") : null);
            }
            return new MeshPart(geometry, material);
        }).ToArray();
        return DerivedMeshCodec.Encode(parts);
    }
}
