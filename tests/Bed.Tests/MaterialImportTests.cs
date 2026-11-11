using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Bed.Import;
using Xunit;

namespace Bed.Tests;

public sealed class MaterialImportTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("bed-material-").FullName;
    private const string Triangle = "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\n";
    private string Put(string name, string text) { var path = Path.Combine(root, name); File.WriteAllText(path, text); return path; }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void OBJ_material_changes_split_meshes_and_pin_MTL_and_image_without_assigning_asset_identity()
    {
        Put("colours.mtl", "newmtl painted\nKd 0.25 0.5 0.75\nmap_Kd colour.png\nnewmtl plain\nKd 1 0 0\n");
        File.WriteAllBytes(Path.Combine(root, "colour.png"), [1, 2, 3]); // Import receipt pins bytes; admission decodes separately.
        var path = Put("self.obj", "mtllib colours.mtl\no box\n" + Triangle + "usemtl painted\nf 1/1 2/2 3/3\nusemtl plain\nf 1/1 3/3 2/2\n");
        var result = ObjImporter.Import(path, ProjectFrame.Gltf);
        Assert.Equal(ImportOutcome.Imports, result.Outcome);
        Assert.Equal(2, result.Asset!.Meshes.Count);
        var first = result.Asset.Meshes[0]; var second = result.Asset.Meshes[1];
        Assert.Equal("box", first.Node); Assert.Equal(first.Node, second.Node);
        Assert.Equal("painted", first.Material); Assert.Equal("plain", second.Material);
        Assert.Equal(new ImportedMaterial(0, "painted", new(.25f, .5f, .75f, 1), "colour.png"), first.MaterialInfo);
        Assert.Equal(new ImportedMaterial(1, "plain", new(1, 0, 0, 1), null), second.MaterialInfo);
        Assert.Equal(["painted", "plain"], result.Asset.Materials);
        Assert.Equal("colour.png", Assert.Single(result.Asset.Textures).Source);
        Assert.Equal(["colour.png", "colours.mtl"], result.ExternalReads.Select(r => r.RelativePath));
        Assert.All(result.ExternalReads, r => Assert.Equal(Hash(File.ReadAllBytes(Path.Combine(root, r.RelativePath))), r.SourceHash));
        Assert.Equal([0, 1, 2], first.Triangles); Assert.Equal(3, second.Positions.Length);
        Assert.Equal(new Vector3(1, 0, 0), first.Positions[1]);
        var before = result.ExternalReads.Single(r => r.RelativePath == "colours.mtl").SourceHash;
        Put("colours.mtl", "newmtl painted\nKd 0.5 0.5 0.5\nmap_Kd colour.png\nnewmtl plain\nKd 1 0 0\n");
        var changed = ObjImporter.Import(path, ProjectFrame.Gltf);
        Assert.NotEqual(before, changed.ExternalReads.Single(r => r.RelativePath == "colours.mtl").SourceHash);
        Assert.Equal(new Vector4(.5f, .5f, .5f, 1), changed.Asset!.Meshes[0].MaterialInfo!.BaseColor);
        Assert.Equal(new Vector4(.25f, .5f, .75f, 1), first.MaterialInfo!.BaseColor);
    }

    [Fact]
    public void GlTF_uses_material_ordinal_even_when_names_match_and_preserves_factor_and_exact_image_source()
    {
        var path = GltfReadSetTests.WriteFixture(root);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["materials"] = JsonNode.Parse("""[{"name":"same","pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,1],"baseColorTexture":{"index":0}}},{"name":"same"}]""");
        json["textures"] = JsonNode.Parse("""[{"source":0}]""");
        json["images"] = JsonNode.Parse("""[{"uri":"colour.png"}]""");
        // Self-contained one-pixel PNG; no external fixture or game art.
        File.WriteAllBytes(Path.Combine(root, "colour.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1EAAAAASUVORK5CYII="));
        var primitives = json["meshes"]![0]!["primitives"]!.AsArray();
        primitives[0]!["material"] = 0;
        var second = primitives[0]!.DeepClone(); second["material"] = 1; primitives.Add(second);
        File.WriteAllText(path, json.ToJsonString());
        var result = GltfImporter.Import(path, ProjectFrame.Gltf);
        Assert.NotEqual(ImportOutcome.Refused, result.Outcome);
        Assert.Equal(2, result.Asset!.Meshes.Count);
        Assert.Equal(new ImportedMaterial(0, "same", new(.2f, .3f, .4f, 1), "colour.png"), result.Asset.Meshes[0].MaterialInfo);
        Assert.Equal(new ImportedMaterial(1, "same", Vector4.One, null), result.Asset.Meshes[1].MaterialInfo);
        Assert.Equal(result.Asset.Meshes[0].Positions, result.Asset.Meshes[1].Positions);
        Assert.Equal("colour.png", Assert.Single(result.Asset.Textures).Source);
        Assert.Contains(result.ExternalReads, r => r.RelativePath == "colour.png" && r.SourceHash == Hash(File.ReadAllBytes(Path.Combine(root, "colour.png"))));
    }

    [Fact]
    public void Untextured_OBJ_keeps_geometry_and_has_no_material_or_external_reads()
    {
        var result = ObjImporter.Import(Put("self.obj", Triangle + "f 1/1 2/2 3/3\n"), ProjectFrame.Gltf);
        Assert.Equal(ImportOutcome.Imports, result.Outcome);
        var mesh = Assert.Single(result.Asset!.Meshes);
        Assert.Null(mesh.Material); Assert.Null(mesh.MaterialInfo);
        Assert.Equal([0, 1, 2], mesh.Triangles);
        Assert.Empty(result.Asset.Materials); Assert.Empty(result.Asset.Textures); Assert.Empty(result.ExternalReads);
    }

    [Fact]
    public void A_used_undeclared_OBJ_material_is_refused_instead_of_silently_dropped()
    {
        var result = ObjImporter.Import(Put("self.obj", Triangle + "usemtl absent\nf 1/1 2/2 3/3\n"), ProjectFrame.Gltf);
        Assert.Equal(ImportOutcome.Refused, result.Outcome); Assert.Null(result.Asset);
        Assert.Contains("Material is not declared: absent", Assert.Single(result.Diagnostics));
    }
}
