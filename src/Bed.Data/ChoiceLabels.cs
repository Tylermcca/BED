using System.Text.Json;

namespace Bed.Data;

// Presentation annotations never add allowed values or turn a label into a stored value.
internal static class ChoiceLabels
{
    internal static string Key(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    internal static IReadOnlyDictionary<string, string>? Read(IEnumerable<JsonElement> schemas, IEnumerable<string> choices)
    {
        var notes = schemas.Where(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("x-choices"u8, out var note) && note.ValueKind == JsonValueKind.Object)
            .Select(s => s.GetProperty("x-choices"u8)).ToArray();
        if (notes.Length == 0) return null;
        var allowed = choices.ToHashSet(StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var note in notes)
            foreach (var entry in note.EnumerateObject())
                if (allowed.Contains(entry.Name) && entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { } text
                    && !string.IsNullOrWhiteSpace(text) && text.Length <= 1024 && !text.Any(char.IsControl))
                    labels.TryAdd(entry.Name, text);
        return labels.Count == 0 ? null : labels;
    }
    internal static string? Current(IEnumerable<JsonElement> schemas, JsonElement? actual)
    {
        if (actual is not { } value) return null;
        foreach (var schema in schemas)
            if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("enum"u8, out var choices) && choices.ValueKind == JsonValueKind.Array)
                foreach (var choice in choices.EnumerateArray())
                    if (JsonSchemas.Same(value, choice)) return Key(choice);
        return Key(value);
    }}