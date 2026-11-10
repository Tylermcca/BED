using System.Numerics;
using System.Text.Json;

namespace Brutal.Authoring.Import;

/// <summary>One source material, before texture admission. Ordinal is local to the imported model, not an asset GUID.</summary>
/// <param name="Ordinal">The material index (glTF), or declaration order (OBJ).</param>
/// <param name="Name">Source label, for diagnostics only.</param>
/// <param name="BaseColor">Linear base-colour factor, including alpha.</param>
/// <param name="BaseColorTexture">Relative source image, or "embedded" when no independent source file exists.</param>
public sealed record ImportedMaterial(int Ordinal, string Name, Vector4 BaseColor, string? BaseColorTexture)
{
    internal static ImportedMaterial FromGltf(JsonElement root, int ordinal, string name)
    {
        var material = root.GetProperty("materials")[ordinal];
        var colour = Vector4.One;
        string? source = null;
        if (material.TryGetProperty("pbrMetallicRoughness", out var pbr))
        {
            if (pbr.TryGetProperty("baseColorFactor", out var factor))
            {
                if (factor.GetArrayLength() != 4) throw new InvalidDataException("Base colour needs four components.");
                colour = new(factor[0].GetSingle(), factor[1].GetSingle(), factor[2].GetSingle(), factor[3].GetSingle());
            }
            if (pbr.TryGetProperty("baseColorTexture", out var texture))
            {
                var entry = root.GetProperty("textures")[texture.GetProperty("index").GetInt32()];
                var image = root.GetProperty("images")[entry.GetProperty("source").GetInt32()];
                source = image.TryGetProperty("uri", out var uri) && uri.GetString() is { } path && !path.StartsWith("data:", StringComparison.Ordinal)
                    ? Uri.UnescapeDataString(path) : "embedded";
            }
        }
        return new(ordinal, name, colour, source);
    }
}
