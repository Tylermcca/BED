using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The general scene document against Codex's A005 fixture scenes: IM-08 round trip and refusals, IM-07 missing
/// assets kept as placeholders, IM-10 duplicating a group with its internal references remapped.</summary>
public sealed class SceneTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("brutal-authoring-scene-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Brutal.Authoring.slnx"))) return d.FullName;
        throw new InvalidOperationException("can't find the repo root");
    }

    private static string Fixtures => Path.Combine(Root(), "fixtures", "import");

    // Fixture scenes are made at test time by Codex's generator (fixtures/import/make_fixtures.py).
    private bool Generate(string function)
    {
        var script = "import importlib.util,sys,os\n" +
            "spec=importlib.util.spec_from_file_location('mf', sys.argv[1]); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)\n" +
            "for k,v in getattr(m, sys.argv[3])().items(): open(os.path.join(sys.argv[2],k),'wb').write(v)\n";
        var psi = new ProcessStartInfo("python", ["-c", script, Path.Combine(Fixtures, "make_fixtures.py"), dir, function])
        { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit(60_000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static readonly SceneFormat Fixture = new("fixture-scene");

    private string F(string name) => File.ReadAllText(Path.Combine(dir, name));

    [Fact]
    public void Im08TheSceneRoundTripsExactly()
    {
        Assert.True(Generate("roundtrip_documents"), "making the fixture scenes needs python (fixtures/import/make_fixtures.py)");
        var scene = Fixture.Read(F("roundtrip.scene.json"));
        Assert.Equal(["first", "second"], scene.Objects.Select(o => o.Id));
        var first = scene.Objects[0];
        Assert.Equal(new Vector3(0.125f, 0.375f, 0.625f), first.Position);
        Assert.Equal(new Vector3(1, 2, 1), first.Scale);
        Assert.Equal("café", first.Properties["label"].GetString());
        Assert.True(first.Properties["enabled"].GetBoolean());
        Assert.Equal("second", first.References["peer"]);
        Assert.Empty(new SceneValidator().Validate(scene));

        var once = Fixture.Write(scene);
        Assert.Equal(once, Fixture.Write(Fixture.Read(once)));   // save, load, save: identical
        Assert.Contains("café", once);
    }

    [Theory]
    [InlineData("unknown_property.scene.json", "$.unexpected")]
    [InlineData("duplicate_property.scene.json", "$.version")]
    [InlineData("future_version.scene.json", "$.version")]
    public void Im08BadScenesAreRefusedWithTheirPath(string file, string path)
    {
        Assert.True(Generate("roundtrip_documents"), "making the fixture scenes needs python (fixtures/import/make_fixtures.py)");
        Assert.Equal(path, Assert.Throws<DocumentFormatException>(() => Fixture.Read(F(file))).Path);
    }

    [Fact]
    public void Im08DuplicateIdsFailValidationNamingTheId()
    {
        Assert.True(Generate("roundtrip_documents"), "making the fixture scenes needs python (fixtures/import/make_fixtures.py)");
        var issues = new SceneValidator().Validate(Fixture.Read(F("duplicate_ids.scene.json")));
        Assert.Contains(issues, i => i.ObjectId == "first" && i.Message.Contains("object id 'first' is duplicated"));
    }

    [Fact]
    public void Im07AMissingAssetIsReportedAndTheObjectAndPathSurvive()
    {
        Assert.True(Generate("missing_references"), "making the fixture scenes needs python (fixtures/import/make_fixtures.py)");
        var scene = Fixture.Read(F("missing.scene.json"));
        var issues = SceneAssets.Check(scene, a => File.Exists(Path.Combine(Fixtures, a)));
        Assert.Equal(["error: object 'placeholder': asset 'missing_asset.glb' not found; retain placeholder"],
            issues.Select(i => $"{i.Severity.ToString().ToLowerInvariant()}: {i.Message}"));
        Assert.Equal(["healthy", "placeholder", "textured"], scene.Objects.Select(o => o.Id));
        Assert.Contains("\"missing_asset.glb\"", Fixture.Write(scene));   // the reference is kept on save
    }

    [Fact]
    public void Im10DuplicatingAGroupRemapsInsideAndKeepsOutside()
    {
        Assert.True(Generate("clone_identity"), "making the fixture scenes needs python (fixtures/import/make_fixtures.py)");
        var group = new SceneFormat("fixture-group");
        var source = group.Read(F("source.group.json"));
        var expected = group.Read(F("expected_clone.group.json"));
        var withOutside = new Scene(source.Objects.Add(SceneObject.At("outside", null, Vector3.Zero)));   // the external target
        var session = new DocumentSession<Scene>(group, new SceneValidator(), SceneKit.Catalog(), withOutside);
        Assert.True(session.Execute(new DuplicateGroup(["root", "child"])), string.Join("; ", session.LastIssues));
        foreach (var e in expected.Objects)
        {
            var got = session.Current.Find(e.Id);
            Assert.NotNull(got);
            Assert.Equal(e.Parent, got!.Parent);
            Assert.Equal(e.References, got.References);
        }
        // Replay: the ids chosen were recorded, so the same copies come back.
        var replay = DocumentSession<Scene>.Replay(withOutside, JournalFile.Read(JournalFile.Write(session.Journal)), group, new SceneValidator(), SceneKit.Catalog());
        Assert.Equal(group.Write(session.Current), group.Write(replay.Current));
    }

    [Fact]
    public void RemovingAReferencedObjectIsRefusedNamingWhoRefersToIt()
    {
        var a = SceneObject.At("lamp", null, Vector3.Zero);
        var b = SceneObject.At("table", null, Vector3.One) with { References = ImmutableSortedDictionary<string, string>.Empty.Add("lit_by", "lamp") };
        var s = new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), new Scene([a, b]));
        Assert.False(s.Execute(new RemoveObject("lamp")));
        Assert.Contains("'table' refers to it", s.LastIssues[0].Message);
        Assert.True(s.Execute(new RemoveObject("table")));
        Assert.True(s.Execute(new RemoveObject("lamp")));
    }

    [Fact]
    public void PropertiesAcceptOnlyPlainValuesAndCanBeRemoved()
    {
        var s = new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(), new Scene([SceneObject.At("a", null, Vector3.Zero)]));
        Assert.True(s.Execute(new SetSceneProperty("a", "hp", JsonDocument.Parse("12").RootElement)));
        Assert.Equal(12, s.Current.Find("a")!.Properties["hp"].GetInt32());
        Assert.False(s.Execute(new SetSceneProperty("a", "bag", JsonDocument.Parse("[1,2]").RootElement)));
        Assert.True(s.Execute(new SetSceneProperty("a", "hp", null)));
        Assert.Empty(s.Current.Find("a")!.Properties);
    }

    [Fact]
    public void AParentCycleIsAnError()
    {
        var a = SceneObject.At("a", null, Vector3.Zero) with { Parent = "b" };
        var b = SceneObject.At("b", null, Vector3.Zero) with { Parent = "a" };
        Assert.Contains(new SceneValidator().Validate(new Scene([a, b])), i => i.Message.Contains("circle"));
    }
}
