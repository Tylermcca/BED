using System.Text.Json;
using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.ImGui;

/// <summary>
/// The block editor for a game whose rules are snap blocks (<see cref="IRuleBlocks"/>, from the game's adapter): the palette by
/// group with colours on the left, the stack in the middle as blocks with slots in their sentences, and the game read back in plain
/// words with Check's findings on the right. Each kept change becomes one whole-file edit of the open document, so Undo, Redo, Save
/// and the outside-edit checks are the editor's own. INVENTED (BED-BLOCKS01); the snap-block look is BORROWED from block languages
/// such as Scratch.
/// </summary>
public sealed class RuleBlocksPanel : IDisposable
{
    /// <summary>The panel's id.</summary>
    public const string Id = "rule-blocks";

    private readonly IRuleBlocks blocks;
    private readonly DataFile file;
    private readonly EditorModel<DataDocument> editor;
    private readonly DataWorkspace? workspace;
    private readonly DataFormat format = new();
    private IRuleBlockDocument? doc;
    private DataDocument? seen;
    private string? openProblem;
    private int version;
    private int cachedVersion = -1;
    // Whether a block in the stack had the keyboard focus last frame (the stack's keys act only then).
    private bool rowFocused;
    private IReadOnlyList<RuleBlockRow> rows = [];
    private readonly Dictionary<string, IReadOnlyList<RuleBlockSlot>> slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> says = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<RuleBlockPart>> sentences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImInputString> words = new(StringComparer.Ordinal);
    private Task<RuleBlockCheckResult>? checking;
    private CancellationTokenSource? cancelCheck;
    private string? checkingText;
    private string? checkedText;
    private string? groupShown;
    private string? dragRow;
    private string? dragPalette;
    private bool focusSelected;
    private int quietUntil;
    private bool reveal;
    private int revealFrom;

    /// <summary>A block editor for <paramref name="file"/>, editing through <paramref name="editor"/>. Play needs <paramref name="workspace"/>.</summary>
    public RuleBlocksPanel(IRuleBlocks blocks, DataFile file, EditorModel<DataDocument> editor, DataWorkspace? workspace = null)
    {
        this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        this.file = file ?? throw new ArgumentNullException(nameof(file));
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.workspace = workspace;
        Sync();
    }

    /// <summary>The panel to add to the file's editor, with its actions in the command palette.</summary>
    public Panel Panel() => new(Id, "Rule blocks", Draw, OpenAtStart: true, Width: 1100, Height: 720, Side: PanelSide.Centre,
        Commands:
        [
            new PanelCommand("check", "Check the rule blocks", StartCheck, () => doc is null ? Unreadable : Checking ? "Check is already running." : null),
            new PanelCommand("play", "Play this game", Play, () => workspace?.PlaySettings is null ? "Play isn't available in this window." : null),
            new PanelCommand("up", "Move the chosen block up", () => MoveSelected(-1), NeedsBlock),
            new PanelCommand("down", "Move the chosen block down", () => MoveSelected(1), NeedsBlock),
            new PanelCommand("delete", "Take the chosen block out", DeleteSelected, NeedsBlock),
            new PanelCommand("mine", "Make the chosen big block mine", MakeSelectedMine,
                () => SelectedRow is { Big: BigBlockState.Linked } ? null : "Choose a big block that's still the standard one first."),
            new PanelCommand("standard", "Go back to the standard big block", GoBackSelected,
                () => SelectedRow is { Big: BigBlockState.Own, CanGoBack: true } ? null : "Choose a big block you made your own first."),
        ]);

    private string Unreadable => "This file can't be shown as blocks: " + (openProblem ?? "it doesn't read.");
    private string? NeedsBlock() => SelectedRow is { Kind: RuleBlockKind.Block or RuleBlockKind.Hat } ? null : "Choose a block in the stack first.";

    // ------------------------------------------------------------------ state

    /// <summary>The block file's model, or null when the file can't be read as blocks (<see cref="Said"/> says why).</summary>
    public IRuleBlockDocument? Document { get { Sync(); return doc; } }

    /// <summary>The stack's rows as they stand.</summary>
    public IReadOnlyList<RuleBlockRow> Rows { get { Sync(); Cache(); return rows; } }

    /// <summary>The chosen row's id, or null.</summary>
    public string? Selected { get; private set; }

    /// <summary>The chosen row, or null.</summary>
    public RuleBlockRow? SelectedRow => Selected is null ? null : Rows.FirstOrDefault(r => r.Id == Selected);

    /// <summary>What the last action said, in plain words.</summary>
    public string Said { get => notice.Text; private set => notice.Say(value); }
    private readonly StatusNotice notice = new();

    /// <summary>Check's last result, or null before Check has finished.</summary>
    public RuleBlockCheckResult? Checked { get; private set; }

    /// <summary>Whether Check's result is for the game as it stands now.</summary>
    public bool CheckedIsCurrent => Checked is not null && doc is not null && checkedText == doc.Text;

    /// <summary>Whether Check is running.</summary>
    public bool Checking => checking is { IsCompleted: false };

    /// <summary>The palette group shown, or null for every group.</summary>
    public string? GroupShown { get => groupShown; set => groupShown = value; }

    // The document changed elsewhere (undo, redo, another panel, a reload from disk): read it again.
    private void Sync()
    {
        if (file.Session is not { } session || ReferenceEquals(session, editor.Session) is false) { doc = null; openProblem = "the file isn't open."; return; }
        if (ReferenceEquals(seen, session.Current) && (doc is not null || openProblem is not null)) return;
        seen = session.Current;
        var text = format.Write(session.Current);
        try
        {
            if (doc is null) doc = blocks.Open(text);
            else doc.Reload(text);
            openProblem = null;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException or ArgumentException or FormatException)
        { doc = null; openProblem = e.Message; Said = Unreadable; }
        version++;
    }

    private void Cache()
    {
        if (cachedVersion == version) return;
        cachedVersion = version;
        slots.Clear(); says.Clear(); sentences.Clear(); words.Clear();
        rows = doc?.Rows() ?? [];
        if (Selected is not null && rows.All(r => r.Id != Selected)) Selected = null;
    }

    private IReadOnlyList<RuleBlockSlot> SlotsOf(RuleBlockRow row)
    {
        if (!slots.TryGetValue(row.Id, out var found)) slots[row.Id] = found = doc?.Slots(row) ?? [];
        return found;
    }

    private string SaysOf(RuleBlockRow row)
    {
        if (!says.TryGetValue(row.Id, out var found)) says[row.Id] = found = doc?.Says(row) ?? "";
        return found;
    }

    private IReadOnlyList<RuleBlockPart> SentenceOf(RuleBlockRow row)
    {
        if (!sentences.TryGetValue(row.Id, out var found)) sentences[row.Id] = found = doc?.Sentence(row) ?? [];
        return found;
    }

    // What a row is and says, independent of where it is: ids are places in the file, which shift as blocks move.
    private string Signature(RuleBlockRow row) =>
        $"{row.Kind}|{row.Title}|{row.Depth}|{SaysOf(row)}|{string.Join("|", SlotsOf(row).Select(x => x.Shown))}";

    private RuleBlockRow? Row(string id) => Rows.FirstOrDefault(r => r.Id == id);

    /// <summary>Chooses a row by its id.</summary>
    public bool Select(string? id)
    {
        if (id is not null && Row(id) is null) return false;
        Selected = id; focusSelected = reveal = id is not null; quietUntil = int.MaxValue; revealFrom = Im.GetCurrentContext().IsNull() ? 0 : Im.GetFrameCount() + 1; return true;
    }

    // A kept change becomes one edit of the open file; a refused one says why and leaves the file alone.
    private bool Kept(bool ok, string? select = null)
    {
        if (doc is null) { Said = Unreadable; return false; }
        if (!ok) { Said = doc.Problem ?? "That can't be done here."; return false; }
        var text = doc.Text;
        JsonElement root;
        try { using var parsed = JsonDocument.Parse(text); root = parsed.RootElement.Clone(); }
        catch (JsonException e) { Said = "The game gave back a file that doesn't read: " + e.Message; Reread(); return false; }
        if (!editor.Execute(new SetValue("", root)))
        {
            var why = editor.Refused.Concat(editor.Session.LastIssues).FirstOrDefault()?.Message ?? "the editor refused it.";
            Said = "That change wasn't kept: " + why;
            Reread();
            return false;
        }
        notice.Done(doc.Done);
        seen = editor.Session.Current;
        version++;
        if (select is not null) Select(select);
        return true;
    }

    private void Reread() { seen = null; Sync(); }

    // ------------------------------------------------------------------ actions

    /// <summary>Adds a palette block after the chosen row (or where the game puts new blocks).</summary>
    public bool Add(string paletteKey, string? atRow = null)
    {
        Sync(); if (doc is null) { Said = Unreadable; return false; }
        var at = atRow is null ? SelectedRow : Row(atRow);
        var before = Rows.Select(Signature).ToList();
        if (!Kept(doc.Add(paletteKey, at))) return false;
        // Choose what was added: the first row, by what it is and says, that wasn't there before (ids are places, which shift).
        var after = Rows;
        RuleBlockRow? added = null;
        for (var i = 0; i < after.Count && added is null; i++)
            if (i >= before.Count || before[i] != Signature(after[i])) added = after[i];
        if (added is not null) Select(added.Id);
        return true;
    }

    /// <summary>Sets a slot of a row.</summary>
    public bool Set(string row, string key, string value)
    {
        Sync(); if (doc is null || Row(row) is not { } r) { Said = doc is null ? Unreadable : "That block isn't there any more."; return false; }
        return Kept(doc.Set(r, key, value));
    }

    /// <summary>One more or one less on a number slot.</summary>
    public bool Step(string row, string key, int by)
    {
        Sync(); if (doc is null || Row(row) is not { } r || SlotsOf(r).FirstOrDefault(s => s.Key == key) is not { } slot) { Said = "That slot isn't there any more."; return false; }
        return Kept(doc.Step(r, slot, by));
    }

    /// <summary>Adds an item to the end of a list slot.</summary>
    public bool AddItem(string row, string key, string value)
    {
        Sync(); if (doc is null || Row(row) is not { } r) { Said = doc is null ? Unreadable : "That block isn't there any more."; return false; }
        return Kept(doc.AddItem(r, key, value), r.Id);
    }

    /// <summary>Takes an item out of a list slot.</summary>
    public bool RemoveItem(string row, string key, int index)
    {
        Sync(); if (doc is null || Row(row) is not { } r) { Said = doc is null ? Unreadable : "That block isn't there any more."; return false; }
        return Kept(doc.RemoveItem(r, key, index), r.Id);
    }

    /// <summary>Moves an item of a list slot up (-1) or down (+1).</summary>
    public bool MoveItem(string row, string key, int index, int by)
    {
        Sync(); if (doc is null || Row(row) is not { } r) { Said = doc is null ? Unreadable : "That block isn't there any more."; return false; }
        return Kept(doc.MoveItem(r, key, index, by), r.Id);
    }

    /// <summary>Moves the chosen block up (-1) or down (+1), keeping it chosen.</summary>
    public bool MoveSelected(int by)
    {
        if (doc is null || SelectedRow is not { } r) { Said = "Choose a block in the stack first."; return false; }
        var index = Rows.ToList().FindIndex(x => x.Id == r.Id);
        var sign = Signature(r);
        if (!Kept(doc.Move(r, by))) return false;
        // Follow it: the nearest row in the moving direction that is what it was and says what it said.
        var list = Rows.ToList();
        var follow = by < 0 ? list.Take(index).LastOrDefault(x => Signature(x) == sign) : list.Skip(index + 1).FirstOrDefault(x => Signature(x) == sign);
        if (follow is not null) Select(follow.Id);
        return true;
    }

    /// <summary>Moves a block to sit after <paramref name="target"/> (drag and drop).</summary>
    public bool MoveTo(string row, string target)
    {
        Sync(); if (doc is null || Row(row) is not { } r || Row(target) is not { } t) { Said = "That block isn't there any more."; return false; }
        return Kept(doc.MoveTo(r, t));
    }

    /// <summary>Takes the chosen block out.</summary>
    public bool DeleteSelected()
    {
        if (doc is null || SelectedRow is not { } r) { Said = "Choose a block in the stack first."; return false; }
        var index = Rows.ToList().FindIndex(x => x.Id == r.Id);
        if (!Kept(doc.Delete(r))) return false;
        var list = Rows;
        if (list.Count > 0) Select(list[Math.Clamp(index, 0, list.Count - 1)].Id);
        return true;
    }

    /// <summary>Lists a big block's blocks under it, or hides them.</summary>
    public bool ToggleOpen(string row)
    {
        if (doc is null || Row(row) is not { Big: not BigBlockState.None } r) { Said = "Choose a big block first."; return false; }
        if (!doc.SetOpen(r, !r.Open)) { Said = doc.Problem ?? "It can't be opened."; return false; }
        version++; notice.Done(r.Open ? "Closed the big block." : "Opened the big block: its blocks are listed under it.");
        return true;
    }

    /// <summary>"Make it mine" on the chosen big block.</summary>
    public bool MakeSelectedMine() => doc is not null && SelectedRow is { } r ? Kept(doc.MakeItMine(r), r.Id) : NoBig();

    /// <summary>"Go back to the standard one" on the chosen big block.</summary>
    public bool GoBackSelected() => doc is not null && SelectedRow is { } r ? Kept(doc.GoBack(r), r.Id) : NoBig();

    private bool NoBig() { Said = "Choose a big block first."; return false; }

    /// <summary>Undoes the last change to the file.</summary>
    public bool Undo() { var ok = editor.Session.Undo(); notice.Say(ok, ok ? "Undone." : "There's nothing to undo."); Sync(); return ok; }

    /// <summary>Redoes the last undone change.</summary>
    public bool Redo() { var ok = editor.Session.Redo(); notice.Say(ok, ok ? "Redone." : "There's nothing to redo."); Sync(); return ok; }

    /// <summary>Starts Check on the game as it stands, off the window's thread.</summary>
    public bool StartCheck()
    {
        Sync();
        if (doc is null) { Said = Unreadable; return false; }
        if (Checking) { Said = "Check is already running."; return false; }
        cancelCheck?.Dispose();
        cancelCheck = new CancellationTokenSource();
        var text = checkingText = doc.Text; var token = cancelCheck.Token;
        checking = Task.Run(() => blocks.Check(text, token), token);
        Said = "Checking the game: can it be played as it stands?";
        return true;
    }

    /// <summary>Takes Check's result once it has finished; true when one was taken.</summary>
    public bool FinishCheck()
    {
        if (checking is not { IsCompleted: true } done) return false;
        checking = null;
        if (done.IsCanceled) { Said = "Check was stopped."; return true; }
        if (done.IsFaulted) { _ = done.Exception; Said = "Check couldn't finish: " + (done.Exception?.InnerException?.Message ?? "it stopped."); return true; }
        Checked = done.Result; checkedText = checkingText;
        Said = Checked.Words;
        if (Checked.Stamped is { } stamped) KeepStamp(stamped);
        return true;
    }

    // Check's record written into the file, as one edit (Undo takes it back): only onto the text that was checked, never over a change
    // made while Check ran, and the result stays current for the stamped text.
    private void KeepStamp(string stamped)
    {
        Sync();
        if (doc is null || doc.Text != checkingText || stamped == doc.Text) return;
        JsonElement root;
        try { using var parsed = JsonDocument.Parse(stamped); root = parsed.RootElement.Clone(); }
        catch (JsonException) { return; }
        if (!editor.Execute(new SetValue("", root))) return;
        Reread();
        version++;
        if (doc is not null) checkedText = doc.Text;
        Said += " Check's result is kept in the file.";
    }

    /// <summary>Waits for a running Check (for tools and tests) and takes its result.</summary>
    public bool WaitForCheck(TimeSpan limit)
    {
        if (checking is null) return false;
        try { checking.Wait(limit); } catch (AggregateException) { }
        return FinishCheck();
    }

    /// <summary>Plays this game with the project's Play: the game's adapter sets Play's arguments for this file, then Play runs the saved files.</summary>
    public bool Play()
    {
        Sync();
        if (doc is null) { Said = Unreadable; return false; }
        if (workspace?.PlaySettings is not { } play) { Said = "Play isn't available in this window."; return false; }
        play.Reload();
        if (play.Settings is not { } current) { Said = play.Status.Length > 0 ? play.Status : "Set the program in Tools > Play settings."; return false; }
        if (blocks.PlayArguments(current.Arguments, file.Name, doc.Text, out var why) is not { } arguments) { Said = why; return false; }
        play.SetDraft(new PlaySettings(current.Program, arguments, current.WorkingFolder, current.Build));
        if (!play.Save()) { Said = play.Status; return false; }
        var started = workspace.RequestProjectPlay();
        Said = workspace.Said;
        return started;
    }

    /// <summary>Stops a running Check.</summary>
    public void Dispose() { cancelCheck?.Cancel(); cancelCheck?.Dispose(); cancelCheck = null; }

    // ------------------------------------------------------------------ drawing

    private static ImColor8 Colour(uint rgb, float shade = 1f) =>
        new((byte)Math.Clamp(((rgb >> 16) & 0xFF) * shade, 0, 255), (byte)Math.Clamp(((rgb >> 8) & 0xFF) * shade, 0, 255), (byte)Math.Clamp((rgb & 0xFF) * shade, 0, 255), 255);

    // Dark words on a light block, light words on a dark one.
    private static ImColor8 Ink(uint rgb, float shade = 1f) =>
        (0.299f * ((rgb >> 16) & 0xFF) + 0.587f * ((rgb >> 8) & 0xFF) + 0.114f * (rgb & 0xFF)) * shade > 150 ? new ImColor8(28, 31, 62, 255) : new ImColor8(255, 255, 255, 255);

    private uint GroupColour(string group) => blocks.Groups.FirstOrDefault(g => g.Key == group)?.Colour ?? 0x6E7681;

    private string GroupName(string group) => blocks.Groups.FirstOrDefault(g => g.Key == group)?.Name ?? group;

    // The three panes take part in the window's Tab order (Tab goes from the toolbar through the palette, the stack and the reader).
    private const ImGuiChildFlags Panes = ImGuiChildFlags.Borders | ImGuiChildFlags.NavFlattened;

    /// <summary>Draws the panel.</summary>
    public void Draw()
    {
        Sync(); Cache(); FinishCheck();
        DrawToolbar();
        if (doc is null) { Ui.TextWrapped(Unreadable, semanticId: "rule-blocks.unreadable"); return; }
        var avail = Im.GetContentRegionAvail();
        var height = MathF.Max(120, avail.Y);
        var left = MathF.Max(180, avail.X * 0.22f); var right = MathF.Max(200, avail.X * 0.28f);
        var middle = MathF.Max(240, avail.X - left - right - 16);
        if (Ui.BeginChild("Blocks to add##rule-blocks.palette"u8, new float2(left, height), Panes)) DrawPalette();
        Ui.EndChild();
        Im.SameLine();
        if (Ui.BeginChild("The game's blocks##rule-blocks.stack"u8, new float2(middle, height), Panes)) DrawStack();
        Ui.EndChild();
        Im.SameLine();
        if (Ui.BeginChild("What the game does##rule-blocks.reader"u8, new float2(0, height), Panes)) DrawReader();
        Ui.EndChild();
    }

    private void DrawToolbar()
    {
        Ui.Text(doc is null ? file.Name : doc.Name, semanticId: "rule-blocks.name");
        Im.SameLine();
        Ui.BeginDisabled(!editor.Session.History.CanUndo, "There's nothing to undo.");
        if (Ui.Button("Undo##rule-blocks"u8)) Undo();
        Ui.EndDisabled(); Im.SameLine();
        Ui.BeginDisabled(!editor.Session.History.CanRedo, "There's nothing to redo.");
        if (Ui.Button("Redo##rule-blocks"u8)) Redo();
        Ui.EndDisabled(); Im.SameLine();
        Ui.BeginDisabled(doc is null || Checking, Checking ? "Check is already running." : Unreadable);
        if (Ui.Button("Check##rule-blocks"u8)) StartCheck();
        Ui.EndDisabled(); Im.SameLine();
        Ui.BeginDisabled(doc is null || workspace?.PlaySettings is null, "Play isn't available in this window.");
        if (Ui.Button("Play##rule-blocks"u8)) Play();
        Ui.EndDisabled();
        var chosen = SelectedRow;
        var isBlock = chosen is { Kind: RuleBlockKind.Block or RuleBlockKind.Hat };
        SameLineIfFits(Im.CalcTextSize("Chosen block:"u8).X + 16); Ui.Text("Chosen block:"u8);
        Im.SameLine();
        Ui.BeginDisabled(!isBlock, "Choose a block in the stack first.");
        if (Ui.Button("Move up##rule-blocks"u8)) MoveSelected(-1);
        SameLineIfFits(Im.CalcTextSize("Move down"u8).X + 16);
        if (Ui.Button("Move down##rule-blocks"u8)) MoveSelected(1);
        SameLineIfFits(Im.CalcTextSize("Take out"u8).X + 16);
        if (Ui.Button("Take out##rule-blocks"u8)) DeleteSelected();
        Ui.EndDisabled();
        if (chosen is { Big: not BigBlockState.None and not BigBlockState.Sealed } big)
        {
            Im.SameLine();
            var open = big.Open ? "Close##rule-blocks.big" : "Open##rule-blocks.big";
            Ui.NameNextItem(open, (big.Open ? "Close " : "Open ") + big.Title);
            if (Ui.Button(open)) ToggleOpen(big.Id);
            if (big.Big == BigBlockState.Linked) { Im.SameLine(); if (Ui.Button("Make it mine##rule-blocks"u8)) MakeSelectedMine(); }
            if (big.Big == BigBlockState.Own)
            {
                Im.SameLine();
                Ui.BeginDisabled(!big.CanGoBack, "The standard one isn't available to go back to.");
                if (Ui.Button("Go back to the standard one##rule-blocks"u8)) GoBackSelected();
                Ui.EndDisabled();
            }
        }
        Ui.Status(Checking ? "Checking..." : notice.Showing ? Said : "Choose a block, then add, move or change it. Ctrl+Z undoes.", semanticId: "rule-blocks.said");
    }

    private void DrawPalette()
    {
        Im.SetNextItemWidth(-1);
        Ui.NameNextItem("##rule-blocks.group"u8, "Group");
        if (Ui.BeginCombo("##rule-blocks.group"u8, groupShown is null ? "Every group" : GroupName(groupShown)))
        {
            if (Ui.Selectable("Every group##rule-blocks.group.all"u8, groupShown is null)) groupShown = null;
            foreach (var g in blocks.Groups)
                if (Ui.Selectable($"{g.Name}##rule-blocks.group.{g.Key}", groupShown == g.Key)) groupShown = g.Key;
            Ui.EndCombo();
        }
        Ui.TextWrapped("Click a block to add it after the chosen one, or drag it onto the stack."u8);
        var draw = Im.GetWindowDrawList();
        foreach (var g in blocks.Groups)
        {
            if (groupShown is not null && g.Key != groupShown) continue;
            var items = blocks.Palette.Where(p => p.Group == g.Key).ToList();
            if (items.Count == 0) continue;
            var at = Im.GetCursorScreenPos();
            var h = Im.GetTextLineHeight();
            draw.AddRectFilled(at + new float2(0, 2), at + new float2(h - 4, h - 2), Colour(g.Colour), 3, 0);
            Im.Indent(h);
            Ui.Text(g.Name, semanticId: "rule-blocks.palette.group." + g.Key);
            Im.Unindent(h);
            Im.PushStyleColor(ImGuiCol.Button, Colour(g.Colour, 0.85f));
            Im.PushStyleColor(ImGuiCol.ButtonHovered, Colour(g.Colour, 1.0f));
            Im.PushStyleColor(ImGuiCol.ButtonActive, Colour(g.Colour, 0.7f));
            Im.PushStyleColor(ImGuiCol.Text, Ink(g.Colour, 0.85f));
            Im.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new float2(0, 0.5f));
            var inner = Im.GetContentRegionAvail().X - Im.GetStyle().FramePadding.X * 2;
            foreach (var item in items)
            {
                // A long name wraps onto more lines rather than being cut.
                var label = $"{WrapWords(item.Title, inner)}###rule-blocks.palette.{item.Key}";
                Ui.NameNextItem(label, item.Title);
                if (Ui.Button(label, new float2(-1, 0))) Add(item.Key);
                DragSource(null, item.Key, item.Title);
            }
            Im.PopStyleVar(1);
            Im.PopStyleColor(4);
            Im.Spacing();
        }
    }

    // Words broken onto lines no wider than width (a single word longer than that keeps its own line).
    private static string WrapWords(string text, float width)
    {
        if (width <= 0 || Im.CalcTextSize(text).X <= width) return text;
        var wrapped = new System.Text.StringBuilder(); var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Im.CalcTextSize(longer).X > width) { wrapped.Append(line).Append('\n'); line = word; }
            else line = longer;
        }
        return wrapped.Append(line).ToString();
    }

    private unsafe void DragSource(string? row, string? palette, string title)
    {
        if (!Im.BeginDragDropSource(0)) return;
        dragRow = row; dragPalette = palette;
        var token = 1;
        Im.SetDragDropPayload("BED_RULE_BLOCK"u8, &token, (nuint)sizeof(int), 0);
        Ui.Text(title);
        Im.EndDragDropSource();
    }

    private void DropTarget(RuleBlockRow target)
    {
        if (!Im.BeginDragDropTarget()) return;
        var payload = Im.AcceptDragDropPayload("BED_RULE_BLOCK"u8, 0);
        if (!payload.IsNull() && payload.Delivery)
        {
            if (dragRow is { } moving) { if (moving != target.Id) MoveTo(moving, target.Id); }
            else if (dragPalette is { } key) Add(key, target.Id);
            dragRow = null; dragPalette = null;
        }
        Im.EndDragDropTarget();
    }

    private void DrawStack()
    {
        var io = Im.GetIO();
        // The keys act on the chosen block while a block in the stack has the keyboard focus (not from the palette or the reader, which
        // share the window's Tab order).
        var keys = rowFocused; rowFocused = false;
        if (keys && !io.WantTextInput && SelectedRow is { } chosen)
        {
            if (io.KeyAlt && Im.IsKeyPressed(ImGuiKey.UpArrow, false)) MoveSelected(-1);
            else if (io.KeyAlt && Im.IsKeyPressed(ImGuiKey.DownArrow, false)) MoveSelected(1);
            else if (!io.KeyCtrl && !io.KeyAlt && Im.IsKeyPressed(ImGuiKey.Delete, false) && chosen.Kind is RuleBlockKind.Block or RuleBlockKind.Hat) DeleteSelected();
        }
        var findings = CheckedIsCurrent ? Checked!.Findings : [];
        var draw = Im.GetWindowDrawList();
        var step = Im.GetFontSize() * 1.2f;
        var right = Im.GetCursorScreenPos().X + Im.GetContentRegionAvail().X;
        var list = Rows;
        for (var i = 0; i < list.Count; i++)
        {
            var row = list[i];
            Im.PushID(row.Id);
            var x0 = Im.GetCursorScreenPos().X + row.Depth * step;
            var y0 = Im.GetCursorScreenPos().Y;
            var colour = GroupColour(row.Group);
            draw.ChannelsSplit(2);
            draw.ChannelsSetCurrent(1);
            Im.Indent(row.Depth * step + 8);
            Im.BeginGroup();
            DrawRowHead(row);
            Im.EndGroup();
            Im.Unindent(row.Depth * step + 8);
            var y1 = Im.GetItemRectMax().Y + 3;
            // A block the panel chose itself (added, moved, picked by a tool) is scrolled into view, once the stack's size has settled.
            if (reveal && Selected == row.Id)
            {
                if (Im.IsRectVisible(new float2(x0, y0), new float2(right, y1)) && Im.GetFrameCount() > revealFrom) reveal = false;
                else Im.SetScrollHereY(0.25f);
            }
            draw.ChannelsSetCurrent(0);
            DrawShape(draw, row, colour, x0, y0 - 2, right, y1, Selected == row.Id);
            draw.ChannelsMerge();
            foreach (var (_, words) in findings.Where(f => f.RowId == row.Id))
            {
                Im.Indent(row.Depth * step + 8);
                Im.PushStyleColor(ImGuiCol.Text, new ImColor8(255, 130, 110, 255));
                Ui.TextWrapped("Check: " + words, semanticId: "rule-blocks.finding." + row.Id);
                Im.PopStyleColor(1);
                Im.Unindent(row.Depth * step + 8);
            }
            Im.PopID();
            Im.Dummy(new float2(1, 2));
        }
        if (list.Count == 0) Ui.TextWrapped("There are no blocks yet. Add one from the left."u8);
        if (focusSelected) quietUntil = Im.GetFrameCount() + 2;
        focusSelected = false;
    }

    private static void DrawShape(ImDrawListPtr draw, RuleBlockRow row, uint colour, float x0, float y0, float right, float y1, bool chosen)
    {
        var fill = row.Kind switch
        {
            RuleBlockKind.Heading => new ImColor8(70, 74, 82, 255),
            RuleBlockKind.Otherwise => Colour(colour, 0.7f),
            RuleBlockKind.Part => Colour(colour, 0.55f),
            _ => row.Big == BigBlockState.Sealed ? Colour(colour, 0.6f) : Colour(colour, 0.9f),
        };
        var edge = Colour(colour, 0.55f);
        var hat = row.Shape == RuleBlockShape.Hat || row.Kind == RuleBlockKind.Hat;
        draw.AddRectFilled(new float2(x0, y0), new float2(right, y1), fill, hat ? 10 : 4, hat ? ImDrawFlags.RoundCornersTop : ImDrawFlags.RoundCornersAll);
        if (row.Kind is RuleBlockKind.Block or RuleBlockKind.Hat)
        {
            // The notch on top (where the block above snaps in) and the tab underneath.
            if (!hat) draw.AddRectFilled(new float2(x0 + 14, y0), new float2(x0 + 30, y0 + 3), Colour(colour, 0.6f), 1, 0);
            draw.AddRectFilled(new float2(x0 + 14, y1 - 1), new float2(x0 + 30, y1 + 3), fill, 2, ImDrawFlags.RoundCornersBottom);
        }
        if (row.Shape == RuleBlockShape.Wrap)
            draw.AddRectFilled(new float2(x0, y1 - 1), new float2(x0 + 10, y1 + 4), fill, 0, 0);   // the wrap's arm runs on down
        draw.AddRect(new float2(x0, y0), new float2(right, y1), edge, hat ? 10 : 4, hat ? ImDrawFlags.RoundCornersTop : ImDrawFlags.RoundCornersAll, 1);
        if (chosen) draw.AddRect(new float2(x0 - 1, y0 - 1), new float2(right + 1, y1 + 1), new ImColor8(0, 120, 215, 255), hat ? 10 : 4, hat ? ImDrawFlags.RoundCornersTop : ImDrawFlags.RoundCornersAll, 2.5f);
    }

    private void DrawRowHead(RuleBlockRow row)
    {
        var chosen = Selected == row.Id;
        Im.PushStyleColor(ImGuiCol.Text, Ink(GroupColour(row.Group), row.Kind switch
        {
            RuleBlockKind.Heading => 0f, RuleBlockKind.Part => 0.55f, RuleBlockKind.Otherwise => 0.7f, _ => row.Big == BigBlockState.Sealed ? 0.6f : 0.9f,
        }));
        // The chosen block gets a blue outline (DrawShape); the row's own highlight stays faint.
        Im.PushStyleColor(ImGuiCol.Header, new ImColor8(255, 255, 255, 0));
        Im.PushStyleColor(ImGuiCol.HeaderHovered, new ImColor8(255, 255, 255, 40));
        Im.PushStyleColor(ImGuiCol.HeaderActive, new ImColor8(255, 255, 255, 80));
        var suffix = row.Big switch
        {
            BigBlockState.Linked => " (standard)", BigBlockState.Own => " (your version)", BigBlockState.Sealed => " (sealed)", _ => "",
        };
        // The sentence with its slots woven in ("Deal [6] cards face [down] to each hand"); slots it leaves out follow with their labels.
        var all = SlotsOf(row);
        var parts = SentenceOf(row).Where(p => p.Slot is null ? p.Words.Length > 0 : all.Any(x => x.Key == p.Slot)).ToList();
        foreach (var missing in all.Where(x => parts.All(p => p.Slot != x.Key)))
        { parts.Add(new((parts.Count == 0 ? "" : ", ") + missing.Label.ToLowerInvariant() + " ")); parts.Add(new("", missing.Key)); }
        if (parts.Count == 0 || parts[0].Slot is not null) parts.Insert(0, new(row.Title + " "));
        if (suffix.Length > 0) parts.Add(new(suffix));
        // Choosing: the first word is the row's handle (named by its title); clicking any of its words chooses it too.
        var first = parts[0].Words; var cut = first.IndexOf(' ', 1);
        var handle = cut > 0 ? first[..cut] : first.TrimEnd();
        parts[0] = new(cut > 0 ? first[cut..] : "");
        Im.AlignTextToFramePadding();
        if (focusSelected && chosen) Im.SetKeyboardFocusHere(0);
        Ui.NameNextItem("###row"u8, row.Title);
        if (Ui.Selectable($"{handle}###row", chosen, 0, new float2(Im.CalcTextSize(handle).X, 0))) Select(row.Id);
        // Choosing follows keyboard focus (Tab, arrows), except just after the panel chose a row itself and moved focus there.
        else if (Im.IsItemFocused() && !chosen && !focusSelected && Im.GetFrameCount() > quietUntil) Selected = row.Id;
        if (Im.IsItemFocused()) rowFocused = true;
        // What the block does, in plain words, for a screen reader (and on hover).
        if (SaysOf(row) is { Length: > 0 } says) Ui.TooltipLast(says);
        if (row.Kind is RuleBlockKind.Block or RuleBlockKind.Hat) DragSource(row.Id, null, row.Title);
        DropTarget(row);
        foreach (var part in parts)
        {
            if (part.Slot is { } key) { DrawSlot(row, all.First(x => x.Key == key)); continue; }
            foreach (var token in System.Text.RegularExpressions.Regex.Split(part.Words, "(?<= )"))
            {
                if (token.Length == 0) continue;
                if (!SameLineIfFits(Im.CalcTextSize(token.TrimEnd()).X, 0) && token.Trim().Length == 0) continue;
                Im.Text(token);
                if (Im.IsItemClicked(0)) Select(row.Id);
            }
        }
        if (row.Big is BigBlockState.Linked or BigBlockState.Own)
        {
            SameLineIfFits(Im.CalcTextSize("Close"u8).X + 16, 8);
            var open = row.Open ? "Close##big" : "Open##big";
            Ui.NameNextItem(open, (row.Open ? "Close " : "Open ") + row.Title);
            if (Ui.SmallButton(open)) ToggleOpen(row.Id);
        }
        Im.PopStyleColor(4);
    }

    // On the same line when width fits there, else on a new line (text aligned to the slots' frames); true when it stayed on the line.
    private static bool SameLineIfFits(float width, float spacing = -1)
    {
        Im.SameLine(0, spacing);
        if (Im.GetContentRegionAvail().X >= width) return true;
        Im.NewLine(); Im.AlignTextToFramePadding();
        return false;
    }

    private static readonly ImColor8 SlotFill = new(255, 255, 255, 235);
    private static readonly ImColor8 SlotInk = new(20, 24, 32, 255);

    private void DrawSlot(RuleBlockRow row, RuleBlockSlot slot)
    {
        var label = slot.Label.ToLowerInvariant();
        var shown = slot.Shown.Length > 0 ? slot.Shown : "none";
        var style = Im.GetStyle();
        var room = Im.GetContentRegionAvail().X;   // the whole line: the last item moved the cursor to the next line
        // A list too long for the block's line is counted instead ("13 items"); its drop-down lists them all.
        if (slot.Kind == RuleBlockSlotKind.List && Im.CalcTextSize(shown).X + style.FramePadding.X * 2 + Im.GetFrameHeight() > MathF.Min(room, 260))
            shown = (slot.Items?.Count ?? 0) == 1 ? "1 item" : $"{slot.Items?.Count ?? 0} items";
        var shownWidth = Im.CalcTextSize(shown).X;
        var small = Im.CalcTextSize("+"u8).X + style.FramePadding.X * 2;
        var width = slot.Kind switch
        {
            RuleBlockSlotKind.Number => small * 2 + shownWidth + 8,
            RuleBlockSlotKind.YesNo => Im.GetFrameHeight(),
            RuleBlockSlotKind.Words => MathF.Max(80, shownWidth + 24),
            RuleBlockSlotKind.Shown => shownWidth + style.FramePadding.X * 2,
            _ => shownWidth + style.FramePadding.X * 2 + Im.GetFrameHeight(),
        };
        width = MathF.Min(width, MathF.Max(60, room));
        SameLineIfFits(width, 4);
        Im.PushID(slot.Key);
        Im.PushStyleColor(ImGuiCol.FrameBg, SlotFill);
        Im.PushStyleColor(ImGuiCol.Button, SlotFill);
        Im.PushStyleColor(ImGuiCol.ButtonHovered, new ImColor8(235, 240, 255, 255));
        Im.PushStyleColor(ImGuiCol.ButtonActive, new ImColor8(210, 220, 245, 255));
        Im.PushStyleColor(ImGuiCol.Text, SlotInk);
        Im.PushStyleVar(ImGuiStyleVar.FrameRounding, 9f);
        switch (slot.Kind)
        {
            case RuleBlockSlotKind.Number:
                {
                    // One light rounded slot: less, the number, more.
                    var at = Im.GetCursorScreenPos();
                    var height = MathF.Max(Im.GetFrameHeight(), 24 * style.FontScaleDpi);
                    Im.GetWindowDrawList().AddRectFilled(at, at + new float2(width, height), SlotFill, height / 2, 0);
                    // The buttons say the number too, so it is heard when they are reached by Tab.
                    Ui.NameNextItem("-##less"u8, $"Less {label}, now {shown}");
                    if (Ui.SmallButton("-##less"u8)) Step(row.Id, slot.Key, -1);
                    Im.SameLine(0, 4);
                    Im.AlignTextToFramePadding();
                    Ui.Text(shown, semanticId: $"rule-blocks.slot.{row.Id}.{slot.Key}");
                    Ui.TooltipLast(slot.Label);
                    Im.SameLine(0, 4);
                    Ui.NameNextItem("+##more"u8, $"More {label}, now {shown}");
                    if (Ui.SmallButton("+##more"u8)) Step(row.Id, slot.Key, 1);
                    break;
                }
            case RuleBlockSlotKind.YesNo:
                var on = string.Equals(slot.Shown, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(slot.Shown, "true", StringComparison.OrdinalIgnoreCase);
                Ui.NameNextItem("##yes"u8, slot.Label);
                if (Ui.Checkbox("##yes"u8, ref on)) Set(row.Id, slot.Key, on ? "true" : "false");
                break;
            case RuleBlockSlotKind.Choice:
                Im.SetNextItemWidth(width);
                Ui.NameNextItem("##choice"u8, slot.Label);
                if (Ui.BeginCombo("##choice"u8, shown))
                {
                    Im.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.Text]);
                    foreach (var c in slot.Choices)
                        if (Ui.Selectable($"{c.Label}##{c.Value}", c.Label == slot.Shown)) Set(row.Id, slot.Key, c.Value);
                    Im.PopStyleColor(1);
                    Ui.EndCombo();
                }
                break;
            case RuleBlockSlotKind.Words:
                {
                    var key = row.Id + "\n" + slot.Key;
                    if (!words.TryGetValue(key, out var input)) words[key] = input = new ImInputString(512, slot.Shown);
                    Im.SetNextItemWidth(width);
                    Ui.NameNextItem("##words"u8, slot.Label);
                    var enter = Ui.InputText("##words"u8, input, ImGuiInputTextFlags.EnterReturnsTrue);
                    if (enter || Im.IsItemDeactivatedAfterEdit())
                    {
                        input.EvaluateLength();
                        var typed = input.ToString();
                        if (typed != slot.Shown) Set(row.Id, slot.Key, typed);
                    }
                    break;
                }
            case RuleBlockSlotKind.List:
                Im.SetNextItemWidth(width);
                Ui.NameNextItem("##list"u8, slot.Label);
                if (Ui.BeginCombo("##list"u8, shown))
                {
                    Im.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.Text]);
                    Im.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.Button]);
                    Im.PushStyleColor(ImGuiCol.FrameBg, style.Colors[(int)ImGuiCol.FrameBg]);
                    Im.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
                    DrawListItems(row, slot);
                    Im.PopStyleVar(1);
                    Im.PopStyleColor(3);
                    Ui.EndCombo();
                }
                break;
            default:
                {
                    // Shown only: a paler slot, the same shape.
                    var at = Im.GetCursorScreenPos();
                    Im.GetWindowDrawList().AddRectFilled(at, at + new float2(width, Im.GetFrameHeight()), new ImColor8(255, 255, 255, 150), 9, 0);
                    Im.SetCursorScreenPos(at + new float2(style.FramePadding.X, 0));
                    Im.AlignTextToFramePadding();
                    Ui.Text(shown, semanticId: $"rule-blocks.slot.{row.Id}.{slot.Key}");
                    Ui.TooltipLast(slot.Label + " (it can't be changed here)");
                    Im.SameLine(0, style.FramePadding.X);
                    Im.Dummy(new float2(0, 0));
                    break;
                }
        }
        Im.PopStyleVar(1);
        Im.PopStyleColor(5);
        Im.PopID();
    }

    // A list slot's drop-down: each item with Move up, Move down and Take out, then a way to add one.
    private void DrawListItems(RuleBlockRow row, RuleBlockSlot slot)
    {
        var items = slot.Items ?? [];
        var label = slot.Label.ToLowerInvariant();
        if (items.Count == 0) Ui.Text("The list is empty."u8);
        var column = items.Count == 0 ? 0 : items.Max(i => Im.CalcTextSize(i).X) + Im.GetStyle().ItemSpacing.X * 2;
        for (var i = 0; i < items.Count; i++)
        {
            Im.PushID(i);
            Im.AlignTextToFramePadding();
            Ui.Text(items[i], semanticId: $"rule-blocks.item.{row.Id}.{slot.Key}.{i}");
            Im.SameLine(column + Im.GetStyle().WindowPadding.X);
            Ui.BeginDisabled(i == 0, "It's already first.");
            Ui.NameNextItem("Move up##item"u8, $"Move {items[i]} up");
            if (Ui.SmallButton("Move up##item"u8)) MoveItem(row.Id, slot.Key, i, -1);
            Ui.EndDisabled(); Im.SameLine();
            Ui.BeginDisabled(i == items.Count - 1, "It's already last.");
            Ui.NameNextItem("Move down##item"u8, $"Move {items[i]} down");
            if (Ui.SmallButton("Move down##item"u8)) MoveItem(row.Id, slot.Key, i, 1);
            Ui.EndDisabled(); Im.SameLine();
            Ui.NameNextItem("Take out##item"u8, $"Take {items[i]} out");
            if (Ui.SmallButton("Take out##item"u8)) RemoveItem(row.Id, slot.Key, i);
            Im.PopID();
        }
        Im.Separator();
        if (slot.Choices.Count > 0)
        {
            Im.SetNextItemWidth(220);
            Ui.NameNextItem("##add"u8, "Add to " + label);
            if (Ui.BeginCombo("##add"u8, "Add..."u8))
            {
                foreach (var c in slot.Choices)
                    if (Ui.Selectable($"{c.Label}##add.{c.Value}")) AddItem(row.Id, slot.Key, c.Value);
                Ui.EndCombo();
            }
        }
        else
        {
            var key = row.Id + "\n" + slot.Key + "\nadd";
            if (!words.TryGetValue(key, out var input)) words[key] = input = new ImInputString(256);
            Im.SetNextItemWidth(160);
            Ui.NameNextItem("##add"u8, "New item for " + label);
            var enter = Ui.InputText("##add"u8, input, ImGuiInputTextFlags.EnterReturnsTrue);
            Im.SameLine();
            if (Ui.SmallButton("Add##item"u8) || enter)
            {
                input.EvaluateLength();
                var typed = input.ToString().Trim();
                if (typed.Length > 0 && AddItem(row.Id, slot.Key, typed)) words.Remove(key);
            }
        }
    }

    private void DrawReader()
    {
        Ui.Text("What the game does"u8, semanticId: "rule-blocks.reader.title");
        Im.Separator();
        if (Checked is { } result)
        {
            var current = CheckedIsCurrent;
            Ui.TextWrapped((result.Passed ? "Check passed: " : "Check found problems: ") + result.Words + (current ? "" : " (The game has changed since; check again.)"),
                semanticId: "rule-blocks.check");
            if (current)
                foreach (var f in result.Findings.Where(f => f.RowId is null || Rows.All(r => r.Id != f.RowId)))
                    Ui.TextWrapped("- " + f.Words, semanticId: "rule-blocks.check.game");
            Im.Separator();
        }
        foreach (var row in Rows)
        {
            if (row.Kind == RuleBlockKind.Heading) { Ui.Text(row.Title); continue; }
            if (row.Kind is not (RuleBlockKind.Block or RuleBlockKind.Hat)) continue;
            var text = SaysOf(row);
            if (text.Length == 0) continue;
            Im.Indent(row.Depth * 10 + 1);
            Ui.TextWrapped(text);
            Im.Unindent(row.Depth * 10 + 1);
        }
    }
}
