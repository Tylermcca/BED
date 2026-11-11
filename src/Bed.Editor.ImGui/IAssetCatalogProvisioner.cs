using Bed.Editor;
namespace Bed.Editor.ImGui;

/// <summary>App-owned startup acquisition; UI never treats a browser path as admitted derived content.</summary>
public interface IAssetCatalogProvisioner
{
    /// <summary>Returns an owned admission for an existing assets folder, or null when no folder exists.</summary>
    ProjectAssetCatalog? Open(ProjectPaths project, string? assetsFolder = null);
}

/// <summary>One startup admission and its diagnostics. Disposing closes catalog selection authority.</summary>
public sealed record ProjectAssetCatalog(AssetBrowser Browser, AssetCatalog Catalog, IDisposable Owner,
    IReadOnlyList<string> Problems) : IDisposable
{
    /// <summary>Nonblocking importer diagnostics, displayed without revoking valid placement admission.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Optional app-owned audio preview, disposed with this admission.</summary>
    public IAssetAudioPreview? AudioPreview { get; init; }
    /// <summary>Releases audio resources and the app's acquisition owner.</summary>
    public void Dispose() { try { AudioPreview?.Dispose(); } finally { Owner.Dispose(); } }
}
