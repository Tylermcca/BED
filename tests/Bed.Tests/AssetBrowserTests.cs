#pragma warning disable CS0618 // AssetRecord.Create is obsolete for imports; these cases test the low-level record writer (and old, undeclared records) on purpose.
using System.Text.Json;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

/// <summary>The asset browser model (AT-025): listing, search, problem badges and files not yet imported.</summary>
public sealed class AssetBrowserTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("bed-browser-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Import(string rel, string licence = "self-authored", AiUse ai = AiUse.Allowed)
    {
        var path = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, rel);
        return AssetRecord.Create(path, "gltf", 1, JsonDocument.Parse("{}").RootElement, new("tester", licence, "test"), ai).Guid;
    }

    [Fact]
    public void ListsAssetsByPathWithTheirRecords()
    {
        Import("props/lamp.glb");
        Import("props/chair.glb", "CC0-1.0");
        var b = new AssetBrowser(dir, ".glb");
        Assert.Equal([Path.Combine("props", "chair.glb"), Path.Combine("props", "lamp.glb")], b.Entries.Select(e => e.RelativePath));
        Assert.Equal("CC0-1.0", b.Entries[0].Licence);
        Assert.All(b.Entries, e => Assert.False(e.HasProblems));
    }

    [Fact]
    public void SearchFindsByNameIdImporterAndLicence()
    {
        var lamp = Import("props/lamp.glb");
        Import("walls/arch.glb", "CC-BY-4.0");
        var b = new AssetBrowser(dir);
        Assert.Equal(["lamp.glb"], b.Search("LAMP").Select(e => e.Name));
        Assert.Equal(["lamp.glb"], b.Search(lamp[..6]).Select(e => e.Name));
        Assert.Equal(["arch.glb"], b.Search("cc-by").Select(e => e.Name));
        Assert.Equal(2, b.Search("gltf").Count);
        Assert.Equal(2, b.Search("").Count);
        Assert.Empty(b.Search("teapot"));
    }

    [Fact]
    public void ProblemsShowAsBadgesAndCanBeFiltered()
    {
        Import("ok.glb");
        Import("changed.glb");
        File.WriteAllText(Path.Combine(dir, "changed.glb"), "edited since import");
        Import("unlicensed.glb", licence: " ");
        var b = new AssetBrowser(dir);
        var problems = b.Search("", problemsOnly: true).Select(e => e.Name).Order().ToList();
        Assert.Equal(["changed.glb", "unlicensed.glb"], problems);
        Assert.Contains(b.Entries.Single(e => e.Name == "changed.glb").Issues, i => i.Message.Contains("changed since it was imported"));
    }

    [Fact]
    public void FilesNotYetImportedAreListedSeparatelyAndRefreshSeesChanges()
    {
        Import("lamp.glb");
        File.WriteAllText(Path.Combine(dir, "new.glb"), "not imported");
        var b = new AssetBrowser(dir, ".glb");
        Assert.Equal(["new.glb"], b.NotImported);
        Assert.Single(b.Entries);

        AssetRecord.Create(Path.Combine(dir, "new.glb"), "gltf", 1, JsonDocument.Parse("{}").RootElement, new("t", "self-authored", "t"), AiUse.Unknown);
        b.Refresh();
        Assert.Empty(b.NotImported);
        Assert.Equal(2, b.Entries.Count);
    }

    [Fact]
    public void AnUnreadableSidecarIsAGeneralProblem()
    {
        Import("lamp.glb");
        File.WriteAllText(Path.Combine(dir, "broken.glb"), "x");
        File.WriteAllText(Path.Combine(dir, "broken.glb.asset.json"), "{ not json");
        var b = new AssetBrowser(dir);
        Assert.Single(b.Entries);
        Assert.Contains(b.General, i => i.Message.Contains("can't read"));
    }

    [Fact]
    public void AnIdUsedTwiceShowsInGeneralAndOnBothBadges()
    {
        var guid = Import("a.glb");
        Directory.CreateDirectory(Path.Combine(dir, "copy"));
        File.WriteAllText(Path.Combine(dir, "copy", "a.glb"), "a.glb");
        File.Copy(Path.Combine(dir, "a.glb.asset.json"), Path.Combine(dir, "copy", "a.glb.asset.json"));
        var b = new AssetBrowser(dir);
        Assert.Contains(b.General, i => i.ObjectId == guid && i.Message.Contains("is used by"));
        Assert.All(b.Entries, e => Assert.Contains(e.Issues, i => i.Message.Contains("is used by")));
    }
}
