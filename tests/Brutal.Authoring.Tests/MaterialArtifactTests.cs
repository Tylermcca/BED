using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Import;
using Brutal.Authoring.Runtime;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class MaterialArtifactTests
{
    private static MeshData Triangle() => new([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2])
    { Uv0 = [new(-1, 2), Vector2.UnitX, Vector2.UnitY], Uv0Origin = MeshUv0Origin.ObjBottomLeft };
    private const string ModelId = "11111111111111111111111111111111", TextureId = "22222222222222222222222222222222";
    private static AssetRecord Record(string id, string path, string importer) => new(id, Path.GetFileName(path), AssetRecord.HashOf(path), importer, 1,
        JsonSerializer.SerializeToElement(new { }), new("self", "self-authored", "test script", Declared: AssetOrigin.Ours), AiUse.Forbidden);

    [Fact]
    public void Material_parts_roundtrip_order_factor_GUID_and_raw_geometry_without_aliasing()
    {
        var texture = Guid.ParseExact(TextureId, "N");
        var first = Triangle(); var second = Triangle() with { Positions = [Vector3.One, Vector3.UnitX, Vector3.UnitY] };
        MeshPart[] parts = [new(first, new(4, new(.2f, .3f, .4f, 1), texture)), new(second, null)];
        var bytes = DerivedMeshCodec.Encode(parts);
        Assert.Equal("BEDMESH2"u8.ToArray(), bytes[..8]); Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        var runtime = BedMeshReader.Decode(bytes); var decoded = DerivedMeshCodec.DecodeParts(bytes);
        Assert.Equal(2, runtime.Parts.Count); Assert.Equal(2, decoded.Count);
        Assert.Equal(parts[0].Material, decoded[0].Material); Assert.Null(decoded[1].Material);
        Assert.Equal(texture, runtime.Parts[0].Material!.Texture); Assert.Equal(4, runtime.Parts[0].Material!.Ordinal);
        Assert.Equal(first.Positions, decoded[0].Geometry.Positions); Assert.Equal(first.Uv0, decoded[0].Geometry.Uv0);
        Assert.Equal(BedMeshUv0Origin.ObjBottomLeft, runtime.Parts[0].Geometry.Uv0Origin);
        Assert.Equal(second.Positions, decoded[1].Geometry.Positions); Assert.Equal(bytes, DerivedMeshCodec.Encode(decoded));
        first.Positions[0] = Vector3.One; Array.Fill(bytes, (byte)0); Assert.Equal(Vector3.Zero, decoded[0].Geometry.Positions[0]);
        Assert.Throws<InvalidOperationException>(() => runtime.Positions); // A compatibility getter cannot hide another part.
        Assert.Throws<InvalidDataException>(() => DerivedMeshCodec.Decode(DerivedMeshCodec.Encode(parts)));
    }

    [Fact]
    public void Material_free_models_keep_old_bytes_and_legacy_runtime_compatibility()
    {
        var old = DerivedMeshCodec.Encode(Triangle());
        Assert.Equal(old, DerivedMeshCodec.Encode(new MeshPart[] { new(Triangle(), null) }));
        var model = BedMeshReader.Decode(old); Assert.Null(Assert.Single(model.Parts).Material);
        Assert.Equal(Triangle().Positions, model.Positions); Assert.Equal(Triangle().Uv0, model.Uv0);
        var two = DerivedMeshCodec.Encode(new MeshPart[] { new(Triangle(), null), new(Triangle(), null) });
        Assert.Equal("BEDMESH1"u8.ToArray(), two[..8]); Assert.Equal(6, DerivedMeshCodec.Decode(two).Positions.Length);
    }

    [Fact]
    public void Material_counts_factors_GUIDs_UVs_and_total_geometry_caps_refuse_without_silent_loss()
    {
        Assert.Throws<InvalidDataException>(() => DerivedMeshCodec.Encode(new MeshPart[] { new(Triangle(), new(-1, Vector4.One, null)) }));
        Assert.Throws<InvalidDataException>(() => DerivedMeshCodec.Encode(new MeshPart[] { new(Triangle(), new(0, new(1, 1, 1, .5f), null)) }));
        Assert.Throws<InvalidDataException>(() => DerivedMeshCodec.Encode(new MeshPart[] { new(Triangle(), new(0, Vector4.One, Guid.Empty)) }));
        Assert.Throws<InvalidDataException>(() => DerivedMeshCodec.Encode(new MeshPart[] { new(Triangle() with { Uv0 = null }, new(0, Vector4.One, Guid.NewGuid())) }));
        var many = new MeshData(new Vector3[500_001], [0, 1, 2]);
        Assert.Throws<InvalidDataException>(() => DerivedMeshCodec.Encode(new MeshPart[] { new(many, new(0, Vector4.One, null)), new(many, null) }));
    }

    [Fact]
    public void Multi_part_reader_checks_truncation_trailing_bytes_totals_and_material_fields()
    {
        var bytes = DerivedMeshCodec.Encode(new MeshPart[] { new(Triangle(), new(0, Vector4.One, null)) });
        foreach (var length in new[] { 0, 8, 11, 12, 27, bytes.Length - 1 }) Assert.Throws<InvalidDataException>(() => BedMeshReader.Decode(bytes.AsSpan(0, length)));
        Assert.Throws<InvalidDataException>(() => BedMeshReader.Decode([.. bytes, 0]));
        var broken = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(8), uint.MaxValue); Assert.Throws<InvalidDataException>(() => BedMeshReader.Decode(broken));
        broken = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(bytes.Length - 32), 0x7fc00000); Assert.Throws<InvalidDataException>(() => BedMeshReader.Decode(broken));
        broken = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(16), uint.MaxValue); Assert.Throws<InvalidDataException>(() => BedMeshReader.Decode(broken));
    }

    [Fact]
    public void Texture_artifact_roundtrips_rgba_with_explicit_dimensions_and_independent_header_bounds()
    {
        using var f = new Fixture(true); var record = AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(f.Image)));
        Assert.True(TextureAssetGeneration.TryCreateSavedInput(record, File.ReadAllBytes(f.Image), "tga", [], true, "BEDTEX1", record.Write(), out var input, out var why), why);
        using var catalog = new AssetCatalog(); Assert.True(catalog.Publish(catalog.BeginRefresh(), [input!], out why), why);
        var bytes = TextureAssetGeneration.EncodeArtifact(catalog.Resolve(TextureId).Current!);
        Assert.Equal("BEDTEX1\0"u8.ToArray(), bytes[..8]); Assert.Equal(20, bytes.Length);
        var image = BedTextureReader.Decode(bytes); Assert.Equal(1, image.Width); Assert.Equal(1, image.Height); Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Rgba);
        bytes[^1] = 0; Assert.Equal(255, image.Rgba[^1]);
        Assert.Throws<InvalidDataException>(() => BedTextureReader.Decode(bytes.AsSpan(0, 19)));
        Assert.Throws<InvalidDataException>(() => BedTextureReader.Decode([.. bytes, 0]));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 8193); Assert.Throws<InvalidDataException>(() => BedTextureReader.Decode(bytes));
        byte[] encoded = new byte[18 + (8192 * 8192 / 128) * 5]; encoded[2] = 10; encoded[13] = encoded[15] = 32; encoded[16] = 32;
        for (var at = 18; at < encoded.Length; at += 5) { encoded[at] = 255; encoded[at + 3] = encoded[at + 4] = 255; }
        Assert.True(AssetImageDecoder.Inspect(encoded, "tga", out _, out _, out why), why); // Complete RLE source fits the general preview dimension/pixel budget.
        Assert.False(TextureAssetGeneration.TryCreateSavedInput(record, encoded, "tga", [], true, "BEDTEX1", "", out _, out why));
        Assert.Contains("complete 64 MiB", why); // Refuses before the decoder would allocate the much larger image.
    }

    [Fact]
    public void Startup_keeps_missing_record_untextured_warns_and_writes_only_the_published_cache()
    {
        using var f = new Fixture(false); var before = f.SourceBytes();
        using var opened = new AssetCatalogProvisioner().Open(f.Project)!;
        Assert.Empty(opened.Problems); Assert.Equal(AssetCatalog.State.Valid, opened.Catalog.Resolve(ModelId).State);
        Assert.Contains(opened.Warnings, w => w.Contains("Texture colour.tga has no asset record yet. Reimport the model to add it.", StringComparison.Ordinal));
        var part = Assert.Single(ProjectAssets.Open(f.Root).ReadMesh(ModelId).Parts);
        Assert.Equal(new Vector4(.25f, .5f, .75f, 1), part.Material!.BaseColor); Assert.Null(part.Material.Texture);
        Assert.False(File.Exists(AssetRecord.SidecarFor(f.Image))); f.AssertUnchanged(before);
    }

    [Fact]
    public void Startup_material_resolves_independent_texture_by_GUID_through_the_same_saved_generation()
    {
        using var f = new Fixture(true); var before = f.SourceBytes();
        using var opened = new AssetCatalogProvisioner().Open(f.Project)!; Assert.Empty(opened.Problems);
        Assert.Equal(AssetCatalog.State.Valid, opened.Catalog.Resolve(ModelId).State); Assert.Equal(AssetCatalog.State.Valid, opened.Catalog.Resolve(TextureId).State);
        var assets = ProjectAssets.Open(f.Root); var part = Assert.Single(assets.ReadMesh(ModelId).Parts);
        Assert.Equal(Guid.ParseExact(TextureId, "N"), part.Material!.Texture);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, assets.ReadTexture(part.Material.Texture!.Value).Rgba);
        Assert.Equal(2, assets.ArtifactNames.Count); f.AssertUnchanged(before);
        var preview = new ModelPreview(opened.Catalog); preview.Select(opened.Catalog.Select(ModelId)!);
        Assert.True(preview.TryMesh(out var mesh, out var why), why); Assert.Equal(TextureId, mesh!.Material!.TextureAssetGuid);
        Assert.Equal(opened.Catalog.Resolve(TextureId).Current!.SourceHash, mesh.Material.TextureSourceHash);
    }

    [Fact]
    public void Explicit_reimport_publishes_new_texture_record_and_complete_runtime_parts_without_changing_model_identity()
    {
        using var f = new Fixture(false); using var catalog = f.InitialCatalog(); var old = catalog.Select(ModelId)!;
        using var controller = new AssetReimportController(f.Root, catalog, ProjectFrame.Gltf);
        Assert.True(controller.TryReimport(controller.OpenSettings(f.Model), 1, out var why), why);
        Assert.False(catalog.TryResolve(old, out _, out _)); var imageRecord = AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(f.Image)));
        Assert.Equal(f.Record.Provenance, imageRecord.Provenance); Assert.Equal(ModelId, AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(f.Model))).Guid);
        var assets = ProjectAssets.Open(f.Root); var part = Assert.Single(assets.ReadMesh(ModelId).Parts);
        Assert.Equal(Guid.ParseExact(imageRecord.Guid, "N"), part.Material!.Texture); Assert.Equal(2, assets.ArtifactNames.Count);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, assets.ReadTexture(part.Material.Texture!.Value).Rgba);
    }

    [Fact]
    public void Model_sidecar_save_failure_rolls_back_cache_and_only_new_texture_sidecar()
    {
        using var f = new Fixture(false); var before = f.SourceBytes(); using var catalog = f.InitialCatalog(); var old = catalog.Select(ModelId)!;
        using var controller = new AssetReimportController(f.Root, catalog, ProjectFrame.Gltf);
        using (var held = new FileStream(AssetRecord.SidecarFor(f.Model), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(controller.TryReimport(controller.OpenSettings(f.Model), 1, out var why)); Assert.StartsWith("Reimport failed:", why); Assert.DoesNotContain("could not be undone", why);
        }
        Assert.Null(controller.Published); Assert.True(catalog.TryResolve(old, out _, out _)); Assert.False(File.Exists(AssetRecord.SidecarFor(f.Image))); f.AssertUnchanged(before);
    }

    [Fact]
    public void Reimport_recovers_a_failed_startup_entry_with_a_new_explicit_complete_dependency_set()
    {
        using var f = new Fixture(true); File.AppendAllText(f.Model, "# edited source\n");
        using var opened = new AssetCatalogProvisioner().Open(f.Project)!;
        Assert.Equal(AssetCatalog.State.Failed, opened.Catalog.Resolve(ModelId).State); Assert.Null(opened.Catalog.Select(ModelId));
        using var controller = new AssetReimportController(f.Root, opened.Catalog, ProjectFrame.Gltf);
        Assert.True(controller.TryReimport(controller.OpenSettings(f.Model), 1, out var why), why);
        Assert.Equal(AssetCatalog.State.Valid, opened.Catalog.Resolve(ModelId).State);
        var part = Assert.Single(ProjectAssets.Open(f.Root).ReadMesh(ModelId).Parts);
        Assert.Equal(Guid.ParseExact(TextureId, "N"), part.Material!.Texture);
    }

    [Fact]
    public void Texture_GUID_wire_bytes_use_RFC_order_independently_of_little_endian_scalar_fields()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var geometry = Triangle(); var bytes = DerivedMeshCodec.Encode(new MeshPart[] { new(geometry, new(3, Vector4.One, guid)) });
        var materialAt = 12 + DerivedMeshCodec.Encode(geometry).Length - 8;
        Assert.Equal(new byte[] { 3, 0, 0, 0 }, bytes[materialAt..(materialAt + 4)]);
        Assert.Equal(Convert.FromHexString("00112233445566778899aabbccddeeff"), bytes[(materialAt + 20)..(materialAt + 36)]);
        Assert.Equal(guid, Assert.Single(BedMeshReader.Decode(bytes).Parts).Material!.Texture);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("m4-material-").FullName;
        public string Model => Path.Combine(Root, "assets", "self.obj"); public string Image => Path.Combine(Root, "assets", "colour.tga");
        public ProjectPaths Project => new(Path.Combine(Root, "data"), Path.Combine(Root, "schemas"));
        public AssetRecord Record { get; }
        public Fixture(bool textureRecord)
        {
            Directory.CreateDirectory(Path.Combine(Root, "assets")); Directory.CreateDirectory(Project.Data); Directory.CreateDirectory(Project.Schemas);
            File.WriteAllText(Model, "mtllib self.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nusemtl colour\nf 1/1 2/2 3/3\n");
            File.WriteAllText(Path.Combine(Root, "assets", "self.mtl"), "newmtl colour\nKd 0.25 0.5 0.75\nmap_Kd colour.tga\n");
            byte[] image = new byte[22]; image[2] = 2; image[12] = image[14] = 1; image[16] = 32; image[17] = 0x28; image[20] = image[21] = 255; File.WriteAllBytes(Image, image);
            Record = MaterialArtifactTests.Record(ModelId, Model, "obj"); File.WriteAllText(AssetRecord.SidecarFor(Model), Record.Write());
            if (textureRecord) File.WriteAllText(AssetRecord.SidecarFor(Image), MaterialArtifactTests.Record(TextureId, Image, "tga").Write());
        }
        public Dictionary<string, byte[]> SourceBytes() => Directory.GetFiles(Path.Combine(Root, "assets")).ToDictionary(p => p, File.ReadAllBytes);
        public void AssertUnchanged(Dictionary<string, byte[]> before) { Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(Path.Combine(Root, "assets")).Order(StringComparer.Ordinal)); foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key)); }
        public AssetCatalog InitialCatalog() { var catalog = new AssetCatalog(); Assert.True(catalog.Publish(catalog.BeginRefresh(), [new(Record, File.ReadAllBytes(Model), DerivedMeshCodec.Encode(Triangle()), [], true, "BEDMESH1", Record.Write())], out _)); return catalog; }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
