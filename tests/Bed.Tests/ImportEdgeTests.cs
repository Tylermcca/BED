using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Bed;
using Bed.Import;
using Xunit;

namespace Bed.Tests;

/// <summary>A010: INVENTED tiny glTF/OBJ inputs, written in isolated temp folders.
/// Format reference: https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
/// No game assets, packages, Blender, external downloads or production writes.</summary>
public sealed class ImportEdgeTests
{
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "A010-" + Guid.NewGuid().ToString("N"));
        public Scratch() => Directory.CreateDirectory(Root);
        public string Write(string name, string text)
        {
            string path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false)); return path;
        }
        public string Bytes(string name, byte[] bytes)
        {
            string path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes); return path;
        }
        public void Dispose()
        {
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe scratch.");
            Directory.Delete(Root, true);
        }
    }

    // Exactly 3 vertices: (0,0,0), (2,0,0), (0,3,0); +Z normals; UV (0,0),(1,0),(0,1).
    private static byte[] TriangleBytes()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        foreach (float x in new float[] { 0,0,0, 2,0,0, 0,3,0, 0,0,1, 0,0,1, 0,0,1, 0,0, 1,0, 0,1 }) w.Write(x);
        foreach (ushort i in new ushort[] { 0, 1, 2 }) w.Write(i);
        w.Write((ushort)0); return stream.ToArray();
    }

    private static JsonObject Gltf()
    {
        var g = JsonNode.Parse("""
        {"asset":{"version":"2.0","generator":"INVENTED A010 fixture"},"scene":0,
         "scenes":[{"nodes":[0]}],"nodes":[{"name":"Triangle","mesh":0}],
         "meshes":[{"name":"Mesh","primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"indices":3,"mode":4}]}],
         "buffers":[{"byteLength":104}],
         "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36,"target":34962},
                        {"buffer":0,"byteOffset":36,"byteLength":36,"target":34962},
                        {"buffer":0,"byteOffset":72,"byteLength":24,"target":34962},
                        {"buffer":0,"byteOffset":96,"byteLength":6,"target":34963}],
         "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[2,3,0]},
                      {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},
                      {"bufferView":2,"componentType":5126,"count":3,"type":"VEC2"},
                      {"bufferView":3,"componentType":5123,"count":3,"type":"SCALAR"}]}
        """)!.AsObject();
        Embed(g, TriangleBytes()); return g;
    }

    private static JsonObject Primitive(JsonObject g) => g["meshes"]![0]!["primitives"]![0]!.AsObject();
    private static void Embed(JsonObject g, byte[] bytes)
    {
        g["buffers"]![0]!["byteLength"] = bytes.Length;
        g["buffers"]![0]!["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bytes);
    }
    private static ImportResult ReadGltf(Scratch s, JsonObject g, string name = "mesh.gltf", ProjectFrame? frame = null) =>
        GltfImporter.Import(s.Write(name, g.ToJsonString()), frame ?? ProjectFrame.Gltf);

    private static ImportedAsset Good(ImportResult r, bool warning = false, string? diagnostic = null)
    {
        Assert.Equal(warning ? ImportOutcome.ImportsWithWarnings : ImportOutcome.Imports, r.Outcome);
        Assert.NotNull(r.Asset);
        if (warning)
        {
            Assert.NotEmpty(r.Issues); Assert.All(r.Issues, i => Assert.Equal(Severity.Warning, i.Severity));
            Assert.Contains(r.Diagnostics, d => d.Contains(diagnostic!, StringComparison.OrdinalIgnoreCase));
        }
        else Assert.Empty(r.Diagnostics);
        return r.Asset;
    }

    private static void Refused(ImportResult r, params string[] reasons)
    {
        Assert.Equal(ImportOutcome.Refused, r.Outcome); Assert.Null(r.Asset);
        Assert.Contains(r.Issues, i => i.Severity == Severity.Error);
        string text = string.Join("\n", r.Diagnostics);
        foreach (string reason in reasons) Assert.Contains(reason, text, StringComparison.OrdinalIgnoreCase);
    }

    private static void Triangle(ImportedMesh m)
    {
        Assert.Equal(3, m.Triangles.Length);
        Assert.All(m.Triangles, i => Assert.InRange(i, 0, m.Positions.Length - 1));
        Vector3 a = m.Positions[m.Triangles[0]], b = m.Positions[m.Triangles[1]], c = m.Positions[m.Triangles[2]];
        Assert.True(Vector3.Cross(b - a, c - a).Length() > .01f);
    }

    [Fact]
    public void GltfEmbeddedDataUriImportsExactTriangle()
    {
        using var s = new Scratch(); var mesh = Assert.Single(Good(ReadGltf(s, Gltf())).Meshes);
        Triangle(mesh); Assert.Equal((Vector3.Zero, new Vector3(2, 3, 0)), mesh.Bounds);
        Assert.Equal([Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], mesh.Normals);
        Assert.Equal([Vector2.Zero, Vector2.UnitX, Vector2.UnitY], mesh.Uv0);
    }

    [Fact]
    public void GltfWithoutIndicesUsesSequentialVertices()
    {
        using var s = new Scratch(); var g = Gltf(); Primitive(g).Remove("indices");
        var mesh = Assert.Single(Good(ReadGltf(s, g)).Meshes); Triangle(mesh); Assert.Equal([0,1,2], mesh.Triangles);
    }

    [Fact]
    public void GltfSeveralPrimitivesRetainBothTriangleSets()
    {
        using var s = new Scratch(); var g = Gltf();
        g["meshes"]![0]!["primitives"]!.AsArray().Add(Primitive(g).DeepClone());
        var meshes = Good(ReadGltf(s, g)).Meshes; Assert.Equal(2, meshes.Count);
        Assert.All(meshes, Triangle); Assert.Equal(6, meshes.Sum(m => m.Triangles.Length));
    }

    [Fact]
    public void GltfUsesNamedDefaultSceneInsteadOfCombiningScenes()
    {
        using var s = new Scratch(); var g = Gltf();
        g["nodes"]!.AsArray().Add(JsonNode.Parse("""{"name":"Other","mesh":0,"translation":[10,0,0]}"""));
        g["scenes"]!.AsArray().Add(JsonNode.Parse("""{"nodes":[1]}""")); g["scene"] = 1;
        var mesh = Assert.Single(Good(ReadGltf(s, g)).Meshes);
        Assert.Equal("Other", mesh.Node); Assert.Equal((new Vector3(10,0,0), new Vector3(12,3,0)), mesh.Bounds);
    }

    [Fact]
    public void GltfSingleSceneWithoutDefaultStillImportsItsGeometry()
    {
        using var s = new Scratch(); var g = Gltf(); g.Remove("scene");
        var mesh = Assert.Single(Good(ReadGltf(s, g)).Meshes); Triangle(mesh); Assert.Equal(3, mesh.Positions.Length);
    }

    [Fact]
    public void GltfSparsePositionAccessorOverlaysTheZeroBase()
    {
        using var s = new Scratch(); var g = Gltf(); byte[] data = new byte[144]; TriangleBytes().CopyTo(data, 0);
        data[104] = 0; data[105] = 1; data[106] = 2;
        Array.Copy(TriangleBytes(), 0, data, 108, 36);
        var views = g["bufferViews"]!.AsArray();
        views.Add(JsonNode.Parse("""{"buffer":0,"byteOffset":104,"byteLength":3}"""));
        views.Add(JsonNode.Parse("""{"buffer":0,"byteOffset":108,"byteLength":36}"""));
        var a = g["accessors"]![0]!.AsObject(); a.Remove("bufferView");
        a["sparse"] = JsonNode.Parse("""{"count":3,"indices":{"bufferView":4,"componentType":5121},"values":{"bufferView":5}}""");
        Embed(g, data);
        var mesh = Assert.Single(Good(ReadGltf(s, g)).Meshes);
        Assert.Equal([Vector3.Zero, new Vector3(2,0,0), new Vector3(0,3,0)], mesh.Positions); Triangle(mesh);
    }

    [Fact]
    public void GltfNormalizedUnsignedByteUvsDecodeToUnitCoordinates()
    {
        using var s = new Scratch(); var g = Gltf(); byte[] data = TriangleBytes();
        new byte[] { 0,0,255,0,0,255 }.CopyTo(data, 72);
        g["bufferViews"]![2]!["byteLength"] = 6;
        g["accessors"]![2]!["componentType"] = 5121; g["accessors"]![2]!["normalized"] = true; Embed(g, data);
        var mesh = Assert.Single(Good(ReadGltf(s, g)).Meshes);
        Assert.Equal([Vector2.Zero, Vector2.UnitX, Vector2.UnitY], mesh.Uv0); Triangle(mesh);
    }

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("%2e%2e%2fescape.bin")]
    public void GltfEscapingBufferUriIsRefused(string uri)
    {
        using var s = new Scratch(); s.Bytes("escape.bin", TriangleBytes()); var g = Gltf(); g["buffers"]![0]!["uri"] = uri;
        Refused(ReadGltf(s, g, "child/mesh.gltf"), "outside", "escape.bin");
    }

    [Fact]
    public void GltfRelativeBufferInsideFolderIsReadWithoutChangingIt()
    {
        using var s = new Scratch(); byte[] bytes = TriangleBytes(); string path = s.Bytes("data.bin", bytes);
        var g = Gltf(); g["buffers"]![0]!["uri"] = "data.bin";
        Triangle(Assert.Single(Good(ReadGltf(s, g)).Meshes)); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void GltfMissingExternalBufferRefusesWithNamedResource()
    {
        using var s = new Scratch(); var g = Gltf(); g["buffers"]![0]!["uri"] = "missing.bin";
        Refused(ReadGltf(s, g), "missing.bin", "read");
    }

    [Fact]
    public void GltfHugeDeclaredAccessorCountRefusesRatherThanReadingBeyondItsBytes()
    {
        // One million records declared over 36 bytes: no large backing buffer or real allocation fixture.
        using var s = new Scratch(); var g = Gltf(); g["accessors"]![0]!["count"] = 1_000_000;
        Refused(ReadGltf(s, g), "mesh.gltf", "count");
    }

    [Fact]
    public void GltfOutOfRangeIndexNamesTheBadIndexAndCount()
    {
        using var s = new Scratch(); var g = Gltf(); var bytes = TriangleBytes(); bytes[100] = 99; Embed(g, bytes);
        Refused(ReadGltf(s, g), "99", "3", "index");
    }

    private static byte[] Glb(string json, uint? declaredJsonLength = null)
    {
        byte[] data = Encoding.UTF8.GetBytes(json); int pad = (4 - data.Length % 4) % 4;
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(0x46546c67u); w.Write(2u); w.Write((uint)(20 + data.Length + pad));
        w.Write(declaredJsonLength ?? (uint)(data.Length + pad)); w.Write(0x4e4f534au);
        w.Write(data); for (int i = 0; i < pad; i++) w.Write((byte)' ');
        return stream.ToArray();
    }

    [Fact]
    public void GlbMalformedJsonReturnsRefusalInsteadOfThrowing()
    {
        using var s = new Scratch();
        Refused(GltfImporter.Import(s.Bytes("broken.glb", Glb("{ invalid")), ProjectFrame.Gltf), "broken.glb", "JSON");
    }

    [Fact]
    public void GlbOverflowingJsonChunkLengthIsRefusedWithoutSlicingAnInvalidRange()
    {
        using var s = new Scratch();
        Refused(GltfImporter.Import(s.Bytes("oversize.glb", Glb("{}", uint.MaxValue)), ProjectFrame.Gltf), "oversize.glb", "length");
    }

    [Fact]
    public void GltfMalformedJsonRootReturnsNamedRefusal()
    {
        using var s = new Scratch();
        Refused(GltfImporter.Import(s.Write("array.gltf", "[]"), ProjectFrame.Gltf), "array.gltf", "object");
    }

    [Fact]
    public void GltfMissingPositionAccessorReturnsRefusalInsteadOfNullDereference()
    {
        using var s = new Scratch(); var g = Gltf(); Primitive(g)["attributes"]!.AsObject().Remove("POSITION");
        Refused(ReadGltf(s, g), "POSITION");
    }

    [Fact]
    public void GltfTruncatedDataUriReturnsNamedRefusal()
    {
        using var s = new Scratch(); var g = Gltf(); g["buffers"]![0]!["uri"] = "data:application/octet-stream;base64,AA==";
        Refused(ReadGltf(s, g), "mesh.gltf", "buffer");
    }

    [Theory]
    [InlineData("KHR_draco_mesh_compression", "compression")]
    [InlineData("EXT_meshopt_compression", "compression")]
    [InlineData("A010_unsupported", "extension")]
    public void GltfUnsupportedRequiredFeatureNamesItsReason(string extension, string reason)
    {
        using var s = new Scratch(); var g = Gltf(); g["extensionsUsed"] = new JsonArray(extension); g["extensionsRequired"] = new JsonArray(extension);
        Refused(ReadGltf(s, g), extension, reason);
    }

    [Fact]
    public void GltfOptionalUnknownExtensionDoesNotDiscardGeometry()
    {
        using var s = new Scratch(); var g = Gltf(); g["extensionsUsed"] = new JsonArray("A010_optional");
        Triangle(Assert.Single(Good(ReadGltf(s, g)).Meshes));
    }

    [Fact]
    public void GltfImporterCreatesNoFilesOrChangesToItsInputs()
    {
        using var s = new Scratch(); string path = s.Write("mesh.gltf", Gltf().ToJsonString());
        byte[] before = SHA256.HashData(File.ReadAllBytes(path));
        Good(GltfImporter.Import(path, ProjectFrame.Gltf));
        Assert.Equal([path], Directory.GetFiles(s.Root)); Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    private const string Vertices = "v 0 0 0\nv 2 0 0\nv 0 3 0\n";
    private const string UvsNormals = "vt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\n";
    private static ImportResult Obj(Scratch s, string text, ProjectFrame? frame = null) =>
        ObjImporter.Import(s.Write("mesh.obj", text), frame ?? ProjectFrame.Gltf);

    [Fact]
    public void ObjTriangleWithoutUvsOrNormalsNamesMissingUvWarning()
    {
        using var s = new Scratch(); var mesh = Assert.Single(Good(Obj(s, Vertices + "f 1 2 3\n"), true, "UV0").Meshes);
        Triangle(mesh); Assert.Empty(mesh.Uv0); Assert.Empty(mesh.Normals);
    }

    [Fact]
    public void ObjFullReferencesImportNormalsUvsAndNoDiagnostics()
    {
        using var s = new Scratch(); var mesh = Assert.Single(Good(Obj(s, Vertices + UvsNormals + "f 1/1/1 2/2/1 3/3/1\n")).Meshes);
        Triangle(mesh); Assert.Equal([Vector2.Zero, Vector2.UnitX, Vector2.UnitY], mesh.Uv0);
        Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ, n));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void ObjConvexPolygonsFanIntoExactlyNMinusTwoTriangles(int corners, int expected)
    {
        using var s = new Scratch();
        string vertices = "v 0 0 0\nv 2 0 0\nv 3 1 0\nv 1 3 0\n" + (corners == 5 ? "v -1 1 0\n" : "");
        string face = "f " + string.Join(" ", Enumerable.Range(1, corners)) + "\n";
        var mesh = Assert.Single(Good(Obj(s, vertices + face), true, "UV0").Meshes);
        Assert.Equal(expected * 3, mesh.Triangles.Length); Assert.Equal(corners, mesh.Positions.Length);
        for (int k = 0; k < expected; k++)
            Assert.True(Vector3.Cross(mesh.Positions[mesh.Triangles[k*3+1]] - mesh.Positions[mesh.Triangles[k*3]],
                mesh.Positions[mesh.Triangles[k*3+2]] - mesh.Positions[mesh.Triangles[k*3]]).Z > 0);
    }

    [Fact]
    public void ObjNegativeReferencesResolveAllThreeAttributeStreams()
    {
        using var s = new Scratch();
        var mesh = Assert.Single(Good(Obj(s, Vertices + UvsNormals + "f -3/-3/-1 -2/-2/-1 -1/-1/-1\n")).Meshes);
        Assert.Equal([Vector3.Zero, new Vector3(2,0,0), new Vector3(0,3,0)], mesh.Positions);
        Assert.Equal([Vector2.Zero, Vector2.UnitX, Vector2.UnitY], mesh.Uv0); Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ, n));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ObjCommentsBlanksTabsAndLineEndingsPreserveGeometry(string newline)
    {
        using var s = new Scratch();
        string text = "# original comment\n\n" + Vertices + "\tf\t1\t2\t3\n";
        var mesh = Assert.Single(Good(Obj(s, text.Replace("\n", newline, StringComparison.Ordinal)), true, "UV0").Meshes);
        Triangle(mesh); Assert.Equal((Vector3.Zero, new Vector3(2,3,0)), mesh.Bounds);
    }

    [Fact]
    public void ObjInlineFaceCommentDoesNotBecomeAFourthVertex()
    {
        using var s = new Scratch();
        Triangle(Assert.Single(Good(Obj(s, Vertices + "f 1 2 3 # triangle\n"), true, "UV0").Meshes));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("-4")]
    public void ObjInvalidPositionIndexIsRefusedWithLineAndVertex(string index)
    {
        using var s = new Scratch(); Refused(Obj(s, Vertices + $"f 1 2 {index}\n"), "line 4", "vertex", index);
    }

    [Theory]
    [InlineData("vt", "1/-4/1", "texture")]
    [InlineData("vn", "1/1/-2", "normal")]
    public void ObjTooNegativeOptionalReferenceIsRefusedRatherThanSilentlyDropped(string stream, string corner, string reason)
    {
        _ = stream;
        using var s = new Scratch(); Refused(Obj(s, Vertices + UvsNormals + $"f {corner} 2/2/1 3/3/1\n"), "line", reason);
    }

    [Fact]
    public void ObjOverflowingIntegerIndexIsRefusedInsteadOfThrowing()
    {
        using var s = new Scratch(); Refused(Obj(s, Vertices + "f 1 2 999999999999999999999\n"), "line 4", "number");
    }

    [Theory]
    [InlineData("banana", "number")]
    [InlineData("NaN", "finite")]
    [InlineData("Infinity", "finite")]
    public void ObjBadOrNonFinitePositionIsRefused(string number, string reason)
    {
        using var s = new Scratch(); Refused(Obj(s, $"v {number} 0 0\nv 2 0 0\nv 0 3 0\nf 1 2 3\n"), "line 1", reason);
    }

    [Fact]
    public void ObjMissingVertexComponentsNamesTheLineAndMissingValues()
    {
        using var s = new Scratch(); Refused(Obj(s, "v 1\n" + Vertices + "f 1 2 3\n"), "line 1", "missing");
    }

    [Fact]
    public void ObjTooSmallFaceIsRefusedWithItsReason()
    {
        using var s = new Scratch(); Refused(Obj(s, Vertices + "f 1 2\n"), "line 4", "three");
    }

    [Fact]
    public void ObjWithoutAnyFacesRefusesInsteadOfReturningAnEmptySuccess()
    {
        using var s = new Scratch(); Refused(Obj(s, "# empty geometry\n" + Vertices), "no faces");
    }

    [Fact]
    public void ObjObjectAndGroupBoundariesKeepBothNamedMeshes()
    {
        using var s = new Scratch(); var meshes = Good(Obj(s, Vertices + "o First\nf 1 2 3\ng Second\nf 3 2 1\n"), true, "UV0").Meshes;
        Assert.Equal(["First", "Second"], meshes.Select(m => m.Node)); Assert.All(meshes, Triangle);
    }

    [Fact]
    public void ObjMirrorFrameTransformsPositionsAndFlipsWindingOnce()
    {
        using var s = new Scratch(); var mesh = Assert.Single(Good(Obj(s, Vertices + UvsNormals + "f 1/1/1 2/2/1 3/3/1\n", ProjectFrame.YUpLeftHanded)).Meshes);
        Assert.True(mesh.WindingFlipped); Assert.Contains(new Vector3(-2,0,0), mesh.Positions);
        var a = mesh.Positions[mesh.Triangles[0]]; var b = mesh.Positions[mesh.Triangles[1]]; var c = mesh.Positions[mesh.Triangles[2]];
        Assert.True(Vector3.Cross(b-a,c-a).Z > 0); Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ,n));
    }

    private const string Markers = """
    {"kind":"accept.markers","version":1,"frame":"native","collision":"module.obj#Hull",
     "markers":[{"id":"socket","position":[1,2,3],"up":[0,0,1],"outward":[0,1,0]}]}
    """;

    [Fact]
    public void MarkerReaderPreservesExplicitNativeCoordinatesAndMetadata()
    {
        var r = MarkerFile.Read(Markers, "accept.markers"); var m = Assert.Single(r.Markers);
        Assert.Equal("native", r.Frame); Assert.Equal("module.obj#Hull", r.Collision);
        Assert.Equal("socket", m.Id); Assert.Equal(new Vector3(1,2,3), m.Position);
        Assert.Equal(Vector3.UnitZ, m.Up); Assert.Equal(Vector3.UnitY, m.Outward);
    }

    [Theory]
    [InlineData("kind", "$.kind")]
    [InlineData("version", "$.version")]
    [InlineData("duplicate", "$.markers")]
    [InlineData("unknown-root", "$.extra")]
    [InlineData("unknown-marker", "$.markers[0].extra")]
    [InlineData("short-vector", "$.markers[0].position")]
    [InlineData("string-component", "$.markers[0].position")]
    public void MarkerStrictReadingNamesTheFaultyPath(string mutation, string expectedPath)
    {
        var g = JsonNode.Parse(Markers)!.AsObject();
        switch (mutation)
        {
            case "kind": g["kind"] = "other"; break;
            case "version": g["version"] = 999; break;
            case "duplicate": g["markers"]!.AsArray().Add(g["markers"]![0]!.DeepClone()); break;
            case "unknown-root": g["extra"] = true; break;
            case "unknown-marker": g["markers"]![0]!["extra"] = true; break;
            case "short-vector": g["markers"]![0]!["position"] = new JsonArray(1,2); break;
            case "string-component": g["markers"]![0]!["position"]![1] = "two"; break;
        }
        var error = Assert.Throws<DocumentFormatException>(() => MarkerFile.Read(g.ToJsonString(), "accept.markers"));
        Assert.StartsWith(expectedPath, error.Path, StringComparison.Ordinal); Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }
}
