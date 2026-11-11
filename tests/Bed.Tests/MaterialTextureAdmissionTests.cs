using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor.App;
using Bed.Import;
using Xunit;

namespace Bed.Tests;

public sealed class MaterialTextureAdmissionTests : IDisposable
{
    private const string Id = "11111111111111111111111111111111";
    private readonly string root = Directory.CreateTempSubdirectory("bed-material-admission-").FullName;
    private string Image => Path.Combine(root, "colour.tga");
    private string Model => Path.Combine(root, "self.obj");
    private AssetRecord Record()
    {
        // One script-made opaque red pixel in an uncompressed 32-bit TGA, with top-left origin.
        byte[] bytes = new byte[22]; bytes[2] = 2; bytes[12] = bytes[14] = 1; bytes[16] = 32; bytes[17] = 0x28; bytes[20] = bytes[21] = 255;
        File.WriteAllBytes(Image, bytes); File.WriteAllText(Model, "self-authored source placeholder");
        var record = new AssetRecord(Id, "colour.tga", Convert.ToHexStringLower(SHA256.HashData(bytes)), "tga", 1,
            JsonSerializer.SerializeToElement(new { }), new("test", "self-authored", "test script", Declared: AssetOrigin.Ours), AiUse.Forbidden);
        File.WriteAllText(AssetRecord.SidecarFor(Image), record.Write());
        return record;
    }
    private static ImportedAsset Asset(params ImportedMaterial[] materials) => new(materials.Select(m =>
        new ImportedMesh("node", "mesh", [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [], [Vector2.Zero, Vector2.UnitX, Vector2.UnitY], [0, 1, 2], m.Name, 1, false, false)
        { MaterialInfo = m }).ToArray(), [], [], materials.Select(m => m.Name).ToArray(), 0, 0, [], materials.Length);
    private AssetRecord ModelRecord() => new("22222222222222222222222222222222", "self.obj", AssetRecord.HashOf(Model), "obj", 1,
        JsonSerializer.SerializeToElement(new { }), new("test", "self-authored", "test script", Declared: AssetOrigin.Ours), AiUse.Forbidden);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Existing_texture_binds_its_sidecar_GUID_and_exact_hash_not_the_material_name_without_writing()
    {
        var record = Record(); var sidecar = File.ReadAllText(AssetRecord.SidecarFor(Image));
        var names = Directory.GetFiles(root).Order(StringComparer.Ordinal).ToArray();
        var bindings = MaterialTextureAdmission.Prepare(Model, Asset(new(0, "different label", Vector4.One, "colour.tga"), new(1, "another", new(.2f, .3f, .4f, 1), "colour.tga")));
        Assert.Equal(2, bindings.Count); Assert.Same(bindings[0].Texture, bindings[1].Texture);
        Assert.All(bindings, b => { Assert.Equal(Id, b.Material.TextureAssetGuid); Assert.Equal(record.SourceHash, b.Material.TextureSourceHash); b.Texture!.Revalidate(); });
        Assert.Equal(new Vector4(.2f, .3f, .4f, 1), bindings[1].Material.BaseColor);
        using var catalog = new AssetCatalog();
        Assert.True(catalog.Publish(catalog.BeginRefresh(), [bindings[0].Texture!.Input], out var why), why);
        var lease = catalog.Resolve(Id).Current!;
        Assert.Equal(AssetCatalog.State.Valid, catalog.Resolve(Id).State); Assert.Equal(AssetCatalog.TextureEncoding.Rgba8Srgb, lease.TextureFormat);
        byte[] pixel = new byte[4]; lease.CopyTo(pixel); Assert.Equal([255, 0, 0, 255], pixel.Select(b => (int)b));
        Assert.Equal(names, Directory.GetFiles(root).Order(StringComparer.Ordinal)); Assert.Equal(sidecar, File.ReadAllText(AssetRecord.SidecarFor(Image)));
    }

    [Fact]
    public void Preparation_refuses_a_missing_record_without_assigning_a_GUID_or_writing_a_sidecar()
    {
        Record(); File.Delete(AssetRecord.SidecarFor(Image)); var before = Directory.GetFiles(root);
        var error = Assert.Throws<InvalidOperationException>(() => MaterialTextureAdmission.Prepare(Model, Asset(new ImportedMaterial(0, "material", Vector4.One, "colour.tga"))));
        Assert.Contains("hasn't been imported yet", error.Message); Assert.False(File.Exists(AssetRecord.SidecarFor(Image))); Assert.Equal(before, Directory.GetFiles(root));
    }

    [Fact]
    public void Changed_texture_or_sidecar_refuses_current_preparation_and_retained_receipt_revalidation()
    {
        Record(); var asset = Asset(new ImportedMaterial(0, "material", Vector4.One, "colour.tga"));
        var prepared = Assert.Single(MaterialTextureAdmission.Prepare(Model, asset)).Texture!;
        File.AppendAllText(AssetRecord.SidecarFor(Image), " ");
        Assert.Throws<InvalidOperationException>(prepared.Revalidate);
        Record(); prepared = Assert.Single(MaterialTextureAdmission.Prepare(Model, asset)).Texture!;
        var bytes = File.ReadAllBytes(Image); bytes[18] = 42; File.WriteAllBytes(Image, bytes);
        Assert.Throws<InvalidOperationException>(prepared.Revalidate);
        Assert.Contains("changed since import", Assert.Throws<InvalidOperationException>(() => MaterialTextureAdmission.Prepare(Model, asset)).Message);
    }

    [Fact]
    public void A_material_without_a_texture_keeps_its_linear_colour_and_needs_no_texture_identity()
    {
        var binding = Assert.Single(MaterialTextureAdmission.Prepare(Model, Asset(new ImportedMaterial(7, "plain", new(.4f, .5f, .6f, 1), null))));
        Assert.Equal(7, binding.Ordinal); Assert.Equal(new Vector4(.4f, .5f, .6f, 1), binding.Material.BaseColor);
        Assert.Null(binding.Material.TextureAssetGuid); Assert.Null(binding.Texture);
    }

    [Fact]
    public void Explicit_reimport_creates_a_texture_GUID_with_inherited_origin_and_retains_it_only_after_commit()
    {
        Record(); File.Delete(AssetRecord.SidecarFor(Image)); var model = ModelRecord();
        using (var preparation = MaterialTextureReimport.Prepare(Model, model, Asset(new ImportedMaterial(0, "painted", Vector4.One, "colour.tga"))))
        {
            var binding = Assert.Single(preparation.Bindings);
            var assigned = AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(Image)));
            Assert.Equal(assigned.Guid, binding.Material.TextureAssetGuid); Assert.NotEqual(model.Guid, assigned.Guid);
            Assert.Equal(model.Provenance, assigned.Provenance); Assert.Equal(model.AiUse, assigned.AiUse);
            preparation.Commit();
        }
        Assert.True(File.Exists(AssetRecord.SidecarFor(Image)));
    }

    [Fact]
    public void A_failed_texture_compensates_only_new_records_leaving_existing_sidecars_and_all_images_untouched()
    {
        Record(); var existing = File.ReadAllText(AssetRecord.SidecarFor(Image));
        var fresh = Path.Combine(root, "new.tga"); var bad = Path.Combine(root, "bad.tga");
        File.WriteAllBytes(fresh, File.ReadAllBytes(Image)); File.WriteAllBytes(bad, [1, 2, 3]);
        var asset = Asset(new(0, "existing", Vector4.One, "colour.tga"), new(1, "fresh", Vector4.One, "new.tga"), new(2, "bad", Vector4.One, "bad.tga"));
        Assert.Throws<InvalidOperationException>(() => MaterialTextureReimport.Prepare(Model, ModelRecord(), asset));
        Assert.Equal(existing, File.ReadAllText(AssetRecord.SidecarFor(Image)));
        Assert.False(File.Exists(AssetRecord.SidecarFor(fresh))); Assert.False(File.Exists(AssetRecord.SidecarFor(bad)));
        Assert.Equal(File.ReadAllBytes(Image), File.ReadAllBytes(fresh)); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(bad));
    }

    [Fact]
    public void Abandoning_a_prepared_reimport_removes_its_new_sidecar_but_not_source_data()
    {
        Record(); var bytes = File.ReadAllBytes(Image); File.Delete(AssetRecord.SidecarFor(Image));
        using (var preparation = MaterialTextureReimport.Prepare(Model, ModelRecord(), Asset(new ImportedMaterial(0, "material", Vector4.One, "colour.tga"))))
            Assert.True(File.Exists(AssetRecord.SidecarFor(Image)));
        Assert.False(File.Exists(AssetRecord.SidecarFor(Image))); Assert.Equal(bytes, File.ReadAllBytes(Image));
    }

    [Fact]
    public void Compensation_refuses_to_remove_a_new_sidecar_that_someone_changed()
    {
        Record(); File.Delete(AssetRecord.SidecarFor(Image));
        var preparation = MaterialTextureReimport.Prepare(Model, ModelRecord(), Asset(new ImportedMaterial(0, "material", Vector4.One, "colour.tga")));
        File.AppendAllText(AssetRecord.SidecarFor(Image), " ");
        Assert.False(preparation.Rollback(out var why)); Assert.Contains("sidecar changed", why);
        Assert.True(File.Exists(AssetRecord.SidecarFor(Image))); Assert.Throws<InvalidOperationException>(preparation.Dispose);
    }

    [Fact]
    public void Startup_retains_untextured_colour_and_warns_once_without_creating_any_record()
    {
        Record(); File.Delete(AssetRecord.SidecarFor(Image)); var before = Directory.GetFiles(root).Order(StringComparer.Ordinal).ToArray();
        var bindings = MaterialTextureAdmission.PrepareForStartup(Model, Asset(new(0, "one", new(.2f, .3f, .4f, 1), "colour.tga"),
            new(1, "two", Vector4.One, "colour.tga")), out var warnings);
        Assert.Equal("Texture colour.tga has no asset record yet. Reimport the model to add it.", Assert.Single(warnings));
        Assert.Equal(2, bindings.Count); Assert.All(bindings, b => { Assert.Null(b.Texture); Assert.Null(b.Material.TextureAssetGuid); });
        Assert.Equal(new Vector4(.2f, .3f, .4f, 1), bindings[0].Material.BaseColor);
        Assert.Equal(before, Directory.GetFiles(root).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(AssetRecord.SidecarFor(Image)));
    }
}
