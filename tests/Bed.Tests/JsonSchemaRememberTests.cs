using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>The validator remembers each list item's result, so an edit re-checks only what changed. Remembering must
/// never change an answer: a reused validator says exactly what a fresh one says.</summary>
public sealed class JsonSchemaRememberTests
{
    private const string Schema = """
    {
      "$defs": {
        "row": {
          "type": "object",
          "required": ["id", "score"],
          "additionalProperties": false,
          "properties": {
            "id": { "type": "string", "pattern": "^r[0-9]+$" },
            "score": { "type": "number", "minimum": 0, "maximum": 100 },
            "kind": { "enum": ["a", "b"] },
            "tags": { "type": "array", "items": { "type": "string", "minLength": 1 } }
          },
          "if": { "properties": { "kind": { "const": "b" } }, "required": ["kind"] },
          "then": { "required": ["tags"] }
        }
      },
      "type": "object",
      "properties": { "rows": { "type": "array", "items": { "$ref": "#/$defs/row" }, "uniqueItems": true } }
    }
    """;

    private static string Said(IReadOnlyList<Issue> issues) => string.Join("\n", issues.Select(i => $"{i.Path} {i.Message}"));

    private static JsonElement Data(JsonNode node) => JsonSerializer.SerializeToElement(node);

    [Fact]
    public void A_reused_validator_agrees_with_a_fresh_one_across_many_edits()
    {
        var reused = new JsonSchemas().Add("t.schema.json", Schema);
        var random = new Random(4417);
        var rows = new JsonArray();
        for (var i = 0; i < 40; i++) rows.Add(new JsonObject { ["id"] = "r" + i, ["score"] = i, ["kind"] = "a" });
        var root = new JsonObject { ["rows"] = rows };
        JsonNode[] bad = [JsonValue.Create(-5)!, JsonValue.Create("x")!, JsonValue.Create(150)!, new JsonArray(""), JsonValue.Create("b")!];
        string[] fields = ["score", "id", "score", "tags", "kind"];
        for (var step = 0; step < 300; step++)
        {
            var row = (JsonObject)rows[random.Next(rows.Count)]!;
            var f = random.Next(fields.Length);
            if (random.Next(3) == 0) row.Remove(fields[f]);
            else row[fields[f]] = random.Next(2) == 0 ? bad[f].DeepClone() : (f == 1 ? JsonValue.Create("r" + random.Next(99)) : f == 3 ? new JsonArray("ok") : f == 4 ? JsonValue.Create("a") : JsonValue.Create(random.Next(100)));
            var data = Data(root);
            var fresh = new JsonSchemas().Add("t.schema.json", Schema);
            Assert.Equal(Said(fresh.Validate(data, "t.schema.json")), Said(reused.Validate(data, "t.schema.json")));
        }
    }

    [Fact]
    public void A_remembered_row_reports_at_its_own_place()
    {
        var schemas = new JsonSchemas().Add("t.schema.json", Schema);
        var row = """{ "id": "r1", "score": 1, "kind": "b", "tags": ["", "ok"] }""";
        var once = JsonDocument.Parse($$"""{ "rows": [ {{row}} ] }""").RootElement;
        var twice = JsonDocument.Parse($$"""{ "rows": [ { "id": "r0", "score": 0 }, {{row}} ] }""").RootElement;
        Assert.Equal("$.rows[0].tags[0]", Assert.Single(schemas.Validate(once, "t.schema.json")).Path);
        Assert.Equal("$.rows[1].tags[0]", Assert.Single(schemas.Validate(twice, "t.schema.json")).Path);
    }

    [Fact]
    public void Adding_a_schema_forgets_what_was_remembered()
    {
        const string list = """{ "type": "array", "items": { "$ref": "item.schema.json" } }""";
        var schemas = new JsonSchemas().Add("list.schema.json", list).Add("item.schema.json", """{ "type": "object", "required": ["a"] }""");
        var data = JsonDocument.Parse("""[ { "b": 1 } ]""").RootElement;
        Assert.Single(schemas.Validate(data, "list.schema.json"));
        schemas.Add("item.schema.json", """{ "type": "object", "required": ["b"] }""");
        Assert.Empty(schemas.Validate(data, "list.schema.json"));
    }

    [Fact]
    public void The_same_text_under_another_schema_is_checked_again()
    {
        var schemas = new JsonSchemas()
            .Add("one.schema.json", """{ "type": "array", "items": { "type": "object", "required": ["a"] } }""")
            .Add("two.schema.json", """{ "type": "array", "items": { "type": "object", "required": ["b"] } }""");
        var data = JsonDocument.Parse("""[ { "a": 1 } ]""").RootElement;
        Assert.Empty(schemas.Validate(data, "one.schema.json"));
        Assert.Single(schemas.Validate(data, "two.schema.json"));
    }
}
