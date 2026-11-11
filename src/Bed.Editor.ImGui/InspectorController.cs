using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

// INVENTED (code_health.md rank 7): one controller per window owns inspector navigation and drafts.
// The frame host keeps layout, shortcuts, status and command routing. Document mutations still go
// through the same Inspector/EditorModel. Call on the host's ImGui thread, just like EditorWindow.
internal sealed class InspectorController<TDoc> where TDoc : class
{
    private readonly EditorModel<TDoc> Editor;
    private readonly Inspector<TDoc>? Inspector;
    private readonly Func<string, string> Label;
    private readonly Func<Issue, bool> GoTo;
    private readonly Func<bool, string, string, bool> Say;
    private readonly Func<IReadOnlyList<Issue>, string> Explain;
    private readonly Action<string> setStatus;
    private string status { set => setStatus(value); }
    private readonly Dictionary<string, ImInputString> textFields = [];
    private string[] selectionIds=[];
    private string selectionKey="";
    private string SelectionKey()
    {
        if(!Editor.Selection.SequenceEqual(selectionIds))
        {selectionIds=Editor.Selection.ToArray();selectionKey=string.Join(",",selectionIds);}
        return selectionKey;
    }

    internal InspectorController(EditorModel<TDoc> editor, Inspector<TDoc>? inspector,
        Func<string, string> label, Func<Issue, bool> goTo,
        Func<bool, string, string, bool> say, Func<IReadOnlyList<Issue>, string> explain,
        Action<string> setStatus)
    {
        Editor = editor;
        Inspector = inspector;
        Label = label;
        GoTo = goTo;
        Say = say;
        Explain = explain;
        this.setStatus = setStatus;
    }

    private static string Text(ImInputString field)
    {
        field.EvaluateLength();
        return field.ToString();
    }

    private static string FirstSentence(string text) => EditorWindow<TDoc>.FirstSentence(text);
    private static ImInputString TextBox(string text) => EditorWindow<TDoc>.TextBox(text);
    private static bool TooLong(string text, out string said) => EditorWindow<TDoc>.TooLong(text, out said);

    /// <summary>What finds places in a document, for the box above the inspector. None by default, so no box.</summary>
    public Func<TDoc, string, (IReadOnlyList<FoundPlace> Places, bool More)>? Searcher { get; set; }

    /// <summary>What the last search found. Empty once the document has changed, since an index may mean something else then.</summary>
    public IReadOnlyList<FoundPlace> Found => foundIn == Editor.Session.Revision ? found : [];

    private IReadOnlyList<FoundPlace> found = [];
    private bool foundMore;
    private long foundIn = -1;
    private readonly ImInputString findText = new(128, ""u8);
    private string? revealRow;

    /// <summary>Searches the document for <paramref name="text"/>. Changes nothing in it.</summary>
    public int Find(string text)
    {
        if (Searcher is null) return 0;
        (found, foundMore) = Searcher(Editor.Session.Current, text);
        foundIn = Editor.Session.Revision;
        status = found.Count == 0 ? $"Nothing holds '{text.Trim()}'." : $"Found {found.Count}{(foundMore ? " and more" : "")}.";
        return found.Count;
    }

    /// <summary>Goes to a place in the document: selects what holds it, opens what's folded, turns to its page, shows it if it
    /// isn't set, and scrolls to it. A view action: the document, its undo and its unsaved state don't change. False, saying why
    /// in the host status line, when no row shows that place.</summary>
    // The same admission protects navigation and check runs from hidden committed values behind visible drafts.
    internal bool HasPendingEdits => editing is { } typing && Text(typing.Field) != typing.Shown
        || keptTyped.Any(k => k.StartsWith(SelectionKey() + "/", StringComparison.Ordinal));

    internal bool TryCommitPending(out string why)
    {
        why = "";
        if (editing is { } typing && Text(typing.Field) != typing.Shown)
        {
            var (ok, refused) = typing.Commit(Text(typing.Field));
            if (!ok)
            {
                keptTyped.Add(typing.Key);
                why = refused ?? "What's being typed was refused. Correct it first.";
                return false;
            }
        }
        var here = SelectionKey() + "/";
        if (keptTyped.Any(k => k.StartsWith(here, StringComparison.Ordinal)))
        {
            why = "An edit here was refused and is waiting to be corrected. Correct it or undo it first.";
            return false;
        }
        return true;
    }

    public bool Reveal(string pointer)
    {
        if (Inspector is not { } inspector) { status = "There's no inspector here to show that."; return false; }
        if (!TryCommitPending(out var why)) { status = why; return false; }
        var parts = Pointer.Split(pointer);
        var length = parts.Count;
        var objects = Editor.Outline.ObjectIds(Editor.Session.Current).ToHashSet(StringComparer.Ordinal);
        // Down to the file itself, when its own plain fields make it an owner (found by A126).
        while (length >= 0 && !objects.Contains(Pointer.Join(parts.Take(length)))) length--;
        if (length < 0 || (!(Editor.Selection.Count == 1 && Editor.Primary == Pointer.Join(parts.Take(length))) && !Editor.Select(Pointer.Join(parts.Take(length))))) { status = $"Nothing here holds {pointer}."; return false; }
        var parent = SelectionKey();
        var rows = inspector.Rows();
        var place = "";
        InspectorRow? row = null;
        for (var i = length; i < parts.Count; i++)
        {
            place += Pointer.Join([parts[i]]);
            row = rows.FirstOrDefault(r => DataEditing.PlaceOfRow(r.Id) == place);
            if (row is null) { status = $"There's no row for {pointer} here."; return false; }
            var key = FoldKey(parent, row, rows);
            if (i + 1 < parts.Count)
            {
                folds[key] = true;
                rows = inspector.Nested(row);
                if (rows.Count > LongList)   // as the row draws them: its page among what it shows (found by A096)
                {
                    var nextPlace = place + Pointer.Join([parts[i + 1]]);
                    if (!row.CanAdd && !showingUnset.Contains(key) && rows.FirstOrDefault(r => DataEditing.PlaceOfRow(r.Id) == nextPlace) is { Value: null, Mixed: false }) showingUnset.Add(key);
                    IReadOnlyList<InspectorRow> shown = !row.CanAdd && !showingUnset.Contains(key) ? [.. rows.Where(r => r.Value is not null || r.Mixed)] : rows;
                    var at = shown.ToList().FindIndex(r => DataEditing.PlaceOfRow(r.Id) == nextPlace);
                    if (at >= 0) pages[key] = at / LongList;
                }
            }
            parent = key;
        }
        revealRow = row?.Id;
        status = $"At {pointer}.";
        return true;
    }

    private void FindBox()
    {
        Ui.TextWrapped("Find in this file"u8);
        Ui.NameNextItem("##inspector.find"u8,"Find in this file");
        ImGui.SetNextItemWidth(-1);
        if (Ui.InputText("##inspector.find"u8, findText, ImGuiInputTextFlags.EnterReturnsTrue)) Find(Text(findText));
        places[("", InspectorPart.Find)] = ImGui.GetItemRectMin();
        foreach (var (place, n) in Found.Select((x, n) => (x, n)))
            if (Ui.Selectable($"{place.Preview}##found{n}")) Reveal(place.Pointer);
        if (Found.Count > 0 && foundMore) Ui.TextDisabled("There are more: search for something longer."u8);
    }

    public void Draw()
    {
        places.Clear();   // before anything returns: nothing drawn now means nothing to reach (found by A082)
        wasEditing = editing is not null;
        editing = null;
        if (Searcher is not null) FindBox();
        if (Inspector is null || Editor.Selection.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text,ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            Ui.TextWrapped(Editor.Selection.Count == 0 ? "Select an Outliner item to see its properties." : "No properties for this.");
            ImGui.PopStyleColor();
            return;
        }
        Ui.TextEllipsis(Editor.Selection.Count == 1 ? Label(Editor.Selection[0]) : $"{Editor.Selection.Count} selected", "inspector.selection");
        DropMovedDrafts(Inspector);
        var selection = SelectionKey();
        // A box's live typing mustn't outlive a change of selection, or Enter writes it into the same field of another object
        // (found by A096). Only then do the ids change, which makes ImGui let go of the box.
        if(selection!=lastSelection){nudgeKey=null;fieldMessages.Clear();}
        if (selection != lastSelection && wasEditing) fieldEpoch++;
        lastSelection = selection;
        if (fieldEpoch > 0) ImGui.PushID(fieldEpoch);
        var top = Inspector.Rows();
        foreach (var row in top) InspectorRowView(row, selection, top);
        Ui.TextWrapped("Number fields: Up/Down +/-1; Shift x10; Ctrl x0.1. Release the arrows to finish one undo step."u8);
        if (fieldEpoch > 0) ImGui.PopID();
        foreach (var (issue, n) in Inspector.Issues().Select((x, n) => (x, n)))
            if (Ui.Selectable($"{issue}##issue{n}")) GoTo(issue);   // each problem leads back to its object
        // After the fields: keyboard order runs through a record's fields first, and a Remove at the top is too easy to hit.
        if (Inspector is { CanAddHere: true } addable)
        {
            if (Ui.SmallButton("Add item"u8)) Report(addable.AddHere(), "Added an item.");
            places[("", InspectorPart.Add)] = ImGui.GetItemRectMin();
        }
        if (Inspector is { CanRemoveHere: true } removable)
        {
            if (Ui.SmallButton("Remove this item"u8)) Report(removable.RemoveHere(), "Removed the item.");
            places[("", InspectorPart.Remove)] = ImGui.GetItemRectMin();
        }
        ForgetFieldsOutOfSight();
    }

    /// <summary>Where the inspector last drew a part of a row, in screen coordinates, or null when it didn't. For a host,
    /// a script or a test to reach a control by what it is rather than by a pixel position that moves with the layout.</summary>
    public float2? InspectorPlace(string rowId, InspectorPart part = InspectorPart.Field) =>
        places.TryGetValue((rowId, part), out var at) ? at : null;

    private readonly Dictionary<(string, InspectorPart), float2> places = [];
    private const int LongList = 50;

    // What a row is called for keeping its fold: a list item's name without its number, which changes when one above it goes.
    // An unnamed item has only its position to go by.
    // A row's place among its siblings, for keeping its fold and page: a segment of a path, escaped so a key holding a slash
    // isn't taken for two levels, and numbered among items of the same name, which would otherwise share one (found by A096).
    private string FoldKey(string parent, InspectorRow row, IReadOnlyList<InspectorRow> siblings) =>
        parent + Pointer.Join([foldNames.GetValue(siblings, FoldNames)[row.Id]]);

    private static Dictionary<string, string> FoldNames(IReadOnlyList<InspectorRow> rows)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var name = FoldName(row);
            var n = seen.GetValueOrDefault(name);
            seen[name] = n + 1;
            names[row.Id] = n == 0 ? name : $"{name}#{n}";
        }
        return names;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<InspectorRow>, Dictionary<string, string>> foldNames = new();

    private static string FoldName(InspectorRow row)
    {
        var dot = row.Label.IndexOf(". ", StringComparison.Ordinal);
        return row.CanRemove && dot > 0 && row.Label[..dot].All(char.IsAsciiDigit) ? row.Label[(dot + 2)..] : Pointer.Split(DataEditing.PlaceOfRow(row.Id)).LastOrDefault() ?? row.Id;   // its field's own name, not a title two may share (found by A096)
    }

    private readonly Dictionary<string, bool> folds = new(StringComparer.Ordinal);

    // One row, then anything inside it, indented under it: a nested object's fields are edited where they belong.
    private void InspectorRowView(InspectorRow row, string parent, IReadOnlyList<InspectorRow> siblings)
    {
        if (Inspector is not { } inspector) return;
        var key = FoldKey(parent, row, siblings);
        ImGui.PushID(row.Id);
        var inside = inspector.Nested(row);
        var nested = inside;
        // A schema can offer far more fields than an object has (found on real data: 299 offered, 4 present, the 4 scattered
        // over pages), so a long object shows what it has, and the rest are a button away. A list's items are all really there.
        var unset = inside.Count > LongList && !row.CanAdd ? inside.Where(r => r.Value is null && !r.Mixed).ToList() : [];
        var showUnset = showingUnset.Contains(key);
        if (unset.Count > 0 && !showUnset) inside = inside.Except(unset).ToList();
        var open = false;
        var closing = false;
        if (inside.Count > 0 || unset.Count > 0)   // with nothing set, the arrow still leads to the button (found by A093)
        {
            // An arrow to fold what's under it, and a long list starts folded (A079: a works' methods fill the panel).
            // Kept per selected object here: ImGui would remember it by the field's name, so folding one item's long list
            // would fold the same field on the next item selected.
            ImGui.SetNextItemOpen(folds.TryGetValue(key, out var kept) ? kept : inside.Count <= LongList);
            Ui.NameNextItem("##inside"u8, row.Label);
            open = Ui.TreeNodeEx("##inside"u8, ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.NoTreePushOnOpen);   // ids stay the same, folded or not
            folds[key] = open;
            closing = !open && ImGui.IsItemToggledOpen();
            ImGui.SameLine();
        }
        // First on its line: after a field it ran off the edge of a narrow, indented panel.
        if (row.CanRemove)
        {
            if (Ui.SmallButton("Remove"u8)) Report(inspector.RemoveItem(row), $"{row.Label} removed.");
            places[(row.Id, InspectorPart.Remove)] = ImGui.GetItemRectMin();
            ImGui.SameLine();
        }
        Field(row);
        places[(row.Id, InspectorPart.Field)] = ImGui.GetItemRectMin();
        if (revealRow == row.Id)
        {
            ImGui.SetScrollHereY(0.3f);
            if (ImGui.GetItemID() != 0)
            {
                ImGui.Internal.SetFocusID(ImGui.GetItemID(), ImGui.Internal.GetCurrentWindow());
                ImGui.SetNavCursorVisible(true);
            }
            var draw=ImGui.GetWindowDrawList();
            if(ImGui.IsItemVisible()&&ImGui.GetItemRectMin().Y>=draw.GetClipRectMin().Y&&ImGui.GetItemRectMax().Y<=draw.GetClipRectMax().Y)
                revealRow = null;
        }
        if (row.Overridden && row.Editable)
        {
            ImGui.SameLine();
            if(Editor.Session.Current is Scene2)
            {Ui.NameNextItem("Revert"u8,"Revert "+row.Label);if(Ui.SmallButton("Revert"u8))Report(inspector.Reset(row.Id),$"{row.Label} reverted to the accepted prefab value.");}
            else if (Ui.SmallButton("reset"u8)) Report(inspector.Reset(row.Id), $"{row.Label} reset.");
        }
        if (row.Range is { } range)
        {
            ImGui.SameLine();
            Ui.TextDisabled(range.ToString());   // the limits, before typing
        }
        if (row.Help.Length > 0)
        {
            // Help shows its first sentence; the whole text appears on hover (the user: long help made the panel
            // "an awful lot of ... information"). It wraps rather than running off the edge.
            var shortHelp = FirstSentence(row.Help);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled));
            Ui.FieldCaption(shortHelp == row.Help ? shortHelp : shortHelp + " ...");
            ImGui.PopStyleColor();
            if (shortHelp != row.Help) ImGui.SetItemTooltip(row.Help);
        }
        if(fieldMessages.TryGetValue((SelectionKey(),row.Id),out var fieldMessage))
            Ui.StatusWrapped(fieldMessage,"inspector.field.issue."+row.Id);
        if(Editor.Session.Current is Bed.Data.DataDocument)
        {
            var relative=DataEditing.PlaceOfRow(row.Id);var owner=Editor.Primary??"";
            foreach(var issue in inspector.Issues().Where(i=>i.Path is {} path&&Pointer.FromPath(path) is {} at&&(at==owner+relative||at==relative&&owner=="")))
                Ui.TextWrapped($"{row.Label}: {issue.Message}");
        }
        // The frame it folds, its rows are still drawn, so a field being left commits as it would anywhere (found by A082).
        if (open || closing)
        {
            ImGui.Indent();
            if (inside.Count > LongList) PagedRows(row, inside, key, nested);
            else foreach (var child in inside) InspectorRowView(child, key, nested);
            if (unset.Count > 0)
            {
                if (Ui.SmallButton(showUnset ? "Hide the fields not set" : $"Show {unset.Count} fields not set"))
                {
                    if (showUnset) showingUnset.Remove(key); else showingUnset.Add(key);
                }
                places[(row.Id, InspectorPart.Unset)] = ImGui.GetItemRectMin();
            }
            ImGui.Unindent();
        }
        if (row.CanAdd)
        {
            if (Ui.SmallButton("Add"u8))
            {
                var added = inspector.AddItem(row);
                Report(added, $"Added to {row.Label}.");
                if (added)
                {
                    pages[key] = int.MaxValue;
                    var updated=inspector.Nested(row);
                    revealRow=updated.Count>0?updated[^1].Id:null;
                }
            }
            places[(row.Id, InspectorPart.Add)] = ImGui.GetItemRectMin();
        }
        if (row.NamedEntries)
        {
            // Each selected object's own box: a name half typed for one mustn't be added to another (found by A110).
            var box = string.Join("|", Editor.Selection.Append(key));
            if (!newNames.TryGetValue(box, out var name)) newNames[box] = name = new ImInputString(4096, ""u8);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
            var entered = Ui.InputText("##newname" + box, name, ImGuiInputTextFlags.EnterReturnsTrue);
            places[(row.Id, InspectorPart.NewName)] = ImGui.GetItemRectMin();
            ImGui.SameLine();
            if (Ui.SmallButton("Add"u8) || entered)
            {
                var called = Text(name);   // exactly as typed: a space can be part of a name a schema wants (found by A110)
                if (string.IsNullOrWhiteSpace(called)) Say(false, "", "Type a name for the new entry first.");
                else
                {
                    var added = inspector.AddNamed(row, called);
                    Report(added, $"Added {called} to {row.Label}.");
                    if (added) newNames.Remove(box);
                }
            }
            places[(row.Id, InspectorPart.Add)] = ImGui.GetItemRectMin();
        }
        ImGui.PopID();
    }

    private readonly Dictionary<string, ImInputString> newNames = new(StringComparer.Ordinal);

    private const int FieldsKept = 4096;
    private readonly HashSet<string> drawnFields = new(StringComparer.Ordinal);

    /// <summary>How many text boxes the inspector is holding on to. A box out of sight comes back with the value it should show.</summary>
    public int FieldsHeld => textFields.Count;

    // A kilobyte a box, for every field ever drawn, reached 745 MB paging through a long list (found by A091). A box out of sight
    // holds nothing worth keeping unless it holds a refused edit, which stays to be corrected.
    private void ForgetFieldsOutOfSight()
    {
        if (textFields.Count > FieldsKept)
            foreach (var key in textFields.Keys.Where(k => !drawnFields.Contains(k) && !keptTyped.Contains(k)).ToList())
                textFields.Remove(key);
        drawnFields.Clear();
    }

    private readonly Dictionary<string, ImInputString> goTo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> pages = new(StringComparer.Ordinal);
    private readonly HashSet<string> showingUnset = new(StringComparer.Ordinal);

    // A long list, a page at a time: drawing every row of a long one each frame overflowed the frame's text storage and
    // crashed at 10,000 rows of 32 fields (found by A088). A page is what one frame ever draws, however long the list.
    private void PagedRows(InspectorRow list, IReadOnlyList<InspectorRow> inside, string key, IReadOnlyList<InspectorRow> siblings)
    {
        var last = (inside.Count - 1) / LongList;
        var page = Math.Clamp(pages.GetValueOrDefault(key), 0, last);
        var next = page;
        Ui.BeginDisabled(page == 0);
        if (Ui.SmallButton("First"u8)) next = 0;
        places[(list.Id, InspectorPart.First)] = ImGui.GetItemRectMin();
        ImGui.SameLine();
        if (Ui.SmallButton("Earlier"u8)) next = page - 1;
        Ui.EndDisabled();
        places[(list.Id, InspectorPart.Earlier)] = ImGui.GetItemRectMin();
        ImGui.SameLine();
        Ui.BeginDisabled(page == last);
        if (Ui.SmallButton("Later"u8)) next = page + 1;
        places[(list.Id, InspectorPart.Later)] = ImGui.GetItemRectMin();
        ImGui.SameLine();
        if (Ui.SmallButton("Last"u8)) next = last;
        Ui.EndDisabled();
        places[(list.Id, InspectorPart.Last)] = ImGui.GetItemRectMin();
        ImGui.SameLine();
        // The box keeps its full width: on a narrow panel it starts the next line rather than shrinking to nothing.
        if (ImGui.GetContentRegionAvail().X < 70 + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Go to"u8).X) ImGui.NewLine();
        if (!goTo.TryGetValue(key, out var wanted)) goTo[key] = wanted = new ImInputString(16, ""u8);
        ImGui.SetNextItemWidth(70);
        if (Ui.InputText("Go to##goto"u8, wanted, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CharsDecimal)
            && int.TryParse(Text(wanted), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var item))
            next = Math.Clamp(item - 1, 0, inside.Count - 1) / LongList;
        places[(list.Id, InspectorPart.GoTo)] = ImGui.GetItemRectMin();
        ImGui.SameLine();
        var first = page * LongList;
        var end = Math.Min(first + LongList, inside.Count);
        Ui.TextDisabled($"{first + 1} to {end} of {inside.Count}");
        var rowHeight=Math.Clamp(ImGui.GetContentRegionAvail().Y-ImGui.GetFrameHeightWithSpacing()*4,ImGui.GetFrameHeightWithSpacing()*2,400);
        var target=revealRow is {} reveal?DataEditing.PlaceOfRow(reveal):null;
        var containsReveal=target is not null&&inside.Skip(first).Take(end-first).Any(row=>
        {
            var path=DataEditing.PlaceOfRow(row.Id);
            return target==path||target.StartsWith(path+"/",StringComparison.Ordinal);
        });
        if(Ui.BeginChild("Rows###nested."+key,new Brutal.Numerics.float2(0,rowHeight),ImGuiChildFlags.Borders))
            for (var i = first; i < end; i++) InspectorRowView(inside[i], key, siblings);
        Ui.EndChild();
        // A target can be inside a child which is itself off-screen (and therefore skips its rows).
        // Scroll every enclosing page into view as well as the leaf's own scroll region.
        if(containsReveal)ImGui.SetScrollHereY(.3f);
        pages[key] = next;
    }

    private void Field(InspectorRow row)
    {
        var label = row.Overridden ? $"{row.Label} *" : row.Label;
        if (row.Value is null && !row.Mixed) label += " (not set)";   // an optional field this object doesn't have yet
        var sharedTransform=Editor.Session.Current is Scene or Scene2&&row.Kind==PropertyKind.Number
            &&(row.Id.StartsWith("position.",StringComparison.Ordinal)||row.Id.StartsWith("rotation.",StringComparison.Ordinal)||row.Id.StartsWith("scale.",StringComparison.Ordinal));
        if (!row.Editable || row.Mixed&&!sharedTransform)
        {
            Ui.FieldCaption($"{label}: {(row.Mixed ? "(mixed)" : row.Value)}", "inspector.value." + row.Id);
            return;
        }
        if (row.Kind != PropertyKind.Toggle) label = Fit(row, label);
        // Override markers and label wrapping change the display, never the focused field identity.
        label += "###field";
        Ui.NameNextItem(label, row.Label);
        switch (row.Kind)
        {
            case PropertyKind.Choice:   // a dropdown of the allowed values (an enum, say)
            {
                var current = row.Value?.ToString() ?? "";
                if (Ui.BeginCombo(label, current))
                {
                    foreach (var choice in row.Choices)
                    {
                        if (Ui.Selectable(choice, choice == current) && choice != current)
                            ReportField(row, Inspector!.Set(row.Id, choice), null);
                        // Keyboard navigation in the open list starts on the current value, not the first item.
                        if (choice == current) ImGui.SetItemDefaultFocus();
                    }
                    Ui.EndCombo();
                }
                break;
            }
            case PropertyKind.Number or PropertyKind.Integer:
            {
                // Typed as text and read with invariant rules, so nothing is silently reinterpreted: a comma is refused
                // ("1,5" isn't 1.5 everywhere), and so is anything that isn't a number (found by A031).
                var shown = row.Mixed ? "mixed" : row.Value is double d ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    : Convert.ToString(row.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                TextField(row, label, shown, typed =>
                {
                    var t = typed.Trim();
                    if (t.Contains(',')) return (false, $"'{t}' has a comma; write numbers with a dot for decimals, like 1.5.");
                    if (row.Kind == PropertyKind.Integer)
                        return long.TryParse(t, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l)
                            ? (Inspector!.Set(row.Id, l), null) : (false, $"'{t}' isn't a whole number.");
                    return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                        ? (Inspector!.Set(row.Id, v), null) : (false, $"'{t}' isn't a number.");
                });
                break;
            }
            case PropertyKind.Toggle:
            {
                var v = row.Value is true;
                if (Ui.Checkbox(label, ref v)) ReportField(row, Inspector!.Set(row.Id, v), null);
                break;
            }
            default:
                TextField(row, label, row.Value?.ToString() ?? "", typed => (Inspector!.Set(row.Id, typed), null));
                break;
        }
    }

    // A label that won't fit beside its field goes above it, wrapped, and the field takes the row, less room for its reset
    // and limits, so larger text is never cut off at the panel's edge (found by A059: labels clipped at 1.5x and 2x).
    // Returns the label to give the field: unchanged, or hidden when it's drawn above.
    private static string Fit(InspectorRow row, string label)
    {
        var style = ImGui.GetStyle();
        var room = (row.Overridden ? ImGui.CalcTextSize(typeof(TDoc)==typeof(Scene2)?"Revert"u8:"reset"u8).X + style.FramePadding.X * 2 + style.ItemSpacing.X : 0)
            + (row.Range is { } range ? ImGui.CalcTextSize(range.ToString()).X + style.ItemSpacing.X : 0);
        if (ImGui.CalcItemWidth() + style.ItemInnerSpacing.X + ImGui.CalcTextSize(label).X + room <= ImGui.GetContentRegionAvail().X) return label;
        Ui.FieldCaption(label);
        ImGui.SetNextItemWidth(-MathF.Max(1, room));
        return "##" + label;
    }

    // A text box for a field. What was typed is committed on Enter or on leaving the box. When the edit is refused, the
    // typed text stays in the box to be corrected rather than snapping back (found by A031); a successful edit, or the
    // selection changing, clears it.
    private unsafe void TextField(InspectorRow row, string label, string shown, Func<string, (bool Ok, string? Why)> commit)
    {
        var key = SelectionKey() + "/" + row.Id;
        drawnFields.Add(key);
        if (TooLong(shown, out var said)) { Ui.TextDisabled(said); return; }
        if (!textFields.TryGetValue(key, out var field) || (!ImGui.IsAnyItemActive() && !keptTyped.Contains(key) && Text(field) != shown))
            textFields[key] = field = TextBox(shown);
        var numeric=row.Kind is PropertyKind.Number or PropertyKind.Integer;
        // Native history callbacks own Up/Down and update only the text preview until both arrows are released.
        int Nudge(ImGuiInputTextCallbackData* raw)
        {
            var text=System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)(byte*)raw->Buf,raw->BufTextLen)??"";
            if(!double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value))
            {ReportField(row,false,"Enter a finite number before nudging.");return 0;}
            var io=ImGui.GetIO();var step=(io.KeyShift?10:1)*(io.KeyCtrl?.1:1);
            var next=value+(raw->EventKey==ImGuiKey.UpArrow?step:-step);
            if(!double.IsFinite(next)||row.Range is {} range&&!range.Allows(next))
            {ReportField(row,false,"That nudge is outside this field's allowed range.");return 0;}
            if(row.Kind==PropertyKind.Integer&&next!=Math.Truncate(next))
            {ReportField(row,false,"This field needs a whole number; use Up/Down or Shift+Up/Down.");return 0;}
            if(nudgeKey!=key){nudgeKey=key;nudgeRevision=Editor.Session.Revision;}
            var data=new ImGuiInputTextCallbackDataPtr(raw);data.DeleteChars(0,raw->BufTextLen);
            data.InsertChars(0,next.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            return 0;
        }
        Ui.NameNextItem(label,row.Label); // Fit may draw the visible label above a hidden native label.
        var entered = Ui.InputText(label, field, ImGuiInputTextFlags.EnterReturnsTrue|(numeric?ImGuiInputTextFlags.CallbackHistory:0),numeric?Nudge:null);
        var finishedNudge=false;
        if(nudgeKey==key&&(!ImGui.IsItemActive()||(!ImGui.IsKeyDown(ImGuiKey.UpArrow)&&!ImGui.IsKeyDown(ImGuiKey.DownArrow))))
        {
            nudgeKey=null;
            if(Editor.Session.Revision!=nudgeRevision)
            {ImGui.Internal.ClearActiveID();field.SetValue(shown.AsSpan());status="The document changed; nudge the current value again.";return;}
            finishedNudge=true;
        }
        if (ImGui.IsItemActive()) editing = (key, field, shown, commit);
        if (Text(field) == shown)
        {
            fieldMessages.Remove((SelectionKey(),row.Id));
            keptTyped.Remove(key); // correcting a refused draft back to the committed value is not a pending edit.
            keptInList.Remove(key);
            return;
        }
        if(nudgeKey==key)return;
        if (!(entered || finishedNudge || ImGui.IsItemDeactivatedAfterEdit())) return;
        var (ok, why) = commit(Text(field));
        if (ok) { keptTyped.Remove(key); keptInList.Remove(key); }
        else
        {
            keptTyped.Add(key);
            if (Inspector is { } inspector && inspector.Rows().All(r => r.Id != row.Id))
            {
                keptInList.Add(key);
                keptShape = RowShape(inspector);
            }
        }
        ReportField(row, ok, why);
    }

    private string? nudgeKey;
    private long nudgeRevision;
    private readonly HashSet<string> keptTyped = new(StringComparer.Ordinal);
    private (string Key, ImInputString Field, string Shown, Func<string, (bool Ok, string? Why)> Commit)? editing;
    private bool wasEditing;
    private string lastSelection = "";
    private int fieldEpoch;

    // Refused text in a nested row is keyed by position, and a list's items move when one is added, removed or undone, so
    // it would land on a different item (found by A079). When the rows' shape changes, it's dropped, not carried over.
    private readonly HashSet<string> keptInList = new(StringComparer.Ordinal);
    private string keptShape = "";

    private static string RowShape(Inspector<TDoc> inspector)
    {
        var shape = new System.Text.StringBuilder();
        void Walk(IEnumerable<InspectorRow> rows)
        {
            foreach (var row in rows)
            {
                shape.Append(row.Id).Append('=').Append(row.Label).Append('|');
                Walk(inspector.Nested(row));
            }
        }
        Walk(inspector.Rows());
        return shape.ToString();
    }

    private void DropMovedDrafts(Inspector<TDoc> inspector)
    {
        if (keptInList.Count == 0 || RowShape(inspector) == keptShape) return;
        foreach (var key in keptInList)
        {
            keptTyped.Remove(key);
            textFields.Remove(key);
        }
        keptInList.Clear();
    }

    // Every field message names the field: what, which, and the remedy (U18; found by A031).
    private readonly Dictionary<(string Selection,string Row),string> fieldMessages=new();
    private void ReportField(InspectorRow row,bool ok,string? why)
    {
        var key=(SelectionKey(),row.Id);var message=$"{row.Label}: {why??Explain(Inspector?.LastIssues??[])}";
        if(ok)fieldMessages.Remove(key);else fieldMessages[key]=message;
        Say(ok,$"{row.Label} set.",message);
    }

    private void Report(bool ok, string done) =>
        Say(ok, done, Explain(Inspector?.LastIssues ?? []));

}
