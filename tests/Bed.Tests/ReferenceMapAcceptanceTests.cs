using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Data;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

// INVENTED fixtures and contract-derived cases. No games, existing test bodies or resolver implementation oracle.
public sealed class ReferenceMapAcceptanceTests : IDisposable
{
    private readonly string owner = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "A043-owned"));
    private readonly string root;
    private readonly string data;
    private readonly string schemas;
    private readonly ITestOutputHelper output;

    public ReferenceMapAcceptanceTests(ITestOutputHelper output)
    {
        this.output = output;
        root = Path.Combine(owner, Guid.NewGuid().ToString("N"));
        data = Path.Combine(root, "data"); schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
        Write("source.json", "{}");
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(root);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(resolved), owner)
            || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside this fixture's owned GUID child.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    // RM71/75: Game describes the owner; Count is reference places, not label places.
    [Fact] public void Empty_map_preserves_game_and_has_no_reference_places()
    {
        var map = ReferenceMap.Read(Empty().ToJsonString());
        Assert.Equal("Self-authored archive", map.Game); Assert.Equal(0, map.Count);
    }
    [Fact] public void Count_includes_reference_places_but_not_label_entries()
    {
        var node = Empty(); node["labels"]!.AsArray().Add(LabelRule("/rows", "caption"));
        node["references"]!.AsArray().Add(Rule("/first")); node["references"]!.AsArray().Add(Rule("/second"));
        var map = ReferenceMap.Read(node.ToJsonString()); Assert.Equal(2, map.Count);
    }

    // RM152-155: an unlisted place is NotAReference; no spelling or row-number guessing.
    [Fact] public void Unlisted_source_filename_is_not_a_reference()
    {
        WriteTarget(); var result = Read(Rule("/chosen")).Resolve(Project(), "unlisted.json", "/chosen", Json("\"alpha\""));
        Unresolved(result, ReferenceOutcome.NotAReference);
    }
    [Fact] public void Unlisted_pointer_does_not_guess_a_matching_target()
    {
        WriteTarget(); var result = Read(Rule("/chosen")).Resolve(Project(), "source.json", "/unmapped", Json("\"alpha\""));
        Unresolved(result, ReferenceOutcome.NotAReference);
    }
    [Fact] public void Source_list_wildcard_matches_each_actual_index()
    {
        Write("source.json", "{\"rows\":[{\"of\":\"alpha\"},{\"of\":\"beta\"}]}"); WriteTarget();
        var map = Read(Rule("/rows/*/of")); var p = Project();
        Found(map.Resolve(p, "source.json", "/rows/0/of", Json("\"alpha\"")), "target.json", "/items/0");
        Found(map.Resolve(p, "source.json", "/rows/1/of", Json("\"beta\"")), "target.json", "/items/1");
    }
    [Fact] public void Source_object_wildcard_matches_different_key_names()
    {
        Write("source.json", "{\"rows\":{\"north\":{\"of\":\"alpha\"},\"south\":{\"of\":\"beta\"}}}"); WriteTarget();
        var map = Read(Rule("/rows/*/of")); var p = Project();
        Found(map.Resolve(p, "source.json", "/rows/north/of", Json("\"alpha\"")), "target.json", "/items/0");
        Found(map.Resolve(p, "source.json", "/rows/south/of", Json("\"beta\"")), "target.json", "/items/1");
    }
    [Fact] public void Source_place_combines_object_and_array_wildcards()
    {
        Write("source.json", "{\"groups\":{\"east\":{\"rows\":[{\"of\":\"beta\"}]}}}"); WriteTarget();
        Found(Read(Rule("/groups/*/rows/*/of")).Resolve(Project(), "source.json", "/groups/east/rows/0/of", Json("\"beta\"")), "target.json", "/items/1");
    }
    [Fact] public void Escaped_slash_in_source_field_is_a_pointer_part()
    {
        Write("source.json", "{\"row/name\":{\"of\":\"beta\"}}"); WriteTarget();
        Found(Read(Rule("/row~1name/of")).Resolve(Project(), "source.json", "/row~1name/of", Json("\"beta\"")), "target.json", "/items/1");
    }
    [Fact] public void Escaped_tilde_in_source_field_is_a_pointer_part()
    {
        Write("source.json", "{\"row~name\":{\"of\":\"alpha\"}}"); WriteTarget();
        Found(Read(Rule("/row~0name/of")).Resolve(Project(), "source.json", "/row~0name/of", Json("\"alpha\"")), "target.json", "/items/0");
    }
    [Fact] public void Empty_named_source_property_uses_the_valid_slash_pointer()
    {
        Write("source.json", "{\"\":\"alpha\"}"); WriteTarget();
        Found(Read(Rule("/")).Resolve(Project(), "source.json", "/", Json("\"alpha\"")), "target.json", "/items/0");
    }
    [Fact] public void List_of_ids_matches_each_items_pointer_including_missing_id()
    {
        Write("source.json", "{\"chosen\":[\"beta\",\"alpha\",\"absent\"]}"); WriteTarget();
        var map = Read(Rule("/chosen")); var p = Project();
        Found(map.Resolve(p, "source.json", "/chosen/0", Json("\"beta\"")), "target.json", "/items/1");
        Found(map.Resolve(p, "source.json", "/chosen/1", Json("\"alpha\"")), "target.json", "/items/0");
        Unresolved(map.Resolve(p, "source.json", "/chosen/2", Json("\"absent\"")), ReferenceOutcome.Missing);
    }
    [Fact] public void Scalar_parent_is_not_promoted_to_a_wildcard_list_item()
    {
        Write("source.json", "{\"chosen\":\"alpha\"}"); WriteTarget();
        var result = Read(Rule("/chosen/*")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\""));
        NoSuccessfulTarget(result);
    }
    [Fact] public void Whole_source_list_does_not_invent_one_scalar_target()
    {
        Write("source.json", "{\"chosen\":[\"alpha\",\"beta\"]}"); WriteTarget();
        // Wrong-shape classification is unspecified; only a fabricated success is forbidden.
        var result = Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("[\"alpha\",\"beta\"]"));
        NoSuccessfulTarget(result);
    }

    // Format target.key: an item's field, $key for keyed objects, $value for primitive ids.
    [Fact] public void Alternate_target_identity_field_is_used_instead_of_id()
    {
        Write("target.json", "{\"items\":[{\"id\":\"unrelated\",\"code\":\"alpha\"}]}");
        Found(Read(Rule("/chosen", key: "code")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/items/0");
    }
    [Fact] public void Literal_target_field_can_contain_a_slash()
    {
        Write("target.json", "{\"items\":[{\"key/id\":\"alpha\"}]}");
        Found(Read(Rule("/chosen", key: "key/id")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/items/0");
    }
    [Fact] public void Escaped_target_list_path_returns_a_valid_escaped_pointer()
    {
        Write("target.json", "{\"rows/list~set\":[{\"id\":\"alpha\"}]}");
        Found(Read(Rule("/chosen", list: "/rows~1list~0set")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/rows~1list~0set/0");
    }
    [Fact] public void Numeric_integer_identity_resolves_by_value_not_row_index()
    {
        Write("target.json", "{\"items\":[{\"id\":91},{\"id\":17}]}");
        Found(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("17")), "target.json", "/items/1");
    }
    [Fact] public void Numeric_decimal_identity_resolves_to_its_record()
    {
        Write("target.json", "{\"items\":[{\"id\":17.25},{\"id\":9.5}]}");
        Found(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("9.5")), "target.json", "/items/1");
    }
    [Fact] public void Case_distinct_ids_select_their_exact_targets()
    {
        Write("target.json", "{\"items\":[{\"id\":\"drift\"},{\"id\":\"DRIFT\"}]}"); var map = Read(Rule("/chosen")); var p = Project();
        Found(map.Resolve(p, "source.json", "/chosen", Json("\"drift\"")), "target.json", "/items/0");
        Found(map.Resolve(p, "source.json", "/chosen", Json("\"DRIFT\"")), "target.json", "/items/1");
    }
    [Fact] public void Target_search_crosses_several_array_and_object_wildcards()
    {
        Write("target.json", "{\"groups\":[{\"regions\":{\"first\":{\"items\":[{\"id\":\"beta\"}]} }},{\"regions\":{\"last\":{\"items\":[{\"id\":\"alpha\"}]}}}]}");
        Found(Read(Rule("/chosen", list: "/groups/*/regions/*/items")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/groups/1/regions/last/items/0");
    }
    [Fact] public void Duplicate_id_across_target_wildcard_branches_is_ambiguous()
    {
        Write("target.json", "{\"groups\":{\"east\":{\"items\":[{\"id\":\"alpha\"}]},\"west\":{\"items\":[{\"id\":\"alpha\"}]}}}");
        Unresolved(Read(Rule("/chosen", list: "/groups/*/items")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.Ambiguous);
    }
    [Fact] public void Keyed_target_returns_the_escaped_dictionary_item_pointer()
    {
        Write("target.json", "{\"registry\":{\"alpha/~\":{\"caption\":\"Entry\"}}}");
        Found(Read(Rule("/chosen", list: "/registry", key: "$key")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha/~\"")), "target.json", "/registry/alpha~1~0");
    }
    [Fact] public void Primitive_string_target_returns_the_matching_array_item()
    {
        Write("target.json", "{\"codes\":[\"beta\",\"alpha\"]}");
        Found(Read(Rule("/chosen", list: "/codes", key: "$value")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/codes/1");
    }
    [Fact] public void Primitive_numeric_target_returns_the_matching_array_item()
    {
        Write("target.json", "{\"codes\":[99,17]}");
        Found(Read(Rule("/chosen", list: "/codes", key: "$value")).Resolve(Project(), "source.json", "/chosen", Json("17")), "target.json", "/codes/1");
    }
    [Fact] public void Scalar_target_collection_does_not_invent_an_item()
    {
        Write("target.json", "{\"items\":\"alpha\"}");
        NoSuccessfulTarget(Read(Rule("/chosen", key: "$value")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")));
    }
    [Fact] public void Null_target_collection_does_not_invent_an_item()
    {
        Write("target.json", "{\"items\":null}");
        NoSuccessfulTarget(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")));
    }
    [Fact] public void Missing_id_in_a_valid_opened_target_is_missing()
    {
        WriteTarget(); Unresolved(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("\"absent\"")), ReferenceOutcome.Missing);
    }

    // RM24/152-154: target files are opened through the project as needed; an unreadable one is NotLoaded.
    [Fact] public void Target_file_absent_from_project_is_not_loaded()
    {
        Unresolved(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.NotLoaded);
    }
    [Fact] public void Malformed_target_json_is_not_loaded()
    {
        Write("target.json", "{broken"); Unresolved(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.NotLoaded);
    }
    [Fact] public void Schema_invalid_target_is_not_loaded()
    {
        Write("target.json", "{\"items\":[{\"id\":\"alpha\"}]}", "{\"type\":\"object\",\"required\":[\"necessary\"]}");
        Unresolved(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.NotLoaded);
    }
    [Fact] public void Target_deleted_after_project_listing_is_not_loaded()
    {
        WriteTarget(); var p = Project(); File.Delete(Path.Combine(data, "target.json"));
        Unresolved(Read(Rule("/chosen")).Resolve(p, "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.NotLoaded);
    }
    [Fact] public void Resolve_opens_the_target_lazily_through_its_project_file()
    {
        WriteTarget(); var p = Project(); var f = Assert.IsType<DataFile>(p.Find("target.json")); Assert.Null(f.Session);
        Found(Read(Rule("/chosen")).Resolve(p, "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/items/0");
        Assert.NotNull(f.Session); Assert.Equal(DataFileState.Open, f.State);
    }
    [Fact] public void Unrelated_bad_file_does_not_prevent_a_valid_reference()
    {
        WriteTarget(); Write("unrelated.json", "{broken"); var p = Project();
        Found(Read(Rule("/chosen")).Resolve(p, "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/items/0");
        Assert.Null(p.Find("unrelated.json")!.Session);
    }

    // RM46-47 and format also/sentinels: all targets count, and deliberate non-ids never become ids.
    [Fact] public void Also_can_supply_the_only_matching_target()
    {
        WriteTarget(); Write("second.json", "{\"items\":[{\"id\":\"gamma\"}]}"); var rule = Rule("/chosen"); rule["also"] = new JsonArray(Target("second.json"));
        Found(Read(rule).Resolve(Project(), "source.json", "/chosen", Json("\"gamma\"")), "second.json", "/items/0");
    }
    [Fact] public void Match_in_primary_and_also_is_ambiguous()
    {
        WriteTarget(); Write("second.json", "{\"items\":[{\"id\":\"alpha\"}]}"); var rule = Rule("/chosen"); rule["also"] = new JsonArray(Target("second.json"));
        Unresolved(Read(rule).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.Ambiguous);
    }
    [Fact] public void Two_matching_records_in_one_list_are_ambiguous()
    {
        Write("target.json", "{\"items\":[{\"id\":\"alpha\"},{\"id\":\"alpha\"}]}");
        Unresolved(Read(Rule("/chosen")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.Ambiguous);
    }
    [Fact] public void Duplicate_primitive_ids_are_ambiguous()
    {
        Write("target.json", "{\"codes\":[\"alpha\",\"alpha\"]}");
        Unresolved(Read(Rule("/chosen", list: "/codes", key: "$value")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.Ambiguous);
    }
    [Fact] public void Sentinel_wins_even_when_a_real_target_has_that_id()
    {
        WriteTarget(); var rule = Rule("/chosen"); rule["sentinels"] = new JsonArray("alpha");
        Unresolved(Read(rule).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.Sentinel);
    }
    [Fact] public void Word_only_scope_is_not_guessed_even_with_one_global_match()
    {
        WriteTarget(); var rule = Rule("/chosen"); rule["scope"] = "the source object's own district";
        Unresolved(Read(rule).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), ReferenceOutcome.NeedsScope);
    }

    // RM152-154 / keys: use the decoded last pointer part, not the property's value.
    [Fact] public void Source_dictionary_key_is_resolved_instead_of_its_value()
    {
        Write("source.json", "{\"weights\":{\"beta\":\"alpha\"}}"); WriteTarget(); var rule = Rule("/weights"); rule["keys"] = true;
        Found(Read(rule).Resolve(Project(), "source.json", "/weights/beta", Json("\"alpha\"")), "target.json", "/items/1");
    }
    [Fact] public void Absent_source_key_identity_is_missing_even_if_its_value_is_a_real_id()
    {
        Write("source.json", "{\"weights\":{\"absent\":\"alpha\"}}"); WriteTarget(); var rule = Rule("/weights"); rule["keys"] = true;
        Unresolved(Read(rule).Resolve(Project(), "source.json", "/weights/absent", Json("\"alpha\"")), ReferenceOutcome.Missing);
    }
    [Fact] public void Nested_declared_key_place_decodes_escaped_last_key()
    {
        Write("source.json", "{\"groups\":{\"west\":{\"weights\":{\"alpha/~\":9}}}}"); Write("target.json", "{\"registry\":{\"alpha/~\":{}}}");
        var rule = Rule("/groups/*/weights", list: "/registry", key: "$key"); rule["keys"] = true;
        Found(Read(rule).Resolve(Project(), "source.json", "/groups/west/weights/alpha~1~0", Json("9")), "target.json", "/registry/alpha~1~0");
    }
    [Fact] public void Descendant_of_a_declared_dictionary_key_is_not_another_declared_key()
    {
        Write("source.json", "{\"weights\":{\"box\":{\"alpha\":9}}}"); WriteTarget(); var rule = Rule("/weights"); rule["keys"] = true;
        Unresolved(Read(rule).Resolve(Project(), "source.json", "/weights/box/alpha", Json("9")), ReferenceOutcome.NotAReference);
    }

    // RM117-119 / format label.list: labels describe the immediate items of the declared list.
    [Fact] public void Label_field_applies_to_each_flat_list_item()
    {
        var map = Labels(LabelRule("/rows", "caption"));
        Assert.Equal("caption", map.LabelField("source.json", "/rows/0")); Assert.Equal("caption", map.LabelField("source.json", "/rows/27"));
    }
    [Fact] public void Label_field_applies_to_items_of_nested_lists()
    {
        var map = Labels(LabelRule("/groups/*/rows", "caption"));
        Assert.Equal("caption", map.LabelField("source.json", "/groups/0/rows/2")); Assert.Equal("caption", map.LabelField("source.json", "/groups/3/rows/1"));
    }
    [Fact] public void Label_field_combines_object_keys_and_array_depths()
    {
        var map = Labels(LabelRule("/groups/*/bands/*/rows", "caption"));
        Assert.Equal("caption", map.LabelField("source.json", "/groups/west/bands/2/rows/4"));
    }
    [Fact] public void Label_field_is_not_assigned_to_the_list_itself()
    {
        var map = Labels(LabelRule("/groups/*/rows", "caption")); Assert.Null(map.LabelField("source.json", "/groups/0/rows"));
    }
    [Fact] public void Label_field_is_not_assigned_to_a_field_below_the_item()
    {
        var map = Labels(LabelRule("/rows", "caption")); Assert.Null(map.LabelField("source.json", "/rows/0/caption"));
    }
    [Fact] public void Label_field_is_not_assigned_in_an_unlisted_file()
    {
        var map = Labels(LabelRule("/rows", "caption")); Assert.Null(map.LabelField("other.json", "/rows/0"));
    }
    [Fact] public void Label_list_path_supports_pointer_escapes()
    {
        var map = Labels(LabelRule("/rows~1set~0", "caption")); Assert.Equal("caption", map.LabelField("source.json", "/rows~1set~0/0"));
    }

    [Fact] public void Ten_thousand_resolves_over_five_thousand_targets_have_a_generous_ceiling()
    {
        var items = Enumerable.Range(0, 5_000).Select(i => new { id = "item_" + i }).ToArray();
        Write("target.json", JsonSerializer.Serialize(new { items })); var p = Project(); var map = Read(Rule("/chosen"));
        var ids = items.Select(v => Json(JsonSerializer.Serialize(v.id))).ToArray();
        Assert.Null(p.Find("target.json")!.Session);
        // Fixture/schema/map/value preparation excluded. First target lazy-open is intentionally included.
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(120), $"Reference sanity deadline: completed {i}/10000 resolves in {timer.Elapsed.TotalSeconds:F3} s; ceiling 120 s.");
            Found(map.Resolve(p, "source.json", "/chosen", ids[i % ids.Length]), "target.json", "/items/" + (i % ids.Length));
        }
        timer.Stop(); output.WriteLine($"10000 resolves / 5000 targets: {timer.Elapsed.TotalMilliseconds:F3} ms; first lazy open included; fixture setup excluded.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(120), $"Reference sanity ceiling 120 s exceeded: {timer.Elapsed.TotalSeconds:F3} s");
    }

    // Format refusals below are independent mutations of a wholly valid old-feature map.

    [Fact] public void Named_identity_field_works_inside_a_keyed_target_object()
    {
        Write("target.json", "{\"bank\":{\"west\":{\"id\":\"alpha\"}}}");
        Found(Read(Rule("/chosen", list: "/bank")).Resolve(Project(), "source.json", "/chosen", Json("\"alpha\"")), "target.json", "/bank/west");
    }
    [Fact] public void Pointer_escape_decoding_is_single_pass()
    {
        Write("source.json", "{\"a~1b\":\"alpha\"}"); Write("target.json", "{\"a~1b\":[{\"id\":\"alpha\"}]}");
        Found(Read(Rule("/a~01b", list: "/a~01b")).Resolve(Project(), "source.json", "/a~01b", Json("\"alpha\"")), "target.json", "/a~01b/0");
    }
    [Fact] public void Malformed_map_JSON_throws_a_JSON_exception()
    {
        Assert.ThrowsAny<JsonException>(() => ReferenceMap.Read("{broken"));
    }
    [Fact] public void Non_object_map_root_is_a_format_refusal()
    {
        var error = Assert.ThrowsAny<InvalidDocumentException>(() => ReferenceMap.Read("[]")); Assert.NotEmpty(error.Issues);
    }
    [Fact] public void Complete_in_scope_format_example_reads_all_optional_fields()
    {
        var map = ReferenceMap.Read(FormatExample().ToJsonString());
        Assert.Equal("Self-authored archive", map.Game); Assert.Equal(1, map.Count);
        Assert.Equal("caption", map.LabelField("source.json", "/rows/0"));
    }
    [Fact] public void Independent_format_problems_are_all_returned()
    {
        var node = FormatExample(); node["game"] = ""; Label(node)["field"] = ""; Reference(node)["evidence"] = "";
        var error = Assert.ThrowsAny<InvalidDocumentException>(() => ReferenceMap.Read(node.ToJsonString()));
        Assert.True(error.Issues.Count >= 3);
        Assert.Contains(error.Issues, i => (i.Message + " " + i.Path).Contains("game", StringComparison.Ordinal));
        Assert.Contains(error.Issues, i => (i.Message + " " + i.Path).Contains("field", StringComparison.Ordinal));
        Assert.Contains(error.Issues, i => (i.Message + " " + i.Path).Contains("evidence", StringComparison.Ordinal));
    }
    [Fact] public void Missing_game_is_refused() => Reject(m => { m.Remove("game"); });
    [Fact] public void Missing_labels_is_refused() => Reject(m => { m.Remove("labels"); });
    [Fact] public void Missing_references_is_refused() => Reject(m => { m.Remove("references"); });
    [Fact] public void Unknown_root_property_is_refused() => Reject(m => { m["unlisted"] = true; });
    [Fact] public void Schema_annotation_must_be_a_string() => Reject(m => { m["$schema"] = 7; });
    [Fact] public void Game_must_be_a_string() => Reject(m => { m["game"] = 7; });
    [Fact] public void Game_must_not_be_empty() => Reject(m => { m["game"] = ""; });
    [Fact] public void Labels_must_be_an_array() => Reject(m => { m["labels"] = new JsonObject(); });
    [Fact] public void Label_items_must_be_objects() => Reject(m => { m["labels"] = new JsonArray(7); });
    [Fact] public void Label_requires_its_file() => Reject(m => { Label(m).Remove("file"); });
    [Fact] public void Label_requires_its_list() => Reject(m => { Label(m).Remove("list"); });
    [Fact] public void Field_label_requires_its_field() => Reject(m => { Label(m).Remove("field"); });
    [Fact] public void Unknown_label_property_is_refused() => Reject(m => { Label(m)["unlisted"] = true; });
    [Fact] public void Label_file_must_be_a_string() => Reject(m => { Label(m)["file"] = 7; });
    [Fact] public void File_name_must_end_in_json() => Reject(m => { Label(m)["file"] = "source.txt"; });
    [Fact] public void File_name_cannot_contain_a_forward_slash() => Reject(m => { Label(m)["file"] = "folder/source.json"; });
    [Fact] public void File_name_cannot_contain_a_backslash() => Reject(m => { Label(m)["file"] = "folder\\source.json"; });
    [Fact] public void Label_place_must_be_a_string() => Reject(m => { Label(m)["list"] = 7; });
    [Fact] public void Place_requires_its_leading_slash() => Reject(m => { Label(m)["list"] = "rows"; });
    [Fact] public void Empty_place_is_refused() => Reject(m => { Label(m)["list"] = ""; });
    [Fact] public void Partial_wildcard_segment_is_refused() => Reject(m => { Label(m)["list"] = "/row*"; });
    [Fact] public void Naked_wildcard_is_refused() => Reject(m => { Label(m)["list"] = "*"; });
    [Fact] public void Unknown_pointer_tilde_escape_is_refused() => Reject(m => { Label(m)["list"] = "/rows~2name"; });
    [Fact] public void Label_field_must_be_a_string() => Reject(m => { Label(m)["field"] = 7; });
    [Fact] public void Label_field_must_not_be_empty() => Reject(m => { Label(m)["field"] = ""; });
    [Fact] public void Label_note_must_be_a_string() => Reject(m => { Label(m)["note"] = 7; });
    [Fact] public void References_must_be_an_array() => Reject(m => { m["references"] = new JsonObject(); });
    [Fact] public void Reference_items_must_be_objects() => Reject(m => { m["references"] = new JsonArray(7); });
    [Fact] public void Reference_requires_its_file() => Reject(m => { Reference(m).Remove("file"); });
    [Fact] public void Reference_requires_its_place() => Reject(m => { Reference(m).Remove("at"); });
    [Fact] public void Reference_requires_its_target() => Reject(m => { Reference(m).Remove("target"); });
    [Fact] public void Reference_requires_its_evidence() => Reject(m => { Reference(m).Remove("evidence"); });
    [Fact] public void Unknown_reference_property_is_refused() => Reject(m => { Reference(m)["unlisted"] = true; });
    [Fact] public void Reference_file_must_be_a_string() => Reject(m => { Reference(m)["file"] = 7; });
    [Fact] public void Reference_place_must_be_a_string() => Reject(m => { Reference(m)["at"] = 7; });
    [Fact] public void Reference_place_must_be_a_valid_pointer() => Reject(m => { Reference(m)["at"] = "chosen"; });
    [Fact] public void Keys_flag_must_be_a_boolean() => Reject(m => { Reference(m)["keys"] = "yes"; });
    [Fact] public void Target_must_be_an_object() => Reject(m => { Reference(m)["target"] = 7; });
    [Fact] public void Target_requires_its_file() => Reject(m => { TargetOf(m).Remove("file"); });
    [Fact] public void Target_requires_its_list() => Reject(m => { TargetOf(m).Remove("list"); });
    [Fact] public void Target_requires_its_key() => Reject(m => { TargetOf(m).Remove("key"); });
    [Fact] public void Unknown_target_property_is_refused() => Reject(m => { TargetOf(m)["unlisted"] = true; });
    [Fact] public void Target_file_must_be_a_basename() => Reject(m => { TargetOf(m)["file"] = "folder/target.json"; });
    [Fact] public void Target_list_must_be_a_pointer() => Reject(m => { TargetOf(m)["list"] = "items"; });
    [Fact] public void Target_key_must_be_a_string() => Reject(m => { TargetOf(m)["key"] = 7; });
    [Fact] public void Target_key_must_not_be_empty() => Reject(m => { TargetOf(m)["key"] = ""; });
    [Fact] public void Also_must_be_an_array() => Reject(m => { Reference(m)["also"] = new JsonObject(); });
    [Fact] public void Also_items_must_be_target_objects() => Reject(m => { Reference(m)["also"] = new JsonArray(7); });
    [Fact] public void Also_targets_require_their_key() => Reject(m => { Reference(m)["also"]!.AsArray()[0]!.AsObject().Remove("key"); });
    [Fact] public void Scope_must_be_a_string() => Reject(m => { Reference(m)["scope"] = 7; });
    [Fact] public void Sentinels_must_be_an_array() => Reject(m => { Reference(m)["sentinels"] = "none"; });
    [Fact] public void Each_sentinel_must_be_a_string() => Reject(m => { Reference(m)["sentinels"] = new JsonArray(7); });
    [Fact] public void Evidence_must_be_a_string() => Reject(m => { Reference(m)["evidence"] = 7; });
    [Fact] public void Evidence_must_not_be_empty() => Reject(m => { Reference(m)["evidence"] = ""; });
    [Fact] public void Reference_note_must_be_a_string() => Reject(m => { Reference(m)["note"] = 7; });
    [Fact] public void Checked_must_be_an_object() => Reject(m => { Reference(m)["checked"] = new JsonArray(); });
    [Fact] public void Unknown_checked_counter_is_refused() => Reject(m => { Checked(m)["unlisted"] = 0; });
    [Fact] public void Checked_values_must_be_an_integer() => Reject(m => { Checked(m)["values"] = 0.5; });
    [Fact] public void Checked_values_must_be_nonnegative() => Reject(m => { Checked(m)["values"] = -1; });
    [Fact] public void Checked_found_must_be_an_integer() => Reject(m => { Checked(m)["found"] = 0.5; });
    [Fact] public void Checked_found_must_be_nonnegative() => Reject(m => { Checked(m)["found"] = -1; });
    [Fact] public void Checked_sentinel_must_be_an_integer() => Reject(m => { Checked(m)["sentinel"] = 0.5; });
    [Fact] public void Checked_sentinel_must_be_nonnegative() => Reject(m => { Checked(m)["sentinel"] = -1; });
    [Fact] public void Checked_missing_must_be_an_integer() => Reject(m => { Checked(m)["missing"] = 0.5; });
    [Fact] public void Checked_missing_must_be_nonnegative() => Reject(m => { Checked(m)["missing"] = -1; });
    [Fact] public void Checked_ambiguous_must_be_an_integer() => Reject(m => { Checked(m)["ambiguous"] = 0.5; });
    [Fact] public void Checked_ambiguous_must_be_nonnegative() => Reject(m => { Checked(m)["ambiguous"] = -1; });

    private void WriteTarget() => Write("target.json", "{\"items\":[{\"id\":\"alpha\"},{\"id\":\"beta\"}]}");
    private void Write(string name, string text, string schema = "{}")
    {
        File.WriteAllText(Path.Combine(data, name), text, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(schemas, Path.GetFileNameWithoutExtension(name) + ".schema.json"), schema, new UTF8Encoding(false));
    }
    private DataProject Project() => DataProject.Open(data, schemas, readOnly: true);
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    private static JsonObject Empty() => new() { ["game"] = "Self-authored archive", ["labels"] = new JsonArray(), ["references"] = new JsonArray() };
    private static JsonObject Target(string file = "target.json", string list = "/items", string key = "id") => new() { ["file"] = file, ["list"] = list, ["key"] = key };
    private static JsonObject Rule(string at, string file = "target.json", string list = "/items", string key = "id") => new()
    {
        ["file"] = "source.json", ["at"] = at, ["target"] = Target(file, list, key), ["evidence"] = "Self-authored public-contract acceptance fixture"
    };
    private static JsonObject LabelRule(string list, string field) => new() { ["file"] = "source.json", ["list"] = list, ["field"] = field };
    private static ReferenceMap Read(params JsonObject[] rules)
    {
        var node = Empty(); foreach (var rule in rules) node["references"]!.AsArray().Add(rule);
        return ReferenceMap.Read(node.ToJsonString());
    }
    private static ReferenceMap Labels(params JsonObject[] labels)
    {
        var node = Empty(); foreach (var label in labels) node["labels"]!.AsArray().Add(label);
        return ReferenceMap.Read(node.ToJsonString());
    }
    private static void Found(ReferenceResult result, string file, string pointer)
    {
        Assert.Equal(ReferenceOutcome.Found, result.Outcome); Assert.Equal(new ReferenceTarget(file, pointer), result.Target);
    }
    private static JsonObject FormatExample()
    {
        var node = Empty(); node["$schema"] = "reference_map.schema.json";
        var label = LabelRule("/rows", "caption"); label["note"] = "Optional label metadata"; node["labels"]!.AsArray().Add(label);
        var rule = Rule("/chosen"); rule["keys"] = false; rule["also"] = new JsonArray(Target("second.json"));
        rule["scope"] = "word-only context"; rule["sentinels"] = new JsonArray("none"); rule["note"] = "Optional reference metadata";
        rule["checked"] = new JsonObject { ["values"] = 0, ["found"] = 0, ["sentinel"] = 0, ["missing"] = 0, ["ambiguous"] = 0 };
        node["references"]!.AsArray().Add(rule); return node;
    }
    private static JsonObject Reference(JsonObject node) => node["references"]!.AsArray()[0]!.AsObject();
    private static JsonObject Label(JsonObject node) => node["labels"]!.AsArray()[0]!.AsObject();
    private static JsonObject TargetOf(JsonObject node) => Reference(node)["target"]!.AsObject();
    private static JsonObject Checked(JsonObject node) => Reference(node)["checked"]!.AsObject();
    // These outcomes are the card's basic single-id cases, never multipart results.
    private static void Unresolved(ReferenceResult result, ReferenceOutcome expected)
    {
        Assert.Equal(expected, result.Outcome); Assert.Null(result.Target);
    }
    private static void NoSuccessfulTarget(ReferenceResult result)
    {
        Assert.NotEqual(ReferenceOutcome.Found, result.Outcome); Assert.Null(result.Target);
    }
    private static void Reject(Action<JsonObject> mutation)
    {
        var node = FormatExample();
        var control = ReferenceMap.Read(node.ToJsonString()); Assert.Equal(1, control.Count);
        mutation(node);
        var error = Assert.ThrowsAny<InvalidDocumentException>(() => ReferenceMap.Read(node.ToJsonString())); Assert.NotEmpty(error.Issues);
    }
}
