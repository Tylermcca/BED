using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Bed.Import;

// Normal OBJ/MTL input only: no writer, path-to-GUID resolution or policy inheritance.
internal sealed class ObjMaterialLibrary(string modelPath)
{
    private readonly string folder = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
    private readonly Dictionary<string, ImportedMaterial> materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImportedSourceRead> reads = new(StringComparer.Ordinal);
    internal IReadOnlyList<ImportedSourceRead> Reads => Array.AsReadOnly(reads.Values.OrderBy(r => r.RelativePath, StringComparer.Ordinal).ToArray());
    internal IReadOnlyList<string> Names => Array.AsReadOnly(materials.Keys.ToArray());

    private byte[] Read(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(folder, relative));
        if (!string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Material resources must be in the model's declared source folder.");
        for (var p = path; p is not null; p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked material resources are not admitted.");
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Material resource exceeds 64 MiB.");
        var bytes = File.ReadAllBytes(path);
        var key = Path.GetFileName(path);
        var observed = new ImportedSourceRead(key, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        if (reads.TryGetValue(key, out var before) && before.SourceHash != observed.SourceHash)
            throw new InvalidDataException("Material resource changed during import: " + key);
        reads[key] = observed;
        return bytes;
    }

    internal void Load(string name)
    {
        string? current = null;
        var colour = Vector4.One;
        string? texture = null;
        void Close()
        {
            if (current is null) return;
            if (materials.ContainsKey(current)) throw new InvalidDataException("Duplicate material declaration: " + current);
            materials.Add(current, new(materials.Count, current, colour, texture));
        }
        foreach (var raw in Encoding.UTF8.GetString(Read(name)).Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            switch (parts[0])
            {
                case "newmtl":
                    Close(); current = line[6..].Trim(); colour = Vector4.One; texture = null;
                    if (current.Length == 0) throw new InvalidDataException("Material name is empty.");
                    break;
                case "Kd":
                    if (current is null || parts.Length != 4) throw new InvalidDataException("Kd needs a material and three components.");
                    float F(string text)
                    {
                        var value = float.Parse(text, CultureInfo.InvariantCulture);
                        return float.IsFinite(value) && value is >= 0 and <= 1 ? value : throw new InvalidDataException("Kd must be finite and between zero and one.");
                    }
                    colour = new(F(parts[1]), F(parts[2]), F(parts[3]), 1);
                    break;
                case "map_Kd":
                    if (current is null) throw new InvalidDataException("map_Kd needs a material.");
                    texture = line[6..].Trim();
                    if (texture.Length == 0 || texture.StartsWith('-')) throw new InvalidDataException("map_Kd requires a plain source image path; options are not supported.");
                    break;
            }
        }
        Close();
    }

    internal ImportedMaterial Resolve(string name)
    {
        if (!materials.TryGetValue(name, out var material)) throw new InvalidDataException("Material is not declared: " + name);
        if (material.BaseColorTexture is { } texture) Read(texture);
        return material;
    }
}
