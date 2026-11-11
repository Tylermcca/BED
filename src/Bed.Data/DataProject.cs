using System.Text.Json;

namespace Bed.Data;

/// <summary>
/// A folder of JSON data files and a folder of their schemas, opened together as one project: the first piece of the
/// basic-functions plan. Every <c>*.json</c> file directly in the data folder is listed (subfolders too when recursive
/// admission is enabled); each opens on demand
/// into its own <see cref="DocumentSession{TDoc}"/>, with its own undo, save and outside-change check. A file's schema is
/// named by its <c>$schema</c> field (the file name is used) or else by its own name (<c>wares.json</c> →
/// <c>wares.schema.json</c>), the same rule as <c>datacheck</c>.
/// <para>
/// Nothing is written unless a file is saved, and a read-only project never writes. A file that can't be opened (bad
/// JSON, no schema, invalid against its schema) is listed with the reasons and stays unopened; the others still open.
/// </para>
/// MIXED: per-file sessions as in Godot's and Unity's open-resource lists; the schema-by-name rule and the refusals are
/// INVENTED.
/// </summary>
public sealed class DataProject
{
    private readonly List<DataFile> files = [];
    private readonly Dictionary<string, string> typedAdmissions = new(StringComparer.Ordinal);

    /// <summary>Reserves an already admitted file for an explicit non-data document kind before it opens.
    /// Reservations survive parse failures and cannot replace an existing data session. Hosts serialize admission.</summary>
    public bool ReserveTypedDocument(string name, string kind, out string why)
    {
        if (string.IsNullOrWhiteSpace(kind)) { why = "A document kind is required."; return false; }
        if (Find(name) is not { } file) { why = $"There's no admitted file called {name}."; return false; }
        if (file.Session is not null) { why = $"{name} already has a data session."; return false; }
        if (typedAdmissions.TryGetValue(name, out var prior))
        { why = prior == kind ? "" : $"{name} is already bound to {prior}."; return prior == kind; }
        typedAdmissions.Add(name, kind);
        why = "";
        return true;
    }

    /// <summary>Why the ordinary data route must refuse a file reserved for another document kind.</summary>
    public string? TypedAdmissionRefusal(string name) => typedAdmissions.TryGetValue(name, out var kind)
        ? $"{name} is bound to {kind}; use its typed document host. Data editing and file operations are unavailable."
        : null;


    /// <summary>Why a name fails the project's current path safety or admission policy, or null when it passes.
    /// This read-only access check is independent of typed diagnostics and read-only/save policy; it does not open a file.</summary>
    /// <param name="name">The project-relative data name.</param>
    /// <returns>A plain safety/admission refusal, or null.</returns>
    public string? FileAccessRefusal(string name) => Paths.Refusal(name);

    private readonly Func<string, string?>? readOnlyFile;
    private readonly Func<string, IValidator<DataDocument>?>? rules;

    private DataProject(string dataFolder, string schemaFolder, JsonSchemas schemas, bool readOnly, Func<string, string?>? readOnlyFile,
        Func<string, IValidator<DataDocument>?>? rules, bool recursive, Func<string, bool>? admit)
    {
        Recursive = recursive;
        Paths = new(dataFolder, recursive, admit);
        Admission = admit;
        this.readOnlyFile = readOnlyFile;
        this.rules = rules;
        DataFolder = dataFolder;
        SchemaFolder = schemaFolder;
        Schemas = schemas;
        ReadOnly = readOnly;
    }

    /// <summary>The original five-argument entry point, retained for already compiled adapters. Opens a flat project.</summary>
    public static DataProject Open(string dataFolder, string schemaFolder, bool readOnly, Func<string, string?>? readOnlyFile,
        Func<string, IValidator<DataDocument>?>? rules) => Open(dataFolder, schemaFolder, readOnly, readOnlyFile, rules, recursive: false);

    /// <summary>The six-argument recursive entry point, retained for already compiled adapters.</summary>
    public static DataProject Open(string dataFolder, string schemaFolder, bool readOnly, Func<string, string?>? readOnlyFile,
        Func<string, IValidator<DataDocument>?>? rules, bool recursive) => Open(dataFolder, schemaFolder, readOnly, readOnlyFile, rules, recursive, admit: null);

    /// <summary>Opens a project. Throws <see cref="DirectoryNotFoundException"/> when either folder is missing, and
    /// <see cref="JsonException"/> when a schema isn't valid JSON. No data file is read until it's opened.</summary>
    /// <param name="readOnlyFile">Why a file (by name) may not be saved, or null when it may: a game marks files another
    /// tool writes, such as ones a spreadsheet exports, so the editor never becomes a second, silent writer.</param>
    /// <param name="admit">Optional pure per-name inclusion rule for recursive mode. Names are canonical relative
    /// paths. Return false to exclude a file from admission, checks and file operations. Exceptions refuse admission;
    /// null keeps every JSON file. The same name must always receive the same decision.</param>
    /// <param name="recursive">Opt in to canonical relative JSON paths in subfolders. Links are never followed;
    /// the default keeps the existing flat project behavior.</param>
    /// <param name="rules">A game's own rules for a file (by name), checked with its schema on opening and on every edit,
    /// so an edit the game would refuse is refused here, with the game's message: rules between fields a schema can't
    /// state, or the game's own loader. Null when the schema is the whole story.</param>
    public static DataProject Open(string dataFolder, string schemaFolder, bool readOnly = false, Func<string, string?>? readOnlyFile = null,
        Func<string, IValidator<DataDocument>?>? rules = null, bool recursive = false, Func<string, bool>? admit = null)
    {
        if (admit is not null && !recursive) throw new ArgumentException("An admission predicate requires recursive mode.", nameof(admit));
        var data = Path.GetFullPath(dataFolder);
        var schemaDir = Path.GetFullPath(schemaFolder);
        if (!Directory.Exists(data)) throw new DirectoryNotFoundException($"there's no data folder at {data}");
        if (!Directory.Exists(schemaDir)) throw new DirectoryNotFoundException($"there's no schema folder at {schemaDir}");
        // In a shared folder only *.schema.json files are schemas: a broken data file beside them stays one unopened file with
        // its reasons, never a project that can't open (found by A218).
        var shared = string.Equals(data.TrimEnd(Path.DirectorySeparatorChar), schemaDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        var project = new DataProject(data, schemaDir, JsonSchemas.FromFolder(schemaDir, shared ? "*.schema.json" : "*.json"), readOnly, readOnlyFile, rules, recursive, admit);
        project.Refresh();
        return project;
    }

    /// <summary>The data folder, as a full path.</summary>
    public string DataFolder { get; }

    /// <summary>Whether JSON files in subfolders are admitted too. Names are canonical forward-slash paths relative
    /// to DataFolder. Junctions and symbolic links are never followed in this mode.</summary>
    public bool Recursive { get; }

    internal DataPaths Paths { get; }
    internal Func<string, bool>? Admission { get; }

    /// <summary>The schema folder, as a full path.</summary>
    public string SchemaFolder { get; }

    /// <summary>Every schema in the schema folder, as one set that can refer between files.</summary>
    public JsonSchemas Schemas { get; }

    /// <summary>Whether saving is refused for every file.</summary>
    public bool ReadOnly { get; }

    /// <summary>The game's own rules for the file called <paramref name="name"/>, if it has any.</summary>
    public IValidator<DataDocument>? RulesFor(string name)
    {
        var own = rules?.Invoke(name);
        if (ProtectedNames is not { IsEmpty: false } names) return own;
        return own is null ? names : new AllOf<DataDocument>(own, names);
    }

    /// <summary>Names the game must never use (<see cref="Data.ProtectedNames"/>): any text value containing one is refused at
    /// its field, on every edit and on opening, as the game's rules are, so the editor, the command line and MCP refuse alike.
    /// Set it before files are opened; null (the default) means none.</summary>
    public ProtectedNames? ProtectedNames { get; set; }

    /// <summary>Why the file called <paramref name="name"/> may not be saved, or null when it may.</summary>
    public string? ReadOnlyReason(string name) => ReadOnly ? "the project is open read-only" : Paths.Refusal(name) ?? readOnlyFile?.Invoke(name);

    /// <summary>Which schema a file uses, by its name, when that isn't <c>name.schema.json</c> and the file can't say so
    /// itself: many files under one schema that forbids a <c>$schema</c> field. Null falls back to the usual way. Set it
    /// before opening files: an open file keeps the schema it opened with until it's closed. If it throws, the file is
    /// refused with the reason, and nothing changes. Creating a file under a schema it doesn't answer for the name is refused.</summary>
    public Func<string, string?>? SchemaFor { get; set; }

    internal string? HostSchema(string name, out string? fault)
    {
        fault = null;
        try { return SchemaFor?.Invoke(name); }
        catch (Exception e) { fault = $"choosing the schema for {name} failed: {e.Message}"; return null; }   // found by A087: it took down a frame
    }

    /// <summary>The schema for a file, or null with why when the game's choice failed.</summary>
    internal string? SchemaOf(string name, JsonElement root, out string? fault) =>
        HostSchema(name, out fault) ?? (fault is null ? SchemaNameFor(name, root) : null);

    /// <summary>A game's last word before a file with unsaved edits is written: why it mustn't be, or null. For a check too
    /// slow to run on every edit, or one that needs several files, such as a game's own validator over the whole project.
    /// People and agents both meet it; the edits stay, unsaved, to be corrected. None by default.</summary>
    public Func<DataFile, string?>? SaveCheck { get; set; }

    /// <summary>Raised synchronously on the save caller's thread (the UI completion thread in BED) after dirty files
    /// have been committed and their sessions marked saved. A dirty single Save raises one notification; SaveAll
    /// raises one containing only files actually written, after the whole operation. No-op and refused saves do not
    /// raise it. The list is read-only. Observers cannot veto an already completed save; exceptions are traced and
    /// contained so other observers still run.</summary>
    public event Action<IReadOnlyList<DataFile>>? FilesSaved;

    internal void NotifySaved(IReadOnlyList<DataFile> files)
    {
        if (files.Count == 0 || FilesSaved is not { } observers) return;
        var saved = Array.AsReadOnly(files.Distinct().ToArray());
        foreach (Action<IReadOnlyList<DataFile>> observer in observers.GetInvocationList())
            try { observer(saved); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError($"A FilesSaved observer failed after the save completed: {error}"); }
    }


    /// <summary>A game's last word before a file is made, renamed, deleted or put back: why it mustn't be, or null. The other
    /// half of <see cref="SaveCheck"/>, which only sees edits: deleting a file others depend on writes no edit. People and
    /// agents both meet it, previews too. A check that throws is a refusal, and nothing changes. None by default.</summary>
    public Func<FileChange, string?>? FileCheck { get; set; }

    private string? AskFileCheck(FileChange change)
    {
        if (FileCheck is not { } check) return null;
        try { return check(change); }
        catch (Exception e) { return $"the check failed ({e.Message}), so nothing changed"; }
    }

    /// <summary>Asks <see cref="SaveCheck"/> about <paramref name="file"/>: why it mustn't be written, or null. A check that
    /// throws is a refusal, and one that leaves the draft changed or the file read-only has judged something else, so that's
    /// a refusal too: save again for a fresh answer.</summary>
    public string? AskSaveCheck(DataFile file)
    {
        if (SaveCheck is not { } check) return null;
        var session = file.Session;   // which draft is judged, as well as its revision (found by A186)
        var revision = session?.Revision;
        string? refused;
        Interlocked.Increment(ref judging);   // a workspace may ask from a worker (background saves), so the count is shared safely
        try { refused = check(file); }
        catch (Exception e) { return $"the check before saving failed ({e.Message}), so nothing was written. Your edits are still here."; }
        finally { Interlocked.Decrement(ref judging); }
        if (refused is not null) return refused;
        if (!ReferenceEquals(file.Session, session) || file.Session?.Revision != revision) return "it changed while it was being checked. Save again to have this version checked.";
        return ReadOnlyReason(file.Name);
    }

    internal Task<string?> CheckSaveGroupInBackground(Func<string?> check)
    {
        Interlocked.Increment(ref judging);
        return Task.Run(()=>{try{return check();}finally{Interlocked.Decrement(ref judging);}});
    }

    private int judging;

    /// <summary>Asks <see cref="SaveCheck"/> on a worker, for a save that mustn't stop the editor (a workspace's background
    /// saves). It counts as a check in progress from the moment it's asked, not when the worker starts, so no other save can
    /// slip in between.</summary>
    public Task<string?> AskSaveCheckInBackground(DataFile file)
    {
        Interlocked.Increment(ref judging);
        return Task.Run(() =>
        {
            try { return AskSaveCheck(file); }
            finally { Interlocked.Decrement(ref judging); }
        });
    }

    /// <summary>Whether a <see cref="SaveCheck"/> is running. Nothing is saved from inside one: it would write before the answer
    /// it's in the middle of giving.</summary>
    public bool Judging => Volatile.Read(ref judging) > 0;

    /// <summary>The project's files, by name in ordinal order, including any that have gone from disk since they were
    /// listed (<see cref="DataFileState.Missing"/>).</summary>
    public IReadOnlyList<DataFile> Files => files;

    /// <summary>The file called <paramref name="name"/> (<c>wares.json</c>), or null.</summary>
    public DataFile? Find(string name) => files.FirstOrDefault(f => f.Name == name);

    /// <summary>The open files with unsaved edits.</summary>
    public IReadOnlyList<DataFile> Dirty => files.Where(f => f.Dirty).ToList();

    /// <summary>Lists the folder again: new files are added; a file that's gone is kept, marked missing, so its unsaved
    /// edits aren't lost; a missing file that's back returns to unopened, or keeps its session.</summary>
    public void Refresh()
    {
        var onDisk = Paths.Files().Select(f => f.Name)
            .Where(n => !SharedFolder || !IsSchemaName(n)).ToHashSet(StringComparer.Ordinal);
        foreach (var name in onDisk.Where(n => files.All(f => f.Name != n))) files.Add(new DataFile(this, name));
        foreach (var f in files) f.Present = onDisk.Contains(f.Name);
        files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    /// <summary>Saves every dirty file that can go, judged together. Returns the files that weren't saved, each with why
    /// (<see cref="DataFile.Problems"/>); empty when all were. If one can't be written after all, those already written go back to what they
    /// were, unless something outside has changed them since, and the rest are judged again without it. Only a power cut leaves part of a
    /// group written with nothing said (<see cref="FileGroup"/>).</summary>
    public IReadOnlyList<DataFile> SaveAll()
    {
        if (typedAdmissions.Count > 0 && TypedSaveAll is {} save) return save();
        // Judged before anything's written. A refused file drops out, and the rest are judged again without it: one of them may have
        // been counting on it, and would otherwise be saved leaning on a draft that's staying a draft (found by A099, P03C).
        if (Judging) return Dirty;   // what it's judging stays as it was: nested, nothing moves
        var going = Dirty.ToList();
        var failed = new List<DataFile>();
        var committed = new List<DataFile>();
        try
        {
            while (true)
            {
                savingTogether = going;
                var revisions = going.ToDictionary(f => f, f => f.Session?.Revision);
                var (refused, why) = going.Select(f => (File: f, Why: f.WhyNotSaved())).FirstOrDefault(x => x.Why is not null);
                // A later file's check may have changed one already judged, or found it locked or read-only since (found by A101).
                if (refused is null) (refused, why) = going.Select(f => (File: f, Why: f.WhyNotNow(revisions[f]))).FirstOrDefault(x => x.Why is not null);
                // Then the writing, and if that fails after all, it's put back and treated like any other refusal (found by A102, L02).
                if (refused is null)
                {
                    var couldnt = WriteTogether(going, out var written);
                    committed.AddRange(written);
                    if (couldnt is null) break;
                    (refused, why) = couldnt.Value;
                    going.RemoveAll(f => !f.Dirty && f != refused);   // one that couldn't be put back is written, and says so
                }
                refused.Refuse(why!);
                failed.Add(refused);
                going.Remove(refused);
            }
        }
        finally { savingTogether = null; }
        NotifySaved(committed);
        return failed;
    }

    // Writes them all, or none. The file that wouldn't go and why, or null if all of them did.
    private static (DataFile File, string Why)? WriteTogether(IReadOnlyList<DataFile> group, out IReadOnlyList<DataFile> written)
    {
        var texts = group.Select(f => f.Session!.TextToSave).ToList();
        var result = FileGroup.Write([.. group.Select((f, i) => new GroupFile(f.Path, f.Session!.ExpectedOnDisk, texts[i]))]);
        // Each session is told only what's true of its file: one that holds something unknown stays a draft, and meets it as an outside change.
        var committed = new List<DataFile>();
        for (var i = 0; i < group.Count; i++)
            if (result.States[i] == GroupFileState.Written) { group[i].Written(texts[i]); committed.Add(group[i]); }
        written = committed;
        if (result.Failed is not { } failed) return null;
        var stuck = group.Where((_, i) => i != failed && result.States[i] != GroupFileState.AsItWas).Select(f => f.Name).ToList();
        var why = $"{result.Why} {DataFile.WhatToDo}";
        return (group[failed], stuck.Count == 0 ? why : $"{why} {string.Join(", ", stuck)} had already been written and couldn't be put back, so check it.");
    }

    private IReadOnlyList<DataFile>? savingTogether;
    internal Func<IReadOnlyList<DataFile>>? TypedSaveAll {get;set;}
    internal IReadOnlyList<DataFile>? SavingTogether {get=>savingTogether;set=>savingTogether=value;}
    internal bool HasTypedAdmission(string name)=>typedAdmissions.ContainsKey(name);


    /// <summary>The files that will be on disk with their edits if this save goes through: just <paramref name="file"/>, or all of
    /// them during <see cref="SaveAll"/>. A <see cref="SaveCheck"/> judges these drafts over what's on disk for the rest. Counting
    /// another file's unsaved edits would pass a save that can't be reopened; not counting them in a save of all
    /// would stop two files that depend on each other from ever being saved.</summary>
    public IReadOnlyList<DataFile> BeingSaved(DataFile file) => savingTogether ?? [file];

    private readonly List<(string Name, byte[] Bytes)> deleted = [];

    /// <summary>The schemas a new file can be made from: every schema file in <see cref="SchemaFolder"/> (only the
    /// <c>*.schema.json</c> ones when the data shares that folder).</summary>
    public IReadOnlyList<string> SchemaNames => Directory.EnumerateFiles(SchemaFolder, "*.json").Select(Path.GetFileName).OfType<string>()
        .Where(n => !SharedFolder || IsSchemaName(n)).Order(StringComparer.Ordinal).ToList();

    /// <summary>True when the data and its schemas live in one folder (as <c>data/wares.json</c> beside
    /// <c>data/wares.schema.json</c>). Then a <c>*.schema.json</c> file is a schema, never data, so it isn't listed or opened as
    /// data (found by A212: a co-located schema was listed and refused to open).</summary>
    public bool SharedFolder => string.Equals(Path.GetFullPath(DataFolder).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(SchemaFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool IsSchemaName(string name) => name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Files deleted with <see cref="Delete"/> since the project was opened, newest last, that
    /// <see cref="Restore"/> can put back.</summary>
    public IReadOnlyList<string> Deleted => deleted.Select(d => d.Name).ToList();

    /// <summary>Makes a new file called <paramref name="name"/> from the schema <paramref name="schema"/> and opens it: an
    /// object holding each property the schema requires (its default, or an empty value of its type), with a
    /// <c>$schema</c> field when the name alone wouldn't pick that schema. Refused, saying why, when the project or that
    /// name is read-only, the name isn't a plain <c>.json</c> file name or is taken, there's no such schema, or the
    /// schema needs more than an empty file can give. Never overwrites a file. With <paramref name="preview"/>, checks
    /// all of that and changes nothing. MIXED: Godot's and Unity's "new resource of type", over INVENTED schema-made
    /// skeletons.</summary>
    public bool Create(string name, string schema, out string why, bool preview = false)
    {
        if (Refusal(name, mustExist: false) is { } no) { why = no; return false; }
        if (Schemas.Find(schema) is not { } shape) { why = $"there's no schema called {schema} in {SchemaFolder}"; return false; }
        var root = new System.Text.Json.Nodes.JsonObject();
        var host = HostSchema(name, out var fault);
        if (fault is not null) { why = fault; return false; }
        if (host is not null && host != schema) { why = $"this project checks {name} against {host}, not {schema}"; return false; }   // found by A087: it said one and opened the other
        if (host is null && SchemaNameFor(name, JsonDocument.Parse("{}").RootElement) != schema) root["$schema"] = schema;
        if (shape.ValueKind == JsonValueKind.Object && shape.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var key in required.EnumerateArray().Select(r => r.GetString()).OfType<string>())
                root[key] = shape.TryGetProperty("properties", out var props) && props.TryGetProperty(key, out var prop) ? Empty(prop) : null;
        var document = new DataDocument(JsonSerializer.SerializeToElement(root));
        IValidator<DataDocument> validator = new DataValidator(Schemas, schema);
        if (RulesFor(name) is { } extra) validator = new AllOf<DataDocument>(validator, extra);
        var errors = validator.Validate(document).Where(i => i.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
        {
            why = $"a new {schema} file would start invalid ({string.Join(" ", errors.Take(3).Select(e => $"{e.Path}: {e.Message}"))}); make it by copying a file instead";
            return false;
        }
        var text = new DataFormat().Write(document);
        if (AskFileCheck(new FileChange(FileChangeKind.Create, name, null, text)) is { } stopped) { why = stopped; return false; }
        if (NotWritable() is { } denied) { why = $"{name} can't be made: {denied}"; return false; }
        if (Recursive && Refusal(name, mustExist: false) is { } changed) { why = changed; return false; }
        if (preview) { why = $"Would make {name} from {schema}: {JsonSerializer.Serialize(root)}"; return true; }
        try
        {
            if (Recursive) Paths.WriteNew(name, System.Text.Encoding.UTF8.GetBytes(text));
            else
            {
                using var stream = new FileStream(Paths.Target(name), FileMode.CreateNew); // never over a file made meanwhile
                stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = $"{name} can't be written: {e.Message}"; return false; }
        var made = Add(name);
        why = made.Open(schema) ? $"Made {name} from {schema}." : $"Made {name} from {schema}, but it won't open: {string.Join(" ", made.Problems.Select(p => p.Message))}";
        return true;
    }

    /// <summary>Copies supplied JSON text into a new admitted data file, preserving its text. Uses the project's
    /// schema, rules and whole-file check; never replaces an existing name. A preview performs the same checks
    /// without creating or opening a data file. The caller retains ownership of the source file.</summary>
    public bool CreateFromText(string name, string text, out string why, bool preview = false)
    {
        if (Refusal(name, mustExist: false) is { } no) { why = no; return false; }
        string schema;
        try
        {
            var document = new DataFormat().Read(text);
            var chosen = SchemaOf(name, document.Root, out var fault);
            if (fault is not null) { why = fault; return false; }
            if (chosen is null || Schemas.Find(chosen) is null)
            { why = $"{name} needs the schema '{chosen}', which isn't in {SchemaFolder}"; return false; }
            schema = chosen;
            IValidator<DataDocument> validator = new DataValidator(Schemas, schema);
            if (RulesFor(name) is { } extra) validator = new AllOf<DataDocument>(validator, extra);
            var errors = validator.Validate(document).Where(i => i.Severity == Severity.Error).ToList();
            if (errors.Count > 0)
            { why = $"{name} can't be copied: {string.Join(" ", errors.Select(e => $"{e.Path}: {e.Message}"))}"; return false; }
        }
        catch (DocumentFormatException e) { why = $"{name} can't be read: {e.Message}"; return false; }
        catch (Exception e) when (e is not OutOfMemoryException)
        { why = $"{name} can't be copied: one of the project's rules failed ({e.Message})"; return false; }
        if (AskFileCheck(new FileChange(FileChangeKind.Create, name, null, text)) is { } stopped)
        { why = stopped; return false; }
        // Host callbacks can change admission or create a competing file. Judge the current guard again.
        if (Refusal(name, mustExist: false) is { } changed) { why = changed; return false; }
        if (NotWritable() is { } denied) { why = $"{name} can't be made: {denied}"; return false; }
        if (preview) { why = $"Would copy {name}."; return true; }
        try
        {
            if (Recursive) Paths.WriteNew(name, System.Text.Encoding.UTF8.GetBytes(text));
            else
            {
                using var stream = new FileStream(Paths.Target(name), FileMode.CreateNew);
                stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { why = $"{name} can't be written: {e.Message}"; return false; }
        var made = Add(name);
        why = made.Open(schema) ? $"Copied {name}." : $"Copied {name}, but it won't open: {string.Join(" ", made.Problems.Select(p => p.Message))}";
        return true;
    }

    /// <summary>Renames the file <paramref name="from"/> to <paramref name="to"/>, keeping its contents and reopening it
    /// if it was open. Refused, saying why, when either name is read-only, the file has unsaved edits (save them first),
    /// the new name is taken or isn't a plain <c>.json</c> name, or the new name would give it a different schema (add a
    /// <c>$schema</c> field first). A game's reference map names files, so links into a renamed file need its map
    /// updated. With <paramref name="preview"/>, checks all of that and changes nothing.</summary>
    public bool Rename(string from, string to, out string why, bool preview = false)
    {
        if (Refusal(from, mustExist: true) is { } no) { why = no; return false; }
        if (Refusal(to, mustExist: false) is { } taken) { why = taken; return false; }
        var file = Find(from)!;
        if (file.Dirty) { why = $"{from} has unsaved edits: save them first"; return false; }
        string? before = null;
        try
        {
            var root = JsonDocument.Parse(File.ReadAllText(file.Path)).RootElement;
            before = SchemaOf(from, root, out var fault);
            var after = before is null ? null : SchemaOf(to, root, out fault);
            if (fault is not null) { why = fault; return false; }
            if (before != after)
            {
                why = $"as {to} it would be checked against {after}, not {before}: add \"$schema\": \"{before}\" to it first";
                return false;
            }
        }
        catch (JsonException) { }   // not JSON yet: it has no schema to keep
        // Deleted or locked by another program since it was listed: a reason, never a crash (found by A208).
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = $"{from} can't be read now: {e.Message}"; return false; }
        if (AskFileCheck(new FileChange(FileChangeKind.Rename, from, to)) is { } stopped) { why = stopped; return false; }
        if (NotWritable() is { } denied) { why = $"{from} can't be renamed: {denied}"; return false; }
        if (Recursive && (Refusal(from, true) ?? Refusal(to, false)) is { } changed) { why = changed; return false; }
        if (preview) { why = $"Would rename {from} to {to}."; return true; }
        try { File.Move(file.Path, Paths.Target(to)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = $"{from} can't be renamed: {e.Message}"; return false; }
        var wasOpen = file.Session is not null;
        file.Close();
        files.Remove(file);
        var renamed = Add(to);
        if (wasOpen) renamed.Open(before);   // the schema just checked: asking again, after the move, could answer differently (found by A087)
        why = $"Renamed {from} to {to}.";
        return true;
    }

    /// <summary>Deletes the file <paramref name="name"/> from the folder. Its bytes are kept until the project is closed,
    /// so <see cref="Restore"/> can put it back. Refused, saying why, when it's read-only or has unsaved edits (save them,
    /// or close it to drop them, first). With <paramref name="preview"/>, checks all of that and changes nothing.</summary>
    public bool Delete(string name, out string why, bool preview = false)
    {
        if (Refusal(name, mustExist: true) is { } no) { why = no; return false; }
        var file = Find(name)!;
        if (file.Dirty) { why = $"{name} has unsaved edits: save them, or close it to drop them, first"; return false; }
        if (AskFileCheck(new FileChange(FileChangeKind.Delete, name)) is { } stopped) { why = stopped; return false; }
        if (NotWritable(file.Path) is { } denied) { why = $"{name} can't be deleted: {denied}"; return false; }
        if (Recursive && Refusal(name, true) is { } changed) { why = changed; return false; }
        if (preview) { why = $"Would delete {name}."; return true; }
        try
        {
            var bytes = File.ReadAllBytes(file.Path);
            File.Delete(file.Path);
            deleted.Add((name, bytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = $"{name} can't be deleted: {e.Message}"; return false; }
        file.Close();
        files.Remove(file);
        why = $"Deleted {name}; Restore puts it back until the project is closed.";
        return true;
    }

    /// <summary>Puts back the last file called <paramref name="name"/> deleted with <see cref="Delete"/>, byte for byte.
    /// Refused, saying why, when none was deleted, the project or that name is read-only, or a file of that name exists
    /// again.</summary>
    public bool Restore(string name, out string why)
    {
        if (Paths.Refusal(name) is { } unsafePath) { why = unsafePath; return false; }
        var at = deleted.FindLastIndex(d => d.Name == name);
        if (at < 0) { why = $"{name} wasn't deleted here"; return false; }
        // A name the host marks read-only isn't written either, even to put a file back (found by A062).
        if (ReadOnlyReason(name) is { } readOnly) { why = ReadOnly ? readOnly : $"{name} is read-only: {readOnly}"; return false; }
        if (AskFileCheck(new FileChange(FileChangeKind.Restore, name, null, System.Text.Encoding.UTF8.GetString(deleted[at].Bytes))) is { } stopped) { why = stopped; return false; }
        try
        {
            if (Recursive) Paths.WriteNew(name, deleted[at].Bytes);
            else
            {
                using var stream = new FileStream(Paths.Target(name), FileMode.CreateNew);
                stream.Write(deleted[at].Bytes);
            }
        }
        catch (Exception e) when (e is IOException || Recursive && e is UnauthorizedAccessException) { why = $"{name} can't be put back: {e.Message}"; return false; }
        deleted.RemoveAt(at);
        if (Find(name) is { } kept) kept.Present = true; else Add(name);
        why = $"Restored {name}.";
        return true;
    }

    private DataFile Add(string name)
    {
        var file = new DataFile(this, name);
        files.Add(file);
        files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return file;
    }

    // Why creating, renaming or deleting the file called name is refused; null when it may go ahead.
    /// <summary>The longest file name a project takes: what one folder entry holds on Windows, macOS and Linux.</summary>
    public const int MostNameLength = 255;

    // Whether the data folder takes a new file now: a preview that says yes is then what the act finds (found by A208: a
    // write-denied folder said yes, then the act threw). It tries, with a file of its own that it removes at once.
    private string? NotWritable(string? file = null)
    {
        try
        {
            if (Paths.SafetyRefusal("probe.json") is { } unsafePath) return unsafePath;
            if (file is not null && Paths.Refusal(Path.GetRelativePath(DataFolder, file).Replace('\\', '/')) is { } unsafeFile) return unsafeFile;
            if (file is not null && File.Exists(file) && File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly))
                return $"{Path.GetFileName(file)} is marked read-only on disk";
            var probe = Path.Combine(DataFolder, $".brutal-write-check-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Said without the check's own file name, so the same refusal reads the same wherever it's asked.
            return e is IOException ? "the data folder can't be written now (the disk refused it)" : "the data folder can't be written (access is denied)";
        }
    }

    private string? Refusal(string name, bool mustExist)
    {
        if (Paths.Refusal(name) is { } unsafePath) return unsafePath;
        if (TypedAdmissionRefusal(name) is { } typed) return typed;
        if (ReadOnly) return "the project is open read-only";
        var leaf = Recursive ? name.Split('/')[^1] : name;
        if (leaf.Length == 0 || (!Recursive && name != Path.GetFileName(name)) || !name.EndsWith(".json", StringComparison.Ordinal) || leaf.StartsWith('.')
            || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return $"'{name}' isn't a plain file name ending in .json";
        // A name longer than a folder entry can hold is refused here, where a preview sees it, not only by the act (found by A208).
        if ((Recursive ? name.Split('/').Max(p => p.Length) : name.Length) > MostNameLength) return $"'{name[..20]}...' is {name.Length} characters; a file name can have at most {MostNameLength}";
        if (readOnlyFile?.Invoke(name) is { } why) return $"{name} is read-only: {why}";
        if (mustExist) return Find(name) is { Present: true } ? null : $"there's no file called {name} in the folder";
        return Find(name) is not null || File.Exists(Path.Combine(DataFolder, name)) ? $"there's already a file called {name}" : null;
    }

    // An empty value of a property's type, or its default.
    private static System.Text.Json.Nodes.JsonNode? Empty(JsonElement property)
    {
        if (property.ValueKind != JsonValueKind.Object) return null;
        if (property.TryGetProperty("default", out var d)) return System.Text.Json.Nodes.JsonNode.Parse(d.GetRawText());
        var type = property.TryGetProperty("type", out var t) ? t.ValueKind == JsonValueKind.Array ? t[0].GetString() : t.GetString() : null;
        return type switch
        {
            "array" => new System.Text.Json.Nodes.JsonArray(),
            "object" => new System.Text.Json.Nodes.JsonObject(),
            "string" => "",
            "integer" or "number" => 0,
            "boolean" => false,
            _ => null,
        };
    }

    /// <summary>The schema a data file names: its <c>$schema</c> field's file name, or else <c>name.schema.json</c>.</summary>
    public static string SchemaNameFor(string fileName, JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$schema", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } named
            ? Path.GetFileName(named) : Path.GetFileNameWithoutExtension(fileName) + ".schema.json";
}

/// <summary>What's being done to a file as a whole.</summary>
public enum FileChangeKind
{
    /// <summary>A new file is being made.</summary>
    Create,

    /// <summary>A file is getting a new name.</summary>
    Rename,

    /// <summary>A file is being deleted.</summary>
    Delete,

    /// <summary>A deleted file is being put back.</summary>
    Restore,

    /// <summary>A file the tools keep beside the data is being written or rewritten, such as an asset's record
    /// (<c>&lt;file&gt;.asset.json</c>) on import or when its declared origin changes; <see cref="FileChange.Text"/> is what it
    /// would hold.</summary>
    Replace,
}

/// <summary>A change to a file as a whole, for <see cref="DataProject.FileCheck"/> to judge.</summary>
/// <param name="Kind">What's being done.</param>
/// <param name="Name">The file's name.</param>
/// <param name="NewName">Its new name, for a rename.</param>
/// <param name="Text">What it would hold, for a new file, one being put back, or one being replaced.</param>
public sealed record FileChange(FileChangeKind Kind, string Name, string? NewName = null, string? Text = null);

/// <summary>Where a project's file stands.</summary>
public enum DataFileState
{
    /// <summary>Listed, not yet read.</summary>
    NotOpened,

    /// <summary>Open in a session.</summary>
    Open,

    /// <summary>Tried, and couldn't be opened; <see cref="DataFile.Problems"/> says why.</summary>
    Refused,

    /// <summary>Gone from the folder since it was listed.</summary>
    Missing,

    /// <summary>Open for repair (<see cref="DataFile.Repair"/>): it doesn't fit its schema yet, and edits may fix that.</summary>
    Repairing,
}

/// <summary>One data file in a <see cref="DataProject"/>.</summary>
public sealed class DataFile
{
    private readonly DataProject project;

    internal DataFile(DataProject project, string name)
    {
        this.project = project;
        Name = name;
    }

    /// <summary>Its file name, such as <c>wares.json</c>, or canonical relative path in a recursive project.</summary>
    public string Name { get; }

    /// <summary>Its full path.</summary>
    public string Path => System.IO.Path.Combine(project.DataFolder, Name);

    internal bool Present { get; set; } = true;

    /// <summary>Its session, once opened.</summary>
    public DocumentSession<DataDocument>? Session { get; private set; }

    /// <summary>The schema it was checked against, once opened or tried.</summary>
    public string? Schema { get; private set; }

    /// <summary>Why it couldn't be opened, or why the last save was refused; empty otherwise.</summary>
    public IReadOnlyList<Issue> Problems { get; private set; } = [];

    /// <summary>Where it stands. A file gone from disk is <see cref="DataFileState.Missing"/> even with a session: an open
    /// file is checked on disk each time (otherwise it would read as open until the folder was listed again).</summary>
    public DataFileState State => project.Paths.Refusal(Name) is not null ? DataFileState.Refused : !Present || Session is not null && !File.Exists(Path) ? DataFileState.Missing
        : Session is { Repairing: true } ? DataFileState.Repairing : Session is not null ? DataFileState.Open
        : project.HasTypedAdmission(Name) ? DataFileState.NotOpened : Problems.Count > 0 ? DataFileState.Refused : DataFileState.NotOpened;

    /// <summary>Why it may not be saved (the project is read-only, or the game marks it so), or null when it may.</summary>
    public string? ReadOnlyReason => project.ReadOnlyReason(Name);

    /// <summary>Whether it's open with unsaved edits.</summary>
    public bool Dirty => Session?.Dirty ?? false;

    /// <summary>Opens it, if it isn't already. Returns false, with <see cref="Problems"/> saying why, when the file is
    /// missing, isn't valid JSON, has no schema in the project, or isn't valid against its schema. Trying again reads the
    /// file again.</summary>
    public bool Open() => Open(null);

    internal bool Open(string? pinned)
    {
        if (project.Paths.Refusal(Name) is { } unsafePath) return Fail(unsafePath);
        if (project.TypedAdmissionRefusal(Name) is { } typed) return Fail(typed);
        if (Session is not null) return true;
        if (!File.Exists(Path)) { Present = false; return Fail($"{Name} isn't in the folder any more"); }
        // A pinned/adapter schema depends only on the admitted name. Avoid a preliminary read/parse
        // in that common case; DocumentSession.Open still reads, strictly parses and validates the file.
        string? fault = null;
        var schema = pinned ?? project.HostSchema(Name, out fault);
        if (fault is not null) return Fail(fault);
        if (schema is null)
        {
            try
            {
                using var parsed = JsonDocument.Parse(Canonical.ReadText(Path));
                schema = DataProject.SchemaNameFor(Name, parsed.RootElement);
            }
            catch (JsonException e) { return Fail($"{Name} isn't valid JSON: {e.Message}"); }
            catch (DocumentFormatException e) { return Fail($"{Name} {e.Message.Split(": ", 2)[^1]}"); }
            catch (IOException e) { return Fail($"{Name} can't be read: {e.Message}"); }
        }
        Schema = schema;
        if (project.Schemas.Find(Schema) is null) return Fail($"{Name} needs the schema '{Schema}', which isn't in {project.SchemaFolder}");
        try
        {
            IValidator<DataDocument> validator = new DataValidator(project.Schemas, Schema);
            if (project.RulesFor(Name) is { } extra) validator = new AllOf<DataDocument>(validator, extra);
            if (project.Paths.Refusal(Name) is { } changed) return Fail(changed);
            Session = DocumentSession<DataDocument>.Open(Path, new DataFormat(), validator, DataKit.Catalog());
            BindAccessPolicy();
            Problems = [];
            return true;
        }
        catch (InvalidDocumentException e) { Problems = e.Issues; return false; }
        catch (DocumentFormatException e) { return Fail($"{Name} can't be read: {e.Message}"); }
        // A project's rule is someone else's code: one that fails on the file keeps it closed, saying so, never a crash (found by A200).
        catch (Exception e) when (e is not OutOfMemoryException) { return Fail($"{Name} can't open: one of the project's rules failed on it ({e.Message}). That's a fault in the rule, not your data"); }
    }

    /// <summary>Opens it for repair when it reads as JSON and has its schema but doesn't fit it, so its problems can be fixed
    /// in the editor rather than by hand (<see cref="DocumentSession{TDoc}.OpenForRepair"/>: an edit is accepted when it
    /// adds no problem). Opens it normally when it's valid. Returns false, with <see cref="Problems"/> saying why, when it
    /// can't be read as JSON or has no schema.</summary>
    public bool Repair()
    {
        if (project.Paths.Refusal(Name) is { } unsafePath) return Fail(unsafePath);
        if (project.TypedAdmissionRefusal(Name) is { } typed) return Fail(typed);
        if (Open()) return true;
        if (Schema is null || project.Schemas.Find(Schema) is null) return false;
        IValidator<DataDocument> validator = new DataValidator(project.Schemas, Schema);
        if (project.RulesFor(Name) is { } extra) validator = new AllOf<DataDocument>(validator, extra);
        try
        {
            if (project.Paths.Refusal(Name) is { } changed) return Fail(changed);
            Session = DocumentSession<DataDocument>.OpenForRepair(Path, new DataFormat(), validator, DataKit.Catalog());
            BindAccessPolicy();
            Problems = Session.Problems;
            return true;
        }
        catch (DocumentFormatException e) { return Fail($"{Name} can't be read: {e.Message}"); }
    }

    private void BindAccessPolicy()
    {
        if (project.Recursive)
            Session!.FileAccessRefusal = path => project.Paths.Refusal(System.IO.Path.GetRelativePath(project.DataFolder, path).Replace('\\', '/'));
    }

    /// <summary>Saves its edits. Refused, returning false with <see cref="Problems"/> saying why, when the project or
    /// this file is read-only (<see cref="ReadOnlyReason"/>), the file isn't open, or it was changed outside the editor since it was opened or saved (reload it, or
    /// keep the outside version, first) or deleted outside (it isn't silently put back). Saving a file with no edits does
    /// nothing and returns true.</summary>
    public bool Save()
    {
        if (project.Judging) return Fail($"{Name} wasn't saved: a check before saving is still running");
        if (WhyNotSaved() is { } why) return Fail(why);
        return Session!.Dirty ? Write() : true;
    }

    // Why it mustn't be written now, or null. Nothing changes.
    internal string? WhyNotSaved()
    {
        if (Session is null) return $"{Name} isn't open";
        if (ReadOnlyReason is { } why) return $"{Name} wasn't saved: {why}";
        if (Outside() is { } changed) return changed;
        if (!Session.Dirty) return null;
        if (project.AskSaveCheck(this) is { } refused) return $"{Name} wasn't saved: {refused}";
        // The check can take seconds, so look again (found by A082).
        return Outside() ?? InUse();
    }

    // The same, without asking the check again, for a file judged a moment ago.
    internal string? WhyNotNow(long? revision)
    {
        if (Session is null) return $"{Name} isn't open";
        if (Session.Revision != revision) return $"{Name} wasn't saved: it changed while the others were being checked. Save again to have this version checked.";
        if (ReadOnlyReason is { } why) return $"{Name} wasn't saved: {why}";
        return Outside() ?? InUse();
    }

    // A file another program has open reads perfectly well, and only refuses when it's replaced: too late for the files that
    // depended on it (found by A100, P03E). Opening it to write changes nothing, and asks the same question first.
    private string? InUse()
    {
        try
        {
            using var _ = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"{Name} can't be written: {e.Message} {WhatToDo}"; }
    }

    internal const string WhatToDo = "If another program has it open, close that and save again. Your edits are still here.";

    internal void ClearProblems()=>Problems=[];

    internal void Written(string text)
    {
        Session!.MarkSaved(text);
        Problems = [];
    }

    private string? Outside() => Session!.CheckDisk() switch
    {
        DiskState.ChangedOnDisk => $"{Name} was changed outside the editor; reload it or keep your version before saving",
        // A file deleted outside isn't silently put back: that may undo a deliberate deletion (found by A049).
        DiskState.Missing => $"{Name} was deleted outside the editor; nothing was saved. Use Save as to keep your version",
        _ => null,
    };

    internal bool Write()
    {
        if (project.Paths.Refusal(Name) is { } unsafePath) return Fail(unsafePath);
        var wasDirty = Session!.Dirty;
        try { Session.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail($"{Name} can't be written: {e.Message} {WhatToDo}"); }
        Problems = [];
        if (wasDirty) project.NotifySaved([this]);
        return true;
    }

    internal void Refuse(string why) => Fail(why);

    /// <summary>Drops the session (and any unsaved edits) so the file is read afresh next time it's opened.</summary>
    public void Close()
    {
        Session = null;
        Problems = [];
    }

    private bool Fail(string message)
    {
        Problems = [Issue.Error(message)];
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({State})";
}
