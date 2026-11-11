using System.Text.Json;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>The self-authored JSON Schema 2020-12 validator, keyword by keyword, on self-authored schemas. (It was also
/// checked against Python's jsonschema on 600 mutated copies of a real game's data, all agreeing; that check reads the
/// game and stays outside this repo.)</summary>
public sealed class JsonSchemasTests
{
    private const string Common = """
        {"$id": "https://example.test/schemas/common.schema.json",
         "$defs": {"id": {"type": "string", "pattern": "^[a-z][a-z0-9_]*$"}, "nonneg": {"type": "number", "minimum": 0}}}
        """;

    private const string Shop = """
        {"$id": "https://example.test/schemas/shop.schema.json", "type": "object", "required": ["kind", "items"],
         "additionalProperties": false,
         "properties": {
           "kind": {"const": "shop"},
           "tags": {"type": "array", "items": {"type": "string", "minLength": 1}, "uniqueItems": true, "maxItems": 3},
           "items": {"type": "array", "minItems": 1, "items": {"$ref": "#/$defs/item"}},
           "prices": {"type": "object", "propertyNames": {"$ref": "common.schema.json#/$defs/id"},
                      "patternProperties": {"^[a-z]": {"$ref": "common.schema.json#/$defs/nonneg"}}, "minProperties": 1}
         },
         "$defs": {"item": {"type": "object", "required": ["id", "size"], "additionalProperties": false,
           "properties": {"id": {"$ref": "common.schema.json#/$defs/id"}, "size": {"enum": ["S", "M", "L"]},
                          "stack": {"type": "integer", "exclusiveMinimum": 0, "maximum": 99},
                          "legal": {"type": "boolean"}, "licence": {"type": "string"}},
           "dependentRequired": {"licence": ["legal"]},
           "if": {"properties": {"size": {"const": "L"}}}, "then": {"required": ["stack"]}}}}
        """;

    private static readonly JsonSchemas Set = new JsonSchemas().Add("common.schema.json", Common).Add("shop.schema.json", Shop);

    private static IReadOnlyList<Issue> Check(string json) => Set.Validate(JsonDocument.Parse(json).RootElement, "shop.schema.json");

    private const string Good = """{"kind":"shop","items":[{"id":"rope","size":"S"},{"id":"crate","size":"L","stack":5}],"prices":{"rope":2}}""";

    [Fact]
    public void AValidDocumentHasNoIssues() => Assert.Empty(Check(Good));

    [Theory]
    [InlineData("""{"kind":"shop","items":[]}""", "$.items", "needs at least 1 item")]
    [InlineData("""{"kind":"store","items":[{"id":"a","size":"S"}]}""", "$.kind", "must be \"shop\"")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S"}],"owner":"x"}""", "$.owner", "'owner' isn't a known field here")]
    [InlineData("""{"kind":"shop"}""", "$", "'items' is required")]
    [InlineData("""{"kind":"shop","items":[{"id":"Rope","size":"S"}]}""", "$.items[0].id", "doesn't fit the pattern")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"XL"}]}""", "$.items[0].size", "must be one of \"S\", \"M\", \"L\"")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S","stack":0}]}""", "$.items[0].stack", "must be more than 0")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S","stack":2.5}]}""", "$.items[0].stack", "expected a whole number, found 2.5")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S","stack":100}]}""", "$.items[0].stack", "must be at most 99")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"L"}]}""", "$.items[0]", "'stack' is required")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S","licence":"x"}]}""", "$.items[0]", "'legal' is required when 'licence' is given")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S"}],"tags":["x","x"]}""", "$.tags", "item 1 repeats item 0")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S"}],"tags":["a","b","c","d"]}""", "$.tags", "at most 3 items")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S"}],"tags":[""]}""", "$.tags[0]", "can't be empty")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S"}],"prices":{}}""", "$.prices", "needs at least 1 entry")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S"}],"prices":{"rope":-1}}""", "$.prices.rope", "must be at least 0")]
    [InlineData("""{"kind":"shop","items":[{"id":"a","size":"S"}],"prices":{"Rope":1}}""", "$.prices.Rope", "'Rope' isn't an allowed name here")]
    public void EachKeywordFailsAtItsPathInPlainWords(string json, string path, string message)
    {
        var issues = Check(json);
        Assert.Contains(issues, i => i.Path == path && i.Message.Contains(message));
    }

    [Fact]
    public void IntegersMayBeWrittenWithAPointZero() => Assert.Empty(Check(Good.Replace("\"stack\":5", "\"stack\":5.0")));

    [Fact]
    public void CombinatorsWork()
    {
        var set = new JsonSchemas().Add("c.json", """
            {"type":"object","properties":{
              "one":{"oneOf":[{"type":"integer"},{"minimum":10}]},
              "any":{"anyOf":[{"type":"string"},{"type":"boolean"}]},
              "not":{"not":{"const":"forbidden"}},
              "all":{"allOf":[{"minimum":1},{"maximum":3}]},
              "pair":{"type":"array","prefixItems":[{"type":"string"},{"type":"number"}],"items":false},
              "has":{"type":"array","contains":{"const":"key"}}}}
            """);
        IReadOnlyList<Issue> V(string j) => set.Validate(JsonDocument.Parse(j).RootElement, "c.json");
        Assert.Empty(V("""{"one":5,"any":true,"not":"fine","all":2,"pair":["a",1],"has":["x","key"]}"""));
        Assert.Contains(V("""{"one":12}"""), i => i.Message.Contains("exactly one"));   // integer and >= 10
        Assert.Contains(V("""{"one":2.5}"""), i => i.Message.Contains("at least 10"));   // the one form a fraction can fit says what it needs (A135)
        Assert.NotEmpty(V("""{"any":3}"""));
        Assert.NotEmpty(V("""{"not":"forbidden"}"""));
        Assert.NotEmpty(V("""{"all":4}"""));
        Assert.Contains(V("""{"pair":["a",1,2]}"""), i => i.Path == "$.pair[2]");
        Assert.NotEmpty(V("""{"pair":[1,1]}"""));
        Assert.NotEmpty(V("""{"has":["x"]}"""));
    }

    [Fact]
    public void AnUnsupportedKeywordIsReportedNeverSilentlyPassed()
    {
        var set = new JsonSchemas().Add("u.json", """{"type":"object","unevaluatedProperties":false}""");
        var issue = Assert.Single(set.Validate(JsonDocument.Parse("{}").RootElement, "u.json"));
        Assert.Contains("'unevaluatedProperties', which this validator doesn't support", issue.Message);
    }

    [Fact]
    public void AMissingReferenceIsAnIssueNotACrash()
    {
        var set = new JsonSchemas().Add("r.json", """{"$ref":"other.schema.json#/$defs/x"}""");
        Assert.Contains("can't be found", Assert.Single(set.Validate(JsonDocument.Parse("1").RootElement, "r.json")).Message);
        Assert.Throws<ArgumentException>(() => set.Validate(JsonDocument.Parse("1").RootElement, "nope.json"));
    }

    [Fact]
    public void SameComparesJsonValuesNotText()
    {
        static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;
        Assert.True(JsonSchemas.Same(J("""{"a":1,"b":[2.0]}"""), J("""{"b":[2],"a":1.0}""")));
        Assert.False(JsonSchemas.Same(J("1"), J("\"1\"")));
    }
}
