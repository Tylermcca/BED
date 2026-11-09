#pragma warning disable CS0618 // AssetRecord.Create is obsolete for imports; these cases test the low-level record writer (and old, undeclared records) on purpose.
using System.Text.Json;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Variants and states of an asset (AT-114).</summary>
public sealed class VariantTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("brutal-authoring-variants-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Import(string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, name);
        return AssetRecord.Create(path, "gltf", 1, JsonDocument.Parse("{}").RootElement, new("tester", "self-authored", "test"), AiUse.Allowed).Guid;
    }

    [Fact]
    public void AGoodSetHasNoIssuesAndShowsWhatWasChosen()
    {
        var (intact, damaged) = (Import("crate.glb"), Import("crate_damaged.glb"));
        var set = new VariantSet("crate", "intact", [("intact", intact), ("damaged", damaged)]);
        var index = AssetIndex.Scan(dir);
        Assert.Empty(set.Validate(index));
        Assert.Equal(damaged, set.AssetFor("damaged", index, out var fell));
        Assert.False(fell);
        Assert.Equal(["intact", "damaged"], set.Names);
    }

    [Fact]
    public void AMissingVariantWarnsAndTheDefaultStandsIn()
    {
        var intact = Import("crate.glb");
        var set = new VariantSet("crate", "intact", [("intact", intact), ("destroyed", "ffffffffffffffffffffffffffffffff")]);
        var index = AssetIndex.Scan(dir);
        var w = Assert.Single(set.Validate(index));
        Assert.Equal(Severity.Warning, w.Severity);
        Assert.Contains("'destroyed'", w.Message);
        Assert.Equal(intact, set.AssetFor("destroyed", index, out var fell));
        Assert.True(fell);
        Assert.Equal(intact, set.AssetFor("no such variant", index, out _));
    }

    [Fact]
    public void BadSetsAreErrors()
    {
        Assert.Contains("no variants", Assert.Single(new VariantSet("x", "a", []).Validate()).Message);
        var twice = new VariantSet("x", "a", [("a", "1"), ("a", "2")]);
        Assert.Contains(twice.Validate(), i => i.Message.Contains("twice"));
        var noDefault = new VariantSet("x", "gold", [("red", "1"), ("blue", "2")]);
        Assert.Contains(noDefault.Validate(), i => i.Message.Contains("defaults to 'gold'"));
    }
}
