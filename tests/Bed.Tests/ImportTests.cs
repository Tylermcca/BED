using System.Numerics;
using System.Text.Json;
using Bed.Import;
using Xunit;

namespace Bed.Tests;

/// <summary>The glTF importer against Codex's A005 fixtures (fixtures/import): outcomes and diagnostics exactly as
/// expected.json pins them, plus the measured checks for each case.</summary>
public sealed class ImportTests
{
    private static readonly string Fixtures = Path.Combine(Root(), "fixtures", "import");

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Bed.slnx"))) return d.FullName;
        throw new InvalidOperationException("can't find the repo root");
    }

    private static string F(string name) => Path.Combine(Fixtures, name);

    private static ImportResult Import(string name, ProjectFrame? frame = null) => GltfImporter.Import(F(name), frame ?? ProjectFrame.Gltf);

    private static void Near(Vector3 expected, Vector3 actual, float tol = 1e-5f) =>
        Assert.True(Vector3.Distance(expected, actual) <= tol, $"expected {expected}, got {actual}");

    private static Vector3 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());

    public static IEnumerable<object[]> GlbCases()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(F("expected.json")));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            if (c.GetProperty("file").ValueKind == JsonValueKind.String && c.GetProperty("file").GetString()!.EndsWith(".glb"))
                yield return [c.GetProperty("id").GetString()!, c.GetProperty("file").GetString()!];
    }

    private static JsonElement Case(string file) => JsonDocument.Parse(File.ReadAllText(F("expected.json"))).RootElement
        .GetProperty("cases").EnumerateArray().First(c => c.GetProperty("file").ValueKind == JsonValueKind.String && c.GetProperty("file").GetString() == file);

    [Theory]
    [MemberData(nameof(GlbCases))]
    public void EveryGlbCaseHasItsExpectedOutcomeAndDiagnostics(string id, string file)
    {
        var c = Case(file);
        var r = Import(file);
        var outcome = r.Outcome switch { ImportOutcome.Imports => "imports", ImportOutcome.ImportsWithWarnings => "imports-with-warnings", _ => "refused" };
        Assert.True(c.GetProperty("outcome").GetString() == outcome, $"{id} {file}: expected {c.GetProperty("outcome")}, got {outcome}: {string.Join(" | ", r.Diagnostics)}");
        Assert.Equal(c.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetString()!), r.Diagnostics);
        if (r.Outcome == ImportOutcome.Refused) Assert.Null(r.Asset);   // nothing usable comes out of a refusal
    }

    [Fact]
    public void Im01TheCube()
    {
        var mesh = Assert.Single(Import("im01_cube.glb").Asset!.Meshes);
        Assert.Equal(12, mesh.Triangles.Length / 3);
        Near(new(-0.5f), mesh.Bounds.Min);
        Near(new(0.5f), mesh.Bounds.Max);
        Assert.NotEmpty(mesh.Normals);
        Assert.NotEmpty(mesh.Uv0);
    }

    [Fact]
    public void Im02MaterialsAndTextureRoles()
    {
        foreach (var file in new[] { "im02_embedded.glb", "im02_relative.glb" })
        {
            var a = Import(file).Asset!;
            Assert.Equal(2, a.Materials.Count);
            Assert.Equal("sRGB", a.Textures.Single(t => t.Role == "BaseColor").ColourSpace);
            Assert.Equal("linear", a.Textures.Single(t => t.Role == "Normal").ColourSpace);
            Assert.DoesNotContain(a.Textures, t => t.Missing);
        }
        var rel = Import("im02_relative.glb").Asset!;
        Assert.Equal(["base.png", "normal.png"], rel.Textures.Select(t => t.Source).Order());
    }

    [Fact]
    public void Im03SkinsAndClipsAreReportedNotPlayed()
    {
        var a = Import("im03_skin.glb").Asset!;
        Assert.Equal((1, 3), (a.Skins, a.Joints));
        var clip = Assert.Single(a.Clips);
        Assert.Equal(1.0f, clip.Seconds, 1e-5f);
        var mesh = Assert.Single(a.Meshes);
        Assert.True(mesh.Skinned);
        Near(new(0, 0, 0), mesh.Positions[0]);
        Near(new(1, 0, 0), mesh.Positions[1]);
        Near(new(0, 1, 0), mesh.Positions[2]);
    }

    [Fact]
    public void Im04WorldTransformsAreBakedAndWindingCorrectedOnce()
    {
        var checks = Case("im04_transforms.glb").GetProperty("checks");
        var meshes = Import("im04_transforms.glb").Asset!.Meshes.ToDictionary(m => m.Node);
        foreach (var node in new[] { "Mirror", "DoubleReflection" })
        {
            var c = checks.GetProperty(node);
            Near(V(c.GetProperty("bounds_min")), meshes[node].Bounds.Min);
            Near(V(c.GetProperty("bounds_max")), meshes[node].Bounds.Max);
            Assert.Equal(c.GetProperty("determinant").GetSingle(), meshes[node].WorldDeterminant, 1e-4f);
            Assert.Equal(c.GetProperty("winding_flips").GetInt32() == 1, meshes[node].WindingFlipped);
        }
        var slanted = checks.GetProperty("Slanted");
        var s = meshes["Slanted"];
        Assert.Equal(6f, s.WorldDeterminant, 1e-4f);
        var expectedVerts = slanted.GetProperty("world_vertices").EnumerateArray().Select(V).ToList();
        Assert.All(expectedVerts, v => Assert.Contains(s.Positions, p => Vector3.Distance(p, v) <= 1e-4f));
        Assert.Contains(s.Normals, n => Vector3.Distance(n, V(slanted.GetProperty("normal"))) <= 1e-4f);
    }

    [Fact]
    public void Im05FramesPutTheArrowAndItsMarkerWhereEachProjectExpects()
    {
        var checks = Case("im05_arrow.glb").GetProperty("checks");
        var gltf = Import("im05_arrow.glb").Asset!;
        Near(V(checks.GetProperty("bounds_min")), gltf.Meshes[0].Bounds.Min);
        Near(V(checks.GetProperty("bounds_max")), gltf.Meshes[0].Bounds.Max);
        Assert.Equal(24, gltf.Meshes[0].Triangles.Length / 3);
        Near(V(checks.GetProperty("input").GetProperty("marker")), gltf.Markers.Single().Position);

        foreach (var frame in new[] { ProjectFrame.YUpLeftHanded, ProjectFrame.ZUpRightHanded })
        {
            var expect = checks.GetProperty(frame.Name);
            var a = Import("im05_arrow.glb", frame).Asset!;
            var marker = a.Markers.Single();
            Near(V(expect.GetProperty("marker")), marker.Position);
            Near(V(expect.GetProperty("marker_outward")), marker.Outward!.Value);
            Near(V(expect.GetProperty("up")), marker.Up!.Value);
            Assert.Equal(expect.GetProperty("winding_flips").GetInt32() == 1, a.Meshes[0].WindingFlipped);
            Assert.Equal(expect.GetProperty("determinant").GetSingle(), frame.Determinant, 1e-5f);
            // Outward winding survives every frame: a mirroring frame negates the volume and the one winding flip restores it,
            // so the signed volume stays positive.
            Assert.True(SignedVolume(a.Meshes[0]) > 0, $"{frame.Name}: winding points inward");
            Assert.Equal(checks.GetProperty("signed_volume").GetSingle(), MathF.Abs(SignedVolume(a.Meshes[0])), 1e-3f);
        }
    }

    private static float SignedVolume(ImportedMesh m)
    {
        var v = 0f;
        for (var i = 0; i < m.Triangles.Length; i += 3)
            v += Vector3.Dot(m.Positions[m.Triangles[i]], Vector3.Cross(m.Positions[m.Triangles[i + 1]], m.Positions[m.Triangles[i + 2]]));
        return v / 6;
    }

    [Fact]
    public void Im06TruncatedAndWrongExtensionAreRefusedAndNothingIsWritten()
    {
        var dir = Directory.CreateTempSubdirectory("bed-import-").FullName;
        try
        {
            var cube = File.ReadAllBytes(F("im01_cube.glb"));
            File.WriteAllBytes(Path.Combine(dir, "truncated.glb"), cube[..^11]);
            File.WriteAllBytes(Path.Combine(dir, "unsupported.fbx"), cube);
            var before = Directory.GetFiles(dir).Length;
            Assert.Equal(["error: truncated.glb: declared GLB length exceeds available bytes"],
                GltfImporter.Import(Path.Combine(dir, "truncated.glb"), ProjectFrame.Gltf).Diagnostics);
            Assert.Equal(["error: unsupported.fbx: FBX is not supported; export glTF 2.0"],
                GltfImporter.Import(Path.Combine(dir, "unsupported.fbx"), ProjectFrame.Gltf).Diagnostics);
            Assert.Equal(before, Directory.GetFiles(dir).Length);   // the importer never writes
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Im06DegenerateTrianglesAreRemovedAndCounted()
    {
        var mesh = Assert.Single(Import("im06_degenerate.glb").Asset!.Meshes);
        Assert.Equal(1, mesh.Triangles.Length / 3);
    }

    [Fact]
    public void Im07AMissingTextureWarnsAndTheRestImports()
    {
        var a = Import("im07_missing_texture.glb").Asset!;
        Assert.Equal(12, a.Meshes.Sum(m => m.Triangles.Length) / 3);
        Assert.Equal(2, a.Materials.Count);
        Assert.True(a.Textures.Single(t => t.Source == "missing_normal.png").Missing);
        Assert.False(a.Textures.Single(t => t.Source == "base.png").Missing);
    }

    [Fact]
    public void Im10TwoNodesShareOneMeshAndKeepTheirOwnPlaces()
    {
        var a = Import("im10_instances.glb").Asset!;
        Assert.Equal(1, a.LogicalMeshes);
        Assert.Equal(["Left", "Right"], a.Meshes.Select(m => m.Node).Order());
        var centres = a.Meshes.OrderBy(m => m.Node).Select(m => (m.Bounds.Min + m.Bounds.Max) / 2).ToList();
        Near(new(-2, 0, 0), centres[0]);
        Near(new(2, 0, 0), centres[1]);
    }

    [Fact]
    public void Im11TheOriginAndPivotPlaceTheBoundsExactly()
    {
        var c = Case("im11_origin.glb").GetProperty("checks");
        var mesh = Assert.Single(Import("im11_origin.glb").Asset!.Meshes);
        Near(V(c.GetProperty("world_bounds_min")), mesh.Bounds.Min, 1e-4f);
        Near(V(c.GetProperty("world_bounds_max")), mesh.Bounds.Max, 1e-4f);
    }
}
