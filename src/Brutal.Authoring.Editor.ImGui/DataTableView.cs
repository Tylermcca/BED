using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// A list in a data document as a table (goal 15; docs/research/table_editing.md, first slice P0.1â€“P0.3): one row per
/// item, one column per field, with the item's name pinned on the left and the header pinned on top (BORROWED:
/// spreadsheets' frozen panes; MIXED: Unreal's typed table rows and Odin's members-as-columns). Columns, their kinds,
/// choices and help come from the same schema-driven object type the inspector uses. A cell edit is one of the
/// document's own commands, through the editor (so focus and play locks apply), validated and undoable. Sorting and the
/// filter only change what's shown, never the file's order (INVENTED guard: a header click never moves an item). Lists
/// and nested objects show as summaries; edit them in the inspector.
/// </summary>
public sealed partial class DataTableView
{
    private readonly DataTableModel model;
    private readonly Dictionary<string, ImInputString> texts = [];
    private readonly ImInputString filterText = new(256, ""u8);
    private bool sortSeen;
    private readonly ImInputString fillText = new(256, ""u8);
    private string fillField = "";
    private BatchPlan? pending;   // a fill or paste waiting for the person to confirm it

    /// <summary>How references are followed, or null when this table follows none. See
    /// <see cref="DataEditing.SchemaReferences"/>.</summary>
    public DataEditing.References? References { get; set; }

    private readonly Stack<(string? List, string? Item)> back = new();

    /// <summary>The item a cell's value names, or null.</summary>
    public string? Target(string item, string field) =>
        References is { } r && Editor.Session.Current.At(item + Pointer.Join([field])) is { } v ? r(Editor.Session.Current, item + Pointer.Join([field]), v) : null;

    /// <summary>Links that may lead to other files, such as a game's reference map: given a field's pointer and value,
    /// the file and pointer it names, or null. Used where <see cref="References"/> finds nothing.</summary>
    public Func<string, JsonElement, ReferenceTarget?>? Links { get; set; }

    /// <summary>Why a value that is meant to be a link can't be followed, and what to do about it (such as two items
    /// sharing its id); null when it isn't meant to be one. Said when <see cref="Follow"/> finds nothing, so the
    /// resolver's reason isn't lost.</summary>
    public Func<string, JsonElement, string?>? LinkProblem { get; set; }

    /// <summary>The name of the file this table shows, so a link back into it stays in this table.</summary>
    public string? File { get; set; }

    /// <summary>Shows an item in another file (a project's workspace supplies it); false when it can't.</summary>
    public Func<ReferenceTarget, bool>? GoTo { get; set; }

    /// <summary>Returns across files once this table's own Back history is spent; false when there's nowhere to go.</summary>
    public Func<bool>? BackElsewhere { get; set; }

    /// <summary>Whether <see cref="BackElsewhere"/> has somewhere to go.</summary>
    public Func<bool>? CanBackElsewhere { get; set; }

    /// <summary>Where a cell's value leads: an item in this document, or one in another file; null when it names
    /// nothing.</summary>
    public ReferenceTarget? LinkOf(string item, string field)
    {
        if (Target(item, field) is { } here) return new(File ?? "", here);
        var at = item + Pointer.Join([field]);
        return Links is { } l && Editor.Session.Current.At(at) is { } v ? l(at, v) : null;
    }

    /// <summary>Follows a cell's reference: shows the target's list, selects the target, and remembers where it came
    /// from for <see cref="Back"/>. A target in another file is shown through <see cref="GoTo"/>. Returns false, saying
    /// why, when the value names nothing.</summary>
    public bool Follow(string item, string field)
    {
        if (Target(item, field) is not { } target)
        {
            if (LinkOf(item, field) is { } link)
            {
                if (link.File == File) target = link.Pointer;
                else if (GoTo?.Invoke(link) == true) { Said = $"Went to {link.File}; Back returns."; return true; }
                else { Said = $"{field} names an item in {link.File}, which can't be shown."; return false; }
            }
            else
            {
                var at = item + Pointer.Join([field]);
                Said = LinkProblem is { } why && Editor.Session.Current.At(at) is { } v && why(at, v) is { } problem
                    ? $"{field} can't be followed: {problem}"
                    : $"{field} doesn't name an item here.";
                return false;
            }
        }
        if (Editor.HolderOf(target) is { } holder) target = holder;   // a plain field is held by the file's own entry (A141)
        back.Push((List, item));
        List = Pointer.Join(Pointer.Split(target).Take(1));
        Filter = "";
        Sort = null;
        Editor.Select(target);
        Said = $"Went to {Editor.Outline.Label(Editor.Session.Current, target)}; Back returns.";
        return true;
    }

    /// <summary>Returns to where the last <see cref="Follow"/> started. Returns false when there's nowhere to go back to.</summary>
    public bool Back()
    {
        if (back.Count == 0) return BackElsewhere?.Invoke() ?? false;
        var (list, item) = back.Pop();
        List = list;
        if (item is not null) Editor.Select(item);
        Said = "Back.";
        return true;
    }

    /// <summary>The cell a paste lands at: the last one clicked or focused, as (item pointer, field).</summary>
    public (string Item, string Field)? Anchor { get; set; }

    /// <summary>A table over <paramref name="editor"/>'s document, with columns from <paramref name="type"/>
    /// (<see cref="DataEditing.ObjectType"/>).</summary>
    public DataTableView(EditorModel<DataDocument> editor, IObjectType<DataDocument> type)
    {
        Editor = editor;
        Type = type;
        model = new(editor, type);
    }

    /// <summary>The editor it shows and edits.</summary>
    public EditorModel<DataDocument> Editor { get; }

    /// <summary>The object type giving the columns.</summary>
    public IObjectType<DataDocument> Type { get; }

    /// <summary>The list shown, as a JSON Pointer (<c>/wares</c>); null shows the first top-level list of objects.</summary>
    public string? List { get => model.List; set => model.List = value; }

    /// <summary>Only items whose name or any shown value contains this text (any case) are shown. View state.</summary>
    public string Filter { get => model.Filter; set => model.Filter = value; }

    /// <summary>The column the rows are sorted by and its direction, or null for the file's own order. View state.</summary>
    public (string Column, bool Descending)? Sort { get => model.Sort; set => model.Sort = value; }

    /// <summary>What the table last said (an edit done, or why it was refused).</summary>
    public string Said { get; private set; } = "";

    /// <summary>The top-level lists that hold objects, the choices for <see cref="List"/>.</summary>
    public IReadOnlyList<string> Lists() => model.Lists();

    /// <summary>The list actually shown: <see cref="List"/>, or the first list of objects.</summary>
    public string? Shown => model.Shown;

    /// <summary>Every item in the shown list, as pointers, in the file's order.</summary>
    public IReadOnlyList<string> Items() => model.Items();

    /// <summary>The columns: every field any item has a row for, with its label and kind. Fields that can be edited in
    /// a cell come first, in the schema's order; lists and nested objects (summaries) after them, so the numbers people
    /// tune aren't pushed off to the right.</summary>
    public IReadOnlyList<InspectorRow> Columns() => model.Columns();

    // Drawing uses the same cached rows as queries and batch planning.
    private Dictionary<string, InspectorRow> RowsOf(string item) => model.RowsOf(item);

    /// <summary>One cell: the row the inspector would show for that item's field, or null when it has none.</summary>
    public InspectorRow? Cell(string item, string field) => model.Cell(item, field);

    /// <summary>The items shown, after the filter and sort: pointers into the file, which keep their own order.</summary>
    public IReadOnlyList<string> Visible() => model.Visible();

    /// <summary>Sets one cell, as the document's own command through the editor. Returns false, with the reason in
    /// <see cref="Said"/>, when it's refused (the schema, the focus, a game playing) or the field can't be edited here.</summary>
    public bool SetCell(string item, string field, object? value)
    {
        if (Cell(item, field) is not { Editable: true })   // only a shown, editable cell; never adds an unknown field (A032)
        {
            Said = $"'{field}' isn't an editable column here; use the inspector.";
            return false;
        }
        var commands = Type.EditCommands(Editor.Session.Current, [item], field, value, reset: false);
        if (commands is not [var command])
        {
            // Say why when it's the value's kind: a fraction for a whole number, text for a number (found by A134).
            var kind = Cell(item, field)?.Kind;
            Said = kind == PropertyKind.Integer && value is not null ? $"'{field}' takes a whole number, not {value}."
                : kind == PropertyKind.Number && value is not null ? $"'{field}' takes a number, not {value}."
                : $"'{field}' can't be edited in the table; use the inspector.";
            return false;
        }
        if (Editor.Execute(command, item))
        {
            Said = $"{Editor.Outline.Label(Editor.Session.Current, item)}: {field} set.";
            return true;
        }
        Said = Explain(Editor.Refused.Count > 0 ? Editor.Refused : Editor.Session.LastIssues);
        return false;
    }

    // ---- fill and paste (A029 P0.4) ----

    /// <summary>What a fill or paste would do, worked out before anything changes: the cells it would set, how many
    /// it covers, and why it can't be done, when it can't. Nothing applies until <see cref="Apply"/>.</summary>
    /// <param name="Commands">One command per changed cell; unchanged cells are left out.</param>
    /// <param name="Cells">How many cells it covers.</param>
    /// <param name="Problems">Why it can't be done; empty when it can.</param>
    public sealed record BatchPlan(IReadOnlyList<ICommand<DataDocument>> Commands, int Cells, IReadOnlyList<string> Problems)
    {
        /// <summary>True when the scalar plan can be applied; whole-document checks still run at Apply.</summary>
        public bool Ok => Problems.Count == 0;

        /// <summary>Candidate cell counts, even when refusals discard all executable commands. Null for an operation-level
        /// shape/anchor/extent error or a plan made through the original constructor without classification details.</summary>
        public BatchCellCounts? Counts { get; init; }

        /// <summary>The table operation for its immediate result sentence: fill, paste, or edit for an original caller's plan.
        /// This is not a history or undo label.</summary>
        public string Operation { get; init; } = "edit";
    }

    /// <summary>Per-cell planning outcomes, not committed changes or a whole-document validation result. The three counts
    /// cover all cells; any refusal still disables the whole plan.</summary>
    /// <param name="Changed">Cells with a candidate edit command before whole-batch refusal is considered.</param>
    /// <param name="Unchanged">Same-value cells, including empty text over a missing field.</param>
    /// <param name="Refused">Cells refused by editability, conversion or command planning, not schema validation.</param>
    /// <param name="Reasons">Stable grouped reasons in their first-occurrence order.</param>
    public sealed record BatchCellCounts(int Changed, int Unchanged, int Refused, IReadOnlyList<BatchRefusal> Reasons);

    /// <summary>How many refused cells have one stable reason, independent of their row names or entered text.</summary>
    /// <param name="Reason">The shared editability or expected-kind reason.</param>
    /// <param name="Cells">How many candidate cells have that reason.</param>
    public sealed record BatchRefusal(string Reason, int Cells);

    /// <summary>Plans setting <paramref name="field"/> to <paramref name="text"/> on every shown item (after the filter):
    /// fill a column with one value. The text is read as the field's kind, as a pasted cell is.</summary>
    public BatchPlan PlanFill(string field, string text) => model.PlanFill(field, text) with { Operation = "fill" };

    /// <summary>
    /// Plans pasting a grid of tab-separated values (rows on lines, as a spreadsheet copies them), its top-left cell at
    /// <paramref name="item"/>'s row and <paramref name="field"/>'s column, in the order shown. INVENTED policy, from
    /// docs/research/table_editing.md: a plain rectangular grid only; numbers with a dot for decimals; <c>true</c> and
    /// <c>false</c> for on/off; choices exactly as listed. A grid that runs past the shown rows or columns, has rows of
    /// different widths, or has any cell that doesn't fit its field is refused whole, never applied in part.
    /// </summary>
    public BatchPlan PlanPaste(string item, string field, string tsv) => model.PlanPaste(item, field, tsv) with { Operation = "paste" };

    /// <summary>Applies a plan as one step: every cell or none, one undo for the lot, through the same checks as a cell
    /// edit (the schema, the focus, a game playing). Returns false, saying why in <see cref="Said"/>, when it's refused.</summary>
    public bool Apply(BatchPlan plan)
    {
        if(selectedFill is {} captured&&ReferenceEquals(captured.Plan,plan)
            &&(Editor.Session.Revision!=captured.Revision||!ReferenceEquals(Editor.Session.Current,captured.Document)
                ||Shown!=captured.List||!Editor.Selection.SequenceEqual(captured.Selection)))
        {Said="The data or row selection changed; preview the selected-row edit again.";return false;}
        if (!plan.Ok) { Said = string.Join(" ", plan.Problems); return false; }
        if (plan.Commands.Count == 0) { Said = "Nothing to change: every cell already has that value."; return true; }
        if (Editor.Playing is { } playing) { Said = $"'{playing.Title}' is playing; stop it to edit."; return false; }
        var touched=ReferenceEquals(selectedFill?.Plan,plan)?selectedFill!.Selection:plan.Commands.OfType<SetValue>().Select(c=>ItemOf(c.At)).Distinct().ToArray();
        var outside = touched.Where(i => !Editor.InFocus(i)).Distinct().ToList();
        if (outside.Count > 0) { Said = $"{outside.Count} of these rows are outside the place you're focused on; step out of focus to edit them."; return false; }
        if (Editor.Execute(new SetValues(plan.Commands.OfType<SetValue>().ToList()),touched))   // one rebuild, one journal entry (A033)
        {
            Said = $"Set {plan.Commands.Count} cell{(plan.Commands.Count == 1 ? "" : "s")} as one step.";
            return true;
        }
        Said = Explain(Editor.Refused.Count>0?Editor.Refused:Editor.Session.LastIssues);
        return false;
    }

    // The UI's immediate result sentence. Public Apply keeps its established Said contract for existing callers.
    private static string AppliedSentence(BatchPlan plan)
    {
        var cells = $"{plan.Commands.Count} cell{(plan.Commands.Count == 1 ? "" : "s")}";
        return plan.Operation switch
        {
            "fill" => $"Filled {cells}; one Undo reverses the whole fill.",
            "selected fill" => $"Set {cells} in selected rows; one Undo reverses the whole edit.",
            "paste" => $"Pasted {cells}; one Undo reverses the whole paste.",
            _ => $"Set {cells} as one step; one Undo reverses the whole edit.",
        };
    }

    // Each issue with the row and column it's about, by their labels, then the rule broken, which says what would do
    // (U18: what, where, and how to fix it; found by A037: "must be at least 0" didn't say which column).
    private string Explain(IEnumerable<Issue> issues) => string.Join(" ", issues.Select(i =>
    {
        if (i.Path is not { } path || Pointer.FromPath(path) is not { } at || Pointer.Split(at) is not { Count: >= 3 } parts) return i.Message;
        var item = Pointer.Join(parts.Take(2));
        return $"{Editor.Outline.Label(Editor.Session.Current, item) ?? item}, {Cell(item, parts[2])?.Label ?? parts[2]}: {i.Message}";
    }));

    private static string ItemOf(string pointer) => Pointer.Join(Pointer.Split(pointer).Take(2));

    /// <summary>This table as a window panel.</summary>
    public Panel AsPanel(string id = "editor.table", string title = "Table") => new(id, title, Draw, Width: 760, Height: 480,
        Commands:
        [
            new("fill", "Fill a column of the shown rows...", () => focusFill = true),
            new("paste", "Paste the clipboard at the picked cell", () => (pending = PlanPaste(Anchor!.Value.Item, Anchor.Value.Field, ImGui.GetClipboardText().ToString() ?? "")).Ok,
                () => Anchor is null ? "pick a cell first" : null),
            new("copy", "Copy the picked cells", () => Copy() is not null, () => Anchor is null ? "pick a cell first" : null),
        ]);

    private bool focusFill;   // the palette's "Fill a column" puts the keyboard in the fill box

    /// <summary>Draws the table into the current ImGui window and handles its edits.</summary>
    public void Draw()
    {
        if (moveTo is not null && ImGui.GetFrameCount() - moveFrame > 1) moveTo = null;   // its cell wasn't drawn
        var lists = Lists();
        if (lists.Count == 0) { Ui.TextDisabled("This file has no list of items to show as a table."u8); return; }
        var shown = Shown!;
        ImGui.SetNextItemWidth(180);
        if (Ui.BeginCombo("##list"u8, Pointer.Split(shown)[0]))
        {
            foreach (var l in lists)
                if (Ui.Selectable(Pointer.Split(l)[0], l == shown)) { List = l; Sort = null; }
            Ui.EndCombo();
        }
        ImGui.SameLine();
        Ui.NameItem("##list"u8, "List");
        ImGui.SetNextItemWidth(200);
        filterText.EvaluateLength();
        if (Ui.InputText("Filter##table"u8, filterText)) { filterText.EvaluateLength(); Filter = filterText.ToString(); }
        var columns = Columns();
        var visible = Visible();
        if (back.Count > 0 || CanBackElsewhere?.Invoke() == true)
        {
            ImGui.SameLine();
            if (Ui.SmallButton("Back"u8)) Back();
        }
        ImGui.SameLine();
        Ui.TextDisabled($"{visible.Count} of {Items().Count} shown{(Said.Length > 0 ? "  |  " + Said : "")}");
        BatchBar(columns);

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable | ImGuiTableFlags.SizingFixedFit;
        if (!Ui.BeginTable("##data"u8, columns.Count + 1, flags)) return;
        ImGui.TableSetupScrollFreeze(1, 1);   // the name column and the header stay put
        ImGui.TableSetupColumn("Item"u8, ImGuiTableColumnFlags.NoHide, 160 * ImGui.GetFontSize() / 13f, default);
        // Widths grow with the text size, and a choice column fits its longest choice beside the combo's arrow, so larger
        // text isn't cut off (found by A059: "Normal" was clipped at 1.5x and 2x).
        var scale = ImGui.GetFontSize() / 13f;
        var style = ImGui.GetStyle();
        foreach (var c in columns)
        {
            var width = (!c.Editable ? 80 : c.Kind is PropertyKind.Text ? 160 : 90) * scale;
            if (c.Kind is PropertyKind.Choice && c.Choices.Count > 0)
                width = MathF.Max(width, c.Choices.Max(ch => ImGui.CalcTextSize(ch).X) + ImGui.GetFrameHeight() + style.FramePadding.X * 2 + style.CellPadding.X * 2);
            ImGui.TableSetupColumn(c.Label, ImGuiTableColumnFlags.None, width, default);
        }
        ImGui.TableHeadersRow();
        ReadSort(columns);
        var doc = Editor.Session.Current;
        // Only the rows on screen are drawn (ImGui's list clipper): a frame's text lives in a fixed per-frame buffer, and
        // drawing all of a 10,000-row list overflowed it (found by A033).
        var clipper = new ImGuiListClipper();
        clipper.Begin(visible.Count, -1);
        while (clipper.Step())
            for (var r = clipper.DisplayStart; r < clipper.DisplayEnd; r++)
            {
                var item = visible[r];
                Ui.TableNextRow(ImGuiTableRowFlags.None, 0);
                Ui.TableSetColumnIndex(0);
                var picked=Ui.Selectable($"{RowLabel(doc, item)}##{item}",Editor.Selection.Contains(item));
                SelectTableRow(visible,r,picked);
                ImGui.SetItemTooltip("Ctrl+click or Ctrl+Space selects several rows. Shift+click selects a range."u8);
                RowContext(item, columns);
                var cells = RowsOf(item);
                for (var c = 0; c < columns.Count; c++)
                {
                    Ui.TableSetColumnIndex(c + 1);
                    if (cells.TryGetValue(columns[c].Id, out var cell)) DrawCell(item, cell);
                    else Ui.TextDisabled("-"u8);
                }
            }
        clipper.End();
        Ui.EndTable();
    }

    // Row-label surface only: every action stays on the existing table command/preview paths.
    private void RowContext(string item, IReadOnlyList<InspectorRow> columns)
    {
        var io = ImGui.GetIO();
        var mouse = ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        var keyboard = ImGui.IsItemFocused() && !io.WantTextInput && !io.KeyCtrl && !io.KeyAlt && !io.KeySuper
            && (ImGui.IsKeyPressed(ImGuiKey.Menu, false) || io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.F10, false));
        var popup = "Row actions##" + item;
        if (mouse || keyboard)
        {
            Editor.Select(item);
            if (columns.Count > 0) { Anchor = (item, columns[0].Id); RangeEnd = (item, columns[^1].Id); }
            ImGui.OpenPopup(popup);
        }
        if (!Ui.BeginPopup(popup)) return;
        void Entry(string label, Action action, string? reason = null)
        {
            if (Ui.MenuItem(label, ""u8, false, reason is null, reason)) action();
        }
        var empty = columns.Count == 0 ? "This row has no table columns." : null;
        Entry("Copy", () =>
        {
            Anchor = (item, columns[0].Id); RangeEnd = (item, columns[^1].Id); CopyRow(item);
        }, empty);
        Entry("Paste", () =>
        {
            Anchor = (item, columns[0].Id); RangeEnd = null;
            pending = PlanPaste(item, columns[0].Id, ImGui.GetClipboardText().ToString() ?? "");
        }, empty ?? (Cell(item, columns[0].Id) is { Editable: true } ? null : "The first column cannot be pasted into here."));
        Entry("Fill column...", () => { focusFill = true; }, columns.Any(c => c.Editable) ? null : "This table has no editable columns.");
        var removal = new Inspector<DataDocument>(Editor).Add(Type);
        var removeReason = Editor.Playing is not null ? "Stop Play before deleting a row."
            : !Editor.InFocus(item) ? "This row is outside the current focus."
            : Type.RemoveHereCommands(Editor.Session.Current, [item]) is null ? "This object type does not provide row removal." : null;
        Entry("Delete row", () =>
        {
            Editor.Select(item);
            Said = removal.RemoveHere() ? "Deleted row; Undo restores it." : string.Join(" ", removal.LastIssues.Select(i => i.Message));
        }, removeReason);
        Entry("Insert row above", () => { }, "This table does not expose an insert-at-row action.");
        Entry("Insert row below", () => { }, "This table does not expose an insert-at-row action.");
        Ui.EndPopup();
    }

    // A header click sorts the view (the file's order is untouched); a third click on the same column clears it.
    private void ReadSort(IReadOnlyList<InspectorRow> columns)
    {
        var specs = ImGui.TableGetSortSpecs();
        if (specs.IsNull() || !specs.SpecsDirty) return;
        if (!sortSeen)   // the table's first frame reports ImGui's default sort; a sort already set (by code) wins over it
        {
            sortSeen = true;
            if (Sort is not null) { specs.SpecsDirty = false; return; }
        }
        if (specs.SpecsCount == 0) Sort = null;
        else
        {
            var first = specs.Specs[0];
            var col = first.ColumnIndex;
            Sort = col <= 0 ? null : (columns[col - 1].Id, first.SortDirection == ImGuiSortDirection.Descending);
        }
        specs.SpecsDirty = false;
    }

    // The fill and paste row: fill a column of the shown rows with one value, or paste the clipboard's grid at the last
    // cell touched. Both show what they'd do and wait for Apply; nothing changes before that.
    private void BatchBar(IReadOnlyList<InspectorRow> columns)
    {
        var editable = columns.Where(c => c.Editable).ToList();
        if (editable.Count == 0) return;
        if (fillField.Length == 0 || editable.All(c => c.Id != fillField)) fillField = editable[0].Id;
        ImGui.SetNextItemWidth(140);
        if (Ui.BeginCombo("##fillcol"u8, fillField))
        {
            foreach (var c in editable) if (Ui.Selectable(c.Label + "##" + c.Id, c.Id == fillField)) fillField = c.Id;
            Ui.EndCombo();
        }
        ImGui.SameLine();
        Ui.NameItem("##fillcol"u8, "Fill column");
        ImGui.SetNextItemWidth(120);
        if (focusFill) { ImGui.SetKeyboardFocusHere(); focusFill = false; }
        Ui.InputText("##fillval"u8, fillText);
        Ui.NameItem("##fillval"u8, "Fill value");
        ImGui.SameLine();
        // Apply or Cancel took its row away with the focus in it (A210): focus lands on a button that stays (never a text box,
        // which would take Ctrl+Z for itself): Paste at cell with a cell picked, else Fill shown rows.
        if (refocusAfterPlan && Anchor is null) { ImGui.SetKeyboardFocusHere(); refocusAfterPlan = false; }
        if (Ui.Button("Fill shown rows"u8)) { selectedFill=null;fillText.EvaluateLength(); pending = PlanFill(fillField, fillText.ToString()); }
        ImGui.SameLine();
        Ui.BeginDisabled(Anchor is null);
        if (refocusAfterPlan) { ImGui.SetKeyboardFocusHere(); refocusAfterPlan = false; }
        if (Ui.Button("Paste at cell"u8) && Anchor is { } at) {selectedFill=null;pending = PlanPaste(at.Item, at.Field, ImGui.GetClipboardText().ToString() ?? "");}
        Ui.EndDisabled();
        if (pending is { } plan)
        {
            if (plan.Counts is { } counts)
            {
                Ui.TextDisabled($"Would change {counts.Changed} cell{(counts.Changed == 1 ? "" : "s")}; {counts.Unchanged} unchanged; {counts.Refused} refused.");
                foreach (var reason in counts.Reasons)
                    Ui.TextWrapped($"{reason.Cells} cell{(reason.Cells == 1 ? "" : "s")}: {reason.Reason}.");
            }
            else
                Ui.TextDisabled(plan.Ok ? $"Would change {plan.Commands.Count} of {plan.Cells} cells, as one undo step." : "Can't: " + string.Join(" ", plan.Problems.Take(3)));
            Ui.TextDisabled("Whole-document checks still run at Apply."u8);
            ImGui.SameLine();
            Ui.BeginDisabled(!plan.Ok || plan.Commands.Count == 0);
            if (Ui.SmallButton("Apply"u8))
            {
                var selected=ReferenceEquals(selectedFill?.Plan,plan);
                var applied=Apply(plan);
                if (applied && plan.Commands.Count > 0) Said = AppliedSentence(plan);
                else if(!applied&&selected)SelectedApplyRefused();
                pending = null;
                selectedFill=null;
                if(selected)focusSelectedFill=true;else refocusAfterPlan=true;
            }
            Ui.EndDisabled();
            ImGui.SameLine();
            if (Ui.SmallButton("Cancel"u8))
            {var selected=ReferenceEquals(selectedFill?.Plan,plan);pending=null;selectedFill=null;if(selected)focusSelectedFill=true;else refocusAfterPlan=true;}
        }
        SelectedFillControls();
    }

    // ---- the keyboard (A029 P0.7, found by A037) ----

    /// <summary>The far corner of a block of cells picked from <see cref="Anchor"/> with Shift and the arrow keys, if
    /// any. <see cref="Copy"/> copies that block.</summary>
    public (string Item, string Field)? RangeEnd { get; private set; }

    private bool refocusAfterPlan;   // Apply or Cancel removed the focused row: put the focus back next frame
    private (string Item, string Field)? moveTo;
    private int moveFrame;

    /// <summary>Copies the picked block, or the anchor cell alone, to the clipboard as tab-separated rows in shown order,
    /// the way a spreadsheet does, so it pastes back here or into one. Returns the text, or null with no cell picked.</summary>
    public string? Copy()
    {
        if (Anchor is not { } a) return null;
        var b = RangeEnd ?? a;
        var visible = Visible();
        var columns = Columns().Select(c => c.Id).ToList();
        int r0 = IndexOf(visible, a.Item), r1 = IndexOf(visible, b.Item), c0 = columns.IndexOf(a.Field), c1 = columns.IndexOf(b.Field);
        if (r0 < 0 || r1 < 0 || c0 < 0 || c1 < 0) return null;
        return CopyCells(visible.Skip(Math.Min(r0,r1)).Take(Math.Abs(r1-r0)+1).ToList(),
            columns.Skip(Math.Min(c0,c1)).Take(Math.Abs(c1-c0)+1).ToList());
    }

    /// <summary>Copies the explicit current row with the same columns and TSV quoting as the row menu.
    /// Prior list, filter, sort and cell/range selection are not used as the target and remain unchanged.</summary>
    public string? CopyRow(string item)
    {
        var parts=Pointer.Split(item);
        if(parts.Count==0) return null;
        var before=List;
        try
        {
            List=Pointer.Join(parts.Take(parts.Count-1));
            if(!Items().Contains(item)) return null;
            var columns=Columns().Select(column=>column.Id).ToList();
            return columns.Count==0 ? null : CopyCells([item],columns);
        }
        finally {List=before;}
    }

    private string CopyCells(IReadOnlyList<string> items,IReadOnlyList<string> columns)
    {
        var lines=items.Select(item=>string.Join("\t",columns.Select(column=>Quoted(AsText(Cell(item,column)?.Value))))).ToList();
        var text=string.Join("\n",lines);
        ImGui.SetClipboardText(text);
        var cells=items.Count*columns.Count;
        Said=$"Copied {cells} cell{(cells==1 ? "" : "s")}.";
        return text;
    }

    // A cell holding a tab, a new line or a quote is quoted, its quotes doubled, as spreadsheets copy it, so it pastes back as one
    // cell (found by A206: it split into two and overwrote its neighbour).
    private static string Quoted(string text) =>
        text.AsSpan().IndexOfAny('\t', '\n', '"') >= 0 || text.Contains('\r') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    // A cell's value as paste reads it back: numbers with a dot, true or false, text as it is.
    private static string AsText(object? v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static int IndexOf(IReadOnlyList<string> list, string item)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == item) return i;
        return -1;
    }

    // The cell rows and columns away in the shown table, or null past an edge.
    private (string Item, string Field)? Step((string Item, string Field) from, int rows, int cols)
    {
        var visible = Visible();
        var columns = Columns();
        var r = IndexOf(visible, from.Item);
        var c = columns.Select(x => x.Id).ToList().IndexOf(from.Field);
        if (r < 0 || c < 0) return null;
        r += rows;
        c += cols;
        return r >= 0 && r < visible.Count && c >= 0 && c < columns.Count ? (visible[r], columns[c].Id) : null;
    }

    // Spreadsheet keys on the focused cell. Up and Down keep the column: ImGui's own move scored the row's name nearer
    // and went there. Shift and an arrow grow a block from the anchor while focus stays put. Ctrl+C copies the block, or
    // the cell when it isn't being typed in (while typing, it copies the text, as ImGui does). The table makes these moves
    // itself, cancelling ImGui's, and a move never starts an edit; Enter does. BORROWED: the keys, from spreadsheets.
    private void Keyboard(string item, InspectorRow cell)
    {
        if (moveTo == (item, cell.Id))
        {
            ImGui.Internal.SetFocusID(ImGui.GetItemID(), ImGui.Internal.GetCurrentWindow());
            ImGui.SetNavCursorVisible(true);
            moveTo = null;
        }
        if (!ImGui.IsItemFocused() || moveFrame == ImGui.GetFrameCount()) return;   // one move per key press, not one per cell it lands on
        var io = ImGui.GetIO();
        var active = ImGui.IsItemActive();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C, false) && (RangeEnd is not null || !active)) { Copy(); return; }
        if (io.KeyCtrl || io.KeyAlt) return;
        var (rows, cols) = ImGui.IsKeyPressed(ImGuiKey.DownArrow) ? (1, 0) : ImGui.IsKeyPressed(ImGuiKey.UpArrow) ? (-1, 0)
            : ImGui.IsKeyPressed(ImGuiKey.RightArrow) ? (0, 1) : ImGui.IsKeyPressed(ImGuiKey.LeftArrow) ? (0, -1) : (0, 0);
        if (rows == 0 && cols == 0) return;
        if (io.KeyShift)
        {
            if (active) return;   // while typing, Shift and an arrow select text
            if (Step(RangeEnd ?? (item, cell.Id), rows, cols) is { } end) RangeEnd = end;
            ImGui.Internal.NavMoveRequestCancel();
            return;
        }
        if (cols != 0) return;   // Left and Right are ImGui's, and the text's while typing
        if (Step((item, cell.Id), rows, 0) is not { } next) return;
        if (active) ImGui.Internal.ClearActiveID();
        moveTo = next;
        moveFrame = ImGui.GetFrameCount();
        ImGui.Internal.NavMoveRequestCancel();
    }

    /// <summary>The fill or paste waiting for Apply or Cancel, if any.</summary>
    public BatchPlan? Pending => pending;

    // A drawn row's name, kept per document and outline version as the outliner keeps it; live for an outline with no version.
    private string RowLabel(DataDocument doc, string item)
    {
        if (Editor.Outline.Version is not { } version) return Editor.Outline.Label(doc, item) ?? item;
        if (labelsFor is not { } k || !ReferenceEquals(k.Doc, doc) || k.Version != version) { labels.Clear(); labelsFor = (doc, version); }
        if (!labels.TryGetValue(item, out var label)) labels[item] = label = Editor.Outline.Label(doc, item) ?? item;
        return label;
    }

    private (DataDocument Doc, long Version)? labelsFor;
    private readonly Dictionary<string, string> labels = new(StringComparer.Ordinal);

    private void DrawCell(string item, InspectorRow cell)
    {
        var id = $"##{item}/{cell.Id}";
        // A cell with a link leaves room for its "->", which otherwise fell past the column's edge and was clipped away
        // (found by A048: the arrow was logged but never drawn). Asked every frame for every drawn cell (found by A179): the
        // tools' own References and Links (DataEditing.SchemaReferences, ReferenceMap.Resolve) keep their answers while the
        // documents and schemas they read stay the same.
        var link = LinkOf(item, cell.Id) is not null;
        var style = ImGui.GetStyle();
        ImGui.SetNextItemWidth(link ? -(ImGui.CalcTextSize("->"u8).X + style.FramePadding.X * 2 + style.ItemSpacing.X) : -1);
        void Arrow()
        {
            if (!link) return;
            ImGui.SameLine();
            if (Ui.SmallButton($"->##go{item}/{cell.Id}")) Follow(item, cell.Id);
            Tooltips.Show("Follow reference");
        }
        if (!cell.Editable) { Ui.TextDisabled(cell.Value?.ToString() ?? "-"); Arrow(); return; }
        if (cell.Value is null && cell.Kind != PropertyKind.Toggle) { Ui.TextDisabled("(not set)"u8); return; }
        switch (cell.Kind)
        {
            case PropertyKind.Number:
            {
                var v = Convert.ToDouble(cell.Value);
                if (Ui.InputDouble(id, ref v, 0, 0, "%g"u8, ImGuiInputTextFlags.EnterReturnsTrue)) SetCell(item, cell.Id, v);
                break;
            }
            case PropertyKind.Integer:
            {
                var v = Convert.ToDouble(cell.Value);
                if (Ui.InputDouble(id, ref v, 0, 0, "%.0f"u8, ImGuiInputTextFlags.EnterReturnsTrue)) SetCell(item, cell.Id, v);   // not rounded: 1.5 is refused with the reason (found by A134)
                break;
            }
            case PropertyKind.Toggle:
            {
                var v = cell.Value is true;
                if (Ui.Checkbox(id, ref v)) SetCell(item, cell.Id, v);
                break;
            }
            case PropertyKind.Choice:
            {
                var current = cell.Value?.ToString() ?? "";
                if (Ui.BeginCombo(id, current))
                {
                    foreach (var choice in cell.Choices)
                        if (Ui.Selectable(choice, choice == current) && choice != current) SetCell(item, cell.Id, choice);
                    Ui.EndCombo();
                }
                break;
            }
            default:
            {
                var text = cell.Value?.ToString() ?? "";
                if (EditorWindow<DataDocument>.TooLong(text, out var said)) { Ui.TextDisabled(said); break; }
                if (!texts.TryGetValue(id, out var field) || (!ImGui.IsItemActive() && field.ToString() != text)) texts[id] = field = EditorWindow<DataDocument>.TextBox(text);
                if (Ui.InputText(id, field, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    field.EvaluateLength();
                    if (field.ToString() != text) SetCell(item, cell.Id, field.ToString());
                }
                break;
            }
        }
        Ui.NameItem(id, $"{RowLabel(Editor.Session.Current, item)}: {cell.Label}");
        if ((ImGui.IsItemActivated() || ImGui.IsItemFocused()) && Anchor != (item, cell.Id)) { Anchor = (item, cell.Id); RangeEnd = null; }   // where a paste lands
        Keyboard(item, cell);
        Arrow();
        if (cell.Help.Length > 0) ImGui.SetItemTooltip(cell.Help);
    }
}
