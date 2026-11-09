using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Load-time migrations (AT-014) on a small test document whose format has grown twice.</summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("brutal-authoring-migrate-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    // Version 3 of a "note" document: a title and a list of tags. Version 1 had "name"; version 2 had a comma-separated "tags".
    private sealed record Note(string Title, IReadOnlyList<string> Tags);

    private sealed class NoteFormat : IDocumentFormat<Note>
    {
        public string Kind => "test.note";
        public int Version => 3;

        public Note Read(string text)
        {
            var r = StrictReader.Parse(text);
            r.Only("kind", "version", "title", "tags");
            if (r.Int("version") != Version) throw new DocumentFormatException("$.version", "expected 3");
            var tags = r.Element("tags");
            return new Note(r.String("title"), tags.EnumerateArray().Select(t => t.GetString()!).ToList());
        }

        public string Write(Note d)
        {
            using var s = new MemoryStream();
            using (var w = new Utf8JsonWriter(s, Canonical.WriterOptions))
            {
                w.WriteStartObject();
                w.WriteString("kind", Kind);
                w.WriteNumber("version", Version);
                w.WriteString("title", d.Title);
                w.WriteStartArray("tags");
                foreach (var t in d.Tags) w.WriteStringValue(t);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(s.ToArray()) + "\n";
        }
    }

    private sealed class AnyNote : IValidator<Note>
    {
        public IReadOnlyList<Issue> Validate(Note d) => [];
    }

    private static Migrations Steps() => new Migrations("test.note", 3)
        .Step(1, "\"name\" became \"title\"", o => { o["title"] = o["name"]?.DeepClone(); o.Remove("name"); return o; })
        .Step(2, "tags became a list", o =>
        {
            var tags = (o["tags"]?.GetValue<string>() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            o["tags"] = new JsonArray([.. tags.Select(t => (JsonNode?)t)]);
            return o;
        });

    [Fact]
    public void AnOldFileIsUpgradedInMemoryAndSavedOnlyWhenAsked()
    {
        var path = Path.Combine(dir, "note.json");
        var original = """{"kind": "test.note", "version": 1, "name": "Crypt notes", "tags": "dark, damp"}""";
        File.WriteAllText(path, original);
        var format = new MigratingFormat<Note>(new NoteFormat(), Steps());
        var session = DocumentSession<Note>.Open(path, format, new AnyNote(), new CommandCatalog<Note>());

        Assert.Equal(new[] { "dark", "damp" }, session.Current.Tags);
        Assert.Equal("Crypt notes", session.Current.Title);
        Assert.Equal(2, format.LastApplied.Count);
        Assert.Equal(original, File.ReadAllText(path));   // opening never rewrites the file
        Assert.True(session.Dirty);                       // saving would write version 3
        session.Save();
        Assert.Contains("\"version\": 3", File.ReadAllText(path));
        Assert.False(session.Dirty);
    }

    [Fact]
    public void ACurrentFileIsLeftExactlyAsItIs()
    {
        var text = new NoteFormat().Write(new Note("t", ["a"]));
        var (upgraded, applied) = Steps().Upgrade(text);
        Assert.Same(text, upgraded);
        Assert.Empty(applied);
    }

    [Fact]
    public void ANewerFileIsRefusedClearly()
    {
        var e = Assert.Throws<DocumentFormatException>(() => Steps().Upgrade("""{"kind": "test.note", "version": 9}"""));
        Assert.Equal("$.version", e.Path);
        Assert.Contains("newer tool", e.Message);
    }

    [Fact]
    public void AMissingStepOrWrongKindIsRefused()
    {
        var gap = new Migrations("test.note", 3).Step(2, "only the second step", o => o);
        Assert.Contains("no upgrade from version 1", Assert.Throws<DocumentFormatException>(() => gap.Upgrade("""{"kind": "test.note", "version": 1}""")).Message);
        Assert.Equal("$.kind", Assert.Throws<DocumentFormatException>(() => Steps().Upgrade("""{"kind": "test.map", "version": 1}""")).Path);
        Assert.Equal("$", Assert.Throws<DocumentFormatException>(() => Steps().Upgrade("[1]")).Path);
    }

    [Fact]
    public void StepsAreCheckedWhenAdded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Migrations("k", 2).Step(2, "past the end", o => o));
        Assert.Throws<ArgumentException>(() => new Migrations("k", 3).Step(1, "a", o => o).Step(1, "again", o => o));
    }
}
