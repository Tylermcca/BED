using System.Text.Json;

namespace Brutal.Authoring.Data;

/// <summary>
/// A set of JSON Schemas (draft 2020-12) that can refer to each other, and a validator for them. It's self-authored, with
/// no packages, so it puts no licence or fee on anyone using the tools. The binary JsonSchema.Net release carries a
/// maintenance-fee agreement, and the Roslyn-based validators are heavy (2026-10-31).
/// <para>
/// Supported, the keywords real game data uses (counted across a real game's 60 schemas):
/// <c>type enum const $ref $defs properties required additionalProperties patternProperties propertyNames
/// minProperties maxProperties dependentRequired items prefixItems contains minItems maxItems uniqueItems minimum
/// maximum exclusiveMinimum exclusiveMaximum multipleOf minLength maxLength pattern allOf anyOf oneOf not if then
/// else</c>, plus the annotations it ignores (<c>$schema $id $comment title description default examples
/// deprecated readOnly writeOnly</c>), and any <c>x-</c> keyword, the convention for a tool's own notes (such as the
/// editor's <c>x-label</c>). Any other keyword is reported, never silently passed.
/// </para>
/// BORROWED: the keywords' meanings, from the 2020-12 specification. INVENTED: the plain-words messages, each at the
/// JSON path it's about.
/// </summary>
public sealed class JsonSchemas
{
    private readonly JsonSchemaKeywords keywords = new();
    private readonly JsonSchemaDiscovery discovery;

    /// <summary>Creates an empty schema set with its own registry and item-validation cache.</summary>
    public JsonSchemas() => discovery = new(keywords);

    /// <summary>Goes up each time a schema is added or replaced, invalidating derived field descriptions.</summary>
    public long Generation => keywords.Generation;

    /// <summary>Adds a schema under its file name and $id, and forgets previous item validation results.</summary>
    public JsonSchemas Add(string name, string text)
    {
        keywords.Add(name, text);
        return this;
    }

    /// <summary>Every schema in <paramref name="folder"/>: each file matching <paramref name="pattern"/> (all <c>*.json</c>
    /// files unless a folder also holds data, when it's <c>*.schema.json</c>).</summary>
    public static JsonSchemas FromFolder(string folder, string pattern = "*.json")
    {
        var set = new JsonSchemas();
        foreach (var f in Directory.EnumerateFiles(folder, pattern).Order(StringComparer.Ordinal)) set.Add(Path.GetFileName(f), File.ReadAllText(f));
        return set;
    }

    /// <summary>The schema added under a file name or $id, or null.</summary>
    public JsonElement? Find(string name) => keywords.Find(name);

    /// <summary>Ordered errors at JSON paths for the named schema; empty when valid.</summary>
    public IReadOnlyList<Issue> Validate(JsonElement instance, string schema) => keywords.Validate(instance, schema);

    /// <summary>The schemas applying at a JSON Pointer, including references and the instance's active branches.</summary>
    public IReadOnlyList<JsonElement> At(JsonElement instance, string schema, string pointer) =>
        discovery.At(instance, schema, pointer);

    /// <summary>Schemas for named children, walking to their parent only once.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> Children(JsonElement instance, string schema, string pointer, IEnumerable<string> keys) =>
        discovery.Children(instance, schema, pointer, keys);

    /// <summary>Whether two JSON values are equal as JSON: numbers by value, objects regardless of key order.</summary>
    public static bool Same(JsonElement a, JsonElement b) => JsonSchemaKeywords.Same(a, b);
}
