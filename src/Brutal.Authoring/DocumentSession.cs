namespace Brutal.Authoring;

/// <summary>What a session found when it looked at its file on disk.</summary>
public enum DiskState
{
    /// <summary>The file holds what this session last loaded or saved.</summary>
    Unchanged,
    /// <summary>Something else changed the file since then.</summary>
    ChangedOnDisk,
    /// <summary>The file is gone.</summary>
    Missing,
    /// <summary>The session has no file yet.</summary>
    NoFile,
}

/// <summary>
/// One open document and everything that may change it (worksheet AT-010, AT-011, FUT-001, FUT-101). The only way to edit
/// is <see cref="Execute"/> or <see cref="ExecuteBatch"/>:
/// <list type="bullet">
///   <item>each command builds a candidate, the validator checks it, and only a clean candidate is committed;</item>
///   <item>a refused edit leaves the document, its history and its journal exactly as they were;</item>
///   <item>every committed step, undo and redo is journaled, so the session can be replayed (<see cref="ReplayFrom"/>).</item>
/// </list>
/// Saving is atomic: a failed save leaves the old file in place. <see cref="CheckDisk"/> notices outside edits.
/// </summary>
public sealed class DocumentSession<TDoc> where TDoc : class
{
    private readonly List<JournalEntry> journal = [];
    private long journalBytes;   // the journal's command arguments, as text: what it holds that can grow large
    private string savedText;   // canonical text of the last loaded or saved document: what Dirty compares against
    private string? diskText;   // the file's exact text when last loaded or saved: what CheckDisk compares against

    /// <summary>A session over <paramref name="document"/>, not yet tied to a file. Throws if the document isn't valid.</summary>
    public DocumentSession(IDocumentFormat<TDoc> format, IValidator<TDoc> validator, CommandCatalog<TDoc> catalog, TDoc document, int historyCapacity = 256)
        : this(format, validator, catalog, document, historyCapacity, repair: false) { }

    private DocumentSession(IDocumentFormat<TDoc> format, IValidator<TDoc> validator, CommandCatalog<TDoc> catalog, TDoc document, int historyCapacity, bool repair)
    {
        Format = format;
        Validator = validator;
        Catalog = catalog;
        var issues = validator.Validate(document);
        if (issues.HasErrors() && !repair) throw new InvalidDocumentException(issues);
        repairable = repair;
        problems = repair ? Errors(issues) : [];
        Current = document;
        History = new EditHistory<TDoc>(historyCapacity);
        JournalBase = new JournalBase<TDoc>(document, History);
        savedText = format.Write(document);
    }

    /// <summary>Opens a document file. Throws <see cref="DocumentFormatException"/> or <see cref="InvalidDocumentException"/>
    /// if it can't be read or isn't valid; a bad file never becomes a session.</summary>
    public static DocumentSession<TDoc> Open(string path, IDocumentFormat<TDoc> format, IValidator<TDoc> validator, CommandCatalog<TDoc> catalog, int historyCapacity = 256)
    {
        var text = Canonical.ReadText(path);
        var session = new DocumentSession<TDoc>(format, validator, catalog, format.Read(text), historyCapacity) { FilePath = Path.GetFullPath(path), diskText = text };
        if (format is IUpgradingFormat { LastApplied.Count: > 0 }) session.savedText = "";   // upgraded: saving would write the new version
        return session;
    }

    /// <summary>
    /// Opens a file that isn't valid, to repair it: the schema's problems are listed in <see cref="Problems"/>, and an edit
    /// is accepted when it adds no problem of its own, even while others remain, so they can be fixed one at a time and
    /// nothing new can be broken meanwhile. Once none are left the session is an ordinary one (<see cref="Repairing"/>
    /// false); an undo back into a broken state repairs again. Throws <see cref="DocumentFormatException"/> when the text
    /// can't be read as a document at all. Saving is allowed, since the file on disk was already broken. INVENTED (found
    /// listing gaps, 2026-11-01: a broken file could only be fixed by hand).
    /// </summary>
    public static DocumentSession<TDoc> OpenForRepair(string path, IDocumentFormat<TDoc> format, IValidator<TDoc> validator, CommandCatalog<TDoc> catalog, int historyCapacity = 256)
    {
        var text = Canonical.ReadText(path);
        var session = new DocumentSession<TDoc>(format, validator, catalog, format.Read(text), historyCapacity, repair: true) { FilePath = Path.GetFullPath(path), diskText = text };
        if (format is IUpgradingFormat { LastApplied.Count: > 0 }) session.savedText = "";
        return session;
    }

    private readonly bool repairable;
    private IReadOnlyList<Issue> problems;

    /// <summary>True while a session opened for repair still has problems: edits then need only add none of their own.</summary>
    public bool Repairing => problems.Count > 0;

    /// <summary>The problems a session opened for repair still has; empty otherwise.</summary>
    public IReadOnlyList<Issue> Problems => problems;

    private static List<Issue> Errors(IEnumerable<Issue> issues) => issues.Where(i => i.Severity == Severity.Error).ToList();

    /// <summary>How documents of this type are read and written.</summary>
    public IDocumentFormat<TDoc> Format { get; }

    /// <summary>What makes a document of this type valid.</summary>
    public IValidator<TDoc> Validator { get; }

    /// <summary>The commands this document type supports.</summary>
    public CommandCatalog<TDoc> Catalog { get; }

    /// <summary>Undo and redo.</summary>
    public EditHistory<TDoc> History { get; private set; }

    /// <summary>The document as it is now. Documents are immutable, so holding this can't change the session.</summary>
    public TDoc Current { get; private set; }

    /// <summary>Counts every change to <see cref="Current"/> (edits, undos, redos, reloads). A proposal made against one
    /// revision can be refused when the document has moved on (FUT-101).</summary>
    public long Revision { get; private set; }

    /// <summary>The file this session reads and saves, if any.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Every step since <see cref="JournalBase"/>, in order: commands, batches, undos and redos.</summary>
    public IReadOnlyList<JournalEntry> Journal => journal;

    /// <summary>Where <see cref="Journal"/> starts: the opened document at first, then the session as it stood whenever the journal
    /// passed <see cref="JournalLimit"/> entries or <see cref="JournalBudget"/> bytes of arguments, so memory stays bounded however
    /// long a session runs (found by A204: a thousand 1 MB edits held 1.3 GB). Replaying the journal from it gives the current
    /// document, undo and redo included.</summary>
    public JournalBase<TDoc> JournalBase { get; private set; }

    /// <summary>The most journal entries kept before <see cref="JournalBase"/> moves forward.</summary>
    public int JournalLimit { get; set; } = 10_000;

    /// <summary>The most bytes of command arguments kept before <see cref="JournalBase"/> moves forward.</summary>
    public long JournalBudget { get; set; } = 64L * 1024 * 1024;

    // Adds one entry, and moves the base forward when the journal's past its limit: the session as it is now (its document and
    // undo steps, all immutable and shared, not copied) is the new start, and the journal begins again from it.
    private void Journaled(JournalEntry entry)
    {
        journal.Add(entry);
        journalBytes += SizeOf(entry);
        if (journal.Count <= JournalLimit && journalBytes <= JournalBudget) return;
        JournalBase = new JournalBase<TDoc>(Current, History);
        journal.Clear();
        journalBytes = 0;
    }

    private static long SizeOf(JournalEntry e) =>
        (e.Args is { } a ? a.GetRawText().Length : 0) + (e.Batch?.Sum(SizeOf) ?? 0);

    /// <summary>The issues from the last edit, undo, redo, load or save (empty when it went through cleanly).</summary>
    public IReadOnlyList<Issue> LastIssues { get; private set; } = [];

    /// <summary>True when the document differs from what was last loaded or saved.</summary>
    public bool Dirty
    {
        get
        {
            // Asked every frame, several times, and it serializes the whole document (found by A088). A document is immutable,
            // so the answer stands until the document or the saved text is a different one.
            var (current, saved) = (Current, savedText);
            if (dirtyAnswer is { } known && ReferenceEquals(known.Document, current) && ReferenceEquals(known.Saved, saved)) return known.Dirty;
            var dirty = Format.Write(current) != saved;
            dirtyAnswer = new DirtyAnswer(current, saved, dirty);
            return dirty;
        }
    }

    private sealed record DirtyAnswer(TDoc Document, string Saved, bool Dirty);
    private DirtyAnswer? dirtyAnswer;

    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Applies one command. Returns false, leaving everything unchanged, when the command or the validator refuses.</summary>
    public bool Execute(ICommand<TDoc> command) => Commit([command]) is { } entries && Record(entries[0]);

    /// <summary>Applies commands as one step: all of them, or (if any is refused) none, with one undo step for the lot.</summary>
    public bool ExecuteBatch(IReadOnlyList<ICommand<TDoc>> commands)
    {
        if (commands.Count == 0) { LastIssues = []; return true; }
        return Commit(commands) is { } entries && Record(JournalEntry.Of(entries));
    }

    private List<JournalEntry>? Commit(IReadOnlyList<ICommand<TDoc>> commands)
    {
        var candidate = Current;
        var recorded = new List<ICommand<TDoc>>(commands.Count);
        var warnings = new List<Issue>();
        if(Current is Scene scene&&commands.Count>1&&commands.All(command=>command is MoveObject)
            &&SceneMoveBatch.TryApply(scene,commands.Cast<MoveObject>().ToArray(),out var moved))
        {
            foreach(var command in commands)Catalog.InfoFor(command);
            if(!moved!.Ok){LastIssues=moved.Issues;return null;}
            candidate=(TDoc)(object)moved.Document!;
            recorded.AddRange(commands);
        }
        else foreach (var command in commands)
        {
            Catalog.InfoFor(command);   // an unregistered command is a programming error, not a user one
            var result = command.Apply(candidate);
            if (!result.Ok) { LastIssues = result.Issues; return null; }
            warnings.AddRange(result.Issues);
            candidate = result.Document!;
            recorded.Add(result.Resolved ?? command);
        }
        // The validator speaks first: its message names the object and field (A009 found a NaN reaching the journal
        // writer before the validator could refuse it).
        var issues = Validator.Validate(candidate);
        if (issues.HasErrors())
        {
            // While repairing, only the problems an edit adds refuse it; ones already there may stay for now.
            // Known means the same item, place and message: a game rule's problems can share a message and have no path,
            // so another item's new problem must not pass as an old one (found by A056).
            var known = problems.Select(p => (p.ObjectId, p.Path, p.Message)).ToHashSet();
            var added = Errors(issues).Where(i => !known.Contains((i.ObjectId, i.Path, i.Message))).ToList();
            if (!Repairing || added.Count > 0) { LastIssues = Repairing ? added : issues; return null; }
        }
        var entries = new List<JournalEntry>(recorded.Count);
        foreach (var command in recorded)
        {
            try { entries.Add(Catalog.ToEntry(command)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or System.Text.Json.JsonException)
            {
                LastIssues = [Issue.Error($"{Catalog.InfoFor(command).Label} can't be recorded: {e.Message}")];
                return null;   // an edit that can't be journaled can't be replayed, so it isn't made
            }
        }
        History.Push(Current);
        Set(candidate);
        LastIssues = [.. warnings, .. issues];
        return entries;
    }

    private bool Record(JournalEntry entry)
    {
        Journaled(entry);
        return true;
    }

    /// <summary>Steps back one edit. False when there's nothing to undo.</summary>
    public bool Undo()
    {
        LastIssues = [];
        if (!History.TryUndo(Current, out var previous)) return false;
        Set(previous);
        Journaled(JournalEntry.Undo());
        return true;
    }

    /// <summary>Steps forward one undone edit. False when there's nothing to redo.</summary>
    public bool Redo()
    {
        LastIssues = [];
        if (!History.TryRedo(Current, out var next)) return false;
        Set(next);
        Journaled(JournalEntry.Redo());
        return true;
    }

    private void Set(TDoc document)
    {
        if (repairable) problems = Errors(Validator.Validate(document));   // a repair session follows its document, undo included
        Current = document;
        Revision++;
        Changed?.Invoke();
    }

    /// <summary>Optional owner policy checked before Save, Revert and CheckDisk touch a full path. Return a plain
    /// refusal or null to allow access. Save throws IOException, Revert returns false with LastIssues, and CheckDisk
    /// reports ChangedOnDisk on refusal. Null preserves normal session behavior. Keep the callback free of side effects;
    /// it may be called again immediately before writing.</summary>
    public Func<string, string?>? FileAccessRefusal { get; set; }

    private string? AccessRefusal(string path)
    {
        try { return FileAccessRefusal?.Invoke(path); }
        catch (Exception e) when (e is not OutOfMemoryException) { return "The file access policy failed: " + e.Message; }
    }

    /// <summary>Saves to <see cref="FilePath"/>, or to <paramref name="path"/> (which becomes the session's file). Atomic:
    /// the text goes to a temporary file beside the target, is flushed, then replaces it. On failure the old file is untouched
    /// and the exception propagates.</summary>
    public void Save(string? path = null)
    {
        var target = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("this session has no file; pass a path"));
        if (AccessRefusal(target) is { } refusal) throw new IOException(refusal);
        var text = Format.Write(Current);
        if (AccessRefusal(target) is { } changed) throw new IOException(changed);
        AtomicFile.Write(target, text);
        FilePath = target;
        savedText = diskText = text;
        LastIssues = [];
    }

    /// <summary>The text <see cref="Save"/> would write now. With <see cref="MarkSaved"/>, for a caller writing several files as a group,
    /// which only knows they all went once the last has.</summary>
    public string TextToSave => Format.Write(Current);

    /// <summary>The text this session last read from or wrote to its file: anything else there came from outside.</summary>
    public string? ExpectedOnDisk => FilePath is null ? null : diskText;

    /// <summary>Records that the session's file now holds <paramref name="text"/>, written by the caller.</summary>
    public void MarkSaved(string text)
    {
        savedText = diskText = text;
        LastIssues = [];
    }

    /// <summary>Compares the file on disk with what this session last loaded or saved.</summary>
    public DiskState CheckDisk()
    {
        if (FilePath is null) return DiskState.NoFile;
        if (AccessRefusal(FilePath) is not null) return DiskState.ChangedOnDisk;
        if (!File.Exists(FilePath)) return DiskState.Missing;
        string text;
        try { text = File.ReadAllText(FilePath); }
        catch (IOException) { return DiskState.ChangedOnDisk; }   // being written right now: treat as changed
        return text == diskText ? DiskState.Unchanged : DiskState.ChangedOnDisk;
    }

    /// <summary>Replaces the document with the file on disk, dropping unsaved edits and history. If the file can't be read or
    /// isn't valid, nothing changes and <see cref="LastIssues"/> says why; returns false.</summary>
    public bool Revert()
    {
        if (FilePath is null) throw new InvalidOperationException("this session has no file to revert to");
        string text;
        TDoc document;
        try
        {
            if (AccessRefusal(FilePath) is { } refusal) throw new IOException(refusal);
            text = Canonical.ReadText(FilePath);
            document = Format.Read(text);
        }
        catch (Exception e) when (e is IOException or DocumentFormatException or UnauthorizedAccessException)
        {
            LastIssues = [Issue.Error("can't reload the file: " + e.Message, path: (e as DocumentFormatException)?.Path)];
            return false;
        }
        var issues = Validator.Validate(document);
        if (issues.HasErrors()) { LastIssues = issues; return false; }
        History.Clear();
        journal.Clear();   // the reloaded file is the new base for replay
        journalBytes = 0;
        diskText = text;
        savedText = Format is IUpgradingFormat { LastApplied.Count: > 0 } ? "" : Format.Write(document);
        Set(document);
        JournalBase = new JournalBase<TDoc>(document, History);
        LastIssues = issues;
        return true;
    }

    /// <summary>Replays <paramref name="entries"/> onto <paramref name="baseDocument"/> and returns the session it leaves. Throws
    /// <see cref="ReplayException"/> at the first entry that can't be read or is refused, naming the entry.</summary>
    public static DocumentSession<TDoc> Replay(TDoc baseDocument, IEnumerable<JournalEntry> entries, IDocumentFormat<TDoc> format,
        IValidator<TDoc> validator, CommandCatalog<TDoc> catalog, int historyCapacity = 256)
    {
        var session = new DocumentSession<TDoc>(format, validator, catalog, baseDocument, historyCapacity);
        session.Apply(entries);
        return session;
    }

    /// <summary>Replays <paramref name="entries"/> from <paramref name="from"/> (a session's <see cref="JournalBase"/>), its undo and
    /// redo steps included, and returns the session it leaves. Throws <see cref="ReplayException"/> as the other overload does.</summary>
    public static DocumentSession<TDoc> ReplayFrom(JournalBase<TDoc> from, IEnumerable<JournalEntry> entries, IDocumentFormat<TDoc> format,
        IValidator<TDoc> validator, CommandCatalog<TDoc> catalog)
    {
        var history = from.History();
        var session = new DocumentSession<TDoc>(format, validator, catalog, from.Document, history.Capacity);
        session.History = history;
        session.JournalBase = new JournalBase<TDoc>(from.Document, history);
        session.Apply(entries);
        return session;
    }

    /// <summary>Applies journal <paramref name="entries"/> to this session in order. Stops at the first one that fails:
    /// a <see cref="ReplayException"/> when an edit is refused, a <see cref="DocumentFormatException"/> (whose path names
    /// the entry) when an entry can't be read. The entries before it stay applied.</summary>
    public void Apply(IEnumerable<JournalEntry> entries)
    {
        var i = 0;
        foreach (var e in entries)
        {
            var path = $"$.entries[{i}]";
            bool ok;
            try
            {
                ok = e.Op switch
                {
                    JournalEntry.DoOp => Execute(Catalog.FromEntry(e, path)),
                    JournalEntry.BatchOp => ExecuteBatch(e.Batch!.Select((b, j) => Catalog.FromEntry(b, $"{path}.batch[{j}]")).ToList()),
                    JournalEntry.UndoOp => Undo() || Fail("nothing to undo"),
                    JournalEntry.RedoOp => Redo() || Fail("nothing to redo"),
                    _ => Fail($"unknown op '{e.Op}'"),
                };
            }
            catch (DocumentFormatException)
            {
                throw;   // a journal that can't be read is bad input, not a refused edit; its path names the entry
            }
            if (!ok) throw new ReplayException(i, LastIssues);
            i++;
        }
    }

    private bool Fail(string message)
    {
        LastIssues = [Issue.Error(message)];
        return false;
    }
}

/// <summary>Writes a file atomically: a temporary file beside the target, flushed to disk, then moved over it.</summary>
public static class AtomicFile
{
    /// <summary>Writes <paramref name="text"/> (UTF-8, no byte-order mark) to <paramref name="path"/> atomically.</summary>
    public static void Write(string path, string text) => Write(path, new System.Text.UTF8Encoding(false).GetBytes(text));

    /// <summary>Writes <paramref name="bytes"/> to <paramref name="path"/> atomically.</summary>
    public static void Write(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var temporary = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

/// <summary>One file in a group to write: what should be there now, and what to put there.</summary>
public sealed record GroupFile(string Path, string? Expected, string Text);

/// <summary>Where a file in a group ended up.</summary>
public enum GroupFileState
{
    /// <summary>It still holds what it held: never written, or written and put back.</summary>
    AsItWas,

    /// <summary>It holds the new text.</summary>
    Written,

    /// <summary>It was written, and now holds something else that isn't ours to overwrite, or can't be read. Left as it is.</summary>
    Unknown,
}

/// <summary>How a group went: every file's state, in order, and if it stopped, which file stopped it and why.</summary>
public sealed record GroupResult(IReadOnlyList<GroupFileState> States, int? Failed, string? Why);

/// <summary>Writes several files as one: all of them, or none.</summary>
public static class FileGroup
{
    /// <summary>Writes each file in turn. A file that no longer holds what's expected stops it, as does one that won't write. Then what's been
    /// written goes back to what it was, but only where it still holds what was written: anything else there came from outside since, and
    /// isn't overwritten. Each state comes from reading the file at the end, not from what was attempted, and a file found holding something
    /// else stops the group even if every write went through. Only a power cut can leave part of a group without a report.</summary>
    /// <param name="writer">How a file's written; <see cref="AtomicFile"/> unless a test wants it to fail.</param>
    public static GroupResult Write(IReadOnlyList<GroupFile> files, Action<string, byte[]>? writer = null)
    {
        writer ??= AtomicFile.Write;
        var encoding = new System.Text.UTF8Encoding(false);
        var was = new byte[files.Count][];
        var touched = new bool[files.Count];
        int? failed = null;
        string? why = null;
        for (var i = 0; i < files.Count && failed is null; i++)
        {
            var file = files[i];
            try
            {
                was[i] = File.ReadAllBytes(file.Path);
                if (file.Expected is not null && Decode(was[i]) != file.Expected)
                {
                    (failed, why) = (i, $"{System.IO.Path.GetFileName(file.Path)} was changed outside the editor; reload it or keep your version before saving");
                    break;
                }
                touched[i] = true;
                writer(file.Path, encoding.GetBytes(file.Text));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                (failed, why) = (i, $"{System.IO.Path.GetFileName(file.Path)} can't be written: {e.Message}");
            }
        }
        if (failed is not null)
            for (var d = 0; d < files.Count; d++)
                if (touched[d] && Holds(files[d].Path, encoding.GetBytes(files[d].Text)) && !Holds(files[d].Path, was[d]))
                {
                    try { writer(files[d].Path, was[d]); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the reading below says where that left it
                }

        var states = new GroupFileState[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            if (!touched[i]) continue;   // never written, so as it was, whatever it holds
            var ours = encoding.GetBytes(files[i].Text);
            states[i] = Holds(files[i].Path, ours) ? GroupFileState.Written : Holds(files[i].Path, was[i]) ? GroupFileState.AsItWas : GroupFileState.Unknown;
        }
        if (failed is null && Array.IndexOf(states, GroupFileState.Unknown) is >= 0 and var other)
            (failed, why) = (other, $"{System.IO.Path.GetFileName(files[other].Path)} was changed outside the editor while it was being saved; reload it or keep your version");
        return new GroupResult(states, failed, why);
    }

    private static bool Holds(string path, byte[] bytes)
    {
        try { return File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes));   // as File.ReadAllText reads it, which is how a session compares
        return reader.ReadToEnd();
    }
}

/// <summary>A document that failed validation where a valid one was required.</summary>
public sealed class InvalidDocumentException(IReadOnlyList<Issue> issues)
    : Exception("the document isn't valid: " + string.Join("; ", issues.Where(i => i.Severity == Severity.Error)))
{
    /// <summary>Everything the validator found.</summary>
    public IReadOnlyList<Issue> Issues { get; } = issues;
}

/// <summary>A journal entry that couldn't be replayed.</summary>
public sealed class ReplayException(int entry, IReadOnlyList<Issue> issues)
    : Exception($"journal entry {entry} failed: " + string.Join("; ", issues))
{
    /// <summary>The index of the failing entry.</summary>
    public int Entry { get; } = entry;

    /// <summary>Why it failed.</summary>
    public IReadOnlyList<Issue> Issues { get; } = issues;
}
