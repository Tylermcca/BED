using System.Text.Json;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

// Self-authored seam witnesses. These exercise the unchanged public facade, not implementation types.
public sealed class JsonSchemaSeparationTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static string[] Facets(IEnumerable<JsonElement> schemas) => schemas.Select(s => s.GetRawText()).ToArray();
    private static (string?, string)[] Problems(JsonSchemas schemas, JsonElement value, string name) =>
        schemas.Validate(value, name).Select(i => (i.Path, i.Message)).ToArray();

    [Fact]
    public void Branch_discovery_and_validation_share_the_current_registry()
    {
        var schemas = new JsonSchemas().Add("shape", """
            {"type":"object","properties":{"mode":{"type":"string"}},
             "if":{"properties":{"mode":{"const":"a"}},"required":["mode"]},
             "then":{"properties":{"value":{"type":"integer","minimum":2}}},
             "else":{"properties":{"value":{"type":"string","minLength":2}}}}
            """);
        var a = Json("""{"mode":"a","value":1}""");
        var b = Json("""{"mode":"b","value":"x"}""");
        Assert.Equal("integer", Assert.Single(schemas.At(a, "shape", "/value")).GetProperty("type").GetString());
        Assert.Equal("string", Assert.Single(schemas.At(b, "shape", "/value")).GetProperty("type").GetString());
        Assert.Equal(new (string?, string)[] { ("$.value", "must be at least 2") }, Problems(schemas, a, "shape"));
        Assert.Equal(new (string?, string)[] { ("$.value", "must be at least 2 characters") }, Problems(schemas, b, "shape"));
        Assert.Equal(Facets(schemas.At(a, "shape", "/value")), Facets(schemas.Children(a, "shape", "", ["value"])["value"]));
    }

    [Fact]
    public void Discovery_retains_missing_fields_escaped_pointers_and_pattern_overlap()
    {
        var schemas = new JsonSchemas().Add("shape", """
            {"properties":{"a/b~c":{"type":"string"}},"patternProperties":{"^a":{"minLength":2}},
             "additionalProperties":{"type":"boolean"}}
            """);
        var value = Json("{}");
        var facets = schemas.At(value, "shape", "/a~1b~0c");
        Assert.Equal(2, facets.Count);
        Assert.Equal("string", facets[0].GetProperty("type").GetString());
        Assert.Equal(2, facets[1].GetProperty("minLength").GetInt32());
        var children = schemas.Children(value, "shape", "", ["a/b~c", "other"]);
        Assert.Equal(Facets(facets), Facets(children["a/b~c"]));
        Assert.Equal("boolean", Assert.Single(children["other"]).GetProperty("type").GetString());
    }

    [Fact]
    public void Added_schema_invalidates_item_results_and_is_visible_to_discovery()
    {
        const string list = """{"type":"array","items":{"type":"object","properties":{"value":{"$ref":"item"}}}}""";
        var first = new JsonSchemas();
        Assert.Same(first, first.Add("item", """{"type":"integer","minimum":2}"""));
        first.Add("list", list);
        var second = new JsonSchemas().Add("item", """{"type":"integer","minimum":2}""").Add("list", list);
        var values = Json("""[{"value":1}]""");
        Assert.Single(first.Validate(values, "list"));
        var generation = first.Generation;
        first.Add("item", """{"type":"integer","minimum":0}""");
        Assert.Equal(generation + 1, first.Generation);
        Assert.Empty(first.Validate(values, "list"));
        Assert.Single(second.Validate(values, "list"));
        Assert.Contains(first.At(values, "list", "/0/value"), s => s.TryGetProperty("minimum", out var min) && min.GetInt32() == 0);
        Assert.Contains(second.At(values, "list", "/0/value"), s => s.TryGetProperty("minimum", out var min) && min.GetInt32() == 2);
    }

    [Fact]
    public void Union_choices_keep_outer_constraints_and_numeric_equality()
    {
        var schemas = new JsonSchemas().Add("choice", """
            {"minimum":2,"anyOf":[{"enum":[1,2]},{"enum":[2.0,3]}]}
            """);
        var facets = schemas.At(Json("2"), "choice", "");
        var offered = facets.Where(s => s.TryGetProperty("enum", out _)).SelectMany(s => s.GetProperty("enum").EnumerateArray()).ToArray();
        Assert.Equal(2, offered.Length);
        Assert.True(JsonSchemas.Same(Json("2.0"), offered[0]));
        Assert.True(JsonSchemas.Same(Json("3"), offered[1]));
        Assert.Empty(schemas.Validate(Json("2"), "choice"));
        Assert.Single(schemas.Validate(Json("1"), "choice"));
        Assert.True(JsonSchemas.Same(Json("{\"a\":1,\"b\":[2]}"), Json("{\"b\":[2.0],\"a\":1.0}")));
    }

    [Fact]
    public void Cycle_faults_and_explanation_state_do_not_leak_between_calls()
    {
        var schemas = new JsonSchemas().Add("cycle", """{"$ref":"cycle"}""").Add("ok", """{"type":"integer"}""");
        var expected = new (string?, string)[] { ("$", "the schema refers to 'cycle' in a loop that never reaches the value") };
        Assert.Equal(expected, Problems(schemas, Json("1"), "cycle"));
        Assert.Empty(schemas.Validate(Json("1"), "ok"));
        Assert.Equal(expected, Problems(schemas, Json("1"), "cycle"));
        Assert.NotEmpty(schemas.At(Json("1"), "cycle", ""));
        Assert.Empty(schemas.Validate(Json("1"), "ok"));
    }

    [Fact]
    public void Keyword_issue_order_unknown_keywords_and_regex_translation_are_unchanged()
    {
        var schemas = new JsonSchemas().Add("bad", """{"unsupported":true,"type":"integer","const":"x","minLength":2}""");
        Assert.Equal(new (string?, string)[]
        {
            ("$", "the schema uses 'unsupported', which this validator doesn't support, so this value wasn't fully checked"),
            ("$", "expected a whole number, found text"),
            ("$", "must be \"x\""),
            ("$", "must be at least 2 characters"),
        }, Problems(schemas, Json("\"a\""), "bad"));
        schemas.Add("ascii", """{"pattern":"^\\d+$"}""");
        Assert.Empty(schemas.Validate(Json("\"12\""), "ascii"));
        Assert.Single(schemas.Validate(Json("\"\u0661\""), "ascii"));
        schemas.Add("invalid", """{"pattern":"["}""");
        Assert.Equal(new (string?, string)[] { ("$", "the schema's pattern [ can't be read by this validator, so this text wasn't checked against it") }, Problems(schemas, Json("\"a\""), "invalid"));
    }

    [Fact]
    public void Facade_preserves_missing_schema_exception_and_embedded_id_lookup()
    {
        var schemas = new JsonSchemas().Add("root", """
            {"$id":"https://example.test/root","$defs":{"child":{"$id":"child","type":"integer"}}}
            """);
        Assert.NotNull(schemas.Find("https://example.test/child"));
        Assert.Empty(schemas.Validate(Json("1"), "https://example.test/child"));
        var error = Assert.Throws<ArgumentException>(() => schemas.Validate(Json("1"), "missing"));
        Assert.Equal("schema", error.ParamName);
        Assert.StartsWith("no schema called 'missing' has been added", error.Message);
        Assert.Throws<ArgumentException>(() => schemas.At(Json("1"), "missing", ""));
    }
}
