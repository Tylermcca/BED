using System.Security.Cryptography;
using Bed.Editor;
using Bed.Import;

namespace Bed.Editor.App;

/// <summary>Prepares source-material bindings through independently identified, same-folder texture assets.
/// Does not publish a generation, write a sidecar, or infer identity from a material name.</summary>
public static class MaterialTextureAdmission
{
    /// <summary>Prepared existing texture and the exact sidecar predecessor; revalidate immediately before publication.</summary>
    public sealed record Texture(string Path, string SidecarText, AssetCatalog.Input Input)
    {
        /// <summary>Refuses changed source or sidecar metadata. This is a read-set check, not filesystem atomicity.</summary>
        public void Revalidate()
        {
            if (File.ReadAllText(AssetRecord.SidecarFor(Path)) != SidecarText || AssetRecord.HashOf(Path) != Input.ProducingRecord.SourceHash)
                throw new InvalidOperationException("The texture or its import file changed while the material was loading; try again.");
        }
    }

    /// <summary>A source ordinal and admitted material; optional texture input must join the model's complete catalog generation.</summary>
    public sealed record Binding(int Ordinal, SceneMaterial Material, Texture? Texture);

    /// <summary>Prepares only existing sidecars. Missing records refuse until an explicit import assigns their permanent GUID.</summary>
    public static IReadOnlyList<Binding> Prepare(string modelPath, ImportedAsset asset)
        => PrepareCore(modelPath, asset, null);

    /// <summary>Read-only startup policy: missing texture sidecars retain the linear colour and emit the approved Assets warning.
    /// Existing but invalid texture records still refuse. This method never assigns a new GUID.</summary>
    public static IReadOnlyList<Binding> PrepareForStartup(string modelPath, ImportedAsset asset, out IReadOnlyList<string> warnings)
    {
        var emitted = new List<string>();
        var result = PrepareCore(modelPath, asset, emitted.Add);
        warnings = Array.AsReadOnly(emitted.Distinct(StringComparer.Ordinal).ToArray());
        return result;
    }

    private static IReadOnlyList<Binding> PrepareCore(string modelPath, ImportedAsset asset, Action<string>? missingRecord)
    {
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(modelPath))!;
        var textures = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Binding>();
        foreach (var group in asset.Meshes.Select(m => m.MaterialInfo).OfType<ImportedMaterial>().GroupBy(m => m.Ordinal))
        {
            var material = group.First();
            if (group.Any(m => m != material)) throw new InvalidOperationException("One material ordinal has conflicting source metadata.");
            Texture? texture = null;
            if (material.BaseColorTexture is { } source)
            {
                if (source == "embedded" || System.IO.Path.IsPathRooted(source) || Uri.TryCreate(source, UriKind.Absolute, out _))
                    throw new InvalidOperationException("A base-colour texture needs its own source file, in the same folder, and its own asset ID.");
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, source));
                if (!string.Equals(System.IO.Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A texture outside the model's own source folder can't be used.");
                for (var p = path; p is not null; p = System.IO.Path.GetDirectoryName(p))
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked texture files (shortcuts to elsewhere) can't be used.");
                if (!textures.TryGetValue(path, out texture))
                {
                    var sidecarPath = AssetRecord.SidecarFor(path);
                    if (!File.Exists(sidecarPath))
                    {
                        if (missingRecord is null) throw new InvalidOperationException("The texture hasn't been imported yet; import it first: " + source);
                        missingRecord($"Texture {System.IO.Path.GetFileName(path)} has no asset record yet. Reimport the model to add it.");
                        result.Add(new(material.Ordinal, new(material.BaseColor), null));
                        continue;
                    }
                    if ((File.GetAttributes(sidecarPath) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked texture import files (shortcuts to elsewhere) can't be used.");
                    var sidecar = File.ReadAllText(sidecarPath);
                    var record = AssetRecord.Read(sidecar);
                    if (record.Source != System.IO.Path.GetFileName(path)) throw new InvalidOperationException("The texture's import file names a different source file.");
                    if (new FileInfo(path).Length > AssetImageDecoder.MaxEncodedBytes) throw new InvalidOperationException("Texture source exceeds its encoded-byte bound.");
                    var bytes = File.ReadAllBytes(path);
                    if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != record.SourceHash) throw new InvalidOperationException("Texture changed since import; reimport it first.");
                    // Reuse the existing rights/provenance reimport judgement without saving or changing the record.
                    var checkedRecord = record.PrepareReimport(path, record.ImporterVersion);
                    if (checkedRecord.Write() != record.Write()) throw new InvalidOperationException("The texture file changed since it was imported; reimport it first.");
                    if (!TextureAssetGeneration.TryCreateSavedInput(record, bytes, System.IO.Path.GetExtension(path), [], true,
                        "BEDTEX1", record.Write(), out var input, out var why)) throw new InvalidOperationException(why);
                    textures.Add(path, texture = new(path, sidecar, input!));
                }
            }
            result.Add(new(material.Ordinal, new(material.BaseColor, texture?.Input.Guid, texture?.Input.ProducingRecord.SourceHash), texture));
        }
        return result.AsReadOnly();
    }
}
