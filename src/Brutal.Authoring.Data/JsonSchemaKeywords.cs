using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Brutal.Authoring.Data;

// Keyword validation, reference registry and its existing cache ownership. BORROWED: JsonSchemas internals.
// One instance belongs to one facade; static regex and thread-local recursion/explanation state retain their
// original lifetimes. Discovery calls this same owner for branch checks, references and union facets.
internal sealed class JsonSchemaKeywords
{
    private readonly Dictionary<string, JsonElement> byUri = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Known =
    [
        "type", "enum", "const", "$ref", "$defs", "properties", "required", "additionalProperties", "patternProperties",
        "propertyNames", "minProperties", "maxProperties", "dependentRequired", "items", "prefixItems", "contains",
        "minItems", "maxItems", "uniqueItems", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "pattern", "allOf", "anyOf", "oneOf", "not", "if", "then", "else",
        "$schema", "$id", "$comment", "title", "description", "default", "examples", "deprecated", "readOnly", "writeOnly",
    ];

    /// <summary>Goes up each time a schema is added or replaced, so what was worked out from the old ones can be worked out again.</summary>
    internal long Generation { get; private set; }

    /// <summary>Adds a schema. It's found by its <c>$id</c>, and by <paramref name="name"/> (a file name such as
    /// <c>common.schema.json</c>) so references between files in one folder resolve.</summary>
    internal JsonSchemaKeywords Add(string name, string text)
    {
        var root = JsonDocument.Parse(text).RootElement.Clone();
        var id = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$id", out var i) && i.GetString() is { } s ? s : "urn:schema:" + name;
        byUri[Strip(id)] = root;
        byUri["urn:schema:" + name] = root;
        if (Uri.TryCreate(id, UriKind.Absolute, out var u)) byUri[Strip(new Uri(u, name).ToString())] = root;
        Embedded(root, Strip(id));
        lock (rememberedLock) { remembered.Clear(); older.Clear(); schemaKeys.Clear(); }
        Generation++;
        return this;
    }

    // Subschemas with their own $id are resources of their own, findable by that id (found by A026). Only places that
    // hold schemas are searched, never data such as an enum's values, which may contain an "$id" of its own.
    private void Embedded(JsonElement s, string baseUri)
    {
        if (s.ValueKind != JsonValueKind.Object) return;
        if (s.TryGetProperty("$id", out var id) && id.GetString() is { } text && text.Length > 0)
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var abs)) baseUri = Strip(abs.ToString());
            else if (Uri.TryCreate(baseUri, UriKind.Absolute, out var b) && !baseUri.StartsWith("urn:", StringComparison.Ordinal)) baseUri = Strip(new Uri(b, text).ToString());
            byUri.TryAdd(baseUri, s);
        }
        foreach (var key in new[] { "properties", "patternProperties", "$defs", "definitions", "dependentSchemas" })
            if (s.TryGetProperty(key, out var map) && map.ValueKind == JsonValueKind.Object)
                foreach (var p in map.EnumerateObject()) Embedded(p.Value, baseUri);
        foreach (var key in new[] { "items", "additionalProperties", "contains", "not", "if", "then", "else", "propertyNames" })
            if (s.TryGetProperty(key, out var sub)) Embedded(sub, baseUri);
        foreach (var key in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
            if (s.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var sub in list.EnumerateArray()) Embedded(sub, baseUri);
    }

    // A count keyword (minItems and the like) as a whole number: 2.0 is as good as 2 (found by A026).
    private static int Count(JsonElement e) => (int)Math.Clamp(e.GetDouble(), int.MinValue, int.MaxValue);

    // ECMAScript regular expressions, as JSON Schema specifies, run on .NET's engine: \d and \w mean ASCII digits and
    // word characters, and long Unicode property names (\p{Letter}) map to .NET's short ones. A pattern .NET can't read
    // is reported, never a crash (found by A026). Compiled patterns are cached.
    private static readonly Dictionary<string, Regex?> Patterns = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> PropertyNames = new(StringComparer.Ordinal)
    {
        ["Letter"] = "L", ["Lowercase_Letter"] = "Ll", ["Uppercase_Letter"] = "Lu", ["Titlecase_Letter"] = "Lt",
        ["Modifier_Letter"] = "Lm", ["Other_Letter"] = "Lo", ["Mark"] = "M", ["Number"] = "N", ["Decimal_Number"] = "Nd",
        ["Letter_Number"] = "Nl", ["Other_Number"] = "No", ["Punctuation"] = "P", ["Symbol"] = "S", ["Separator"] = "Z",
        ["Space_Separator"] = "Zs", ["Other"] = "C", ["Control"] = "Cc", ["digit"] = "Nd",
    };

    internal static Regex? Pattern(string pattern)
    {
        lock (Patterns)
        {
            if (Patterns.TryGetValue(pattern, out var cached)) return cached;
            var net = new System.Text.StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != '\\' || i + 1 >= pattern.Length) { net.Append(pattern[i]); continue; }
                var c = pattern[++i];
                switch (c)
                {
                    case 'd': net.Append("[0-9]"); break;
                    case 'D': net.Append("[^0-9]"); break;
                    case 'w': net.Append("[A-Za-z0-9_]"); break;
                    case 'W': net.Append("[^A-Za-z0-9_]"); break;
                    case 's': net.Append(@"[\s\uFEFF]"); break;   // ECMAScript whitespace includes U+FEFF
                    case 'S': net.Append(@"[^\s\uFEFF]"); break;
                    case 'p' or 'P' when i + 1 < pattern.Length && pattern[i + 1] == '{' && pattern.IndexOf('}', i) is var end and > 0:
                        var name = pattern[(i + 2)..end];
                        net.Append('\\').Append(c).Append('{').Append(PropertyNames.GetValueOrDefault(name, name.Replace("General_Category=", "").Replace("gc=", ""))).Append('}');
                        i = end;
                        break;
                    default: net.Append('\\').Append(c); break;
                }
            }
            Regex? compiled;
            try { compiled = new Regex(net.ToString(), RegexOptions.CultureInvariant); }
            catch (ArgumentException) { compiled = null; }
            Patterns[pattern] = compiled;
            return compiled;
        }
    }

    /// <summary>The schema added under <paramref name="name"/> (its file name or <c>$id</c>), or null.</summary>
    internal JsonElement? Find(string name) =>
        byUri.TryGetValue("urn:schema:" + name, out var s) || byUri.TryGetValue(Strip(name), out s) ? s : null;

    /// <summary>Everything wrong with <paramref name="instance"/> against the schema named <paramref name="schema"/>, as
    /// errors at JSON paths (<c>$.factories[3].inputs</c>). Empty when it's valid.</summary>
    internal IReadOnlyList<Issue> Validate(JsonElement instance, string schema)
    {
        var root = Find(schema) ?? throw new ArgumentException($"no schema called '{schema}' has been added", nameof(schema));
        var issues = new List<Issue>();
        Check(instance, root, BaseOf(root, schema), "$", issues);
        return issues;
    }

    // Why a value fits none of a union's alternatives, in the order A135 set out (docs/plan/union_reasons.md): a fault in the
    // rules first; then a field that says which form a record is; then a list of fixed choices; then the one alternative of the
    // value's kind, explained by its own first problem; then the kinds allowed; else a short summary. Never the alternative with
    // the fewest errors: that can be the wrong one. Validation has already refused it; this only explains. INVENTED, on A135.
    [ThreadStatic] private static bool explaining;
    [ThreadStatic] private static Issue? lastFault;   // the last fault in the rules met, even inside a scratch check (A137)

    private Issue WhyNone(JsonElement x, JsonElement alternatives, JsonElement s, string key, string baseUri, string path)
    {
        // While one explanation is worked out, a union inside it isn't explained too: that recursed without end on a cyclic rule
        // and doubled at each level of nesting (found by A137). Its refusal still counts; only the wording is short.
        if (explaining) return Issue.Error("doesn't fit any of its allowed forms", null, path);
        explaining = true;
        try { return Explain(x, alternatives, s, key, baseUri, path); }
        finally { explaining = false; }
    }

    private Issue Explain(JsonElement x, JsonElement alternatives, JsonElement s, string key, string baseUri, string path)
    {
        var count = alternatives.GetArrayLength();
        var forms = count <= 64 ? alternatives.EnumerateArray().Select(sub => Followed(sub, baseUri)).ToList() : [];   // past 64, no form is singled out (A137)
        lastFault = null;
        var problems = forms.Select(f => { var found = new List<Issue>(); Check(x, f.Schema, f.Base, path, found); return found; }).ToList();
        if ((problems.SelectMany(p => p).FirstOrDefault(i => RuleFault(i.Message)) ?? lastFault) is { } fault)
            return Short("its rules couldn't be checked: " + fault.Message, fault.Path ?? path);

        if (x.ValueKind == JsonValueKind.Object && Selector(forms) is { } selector && x.TryGetProperty(selector.Key, out var said))
        {
            var at = selector.Values.FindIndex(v => Same(v, said));
            if (at < 0) return Short($"{selector.Key} must be {Listed(selector.Values)}", Child(path, selector.Key));
            if (problems[at].FirstOrDefault() is { } first) return first with { Message = Fit(first.Message) };
        }

        if (Allowed(alternatives, s, key, baseUri) is { } allowed)
        {
            var values = allowed.Where(v => v.ValueKind != JsonValueKind.Null).ToList();
            var nothing = values.Count < allowed.Count;
            return Short(values.Count == 0 ? "must be nothing (null)" : $"must be {Listed(values)}{(nothing ? ", or nothing" : "")}", path);
        }

        var fitting = Enumerable.Range(0, forms.Count).Where(i => !(forms[i].Schema.ValueKind == JsonValueKind.Object && forms[i].Schema.TryGetProperty("type", out var ty) && !TypeOk(x, ty))).ToList();
        if (fitting.Count == 1 && problems[fitting[0]].FirstOrDefault() is { } only) return only with { Message = Fit(only.Message) };
        if (forms.Count > 0 && fitting.Count == 0 && Types(alternatives, baseUri) is { } kinds)
            return Short($"expected {string.Join(" or ", kinds.Select(Article))}, found {Kind(x)}", path);
        return Short($"doesn't fit any of its {count} allowed forms", path);
    }

    // The fixed values a union allows once the rest of its schema is applied: outer limits, sibling references and the other
    // union; and for oneOf, a value two alternatives both list fits neither (A132, A137). Nulls stay in, for the wording; the
    // inspector doesn't offer them (A134). Null when the union isn't all fixed values.
    internal List<JsonElement>? Allowed(JsonElement alternatives, JsonElement s, string key, string baseUri)
    {
        if (Tally(alternatives, baseUri) is not { } tally) return null;
        var rest = Without(s, key);
        var allowed = new List<JsonElement>();
        foreach (var (value, times) in tally)
            if ((key == "anyOf" || times == 1) && Valid(value, rest, baseUri, "$")) allowed.Add(value);
        return allowed.Count == 0 ? null : allowed;
    }

    private static JsonElement Without(JsonElement s, string key)
    {
        if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty(key, out _)) return s;
        var copy = new Dictionary<string, JsonElement>();
        foreach (var p in s.EnumerateObject()) if (p.Name != key) copy[p.Name] = p.Value;
        return JsonSerializer.SerializeToElement(copy);
    }

    private (JsonElement Schema, string Base) Followed(JsonElement s, string baseUri)
    {
        for (var hops = 0; hops < 32 && s.ValueKind == JsonValueKind.Object && s.TryGetProperty("$ref", out var r) && r.GetString() is { } refText && Resolve(refText, baseUri) is { } t; hops++)
        {
            if (s.EnumerateObject().Count() > 1) break;   // a reference with rules of its own beside it stays as it is
            (s, baseUri) = (t.Schema, t.Base);
        }
        return (s, baseUri);
    }

    private static bool RuleFault(string message) => message.StartsWith("the schema", StringComparison.Ordinal);   // refers to, uses, 's pattern (A137)

    // A field every form requires, with a different fixed value in each: it says which form a record means.
    private static (string Key, List<JsonElement> Values)? Selector(List<(JsonElement Schema, string Base)> forms)
    {
        if (forms.Count < 2 || forms.Any(f => f.Schema.ValueKind != JsonValueKind.Object || !f.Schema.TryGetProperty("properties", out _))) return null;
        foreach (var key in forms[0].Schema.GetProperty("properties").EnumerateObject().Select(p => p.Name))
        {
            var values = new List<JsonElement>();
            foreach (var (schema, _) in forms)
            {
                var required = schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array && req.EnumerateArray().Any(r => r.GetString() == key);
                if (!required || !schema.GetProperty("properties").TryGetProperty(key, out var p) || p.ValueKind != JsonValueKind.Object) { values.Clear(); break; }
                if (p.TryGetProperty("const", out var c)) values.Add(c);
                else if (p.TryGetProperty("enum", out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 1) values.Add(e[0]);
                else { values.Clear(); break; }
            }
            if (values.Count == forms.Count && !values.Where((v, i) => values.Skip(i + 1).Any(w => Same(v, w))).Any()) return (key, values);   // 1 and 1.0 are one value (A137)
        }
        return null;
    }

    // Up to three values, or how many there are.
    private static string Listed(List<JsonElement> values) =>
        values.Any(v => Shown(v).Length > 40) ? $"one of its {values.Count} choices" :   // too long to show whole, so none is cut (A137)
        values.Count == 0 ? "something else" : values.Count == 1 ? Shown(values[0])
        : values.Count <= 3 ? string.Join(", ", values.Take(values.Count - 1).Select(Shown)) + " or " + Shown(values[^1])
        : $"one of its {values.Count} choices, such as {Shown(values[0])}";

    private static string Shown(JsonElement v) => v.ValueKind == JsonValueKind.String ? $"'{v.GetString()}'" : v.GetRawText();

    private static Issue Short(string message, string path) => Issue.Error(Fit(message), null, path);

    // One line of at most 96 characters (A135): every control or line-separating character becomes a space, and a long line is
    // cut at a word, so a quoted value or a number isn't split (A137).
    private static string Fit(string message)
    {
        var spaced = new string(message.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c).ToArray());
        var line = string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= 96) return line;
        var cut = line.LastIndexOf(' ', 94);
        if (cut < 48) cut = char.IsHighSurrogate(line[94]) ? 94 : 95;
        return line[..cut] + "…";
    }

    // Every type the alternatives allow, when each one states its type; null when any doesn't.
    internal List<string>? Types(JsonElement alternatives, string baseUri)
    {
        var all = new List<string>();
        foreach (var sub in alternatives.EnumerateArray())
        {
            var (here, at) = (sub, baseUri);
            for (var hops = 0; hops < 32 && here.ValueKind == JsonValueKind.Object && here.TryGetProperty("$ref", out var r) && r.GetString() is { } refText && Resolve(refText, at) is { } t; hops++) (here, at) = (t.Schema, t.Base);
            if (here.ValueKind != JsonValueKind.Object || !here.TryGetProperty("type", out var type)) return null;
            if (type.ValueKind == JsonValueKind.String) all.Add(type.GetString()!);
            else if (type.ValueKind == JsonValueKind.Array) all.AddRange(type.EnumerateArray().Select(x => x.GetString()!));
            else return null;
        }
        return all.Distinct().ToList();
    }

    // Every fixed value the alternatives list, and how many alternatives list it, when each one is only a list (enum), a single
    // value (const) or nothing (type null), following $ref chains with their own base. Equal values written differently are one.
    private List<(JsonElement Value, int Times)>? Tally(JsonElement alternatives, string baseUri)
    {
        var order = new List<(JsonElement Value, int Times)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(JsonElement v)
        {
            var k = ValueKey(v);
            var i = k is not null ? (index.TryGetValue(k, out var found) ? found : -1) : order.FindIndex(o => Same(o.Value, v));
            if (i >= 0) { order[i] = (order[i].Value, order[i].Times + 1); return; }
            if (k is not null) index[k] = order.Count;
            order.Add((v, 1));
        }
        foreach (var sub in alternatives.EnumerateArray())
        {
            var (here, at) = (sub, baseUri);
            for (var hops = 0; hops < 32 && here.ValueKind == JsonValueKind.Object && here.TryGetProperty("$ref", out var r) && r.GetString() is { } refText && Resolve(refText, at) is { } t; hops++) (here, at) = (t.Schema, t.Base);
            if (here.ValueKind != JsonValueKind.Object || here.TryGetProperty("$ref", out _)) return null;
            var listed = new List<JsonElement>();
            if (here.TryGetProperty("enum", out var e) && e.ValueKind == JsonValueKind.Array) listed.AddRange(e.EnumerateArray());
            else if (here.TryGetProperty("const", out var c)) listed.Add(c);
            else if (here.TryGetProperty("type", out var nothing) && nothing.ValueKind == JsonValueKind.String && nothing.GetString() == "null" && here.EnumerateObject().Count() == 1) listed.Add(NullValue);
            else return null;
            // Only where its own alternative accepts it, with that alternative's type, limits and reference siblings (A132, A137).
            var plain = sub.ValueKind == JsonValueKind.Object && sub.EnumerateObject().All(q => q.Name is "enum" or "const" or "title" or "description");
            foreach (var v in listed) if (plain || Valid(v, sub, baseUri, "$")) Count(v);
        }
        return order.Count == 0 ? null : order;
    }

    private static readonly JsonElement NullValue = JsonDocument.Parse("null").RootElement.Clone();

    // A key equal for equal plain values (1 and 1.0; the same text however it was escaped); null for records and lists, which
    // are compared in full.
    private static string? ValueKey(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => "s" + v.GetString(),
        JsonValueKind.Number => "n" + (decimal.TryParse(v.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (d / 1.000000000000000000000000000m).ToString(CultureInfo.InvariantCulture) : v.GetRawText()),
        JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => v.GetRawText(),
        _ => null,
    };


    internal string BaseOf(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$id", out var i) && i.GetString() is { } s ? Strip(s) : "urn:schema:" + name;

    private static string Strip(string uri) => uri.Split('#')[0];

    // ---- the keywords ----

    private void Check(JsonElement x, JsonElement s, string baseUri, string path, List<Issue> issues)
    {
        if (s.ValueKind == JsonValueKind.True) return;
        if (s.ValueKind == JsonValueKind.False) { issues.Add(Issue.Error("nothing is allowed here", null, path)); return; }
        if (s.ValueKind != JsonValueKind.Object) return;
        if (s.TryGetProperty("$id", out var id) && id.GetString() is { } newBase)   // a nested $id moves the base
        {
            if (Uri.TryCreate(newBase, UriKind.Absolute, out var abs)) baseUri = Strip(abs.ToString());
            else if (Uri.TryCreate(baseUri, UriKind.Absolute, out var b) && !baseUri.StartsWith("urn:", StringComparison.Ordinal)) baseUri = Strip(new Uri(b, newBase).ToString());
        }

        foreach (var k in s.EnumerateObject())
            if (!Known.Contains(k.Name) && !k.Name.StartsWith("x-", StringComparison.Ordinal)) issues.Add(Issue.Error($"the schema uses '{k.Name}', which this validator doesn't support, so this value wasn't fully checked", null, path));

        if (s.TryGetProperty("$ref", out var r) && r.GetString() is { } refText)
        {
            if (Resolve(refText, baseUri) is { } target)
            {
                // The same reference again at the same place in the data, with nothing consumed between, would never end (found by
                // A132: a stack overflow). Refused as a schema fault instead.
                var step = (path, target.Base, refText);
                if (activeRefs.Contains(step)) issues.Add(lastFault = Issue.Error($"the schema refers to '{refText}' in a loop that never reaches the value", null, path));
                else
                {
                    activeRefs.Add(step);
                    try { Check(x, target.Schema, target.Base, path, issues); }
                    finally { activeRefs.Remove(step); }
                }
            }
            else issues.Add(lastFault = Issue.Error($"the schema refers to '{refText}', which can't be found", null, path));
        }
        if (s.TryGetProperty("type", out var type) && !TypeOk(x, type))
            issues.Add(Issue.Error($"expected {Describe(type)}, found {Kind(x)}", null, path));
        if (s.TryGetProperty("const", out var c) && !Same(x, c))
            issues.Add(Issue.Error($"must be {c.GetRawText()}", null, path));
        if (s.TryGetProperty("enum", out var e) && !e.EnumerateArray().Any(v => Same(x, v)))
            issues.Add(Issue.Error($"must be one of {string.Join(", ", e.EnumerateArray().Select(v => v.GetRawText()))}", null, path));

        switch (x.ValueKind)
        {
            case JsonValueKind.Object: Object(x, s, baseUri, path, issues); break;
            case JsonValueKind.Array: Array(x, s, baseUri, path, issues); break;
            case JsonValueKind.Number: Number(x.GetDouble(), s, path, issues); break;
            case JsonValueKind.String: String(x.GetString()!, s, baseUri, path, issues); break;
        }

        if (s.TryGetProperty("allOf", out var all)) foreach (var sub in all.EnumerateArray()) Check(x, sub, baseUri, path, issues);
        if (s.TryGetProperty("anyOf", out var any) && !any.EnumerateArray().Any(sub => Valid(x, sub, baseUri, path)))
            issues.Add(WhyNone(x, any, s, "anyOf", baseUri, path));
        if (s.TryGetProperty("oneOf", out var one))
        {
            var n = one.EnumerateArray().Count(sub => Valid(x, sub, baseUri, path));
            if (n == 0) issues.Add(WhyNone(x, one, s, "oneOf", baseUri, path));
            else if (n > 1) issues.Add(Issue.Error($"fits {n} of its choices; it must fit exactly one", null, path));
        }
        if (s.TryGetProperty("not", out var not) && Valid(x, not, baseUri, path))
            issues.Add(Issue.Error("matches a shape that isn't allowed here", null, path));
        if (s.TryGetProperty("if", out var cond))
        {
            if (Valid(x, cond, baseUri, path)) { if (s.TryGetProperty("then", out var then)) Check(x, then, baseUri, path, issues); }
            else if (s.TryGetProperty("else", out var otherwise)) Check(x, otherwise, baseUri, path, issues);
        }
    }

    // ---- remembering list items ----
    // A list item's issues depend only on its own value, its schema and that schema's base URI: no supported keyword
    // looks outside the value (there's no unevaluatedProperties or $dynamicRef). So each item's result is remembered by
    // those, with paths relative to the item, and after a one-cell edit in a 10,000-row list only the edited row is
    // checked again (A033 found every edit re-validating the whole file). Adding a schema forgets everything, since a
    // $ref may now resolve differently. INVENTED.
    private readonly Dictionary<string, string> schemaKeys = new(StringComparer.Ordinal);
    // Two generations: when one fills it becomes the older one, still consulted, and what's used again moves up. Clearing it
    // all when full made a file just over the limit re-check nearly every row on every edit: 50 ms became 4 s (found by A088).
    private Dictionary<string, Issue[]> remembered = new(StringComparer.Ordinal), older = new(StringComparer.Ordinal);
    private readonly Lock rememberedLock = new();
    private const int RememberedLimit = 100_000;

    private void Remember(string key, Issue[] issues)
    {
        if (remembered.Count >= RememberedLimit)
        {
            older = remembered;
            remembered = new(StringComparer.Ordinal);
        }
        remembered[key] = issues;
    }

    private string SchemaKey(JsonElement s, string baseUri)
    {
        var text = baseUri + '\0' + s.GetRawText();
        lock (rememberedLock)
        {
            if (!schemaKeys.TryGetValue(text, out var key)) schemaKeys[text] = key = schemaKeys.Count.ToString(CultureInfo.InvariantCulture) + ":";
            return key;
        }
    }

    private void Remembered(JsonElement x, JsonElement s, string baseUri, string path, string schemaKey, List<Issue> issues)
    {
        if (x.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) { Check(x, s, baseUri, path, issues); return; }
        var key = schemaKey + x.GetRawText();
        Issue[]? found;
        lock (rememberedLock)
            if (!remembered.TryGetValue(key, out found) && older.TryGetValue(key, out found)) Remember(key, found);
        if (found is null)
        {
            var fresh = new List<Issue>();
            // A fresh check starts its paths at "$" again, so it follows references with its own set: a loop has to stay at one value.
            var outer = refsInFlight;
            refsInFlight = [];
            try { Check(x, s, baseUri, "$", fresh); }
            finally { refsInFlight = outer; }
            found = [.. fresh];
            lock (rememberedLock) Remember(key, found);
        }
        foreach (var i in found) issues.Add(i with { Path = i.Path is { } p ? path + p[1..] : path });
    }

    // The references being followed right now, by place in the data: see Check's $ref.
    [ThreadStatic] private static HashSet<(string, string, string)>? refsInFlight;
    private static HashSet<(string, string, string)> activeRefs => refsInFlight ??= [];

    internal bool Valid(JsonElement x, JsonElement s, string baseUri, string path)
    {
        var scratch = new List<Issue>();
        Check(x, s, baseUri, path, scratch);
        return scratch.Count == 0;
    }

    private void Object(JsonElement x, JsonElement s, string baseUri, string path, List<Issue> issues)
    {
        var props = x.EnumerateObject().ToList();
        if (s.TryGetProperty("required", out var req))
            foreach (var name in req.EnumerateArray().Select(v => v.GetString()!).Where(n => !x.TryGetProperty(n, out _)))
                issues.Add(Issue.Error($"'{name}' is required", null, path));
        if (s.TryGetProperty("minProperties", out var minP) && props.Count < Count(minP))
            issues.Add(Issue.Error($"needs at least {Count(minP)} entr{(Count(minP) == 1 ? "y" : "ies")}", null, path));
        if (s.TryGetProperty("maxProperties", out var maxP) && props.Count > Count(maxP))
            issues.Add(Issue.Error($"may have at most {Count(maxP)} entries", null, path));
        if (s.TryGetProperty("dependentRequired", out var dep))
            foreach (var d in dep.EnumerateObject().Where(d => x.TryGetProperty(d.Name, out _)))
                foreach (var need in d.Value.EnumerateArray().Select(v => v.GetString()!).Where(n => !x.TryGetProperty(n, out _)))
                    issues.Add(Issue.Error($"'{need}' is required when '{d.Name}' is given", null, path));
        var defined = s.TryGetProperty("properties", out var p) ? p : default;
        var patterns = new List<(Regex Re, JsonElement Schema)>();
        if (s.TryGetProperty("patternProperties", out var pp))
            foreach (var q in pp.EnumerateObject())
                if (Pattern(q.Name) is { } re) patterns.Add((re, q.Value));
                else issues.Add(Issue.Error($"the schema's property pattern {q.Name} can't be read by this validator, so names here weren't checked against it", null, path));
        foreach (var prop in props)
        {
            var at = Child(path, prop.Name);
            if (s.TryGetProperty("propertyNames", out var names))
            {
                using var nameDoc = JsonDocument.Parse(JsonSerializer.Serialize(prop.Name));
                // Says why, as a value's refusal does: a name refused with no reason can't be corrected (found by A110).
                var refusals = new List<Issue>();
                Check(nameDoc.RootElement, names, baseUri, at, refusals);
                if (refusals.Count > 0) issues.Add(Issue.Error($"'{prop.Name}' isn't an allowed name here: {refusals[0].Message}", null, at));
            }
            var matched = false;
            if (defined.ValueKind == JsonValueKind.Object && defined.TryGetProperty(prop.Name, out var ps)) { matched = true; Check(prop.Value, ps, baseUri, at, issues); }
            foreach (var (re, sch) in patterns.Where(q => q.Re.IsMatch(prop.Name))) { matched = true; Check(prop.Value, sch, baseUri, at, issues); }
            if (!matched && s.TryGetProperty("additionalProperties", out var extra))
            {
                if (extra.ValueKind == JsonValueKind.False) issues.Add(Issue.Error($"'{prop.Name}' isn't a known field here", null, at));
                else Check(prop.Value, extra, baseUri, at, issues);
            }
        }
    }

    private void Array(JsonElement x, JsonElement s, string baseUri, string path, List<Issue> issues)
    {
        var items = x.EnumerateArray().ToList();
        if (s.TryGetProperty("minItems", out var min) && items.Count < Count(min))
            issues.Add(Issue.Error($"needs at least {Count(min)} item{(Count(min) == 1 ? "" : "s")}", null, path));
        if (s.TryGetProperty("maxItems", out var max) && items.Count > Count(max))
            issues.Add(Issue.Error($"may have at most {Count(max)} items", null, path));
        if (s.TryGetProperty("uniqueItems", out var u) && u.ValueKind == JsonValueKind.True)
            for (var i = 0; i < items.Count; i++)
                for (var j = 0; j < i; j++)
                    if (Same(items[i], items[j])) { issues.Add(Issue.Error($"item {i} repeats item {j}; each must be different", null, path)); i = items.Count; break; }
        var first = 0;
        if (s.TryGetProperty("prefixItems", out var prefix))
            foreach (var (sch, i) in prefix.EnumerateArray().Select((sch, i) => (sch, i)).Where(t => t.i < items.Count))
            {
                Check(items[i], sch, baseUri, $"{path}[{i}]", issues);
                first = i + 1;
            }
        if (s.TryGetProperty("items", out var each))
        {
            var kind = each.ValueKind == JsonValueKind.Object ? SchemaKey(each, baseUri) : null;
            for (var i = first; i < items.Count; i++)
                if (each.ValueKind == JsonValueKind.False) issues.Add(Issue.Error("no more items are allowed here", null, $"{path}[{i}]"));
                else if (kind is null) Check(items[i], each, baseUri, $"{path}[{i}]", issues);
                else Remembered(items[i], each, baseUri, $"{path}[{i}]", kind, issues);
        }
        if (s.TryGetProperty("contains", out var contains) && !items.Select((it, i) => Valid(it, contains, baseUri, $"{path}[{i}]")).Any(v => v))
            issues.Add(Issue.Error("needs at least one item of the required shape", null, path));
    }

    private static void Number(double v, JsonElement s, string path, List<Issue> issues)
    {
        string N(JsonElement e) => e.GetRawText();
        if (s.TryGetProperty("minimum", out var min) && v < min.GetDouble()) issues.Add(Issue.Error($"must be at least {N(min)}", null, path));
        if (s.TryGetProperty("maximum", out var max) && v > max.GetDouble()) issues.Add(Issue.Error($"must be at most {N(max)}", null, path));
        if (s.TryGetProperty("exclusiveMinimum", out var emin) && v <= emin.GetDouble()) issues.Add(Issue.Error($"must be more than {N(emin)}", null, path));
        if (s.TryGetProperty("exclusiveMaximum", out var emax) && v >= emax.GetDouble()) issues.Add(Issue.Error($"must be less than {N(emax)}", null, path));
        if (s.TryGetProperty("multipleOf", out var mul))
        {
            var q = v / mul.GetDouble();
            // When the quotient overflows, a whole number is still a multiple of m exactly when 1/m is whole (1e308 of 0.5),
            // and otherwise isn't (1e308 of 0.123456789): both from the official suite (A026).
            var whole = double.IsFinite(q) ? Math.Abs(q - Math.Round(q)) <= 1e-9
                : Math.Abs(v % 1) == 0 && 1 / mul.GetDouble() is var r && double.IsFinite(r) && Math.Abs(r - Math.Round(r)) <= 1e-9;
            if (!whole)
                issues.Add(Issue.Error($"must be a multiple of {N(mul)}", null, path));
        }
    }

    private static void String(string v, JsonElement s, string baseUri, string path, List<Issue> issues)
    {
        var length = v.EnumerateRunes().Count();   // characters, not UTF-16 units (2020-12)
        if (s.TryGetProperty("minLength", out var min) && length < Count(min))
            issues.Add(Issue.Error(Count(min) == 1 ? "can't be empty" : $"must be at least {Count(min)} characters", null, path));
        if (s.TryGetProperty("maxLength", out var max) && length > Count(max))
            issues.Add(Issue.Error($"must be at most {Count(max)} characters", null, path));
        if (s.TryGetProperty("pattern", out var pat))
        {
            if (Pattern(pat.GetString()!) is not { } re)
                issues.Add(Issue.Error($"the schema's pattern {pat.GetString()} can't be read by this validator, so this text wasn't checked against it", null, path));
            else if (!re.IsMatch(v))
                issues.Add(Issue.Error($"'{v}' doesn't fit the pattern {pat.GetString()}", null, path));
        }
    }

    // ---- helpers ----

    internal (JsonElement Schema, string Base)? Resolve(string reference, string baseUri)
    {
        var hash = reference.IndexOf('#');
        var file = hash < 0 ? reference : reference[..hash];
        var pointer = hash < 0 ? "" : reference[(hash + 1)..];
        string docUri;
        if (file.Length == 0) docUri = baseUri;
        else if (Uri.TryCreate(file, UriKind.Absolute, out var abs)) docUri = Strip(abs.ToString());
        else if (Uri.TryCreate(baseUri, UriKind.Absolute, out var b) && !baseUri.StartsWith("urn:", StringComparison.Ordinal)) docUri = Strip(new Uri(b, file).ToString());
        else docUri = "urn:schema:" + Path.GetFileName(file);
        if (!byUri.TryGetValue(docUri, out var doc) && !byUri.TryGetValue("urn:schema:" + Path.GetFileName(file), out doc)) return null;
        var node = doc;
        foreach (var part in pointer.Length == 0 ? [] : pointer.TrimStart('/').Length == pointer.Length ? pointer.Split('/') : pointer[1..].Split('/'))   // "" tokens are real keys (A026)
        {
            var key = Uri.UnescapeDataString(part).Replace("~1", "/").Replace("~0", "~");
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var next)) node = next;
            else if (node.ValueKind == JsonValueKind.Array && int.TryParse(key, out var idx) && idx < node.GetArrayLength()) node = node[idx];
            else return null;
        }
        return (node, docUri);
    }

    private static bool TypeOk(JsonElement x, JsonElement type) => type.ValueKind == JsonValueKind.Array
        ? type.EnumerateArray().Any(t => Is(x, t.GetString()!))
        : Is(x, type.GetString()!);

    private static bool Is(JsonElement x, string type) => type switch
    {
        "object" => x.ValueKind == JsonValueKind.Object,
        "array" => x.ValueKind == JsonValueKind.Array,
        "string" => x.ValueKind == JsonValueKind.String,
        "number" => x.ValueKind == JsonValueKind.Number,
        "integer" => x.ValueKind == JsonValueKind.Number && Math.Abs(x.GetDouble() % 1) == 0,   // 2.0 is an integer (2020-12)
        "boolean" => x.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => x.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static string Describe(JsonElement type) => type.ValueKind == JsonValueKind.Array
        ? string.Join(" or ", type.EnumerateArray().Select(t => Article(t.GetString()!))) : Article(type.GetString()!);

    private static string Article(string t) => t switch
    {
        "object" => "an object", "array" => "a list", "string" => "text", "number" => "a number",
        "integer" => "a whole number", "boolean" => "true or false", "null" => "null", _ => t,
    };

    private static string Kind(JsonElement x) => x.ValueKind switch
    {
        JsonValueKind.Object => "an object", JsonValueKind.Array => "a list", JsonValueKind.String => "text",
        JsonValueKind.Number => x.GetRawText(), JsonValueKind.True or JsonValueKind.False => x.GetRawText(), _ => "null",
    };

    /// <summary>Whether two JSON values are equal as JSON: numbers by value, objects regardless of key order.</summary>
    internal static bool Same(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            // Exact value where decimal holds both (9007199254740993 isn't 9007199254740992; found by A056), else exactly on the
            // digits and the power of ten: as doubles, 1e400 and 2e400 are both infinity and an edit was lost (found by A202).
            JsonValueKind.Number => a.TryGetDecimal(out var da) && b.TryGetDecimal(out var db) ? da == db
                : CheckSnapshot.ExactNumber(a.GetRawText()) == CheckSnapshot.ExactNumber(b.GetRawText()),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(t => Same(t.First, t.Second)),
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var q) && Same(p.Value, q)),
            _ => true,
        };
    }

    private static string Child(string path, string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") ? $"{path}.{name}" : $"{path}['{name}']";
}
