using System.Text.Json;
using Bed.Data;

namespace Bed.Launcher;

// INVENTED read-only comparison; key policy follows A349's explicit review decision.
internal static class StructuralCompare
{
    internal sealed record Change(string Kind, string? BeforeAt, string? AfterAt, string? Before, string? After);
    internal sealed record ListChoice(string Pointer, string? Key, IReadOnlyList<string> Fields, string Mode);
    internal sealed record Result(IReadOnlyList<Change> Changes, IReadOnlyList<string> Problems, IReadOnlyList<ListChoice> Lists)
    {
        internal bool Complete => Problems.Count == 0;
    }

    internal static Result Compare(DataDocument before, DataDocument after, IReadOnlyDictionary<string, string?>? keys = null)
    {
        var changes = new List<Change>();
        var problems = new List<string>();
        var lists = new List<ListChoice>();
        Walk(before.Root, after.Root, "", "", 0);
        return new(changes, problems, lists);

        void Add(string kind, JsonElement? a, JsonElement? b, string? ap, string? bp) =>
            changes.Add(new(kind, ap, bp, a?.GetRawText(), b?.GetRawText()));

        void Walk(JsonElement a, JsonElement b, string ap, string bp, int depth)
        {
            if (depth > 128) { problems.Add($"{bp}: comparison depth exceeds 128; result is incomplete."); return; }
            if (a.ValueKind != b.ValueKind) { Add("changed", a, b, ap, bp); return; }
            if (a.ValueKind == JsonValueKind.Object)
            {
                var aa = Properties(a, ap); var bb = Properties(b, bp);
                if (aa is null || bb is null) return;
                foreach (var (name, value) in aa)
                {
                    var left = ap + Pointer.Join([name]); var right = bp + Pointer.Join([name]);
                    if (bb.TryGetValue(name, out var other)) Walk(value, other, left, right, depth + 1);
                    else Add("removed", value, null, left, null);
                }
                foreach (var (name, value) in bb)
                    if (!aa.ContainsKey(name)) Add("added", null, value, null, bp + Pointer.Join([name]));
                return;
            }
            if (a.ValueKind == JsonValueKind.Array)
            {
                var aa = a.EnumerateArray().ToArray(); var bb = b.EnumerateArray().ToArray();
                var fields = aa.Concat(bb).Where(x => x.ValueKind == JsonValueKind.Object)
                    .SelectMany(x => x.EnumerateObject()).Where(x => x.Value.ValueKind == JsonValueKind.String)
                    .Select(x => x.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                string? key = null;
                var explicitChoice = keys is not null && keys.TryGetValue(bp, out key);
                if (!explicitChoice && aa.Concat(bb).Any(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id"u8, out _))) key = "id";
                if (key is not null)
                {
                    var left = Index(aa, key, ap); var right = Index(bb, key, bp);
                    lists.Add(new(bp, key, fields, left is null || right is null ? "incomplete key" : "keyed; order ignored"));
                    if (left is null || right is null) return;
                    foreach (var (id, i) in left)
                        if (right.TryGetValue(id, out var j)) Walk(aa[i], bb[j], ap + "/" + i, bp + "/" + j, depth + 1);
                        else Add("removed", aa[i], null, ap + "/" + i, null);
                    foreach (var (id, j) in right)
                        if (!left.ContainsKey(id)) Add("added", null, bb[j], null, bp + "/" + j);
                }
                else
                {
                    lists.Add(new(bp, null, fields, "positional; no identity matching"));
                    for (var i = 0; i < Math.Max(aa.Length, bb.Length); i++)
                        if (i >= aa.Length) Add("added", null, bb[i], null, bp + "/" + i);
                        else if (i >= bb.Length) Add("removed", aa[i], null, ap + "/" + i, null);
                        else Walk(aa[i], bb[i], ap + "/" + i, bp + "/" + i, depth + 1);
                }
                return;
            }
            if (!JsonSchemas.Same(a, b)) Add("changed", a, b, ap, bp);
        }

        Dictionary<string, JsonElement>? Properties(JsonElement node, string at)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
                if (!result.TryAdd(property.Name, property.Value))
                { problems.Add($"{at}: duplicate property '{property.Name}'; result is incomplete."); return null; }
            return result;
        }

        Dictionary<string, int>? Index(JsonElement[] items, string key, string at)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
                { problems.Add($"{at}/{i}: missing or non-string key '{key}'; result is incomplete."); return null; }
                if (item.EnumerateObject().Count(p => p.Name == key) != 1)
                { problems.Add($"{at}/{i}: duplicate key property '{key}'; result is incomplete."); return null; }
                if (!result.TryAdd(value.GetString()!, i))
                { problems.Add($"{at}/{i}: duplicate key '{key}' value '{value.GetString()}'; result is incomplete."); return null; }
            }
            return result;
        }
    }
}
