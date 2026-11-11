using System.Globalization;
using System.Text.Json;
using Bed.Data;

namespace Bed.Editor;

/// <summary>A place in a document that a search found.</summary>
/// <param name="Pointer">Where it is, as a JSON Pointer, in the document searched. An index means that document only.</param>
/// <param name="Preview">What to show for it: where, and the value.</param>
public sealed record FoundPlace(string Pointer, string Preview);

/// <summary>
/// Search through a whole data document: every field's name and every plain value, however deep, ignoring case. What a file
/// doesn't hold can't be found: a schema's offered fields and defaults aren't searched. MIXED: Codex's design
/// (docs/plan/off_frame.md, C20), narrowed to one synchronous pass, since real data files are small.
/// </summary>
public static class DataSearch
{
    /// <summary>The first <paramref name="limit"/> places holding <paramref name="text"/>, in document order, and whether there
    /// were more. Empty text finds nothing.</summary>
    public static (IReadOnlyList<FoundPlace> Places, bool More) Find(DataDocument document, string text, int limit = 100)
    {
        var found = new List<FoundPlace>();
        var wanted = text.Trim();
        if (wanted.Length == 0) return (found, false);
        var more = false;
        foreach (var (value, at, name) in Walk(document.Root))
        {
            var plain = Plain(value);
            var hit = (name is not null && name.Contains(wanted, StringComparison.OrdinalIgnoreCase)) || (plain is not null && plain.Contains(wanted, StringComparison.OrdinalIgnoreCase));
            if (hit)
            {
                if (found.Count == limit) { more = true; break; }
                found.Add(new FoundPlace(at, plain is null ? at : $"{at} = {Short(plain)}"));
            }
        }
        return (found, more);
    }

    // BORROWED: the existing preorder walk, resumed one sibling at a time instead of materializing every sibling.
    // The explicit stack keeps deep documents off the call stack; disposing an early search releases all cursors.
    private static IEnumerable<(JsonElement Value, string At, string? Name)> Walk(JsonElement root)
    {
        var stack = new Stack<IEnumerator<(JsonElement Value, string At, string? Name)>>();
        var here = (Value: root, At: "", Name: (string?)null);
        try
        {
            while (true)
            {
                yield return here;
                if (here.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    stack.Push(Children(here.Value, here.At).GetEnumerator());
                while (stack.Count > 0 && !stack.Peek().MoveNext()) stack.Pop().Dispose();
                if (stack.Count == 0) yield break;
                here = stack.Peek().Current;
            }
        }
        finally
        {
            while (stack.TryPop(out var cursor)) cursor.Dispose();
        }
    }

    private static IEnumerable<(JsonElement Value, string At, string? Name)> Children(JsonElement value, string at)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                yield return (property.Value, at + Pointer.Join([property.Name]), property.Name);
        }
        else
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                yield return (item, at + "/" + (index++).ToString(CultureInfo.InvariantCulture), null);
        }
    }

    private static string? Plain(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => null,
    };

    // Never through the middle of a surrogate pair, which can't be encoded (found by A096).
    private static string Short(string text)
    {
        if (text.Length <= 60) return text;
        return text[..(char.IsHighSurrogate(text[56]) ? 56 : 57)] + "...";
    }
}
