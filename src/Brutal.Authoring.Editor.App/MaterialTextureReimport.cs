using System.Text.Json;
using Brutal.Authoring.Import;

namespace Brutal.Authoring.Editor.App;

/// <summary>Explicit model-reimport sidecar preparation. Startup must not call this writer.
/// Newly created texture records are compensated on failure; pre-existing records are never saved or removed.</summary>
public sealed class MaterialTextureReimport : IDisposable
{
    private readonly List<(string Path, string Text)> created = [];
    private bool completed;
    private MaterialTextureReimport() { }
    /// <summary>Read-only bindings prepared after all required independent texture records exist.</summary>
    public IReadOnlyList<MaterialTextureAdmission.Binding> Bindings { get; private set; } = [];

    /// <summary>Creates missing records only in the model's declared source folder through the existing judged import API.
    /// The caller must retain this preparation until catalog publication and model-sidecar save succeed, then Commit.</summary>
    public static MaterialTextureReimport Prepare(string modelPath, AssetRecord modelRecord, ImportedAsset asset)
    {
        var operation = new MaterialTextureReimport();
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
            if (modelRecord.Source != Path.GetFileName(modelPath)) throw new InvalidOperationException("Model record names another source.");
            foreach (var source in asset.Meshes.Select(m => m.MaterialInfo?.BaseColorTexture).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (source == "embedded" || Path.IsPathRooted(source) || Uri.TryCreate(source, UriKind.Absolute, out _))
                    throw new InvalidOperationException("A texture needs its own same-folder source file.");
                var path = Path.GetFullPath(Path.Combine(folder, source));
                if (!string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Texture origin may only be inherited in the model's declared source folder.");
                for (var p = path; p is not null; p = Path.GetDirectoryName(p))
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked texture sources are not admitted.");
                if (File.Exists(AssetRecord.SidecarFor(path))) continue;
                var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                if (extension is not ("png" or "jpg" or "jpeg" or "tga")) throw new InvalidOperationException("Texture format is not supported by the bounded decoder.");
                if (new FileInfo(path).Length > AssetImageDecoder.MaxEncodedBytes) throw new InvalidOperationException("Texture exceeds its encoded-byte bound.");
                // This public API checks the inherited declaration against this texture's own actual observations.
                var result = AssetImport.Import(path, extension, 1, JsonSerializer.SerializeToElement(new { }), modelRecord.Provenance, modelRecord.AiUse);
                if (result.Record is not { } record) throw new InvalidOperationException(string.Join(" ", result.Issues.Select(i => i.Message)));
                operation.created.Add((AssetRecord.SidecarFor(path), record.Write()));
            }
            operation.Bindings = MaterialTextureAdmission.Prepare(modelPath, asset);
            return operation;
        }
        catch (Exception original)
        {
            if (!operation.Rollback(out var why)) throw new InvalidOperationException("Reimport failed and could not be undone: " + why, original);
            throw;
        }
    }

    /// <summary>Retains the new records after the owner has successfully committed the complete generation and model record.</summary>
    public void Commit() { if (completed) throw new InvalidOperationException("Texture preparation is already complete."); completed = true; }

    /// <summary>Removes only unchanged sidecars created by this operation, never source images or pre-existing records.</summary>
    public bool Rollback(out string why)
    {
        why = ""; if (completed) return true;
        var failures = new List<string>();
        foreach (var entry in created.AsEnumerable().Reverse())
        {
            try
            {
                if (!File.Exists(entry.Path)) continue;
                if ((File.GetAttributes(entry.Path) & FileAttributes.ReparsePoint) != 0 || File.ReadAllText(entry.Path) != entry.Text)
                    throw new InvalidOperationException("New texture sidecar changed; compensation refused: " + Path.GetFileName(entry.Path));
                File.Delete(entry.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { failures.Add(e.Message); }
        }
        if (failures.Count == 0) completed = true;
        why = string.Join(" ", failures); return failures.Count == 0;
    }

    /// <summary>Compensates abandoned preparation. A failed compensation is reported rather than deleting changed files.</summary>
    public void Dispose() { if (!Rollback(out var why)) throw new InvalidOperationException("Reimport failed and could not be undone: " + why); }
}
