using System.Text.Encodings.Web;
using System.Text.Json;

namespace Bed;

/// <summary>The canonical JSON settings every document and journal is written with: indented, LF line endings,
/// readable (non-ASCII text stays as written), numbers in their shortest round-trip form. Formats append a final newline.</summary>
public static class Canonical
{
    private static readonly System.Text.UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>A document file's text: UTF-8, with or without a byte-order mark, read strictly. Bytes that aren't UTF-8 are
    /// refused with <see cref="DocumentFormatException"/>, never replaced: replacing them and saving would destroy the file's
    /// original bytes (found by A202). A UTF-16 or UTF-32 file, marked by its byte-order mark, is read as that.</summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..] or [0x00, 0x00, 0xFE, 0xFF, ..])
            return File.ReadAllText(path);   // a marked wide encoding, read as the platform reads it
        var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        try { return Strict.GetString(bytes, start, bytes.Length - start); }
        catch (System.Text.DecoderFallbackException e)
        {
            var at = e.Index >= 0 ? $" at byte {e.Index + start}" : "";
            throw new DocumentFormatException("$", $"isn't valid UTF-8{at}. Save the file as UTF-8 (or fix those bytes), then open it again; nothing was changed");
        }
    }

    /// <summary>Writer options for canonical text.</summary>
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// A strict, path-aware view of a JSON object for reading documents. Every problem names its JSON path, unknown or
/// duplicate properties are refused, and numbers must be finite. Formats use it so their errors read the same way.
/// </summary>
public sealed class StrictReader
{
    private readonly JsonElement element;

    /// <summary>Wraps <paramref name="element"/>, found at <paramref name="path"/>.</summary>
    public StrictReader(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new DocumentFormatException(path, "expected an object");
        this.element = element;
        Path = path;
    }

    /// <summary>Where this object sits, for example <c>$.items[2]</c>.</summary>
    public string Path { get; }

    /// <summary>Refuses any property not in <paramref name="allowed"/>, and any property given twice.</summary>
    public void Only(params string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new DocumentFormatException($"{Path}.{p.Name}", "property given twice");
            if (System.Array.IndexOf(allowed, p.Name) < 0) throw new DocumentFormatException($"{Path}.{p.Name}", "unknown property");
        }
    }

    /// <summary>True when the property is present (and not null).</summary>
    public bool Has(string name) => element.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null;

    /// <summary>A required property, as raw JSON.</summary>
    public JsonElement Element(string name) =>
        element.TryGetProperty(name, out var v) ? v.Clone() : throw new DocumentFormatException($"{Path}.{name}", "missing");

    /// <summary>A required string.</summary>
    public string String(string name)
    {
        var v = Element(name);
        return v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new DocumentFormatException($"{Path}.{name}", "expected a string");
    }

    /// <summary>A required whole number.</summary>
    public int Int(string name)
    {
        var v = Element(name);
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : throw new DocumentFormatException($"{Path}.{name}", "expected a whole number");
    }

    /// <summary>A required finite number.</summary>
    public double Double(string name)
    {
        var v = Element(name);
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)
            ? d
            : throw new DocumentFormatException($"{Path}.{name}", "expected a finite number");
    }

    /// <summary>A required array of objects, each wrapped with its own path.</summary>
    public IEnumerable<StrictReader> Array(string name)
    {
        var v = Element(name);
        if (v.ValueKind != JsonValueKind.Array) throw new DocumentFormatException($"{Path}.{name}", "expected an array");
        var i = 0;
        foreach (var item in v.EnumerateArray()) yield return new StrictReader(item, $"{Path}.{name}[{i++}]");
    }

    /// <summary>Reads a whole text as a strict object, refusing anything that isn't JSON.</summary>
    public static StrictReader Parse(string text)
    {
        try { return new StrictReader(JsonDocument.Parse(text).RootElement.Clone(), "$"); }
        catch (JsonException e) { throw new DocumentFormatException("$", "not valid JSON: " + e.Message); }
    }
}
