using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Bed;

namespace Ladder;

// Small shared bootstrap, counted separately from the three rungs.
public sealed class NotebookFormat : IDocumentFormat<Notebook>
{
    public string Kind => "ladder.notebook";
    public int Version => 1;
    public Notebook Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "counters", "notes");
        if (r.String("kind") != Kind) throw new DocumentFormatException("$.kind", "Expected ladder.notebook.");
        if (r.Int("version") != Version) throw new DocumentFormatException("$.version", "Expected version 1.");
        var counters = r.Array("counters").Select(c =>
        {
            c.Only("id", "label", "value");
            return new Counter(c.String("id"), c.String("label"), c.Int("value"));
        }).ToImmutableArray();
        var notes = r.Array("notes").Select(n =>
        {
            n.Only("id", "label");
            return new Note(n.String("id"), n.String("label"));
        }).ToImmutableArray();
        return new Notebook(counters, notes);
    }
    public string Write(Notebook d)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Canonical.WriterOptions))
        {
            w.WriteStartObject(); w.WriteString("kind"u8, Kind); w.WriteNumber("version"u8, Version);
            w.WriteStartArray("counters"u8);
            foreach (var c in d.Counters) { w.WriteStartObject(); w.WriteString("id"u8, c.Id); w.WriteString("label"u8, c.Label); w.WriteNumber("value"u8, c.Value); w.WriteEndObject(); }
            w.WriteEndArray(); w.WriteStartArray("notes"u8);
            foreach (var n in d.Notes) { w.WriteStartObject(); w.WriteString("id"u8, n.Id); w.WriteString("label"u8, n.Label); w.WriteEndObject(); }
            w.WriteEndArray(); w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}

