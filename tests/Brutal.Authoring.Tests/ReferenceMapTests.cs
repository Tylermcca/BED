using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A game's reference map: labels and cross-file ids, followed through a project, never guessed.</summary>
public sealed class ReferenceMapTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "refmap-" + Guid.NewGuid().ToString("N"));
    private readonly DataProject project;

    private const string Map = """
    {
      "game": "Toy",
      "labels": [ { "file": "factories.json", "list": "/factories", "field": "title" } ],
      "references": [
        { "file": "factories.json", "at": "/factories/*/output", "target": { "file": "wares.json", "list": "/wares", "key": "id" }, "sentinels": ["none"], "evidence": "toy" },
        { "file": "factories.json", "at": "/factories/*/inputs", "target": { "file": "wares.json", "list": "/wares", "key": "id" }, "evidence": "toy" },
        { "file": "factories.json", "at": "/factories/*/owner", "target": { "file": "factions.json", "list": "/factions", "key": "$key" }, "evidence": "toy" },
        { "file": "factories.json", "at": "/factories/*/mood", "target": { "file": "factions.json", "list": "/moods", "key": "$value" }, "evidence": "toy" },
        { "file": "factories.json", "at": "/standing", "keys": true, "target": { "file": "factions.json", "list": "/factions", "key": "$key" }, "evidence": "toy" },
        { "file": "factories.json", "at": "/factories/*/either", "target": { "file": "wares.json", "list": "/wares", "key": "id" },
          "also": [ { "file": "wares.json", "list": "/families", "key": "id" } ], "evidence": "toy" },
        { "file": "factories.json", "at": "/factories/*/dock", "target": { "file": "factions.json", "list": "/factions/*/docks", "key": "id" }, "evidence": "toy" },
        { "file": "factories.json", "at": "/factories/*/local", "target": { "file": "wares.json", "list": "/wares", "key": "id" }, "scope": "the same sector", "evidence": "toy" },
        { "file": "factories.json", "at": "/factories/*/elsewhere", "target": { "file": "absent.json", "list": "/x", "key": "id" }, "evidence": "toy" }
      ]
    }
    """;

    public ReferenceMapTests()
    {
        var data = Path.Combine(root, "data");
        var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(schemas);
        foreach (var name in new[] { "wares", "factories", "factions" }) File.WriteAllText(Path.Combine(schemas, name + ".schema.json"), "{}");
        File.WriteAllText(Path.Combine(data, "wares.json"), """{"wares":[{"id":"ore"},{"id":"steel"},{"id":"food"}],"families":[{"id":"food"},{"id":"bio"}]}""");
        File.WriteAllText(Path.Combine(data, "factions.json"), """{"factions":{"meridian":{"docks":[{"id":"d1"}]},"terran":{"docks":[{"id":"d2"}]}},"moods":["calm","angry"]}""");
        File.WriteAllText(Path.Combine(data, "factories.json"), """
        {"factories":[{"title":"Smelter","output":"steel","inputs":["ore","coal"],"owner":"meridian","mood":"calm","either":"food","dock":"d2","local":"ore","elsewhere":"x"},
                      {"title":"Idle","output":"none","either":"bio"}],
         "standing":{"terran":3,"vandals":1}}
        """);
        project = DataProject.Open(data, schemas);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static JsonElement S(string text) => JsonSerializer.SerializeToElement(text);

    private ReferenceResult Follow(string pointer) =>
        ReferenceMap.Read(Map).Resolve(project, "factories.json", pointer, project.Find("factories.json")!.Open()
            ? project.Find("factories.json")!.Session!.Current.At(pointer) ?? default : default);

    [Fact]
    public void A_field_id_is_found_in_another_file()
    {
        var r = Follow("/factories/0/output");
        Assert.Equal(ReferenceOutcome.Found, r.Outcome);
        Assert.Equal(new ReferenceTarget("wares.json", "/wares/1"), r.Target);
    }

    [Fact]
    public void Each_item_of_a_list_of_ids_is_followed_and_a_missing_one_says_so()
    {
        Assert.Equal(new ReferenceTarget("wares.json", "/wares/0"), Follow("/factories/0/inputs/0").Target);
        var missing = Follow("/factories/0/inputs/1");
        Assert.Equal(ReferenceOutcome.Missing, missing.Outcome);
        Assert.Contains("coal", missing.Note);
    }

    [Fact]
    public void Keys_values_and_object_keys_are_ids_too()
    {
        Assert.Equal(new ReferenceTarget("factions.json", "/factions/meridian"), Follow("/factories/0/owner").Target);
        Assert.Equal(new ReferenceTarget("factions.json", "/moods/0"), Follow("/factories/0/mood").Target);
        Assert.Equal(new ReferenceTarget("factions.json", "/factions/terran"), Follow("/standing/terran").Target);
        Assert.Equal(ReferenceOutcome.Missing, Follow("/standing/vandals").Outcome);
    }

    [Fact]
    public void An_id_in_two_targets_is_ambiguous_and_one_in_either_is_found()
    {
        Assert.Equal(ReferenceOutcome.Ambiguous, Follow("/factories/0/either").Outcome);   // food is a ware and a family
        Assert.Equal(new ReferenceTarget("wares.json", "/families/1"), Follow("/factories/1/either").Target);
    }

    [Fact]
    public void A_target_list_with_a_wildcard_searches_every_match()
    {
        Assert.Equal(new ReferenceTarget("factions.json", "/factions/terran/docks/0"), Follow("/factories/0/dock").Target);
    }

    [Fact]
    public void Sentinels_scopes_unlisted_places_and_absent_files_are_never_guessed()
    {
        Assert.Equal(ReferenceOutcome.Sentinel, Follow("/factories/1/output").Outcome);
        Assert.Equal(ReferenceOutcome.NeedsScope, Follow("/factories/0/local").Outcome);
        Assert.Equal(ReferenceOutcome.NotAReference, Follow("/factories/0/title").Outcome);
        Assert.Equal(ReferenceOutcome.NotLoaded, Follow("/factories/0/elsewhere").Outcome);
        Assert.Equal(ReferenceOutcome.NotAReference, ReferenceMap.Read(Map).Resolve(project, "wares.json", "/wares/0/id", S("ore")).Outcome);
    }

    [Fact]
    public void Labels_come_from_the_named_field()
    {
        var map = ReferenceMap.Read(Map);
        Assert.Equal("title", map.LabelField("factories.json", "/factories/1"));
        Assert.Null(map.LabelField("factories.json", "/standing"));
        Assert.Null(map.LabelField("wares.json", "/wares/0"));
        Assert.Equal(9, map.Count);
        Assert.Equal("Toy", map.Game);
    }

    [Fact]
    public void A_map_that_doesnt_fit_the_format_is_refused_with_every_problem()
    {
        var e = Assert.Throws<InvalidDocumentException>(() => ReferenceMap.Read("""{"game":"","labels":[{"file":"a/b.json","list":"x","field":"f"}]}"""));
        Assert.Contains(e.Issues, i => i.Message.Contains("'references' is required"));
        Assert.Contains(e.Issues, i => i.Path == "$.labels[0].file");
        Assert.Contains(e.Issues, i => i.Path == "$.labels[0].list");
        Assert.ThrowsAny<JsonException>(() => ReferenceMap.Read("{ nope"));
    }

    // ---- the three general features A040's open places asked for ----

    private DataProject World()
    {
        var data = Path.Combine(root, "world");
        var schemas = Path.Combine(root, "world-schemas");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(schemas);
        foreach (var name in new[] { "universe", "people", "kits" }) File.WriteAllText(Path.Combine(schemas, name + ".schema.json"), "{}");
        File.WriteAllText(Path.Combine(data, "universe.json"), """
        {"sectors":[{"id":"north","stations":[{"id":"dock"},{"id":"yard"}]},{"id":"south","stations":[{"id":"dock"}]}],
         "factions":[{"id":"red"},{"id":"blue"}]}
        """);
        File.WriteAllText(Path.Combine(data, "kits.json"), """{"genes":{"eyes":["round","narrow"],"hair":["short"]}}""");
        File.WriteAllText(Path.Combine(data, "people.json"), """
        {"people":[{"of":"ore","count":3,"sector":"south","station":"dock","pair":"red/blue","look":"eyes:narrow"},
                   {"of":"salt","sector":"west","station":"dock","pair":"red/green","look":"eyes"},
                   {"note":"nothing to name it"}],
         "homes":{"north":{"base":"yard"}}}
        """);
        return DataProject.Open(data, schemas);
    }

    private const string WorldMap = """
    {"game":"World",
     "labels":[{"file":"people.json","list":"/people","template":"{of} x{count}"}],
     "references":[
       {"file":"people.json","at":"/people/*/station","target":{"file":"universe.json","list":"/sectors/*/stations","key":"id"},
        "scope":"the sector named by sibling sector","narrow":{"by":"1/sector","file":"universe.json","list":"/sectors","key":"id"},"evidence":"test"},
       {"file":"people.json","at":"/homes/*/base","target":{"file":"universe.json","list":"/sectors/*/stations","key":"id"},
        "narrow":{"by":"1#","file":"universe.json","list":"/sectors","key":"id"},"evidence":"test"},
       {"file":"people.json","at":"/people/*/pair","target":{"file":"universe.json","list":"/factions","key":"id"},
        "parts":{"separator":"/","targets":[{"file":"universe.json","list":"/factions","key":"id"},{"file":"universe.json","list":"/factions","key":"id"}]},"evidence":"test"},
       {"file":"people.json","at":"/people/*/look","target":{"file":"kits.json","list":"/genes/*","key":"$value"},
        "parts":{"separator":":","targets":[{"file":"kits.json","list":"/genes","key":"$key"},{"file":"kits.json","list":"/genes/*","key":"$value"}]},"evidence":"test"}
     ]}
    """;

    private static ReferenceResult FollowIn(DataProject p, string pointer) =>
        ReferenceMap.Read(WorldMap).Resolve(p, "people.json", pointer, p.Find("people.json")!.Open() ? p.Find("people.json")!.Session!.Current.At(pointer)!.Value : default);

    [Fact]
    public void A_template_label_joins_several_fields_and_gives_way_when_none_are_there()
    {
        var p = World();
        Assert.True(p.Find("people.json")!.Open());
        var doc = p.Find("people.json")!.Session!.Current;
        var map = ReferenceMap.Read(WorldMap);
        Assert.Equal("ore x3", map.Label("people.json", "/people/0", doc.At("/people/0")!.Value));
        Assert.Equal("salt x", map.Label("people.json", "/people/1", doc.At("/people/1")!.Value));   // a missing field is nothing
        Assert.Null(map.Label("people.json", "/people/2", doc.At("/people/2")!.Value));            // none there: the editor guesses
        Assert.Null(map.LabelField("people.json", "/people/0"));                                    // a template has no single field
    }

    [Fact]
    public void A_narrowed_link_looks_only_inside_the_container_its_context_names()
    {
        var p = World();
        Assert.Equal(new ReferenceTarget("universe.json", "/sectors/1/stations/0"), FollowIn(p, "/people/0/station").Target);   // dock in south, not north
        var west = FollowIn(p, "/people/1/station");
        Assert.Equal(ReferenceOutcome.Missing, west.Outcome);
        Assert.Contains("'west'", west.Note);
        Assert.Equal(new ReferenceTarget("universe.json", "/sectors/0/stations/1"), FollowIn(p, "/homes/north/base").Target);   // "1#": the key above
    }

    [Fact]
    public void An_id_in_parts_resolves_each_part_and_names_the_bad_one()
    {
        var p = World();
        var pair = FollowIn(p, "/people/0/pair");
        Assert.Equal(ReferenceOutcome.Found, pair.Outcome);
        Assert.Equal(["/factions/0", "/factions/1"], pair.Parts!.Select(t => t.Pointer));
        Assert.Equal("/factions/1", pair.Target!.Pointer);   // the last part
        var green = FollowIn(p, "/people/1/pair");
        Assert.Equal(ReferenceOutcome.Missing, green.Outcome);
        Assert.Contains("part 2", green.Note);
        Assert.Equal(new ReferenceTarget("kits.json", "/genes/eyes/1"), FollowIn(p, "/people/0/look").Target);
        Assert.Contains("2 parts", FollowIn(p, "/people/1/look").Note);   // "eyes" alone has one part
    }
}
