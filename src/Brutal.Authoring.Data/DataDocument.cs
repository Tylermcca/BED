using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Brutal.Authoring.Data;

/// <summary>
/// Any JSON data file as an editor document: a game's catalogue of wares, factions, recipes or tuning numbers. Immutable,
/// like every document: commands return a new one. What makes it valid is a JSON Schema (<see cref="DataValidator"/>).
/// Places in it are JSON Pointers (<c>/factories/3/name</c>, RFC 6901).
/// </summary>
/// <param name="Root">The whole file's JSON.</param>
public sealed record DataDocument(JsonElement Root)
{
    /// <summary>The file as it was read, if it was: the writer copies every part an edit didn't change verbatim from
    /// here, so hand formatting (a short object kept on one line, say) survives and a diff shows only the edit.</summary>
    public JsonElement? Source { get; init; }

    // Retain whitespace outside the root too, and exact mixed endings for an unchanged file.
    internal string? SourceText { get; init; }

    /// <summary>The file's most common line ending ("\r\n" or "\n"), used for edited content; ties use "\n".
    /// Null for a new document (written with "\n").</summary>
    public string? LineEnding { get; init; }

    /// <summary>The value at <paramref name="pointer"/>, or null when there's nothing there.</summary>
    public JsonElement? At(string pointer)
    {
        var node = Root;
        foreach (var part in Pointer.Split(pointer))
        {
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(part, out var next)) node = next;
            else if (node.ValueKind == JsonValueKind.Array && int.TryParse(part, out var i) && i >= 0 && i < node.GetArrayLength()) node = ListIndex.Item(node, i);
            else return null;
        }
        return node;
    }

    // How deep an edit can reach: far past what a file is read with, and still bounded.
    private const int DeepestEdit = 4096;

    /// <summary>The document with <paramref name="edit"/> applied to a mutable copy.</summary>
    internal DataDocument With(Action<JsonNode> edit)
    {
        // Edited at any depth the document already has: a document made in memory can nest deeper than a file is read with, and its
        // edit threw at the default depth (found by A209). Files keep their own limit when they're read.
        var node = JsonNode.Parse(Root.GetRawText(), documentOptions: new JsonDocumentOptions { MaxDepth = DeepestEdit })!;
        edit(node);
        return this with { Root = JsonSerializer.SerializeToElement(node, new JsonSerializerOptions { MaxDepth = DeepestEdit }) };
    }
}

/// <summary>JSON Pointer helpers (RFC 6901): <c>/a/b~1c/0</c> is a, then "b/c", then item 0.</summary>
public static class Pointer
{
    /// <summary>A pointer's parts, unescaped; the root is <c>""</c> (no parts).</summary>
    public static IReadOnlyList<string> Split(string pointer)
    {
        if (pointer.Length == 0) return [];
        if (pointer[0] != '/') throw new FormatException($"'{pointer}' isn't a JSON pointer; it starts with /");
        return pointer[1..].Split('/').Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToList();
    }

    /// <summary>A pointer from parts.</summary>
    public static string Join(IEnumerable<string> parts) => string.Concat(parts.Select(p => "/" + p.Replace("~", "~0").Replace("/", "~1")));

    /// <summary>The pointer for a JSON path as the validator writes it (<c>$.factories[3].name</c>,
    /// <c>$['odd key'][0]</c>), or null when it isn't one.</summary>
    public static string? FromPath(string path)
    {
        if (!path.StartsWith('$')) return null;
        var parts = new List<string>();
        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var end = i + 1;
                while (end < path.Length && path[end] is not ('.' or '[')) end++;
                parts.Add(path[(i + 1)..end]);
                i = end;
            }
            else if (path.AsSpan(i).StartsWith("['"))
            {
                var end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0) return null;
                parts.Add(path[(i + 2)..end]);
                i = end + 2;
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i);
                if (end < 0 || !int.TryParse(path[(i + 1)..end], NumberStyles.None, CultureInfo.InvariantCulture, out _)) return null;   // [3] only; keys are ['quoted'] (A027)
                parts.Add(path[(i + 1)..end]);
                i = end + 1;
            }
            else return null;
        }
        return Join(parts);
    }

    /// <summary>The JSON path the validator reports (<c>$.factories[3].name</c>) for a pointer.</summary>
    public static string ToPath(string pointer) => "$" + string.Concat(Split(pointer).Select(p =>
        int.TryParse(p, out _) ? $"[{p}]" : System.Text.RegularExpressions.Regex.IsMatch(p, "^[A-Za-z_][A-Za-z0-9_]*$") ? "." + p : $"['{p}']"));
}

/// <summary>
/// Reads and writes data files, keeping their style so an edit changes only the edited lines in a diff: two-space
/// indentation, the file's own key order and number spellings (<c>1.0</c> stays <c>1.0</c>), characters written as
/// themselves rather than escaped, the file's majority line ending and its original trailing whitespace. An unchanged
/// file is returned exactly as read. New files use LF and a final newline.
/// </summary>
/// <param name="kind">A name for this kind of data, shown to people; the file itself isn't checked for it.</param>
public sealed class DataFormat(string kind = "data") : IDocumentFormat<DataDocument>
{
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <inheritdoc />
    public string Kind => kind;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public DataDocument Read(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var root = doc.RootElement.Clone();
            // Only text with a \u escape can hold a lone surrogate, so the rest isn't walked.
            if (text.Contains("\\u", StringComparison.Ordinal) && LoneSurrogate(root, "$") is { } bad)
                throw new DocumentFormatException(bad, "holds a \\u escape for half of a character pair (a lone surrogate), which isn't text. Replace it with the character, or remove it");
            return new DataDocument(root)
            {
                Source = root,
                SourceText = text,
                LineEnding = MajorityLineEnding(text),
            };
        }
        catch (JsonException e)
        {
            throw new DocumentFormatException("$", $"isn't valid JSON: {e.Message}");
        }
    }

    // The first place, as a JSON path, holding a string or key that can't be made into text, or null.
    private static string? LoneSurrogate(JsonElement value, string path)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                try { value.GetString(); return null; }
                catch (InvalidOperationException) { return path; }
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in value.EnumerateArray())
                    if (LoneSurrogate(item, $"{path}[{i++}]") is { } inside) return inside;
                return null;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    string name;
                    try { name = property.Name; }
                    catch (InvalidOperationException) { return path; }
                    if (LoneSurrogate(property.Value, $"{path}.{name}") is { } inside) return inside;
                }
                return null;
            default:
                return null;
        }
    }

    private static string MajorityLineEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                if (i > 0 && text[i - 1] == '\r') crlf++;
                else lf++;
            }
        return crlf > lf ? "\r\n" : "\n";
    }

    /// <inheritdoc />
    public string Write(DataDocument document)
    {
        if (document.SourceText is { } original && document.Source is { } source && JsonSchemas.Same(document.Root, source))
            return original;

        var text = new StringBuilder();
        Emit(text, document.Root, document.Source, 0);
        var body = text.ToString().Replace("\r\n", "\n");
        if (document.LineEnding == "\r\n") body = body.Replace("\n", "\r\n");
        if (document.SourceText is not { } input) return body + (document.LineEnding ?? "\n");

        // JsonElement excludes outer whitespace. Preserve it separately, especially the exact final
        // newline (or its absence), even if it differs from the body's majority line ending.
        var start = 0;
        var end = input.Length;
        while (start < end && JsonWhitespace(input[start])) start++;
        while (end > start && JsonWhitespace(input[end - 1])) end--;
        return input[..start] + body + input[end..];
    }

    private static bool JsonWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n';

    // Writes a value at nesting depth (two spaces a level). When it's unchanged from the original (same place, same
    // JSON), the original text goes out as it was: its inner lines already carry the right indentation, because an
    // unchanged part sits at the same depth. A changed container written on one line stays on one line; otherwise
    // containers are laid out afresh and their children checked the same way. A list item that only moved (an insert
    // or removal before it) is found among the original items by value.
    private static void Emit(StringBuilder o, JsonElement now, JsonElement? before, int depth)
    {
        if (before is { } b && JsonSchemas.Same(now, b))
        {
            o.Append(b.GetRawText());
            return;
        }
        if (before is { ValueKind: JsonValueKind.Object or JsonValueKind.Array } one && now.ValueKind == one.ValueKind && !one.GetRawText().Contains('\n'))
        {
            o.Append(Inline(now, one));
            return;
        }
        var inner = new string(' ', (depth + 1) * 2);
        switch (now.ValueKind)
        {
            case JsonValueKind.Object:
                var props = now.EnumerateObject().ToList();
                if (props.Count == 0) { o.Append("{}"); return; }
                o.Append('{');
                for (var i = 0; i < props.Count; i++)
                {
                    o.Append(i == 0 ? "\n" : ",\n").Append(inner).Append(Scalar(props[i].Name)).Append(": ");
                    Emit(o, props[i].Value, before is { ValueKind: JsonValueKind.Object } bo && bo.TryGetProperty(props[i].Name, out var bp) ? bp : null, depth + 1);
                }
                o.Append('\n').Append(' ', depth * 2).Append('}');
                return;
            case JsonValueKind.Array:
                var items = now.EnumerateArray().ToList();
                if (items.Count == 0) { o.Append("[]"); return; }
                var old = before is { ValueKind: JsonValueKind.Array } ba ? ba.EnumerateArray().ToList() : [];
                o.Append('[');
                for (var i = 0; i < items.Count; i++)
                {
                    JsonElement? match = i < old.Count && JsonSchemas.Same(items[i], old[i]) ? old[i]
                        : old.FirstOrDefault(x => JsonSchemas.Same(items[i], x)) is { ValueKind: not JsonValueKind.Undefined } found ? found
                        : i < old.Count ? old[i] : null;
                    o.Append(i == 0 ? "\n" : ",\n").Append(inner);
                    Emit(o, items[i], match, depth + 1);
                }
                o.Append('\n').Append(' ', depth * 2).Append(']');
                return;
            case JsonValueKind.String:
                o.Append(Scalar(now.GetString()!));
                return;
            default:
                o.Append(now.GetRawText());   // numbers keep their spelling; true, false, null
                return;
        }
    }

    // One-line text for a value, copying the original's style: padded brackets ("{ " … " }") when it had them, ", "
    // between items and ": " after keys (the usual hand style), and the original text of any unchanged part.
    private static string Inline(JsonElement now, JsonElement? before)
    {
        if (before is { } b && JsonSchemas.Same(now, b)) return b.GetRawText();
        var raw = before?.GetRawText() ?? "";
        var pad = raw.Length > 1 && raw[1] == ' ' ? " " : "";
        switch (now.ValueKind)
        {
            case JsonValueKind.Object:
                var props = now.EnumerateObject().Select(p => Scalar(p.Name) + ": " +
                    Inline(p.Value, before is { ValueKind: JsonValueKind.Object } bo && bo.TryGetProperty(p.Name, out var bp) ? bp : null)).ToList();
                return props.Count == 0 ? "{}" : "{" + pad + string.Join(", ", props) + pad + "}";
            case JsonValueKind.Array:
                var old = before is { ValueKind: JsonValueKind.Array } ba ? ba.EnumerateArray().ToList() : [];
                var items = now.EnumerateArray().Select((x, i) => Inline(x, i < old.Count ? old[i] : null)).ToList();
                return items.Count == 0 ? "[]" : "[" + pad + string.Join(", ", items) + pad + "]";
            default:
                return now.GetRawText();
        }
    }

    private static string Scalar(string text) => JsonSerializer.Serialize(text, Relaxed);
}

/// <summary>A data document is valid when its schema says so (<see cref="JsonSchemas"/>).</summary>
public sealed class DataValidator(JsonSchemas schemas, string schema) : IValidator<DataDocument>
{
    /// <summary>The schema it checks against.</summary>
    public string Schema => schema;

    /// <summary>Everything the schema finds wrong. Each issue names, as its object, the item it's in: the list item at
    /// the top of the file (<c>/factories/3</c>) when there is one, else the top-level field (<c>/rules</c>), so an
    /// editor can show it beside that item and jump to it.</summary>
    public IReadOnlyList<Issue> Validate(DataDocument document) =>
        schemas.Validate(document.Root, schema).Select(i => i with { ObjectId = ItemOf(i.Path) }).ToList();

    private static string? ItemOf(string? path)
    {
        if (path is null || Pointer.FromPath(path) is not { } pointer) return null;
        var parts = Pointer.Split(pointer);
        return parts.Count switch
        {
            0 => null,
            1 => Pointer.Join(parts.Take(1)),
            _ => int.TryParse(parts[1], out _) ? Pointer.Join(parts.Take(2)) : Pointer.Join(parts.Take(1)),
        };
    }
}

/// <summary>Sets the value at <see cref="At"/> (adding the property if its object lacks it), or removes the property when
/// <see cref="Value"/> is null. A pointer whose parent isn't there is refused.</summary>
/// <param name="At">JSON Pointer to the value. An empty pointer replaces the document root; a non-root pointer needs an existing parent.</param>
/// <param name="Value">The new JSON value. An absent nullable value removes an object property; a JSON null value writes null. The document root cannot be removed.</param>
public sealed record SetValue(string At, JsonElement? Value) : ICommand<DataDocument>
{
    /// <inheritdoc />
    public CommandResult<DataDocument> Apply(DataDocument d)
    {
        if (Pointer.Split(At).Count == 0)
            // The file as read and its line ending stay, so a whole-file replacement (the Text panel's Apply) still writes the
            // parts it didn't change as they were, and keeps a CRLF file CRLF.
            return Value is { } whole ? CommandResult<DataDocument>.Done(d with { Root = whole.Clone() })
                : CommandResult<DataDocument>.Refused(Issue.Error("the whole file can't be removed", null, "$"));
        return Check(d) is { } why ? CommandResult<DataDocument>.Refused(why) : CommandResult<DataDocument>.Done(d.With(Write));
    }

    /// <summary>Why this can't be applied to <paramref name="d"/>, or null when it can. Only reads the document, so many
    /// sets can be checked without rebuilding it each time (see <see cref="SetValues"/>).</summary>
    internal Issue? Check(DataDocument d)
    {
        var parts = Pointer.Split(At);
        if (parts.Count == 0) return Issue.Error("the whole file can't be set as part of several values", null, "$");
        var parentPointer = Pointer.Join(parts.Take(parts.Count - 1));
        if (d.At(parentPointer) is not { } parent || parent.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            return Issue.Error($"there's nothing at {Pointer.ToPath(parentPointer)} to set a value in", null, Pointer.ToPath(At));
        var key = parts[^1];
        if (parent.ValueKind == JsonValueKind.Array && !(int.TryParse(key, out var i) && i >= 0 && i < parent.GetArrayLength()))
            return Issue.Error($"there's no item {key} at {Pointer.ToPath(parentPointer)}", null, Pointer.ToPath(At));
        if (Value is null && parent.ValueKind == JsonValueKind.Array)
            return Issue.Error("use RemoveItem to remove an item from a list", null, Pointer.ToPath(At));
        return null;
    }

    // Applies the change to a mutable copy (already checked).
    internal void Write(JsonNode root)
    {
        var parts = Pointer.Split(At);
        var p = Find(root, Pointer.Join(parts.Take(parts.Count - 1)))!;
        var key = parts[^1];
        var v = Value is { } x ? JsonNode.Parse(x.GetRawText()) : null;
        if (p is JsonObject o) { if (Value is null) o.Remove(key); else o[key] = v; }
        else ((JsonArray)p)[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)] = v;
    }

    internal static JsonNode? Find(JsonNode root, string pointer)
    {
        var node = root;
        foreach (var part in Pointer.Split(pointer))
            node = node switch
            {
                JsonObject o => o[part],
                JsonArray a when int.TryParse(part, out var i) && i >= 0 && i < a.Count => a[i],
                _ => null,
            };
        return node;
    }
}

/// <summary>
/// Sets many values in one step: every set is checked against the document first, and if any can't apply, none does;
/// then the document is rebuilt once, not once per value, so filling or pasting a thousand cells stays fast (a fill of
/// 1,000 separate sets would otherwise rebuild the file each time, and take seconds). It's one journal entry and one
/// undo step. The sets apply in order; scalar sets to distinct places don't affect each other's checks.
/// </summary>
/// <param name="Sets">Value edits in application order. Each is checked against the starting document before any is written; root replacement is not supported here.</param>
public sealed record SetValues(IReadOnlyList<SetValue> Sets) : ICommand<DataDocument>
{
    /// <inheritdoc />
    public CommandResult<DataDocument> Apply(DataDocument d)
    {
        if (Sets.Count == 0) return CommandResult<DataDocument>.Done(d);
        var problems = Sets.Select(s => s.Check(d)).OfType<Issue>().ToList();
        if (problems.Count > 0) return CommandResult<DataDocument>.Refused([.. problems]);
        return CommandResult<DataDocument>.Done(d.With(root => { foreach (var s in Sets) s.Write(root); }));
    }
}

/// <summary>Inserts <see cref="Value"/> into the list at <see cref="List"/>, before position <see cref="Index"/> (the
/// list's length appends).</summary>
/// <param name="List">JSON Pointer to the array receiving the item.</param>
/// <param name="Index">Zero-based position to insert before, from zero through the current length. The current length appends.</param>
/// <param name="Value">The JSON value to insert, copied into the changed document.</param>
public sealed record InsertItem(string List, int Index, JsonElement Value) : ICommand<DataDocument>
{
    /// <inheritdoc />
    public CommandResult<DataDocument> Apply(DataDocument d)
    {
        if (d.At(List) is not { ValueKind: JsonValueKind.Array } list)
            return CommandResult<DataDocument>.Refused(Issue.Error($"{Pointer.ToPath(List)} isn't a list", null, Pointer.ToPath(List)));
        if (Index < 0 || Index > list.GetArrayLength())
            return CommandResult<DataDocument>.Refused(Issue.Error($"position {Index} is outside the list (0 to {list.GetArrayLength()})", null, Pointer.ToPath(List)));
        return CommandResult<DataDocument>.Done(d.With(root => ((JsonArray)SetValue.Find(root, List)!).Insert(Index, JsonNode.Parse(Value.GetRawText()))));
    }
}

/// <summary>Removes the list item at <see cref="At"/>.</summary>
/// <param name="At">JSON Pointer to an existing array item, including its zero-based index. The root and object properties are not removed by this command.</param>
public sealed record RemoveItem(string At) : ICommand<DataDocument>
{
    /// <inheritdoc />
    public CommandResult<DataDocument> Apply(DataDocument d)
    {
        var parts = Pointer.Split(At);
        var list = Pointer.Join(parts.Take(Math.Max(0, parts.Count - 1)));
        if (parts.Count == 0 || d.At(list) is not { ValueKind: JsonValueKind.Array } arr || !int.TryParse(parts[^1], out var i) || i < 0 || i >= arr.GetArrayLength())
            return CommandResult<DataDocument>.Refused(Issue.Error($"there's no list item at {Pointer.ToPath(At)}", null, Pointer.ToPath(At)));
        return CommandResult<DataDocument>.Done(d.With(root => ((JsonArray)SetValue.Find(root, list)!).RemoveAt(i)));
    }
}

/// <summary>Moves a list item from position <see cref="From"/> to <see cref="To"/>.</summary>
/// <param name="List">JSON Pointer to the array whose order changes.</param>
/// <param name="From">Zero-based index of the item before it moves.</param>
/// <param name="To">Zero-based index in the resulting array. Both indices must be less than the original array length.</param>
public sealed record MoveItem(string List, int From, int To) : ICommand<DataDocument>
{
    /// <inheritdoc />
    public CommandResult<DataDocument> Apply(DataDocument d)
    {
        if (d.At(List) is not { ValueKind: JsonValueKind.Array } list)
            return CommandResult<DataDocument>.Refused(Issue.Error($"{Pointer.ToPath(List)} isn't a list", null, Pointer.ToPath(List)));
        var n = list.GetArrayLength();
        if (From < 0 || From >= n || To < 0 || To >= n)
            return CommandResult<DataDocument>.Refused(Issue.Error($"positions must be 0 to {n - 1}", null, Pointer.ToPath(List)));
        return CommandResult<DataDocument>.Done(d.With(root =>
        {
            var a = (JsonArray)SetValue.Find(root, List)!;
            var item = a[From];
            a.RemoveAt(From);
            a.Insert(To, item);
        }));
    }
}

/// <summary>The data document's commands.</summary>
public static class DataKit
{
    /// <summary>The commands every data document supports.</summary>
    public static CommandCatalog<DataDocument> Catalog() => new CommandCatalog<DataDocument>()
        .Register<SetValue>("data.set", 1, "Set a value")
        .Register<SetValues>("data.set_many", 1, "Set several values")
        .Register<InsertItem>("data.insert", 1, "Insert an item")
        .Register<RemoveItem>("data.remove", 1, "Remove an item")
        .Register<MoveItem>("data.move", 1, "Move an item");

    /// <summary>A session over <paramref name="document"/>, validated by <paramref name="schema"/> from <paramref name="schemas"/>.</summary>
    public static DocumentSession<DataDocument> Session(DataDocument document, JsonSchemas schemas, string schema, string kind = "data") =>
        new(new DataFormat(kind), new DataValidator(schemas, schema), Catalog(), document);

    /// <summary>Opens a data file, validated by <paramref name="schema"/>.</summary>
    public static DocumentSession<DataDocument> Open(string path, JsonSchemas schemas, string schema, string kind = "data") =>
        DocumentSession<DataDocument>.Open(path, new DataFormat(kind), new DataValidator(schemas, schema), Catalog());
}
