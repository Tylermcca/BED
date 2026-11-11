namespace Bed.Editor;

/// <summary>How a property is shown and entered.</summary>
public enum PropertyKind
{
    /// <summary>A line of text.</summary>
    Text,
    /// <summary>A number.</summary>
    Number,
    /// <summary>A whole number.</summary>
    Integer,
    /// <summary>On or off.</summary>
    Toggle,
    /// <summary>One of <see cref="PropertyDescriptor{TDoc,TObj}.Choices"/>.</summary>
    Choice,
}

/// <summary>
/// One editable property of an object type (worksheet AT-017). It reads a value, and turns a new value into one of the
/// document type's own typed commands, so every edit stays validated, undoable and journaled (no universal setter).
/// A property may also have a default (an archetype or template value); when it differs, the value is an override, and
/// resetting it is one more command.
/// </summary>
/// <param name="Id">A stable id within the object type (for example <c>label</c>).</param>
/// <param name="Label">What people see.</param>
/// <param name="Kind">How it's shown and entered.</param>
/// <param name="Get">Reads the value from an object.</param>
/// <param name="Edit">Builds the command that sets the value on the object; null makes the property read-only.</param>
/// <param name="Default">The object's default (inherited) value, if the type has defaults.</param>
/// <param name="Choices">For <see cref="PropertyKind.Choice"/>: the allowed values.</param>
/// <param name="Help">One plain sentence shown beside it.</param>
public sealed record PropertyDescriptor<TDoc, TObj>(string Id, string Label, PropertyKind Kind, Func<TObj, object?> Get,
    Func<TObj, object?, ICommand<TDoc>>? Edit = null, Func<TDoc, TObj, object?>? Default = null,
    IReadOnlyList<string>? Choices = null, string Help = "") where TDoc : class;

/// <summary>
/// A kind of object in a document (the extension seam OSS-007): how to find objects of this kind and which properties
/// they have. A game or plug-in registers its own types; the editor knows nothing else about them.
/// </summary>
/// <param name="Id">A stable, dotted id (for example <c>toy.item</c>).</param>
/// <param name="Label">What people see.</param>
/// <param name="Find">The object with an id, or null when it isn't of this type.</param>
/// <param name="Properties">Its properties, in display order.</param>
public sealed record ObjectType<TDoc, TObj>(string Id, string Label, Func<TDoc, string, TObj?> Find,
    IReadOnlyList<PropertyDescriptor<TDoc, TObj>> Properties) : IObjectType<TDoc> where TDoc : class where TObj : class
{
    // Only fixed built-in finders/descriptor sets opt into the existing row cache.
    // A host's with-expression replacing either part returns to live null-version behavior.
    private ObjectType<TDoc, TObj>? StaticRowsSource { get; init; }
    internal ObjectType<TDoc, TObj> WithStaticRows() => this with { StaticRowsSource = this };
    long? IObjectType<TDoc>.RowsVersion => StaticRowsSource is { } source
        && ReferenceEquals(source.Find, Find) && ReferenceEquals(source.Properties, Properties) ? 0 : null;

    /// <inheritdoc />
    public IReadOnlyList<InspectorRow> Rows(TDoc document, IReadOnlyList<string> ids)
    {
        var objects = ids.Select(id => Find(document, id)).ToList();
        if (objects.Count == 0 || objects.Any(o => o is null)) return [];
        return Properties.Select(p =>
        {
            var values = objects.Select(o => p.Get(o!)).ToList();
            var same = values.All(v => Equals(v, values[0]));
            var overridden = p.Default is { } d && objects.Any(o => !Equals(p.Get(o!), d(document, o!)));
            return new InspectorRow(p.Id, p.Label, p.Kind, same ? values[0] : null, !same, p.Edit is not null, overridden, p.Default is not null,
                p.Choices ?? [], p.Help);
        }).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ICommand<TDoc>>? EditCommands(TDoc document, IReadOnlyList<string> ids, string propertyId, object? value, bool reset)
    {
        var p = Properties.FirstOrDefault(x => x.Id == propertyId);
        if (p?.Edit is null) return null;
        var objects = ids.Select(id => Find(document, id)).ToList();
        if (objects.Any(o => o is null)) return null;
        if (reset && p.Default is null) return null;
        return objects.Select(o => p.Edit(o!, reset ? p.Default!(document, o!) : value)).ToList();
    }
}

/// <summary>The type-erased face of an <see cref="ObjectType{TDoc,TObj}"/>, so one registry can hold many object types.</summary>
public interface IObjectType<TDoc> where TDoc : class
{
    /// <summary>A number for everything a row depends on besides the document, the selection and the row asked for, so the
    /// inspector may keep rows until one of those or this changes (a schema is one). Null, the default, keeps
    /// none: a type reading anything it can't number stays live.</summary>
    long? RowsVersion => null;

    /// <summary>The type's id.</summary>
    string Id { get; }

    /// <summary>The type's label.</summary>
    string Label { get; }

    /// <summary>The inspector rows for these objects (all of this type), or empty when any isn't.</summary>
    IReadOnlyList<InspectorRow> Rows(TDoc document, IReadOnlyList<string> ids);

    /// <summary>The rows inside the row <paramref name="propertyId"/>, when it holds fields of its own, such as a price's
    /// minimum and maximum; none by default. The inspector shows them under it, and sets them through
    /// <see cref="EditCommands"/> by the ids given here.</summary>
    IReadOnlyList<InspectorRow> NestedRows(TDoc document, IReadOnlyList<string> ids, string propertyId) => [];

    /// <summary>What adds an item to the list row <paramref name="propertyId"/>; null when it can't. None by default.</summary>
    IReadOnlyList<ICommand<TDoc>>? AddCommands(TDoc document, IReadOnlyList<string> ids, string propertyId) => null;

    /// <summary>What removes the list item row <paramref name="propertyId"/>; null when it can't. None by default.</summary>
    IReadOnlyList<ICommand<TDoc>>? RemoveCommands(TDoc document, IReadOnlyList<string> ids, string propertyId) => null;

    /// <summary>Commands adding an entry called <paramref name="name"/> to the object of named entries at <paramref name="propertyId"/>, or null if it can't have one.</summary>
    IReadOnlyList<ICommand<TDoc>>? AddNamedCommands(TDoc document, IReadOnlyList<string> ids, string propertyId, string name) => null;

    /// <summary>Commands adding an item to the list that's itself selected, or null if what's selected isn't one.</summary>
    IReadOnlyList<ICommand<TDoc>>? AddHereCommands(TDoc document, IReadOnlyList<string> ids) => null;

    /// <summary>Commands removing the selected item from the list it's in, or null if it isn't in one.</summary>
    IReadOnlyList<ICommand<TDoc>>? RemoveHereCommands(TDoc document, IReadOnlyList<string> ids) => null;

    /// <summary>The commands setting (or resetting) a property on these objects; null when it can't be edited.</summary>
    IReadOnlyList<ICommand<TDoc>>? EditCommands(TDoc document, IReadOnlyList<string> ids, string propertyId, object? value, bool reset);
}

/// <summary>One line of the inspector.</summary>
/// <param name="Id">The property's id.</param>
/// <param name="Label">What people see.</param>
/// <param name="Kind">How it's shown.</param>
/// <param name="Value">The value, when every selected object has the same one.</param>
/// <param name="Mixed">True when the selected objects differ (show "—", not a value).</param>
/// <param name="Editable">True when it can be edited.</param>
/// <param name="Overridden">True when a value differs from its default (show it marked, with a reset).</param>
/// <param name="HasDefault">True when the property has a default to reset to.</param>
/// <param name="Choices">For choices: the allowed values.</param>
/// <param name="Help">One plain sentence shown beside it.</param>
/// <param name="Range">For numbers: the values allowed, when the type says (shown beside the field, so the limits are
/// known before typing, not learned from a refusal).</param>
/// <param name="CanAdd">True for a list an item can be added to.</param>
/// <param name="CanRemove">True for a list item, or an entry in an object of named entries, that can be removed.</param>
/// <param name="NamedEntries">True for an object whose entries are named by whoever adds them, rather than fixed by its schema.</param>
public sealed record InspectorRow(string Id, string Label, PropertyKind Kind, object? Value, bool Mixed, bool Editable, bool Overridden,
    bool HasDefault, IReadOnlyList<string> Choices, string Help, NumberRange? Range = null, bool CanAdd = false, bool CanRemove = false, bool NamedEntries = false);

/// <summary>The numbers a field allows: a lowest and a highest, each either included or not; null for no limit.</summary>
/// <param name="Min">The lower bound, or null for no lower bound.</param>
/// <param name="MinExcluded">Whether the lower bound itself is excluded. This has no effect when Min is null.</param>
/// <param name="Max">The upper bound, or null for no upper bound.</param>
/// <param name="MaxExcluded">Whether the upper bound itself is excluded. This has no effect when Max is null.</param>
public sealed record NumberRange(double? Min, bool MinExcluded, double? Max, bool MaxExcluded)
{
    /// <summary>Whether <paramref name="value"/> is allowed.</summary>
    public bool Allows(double value) =>
        (Min is not { } lo || (MinExcluded ? value > lo : value >= lo)) && (Max is not { } hi || (MaxExcluded ? value < hi : value <= hi));

    /// <summary>The range written the way a person reads it: "0 &lt; x &lt;= 5", "x &gt;= 0", "x &lt; 1" (ASCII: the default font has no ≤).</summary>
    public override string ToString()
    {
        static string N(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var low = Min is { } lo ? $"{N(lo)} {(MinExcluded ? "<" : "<=")} " : "";
        var high = Max is { } hi ? $" {(MaxExcluded ? "<" : "<=")} {N(hi)}" : "";
        if (Min is null && Max is { } h) return $"x {(MaxExcluded ? "<" : "<=")} {N(h)}";
        if (Max is null && Min is { } l) return $"x {(MinExcluded ? ">" : ">=")} {N(l)}";
        return low + "x" + high;
    }
}

/// <summary>
/// The inspector for an <see cref="EditorModel{TDoc}"/>: rows for the current selection, and edits that run through the
/// editor (so focus locking applies) as one undo step for the whole selection.
/// </summary>
public sealed class Inspector<TDoc> where TDoc : class
{
    private readonly List<IObjectType<TDoc>> types = [];

    /// <summary>An inspector over <paramref name="editor"/>.</summary>
    public Inspector(EditorModel<TDoc> editor) : this(editor, TimeProvider.System) { }

    /// <summary>An inspector using an explicit clock for the advisory issue-cache lifetime.</summary>
    public Inspector(EditorModel<TDoc> editor, TimeProvider timeProvider)
    {
        Editor = editor;
        issueClock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    private string[] selectionIds=[];
    private string selectionKey="";
    // Preserve the existing key exactly, but avoid rebuilding a large string for every Nested/Selected query.
    private string SelectionKey()
    {
        if(!Editor.Selection.SequenceEqual(selectionIds))
        {selectionIds=Editor.Selection.ToArray();selectionKey=string.Join("\n",selectionIds);}
        return selectionKey;
    }

    private readonly TimeProvider issueClock;

    /// <summary>The editor it inspects.</summary>
    public EditorModel<TDoc> Editor { get; }

    /// <summary>The registered object types, in registration order.</summary>
    public IReadOnlyList<IObjectType<TDoc>> Types => types;

    /// <summary>Registers an object type. Its id must be new.</summary>
    public Inspector<TDoc> Add(IObjectType<TDoc> type)
    {
        if (types.Any(t => t.Id == type.Id)) throw new ArgumentException($"object type '{type.Id}' is already registered", nameof(type));
        types.Add(type);
        return this;
    }

    /// <summary>The object type shared by the whole selection, or null (nothing selected, or mixed types).</summary>
    public IObjectType<TDoc>? SelectedType => Selected().Type;

    /// <summary>The rows for the current selection, with a property read-only when any selected object is outside the focus.</summary>
    public IReadOnlyList<InspectorRow> Rows()
    {
        var (type, rows) = Selected();
        if (type is null) return [];
        var locked = Editor.Selection.Any(id => !Editor.InFocus(id));
        if (!locked) return rows;
        if (lockedFor is { } known && ReferenceEquals(known.Rows, rows) && selectedFor is { } kept && ReferenceEquals(kept.Rows, rows)) return known.Shown;
        IReadOnlyList<InspectorRow> shown = rows.Select(r => r with { Editable = false, CanAdd = false, CanRemove = false, NamedEntries = false }).ToList();
        lockedFor = (rows, shown);
        return shown;
    }

    // The selection's type and its rows, found by asking each type in turn. Asked several times a frame (by Rows, by every
    // Nested, by each edit), and a type's rows can be real work (found by A179: about 60 MiB a frame on a large story item),
    // so kept while the document, the selection and every asked type's RowsVersion stay the same. A type with no version
    // keeps nothing, as for Nested.
    private (IObjectType<TDoc>? Type, IReadOnlyList<InspectorRow> Rows) Selected()
    {
        if (Editor.Selection.Count == 0) return (null, []);
        var document = Editor.Session.Current;
        var selection = SelectionKey();
        if (selectedFor is { } known && ReferenceEquals(known.Document, document) && known.Selection == selection && known.Count == types.Count && SameVersions(known.Versions))
            return (known.Type, known.Rows);
        selectedFor = null;
        var versions = new long[types.Count];
        var asked = 0;
        var numbered = true;
        foreach (var type in types)
        {
            if (type.RowsVersion is { } v) versions[asked] = v; else numbered = false;
            asked++;
            var rows = type.Rows(document, Editor.Selection);
            if (rows.Count == 0) continue;
            if (numbered) selectedFor = (document, selection, types.Count, versions[..asked], type, rows);
            return (type, rows);
        }
        if (numbered) selectedFor = (document, selection, types.Count, versions, null, []);
        return (null, []);
    }

    private bool SameVersions(long[] versions)
    {
        for (var i = 0; i < versions.Length; i++)
            if (types[i].RowsVersion != versions[i]) return false;
        return true;
    }

    private (TDoc Document, string Selection, int Count, long[] Versions, IObjectType<TDoc>? Type, IReadOnlyList<InspectorRow> Rows)? selectedFor;
    private (IReadOnlyList<InspectorRow> Rows, IReadOnlyList<InspectorRow> Shown)? lockedFor;

    /// <summary>The rows inside <paramref name="row"/>, shown under it: a nested object's own fields. None for a plain
    /// value.</summary>
    public IReadOnlyList<InspectorRow> Nested(InspectorRow row)
    {
        if (SelectedType is not { } type) return [];
        var locked = Editor.Selection.Any(id => !Editor.InFocus(id));
        // Asked every frame, even for a folded list, and a long list's rows are real work (found by A088). Kept only when the type
        // says its rows come from the document alone.
        var document = Editor.Session.Current;
        var version = type.RowsVersion;
        var remember = version is not null;
        if (version is { } v)
        {
            var selection = SelectionKey();
            if (nestedFor is not { } known || !ReferenceEquals(known.Document, document) || known.Selection != selection || known.Locked != locked || !ReferenceEquals(known.Type, type) || known.Version != v)
            {
                nestedRows.Clear();
                nestedFor = (document, selection, locked, type, v);
            }
            if (nestedRows.TryGetValue(row.Id, out var kept)) return kept;
        }
        var rows = type.NestedRows(document, Editor.Selection, row.Id);
        var shown = locked ? rows.Select(r => r with { Editable = false, CanAdd = false, CanRemove = false, NamedEntries = false }).ToList() : rows;
        if (remember) nestedRows[row.Id] = shown;
        return shown;
    }

    /// <summary>The issues from the document's validator that concern the selected objects (shown beside the rows).</summary>
    public IReadOnlyList<Issue> Issues()
    {
        // Drawn every frame, and it validates the whole document (found by A088). The same document gets the same answer for a
        // second: only a second, since a game's rule may read other files, and nothing says when those change.
        var current = Editor.Session.Current;
        var now = issueClock.GetTimestamp();
        if (issuesFor is not { } known || !ReferenceEquals(known.Document, current) || issueClock.GetElapsedTime(known.At, now) >= TimeSpan.FromMilliseconds(IssuesLastMs))
            issuesFor = known = (current, Editor.Session.Validator.Validate(current), now);
        // The selection's share, kept while the same answer and the same selection stand (found by A179: a fresh list each frame).
        var selection = SelectionKey();
        if (selectedIssues is { } kept && ReferenceEquals(kept.All, known.Issues) && kept.Selection == selection) return kept.Mine;
        IReadOnlyList<Issue> mine = known.Issues.Where(i => i.ObjectId is { } id && Editor.Selection.Contains(id)).ToList();
        selectedIssues = (known.Issues, selection, mine);
        return mine;
    }

    private (IReadOnlyList<Issue> All, string Selection, IReadOnlyList<Issue> Mine)? selectedIssues;

    private const int IssuesLastMs = 1000;
    private (TDoc Document, IReadOnlyList<Issue> Issues, long At)? issuesFor;
    private (TDoc Document, string Selection, bool Locked, IObjectType<TDoc> Type, long Version)? nestedFor;
    private readonly Dictionary<string, IReadOnlyList<InspectorRow>> nestedRows = new(StringComparer.Ordinal);

    /// <summary>Sets a property on every selected object as one undo step. False (and <see cref="LastIssues"/> says why)
    /// when it can't be edited, is outside the focus, or the commands or validator refuse.</summary>
    public bool Set(string propertyId, object? value) => Apply(propertyId, value, reset: false);

    /// <summary>Resets a property to its default on every selected object, as one undo step.</summary>
    public bool Reset(string propertyId) => Apply(propertyId, null, reset: true);

    /// <summary>Why the last set or reset didn't happen; empty after one that did.</summary>
    public IReadOnlyList<Issue> LastIssues { get; private set; } = [];

    /// <summary>Adds an item to the list <paramref name="row"/> as one undo step. False (and <see cref="LastIssues"/> says
    /// why) when it can't be added, or the validator refuses.</summary>
    public bool AddItem(InspectorRow row)
    {
        if (Blocked()) return false;
        var commands = SelectedType?.AddCommands(Editor.Session.Current, Editor.Selection, row.Id);
        if (commands is null)
        {
            LastIssues = [Issue.Error($"'{row.Label}' can't have an item added here")];
            return false;
        }
        return Run(commands);
    }

    /// <summary>Whether the selection is a list that can have an item added.</summary>
    public bool CanAddHere => !Locked && types.Any(t => t.AddHereCommands(Editor.Session.Current, Editor.Selection) is not null);

    /// <summary>Whether the selection is an item in a list, which can be removed from it.</summary>
    public bool CanRemoveHere => !Locked && types.Any(t => t.RemoveHereCommands(Editor.Session.Current, Editor.Selection) is not null);

    private bool Locked => Editor.Selection.Any(id => !Editor.InFocus(id));

    /// <summary>Adds an item to the selected list as one undo step. A record becomes selected; a plain value isn't something that can be, so the list stays selected and shows it. False (and <see cref="LastIssues"/> says why) when it can't be added, or the validator refuses.</summary>
    public bool AddHere()
    {
        if (Blocked()) return false;
        var commands = types.Select(t => t.AddHereCommands(Editor.Session.Current, Editor.Selection)).FirstOrDefault(c => c is not null);
        if (commands is null)
        {
            LastIssues = [Issue.Error("What's selected isn't a list an item can be added to")];
            return false;
        }
        var before = Editor.Outline.ObjectIds(Editor.Session.Current).ToHashSet(StringComparer.Ordinal);
        if (!Run(commands)) return false;
        if (Editor.Outline.ObjectIds(Editor.Session.Current).FirstOrDefault(id => !before.Contains(id)) is { } added) Editor.Select(added);
        return true;
    }

    /// <summary>Removes the selected item from its list as one undo step, and selects the list. False (and <see cref="LastIssues"/> says why) when it can't be removed, or the validator refuses.</summary>
    public bool RemoveHere()
    {
        if (Blocked()) return false;
        var commands = types.Select(t => t.RemoveHereCommands(Editor.Session.Current, Editor.Selection)).FirstOrDefault(c => c is not null);
        if (commands is null)
        {
            LastIssues = [Issue.Error("What's selected isn't an item in a list")];
            return false;
        }
        var parent = Editor.Outline.Parent(Editor.Session.Current, Editor.Selection[0]);
        if (!Run(commands)) return false;
        if (parent is not null) Editor.Select(parent);
        return true;
    }

    /// <summary>Adds an entry called <paramref name="name"/> to the object <paramref name="row"/> as one undo step. False (and
    /// <see cref="LastIssues"/> says why) when it can't be added, the name's taken, or the validator refuses.</summary>
    public bool AddNamed(InspectorRow row, string name)
    {
        if (Blocked()) return false;
        var commands = SelectedType?.AddNamedCommands(Editor.Session.Current, Editor.Selection, row.Id, name);
        if (commands is null)
        {
            LastIssues = [Issue.Error($"'{row.Label}' can't have an entry called '{name}' added: it may already have one")];
            return false;
        }
        return Run(commands);
    }

    /// <summary>Removes the list item <paramref name="row"/> as one undo step. False (and <see cref="LastIssues"/> says why)
    /// when it can't be removed, or the validator refuses.</summary>
    public bool RemoveItem(InspectorRow row)
    {
        if (Blocked()) return false;
        var commands = SelectedType?.RemoveCommands(Editor.Session.Current, Editor.Selection, row.Id);
        if (commands is null)
        {
            LastIssues = [Issue.Error($"'{row.Label}' can't be removed here")];
            return false;
        }
        return Run(commands);
    }

    // Whether editing is off for now: a game is playing, or the selection is outside the focus. LastIssues says which.
    private bool Blocked()
    {
        if (Editor.Playing is { } playing)
        {
            LastIssues = [Issue.Error($"'{playing.Title}' is playing; stop it to edit")];
            return true;
        }
        var locked = Editor.Selection.Where(id => !Editor.InFocus(id)).ToList();
        if (locked.Count == 0) return false;
        LastIssues = [.. locked.Select(id => Issue.Error($"'{id}' is outside the place you're focused on; step out of focus to edit it", id))];
        return true;
    }

    private bool Run(IReadOnlyList<ICommand<TDoc>> commands)
    {
        var touches=Editor.Selection.ToArray();
        var ok = commands.Count == 1 ? Editor.Execute(commands[0],touches) : Editor.ExecuteBatch(commands,touches);
        LastIssues = ok ? [] : Editor.Refused.Count>0 ? Editor.Refused : Editor.Session.LastIssues;
        return ok;
    }

    private bool Apply(string propertyId, object? value, bool reset)
    {
        if (Blocked()) return false;
        var commands = SelectedType?.EditCommands(Editor.Session.Current, Editor.Selection, propertyId, value, reset);
        if (commands is null)
        {
            // Said at the field, for each item it was asked of, so a refusal points where it belongs (found by A209: a non-finite
            // number was refused with no place).
            var message = reset ? $"'{propertyId}' has no default to reset to"
                : value is double d && !double.IsFinite(d) ? $"'{propertyId}' takes a finite number; {d} isn't one"
                : $"'{propertyId}' can't be edited here";
            LastIssues = Editor.Selection.Count == 0 ? [Issue.Error(message)]
                : [.. Editor.Selection.Select(item => Issue.Error(message, item, $"$.{propertyId}"))];
            return false;
        }
        return Run(commands);
    }
}
