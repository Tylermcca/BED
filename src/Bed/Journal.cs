using System.Text;
using System.Text.Json;

namespace Bed;

/// <summary>
/// One recorded step of an editing session: a command (<c>do</c>), an <c>undo</c>, a <c>redo</c>, or a <c>batch</c> of
/// commands applied as one step. A journal replayed from the same base document reproduces the same document (goal 2).
/// </summary>
/// <param name="Op">The step: <c>do</c>, <c>undo</c>, <c>redo</c> or <c>batch</c>.</param>
/// <param name="Command">For <c>do</c>: the command's stable id.</param>
/// <param name="Version">For <c>do</c>: the command's argument version.</param>
/// <param name="Args">For <c>do</c>: the command's arguments.</param>
/// <param name="Batch">For <c>batch</c>: its <c>do</c> entries, applied all or nothing.</param>
public sealed record JournalEntry(string Op, string? Command = null, int? Version = null, JsonElement? Args = null,
    IReadOnlyList<JournalEntry>? Batch = null)
{
    internal const string DoOp = "do", UndoOp = "undo", RedoOp = "redo", BatchOp = "batch";

    /// <summary>A command step.</summary>
    public static JournalEntry Do(string command, int version, JsonElement args) => new(DoOp, command, version, args);

    /// <summary>An undo step.</summary>
    public static JournalEntry Undo() => new(UndoOp);

    /// <summary>A redo step.</summary>
    public static JournalEntry Redo() => new(RedoOp);

    /// <summary>A batch of command steps applied as one.</summary>
    public static JournalEntry Of(IReadOnlyList<JournalEntry> commands) => new(BatchOp, Batch: commands);
}

/// <summary>Where a session's journal starts: the document then, and its undo and redo steps, so the journal replays from here
/// exactly, undos included (<see cref="DocumentSession{TDoc}.ReplayFrom"/>).
/// A session moves it forward when its journal grows past its limit, so a long session's memory stays bounded (found by A204).</summary>
public sealed class JournalBase<TDoc> where TDoc : class
{
    private readonly EditHistory<TDoc> history;

    internal JournalBase(TDoc document, EditHistory<TDoc> history)
    {
        Document = document;
        this.history = history.Copy();
    }

    /// <summary>The document the journal starts from.</summary>
    public TDoc Document { get; }

    /// <summary>A copy of the undo and redo steps there, for a replay.</summary>
    public EditHistory<TDoc> History() => history.Copy();
}

/// <summary>Reads and writes journal files (<c>{"kind": "journal", "version": 1, "entries": [...]}</c>) as canonical text.</summary>
public static class JournalFile
{
    /// <summary>The journal format version this code reads and writes.</summary>
    public const int Version = 1;

    /// <summary>Canonical text for <paramref name="entries"/>: fixed key order, LF line endings, a final newline.</summary>
    public static string Write(IEnumerable<JournalEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Canonical.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("kind", "journal");
            w.WriteNumber("version", Version);
            w.WriteStartArray("entries");
            foreach (var e in entries) WriteEntry(w, e);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteEntry(Utf8JsonWriter w, JournalEntry e)
    {
        w.WriteStartObject();
        w.WriteString("op", e.Op);
        if (e.Command is not null) w.WriteString("command", e.Command);
        if (e.Version is not null) w.WriteNumber("version", e.Version.Value);
        if (e.Args is not null)
        {
            w.WritePropertyName("args");
            e.Args.Value.WriteTo(w);
        }
        if (e.Batch is not null)
        {
            w.WriteStartArray("batch");
            foreach (var b in e.Batch) WriteEntry(w, b);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    /// <summary>Reads a journal. Throws <see cref="DocumentFormatException"/> with the JSON path of the first problem.</summary>
    public static IReadOnlyList<JournalEntry> Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "entries");
        if (r.String("kind") != "journal") throw new DocumentFormatException("$.kind", "expected \"journal\"");
        var version = r.Int("version");
        if (version != Version) throw new DocumentFormatException("$.version", $"journal version {version} is not supported (this tool reads version {Version})");
        return r.Array("entries").Select(ReadEntry).ToList();
    }

    private static JournalEntry ReadEntry(StrictReader r)
    {
        var op = r.String("op");
        switch (op)
        {
            case JournalEntry.DoOp:
                r.Only("op", "command", "version", "args");
                return JournalEntry.Do(r.String("command"), r.Int("version"), r.Element("args"));
            case JournalEntry.UndoOp:
            case JournalEntry.RedoOp:
                r.Only("op");
                return new JournalEntry(op);
            case JournalEntry.BatchOp:
                r.Only("op", "batch");
                return JournalEntry.Of(r.Array("batch").Select(b =>
                    b.String("op") == JournalEntry.DoOp ? ReadEntry(b) : throw new DocumentFormatException(b.Path + ".op", "a batch holds only \"do\" entries")).ToList());
            default:
                throw new DocumentFormatException(r.Path + ".op", $"unknown op '{op}' (expected do, undo, redo or batch)");
        }
    }
}
