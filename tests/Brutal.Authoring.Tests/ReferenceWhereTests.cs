using System.Text.Json;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A050's additions to the reference map: where-conditions on a target, and following a key or a value where
/// both are links. Shapes like a real game's open places, with invented content.</summary>
public sealed class ReferenceWhereTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "refwhere-" + Guid.NewGuid().ToString("N"));
    private readonly DataProject project;

    public ReferenceWhereTests()
    {
        var data = Path.Combine(root, "data");
        var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(schemas);
        foreach (var n in new[] { "world", "factions", "ships", "looks", "facts" }) File.WriteAllText(Path.Combine(schemas, n + ".schema.json"), "{}");
        File.WriteAllText(Path.Combine(data, "world.json"), """{"sectors":[{"id":"home","owner":"red"},{"id":"home","owner":"blue"},{"id":"edge","owner":"red"}]}""");
        File.WriteAllText(Path.Combine(data, "factions.json"), """{"factions":{"red":{"homeworld":"home"},"blue":{"homeworld":"home"},"green":{"homeworld":"edge"},"grey":{}}}""");
        File.WriteAllText(Path.Combine(data, "ships.json"), """{"templates":[{"slots":[{"id":"spine"},{"id":"wing","side_of":"spine"},{"id":"pod","side_of":"wing"}]}]}""");
        File.WriteAllText(Path.Combine(data, "looks.json"), """
        {"kits":[{"kit":"k1","gene":"eyes","choices":[{"id":"round"},{"id":"narrow"}]},{"kit":"k1","gene":"hair","choices":[{"id":"round"}]},{"kit":"k2","gene":"eyes","choices":[{"id":"round"}]}],
         "people":[{"kit":"k1","gene":"eyes","pick":"round"},{"kit":"k2","gene":"eyes","pick":"narrow"},{"kit":"k1","pick":"round"}]}
        """);
        File.WriteAllText(Path.Combine(data, "facts.json"), """{"facts":{"war":{"values":["on","off"]}},"sets":{"war":"on"},"counts":[1,"1"]}""");
        project = DataProject.Open(data, schemas);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private const string Map = """
    {"game":"Test","labels":[],"references":[
      {"file":"factions.json","at":"/factions/*/homeworld","target":{"file":"world.json","list":"/sectors","key":"id",
        "where":[{"at":"0/owner","equalsSource":"1#"}]},"evidence":"a homeworld is a sector its faction owns"},
      {"file":"ships.json","at":"/templates/*/slots/*/side_of","target":{"file":"ships.json","list":"/templates/*/slots","key":"id",
        "where":[{"absent":"side_of"}]},"evidence":"side_of names a spine slot"},
      {"file":"looks.json","at":"/people/*/pick","target":{"file":"looks.json","list":"/kits/*/choices","key":"id",
        "where":[{"at":"2/kit","equalsSource":"1/kit"},{"at":"2/gene","equalsSource":"1/gene"}]},"evidence":"two joins: kit and gene"},
      {"file":"facts.json","at":"/sets","keys":true,"target":{"file":"facts.json","list":"/facts","key":"$key"},"evidence":"the key names a fact"},
      {"file":"facts.json","at":"/sets/*","target":{"file":"facts.json","list":"/facts/*/values","key":"$value"},"evidence":"the value is one of its values"}
    ]}
    """;

    private ReferenceResult Follow(string file, string at, ReferenceIntent? intent = null)
    {
        var f = project.Find(file)!;
        Assert.True(f.Open());
        var value = f.Session!.Current.At(at) ?? default;
        var map = ReferenceMap.Read(Map);
        return intent is { } i ? map.Resolve(project, file, at, value, i) : map.Resolve(project, file, at, value);
    }

    [Fact]
    public void A_condition_on_the_target_picks_the_one_its_source_owns()
    {
        Assert.Equal("/sectors/0", Follow("factions.json", "/factions/red/homeworld").Target!.Pointer);    // two "home"s; red owns one
        Assert.Equal("/sectors/1", Follow("factions.json", "/factions/blue/homeworld").Target!.Pointer);
        Assert.Equal(ReferenceOutcome.Missing, Follow("factions.json", "/factions/green/homeworld").Outcome);   // edge is red's
    }

    [Fact]
    public void An_absent_condition_keeps_only_candidates_without_the_field()
    {
        Assert.Equal("/templates/0/slots/0", Follow("ships.json", "/templates/0/slots/1/side_of").Target!.Pointer);   // spine has no side_of
        Assert.Equal(ReferenceOutcome.Missing, Follow("ships.json", "/templates/0/slots/2/side_of").Outcome);         // wing is a side slot
    }

    [Fact]
    public void Two_conditions_make_two_joins()
    {
        Assert.Equal("/kits/0/choices/0", Follow("looks.json", "/people/0/pick").Target!.Pointer);   // k1 eyes round, not k1 hair or k2 eyes
        Assert.Equal(ReferenceOutcome.Missing, Follow("looks.json", "/people/1/pick").Outcome);       // k2 eyes has no narrow
        var noGene = Follow("looks.json", "/people/2/pick");                                          // the source has no gene
        Assert.Equal(ReferenceOutcome.NeedsScope, noGene.Outcome);
        Assert.Contains("1/gene", noGene.Note);
    }

    [Fact]
    public void Key_and_value_at_one_place_are_followed_as_asked()
    {
        Assert.Equal("/facts/war", Follow("facts.json", "/sets/war", ReferenceIntent.Key).Target!.Pointer);
        Assert.Equal("/facts/war/values/0", Follow("facts.json", "/sets/war", ReferenceIntent.Value).Target!.Pointer);
        Assert.Equal(ReferenceOutcome.NotAReference, Follow("facts.json", "/counts/0", ReferenceIntent.Key).Outcome);
    }

    [Fact]
    public void Where_clauses_must_take_one_of_the_two_forms()
    {
        var bad = Map.Replace("""{"absent":"side_of"}""", """{"absent":"side_of","at":"0/x"}""");
        Assert.Throws<InvalidDocumentException>(() => ReferenceMap.Read(bad));
        var badPointer = Map.Replace("\"1#\"", "\"01#\"");
        Assert.Throws<InvalidDocumentException>(() => ReferenceMap.Read(badPointer));
    }
}
