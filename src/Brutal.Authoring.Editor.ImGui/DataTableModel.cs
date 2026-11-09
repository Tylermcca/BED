using System.Text.Json;
using Brutal.Authoring.Data;

namespace Brutal.Authoring.Editor.ImGui;

using BatchPlan = DataTableView.BatchPlan;

// INVENTED (code_health.md rank 10): one model per view owns table queries and their caches,
// plus non-mutating fill/paste planning. No ImGui or clipboard operations live here. The view
// retains input/navigation and the existing SetCell/Apply mutation and validation paths.
// Call on the owning view's thread, as before; this extraction adds no concurrency or cache policy.
internal sealed class DataTableModel
{
    private readonly EditorModel<DataDocument> Editor;
    private readonly IObjectType<DataDocument> Type;

    internal DataTableModel(EditorModel<DataDocument> editor, IObjectType<DataDocument> type)
    {
        Editor = editor;
        Type = type;
    }

    internal string? List { get; set; }
    internal string Filter { get; set; } = "";
    internal (string Column, bool Descending)? Sort { get; set; }

    /// <summary>The top-level lists that hold objects, the choices for <see cref="List"/>.</summary>
    public IReadOnlyList<string> Lists() => Editor.Session.Current.Root.ValueKind != JsonValueKind.Object ? []
        : Editor.Session.Current.Root.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Array && p.Value.EnumerateArray().Any(i => i.ValueKind == JsonValueKind.Object))
            .Select(p => Pointer.Join([p.Name])).ToList();

    /// <summary>The list actually shown: <see cref="List"/>, or the first list of objects.</summary>
    public string? Shown => List ?? Lists().FirstOrDefault();

    /// <summary>Every item in the shown list, as pointers, in the file's order.</summary>
    public IReadOnlyList<string> Items()
    {
        if (Shown is not { } list || Editor.Session.Current.At(list) is not { ValueKind: JsonValueKind.Array } arr) return [];
        // Counted for the status line every frame (found by A179): kept for one version of the document.
        if (itemsFor is { } k && ReferenceEquals(k.Doc, Editor.Session.Current) && k.List == list) return itemsCache;
        itemsCache = arr.EnumerateArray().Select((item, i) => (item, i)).Where(t => t.item.ValueKind == JsonValueKind.Object)
            .Select(t => $"{list}/{t.i}").ToList();
        itemsFor = (Editor.Session.Current, list);
        return itemsCache;
    }

    private (DataDocument Doc, string List)? itemsFor;
    private IReadOnlyList<string> itemsCache = [];

    /// <summary>The columns: every field any item has a row for, with its label and kind. Fields that can be edited in
    /// a cell come first, in the schema's order; lists and nested objects (summaries) after them, so the numbers people
    /// tune aren't pushed off to the right.</summary>
    public IReadOnlyList<InspectorRow> Columns()
    {
        if (columnsFor is { } k && ReferenceEquals(k.Doc, Editor.Session.Current) && k.List == Shown) return columnsCache;
        var seen = new Dictionary<string, InspectorRow>(StringComparer.Ordinal);
        var doc = Editor.Session.Current;
        // Every field any item has (found by A206: only the first 500 were read, so a field first seen below them had no column). Each
        // item's rows are worked out only when it has a field not yet seen, so a long table of alike items stays cheap.
        var n = 0;
        foreach (var item in Items())
        {
            if (n++ >= 500 && !(doc.At(item) is { ValueKind: JsonValueKind.Object } value && value.EnumerateObject().Any(p => !seen.ContainsKey(p.Name)))) continue;
            foreach (var row in Type.Rows(doc, [item]))
                seen.TryAdd(row.Id, row);
        }
        columnsCache = seen.Values.OrderBy(r => r.Editable ? 0 : 1).ToList();   // stable: schema order within each group
        columnsFor = (Editor.Session.Current, Shown);
        return columnsCache;
    }

    // What's been worked out for one version of the document, kept until it changes (found by A033: recomputing every
    // item's rows each frame made a 10,000-row table unusable).
    private (DataDocument Doc, string? List)? columnsFor;
    private IReadOnlyList<InspectorRow> columnsCache = [];
    private (DataDocument Doc, string? List, string Filter, (string, bool)? Sort)? visibleFor;
    private IReadOnlyList<string> visibleCache = [];
    private DataDocument? rowsFor;
    private readonly Dictionary<string, Dictionary<string, InspectorRow>> rowsCache = new(StringComparer.Ordinal);

    // An item's rows by field, for the current document; worked out once per item per document version.
    internal Dictionary<string, InspectorRow> RowsOf(string item)
    {
        if (!ReferenceEquals(rowsFor, Editor.Session.Current)) { rowsCache.Clear(); rowsFor = Editor.Session.Current; }
        if (!rowsCache.TryGetValue(item, out var rows))
            rowsCache[item] = rows = Type.Rows(Editor.Session.Current, [item]).ToDictionary(r => r.Id, StringComparer.Ordinal);
        return rows;
    }

    /// <summary>One cell: the row the inspector would show for that item's field, or null when it has none.</summary>
    public InspectorRow? Cell(string item, string field) => RowsOf(item).GetValueOrDefault(field);

    /// <summary>The items shown, after the filter and sort: pointers into the file, which keep their own order.</summary>
    public IReadOnlyList<string> Visible()
    {
        var doc = Editor.Session.Current;
        if (visibleFor is { } k && ReferenceEquals(k.Doc, doc) && k.List == Shown && k.Filter == Filter && Equals(k.Sort, Sort)) return visibleCache;
        var items = Items().ToList();
        if (Filter.Trim() is { Length: > 0 } f)
            items = items.Where(i => (Editor.Outline.Label(doc, i) ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)
                || RowsOf(i).Values.Any(r => r.Value?.ToString()?.Contains(f, StringComparison.OrdinalIgnoreCase) == true)).ToList();
        if (Sort is { } s)
        {
            // Missing values sort last either way; numbers by value, everything else as text; ties keep file order.
            var keyed = items.Select((i, n) => (Item: i, N: n, V: Cell(i, s.Column)?.Value)).ToList();
            var present = keyed.Where(k => k.V is not null).ToList();
            // Numbers by their exact value, read from the file's own text: as doubles, 1e400 and 2e400 tie, and 9007199254740993 sits
            // with 9007199254740992 (found by A206).
            // Each number's text is read once, and plain whole numbers and doubles compare as themselves; only text a double can't hold
            // exactly takes the exact route (A206 measured 1.8 s and 1 GB for a first sort of 50,000 rows when it was read per comparison).
            var exact = Comparer<(long? Whole, double Value, string Raw)>.Create((x, y) =>
                x.Whole is { } a && y.Whole is { } b ? a.CompareTo(b)
                : x.Whole is null && y.Whole is null && Exact(x.Raw) && Exact(y.Raw) ? x.Value.CompareTo(y.Value)
                : JsonNumbers.Compare(x.Raw, y.Raw));
            var column = Pointer.Join([s.Column]);
            (long?, double, string) Key(string item) => doc.At(item + column) is { ValueKind: JsonValueKind.Number } r
                ? (r.TryGetInt64(out var l) ? l : null, r.TryGetDouble(out var d) ? d : 0, r.GetRawText()) : (0, 0, "0");
            var keys = present.ToDictionary(k => k.Item, k => Key(k.Item), StringComparer.Ordinal);
            var ordered = present.All(k => k.V is double or long or int)
                ? (s.Descending ? present.OrderByDescending(k => keys[k.Item], exact) : present.OrderBy(k => keys[k.Item], exact)).ThenBy(k => k.N)
                : (s.Descending ? present.OrderByDescending(k => k.V!.ToString(), StringComparer.OrdinalIgnoreCase) : present.OrderBy(k => k.V!.ToString(), StringComparer.OrdinalIgnoreCase)).ThenBy(k => k.N);
            items = ordered.Concat(keyed.Where(k => k.V is null)).Select(k => k.Item).ToList();
        }
        visibleCache = items;
        visibleFor = (doc, Shown, Filter, Sort);
        return items;
    }

    /// <summary>Plans setting <paramref name="field"/> to <paramref name="text"/> on every shown item (after the filter):
    /// fill a column with one value. The text is read as the field's kind, as a pasted cell is.</summary>
    public BatchPlan PlanFill(string field, string text) =>
        Plan(Visible().Select(item => (item, field, text)).ToList());

    /// <summary>
    /// Plans pasting a grid of tab-separated values (rows on lines, as a spreadsheet copies them), its top-left cell at
    /// <paramref name="item"/>'s row and <paramref name="field"/>'s column, in the order shown. INVENTED policy, from
    /// docs/research/table_editing.md: a plain rectangular grid only; numbers with a dot for decimals; <c>true</c> and
    /// <c>false</c> for on/off; choices exactly as listed. A grid that runs past the shown rows or columns, has rows of
    /// different widths, or has any cell that doesn't fit its field is refused whole, never applied in part.
    /// </summary>
    public BatchPlan PlanPaste(string item, string field, string tsv)
    {
        var rows = Grid(tsv.Replace("\r\n", "\n").TrimEnd('\n'));
        if (rows.Count == 0 || rows[0].Length == 0 || (rows.Count == 1 && rows[0] is [""])) return new([], 0, ["there's nothing to paste"]);
        if (rows.Any(r => r.Length != rows[0].Length)) return new([], 0, ["the pasted rows have different numbers of cells; paste a rectangle"]);
        var visible = Visible().ToList();
        var columns = Columns().Select(c => c.Id).ToList();
        var r0 = visible.IndexOf(item);
        var c0 = columns.IndexOf(field);
        if (r0 < 0 || c0 < 0) return new([], 0, ["pick a shown cell to paste at"]);
        if (r0 + rows.Count > visible.Count) return new([], 0, [$"the grid has {rows.Count} rows but only {visible.Count - r0} are shown from here; nothing was added"]);
        if (c0 + rows[0].Length > columns.Count) return new([], 0, [$"the grid has {rows[0].Length} columns but only {columns.Count - c0} are shown from here"]);
        var cells = new List<(string, string, string)>();
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[0].Length; c++)
                cells.Add((visible[r0 + r], columns[c0 + c], rows[r][c]));
        return Plan(cells);
    }

    // Type-checks every cell against its field before building any command (A029's paste policy).
    private BatchPlan Plan(IReadOnlyList<(string Item, string Field, string Text)> cells)
    {
        var commands = new List<ICommand<DataDocument>>();
        var problems = new List<string>();
        var refusals = new List<string>();
        var unchanged = 0;
        var doc = Editor.Session.Current;
        foreach (var (item, field, text) in cells)
        {
            var row = Cell(item, field);
            var name = $"{Editor.Outline.Label(doc, item) ?? item}, {field}";
            if (row is null || !row.Editable)
            {
                problems.Add($"{name}: can't be set in the table");
                refusals.Add("can't be set in the table");
                continue;
            }
            object? value = row.Kind switch
            {
                PropertyKind.Number => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null,
                PropertyKind.Integer => long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : null,
                PropertyKind.Toggle => text.Trim() switch { "true" => true, "false" => false, _ => null },
                PropertyKind.Choice => row.Choices.Contains(text) ? text : null,
                _ => text,
            };
            if (value is null)
            {
                problems.Add($"{name}: '{text}' isn't {Expect(row)}");
                refusals.Add("enter " + Expect(row));
                continue;
            }
            if (row.Value is null && text.Length == 0) { unchanged++; continue; }   // an empty cell pasted on a missing field leaves it missing (found by A206)
            if (Same(row.Value, value)) { unchanged++; continue; }   // unchanged: no command
            if (Type.EditCommands(doc, [item], field, value, reset: false) is [var command]) commands.Add(command);
            else
            {
                problems.Add($"{name}: can't be set in the table");
                refusals.Add("can't be set in the table");
            }
        }
        // Count candidates before discarding commands on a refusal: none of these counts is a partial apply.
        var reasons = refusals.GroupBy(reason => reason, StringComparer.Ordinal)
            .Select(group => new DataTableView.BatchRefusal(group.Key, group.Count())).ToArray();
        return new BatchPlan(problems.Count > 0 ? [] : commands, cells.Count, problems)
        {
            Counts = new(commands.Count, unchanged, refusals.Count, reasons),
        };
    }

    // Whether a number's text is held exactly by a double (short decimal, nothing past its range or its 17 digits), so doubles can
    // compare it: anything longer or larger goes the exact way.
    private static bool Exact(string raw) =>
        raw.Length <= 15 && raw.IndexOfAny(['e', 'E']) < 0 && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d);

    // Whether a cell's value and a pasted one are the same value: whole numbers exactly, never through a double (found by A206:
    // 9007199254740993 pasted over 9007199254740992 was "nothing to change").
    private static bool Same(object? a, object? b) => (a, b) switch
    {
        (long or int, long or int) => Convert.ToInt64(a) == Convert.ToInt64(b),
        (double x, long or int) => double.IsFinite(x) && x == Math.Round(x) && Math.Abs(x) < 9007199254740992 && (long)x == Convert.ToInt64(b),
        (long or int, double y) => double.IsFinite(y) && y == Math.Round(y) && Math.Abs(y) < 9007199254740992 && (long)y == Convert.ToInt64(a),
        (double x, double y) => double.IsFinite(x) && x == y,
        _ => Equals(a, b),
    };

    private static string Expect(InspectorRow row) => row.Kind switch
    {
        PropertyKind.Number => "a number (use a dot for decimals)",
        PropertyKind.Integer => "a whole number",
        PropertyKind.Toggle => "true or false",
        PropertyKind.Choice => "one of " + string.Join(", ", row.Choices),
        _ => "text",
    };

    // Tab-separated rows, as spreadsheets write them: a cell in quotes may hold tabs, new lines and doubled quotes.
    private static List<string[]> Grid(string tsv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new System.Text.StringBuilder();
        var text = tsv.Replace("\r\n", "\n");
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (cell.Length == 0 && c == '"' && (i == 0 || text[i - 1] is '\t' or '\n'))
            {
                var j = i + 1;
                var quoted = new System.Text.StringBuilder();
                for (; j < text.Length; j++)
                {
                    if (text[j] == '"' && j + 1 < text.Length && text[j + 1] == '"') { quoted.Append('"'); j++; }
                    else if (text[j] == '"') break;
                    else quoted.Append(text[j]);
                }
                // A closed quote followed by the end of the cell is one quoted cell; anything else is plain text, kept as written.
                if (j < text.Length && (j + 1 == text.Length || text[j + 1] is '\t' or '\n')) { cell.Append(quoted); i = j; continue; }
            }
            if (c == '\t') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add([.. row]); row.Clear(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add([.. row]); }
        return rows;
    }

}
