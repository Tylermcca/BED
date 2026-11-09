using Brutal.Authoring.Data;

namespace Brutal.Authoring.Editor;

/// <summary>
/// What the editor needs to know about a document type beyond the core: which objects it holds, and how its objects nest
/// into places that can be focused on (a world, a region, a room, a group). A game or plug-in supplies one per document
/// type; the editor never knows the game.
/// </summary>
public interface IDocumentOutline<TDoc> where TDoc : class
{
    /// <summary>Every object id in the document, in document order.</summary>
    IEnumerable<string> ObjectIds(TDoc document);

    /// <summary>The object's display label, or null when the id isn't in the document.</summary>
    string? Label(TDoc document, string objectId);

    /// <summary>The object's parent place, or null at the top level.</summary>
    string? Parent(TDoc document, string objectId);

    /// <summary>A number for everything the ids, labels and parents depend on besides the document, so a view may keep them
    /// until the document or this changes (otherwise a large outline is walked and labelled whole every frame). Null,
    /// the default, keeps none: an outline reading anything it can't number stays live.</summary>
    long? Version => null;
}

/// <summary>A flat outline: every object at the top level. Enough for documents with no nesting.</summary>
public sealed class FlatOutline<TDoc>(Func<TDoc, IEnumerable<(string Id, string Label)>> objects) : IDocumentOutline<TDoc> where TDoc : class
{
    /// <inheritdoc />
    public IEnumerable<string> ObjectIds(TDoc document) => objects(document).Select(o => o.Id);

    /// <inheritdoc />
    public string? Label(TDoc document, string objectId) => objects(document).Where(o => o.Id == objectId).Select(o => o.Label).FirstOrDefault();

    /// <inheritdoc />
    public string? Parent(TDoc document, string objectId) => null;
}

/// <summary>
/// A focused editor for one kind of work (worksheet AT-110): a lighting pass, a dialogue editor, a game's ship designer.
/// Workspaces are added by games and plug-ins. They share the one session, undo, save and palette; they only change what
/// is shown first and which commands lead.
/// </summary>
/// <param name="Id">A stable, dotted id (for example <c>toy.layout</c>).</param>
/// <param name="Label">What people see.</param>
/// <param name="Commands">Command ids this workspace puts first in the palette.</param>
public sealed record Workspace(string Id, string Label, IReadOnlyList<string> Commands);

/// <summary>A game running from the editor (goal 14): the game's own code, its own state, its own frame. It's handed a
/// copy of the document when Play starts and is thrown away when Play stops; nothing it does reaches the document. While
/// it plays, the window draws it in its own frame and authoring panels step aside.</summary>
public interface IPlaySession : IDisposable
{
    /// <summary>Draws one frame of the game (with the UI library the window uses) and handles its input.</summary>
    void Draw();

    /// <summary>One line about how the game stands (whose turn, the score, who won), shown beside Stop.</summary>
    string Status { get; }
}

/// <summary>
/// A way to play the document (goal 14), BORROWED from Unity, Godot and Unreal's Play button: author, press Play, the
/// game's own code runs, Stop returns to editing. INVENTED here: play never edits the document. The editor hands
/// <see cref="Start"/> the current document (documents are immutable, so it's a snapshot) and locks editing until Stop.
/// </summary>
/// <param name="Id">A stable, dotted id (for example <c>checkers.match</c>).</param>
/// <param name="Title">What people see on the Play button and the play frame.</param>
/// <param name="Start">Starts a game from the document.</param>
public sealed record PlayMode<TDoc>(string Id, string Title, Func<TDoc, IPlaySession> Start) where TDoc : class;

/// <summary>
/// A panel a game or plug-in adds to the editor window: a tool view beside the outliner and inspector (a lighting
/// overview, a validation report, a game's ship stats). The window opens a frame for it and calls <see cref="Draw"/>
/// inside; the panel draws with the UI library the window uses. Panels read the editor and edit only through its
/// commands. Whether a panel is open is view state.
/// </summary>
/// <param name="Id">A stable, dotted id (for example <c>lights.overview</c>).</param>
/// <param name="Title">What people see on its frame.</param>
/// <param name="Draw">Draws its contents; called once per frame while it's open.</param>
/// <param name="OpenAtStart">Whether it starts open.</param>
/// <param name="Width">Its width when first shown, in pixels; people can resize it.</param>
/// <param name="Height">Its height when first shown, in pixels.</param>
/// <param name="Side">Where the first layout docks it: the centre as a tab (the default), or the left above the outliner,
/// or the right above the inspector. People can move it after.</param>
/// <param name="Commands">What it offers the command palette (the table's "Fill a column"), so its actions can be found by
/// name and run from the keyboard like any other.</param>
/// <param name="ShowWhen">Optional presentation predicate. False hides listing and layout without unregistering commands.</param>
public sealed record Panel(string Id, string Title, Action Draw, bool OpenAtStart = true, float Width = 320, float Height = 240,
    IReadOnlyList<PanelCommand>? Commands = null, PanelSide Side = PanelSide.Centre, Func<bool>? ShowWhen = null);

/// <summary>Where a panel first docks.</summary>
public enum PanelSide
{
    /// <summary>A tab in the centre, with the viewport and the game.</summary>
    Centre,

    /// <summary>The left column, above the outliner.</summary>
    Left,

    /// <summary>The right column, above the inspector.</summary>
    Right,
}

/// <summary>An action a panel offers the command palette. Running it opens the panel first, if it's closed. Like any
/// edit, it's refused while a game plays.</summary>
/// <param name="Id">Its id within the panel (for example <c>fill</c>).</param>
/// <param name="Label">What the palette shows (for example "Fill a column of the shown rows...").</param>
/// <param name="Run">Does it; false when it couldn't, with the panel saying why.</param>
/// <param name="Unavailable">Why it can't run right now, or null when it can.</param>
public sealed record PanelCommand(string Id, string Label, Func<bool> Run, Func<string?>? Unavailable = null);

/// <summary>One command offered by the palette: what it is, and why it can't run right now, if it can't.</summary>
/// <param name="Command">The registered command offered by the palette.</param>
/// <param name="Unavailable">Why the command cannot run now, or null when it is available.</param>
public sealed record PaletteEntry(CommandInfo Command, string? Unavailable)
{
    /// <summary>True when the command can run now.</summary>
    public bool Available => Unavailable is null;
}

/// <summary>
/// One open document in the editor: its <see cref="DocumentSession{TDoc}"/>, plus what the person is looking at and
/// working on. Selection, focus and the active workspace are view state: changing them never dirties the document, never
/// enters undo and never enters the journal (Godot's <c>mark_unsaved=false</c> lesson). Edits still go only through
/// the session's commands.
/// </summary>
public sealed class EditorModel<TDoc> where TDoc : class
{
    private readonly List<Workspace> workspaces = [];
    private readonly List<string> selection = [];
    private readonly List<string> focus = [];

    /// <summary>An editor over <paramref name="session"/>, described by <paramref name="outline"/>.</summary>
    public EditorModel(DocumentSession<TDoc> session, IDocumentOutline<TDoc> outline)
    {
        Session = session;
        Outline = outline;
        session.Changed += Prune;
    }

    /// <summary>The document session. Every edit goes through it.</summary>
    public DocumentSession<TDoc> Session { get; }

    /// <summary>What the editor knows about the document's objects and places.</summary>
    public IDocumentOutline<TDoc> Outline { get; }

    /// <summary>Raised when selection, focus or the active workspace changes.</summary>
    public event Action? ViewChanged;

    // ---- selection ----

    /// <summary>The selected object ids, in the order they were selected. The last one is the primary selection.</summary>
    public IReadOnlyList<string> Selection => selection;

    /// <summary>The primary selection, or null.</summary>
    public string? Primary => selection.Count > 0 ? selection[^1] : null;

    /// <summary>Selects exactly <paramref name="ids"/> (unknown ids are ignored). Returns false when nothing changed.</summary>
    public bool Select(params string[] ids)
    {
        var known = KnownIds();
        var next = ids.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (next.SequenceEqual(selection)) return false;
        selection.Clear();
        selection.AddRange(next);
        ViewChanged?.Invoke();
        return true;
    }

    private object? knownFor;
    private HashSet<string> knownIds = new(StringComparer.Ordinal);

    /// <summary>The document's object ids as a set, built once per document version: a session's documents are immutable, so
    /// the same instance has the same ids (A211: rebuilding it on every selection cost 44 ms in a 100,000-item file).</summary>
    private HashSet<string> KnownIds()
    {
        var current = Session.Current;
        if (!ReferenceEquals(current, knownFor))
        {
            knownIds = Outline.ObjectIds(current).ToHashSet(StringComparer.Ordinal);
            knownFor = current;
        }
        return knownIds;
    }

    /// <summary>Adds <paramref name="id"/> to the selection, or removes it if it's already selected.</summary>
    public bool Toggle(string id)
    {
        if (selection.Remove(id)) { ViewChanged?.Invoke(); return true; }
        return Select([.. selection, id]);
    }

    // ---- palette makers ----

    private readonly Dictionary<string, Func<EditorModel<TDoc>, ICommand<TDoc>?>> makers = new(StringComparer.Ordinal);

    /// <summary>Says how the palette makes command <paramref name="commandId"/> from the editor's state (the selection, the
    /// focus), so it can run with no further input; the maker returns null when it can't right now. A game or plug-in adds
    /// one per command that makes sense from the palette.</summary>
    public EditorModel<TDoc> Maker(string commandId, Func<EditorModel<TDoc>, ICommand<TDoc>?> make)
    {
        if (Session.Catalog.Commands.All(c => c.Id != commandId)) throw new ArgumentException($"there's no command '{commandId}'", nameof(commandId));
        if (!makers.TryAdd(commandId, make)) throw new ArgumentException($"command '{commandId}' already has a maker", nameof(commandId));
        return this;
    }

    /// <summary>The command the palette would run for <paramref name="commandId"/> now, or null.</summary>
    public ICommand<TDoc>? Make(string commandId) => makers.TryGetValue(commandId, out var m) ? m(this) : null;

    /// <summary>Whether <paramref name="commandId"/> has a maker.</summary>
    public bool HasMaker(string commandId) => makers.ContainsKey(commandId);

    // ---- play ----

    private readonly List<PlayMode<TDoc>> playModes = [];

    /// <summary>The play modes, in the order they were added.</summary>
    public IReadOnlyList<PlayMode<TDoc>> PlayModes => playModes;

    /// <summary>Adds a play mode. Its id must be new.</summary>
    public EditorModel<TDoc> Add(PlayMode<TDoc> mode)
    {
        if (playModes.Any(p => p.Id == mode.Id)) throw new ArgumentException($"play mode '{mode.Id}' is already added", nameof(mode));
        playModes.Add(mode);
        return this;
    }

    /// <summary>Why the host won't let a game start in this editor right now, or null when it may: a host that owns several
    /// editors (a <c>DataWorkspace</c>, one editor per file) refuses a second game while another of its editors plays, so at most
    /// one runs and it can be stopped before the host moves on. Asked by <see cref="Play"/> after the mode is found and before
    /// it starts; its answer is <see cref="Refused"/>. INVENTED.</summary>
    public Func<string?>? PlayRefusal { get; set; }

    /// <summary>The play mode running now, or null when editing.</summary>
    public PlayMode<TDoc>? Playing { get; private set; }

    /// <summary>The running game, or null when editing.</summary>
    public IPlaySession? Game { get; private set; }

    /// <summary>Starts the play mode with <paramref name="id"/> on the current document. Returns false, with the reason in
    /// <see cref="Refused"/>, when it's unknown, something is already playing, or the game fails to start.</summary>
    public bool Play(string id)
    {
        if (Playing is not null) return Refuse($"'{Playing.Title}' is already playing; stop it first");
        if (playModes.FirstOrDefault(p => p.Id == id) is not { } mode) return Refuse($"there's no play mode '{id}'");
        if (PlayRefusal?.Invoke() is { } refused) return Refuse(refused);
        try { Game = mode.Start(Session.Current); }
        catch (Exception e) { return Refuse($"'{mode.Title}' couldn't start: {e.Message}"); }
        Playing = mode;
        Refused = [];
        ViewChanged?.Invoke();
        return true;
    }

    /// <summary>Stops the game and returns to editing; the document is exactly as it was before Play. Returns false when
    /// nothing was playing.</summary>
    public bool Stop()
    {
        if (Playing is null) return false;
        try { Game?.Dispose(); }
        finally
        {
            Game = null;
            Playing = null;
            ViewChanged?.Invoke();
        }
        return true;
    }

    private bool Refuse(string why)
    {
        Refused = [Issue.Error(why)];
        return false;
    }

    // ---- panels ----

    private readonly List<Panel> panels = [];
    private readonly HashSet<string> openPanels = new(StringComparer.Ordinal);

    /// <summary>The added panels, in the order they were added.</summary>
    public IReadOnlyList<Panel> Panels => panels;

    /// <summary>Adds a panel. Its id must be new.</summary>
    public EditorModel<TDoc> Add(Panel panel)
    {
        if (panels.Any(p => p.Id == panel.Id)) throw new ArgumentException($"panel '{panel.Id}' is already added", nameof(panel));
        panels.Add(panel);
        if (panel.OpenAtStart) openPanels.Add(panel.Id);
        return this;
    }

    /// <summary>Whether the panel with <paramref name="id"/> is open.</summary>
    public bool IsOpen(string id) => openPanels.Contains(id);

    /// <summary>Opens or closes a panel. Returns false when there's no such panel or nothing changed.</summary>
    public bool SetOpen(string id, bool open)
    {
        if (panels.All(p => p.Id != id)) return false;
        if (!(open ? openPanels.Add(id) : openPanels.Remove(id))) return false;
        ViewChanged?.Invoke();
        return true;
    }

    // ---- workspaces ----

    /// <summary>The registered workspaces, in registration order.</summary>
    public IReadOnlyList<Workspace> Workspaces => workspaces;

    /// <summary>The workspace in use, or null for the plain editor.</summary>
    public Workspace? Active { get; private set; }

    /// <summary>Adds a workspace. Its id must be new, and every command it leads with must be in the catalogue.</summary>
    public EditorModel<TDoc> Add(Workspace workspace)
    {
        if (workspaces.Any(w => w.Id == workspace.Id)) throw new ArgumentException($"workspace '{workspace.Id}' is already added", nameof(workspace));
        var known = Session.Catalog.Commands.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var missing = workspace.Commands.Where(c => !known.Contains(c)).ToList();
        if (missing.Count > 0) throw new ArgumentException($"workspace '{workspace.Id}' names commands the catalogue doesn't have: {string.Join(", ", missing)}", nameof(workspace));
        workspaces.Add(workspace);
        return this;
    }

    /// <summary>Switches to the workspace with <paramref name="id"/>, or to the plain editor with null.</summary>
    public void Use(string? id)
    {
        var next = id is null ? null : workspaces.FirstOrDefault(w => w.Id == id) ?? throw new ArgumentException($"no workspace '{id}'", nameof(id));
        if (next == Active) return;
        Active = next;
        ViewChanged?.Invoke();
    }

    // ---- the command palette ----

    /// <summary>
    /// Commands matching <paramref name="text"/>, best first (FUT-008):
    /// <list type="number">
    ///   <item>the label or id starts with the text;</item>
    ///   <item>a word in the label starts with it;</item>
    ///   <item>the label or id contains it.</item>
    /// </list>
    /// Within each rank, the active workspace's commands lead in the workspace's own order, then the rest in catalogue order. Internal commands never appear.
    /// <paramref name="unavailable"/> gives a reason a command can't run now (shown, not hidden, so people can find it).
    /// </summary>
    public IReadOnlyList<PaletteEntry> Search(string text, Func<CommandInfo, string?>? unavailable = null)
    {
        var t = text.Trim();
        var lead = (Active?.Commands ?? []).ToList();
        return Session.Catalog.Commands
            .Where(c => !c.Internal)
            .Select((c, order) => (c, order, rank: Rank(c, t)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenBy(x => lead.IndexOf(x.c.Id) is var i and >= 0 ? i : int.MaxValue)   // the workspace's own order
            .ThenBy(x => x.order)
            .Select(x => new PaletteEntry(x.c, unavailable?.Invoke(x.c)))
            .ToList();
    }

    private static int Rank(CommandInfo c, string t)
    {
        if (t.Length == 0) return 0;
        if (c.Label.StartsWith(t, StringComparison.OrdinalIgnoreCase) || c.Id.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return 0;
        if (c.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(t, StringComparison.OrdinalIgnoreCase))) return 1;
        if (c.Label.Contains(t, StringComparison.OrdinalIgnoreCase) || c.Id.Contains(t, StringComparison.OrdinalIgnoreCase)) return 2;
        return -1;
    }

    // ---- focus: honing in on a place ----

    /// <summary>The places focused into, outermost first: the breadcrumb. Empty when looking at the whole document.</summary>
    public IReadOnlyList<string> Focus => focus;

    /// <summary>The innermost focused place, or null for the whole document.</summary>
    public string? FocusedOn => focus.Count > 0 ? focus[^1] : null;

    /// <summary>Focuses on <paramref name="placeId"/> (AT-111). The breadcrumb becomes its chain of parents. Returns false
    /// when the place isn't in the document.</summary>
    public bool FocusOn(string placeId)
    {
        if (Outline.Label(Session.Current, placeId) is null) return false;
        var chain = new List<string>();
        for (var p = placeId; p is not null; p = Outline.Parent(Session.Current, p))
        {
            if (chain.Contains(p)) break;   // a cycle in the outline: stop rather than loop
            chain.Insert(0, p);
        }
        if (chain.SequenceEqual(focus)) return true;
        focus.Clear();
        focus.AddRange(chain);
        ViewChanged?.Invoke();
        return true;
    }

    /// <summary>The deepest object in the outline that holds <paramref name="pointer"/>: the pointer itself when it is one,
    /// else its nearest holder, down to the document's own entry when the outline has one. Null when nothing holds it. An
    /// issue or link about a file's own plain field selects the file's entry this way.</summary>
    public string? HolderOf(string pointer)
    {
        var known = Outline.ObjectIds(Session.Current).ToHashSet(StringComparer.Ordinal);
        if (known.Contains(pointer)) return pointer;
        var parts = Pointer.Split(pointer);
        for (var n = parts.Count; n >= 0; n--)
            if (known.Contains(Pointer.Join(parts.Take(n)))) return Pointer.Join(parts.Take(n));
        return null;
    }

    /// <summary>Steps one level back out. Returns false when already looking at the whole document.</summary>
    public bool FocusOut()
    {
        if (focus.Count == 0) return false;
        focus.RemoveAt(focus.Count - 1);
        ViewChanged?.Invoke();
        return true;
    }

    /// <summary>True when <paramref name="objectId"/> is inside the focused place (or there's no focus). Objects outside it
    /// are hidden or dimmed by the view, and locked: <see cref="Editable"/> is false for them.</summary>
    public bool InFocus(string objectId)
    {
        if (FocusedOn is not { } place) return true;
        for (var p = (string?)objectId; p is not null; p = Outline.Parent(Session.Current, p))
            if (p == place) return true;
        return false;
    }

    /// <summary>Whether <paramref name="objectId"/> may be edited from the view: it exists and is in focus.</summary>
    public bool Editable(string objectId) => Outline.Label(Session.Current, objectId) is not null && InFocus(objectId);

    /// <summary>Applies a command through the session, refusing it while a game is playing, or when it names an object
    /// outside the focus.</summary>
    public bool Execute(ICommand<TDoc> command, params string[] touches) => MayEdit(touches) && Session.Execute(command);

    /// <summary>Applies several commands as one undo step, all or none, under the same refusals as <see cref="Execute"/>.
    /// <paramref name="touches"/> names every object the batch changes, not just the selected one: a focus refuses a batch
    /// that reaches outside it.</summary>
    public bool ExecuteBatch(IReadOnlyList<ICommand<TDoc>> commands, params string[] touches) => MayEdit(touches) && Session.ExecuteBatch(commands);

    /// <summary>As <see cref="ExecuteBatch"/>, for a change decided a while ago, such as a drag let go: also refused when the
    /// document has moved on from <paramref name="revision"/>, or a named object can't be found. The commands' bodies stay
    /// opaque, so <paramref name="touches"/> is still the caller's word: it's not a boundary against code you don't trust.</summary>
    public bool ExecuteAt(long revision, IReadOnlyList<ICommand<TDoc>> commands, params string[] touches)
    {
        if (!MayEdit(touches)) return false;
        if (Session.Revision != revision) return Refuse("the document changed since this began; try it again");
        if (touches.FirstOrDefault(id => Outline.Label(Session.Current, id) is null) is { } gone) return Refuse($"'{gone}' isn't in the document any more");
        return Session.ExecuteBatch(commands);
    }

    private bool MayEdit(string[] touches)
    {
        if (Playing is { } p) return Refuse($"'{p.Title}' is playing; stop it to edit");
        var locked = touches.Where(id => Outline.Label(Session.Current, id) is not null && !InFocus(id)).ToList();
        if (locked.Count > 0)
        {
            Refused = [.. locked.Select(id => Issue.Error($"'{id}' is outside the place you're focused on; step out of focus to edit it", id))];
            return false;
        }
        Refused = [];
        return true;
    }

    /// <summary>Why the editor (not the session) last refused an edit; empty otherwise.</summary>
    public IReadOnlyList<Issue> Refused { get; private set; } = [];

    // After any change to the document (an edit, undo, redo or revert), drop selected or focused objects that are gone.
    private void Prune()
    {
        var known = Outline.ObjectIds(Session.Current).ToHashSet(StringComparer.Ordinal);
        var changed = selection.RemoveAll(id => !known.Contains(id)) > 0;
        var cut = focus.FindIndex(id => !known.Contains(id));
        if (cut >= 0) { focus.RemoveRange(cut, focus.Count - cut); changed = true; }
        if (changed) ViewChanged?.Invoke();
    }
}
