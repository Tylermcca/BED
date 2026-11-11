using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Bed.Import;
using Xunit;

namespace Bed.Tests;

/// <summary>IM-09: a native-frame module (OBJ plus markers) keeps its geometry, markers and collision identity, with no conversion.</summary>
public sealed class ObjImportTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("bed-obj-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Bed.slnx"))) return d.FullName;
        throw new InvalidOperationException("can't find the repo root");
    }

    // The fixture is made at test time by Codex's generator (fixtures/import/make_fixtures.py: module_with_markers).
    private bool Generate()
    {
        var script = "import importlib.util,sys,os\n" +
            "spec=importlib.util.spec_from_file_location('mf', sys.argv[1]); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)\n" +
            "for k,v in m.module_with_markers().items(): open(os.path.join(sys.argv[2],k),'wb').write(v)\n";
        var psi = new ProcessStartInfo("python", ["-c", script, Path.Combine(Root(), "fixtures", "import", "make_fixtures.py"), dir])
        { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit(60_000);
            return p.ExitCode == 0 && File.Exists(Path.Combine(dir, "module.obj"));
        }
        catch (System.ComponentModel.Win32Exception) { return false; }   // no python on this machine
    }

    [Fact]
    public void Im09TheModuleImportsNativeWithItsMarkers()
    {
        Assert.True(Generate(), "making the fixture needs python (fixtures/import/make_fixtures.py)");
        var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "fixtures", "import", "expected.json"))).RootElement
            .GetProperty("cases").EnumerateArray().First(c => c.GetProperty("id").GetString() == "IM-09");
        var checks = expected.GetProperty("checks");
        V3 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());

        var r = ObjImporter.Import(Path.Combine(dir, "module.obj"), ProjectFrame.Gltf);   // native: no conversion
        Assert.Equal(ImportOutcome.ImportsWithWarnings, r.Outcome);
        Assert.StartsWith("warning: module.obj: UV0 is absent", Assert.Single(r.Diagnostics));
        var hull = Assert.Single(r.Asset!.Meshes);
        Assert.Equal("Hull", hull.Node);
        Assert.Equal(checks.GetProperty("triangles").GetInt32(), hull.Triangles.Length / 3);
        Assert.Equal(V(checks.GetProperty("bounds_min")).V, hull.Bounds.Min);
        Assert.Equal(V(checks.GetProperty("bounds_max")).V, hull.Bounds.Max);

        var (markers, collision, frame) = MarkerFile.Read(File.ReadAllText(Path.Combine(dir, "markers.json")), "fixture-markers");
        Assert.Equal(checks.GetProperty("collision").GetString(), collision);
        Assert.Equal(checks.GetProperty("frame").GetString(), frame);
        foreach (var m in checks.GetProperty("markers").EnumerateArray())
        {
            var got = markers.Single(x => x.Id == m.GetProperty("id").GetString());
            Assert.Equal(V(m.GetProperty("position")).V, got.Position);
            Assert.Equal(V(m.GetProperty("up")).V, got.Up);
            Assert.Equal(V(m.GetProperty("outward")).V, got.Outward);
        }
    }

    private readonly record struct V3(float X, float Y, float Z)
    {
        public Vector3 V => new(X, Y, Z);
    }

    [Fact]
    public void FacesFanAndBadReferencesAreRefusedWithTheirLine()
    {
        var quad = Path.Combine(dir, "quad.obj");
        File.WriteAllText(quad, "o Quad\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nf 1/1 2/2 3/3 4/4\n");
        var r = ObjImporter.Import(quad, ProjectFrame.Gltf);
        Assert.Equal(ImportOutcome.Imports, r.Outcome);
        Assert.Equal(2, r.Asset!.Meshes[0].Triangles.Length / 3);

        var bad = Path.Combine(dir, "bad.obj");
        File.WriteAllText(bad, "v 0 0 0\nv 1 0 0\nf 1 2 9\n");
        Assert.Equal(["error: bad.obj: line 3: vertex 9 doesn't exist (there are 2)"], ObjImporter.Import(bad, ProjectFrame.Gltf).Diagnostics);
        File.WriteAllText(bad, "v 0 zero 0\n");
        Assert.Contains("line 1", Assert.Single(ObjImporter.Import(bad, ProjectFrame.Gltf).Diagnostics));
    }

    [Fact]
    public void AMirroringFrameFlipsTheWindingOnce()
    {
        var tri = Path.Combine(dir, "tri.obj");
        File.WriteAllText(tri, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        var mesh = ObjImporter.Import(tri, ProjectFrame.YUpLeftHanded).Asset!.Meshes[0];
        Assert.True(mesh.WindingFlipped);
        Assert.Equal([0, 2, 1], mesh.Triangles);
        Assert.Equal(new Vector3(-1, 0, 0), mesh.Positions[1]);
    }

    [Fact]
    public void MarkerFilesAreStrict()
    {
        Assert.Equal("$.kind", Assert.Throws<DocumentFormatException>(() => MarkerFile.Read("""{"kind":"other","version":1,"markers":[]}""", "markers")).Path);
        Assert.Equal("$.markers", Assert.Throws<DocumentFormatException>(() => MarkerFile.Read(
            """{"kind":"markers","version":1,"markers":[{"id":"a","position":[0,0,0],"up":[0,0,1],"outward":[1,0,0]},{"id":"a","position":[0,0,0],"up":[0,0,1],"outward":[1,0,0]}]}""", "markers")).Path);
        Assert.Equal("$.markers[0].up", Assert.Throws<DocumentFormatException>(() => MarkerFile.Read(
            """{"kind":"markers","version":1,"markers":[{"id":"a","position":[0,0,0],"up":[0,1],"outward":[1,0,0]}]}""", "markers")).Path);
    }
}
