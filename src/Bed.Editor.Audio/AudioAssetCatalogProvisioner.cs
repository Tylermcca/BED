using Bed.Editor.App;
using Bed.Editor.ImGui;

namespace Bed.Editor.Audio;

// Attach BED's optional playback through the existing host extension point.
internal sealed class AudioAssetCatalogProvisioner : IAssetCatalogProvisioner
{
    private readonly AssetCatalogProvisioner inner = new();

    public ProjectAssetCatalog? Open(ProjectPaths project, string? assetsFolder = null)
    {
        var assets = inner.Open(project, assetsFolder);
        return assets is null ? null : assets with { AudioPreview = new OpenAlAssetAudioPreview() };
    }
}
