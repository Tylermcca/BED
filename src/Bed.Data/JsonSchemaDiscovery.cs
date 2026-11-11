using System.Globalization;
using System.Text.Json;

namespace Bed.Data;

// Branch-sensitive field discovery, separate from keyword validation. BORROWED: the existing JsonSchemas walk,
// moved without changing its branches or ordering. It reads the same per-facade keyword/registry owner.
internal sealed class JsonSchemaDiscovery(JsonSchemaKeywords keywords)
{
    /// <summary>
    /// The schemas that apply to the value at <paramref name="pointer"/> (a JSON Pointer) in <paramref name="instance"/>,
    /// for an editor to describe a field: following <c>properties</c>, <c>patternProperties</c>,
    /// <c>additionalProperties</c>, <c>prefixItems</c>, <c>items</c>, <c>$ref</c>, <c>allOf</c> and the branch of
    /// <c>if</c>/<c>then</c>/<c>else</c> the instance takes. Empty when nothing describes it. Each applies in full,
    /// so a field's type, range or choices may come from any of them.
    /// </summary>
    internal IReadOnlyList<JsonElement> At(JsonElement instance, string schema, string pointer) =>
        Walk(instance, schema, pointer) is { } w ? w.Schemas.Select(c => c.Item1).ToList() : [];

    /// <summary>
    /// The schemas for each named child of the object at <paramref name="pointer"/>, as <see cref="At"/> would give for
    /// <c>pointer/key</c>, but walking to the object once rather than once per field (per-field walks
    /// from the root would make a 10,000-row table take seconds to filter or sort).
    /// </summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> Children(JsonElement instance, string schema, string pointer, IEnumerable<string> keys)
    {
        var result = new Dictionary<string, IReadOnlyList<JsonElement>>(StringComparer.Ordinal);
        var w = Walk(instance, schema, pointer);
        foreach (var key in keys)
            result[key] = w is { } at && Step(at.Schemas, at.Value, key) is { } next ? next.Schemas.Select(c => c.Item1).ToList() : [];
        return result;
    }

    // Walks from the root to a pointer: the schemas in force there (with their base URIs) and the value, or null.
    private (List<(JsonElement, string)> Schemas, JsonElement Value)? Walk(JsonElement instance, string schema, string pointer)
    {
        var root = keywords.Find(schema) ?? throw new ArgumentException($"no schema called '{schema}' has been added", nameof(schema));
        (List<(JsonElement, string)> Schemas, JsonElement Value)? at = (Expand(root, keywords.BaseOf(root, schema), instance), instance);
        var parts = pointer.Length == 0 ? [] : pointer[1..].Split('/').Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToList();
        foreach (var part in parts)
        {
            at = Step(at.Value.Schemas, at.Value.Value, part);
            if (at is null) return null;
        }
        return at;
    }

    // One step down: from the schemas in force for a value to those for its child named part.
    private (List<(JsonElement, string)> Schemas, JsonElement Value)? Step(List<(JsonElement, string)> current, JsonElement value, string part)
    {
        JsonElement next;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(part, out next)) { }
        else if (value.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < value.GetArrayLength()) next = ListIndex.Item(value, idx);
        else if (value.ValueKind == JsonValueKind.Object) next = default;   // a field the object doesn't have yet still has a schema (found by the table's fill tests)
        else return null;
        var found = new List<(JsonElement, string)>();
        foreach (var (s, b) in current)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var matched = false;
                if (s.TryGetProperty("properties", out var props) && props.TryGetProperty(part, out var ps)) { found.Add((ps, b)); matched = true; }
                if (s.TryGetProperty("patternProperties", out var pp))
                    foreach (var q in pp.EnumerateObject().Where(q => JsonSchemaKeywords.Pattern(q.Name)?.IsMatch(part) == true)) { found.Add((q.Value, b)); matched = true; }
                if (!matched && s.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind == JsonValueKind.Object) found.Add((extra, b));
            }
            else
            {
                var i = int.Parse(part, CultureInfo.InvariantCulture);
                if (s.TryGetProperty("prefixItems", out var prefix) && i < prefix.GetArrayLength()) found.Add((prefix[i], b));
                else if (s.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object) found.Add((items, b));
            }
        }
        return (found.SelectMany(f => Expand(f.Item1, f.Item2, next)).ToList(), next);
    }

    // A schema plus everything it pulls in for this value: $ref targets, allOf members, and the if/then/else branch taken.
    private List<(JsonElement, string)> Expand(JsonElement s, string baseUri, JsonElement value, int depth = 0)
    {
        var all = new List<(JsonElement, string)> { (s, baseUri) };
        if (depth > 32 || s.ValueKind != JsonValueKind.Object) return all;
        if (s.TryGetProperty("$ref", out var r) && r.GetString() is { } refText && keywords.Resolve(refText, baseUri) is { } t)
            all.AddRange(Expand(t.Schema, t.Base, value, depth + 1));
        if (s.TryGetProperty("allOf", out var allOf))
            foreach (var sub in allOf.EnumerateArray()) all.AddRange(Expand(sub, baseUri, value, depth + 1));
        if (value.ValueKind != JsonValueKind.Undefined && s.TryGetProperty("if", out var cond))   // a field not there yet takes no branch
        {
            var branch = keywords.Valid(value, cond, baseUri, "$") ? "then" : "else";
            if (s.TryGetProperty(branch, out var taken)) all.AddRange(Expand(taken, baseUri, value, depth + 1));
        }
        // Alternatives (found by A129): a field's choices and help can sit inside one of them. When every alternative is a list of
        // fixed values, the field offers those the whole schema here allows (A132: overlaps, siblings and outer limits filter them).
        // A record that matches exactly one alternative takes that alternative's fields, like the if/then branch taken. A plain
        // value never takes one alternative's limits as the field's: another may allow more (A132). None or several matching adds
        // nothing. Validation still checks the whole schema. INVENTED, on JSON Schema's anyOf/oneOf.
        foreach (var key in new[] { "anyOf", "oneOf" })
        {
            if (!s.TryGetProperty(key, out var alternatives) || alternatives.ValueKind != JsonValueKind.Array) continue;
            if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                if (keywords.Allowed(alternatives, s, key, baseUri) is { } allowed)
                {
                    var offered = allowed.Where(v => v.ValueKind != JsonValueKind.Null).ToList();
                    if (offered.Count > 0) all.Add((JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["enum"] = offered }), baseUri));
                }
                // The kinds of value the alternatives allow, when each says: a field that is a whole number or "none" is still a whole
                // number when it holds one. Not when whole and any number both appear: a whole-number box would refuse 0.5 (A132).
                // Only the kind the value already is: "pending" in a text-or-number field stays text (A132).
                else if (keywords.Types(alternatives, baseUri) is { } types && !(types.Contains("integer") && types.Contains("number")))
                {
                    var own = value.ValueKind switch
                    {
                        JsonValueKind.String => types.Where(k => k == "string"),
                        JsonValueKind.Number => types.Where(k => k is "number" or "integer"),
                        JsonValueKind.True or JsonValueKind.False => types.Where(k => k == "boolean"),
                        _ => [],
                    };
                    if (own.ToList() is [var kind]) all.Add((JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["type"] = kind }), baseUri));
                }
                continue;
            }
            var matching = alternatives.EnumerateArray().Where(sub => keywords.Valid(value, sub, baseUri, "$")).Take(2).ToList();
            if (matching.Count == 1) all.AddRange(Expand(matching[0], baseUri, value, depth + 1));
        }
        return all;
    }

}
