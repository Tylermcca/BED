using System.Reflection;
using System.Text.Json;

namespace Bed.Data;

/// <summary>What following a value through a <see cref="ReferenceMap"/> found.</summary>
public enum ReferenceOutcome
{
    /// <summary>The map says nothing about this place.</summary>
    NotAReference,

    /// <summary>Exactly one item has this id; <see cref="ReferenceResult.Target"/> is it.</summary>
    Found,

    /// <summary>The value is one of the place's sentinels: deliberately not an id.</summary>
    Sentinel,

    /// <summary>No item has this id.</summary>
    Missing,

    /// <summary>More than one item has this id, so it isn't followed.</summary>
    Ambiguous,

    /// <summary>The target file isn't in the project, or can't be opened.</summary>
    NotLoaded,

    /// <summary>The place's target depends on a scope the map gives only in words; this version doesn't follow it.</summary>
    NeedsScope,
}

/// <summary>Which meaning of a place to follow when its key and its value are both links: the key, for an object's
/// key naming something, or the value at it.</summary>
public enum ReferenceIntent
{
    /// <summary>The place's key, from a <c>keys</c> entry.</summary>
    Key = 1,

    /// <summary>The value at the place, from an ordinary entry.</summary>
    Value = 2,
}

/// <summary>An item in a project: its file and its JSON Pointer.</summary>
/// <param name="File">The target data file name within the project.</param>
/// <param name="Pointer">The JSON Pointer to the target item within that file.</param>
public sealed record ReferenceTarget(string File, string Pointer);

/// <summary>What following a value found, with the target when there's exactly one.</summary>
/// <param name="Parts">For an id in parts, each part's target in order (a part that isn't an id has none); the main
/// <see cref="Target"/> is then the last part found.</param>
/// <param name="Outcome">The outcome of trying to follow the reference.</param>
/// <param name="Target">The resolved target when available, or null. For an id in parts, see the separate Parts description.</param>
/// <param name="Note">An optional explanation of the outcome, such as why the reference could not be followed.</param>
public sealed record ReferenceResult(ReferenceOutcome Outcome, ReferenceTarget? Target = null, string? Note = null, IReadOnlyList<ReferenceTarget>? Parts = null);

/// <summary>
/// A game's reference map (<c>docs/spec/schemas/reference_map.schema.json</c>): which lists take their labels from which
/// field, and which fields hold ids of items elsewhere in the project, across files. It describes a game's data without
/// changing its schemas. Read one with <see cref="Read"/>; ask it with <see cref="LabelField"/> and <c>Resolve</c>.
/// <para>
/// A place is a JSON Pointer with <c>*</c> for any index or key. A place holding a list of ids matches each item's
/// pointer too. <c>keys</c> places hold ids as an object's keys. A target's <c>key</c> is a field of each item, or
/// <c>$key</c> (an object's keys) or <c>$value</c> (a list of plain ids). <c>also</c> adds more targets; an id found more
/// than once across all of them is ambiguous and isn't followed. Sentinels are never ids.
/// </para>
/// INVENTED: the map format and its resolver; MIXED with CastleDB's typed references and Godot's resource UIDs in
/// spirit, never resolving by spelling or by row number.
/// </summary>
public sealed class ReferenceMap
{
    private readonly List<LabelRule> labels;
    private readonly List<Entry> references;

    private sealed record LabelRule(string File, string[] List, string? Field, string? Template);
    private sealed record Target(string File, string[] List, string Key, IReadOnlyList<Clause>? Where = null);
    private sealed record Clause(string? At, string? EqualsSource, string? Absent);
    private sealed record Split(string Separator, IReadOnlyList<Target?> Targets);
    private sealed record Narrow(string By, string File, string[] List, string Key);
    private sealed record Entry(string File, string[] At, bool Keys, IReadOnlyList<Target> Targets, HashSet<string> Sentinels, bool Scoped,
        Split? Parts, Narrow? Within);

    private ReferenceMap(string game, List<LabelRule> labels, List<Entry> references)
    {
        Game = game;
        this.labels = labels;
        this.references = references;
    }

    /// <summary>Whose data it describes.</summary>
    public string Game { get; }

    /// <summary>How many reference places it lists.</summary>
    public int Count => references.Count;

    private static JsonSchemas? format;

    /// <summary>The map's own schema, from the tools.</summary>
    public static JsonSchemas Format => format ??= LoadFormat();

    private static JsonSchemas LoadFormat()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("reference_map.schema.json")
            ?? throw new InvalidOperationException("the reference map schema isn't embedded");
        return new JsonSchemas().Add("reference_map.schema.json", new StreamReader(s).ReadToEnd());
    }

    /// <summary>Reads a map. Throws <see cref="JsonException"/> when it isn't JSON, and <see cref="InvalidDocumentException"/>
    /// with every problem when it doesn't fit the format.</summary>
    public static ReferenceMap Read(string text)
    {
        var root = JsonDocument.Parse(text).RootElement.Clone();
        var issues = Format.Validate(root, "reference_map.schema.json");
        if (issues.HasErrors()) throw new InvalidDocumentException(issues);
        static string[] Parts(JsonElement e) => Pointer.Split(e.GetString()!).ToArray();
        static Target TargetOf(JsonElement t) => new(t.GetProperty("file").GetString()!, Parts(t.GetProperty("list")), t.GetProperty("key").GetString()!,
            t.TryGetProperty("where", out var w)
                ? w.EnumerateArray().Select(c => new Clause(Str(c, "at"), Str(c, "equalsSource"), Str(c, "absent"))).ToList()
                : null);
        static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var labels = root.GetProperty("labels").EnumerateArray()
            .Select(l => new LabelRule(l.GetProperty("file").GetString()!, Parts(l.GetProperty("list")), Str(l, "field"), Str(l, "template"))).ToList();
        var references = root.GetProperty("references").EnumerateArray().Select(r => new Entry(
            r.GetProperty("file").GetString()!,
            Parts(r.GetProperty("at")),
            r.TryGetProperty("keys", out var k) && k.ValueKind == JsonValueKind.True,
            [TargetOf(r.GetProperty("target")), .. (r.TryGetProperty("also", out var also) ? also.EnumerateArray().Select(TargetOf) : [])],
            r.TryGetProperty("sentinels", out var s) ? s.EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal) : [],
            r.TryGetProperty("scope", out _),
            r.TryGetProperty("parts", out var split)
                ? new Split(split.GetProperty("separator").GetString()!, split.GetProperty("targets").EnumerateArray().Select(t => t.ValueKind == JsonValueKind.Null ? null : TargetOf(t)).ToList())
                : null,
            r.TryGetProperty("narrow", out var n)
                ? new Narrow(n.GetProperty("by").GetString()!, n.GetProperty("file").GetString()!, Parts(n.GetProperty("list")), n.GetProperty("key").GetString()!)
                : null)).ToList();
        return new ReferenceMap(root.GetProperty("game").GetString()!, labels, references) { Text = text };
    }

    /// <summary>The map as it was read, so a check's snapshot can carry exactly this map.</summary>
    public string Text { get; private init; } = "";

    /// <summary>The field that labels the item at <paramref name="itemPointer"/> in <paramref name="file"/>, or null when
    /// the map doesn't say, or labels it with a template (see <see cref="Label"/>).</summary>
    public string? LabelField(string file, string itemPointer) => LabelFor(file, itemPointer)?.Field;

    /// <summary>The label the map gives <paramref name="item"/>, at <paramref name="itemPointer"/> in
    /// <paramref name="file"/>: its label field's text, or its template with each <c>{field}</c> replaced by that field's
    /// value (text as it is, numbers and true/false as written, a missing field as nothing). Null when the map doesn't
    /// label it, or every field it names is missing, so the editor's own guess stands.</summary>
    public string? Label(string file, string itemPointer, JsonElement item)
    {
        if (LabelFor(file, itemPointer) is not { } l || item.ValueKind != JsonValueKind.Object) return null;
        static string? Text(JsonElement o, string f) => o.TryGetProperty(f, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
            _ => null,
        } : null;
        if (l.Field is { } field) return Text(item, field) is { Length: > 0 } t ? t : null;
        var any = false;
        var label = System.Text.RegularExpressions.Regex.Replace(l.Template!, "[{]([^{}]+)[}]", m =>
        {
            var t = Text(item, m.Groups[1].Value);
            any |= t is not null;
            return t ?? "";
        });
        return any ? label : null;
    }

    private LabelRule? LabelFor(string file, string itemPointer)
    {
        var parts = Pointer.Split(itemPointer);
        return parts.Count == 0 ? null : labels.FirstOrDefault(l => l.File == file && Matches(l.List, parts.Take(parts.Count - 1).ToList()));
    }

    /// <summary>
    /// Follows the value at <paramref name="pointer"/> in <paramref name="file"/>: a field's value, an item of a list of
    /// ids, or (for <c>keys</c> places) a key, whose id is the pointer's last part. Target files are opened through
    /// <paramref name="project"/> as needed. Never guesses: a place the map doesn't list is
    /// <see cref="ReferenceOutcome.NotAReference"/>.
    /// </summary>
    public ReferenceResult Resolve(DataProject project, string file, string pointer, JsonElement value)
    {
        // A table asks this of every link cell it draws, every frame (found by A179), and each answer matches the map's places
        // and looks its targets up again. So an answer is kept while the files it read are the same: the same file in the
        // project, the same session, the same document (documents don't change: an edit makes a new one). An answer that found
        // a file not open isn't kept, so opening it is tried again, as before.
        var key = (file, pointer, value.ValueKind, value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null });
        var answers = resolved.GetOrCreateValue(project);
        lock (answers)
            if (answers.TryGetValue(key, out var kept) && Fresh(project, kept.Read)) return kept.Result;
        var parts = Pointer.Split(pointer);
        var parent = parts.Take(Math.Max(0, parts.Count - 1)).ToList();
        var matching = references.Where(e => e.File == file && (e.Keys ? Matches(e.At, parent) : Matches(e.At, parts) || Matches(e.At, parent) && IsIndex(parts[^1]))).ToList();
        var entry = matching.FirstOrDefault();
        // Two rules with the same meaning on one place: never pick one, here as in the explicit-intent route, the check and the
        // reverse search, which all come through here (found by A205). A key rule beside a value rule is two meanings (A050).
        var result = entry is null ? new(ReferenceOutcome.NotAReference)
            : matching.Count(e => e.Keys == entry.Keys) > 1
                ? new(ReferenceOutcome.Ambiguous, Note: $"{matching.Count(e => e.Keys == entry.Keys)} map entries describe this place's {(entry.Keys ? "key" : "value")}")
                : ResolveWith(entry, project, file, parts, value);
        if (FilesRead(project, entry) is { } read)
            lock (answers)
            {
                if (answers.Count >= MostKept) answers.Clear();
                answers[key] = (result, read);
            }
        return result;
    }

    private const int MostKept = 100_000;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataProject,
        Dictionary<(string File, string Pointer, JsonValueKind Kind, string? Text), (ReferenceResult Result, (string Name, DataFile File, object Session, object Document)[] Read)>> resolved = new();

    // Every file an entry's answer can depend on, each as it is now; null when one isn't in the project or isn't open.
    private static (string Name, DataFile File, object Session, object Document)[]? FilesRead(DataProject project, Entry? entry)
    {
        if (entry is null) return [];
        var names = new HashSet<string>(StringComparer.Ordinal) { entry.File };
        foreach (var t in entry.Targets) names.Add(t.File);
        foreach (var t in entry.Parts?.Targets.OfType<Target>() ?? []) names.Add(t.File);
        if (entry.Within is { } w) names.Add(w.File);
        var read = new List<(string, DataFile, object, object)>();
        foreach (var name in names)
        {
            if (project.Find(name) is not { Session: { } session } f) return null;
            read.Add((name, f, session, session.Current));
        }
        return [.. read];
    }

    private static bool Fresh(DataProject project, (string Name, DataFile File, object Session, object Document)[] read)
    {
        foreach (var (name, file, session, document) in read)
            if (!ReferenceEquals(Named(project, name), file) || !ReferenceEquals(file.Session, session) || !ReferenceEquals(file.Session!.Current, document)) return false;
        return true;
    }

    // As DataProject.Find, without its allocation: asked for every kept answer.
    private static DataFile? Named(DataProject project, string name)
    {
        var files = project.Files;
        for (var i = 0; i < files.Count; i++)
            if (files[i].Name == name) return files[i];
        return null;
    }

    /// <summary>
    /// As <see cref="Resolve(DataProject, string, string, JsonElement)"/>, following only the meaning asked for, where a
    /// place's key and its value are both links: <see cref="ReferenceIntent.Key"/> follows a <c>keys</c> entry
    /// (the pointer's last part is the id), <see cref="ReferenceIntent.Value"/> an ordinary one.
    /// </summary>
    public ReferenceResult Resolve(DataProject project, string file, string pointer, JsonElement value, ReferenceIntent intent)
    {
        var parts = Pointer.Split(pointer);
        var parent = parts.Take(Math.Max(0, parts.Count - 1)).ToList();
        var entries = references.Where(e => e.File == file && (intent == ReferenceIntent.Key
            ? e.Keys && Matches(e.At, parent)
            : !e.Keys && (Matches(e.At, parts) || Matches(e.At, parent) && parts.Count > 0 && IsIndex(parts[^1])))).ToList();
        return entries.Count switch
        {
            0 => new(ReferenceOutcome.NotAReference),
            1 => ResolveWith(entries[0], project, file, parts, value),
            // Two rules claiming one place for the same meaning: never pick one (A050; found by A056).
            _ => new(ReferenceOutcome.Ambiguous, Note: $"{entries.Count} map entries describe this place's {(intent == ReferenceIntent.Key ? "key" : "value")}"),
        };
    }

    // Reverse searches enumerate many unchanged links after one file changes. Keep each entry's answer under
    // exactly the same file/session/immutable-document dependencies as the existing cell resolver.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataProject,
        Dictionary<(Entry Entry, string Pointer, JsonValueKind Kind, string? Text), (ReferenceResult Result, (string Name, DataFile File, object Session, object Document)[] Read)>> entryResults = new();

    private ReferenceResult ResolveEntry(Entry entry, DataProject project, string file, string pointer, IReadOnlyList<string> parts, JsonElement value)
    {
        var key = (entry, pointer, value.ValueKind, value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null });
        var answers = entryResults.GetOrCreateValue(project);
        lock (answers)
            if (answers.TryGetValue(key, out var kept) && Fresh(project, kept.Read)) return kept.Result;
        var result = ResolveWith(entry, project, file, parts, value);
        if (FilesRead(project, entry) is { } read)
            lock (answers)
            {
                if (answers.Count >= MostKept) answers.Clear();
                answers[key] = (result, read);
            }
        return result;
    }

    private ReferenceResult ResolveWith(Entry entry, DataProject project, string file, IReadOnlyList<string> parts, JsonElement value)
    {
        string? id = entry.Keys ? (parts.Count > 0 ? parts[^1] : null)
            : value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null };
        // true or false where the map says an id goes can't be one: reported, never counted as a clean link (found by A205).
        // An object, a list or null there is no link at all.
        if (id is null && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new(ReferenceOutcome.Missing, Note: $"{value.GetRawText()} is here, where the map says an id goes");
        if (id is null) return new(ReferenceOutcome.NotAReference, Note: "this value isn't an id");
        if (entry.Sentinels.Contains(id)) return new(ReferenceOutcome.Sentinel);
        if (entry.Scoped && entry.Within is null) return new(ReferenceOutcome.NeedsScope, Note: "this place's target depends on its context, which the map gives only in words");
        string? inside = null;
        JsonElement? sourceRoot = null;
        if (entry.Targets.Concat(entry.Parts?.Targets.OfType<Target>() ?? []).Any(t => t.Where is not null))
        {
            if (project.Find(file) is not { } src || !src.Open()) return new(ReferenceOutcome.NotLoaded, Note: $"{file} isn't open in the project");
            sourceRoot = src.Session!.Current.Root;
        }
        var source = sourceRoot is { } sr ? (sr, parts) : ((JsonElement, IReadOnlyList<string>)?)null;
        if (entry.Within is { } w)
        {
            if (project.Find(file) is not { } within || !within.Open()) return new(ReferenceOutcome.NotLoaded, Note: $"{file} isn't open in the project");
            if (Relative(within.Session!.Current.Root, parts, w.By) is not { } context)
                return new(ReferenceOutcome.Missing, Note: $"the value narrowing this link ({w.By}) isn't there");
            var containers = Find(project, new Target(w.File, w.List, w.Key), context, out var unloaded);
            if (unloaded is not null) return unloaded;
            if (containers.Count != 1)
                return new(containers.Count == 0 ? ReferenceOutcome.Missing : ReferenceOutcome.Ambiguous,
                    Note: $"{containers.Count} items in {w.File} have the id '{context}' this link is narrowed to");
            inside = containers[0].Pointer + "/";
        }
        if (entry.Parts is { } split)
        {
            var pieces = id.Split(split.Separator);
            if (pieces.Length != split.Targets.Count)
                return new(ReferenceOutcome.Missing, Note: $"'{id}' should have {split.Targets.Count} parts separated by '{split.Separator}'");
            var each = new List<ReferenceTarget>();
            for (var i = 0; i < pieces.Length; i++)
            {
                if (split.Targets[i] is not { } t) continue;
                var r = One(Find(project, t, pieces[i], out var partUnloaded, inside, source), pieces[i], partUnloaded);
                if (r.Outcome != ReferenceOutcome.Found) return r with { Note = $"part {i + 1} of '{id}': {r.Note}" };
                each.Add(r.Target!);
            }
            return each.Count == 0 ? new(ReferenceOutcome.NotAReference, Note: "no part of this id is an id") : new(ReferenceOutcome.Found, each[^1], Parts: each);
        }
        var found = new List<ReferenceTarget>();
        foreach (var t in entry.Targets)
        {
            found.AddRange(Find(project, t, id, out var targetUnloaded, inside, source));
            if (targetUnloaded is not null) return targetUnloaded;
        }
        return One(found, id, null);
    }

    private static ReferenceResult One(List<ReferenceTarget> found, string id, ReferenceResult? notLoaded) => notLoaded ?? (found.Count switch
    {
        0 => new(ReferenceOutcome.Missing, Note: $"nothing has the id '{id}'"),
        1 => new(ReferenceOutcome.Found, found[0]),
        _ => new(ReferenceOutcome.Ambiguous, Note: $"{found.Count} items have the id '{id}'"),
    });

    // The items of one target with this id, only those inside a container when narrowed and meeting its where clauses;
    // stop is set when the target's file can't be read, or a where clause's source value isn't there (NeedsScope).
    private static List<ReferenceTarget> Find(DataProject project, Target t, string id, out ReferenceResult? stop, string? inside = null,
        (JsonElement Root, IReadOnlyList<string> Parts)? source = null)
    {
        stop = null;
        if (project.Find(t.File) is not { } target || !target.Open())
        {
            stop = new(ReferenceOutcome.NotLoaded, Note: $"{t.File} isn't open in the project");
            return [];
        }
        var root = target.Session!.Current.Root;
        // A where clause's source operands are read once: one that isn't there stops the lookup rather than skip a clause.
        var wanted = new List<(Clause Clause, JsonElement? Source)>();
        foreach (var c in t.Where ?? [])
        {
            if (c.EqualsSource is null) { wanted.Add((c, null)); continue; }
            if (source is not { } s || RelativeElement(s.Root, s.Parts, c.EqualsSource) is not { } v || v.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                stop = new(ReferenceOutcome.NeedsScope, Note: $"the value this link is matched against ({c.EqualsSource}) isn't there");
                return [];
            }
            wanted.Add((c, v));
        }
        var found = new List<ReferenceTarget>();
        foreach (var (listPointer, list) in Places(root, t.List, ""))
            foreach (var p in Items(list, listPointer, t.Key, id))
            {
                if (inside is not null && !p.StartsWith(inside, StringComparison.Ordinal)) continue;
                if (wanted.All(w => Meets(root, p, w.Clause, w.Source))) found.Add(new ReferenceTarget(t.File, p));
            }
        return found;
    }

    // Whether the candidate item at pointer meets one where clause: its field equal to the source value (typed: numbers by
    // value, text exactly, never text against a number), or a field it lacks.
    private static bool Meets(JsonElement root, string pointer, Clause c, JsonElement? source)
    {
        var parts = Pointer.Split(pointer);
        if (c.Absent is { } name)
            return RelativeElement(root, parts, "0") is { ValueKind: JsonValueKind.Object } item && !item.TryGetProperty(name, out _);
        return RelativeElement(root, parts, c.At!) is { } mine && mine.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)
            && source is { } s && JsonSchemas.Same(mine, s);
    }

    // A Relative JSON Pointer (BORROWED: the IETF draft, draft-bhutton-relative-json-pointer) from the value at parts:
    // n levels up, then "#" for the key or index there, or a JSON Pointer below it. The id it names, as text, or null.
    private static string? Relative(JsonElement root, IReadOnlyList<string> parts, string relative) =>
        RelativeElement(root, parts, relative) is { } e
            ? e.ValueKind switch { JsonValueKind.String => e.GetString(), JsonValueKind.Number => e.GetRawText(), _ => null }
            : null;

    // The value a Relative JSON Pointer names, or null; "#" gives the key (or index) as text.
    private static JsonElement? RelativeElement(JsonElement root, IReadOnlyList<string> parts, string relative)
    {
        var digits = relative.TakeWhile(char.IsAsciiDigit).Count();
        var up = int.Parse(relative[..digits], System.Globalization.CultureInfo.InvariantCulture);
        if (up > parts.Count) return null;
        var at = parts.Take(parts.Count - up).ToList();
        var rest = relative[digits..];
        if (rest == "#")
        {
            if (at.Count == 0) return null;
            // A list's index is a number, an object's key text (the Relative JSON Pointer draft; found by A056).
            var holder = RelativeElement(root, at, "1");
            return holder is { ValueKind: JsonValueKind.Array } && int.TryParse(at[^1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
                ? JsonSerializer.SerializeToElement(index)
                : JsonSerializer.SerializeToElement(at[^1]);
        }
        return ElementAt(root, at.Concat(rest.Length == 0 ? [] : Pointer.Split(rest)));
    }

    // Walk already-decoded pointer parts; relative ascent and the typed # result stay in RelativeElement.
    private static JsonElement? ElementAt(JsonElement root, IEnumerable<string> parts)
    {
        var node = root;
        foreach (var part in parts)
        {
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(part, out var child)) node = child;
            else if (node.ValueKind == JsonValueKind.Array && int.TryParse(part, out var i) && i >= 0 && i < node.GetArrayLength()) node = ListIndex.Item(node, i);
            else return null;
        }
        return node;
    }

    /// <summary>How many links in other files point at items in <paramref name="file"/> (each top-level list's items):
    /// what deleting it would leave naming nothing, or renaming it would leave pointing at the old name. Unsaved edits
    /// count. 0 when it can't be opened.</summary>
    public int LinksInto(DataProject project, string file)
    {
        if (project.Find(file) is not { } f || !f.Open()) return 0;
        var root = f.Session!.Current.Root;
        if (root.ValueKind != JsonValueKind.Object) return 0;
        var count = 0;
        foreach (var top in root.EnumerateObject())
        {
            var list = Pointer.Join([top.Name]);
            var items = top.Value.ValueKind switch
            {
                JsonValueKind.Array => Enumerable.Range(0, top.Value.GetArrayLength()).Select(i => list + "/" + i),
                JsonValueKind.Object => top.Value.EnumerateObject().Select(e => list + Pointer.Join([e.Name])),
                _ => [],
            };
            foreach (var item in items) count += ReferencesTo(project, new ReferenceTarget(file, item)).Count(r => r.File != file);
        }
        return count;
    }

    /// <summary>
    /// Everything that refers to <paramref name="target"/>: every value at every place in the map, across the project's
    /// files, that resolves to it (for an id in parts, any part). Each is the place it's at. Files that can't be opened are
    /// skipped. It resolves with <c>Resolve</c>, so "refers" means exactly what following a link means. INVENTED
    /// (the user's gap list, 2026-11-01: "what refers to this?").
    /// </summary>
    public IReadOnlyList<ReferenceTarget> ReferencesTo(DataProject project, ReferenceTarget target) =>
        Links(project).Where(l => l.Result.Outcome == ReferenceOutcome.Found && (l.Result.Target == target || l.Result.Parts?.Contains(target) == true))
            .Select(l => l.At).Distinct().ToList();

    /// <summary>Everything that refers to <paramref name="target"/> or to anything inside it: what removing it would break (found by
    /// A205: a link to a child of the selected item wasn't counted, and the item was called safe to remove).</summary>
    public IReadOnlyList<ReferenceTarget> ReferencesInto(DataProject project, ReferenceTarget target)
    {
        bool Inside(ReferenceTarget t) => t.File == target.File && (t.Pointer == target.Pointer || t.Pointer.StartsWith(target.Pointer + "/", StringComparison.Ordinal));
        return Links(project).Where(l => l.Result.Outcome == ReferenceOutcome.Found && (l.Result.Target is { } t && Inside(t) || l.Result.Parts?.Any(Inside) == true))
            .Select(l => l.At).Distinct().ToList();
    }

    /// <summary>Every link the map describes in the project: each place holding one, and what it resolves to, with each
    /// map entry's own meaning (a keys entry's key, an ordinary entry's value); a place two entries of one meaning describe is
    /// one ambiguous link. Files that can't be opened are skipped.
    /// What a link check reports, and what <see cref="ReferencesTo"/> searches.</summary>
    public IEnumerable<(ReferenceTarget At, ReferenceResult Result)> Links(DataProject project)
    {
        foreach (var entry in references)
        {
            if (project.Find(entry.File) is not { } source || !source.Open()) continue;
            var root = source.Session!.Current.Root;
            var places = new List<(string At, JsonElement Value)>();
            if (entry.Keys)
            {
                foreach (var (at, obj) in Places(root, entry.At, ""))
                    if (obj.ValueKind == JsonValueKind.Object)
                        foreach (var p in obj.EnumerateObject()) places.Add((at + Pointer.Join([p.Name]), default));
            }
            else
                foreach (var (at, value) in Places(root, entry.At, ""))
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        var i = 0;
                        foreach (var item in value.EnumerateArray()) places.Add(($"{at}/{i++}", item));
                    }
                    else places.Add((at, value));
            foreach (var (at, value) in places)
            {
                var parts = Pointer.Split(at);
                // Each entry's own meaning (found by A056): a key rule and a value rule on one place are two links. Two rules of the same
                // meaning on one place are one ambiguous link, said once, by the first of them, as every other route says (found by A205).
                var same = SameMeaning(entry, parts, at);
                if (same.Count > 1)
                {
                    if (ReferenceEquals(same[0], entry))
                        yield return (new ReferenceTarget(entry.File, at), new(ReferenceOutcome.Ambiguous, Note: $"{same.Count} map entries describe this place's {(entry.Keys ? "key" : "value")}"));
                    continue;
                }
                yield return (new ReferenceTarget(entry.File, at), ResolveEntry(entry, project, entry.File, at, parts, value));
            }
        }
    }

    // The entries of this one's meaning (key or value) that describe this place, in the map's order.
    private readonly Dictionary<(Entry Entry, string Pointer), List<Entry>> sameMeaning = new();
    private List<Entry> SameMeaning(Entry entry, IReadOnlyList<string> parts, string pointer)
    {
        lock (sameMeaning)
        {
            if (sameMeaning.TryGetValue((entry, pointer), out var kept)) return kept;
            var parent = parts.Take(Math.Max(0, parts.Count - 1)).ToList();
            var found = references.Where(e => e.File == entry.File && e.Keys == entry.Keys
                && (e.Keys ? Matches(e.At, parent) : Matches(e.At, parts) || Matches(e.At, parent) && parts.Count > 0 && IsIndex(parts[^1]))).ToList();
            if (sameMeaning.Count >= MostKept) sameMeaning.Clear();
            sameMeaning[(entry, pointer)] = found;
            return found;
        }
    }

    // Every value at a place pattern, with its pointer.
    private static IEnumerable<(string Pointer, JsonElement Value)> Places(JsonElement node, string[] pattern, string at, int i = 0)
    {
        if (i == pattern.Length) { yield return (at, node); yield break; }
        var part = pattern[i];
        if (part == "*")
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                var n = 0;
                foreach (var item in node.EnumerateArray()) foreach (var r in Places(item, pattern, $"{at}/{n++}", i + 1)) yield return r;
            }
            else if (node.ValueKind == JsonValueKind.Object)
                foreach (var p in node.EnumerateObject()) foreach (var r in Places(p.Value, pattern, at + Pointer.Join([p.Name]), i + 1)) yield return r;
        }
        else if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(part, out var child))
            foreach (var r in Places(child, pattern, at + Pointer.Join([part]), i + 1)) yield return r;
        else if (node.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < node.GetArrayLength())
            foreach (var r in Places(ListIndex.Item(node, idx), pattern, $"{at}/{idx}", i + 1)) yield return r;
    }

    // The items of a list (or keyed object) whose id is id.
    private static IEnumerable<string> Items(JsonElement list, string at, string key, string id)
    {
        if (key == "$key")
        {
            if (list.ValueKind == JsonValueKind.Object && list.TryGetProperty(id, out _)) yield return at + Pointer.Join([id]);
            yield break;
        }
        if (IndexOf(list, at, key).TryGetValue(id, out var pointers))
            foreach (var p in pointers) yield return p;
    }

    // An id index per target list (found by A043: a scan per resolve was about 1 ms against 5,000 items). The last few
    // lists indexed on each thread are kept, recognised as the same place in the same live document, as ListIndex does;
    // an edited document is a new document, so its lists are indexed afresh.
    [ThreadStatic] private static List<(JsonElement List, string Key, Dictionary<string, List<string>> Ids)>? indexes;

    private static Dictionary<string, List<string>> IndexOf(JsonElement list, string at, string key)
    {
        indexes ??= [];
        foreach (var (l, k, ids) in indexes)
            if (k == key && ListIndex.SameElement(l, list)) return ids;
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var entries = list.ValueKind switch
        {
            JsonValueKind.Array => list.EnumerateArray().Select((v, n) => (Pointer: $"{at}/{n}", Value: v)),
            JsonValueKind.Object => list.EnumerateObject().Select(p => (Pointer: at + Pointer.Join([p.Name]), Value: p.Value)),
            _ => [],
        };
        foreach (var (pointer, value) in entries)
        {
            var candidate = key == "$value" ? value : value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var k) ? k : default;
            var id = candidate.ValueKind switch { JsonValueKind.String => candidate.GetString(), JsonValueKind.Number => candidate.GetRawText(), _ => null };
            if (id is null) continue;
            if (!index.TryGetValue(id, out var list2)) index[id] = list2 = [];
            list2.Add(pointer);
        }
        if (indexes.Count >= 16) indexes.RemoveAt(0);
        indexes.Add((list, key, index));
        return index;
    }

    private static bool Matches(string[] pattern, IReadOnlyList<string> parts) =>
        pattern.Length == parts.Count && pattern.Select((p, i) => p == "*" || p == parts[i]).All(x => x);

    private static bool IsIndex(string part) => part.Length > 0 && part.All(char.IsAsciiDigit);
}
