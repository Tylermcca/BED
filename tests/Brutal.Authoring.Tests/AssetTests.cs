#pragma warning disable CS0618 // AssetRecord.Create is obsolete for imports; these cases test the low-level record writer (and old, undeclared records) on purpose.
using System.Text.Json;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Asset identity (FUT-009, AT-024, FUT-005): a GUID from the first import, kept through moves, never re-bound by path.</summary>
public sealed class AssetTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("brutal-authoring-assets-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static readonly JsonElement NoSettings = JsonDocument.Parse("{}").RootElement;
    private static readonly Provenance Mine = new("tester", "self-authored", "made by the test");

    private string Asset(string name, string content = "mesh")
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void FirstImportAssignsAGuidOnceAndWritesASidecar()
    {
        var path = Asset("cube.glb");
        var record = AssetRecord.Create(path, "gltf", 1, NoSettings, Mine, AiUse.Allowed);
        Assert.Matches("^[0-9a-f]{32}$", record.Guid);
        Assert.True(File.Exists(path + ".asset.json"));
        Assert.Throws<InvalidOperationException>(() => AssetRecord.Create(path, "gltf", 1, NoSettings, Mine, AiUse.Allowed));
        Assert.Equal(record.Guid, AssetRecord.Read(File.ReadAllText(path + ".asset.json")).Guid);
    }

    [Fact]
    public void TheSidecarRoundTripsCanonically()
    {
        var path = Asset("cube.glb");
        var settings = JsonDocument.Parse("""{"scale": 1.5, "flip": true}""").RootElement;
        var record = AssetRecord.Create(path, "gltf", 2, settings, Mine with { Notes = "converted from cube.blend" }, AiUse.Forbidden);
        var text = File.ReadAllText(path + ".asset.json");
        Assert.Equal(text, AssetRecord.Read(text).Write());
        Assert.EndsWith("}\n", text);
        Assert.Contains("\"aiUse\": \"forbidden\"", text);
    }

    [Fact]
    public void AMoveKeepsTheIdAndReimportKeepsItToo()
    {
        var path = Asset("props/lamp.glb");
        var guid = AssetRecord.Create(path, "gltf", 1, NoSettings, Mine, AiUse.Unknown).Guid;
        var moved = Path.Combine(dir, "lighting", "lamp.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(path, moved);
        File.Move(path + ".asset.json", moved + ".asset.json");
        var index = AssetIndex.Scan(dir, ".glb");
        Assert.Equal(moved, index.Resolve(guid));
        Assert.Empty(index.Issues);

        File.WriteAllText(moved, "a better lamp");
        Assert.Contains(AssetIndex.Scan(dir).Issues, i => i.Message.Contains("changed since it was imported"));
        var again = AssetIndex.Scan(dir).Record(guid)!.Reimport(moved, 2);
        Assert.Equal(guid, again.Guid);
        Assert.Empty(AssetIndex.Scan(dir).Issues);
    }

    [Fact]
    public void ADuplicatedIdIsAnErrorNamingBothAndResolvesNeither()
    {
        var a = Asset("a.glb");
        var guid = AssetRecord.Create(a, "gltf", 1, NoSettings, Mine, AiUse.Allowed).Guid;
        var b = Asset("copy/a.glb");
        File.Copy(a + ".asset.json", b + ".asset.json");   // someone copied the file and its sidecar
        var index = AssetIndex.Scan(dir);
        var e = Assert.Single(index.Issues, i => i.Severity == Severity.Error);
        Assert.Contains("a.glb.asset.json", e.Message);
        Assert.Contains(Path.Combine("copy", "a.glb.asset.json"), e.Message);
        Assert.Null(index.Resolve(guid));
    }

    [Fact]
    public void AMissingSourceIsAnErrorAndNothingIsReboundByPath()
    {
        var a = Asset("a.glb");
        var guid = AssetRecord.Create(a, "gltf", 1, NoSettings, Mine, AiUse.Allowed).Guid;
        File.Delete(a);
        Asset("a_copy.glb");   // a lookalike nearby must not inherit the id
        var index = AssetIndex.Scan(dir, ".glb");
        Assert.Null(index.Resolve(guid));
        Assert.Contains(index.Issues, i => i.Severity == Severity.Error && i.ObjectId == guid);
        Assert.Contains(index.Issues, i => i.Message.Contains("a_copy.glb has no .asset.json yet"));
    }

    [Fact]
    public void NoLicenceIsAWarning()
    {
        AssetRecord.Create(Asset("a.glb"), "gltf", 1, NoSettings, new Provenance("someone", " ", "somewhere"), AiUse.Unknown);
        Assert.Contains(AssetIndex.Scan(dir).Issues, i => i.Severity == Severity.Warning && i.Message.Contains("no licence"));
    }

    [Theory]
    [InlineData(AiUse.Allowed, true)]
    [InlineData(AiUse.Forbidden, false)]
    [InlineData(AiUse.Unknown, false)]
    public void OnlyExplicitlyAllowedAssetsMayGoToAI(AiUse use, bool may) =>
        Assert.Equal(may, new AssetRecord(new string('a', 32), "x", "", "gltf", 1, NoSettings, Mine, use).MayUseWithAi);

    [Theory]
    [InlineData("""{"kind":"asset","version":1,"guid":"XYZ","source":"a","sourceHash":"","importer":{"id":"g","version":1},"settings":{},"provenance":{"author":"a","licence":"MIT","origin":"o"},"aiUse":"allowed"}""", "$.guid")]
    [InlineData("""{"kind":"asset","version":1,"guid":"0123456789abcdef0123456789abcdef","source":"a","sourceHash":"","importer":{"id":"g","version":1},"settings":{},"provenance":{"author":"a","licence":"MIT","origin":"o"},"aiUse":"maybe"}""", "$.aiUse")]
    [InlineData("""{"kind":"asset","version":2,"guid":"0123456789abcdef0123456789abcdef"}""", "$.version")]
    [InlineData("""{"kind":"asset","version":1,"guid":"0123456789abcdef0123456789abcdef","source":"a","sourceHash":"","importer":{"id":"g","version":1,"extra":1},"settings":{},"provenance":{"author":"a","licence":"MIT","origin":"o"},"aiUse":"allowed"}""", "$.importer.extra")]
    public void BadSidecarsAreRefusedWithTheirPath(string text, string path) =>
        Assert.Equal(path, Assert.Throws<DocumentFormatException>(() => AssetRecord.Read(text)).Path);
}
