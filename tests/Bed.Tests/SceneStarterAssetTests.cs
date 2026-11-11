using System.Numerics;
using System.Security.Cryptography;
using Bed;
using Bed.Editor;
using Bed.Import;
using Xunit;

namespace Bed.Tests;

public sealed class SceneStarterAssetTests
{
    [Theory]
    [InlineData("cube", "c0bec0bec0bec0bec0bec0bec0bec0be", 1f, 1f)]
    [InlineData("pillar", "a11aa11aa11aa11aa11aa11aa11aa11a", .5f, 2f)]
    public void Generated_project_assets_have_stable_self_authored_sidecars_and_real_OBJ_geometry(string name, string guid, float width, float height)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "scene-starter", "assets", name + ".obj");
        var before = File.ReadAllBytes(path);
        var sidecar = File.ReadAllText(AssetRecord.SidecarFor(path));
        var record = AssetRecord.Read(sidecar);
        Assert.Equal(guid, record.Guid); Assert.Equal(name + ".obj", record.Source);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(before)), record.SourceHash);
        Assert.Equal("obj", record.Importer); Assert.Equal(1, record.ImporterVersion);
        Assert.Equal(AssetOrigin.Ours, record.Provenance.Declared);
        Assert.Equal("self-authored", record.Provenance.Licence);
        Assert.Equal("samples/SceneStarterProject/generate_assets.py", record.Provenance.Origin);
        Assert.Equal(1, record.Settings.GetProperty("scale").GetInt32());
        Assert.Equal("Y", record.Settings.GetProperty("upAxis").GetString());
        var imported = ObjImporter.Import(path, ProjectFrame.Gltf);
        Assert.False(imported.Issues.HasErrors());
        Assert.NotNull(imported.Asset);
        var mesh = Assert.Single(imported.Asset.Meshes);
        Assert.Equal(8, mesh.Positions.Length); Assert.Equal(36, mesh.Triangles.Length);
        Assert.Equal(new Vector3(-width/2, 0, -width/2), new(mesh.Positions.Min(p => p.X), mesh.Positions.Min(p => p.Y), mesh.Positions.Min(p => p.Z)));
        Assert.Equal(new Vector3(width/2, height, width/2), new(mesh.Positions.Max(p => p.X), mesh.Positions.Max(p => p.Y), mesh.Positions.Max(p => p.Z)));
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(sidecar, File.ReadAllText(AssetRecord.SidecarFor(path)));
    }
}
