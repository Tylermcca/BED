using System.Globalization;
using System.Text.Json;
using Bed.Data;

namespace Bed.Launcher;

// View-only interpretation of unchanged comparison results. Never applies a change.
internal static class CompareNavigation
{
    internal sealed record Counts(string List, int Added, int Removed, int Changed);

    internal static Counts Total(StructuralCompare.Result result) => Count("File", result.Changes);

    internal static IReadOnlyList<Counts> ByList(DataDocument before, DataDocument after, StructuralCompare.Result result) =>
        result.Changes.GroupBy(c => Group(before, after, result, c), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Count(g.Key, g)).ToArray();

    private static string Group(DataDocument before, DataDocument after, StructuralCompare.Result result, StructuralCompare.Change change)
    {
        if (change.AfterAt is { } at) return DeepestList(after, at) ?? "(outside lists)";
        var oldList = DeepestList(before, change.BeforeAt!);
        if (oldList is null) return "(outside lists)";
        // A keyed parent may have moved. Keep its changes in the current list's bucket when identifiable.
        return Target(before, after, result, change) ?? oldList + " (before; no current list)";
    }

    private static Counts Count(string list, IEnumerable<StructuralCompare.Change> changes)
    {
        var rows = changes.ToArray();
        return new(list, rows.Count(c => c.Kind == "added"), rows.Count(c => c.Kind == "removed"), rows.Count(c => c.Kind == "changed"));
    }

    private static string? DeepestList(DataDocument document, string at)
    {
        var parts = Pointer.Split(at);
        for (var n = parts.Count; n >= 0; n--)
        {
            var parent = Pointer.Join(parts.Take(n));
            if (document.At(parent)?.ValueKind == JsonValueKind.Array) return parent;
        }
        return null;
    }

    internal static string? Target(DataDocument before, DataDocument after, StructuralCompare.Result result, StructuralCompare.Change change)
    {
        if (change.AfterAt is { } current) return current;
        // A removed item has no current row. Resolve its parent list by key, never by its old row index.
        var list = DeepestList(before, change.BeforeAt!);
        if (list is null) return ""; // a removed object field outside any list belongs to the file
        var parts = Pointer.Split(list);
        var oldAt = "";
        var newAt = "";
        foreach (var part in parts)
        {
            var oldNode = before.At(oldAt);
            var newNode = after.At(newAt);
            if (oldNode?.ValueKind == JsonValueKind.Array)
            {
                var key = result.Lists.FirstOrDefault(l => l.Pointer == newAt)?.Key;
                if (key is null || newNode?.ValueKind != JsonValueKind.Array ||
                    !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index < 0 || index >= oldNode.Value.GetArrayLength()) return null;
                var item = oldNode.Value[index];
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out var id) || id.ValueKind != JsonValueKind.String) return null;
                var matches = newNode.Value.EnumerateArray().Select((v, i) => (v, i)).Where(x =>
                    x.v.ValueKind == JsonValueKind.Object && x.v.TryGetProperty(key, out var other) &&
                    other.ValueKind == JsonValueKind.String && other.GetString() == id.GetString()).ToArray();
                if (matches.Length != 1) return null;
                newAt += "/" + matches[0].i.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                if (newNode?.ValueKind != JsonValueKind.Object || !newNode.Value.TryGetProperty(part, out _)) return null;
                newAt += Pointer.Join([part]);
            }
            oldAt += Pointer.Join([part]);
        }
        return after.At(newAt)?.ValueKind == JsonValueKind.Array ? newAt : null;
    }
}
