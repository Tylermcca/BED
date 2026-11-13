using System.Text.Json;
using Bed.Editor.App;
using Xunit;

namespace Bed.Tests;

public sealed class HostAssetDependencyTests
{
    [Fact]
    public void Window_host_does_not_resolve_asset_importers_or_decoders_but_BED_does()
    {
        var root=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(root,"Bed.slnx")))root=Directory.GetParent(root)!.FullName;
        string[] Libraries(string project)
        {
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"src",project,"obj","project.assets.json")));
            return json.RootElement.GetProperty("libraries").EnumerateObject().Select(p=>p.Name.Split('/')[0]).ToArray();
        }
        foreach(var name in new[] { "Bed.Import", "Bed.Editor.Assets", "SharpGLTF.Core", "Ahwoo.Brutal.Stb" })
        {
            Assert.DoesNotContain(name,Libraries("Bed.Editor.App"),StringComparer.OrdinalIgnoreCase);
            Assert.Contains(name,Libraries("Bed.Launcher"),StringComparer.OrdinalIgnoreCase);
        }
        Assert.Equal(typeof(AssetCatalogProvisioner).Assembly,typeof(AssetImageDecoder).Assembly);
        Assert.Equal(typeof(AssetCatalogProvisioner).Assembly,typeof(TextureAssetGeneration).Assembly);
        Assert.NotEqual(typeof(EditorApp).Assembly,typeof(AssetCatalogProvisioner).Assembly);
        Assert.Equal(typeof(AssetCatalogProvisioner).Assembly,typeof(AssetReimportController).Assembly);
    }
}
