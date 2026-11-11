using System.Text.Json;
using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// Makes one extra panel for a file's editor as the editor is made, or returns null to add none to that file: how a host
/// adds its own per-file panels to a <see cref="DataWorkspace"/> (<see cref="DataWorkspace.FilePanels"/>). The Text panel
/// (<c>Bed.Editor.Text</c>) plugs in this way, so the core editor carries no third-party code.
/// </summary>
/// <param name="file">The file whose editor is being made.</param>
/// <param name="editor">Its editor: a panel reads the document and edits only through it.</param>
public delegate Panel? DataFilePanel(DataFile file, EditorModel<DataDocument> editor);

/// <summary>
/// A <see cref="DataProject"/> in the editor window: a Files panel lists every file, with a mark for unsaved edits and
/// the reason when a file can't open, and showing a file gives it the full editor (outline, inspector, table, palette).
/// Each file keeps its own editor, so switching files keeps each one's selection, undo and unsaved edits. Saving goes
/// through the project's rules: refused when it's read-only or the file changed outside the editor. MIXED: the file list
/// of Godot's FileSystem dock and VS Code's explorer, over INVENTED per-file sessions.
/// <para>Play is the workspace's: at most one file's editor runs a game at a time, and the workspace stops it
/// (<see cref="StopPlay"/>) before it shows another file, replaces or forgets the playing file's window, or is disposed. A host
/// disposes the workspace when it closes or replaces the project, so a game never outlives the editor it was started from.
/// Stopping keeps every draft. MIXED: Unity's and Godot's "leaving the scene stops Play", over INVENTED per-file editors.</para>
/// </summary>
public sealed partial class DataWorkspace : IDisposable
{
    /// <summary>A place that links to an item: <paramref name="At"/> holds the link, inside the item labelled
    /// <paramref name="Label"/>.</summary>
    public sealed record Referrer(ReferenceTarget At, string Label);

    private readonly Dictionary<string, EditorWindow<DataDocument>> windows = new(StringComparer.Ordinal);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SceneDocumentEntry, ViewportView<Scene>> sceneViews = new();
    private readonly Dictionary<string, EditorWindow<Scene>> sceneWindows = new(StringComparer.Ordinal);
    private sealed class SceneFilesBinding { public required DataWorkspace Owner; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EditorModel<Scene>, SceneFilesBinding> sceneFiles = new();
    private void AttachSceneFiles(EditorModel<Scene> editor)
    {
        var binding=sceneFiles.GetValue(editor, key=>
        {
            var state=new SceneFilesBinding {Owner=this};
            var panel=FilesPanel;
            key.Add(panel with {Draw=()=>state.Owner.DrawFiles(),Commands=panel.Commands?.Select(command=>
                new PanelCommand(command.Id,command.Label,
                    ()=>state.Owner.FilesPanel.Commands!.Single(c=>c.Id==command.Id).Run(),
                    ()=>state.Owner.FilesPanel.Commands!.Single(c=>c.Id==command.Id).Unavailable?.Invoke())).ToArray()});
            return state;
        });
        binding.Owner=this;
    }

    private readonly Dictionary<string, DataTableView> tables = new(StringComparer.Ordinal);
    private sealed record NavigationPlace(string File, string[] Selection, string? List, string? Filter);
    private readonly Stack<NavigationPlace> history = new();
    private NavigationPlace? NavigationHere() => Active is {} file ? new(file.Name,
        [.. Window?.Editor.Selection ?? SceneWindow?.Editor.Selection ?? Scene2Window?.Editor.Selection ?? []],
        TableOf(file.Name)?.List, TableOf(file.Name)?.Filter) : null;
    private readonly Shortcuts shortcuts;
    private readonly Action<EditorModel<DataDocument>, DataTableView>? configure;
    private (string? File, string? Item) referencedFor;
    private object[] referencedFrom = [];
    private IReadOnlyList<Referrer> referenced = [];
    private ImInputString nameText = new(256, ""u8);
    private FileForm form;
    private bool focusForm;
    // Where the keyboard focus goes once the form that had it is gone (found by A217: cancelling, finishing New or Rename and
    // deleting left focus on a control that no longer existed, with no cursor, and Tab couldn't get out). A file's row or an
    // action button, by what it does (not its label, which may change); put back on a later frame, once the Files panel it
    // lands in is drawn.
    private (string? File, FileAction? Button, int Frame)? refocus;
    private FileAction formOpener = FileAction.New;
    private string newSchema = "";
    private int formLinks;

    /// <param name="project">The project to show.</param>
    /// <param name="shortcuts">Key bindings shared by every file's editor.</param>
    /// <param name="configure">Called once for each file's editor as it's made, for a game's adapter to add panels,
    /// play modes or references.</param>
    /// <param name="map">The game's reference map, if it has one: it names each list's label field, and its links are
    /// followed across files, with Back returning across them too.</param>
    public DataWorkspace(DataProject project, Shortcuts? shortcuts = null, Action<EditorModel<DataDocument>, DataTableView>? configure = null,
        ReferenceMap? map = null)
    {
        Project = project;
        requestProjectClose = RequestProjectClose;
        Proposals = new EditProposals(project, Checks, map);
        Map = map;
        this.shortcuts = shortcuts ?? new Shortcuts();
        this.shortcuts.Default(EditorWindow<DataDocument>.PanelAction+"project.find_replace","Ctrl+H");
        this.configure = configure;
    }

    /// <summary>The project.</summary>
    public DataProject Project { get; }

    /// <summary>The project's single typed session authority, shared by every workspace over this project.</summary>
    public DocumentHost Documents => DocumentHost.For(Project);

    /// <summary>The scene window currently shown, or null for a data file. Save uses the typed host; Save as remains refused.</summary>
    public EditorWindow<Scene>? SceneWindow => Active is { } f && Documents.TryGetOpenScene(f.Name, out var entry) && sceneWindows.TryGetValue(f.Name, out var w)
        && ReferenceEquals(w.Editor, entry!.Editor) ? w : null;

    /// <summary>Captures a browser-provided admitted selection for a later scene drop. No path fallback or producer is inferred.</summary>
    public bool CaptureSceneAsset(AssetCatalog.Selection selection, out SceneAssetPlacement? placement, string? parent = null)
    {
        placement = null;
        if (Active is not { } file || SceneWindow is null) { Said = "Open a scene before placing an asset."; return false; }
        var ok = Documents.CaptureAssetPlacement(file.Name, selection, out placement, out var why, parent);
        Said = why; return ok;
    }

    /// <summary>Drop receiver for an acquired selection. The host supplies the root position; no native drag handler is installed here.</summary>
    public bool DropSceneAsset(SceneAssetPlacement placement, string id, System.Numerics.Vector3 position)
    {
        if (Active?.Name != placement.DocumentName || SceneWindow is null)
        { Said = "The active scene changed; start placement again."; return false; }
        var ok = Documents.PlaceAsset(placement, id, position, out var why);
        Said = why; return ok;
    }

    /// <summary>Keyboard-command receiver using exactly the same capture/commit path as drop.
    /// The browser command supplies its selected catalog identity and requested root position.</summary>
    public bool InsertSceneAsset(AssetCatalog.Selection selection, string id, System.Numerics.Vector3 position, string? parent = null)
        => CaptureSceneAsset(selection, out var placement, parent) && DropSceneAsset(placement!, id, position);

    /// <summary>The shared CPU viewport of the active scene, with the same editor selection and session as its other panels.</summary>
    public ViewportView<Scene>? SceneViewport => Active is { } file && Documents.IsScene(file.Name)
        && Documents.TryGetOpenScene(file.Name, out var entry) && sceneViews.TryGetValue(entry!, out var view) ? view : null;

    /// <summary>Memory-only proposal store, owned by this workspace's editor thread. Hosts may inject it into AgentSession.</summary>
    public EditProposals Proposals { get; }

    /// <summary>Closes the project, when a host can (the general app returns to its start screen); null hides Close.
    /// Returns false, having said why, when it doesn't close.</summary>
    public Func<bool>? CloseProject { get => closeProject is null ? null : requestProjectClose; init => closeProject = value; }
    private readonly Func<bool>? closeProject;
    private readonly Func<bool> requestProjectClose;

    internal bool TypedProjectCloseConnected {get;set;}
    private bool RequestProjectClose()
    {
        if(scene2WorkspaceClosed||Scene2OwnerCurrent?.Invoke(this)==false){Said="This workspace is no longer current.";return false;}
        if(closeProject is null)return false;
        sceneClose=null;scene2Close=null;
        if(TypedProjectCloseConnected||TransitionDraftCount==0)return closeProject();
        projectCloseDrafts??=TransitionDrafts();focusProjectClose=true;Said="Save changes before closing the project?";return false;
    }

    /// <summary>The game's reference map, if any.</summary>
    public ReferenceMap? Map { get; }

    /// <summary>Called once for each file's editor as it's made, after <c>configure</c>, with this workspace and the file: for an
    /// adapter whose panels or play modes need the rest of the project (a play mode that reads two files' unsaved drafts reads them
    /// through <see cref="Project"/>, never from disk behind the editor's back). A fault in it costs its panels, never the file
    /// (<see cref="AdapterProblem"/> says why). INVENTED.</summary>
    public Action<DataWorkspace, DataFile, EditorModel<DataDocument>, DataTableView>? ConfigureFile { get; init; }

    private GameLaunchSpec? linkedSpec;
    private PlaySnapshotConverter? linkedConverter;
    private ILinkedPlayHost? linkedHost;
    /// <summary>Binds an adapter's explicit linked declaration to current and future file editors. No process starts here.</summary>
    public void BindLinkedPlay(GameLaunchSpec spec,PlaySnapshotConverter converter,ILinkedPlayHost host)
    {
        ArgumentNullException.ThrowIfNull(spec);ArgumentNullException.ThrowIfNull(converter);ArgumentNullException.ThrowIfNull(host);
        linkedSpec=spec;linkedConverter=converter;linkedHost=host;
        Documents.DeclareLinkedPlay(spec);
        foreach(var pair in windows) BindLinked(pair.Value,pair.Key);
        foreach(var pair in sceneWindows) BindLinked(pair.Value,pair.Key);
    }
    private bool OtherLinked(object editor)=>windows.Values.Any(w=>!ReferenceEquals(w.Editor,editor) && w.Editor.Game is LinkedPlayPreview)
        || sceneWindows.Values.Any(w=>!ReferenceEquals(w.Editor,editor) && w.Editor.Game is LinkedPlayPreview);
    private string? LinkedRefusal(object editor)=>windows.Values.Any(w=>!ReferenceEquals(w.Editor,editor) && w.Editor.Playing is not null)
        || sceneWindows.Values.Any(w=>!ReferenceEquals(w.Editor,editor) && w.Editor.Playing is not null)
        ? "Another game is playing in this workspace; stop it first." : null;
    private void BindLinked<T>(EditorWindow<T> window,string name) where T:class
    {
        if(linkedSpec is null || linkedConverter is null || linkedHost is null) return;
        if(window.BindLinkedPlay(linkedSpec,linkedConverter,linkedHost,name,()=>LinkedRefusal(window.Editor)))
        {
            var previous=window.Editor.PlayRefusal;
            window.Editor.PlayRefusal=()=>OtherLinked(window.Editor)
                ? "A linked game is playing in this workspace; stop it first." : previous?.Invoke();
        }
    }

    // ---- play ----

    /// <summary>The file editor whose game is running, or null when none is. At most one is: the others refuse Play while it
    /// runs (<see cref="EditorModel{TDoc}.PlayRefusal"/>).</summary>
    public EditorModel<DataDocument>? Playing => windows.Values.Select(w => w.Editor).FirstOrDefault(e => e.Playing is not null);

    /// <summary>Stops every game running in this workspace's editors, through each editor's own <see cref="EditorModel{TDoc}.Stop"/>,
    /// so each game's <see cref="IDisposable.Dispose"/> runs and the editor returns to editing with its drafts as they were. A game
    /// whose disposal throws is still stopped, the rest are still stopped, and <see cref="Said"/> says what failed. Returns true when
    /// a game was running. Safe to call any number of times.</summary>
    public bool StopPlay()
    {
        var (stopped, fault) = StopAll();
        if (fault.Length > 0) Said = fault;
        return stopped;
    }

    // Stops every running game; what failed while stopping, or "". The launcher uses it to say a failure with its own message.
    internal (bool Stopped, string Fault) StopAll()
    {
        var stopped = false;
        var faults = new List<string>();
        if(projectPlay is not null)
        {
            stopped=!projectPlay.HasCompleted;
            try {DisposeProjectPlay();}
            catch(Exception error){faults.Add("Project Play failed while stopping: "+error.Message);}
        }
        foreach (var editor in windows.Values.Select(w => w.Editor).Where(e => e.Playing is not null).ToList())
        {
            var title = editor.Playing!.Title;
            try { stopped |= editor.Stop(); }
            catch (Exception e) { stopped = true; faults.Add($"'{title}' failed while stopping: {e.Message}. It has stopped; your edits are safe."); }
        }
        foreach(var editor in Project.Files.Select(f=>Documents.TryGetOpenScene(f.Name,out var entry)?entry!.Editor:null).OfType<EditorModel<Scene>>().Distinct().Where(e=>e.Playing is not null).ToList())
        {
            var title=editor.Playing?.Title ?? "Linked game";
            try {stopped |= editor.Stop();}
            catch(Exception error) {stopped=true;faults.Add($"{title} failed while stopping: {error.Message}");}
        }
        foreach(var editor in Project.Files.Select(f=>Documents.TryGetOpenScene2(f.Name,out var e)?e!.Editor:null).OfType<EditorModel<Scene2>>().Distinct().Where(e=>e.Playing is not null).ToList())
        {try{stopped|=editor.Stop();}catch(Exception e){stopped=true;faults.Add(e.Message);}}
        return (stopped, string.Join(" ", faults));
    }

    /// <summary>Stops any running game (<see cref="StopPlay"/>). A host calls it when it closes or replaces the project, or shuts
    /// down. The project and its drafts are untouched: disposing doesn't save, close files or discard edits.</summary>
    public void Dispose() { scene2WorkspaceClosed=true; pendingMixedSave?.Dispose();pendingMixedSave=null; ReleaseObjectClipboard(); exportDialog?.Dispose(); StopPlay(); Proposals.Dispose(); ReleaseProjectAssets(); }

    // Stops a game running in the window shown for name, before its file is renamed, deleted or its window forgotten. What
    // failed while stopping, to say with the action's own message (" ..."), or "".
    private string StopPlayIn(string name) =>
        (windows.TryGetValue(name, out var w) && w.Editor.Playing is not null
            || sceneWindows.TryGetValue(name,out var scene) && scene.Editor.Game is LinkedPlayPreview) && StopAll().Fault is { Length: > 0 } fault ? " " + fault : "";

    // Before another file is shown, or the shown file's window is replaced: Stop, so no game runs hidden. What failed while
    // stopping, to say with what's shown, or "".
    private string StopPlayFor(DataFile file)
    {
        if(sceneWindows.Any(pair=>pair.Value.Editor.Game is LinkedPlayPreview
            && (pair.Key!=file.Name || !Documents.TryGetOpenScene(file.Name,out var entry) || !ReferenceEquals(pair.Value.Editor,entry!.Editor))))
            return StopAll().Fault is {Length:>0} linkedFault ? " "+linkedFault : "";
        if(scene2Windows.Any(pair=>pair.Value.Entry.Editor.Playing is not null && (pair.Key!=file.Name||!Documents.TryGetOpenScene2(file.Name,out var current)||!ReferenceEquals(current,pair.Value.Entry))))
            return StopAll().Fault is {Length:>0} v2Fault?" "+v2Fault:"";
        if (Playing is not { } playing) return "";
        if (windows.TryGetValue(file.Name, out var w) && ReferenceEquals(w.Editor, playing) && ReferenceEquals(w.Editor.Session, file.Session)) return "";
        return StopAll().Fault is { Length: > 0 } fault ? " " + fault : "";
    }

    /// <summary>The table of the file called <paramref name="name"/>, once it's been shown.</summary>
    public DataTableView? TableOf(string name) => tables.GetValueOrDefault(name);

    /// <summary>The host's extra per-file panels, called in order once for each file as its editor is made, after the
    /// table and before the game adapter's <c>configure</c>. Add them before the first file is shown: a file already shown
    /// keeps the panels it was made with. A factory that throws costs its panel, never the file
    /// (<see cref="AdapterProblem"/> says why). INVENTED: the seam that keeps the core free of the Text panel's
    /// third-party code.</summary>
    public IList<DataFilePanel> FilePanels { get; } = new List<DataFilePanel>();

    // Why a map's link can't be followed, with the next step; null where the map says it isn't a link or it was found.
    private static string? Problem(ReferenceResult r)
    {
        var note = r.Note is { Length: > 0 } n ? n + ". " : "";
        return r.Outcome switch
        {
            ReferenceOutcome.Ambiguous => note + "Give one of them a different id, then follow again.",
            ReferenceOutcome.Missing => note + "No item has this id: check its spelling, or add the item it names.",
            ReferenceOutcome.NotLoaded => note + "The file it points into isn't open: see the Files panel for why.",
            ReferenceOutcome.NeedsScope => note + "The map describes this link only in words, without a fixed target. Read the relationship description to choose a target.",
            ReferenceOutcome.Sentinel => "this value deliberately names nothing.",
            _ => null,
        };
    }

    /// <summary>Shows an item in any file: shows the file, its item's list in the table, and selects the item (or, for a
    /// target nested inside an item, that item, saying where inside), remembering where it came from for
    /// <see cref="Back"/>. Returns false, saying why, when it can't.</summary>
    public bool GoTo(ReferenceTarget target)
    {
        if (Documents.IsScene(target.File))
        {if(Documents.OpenScene2(target.File,out _,out _))return GoToScene2(target);Said = "Tables can't be shown for a level file."; return false;}
        var from = NavigationHere();
        if (!Show(target.File)) return false;
        if (from is not null) history.Push(from);
        var table = tables[target.File];
        table.List = Pointer.Join(Pointer.Split(target.Pointer).Take(1));
        table.Filter = "";
        // The outline knows top-level places and their items; a target nested deeper (a station inside a sector) selects
        // the nearest item holding it, and says where inside (found by A044: deep targets selected nothing).
        var editor = Window!.Editor;
        var known = editor.Outline.ObjectIds(editor.Session.Current).ToHashSet(StringComparer.Ordinal);
        var parts = Pointer.Split(target.Pointer);
        var holder = Enumerable.Range(0, parts.Count + 1).Reverse().Select(n => Pointer.Join(parts.Take(n))).FirstOrDefault(known.Contains);
        if (holder is not null) editor.Select(holder);
        var shown = holder is null ? target.Pointer : editor.Outline.Label(editor.Session.Current, holder) ?? holder;
        // What showing the file had to say beyond "Showing" (a game that failed to stop, an adapter's problem) is kept (A236).
        Said = (holder == target.Pointer || holder is null ? $"Went to {shown} in {target.File}."
            : $"Went to {shown} in {target.File}; the link points inside it, at {target.Pointer[holder.Length..]}.") + shownFaults;
        return true;
    }

    /// <summary>Returns to the file and item the last cross-file <see cref="GoTo"/> left. False when there's none.</summary>
    public bool Back()
    {
        while (history.Count > 0)
        {
            var place = history.Pop();
            if (!Show(place.File)) continue;
            if (Window is {} dataWindow) dataWindow.Editor.Select(place.Selection);
            else if (SceneWindow is {} sceneWindow) sceneWindow.Editor.Select(place.Selection);
            else if (Scene2Window is {} scene2Window) scene2Window.Editor.Select(place.Selection);
            if (TableOf(place.File) is {} table)
            { table.List = place.List; table.Filter = place.Filter ?? ""; }
            Said = $"Back to {place.File}." + shownFaults;   // a stop failure while going back is kept (A236)
            return true;
        }
        return false;
    }

    /// <summary>The file being shown, if any.</summary>
    public DataFile? Active { get; private set; }

    /// <summary>The editor window of the file being shown, if any.</summary>
    public EditorWindow<DataDocument>? Window => Active is { } f && windows.TryGetValue(f.Name, out var w) ? w : null;

    /// <summary>What the last action did, or why it didn't.</summary>
    public string Said { get; private set; } = "";

    /// <summary>Why the game adapter, or a host's extra panel (<see cref="FilePanels"/>), couldn't finish setting up the
    /// file last made ready, or null. The file still opens and edits normally, perhaps without those panels.</summary>
    public string? AdapterProblem { get; private set; }

    /// <summary>Puts <paramref name="said"/> where the workspace shows what the last action did.</summary>
    internal void Note(string said) => Said = said;

    // What the last Show said beyond "Showing ...": GoTo and Back keep it in their own message.
    private string shownFaults = "";

    private static string SaveInterruptionNotice(string path)
    {
        try
        {
            var prefix = "." + Path.GetFileName(path) + ".";
            foreach (var candidate in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"))
            {
                var name = Path.GetFileName(candidate);
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal)
                    && Guid.TryParseExact(name[prefix.Length..^4], "N", out _))
                    return " A temporary save remains; a save may have been interrupted or may still be running. Opened the saved file only; temporary files were left untouched.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return "";
    }

    /// <summary>Shows the file called <paramref name="name"/>, opening it first if need be. Returns false, saying why,
    /// when there's no such file or it can't be opened; whatever was shown stays shown.</summary>
    public bool Show(string name)
    {
        if (Project.Find(name) is not { } file) { Said = $"There's no file called {name} in the project."; return false; }
        if (Documents.IsScene(name))
        {
            if (Documents.OpenScene2(name,out var v2,out _))return ShowScene2(file,v2!);
            if (!Documents.OpenScene(name, out var entry, out var why)) { Said = why; return false; }
            var stopped = StopPlayFor(file);
            if (!sceneWindows.TryGetValue(name, out var previous) || !ReferenceEquals(previous.Editor, entry!.Editor))
            {
                var editor = entry!.Editor;
                var flags=Flags(name);
                AttachSceneAssets(name, entry);
                shortcuts.Default("editor.close", "Ctrl+W");
                sceneViews.GetValue(entry, key =>
                {
                    ViewportView<Scene>? view = null;
                    var source = new SceneCatalogSource(() => { if (view is not null) { view.CancelInput(); view.SourceGeneration++; } });
                    view = new ViewportView<Scene>(key.Editor, source);
                    source.Catalog = SceneAssets;
                    source.Flags=flags;
                    view.MaterialCatalog = SceneAssets;
                    key.Editor.Add(view.AsPanel());
                    return view;
                });
                sceneWindows[name] = new EditorWindow<Scene>(editor,
                    new Inspector<Scene>(editor).Add(SceneEditing.ObjectType()), shortcuts, SceneEditing.CommandFor)
                {
                    CloseDocument = () => RequestSceneClose(name, entry),
                    PanelCloseRequested = id=>{if(id=="editor.viewport")RequestSceneClose(name,entry);else editor.SetOpen(id,false);},
                    CopyObject = _ => sceneWindows[name].Run("scene.copy"),
                    OutlineAssetNames = SceneAssetSearchNames,
                    SaveRefusal = () => Project.ReadOnlyReason(name) ?? "Scene files keep their project binding. Use Save to update this level; create another level through your game's project tools.",
                    SaveAsUnavailable = () => Project.ReadOnlyReason(name) ?? "Scene files keep their project binding. Use Save to update this level; create another level through your game's project tools.",
                    SaveAction = () =>
                    {
                        if (!Documents.TryGetOpenScene(name, out var current) || !ReferenceEquals(current, entry))
                            return (false, "This scene window is no longer current; use the reopened scene.");
                        if (Project.Judging) return (false, "A data save check is still running; try saving after it finishes.");
                        var ok = Documents.SaveScene(name, out var message);
                        return (ok, message);
                    }
                };
            }
            BindProblemReport(sceneWindows[name]);
            BindSceneClipboard(sceneWindows[name]);
            BindSceneViewChoices(name,sceneWindows[name],Flags(name));
            AttachSceneFiles(sceneWindows[name].Editor);
            BindLinked(sceneWindows[name],name);
            Active = file;
            AdapterProblem = null;
            shownFaults = stopped + SaveInterruptionNotice(file.Path);
            Said = $"Showing {name}." + shownFaults;
            return true;
        }
        if (!file.Open()) { Said = $"{name} can't be opened: {string.Join(" ", file.Problems.Select(p => p.Message))}"; return false; }
        var stopFault = StopPlayFor(file);   // a game stops before another file is shown or its window is replaced (B4)
        // A window is kept only while it edits the file's own session: a file closed and opened again has a new one, and an
        // old window would save the closed draft (found by A186).
        var made = !windows.TryGetValue(name, out var kept) || !ReferenceEquals(kept.Editor.Session, file.Session);
        if (made) windows[name] = Make(file);
        Active = file;
        shownFaults = (made && AdapterProblem is { } problem ? " " + problem : "") + stopFault + SaveInterruptionNotice(file.Path);
        Said = $"Showing {name}." + shownFaults;
        return true;
    }

    /// <summary>Opens a file that doesn't fit its schema for repair and shows it: its problems are listed, and edits that
    /// add no problem are allowed until none are left. Returns false, saying why, when it can't be read at all.</summary>
    public bool Repair(string name)
    {
        if (Project.Find(name) is not { } file) { Said = $"There's no file called {name} in the project."; return false; }
        if (!file.Repair()) { Said = $"{name} can't be repaired here: {string.Join(" ", file.Problems.Select(p => p.Message))}"; return false; }
        var stopFault = StopPlayFor(file);
        // A window is kept only while it edits the file's own session: a file closed and opened again has a new one, and an
        // old window would save the closed draft (found by A186).
        var made = !windows.TryGetValue(name, out var kept) || !ReferenceEquals(kept.Editor.Session, file.Session);
        if (made) windows[name] = Make(file);
        Active = file;
        var fault = (made && AdapterProblem is { } problem ? " " + problem : "") + stopFault + SaveInterruptionNotice(file.Path);
        Said = (file.Session!.Repairing ? $"Repairing {name}: {file.Session.Problems.Count} problem(s) to fix; edits that add none are allowed." : $"Showing {name}.") + fault;
        return true;
    }

    /// <summary>Saves every captured draft. Returns false while a background group check is pending or a save is refused;
    /// FinishSaves completes pending groups on the owner thread, and Said explains the current result.</summary>
    public bool SaveAll()
    {
        if(scene2WorkspaceClosed||Scene2OwnerCurrent?.Invoke(this)==false){Said="This workspace is no longer current.";return false;}
        if(pendingMixedSave is not null){Said="The save group is being checked; it saves itself when the checks pass.";return false;}
        if(BackgroundSaves&&Project.SaveCheck is not null&&Documents.HasSceneBindings&&Project.Dirty.Count>0)
        {
            if(pendingSaves.Count>0){Said="Wait for the current checked save before saving the group.";return false;}
            pendingMixedCount=TransitionDraftCount;pendingMixedSave=Documents.BeginMixedSave(true,MixedSaveOwnerRefusal);
            if(!pendingMixedSave.Verdict.IsCompleted){Said="Checking the save group before writing any files.";return false;}
            return FinishMixedSave();
        }
        var dirty = TransitionDraftCount;
        var failed = Documents.HasSceneBindings?Documents.BeginMixedSave(false,MixedSaveOwnerRefusal).Complete():Project.SaveAll();
        Said = failed.Count == 0 ? (dirty == 0 ? "Nothing to save." : $"Saved {dirty} file{(dirty == 1 ? "" : "s")}.")
            : string.Join(" ", failed.Select(f => string.Join(" ", f.Problems.Select(p => p.Message))));
        return failed.Count == 0;
    }

    /// <summary>Makes a new file from a schema and shows it (<see cref="DataProject.Create"/>). False, saying why, when
    /// it's refused.</summary>
    public bool NewFile(string name, string schema)
    {
        var ok = Project.Create(name, schema, out var why);
        Said = why;
        if (ok) { Show(name); Said = why; }
        return ok;
    }

    /// <summary>Renames a file (<see cref="DataProject.Rename"/>), showing it under its new name if it was shown. False,
    /// saying why, when it's refused.</summary>
    public bool RenameFile(string from, string to)
    {
        var shown = Active?.Name == from;
        var stopFault = StopPlayIn(from);   // its game stops before the file is closed and moved (B4), even if the rename is then refused
        if (!Project.Rename(from, to, out var why)) { Said = why + stopFault; return false; }
        Forget(from);
        if (shown) Show(to);
        Said = (Map is null ? why : why + " The reference map names files: update it if it names " + from + ".") + stopFault;
        return true;
    }

    /// <summary>Deletes a file (<see cref="DataProject.Delete"/>); <see cref="RestoreFile"/> puts it back until the project
    /// is closed. False, saying why, when it's refused.</summary>
    public bool DeleteFile(string name)
    {
        var stopFault = StopPlayIn(name);   // its game stops before the file is closed and deleted (B4), even if the delete is then refused
        if (!Project.Delete(name, out var why)) { Said = why + stopFault; return false; }
        Forget(name);
        Said = why + stopFault;
        return true;
    }

    /// <summary>Puts back a file deleted here (<see cref="DataProject.Restore"/>). False, saying why, when it can't.</summary>
    public bool RestoreFile(string name)
    {
        var ok = Project.Restore(name, out var why);
        Said = why;
        return ok;
    }

    /// <summary>How many links in other files point at items in the file <paramref name="name"/>, by the map: what a
    /// delete would leave dangling, or a rename would leave pointing at the old name. 0 with no map.</summary>
    public int LinksInto(string name) => Map?.LinksInto(Project, name) ?? 0;

    // A file gone under its old name: its editor and table go, and nothing stays shown from it.
    private void Forget(string name)
    {
        StopPlayIn(name);   // its game stops before its window goes (B4)
        windows.Remove(name);
        tables.Remove(name);
        if (Active?.Name == name) Active = null;
    }

    private enum FileForm { None, New, Rename, Delete, Copy }

    // The Files panel's action buttons, by what they do.
    private enum FileAction { SaveAll, Refresh, New, Rename, Delete, Restore, Close }

    private bool OpenForm(FileForm which)
    {
        if (which is FileForm.Rename or FileForm.Delete && Active is null) return false;
        form = which;
        formOpener = which switch { FileForm.Rename => FileAction.Rename, FileForm.Delete => FileAction.Delete, _ => FileAction.New };
        formLinks = which is FileForm.Rename or FileForm.Delete ? LinksInto(Active!.Name) : 0;
        nameText = new ImInputString(256, which == FileForm.Rename ? Active!.Name : "");
        focusForm = true;
        return true;
    }

    /// <summary>Lists the folder again (<see cref="DataProject.Refresh"/>).</summary>
    public void Refresh()
    {
        Project.Refresh();
        Said = $"{Project.Files.Count} files.";
    }

    /// <summary>What refers to the selected item of the shown file, by the map's links, in every file (unsaved edits
    /// included): each with the file, the place holding the link, and the referring item's label. Empty with no map or
    /// no selection. Worked out again only when the selection or any open file changes. MIXED: Rider's and VS Code's
    /// "find usages" over the map's own reverse search (<see cref="ReferenceMap.ReferencesTo"/>).</summary>
    public IReadOnlyList<Referrer> ReferencedBy()
    {
        // Kept while every file is the same file with the same session and document: a revision count can repeat after a file is
        // closed and opened again, and the old answer would stand (found by A205).
        var key = (Active?.Name, Window?.Editor.Primary);
        var files = Project.Files;
        var same = key == referencedFor && referencedFrom.Length == files.Count;
        for (var i = 0; same && i < files.Count; i++)
            same = ReferenceEquals(referencedFrom[i], (object?)files[i].Session?.Current ?? files[i]);
        if (same) return referenced;
        referencedFor = key;
        referencedFrom = [.. files.Select(f => (object?)f.Session?.Current ?? f)];
        // What refers to the item or anything inside it: what removing it would break (found by A205).
        referenced = Map is { } map && key.Name is { } file && key.Primary is { } item
            ? map.ReferencesInto(Project, new ReferenceTarget(file, item)).Select(r => new Referrer(r, LabelOf(map, r))).ToList()
            : [];
        return referenced;
    }

    // The referring item's label, from the map's label field, or its place.
    private string LabelOf(ReferenceMap map, ReferenceTarget at)
    {
        var parts = Pointer.Split(at.Pointer);
        var item = Pointer.Join(parts.Take(2));
        return parts.Count >= 2 && Project.Find(at.File) is { Session: { } s } && s.Current.At(item) is { ValueKind: JsonValueKind.Object } value
            && map.Label(at.File, item, value) is { Length: > 0 } label ? label : at.Pointer;
    }

    /// <summary>The project's checks (piece J): the tools' own link check, and any a game's adapter adds. They run on a
    /// snapshot, off the frame, from the Checks panel or the palette.</summary>
    public Checks Checks { get; } = new Checks().Add(new LinkCheck());

    /// <summary>Starts the check <paramref name="id"/> on the project as it is now, unsaved edits included. False, saying
    /// why, when there's no such check or it's already running.</summary>
    public bool RunCheck(string id)
    {
        var form = OptionsForCheck(id);
        string? options = null;
        if (form is not null && !form.TryGetOptions(out options, out var refused)) { Said = refused; return false; }
        var run = Checks.Start(id, Project, out var why, Map, options);
        Said = why;
        return run is not null;
    }

    private readonly Dictionary<string, CheckOptionsForm> checkOptions = new(StringComparer.Ordinal);

    /// <summary>The current memory-only choices, initialized once. A check without a schema has no form.</summary>
    public CheckOptionsForm? OptionsForCheck(string id)
    {
        var definition = Checks.Registered.FirstOrDefault(d => d.Id == id);
        if (definition?.OptionsSchema is null) return null;
        if (!checkOptions.TryGetValue(id, out var form)) checkOptions[id] = form = new CheckOptionsForm(definition);
        return form;
    }

    /// <summary>Whether a check can run now from the panel, and if not, why (unknown check, unsupported or incomplete options).</summary>
    public bool CanRunCheck(string id, out string why)
    {
        if (!Checks.Registered.Any(d => d.Id == id)) { why = $"There's no check called '{id}'."; return false; }
        if (Checks.Last(id) is { IsOver: false }) { why = "it's running"; return false; }
        why = OptionsForCheck(id)?.Problem ?? "";
        return why.Length == 0;
    }

    private bool OptionsStale(CheckRun run)
    {
        if (OptionsForCheck(run.Check.Id) is not { } form) return false;
        return !form.Supported || form.HasPendingEdits
            || run.Captured is { } captured && !JsonElement.DeepEquals(captured.Options, form.Options);
    }

    /// <summary>The recorded verdict, including a changed or uncommitted option; the panel uses plain display wording.</summary>
    public string CheckVerdict(string id)
    {
        if (Checks.Last(id) is not { } run) return "never run";
        var word = run.Verdict(Project, PanelFreshness);
        return run.IsOver && OptionsStale(run) && !word.EndsWith(" (stale)", StringComparison.Ordinal) ? word + " (stale)" : word;
    }

    /// <summary>Shows a check finding's exact data field in the Inspector, its scene object in Objects, or its file for
    /// a file-wide finding. Missing fields select their owner and explain why the exact field cannot be shown. Refused,
    /// saying why, when the run that found it is stale: places are list positions, and after an edit the same position
    /// may hold a different item.</summary>
    public bool Reveal(CheckFinding finding)
    {
        var run = Checks.Registered.Select(d => Checks.Last(d.Id)).FirstOrDefault(r => r?.Report?.Findings.Contains(finding) == true);
        if (run is not null && run.IsStale(Project))
        {
            Said = "The project has changed since this check ran, so its places may name other items now. Run it again, then go to the finding.";
            return false;
        }
        if (run is not null && OptionsStale(run))
        {
            Said = "The check options have changed since this check ran. Run it again, then go to the finding.";
            return false;
        }
        return RevealFindingPlace(finding.Place);
    }

    // How old a check's staleness may be when the Checks panel shows it: it's asked every frame, and asking re-reads every file's
    // stamp (A179). A change shows within this.
    private static readonly TimeSpan PanelFreshness = TimeSpan.FromMilliseconds(500);

    /// <summary>The Checks panel: each check with Run (or Cancel while it runs), its progress, its verdict (stale once
    /// the project changes), and its findings, each leading to its place. It starts closed, so it doesn't cover
    /// "Used by" on the right; running a check from the palette opens it.</summary>
    public Panel ChecksPanel => new("project.checks", "Checks", DrawChecks, OpenAtStart: false, Width: 360, Height: 300, Side: PanelSide.Right,
        Commands: Checks.Registered.Select(d => new PanelCommand("run." + d.Id, "Run check: " + d.Label, () => RunCheck(d.Id),
            () => CanRunCheck(d.Id, out var why) ? null : why)).ToList());

    /// <summary>The "Used by" panel: what links to the selected item, each shown with one choice.</summary>
    public Panel ReferencesPanel => new("project.references", "Used by", DrawReferences, Width: 320, Height: 260, Side: PanelSide.Right);

    /// <summary>The Files panel, as each file's editor shows it.</summary>
    public Panel FilesPanel => new("project.files", "Files", DrawFiles, Width: 260, Height: 420,
        Commands:
        [
            new("new_project", "New project...", () => newProject?.Invoke()??false, () => newProject is null?"This editor has no project host.":null),
            new("open_project", "Open project...", () => openProject?.Invoke()??false, () => openProject is null?"This editor has no project host.":null),
            new("open_file", "Open file...", OpenProjectFile),
            new("save_all", "Save all files", SaveAll, () => Project.ReadOnly ? "the project is open read-only" : TransitionDraftCount == 0 ? "nothing to save" : null),
            new("refresh", "Refresh files", () => { Refresh(); return true; }),
            new("new_file", "New data file from a rules file...", () => OpenForm(FileForm.New), () => Project.ReadOnly ? "the project is open read-only" : null),
            new("rename_file", "Rename the shown file...", () => OpenForm(FileForm.Rename),
                () => Project.ReadOnly ? "the project is open read-only" : Active is null ? "no file is shown" : null),
            new("delete_file", "Delete the shown file...", () => OpenForm(FileForm.Delete),
                () => Project.ReadOnly ? "the project is open read-only" : Active is null ? "no file is shown" : null),
            new("close_project", "Close the project", () => CloseProject?.Invoke() ?? false, () => CloseProject is null ? "this editor can't close its project" : null),
            new("restore_file", "Restore the last deleted file", () => Project.Deleted.Count > 0 && RestoreFile(Project.Deleted[^1]),
                () => Project.Deleted.Count == 0 ? "no file was deleted here" : null),
        ], Side: PanelSide.Left);

    /// <summary>Whether a window's save runs the game's check before saving (<see cref="DataProject.SaveCheck"/>) on a worker, so a
    /// check that takes seconds doesn't stop the editor. The save is pending while it runs and finishes
    /// itself when the check passes, through the window's own save and all its guards. An edit made meanwhile isn't saved
    /// unchecked: it's refused as changed while it was being checked, and keeps its edits. Off by default, so a save is one
    /// call that answers at once; the app turns it on. Typed SaveAll groups check data on a worker and commit together
    /// through FinishSaves. Plain-data SaveAll and an AI's direct saves remain synchronous.</summary>
    public bool BackgroundSaves { get; set; }

    /// <summary>Whether a checked save containing <paramref name="name"/> is still pending.</summary>
    public bool SavePending(string name) => pendingSaves.ContainsKey(name)||pendingMixedSave?.Files.Any(f=>f.Name==name)==true;
    private DocumentHost.MixedSave? pendingMixedSave;
    private int pendingMixedCount;
    private string? MixedSaveOwnerRefusal()=>scene2WorkspaceClosed||Scene2OwnerCurrent?.Invoke(this)==false?"This workspace is no longer current; nothing was saved.":null;
    private bool FinishMixedSave()
    {
        var pending=pendingMixedSave!;
        if(scene2WorkspaceClosed||Scene2OwnerCurrent?.Invoke(this)==false){pending.Dispose();pendingMixedSave=null;Said="This workspace is no longer current; the save group was cancelled.";return false;}
        var failed=pending.Complete();pendingMixedSave=null;
        Said=failed.Count==0?$"Saved {pendingMixedCount} files.":string.Join(" ",failed.SelectMany(f=>f.Problems).Select(p=>p.Message).Distinct(StringComparer.Ordinal));return failed.Count==0;
    }

    private sealed record PendingSave(DataFile File, object? Session, long? Revision, Task<string?> Verdict);
    private readonly Dictionary<string, PendingSave> pendingSaves = new(StringComparer.Ordinal);
    private readonly HashSet<string> approved = new(StringComparer.Ordinal);

    // What a window asks before it saves: why not, or null to go ahead.
    private string? BeforeSaving(DataFile file, object session)
    {
        // The window must still be editing the file's own session: a file closed (and not opened again) has none, and its old
        // window's draft is no longer the file's to save (found by A186).
        if (!ReferenceEquals(file.Session, session))
            return $"{file.Name} was closed, so this window's edits aren't its edits any more; nothing was saved. Open it again to edit and save it.";
        if (file.ReadOnlyReason is { } why) return $"{file.Name} wasn't saved: {why}.";
        if (!file.Dirty) return null;
        if (!BackgroundSaves || Project.SaveCheck is null)
            return Project.AskSaveCheck(file) is { } refused ? $"{file.Name} wasn't saved: {refused}" : null;
        if (approved.Contains(file.Name)) return null;   // its check passed a moment ago, for exactly this version
        if (pendingSaves.ContainsKey(file.Name)) return $"{file.Name} is still being checked before saving; it saves itself when the check passes.";
        var revision = file.Session?.Revision;
        pendingSaves[file.Name] = new(file, file.Session, revision, Project.AskSaveCheckInBackground(file));
        Said = $"Checking {file.Name} before saving. It saves itself when the check passes; keep working.";
        return Said;
    }

    /// <summary>Raised synchronously on the caller's owner thread after each FinishSaves pump, including pumps with no
    /// pending saves. Draw pumps once per frame; headless hosts call FinishSaves themselves. Observer exceptions are
    /// traced and contained; they cannot undo a completed save or prevent later observers from running.</summary>
    public event Action? Ticked;

    /// <summary>Finishes background saves whose check has answered: each is saved through its window's own save, or says why it
    /// wasn't (<see cref="Said"/>). <see cref="Draw"/> calls it every frame; a host that draws no frames calls it itself.</summary>
    public void FinishSaves()
    {
        if(pendingMixedSave is {} mixed&&mixed.Verdict.IsCompleted)FinishMixedSave();
        foreach (var (name, pending) in pendingSaves.Where(p => p.Value.Verdict.IsCompleted).ToList())
        {
            pendingSaves.Remove(name);
            var verdict = pending.Verdict.IsCompletedSuccessfully ? pending.Verdict.Result
                : $"the check before saving failed ({pending.Verdict.Exception?.GetBaseException().Message}), so nothing was written. Your edits are still here.";
            if (verdict is not null) { Said = $"{name} wasn't saved: {verdict}"; continue; }
            // The same session, at the same revision: a file closed and opened again starts a new session, whose revision can
            // match the checked one by chance, so the number alone isn't enough (found by A186).
            if (!ReferenceEquals(pending.File.Session, pending.Session) || pending.File.Session?.Revision != pending.Revision) { Said = $"{name} wasn't saved: it changed while it was being checked. Save again to have this version checked."; continue; }
            if (!windows.TryGetValue(name, out var window)) { Said = $"{name} wasn't saved: its window has closed. Your edits are still here."; continue; }
            approved.Add(name);
            try { Said = window.Run("editor.save") ? $"Saved {name}." : window.Status; }
            finally { approved.Remove(name); }
        }
        if (Ticked is { } observers)
            foreach (Action observer in observers.GetInvocationList())
                try { observer(); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError($"A workspace tick observer failed: {error}"); }
    }

    private (bool Success, string Message) SaveOwnedFile(DataFile file, object session)
    {
        var revision = file.Session?.Revision;
        if (BeforeSaving(file, session) is { } refusal) return (false, refusal);
        // BeforeSaving owns host checks (including background approvals); do not run them a second time.
        // Recheck disk/access immediately before the owning DataFile performs its one write and notification.
        var why = file.Dirty ? file.WhyNotNow(revision) : file.WhyNotSaved();
        if (why is not null) return (false, why);
        if (!file.Dirty) return (true, $"Saved {file.Name}.");
        var saved = file.Write();
        return (saved, saved ? $"Saved {file.Name}." : string.Join(" ", file.Problems.Select(p => p.Message)));
    }

    private (bool Saved, string Message) SaveNestedCopy(DataFile source, string path)
    {
        var name = Path.GetRelativePath(Project.DataFolder, path).Replace('\\', '/');
        if (!Project.CreateFromText(name, source.Session!.Format.Write(source.Session.Current), out var why))
            return (false, why);
        // The source draft and its undo history remain owned by its original file. The saved copy
        // gets the target's schema/rules and its own ordinary managed session, rather than retargeting the source validator.
        var shown = Show(name);
        return (true, shown ? $"Saved copy as {name}. The original draft is still open."
            : $"Saved copy as {name}, but it could not be shown: {Said}. The original draft is still open.");
    }

    /// <summary>Draws the shown file's editor, docked; with no file shown, just the file list.</summary>
    public void Draw()
    {
        FinishSaves();
        PrepareRecentProjectsUi();
        PrepareExportUi();
        PreparePlaySettingsUi();
        DrawWorkspaceStatusBar();
        if(Scene2Window is not null&&Active is {} v2File)
        {var v2=scene2Windows[v2File.Name];v2.Owner=this;v2.Draw();DrawCloseChoices();scenePlacement?.FinishFrame();return;}
        if (SceneWindow is { } scene)
        {
            // The view is shared by scene authority, but the visible workspace owns this frame's admitted source.
            if (SceneViewport is { } view)
            {
                view.DropTarget = scenePlacement is { } placement ? placement.DrawTarget : null;
                if (view.Source is SceneCatalogSource source) source.Catalog = SceneAssets;
                view.MaterialCatalog = SceneAssets;
            }
            AttachSceneFiles(scene.Editor);
            scene.DrawDocked();
            DrawCloseChoices();
            scenePlacement?.FinishFrame();
            return;
        }
        else if (Window is { } w) { w.DrawDocked(); DrawCloseChoices(); scenePlacement?.FinishFrame(); return; }
        ImGui.SetNextWindowSize(new float2(FilesPanel.Width, FilesPanel.Height), ImGuiCond.FirstUseEver);
        if (Ui.Begin("Files###panel.project.files"u8)) DrawFiles();
        Ui.End();
        DrawCloseChoices();
        scenePlacement?.FinishFrame();
    }

    private EditorWindow<DataDocument> Make(DataFile file)
    {
        AdapterProblem = null;
        var schemas = Project.Schemas;
        var outline = Map is { } m ? DataEditing.OutlineFor(schemas, file.Schema!, (id, item) => m.Label(file.Name, id, item)) : DataEditing.OutlineFor(schemas, file.Schema!);
        var editor = new EditorModel<DataDocument>(file.Session!, outline);
        // One game at a time in the workspace, so the one running can always be found and stopped (B4).
        editor.PlayRefusal = () => Playing is { } other && !ReferenceEquals(other, editor)
            ? $"'{other.Playing!.Title}' is playing in {Project.Files.FirstOrDefault(f => ReferenceEquals(f.Session, other.Session))?.Name ?? "another file"}; stop it first"
            : null;
        var type = DataEditing.ObjectType(schemas, file.Schema!);
        var table = new DataTableView(editor, type)
        {
            References = DataEditing.SchemaReferences(schemas, file.Schema!),
            File = file.Name,
            Links = Map is { } map ? (at, value) => map.Resolve(Project, file.Name, at, value) is { Outcome: ReferenceOutcome.Found, Target: { } t } ? t : null : null,
            LinkProblem = Map is { } why ? (at, value) => Problem(why.Resolve(Project, file.Name, at, value)) : null,
            GoTo = GoTo,
            BackElsewhere = Back,
            CanBackElsewhere = () => history.Count > 0,
        };
        tables[file.Name] = table;
        editor.Add(FilesPanel).Add(table.AsPanel()).Add(FindReplacePanel);
        var problems = new List<string>();
        foreach (var extra in FilePanels)
        {
            try { if (extra(file, editor) is { } panel) editor.Add(panel); }
            catch (Exception e) { problems.Add($"A panel couldn't be set up for {file.Name}: {e.Message}. That panel is missing; your data is safe."); }
        }
        if (Map is not null) editor.Add(ReferencesPanel);
        editor.Add(ChecksPanel).Add(ProposalsPanel with { ShowWhen = () => PendingSuggestions() > 0 || editor.IsOpen("project.proposals") });
        // A game's panels are extras: a fault in setting them up costs those panels, never the file (found by A073).
        try { configure?.Invoke(editor, table); }
        catch (Exception e) { problems.Add($"The game adapter couldn't finish setting up {file.Name}: {e.Message}. Its panels may be missing; your data is safe."); }
        try { ConfigureFile?.Invoke(this, file, editor, table); }
        catch (Exception e) { problems.Add($"The game adapter couldn't finish setting up {file.Name}: {e.Message}. Its panels may be missing; your data is safe."); }
        if (problems.Count > 0) AdapterProblem = string.Join(" ", problems);
        var window=new EditorWindow<DataDocument>(editor, new Inspector<DataDocument>(editor).Add(type), shortcuts)
        {
            Searcher = (document, text) => DataSearch.Find(document, text),
            SaveRefusal = () => BeforeSaving(file, editor.Session),
            SaveAction = () => SaveOwnedFile(file, editor.Session),
            ManagedSaveAs = Project.Recursive ? path => SaveNestedCopy(file, path) : null,
            CopyObject = id =>
            {
                if (!ReferenceEquals(file.Session, editor.Session) || editor.Outline.Label(editor.Session.Current,id) is null)
                    return false;
                return table.CopyRow(id) is not null;
            },
            RevealInTable = id =>
            {
                if (!ReferenceEquals(file.Session, editor.Session) || editor.Outline.Label(editor.Session.Current,id) is null)
                    return false;
                if (!GoTo(new ReferenceTarget(file.Name,id))) return false;
                editor.SetOpen("editor.table",true);
                return true;
            },
        };
        BindProblemReport(window);
        BindLinked(window,file.Name);
        return window;
    }

    private string? droppedFile;

    /// <summary>Stages one dropped JSON file for local confirmation. The host must first select the Files window
    /// using the last completed UI tree bounds. This receiver never moves or deletes the source and does not copy
    /// anything until the person confirms in the Files panel. Other drop targets use their own receivers.</summary>
    public bool DropOnFiles(IReadOnlyList<string> paths)
    {
        if (form != FileForm.None) { Said = "Finish or cancel the current file action before dropping another file."; return false; }
        if (paths.Count != 1) { Said = "Drop one JSON data file on Files at a time."; return false; }
        if (Project.ReadOnly) { Said = "The project is open read-only; a file can't be copied into it."; return false; }
        try
        {
            var path = Path.GetFullPath(paths[0]);
            if (!path.EndsWith(".json", StringComparison.Ordinal) || !File.Exists(path))
            { Said = "Drop an existing .json data file on Files. Folders and other files aren't supported here."; return false; }
            droppedFile = path;
            form = FileForm.Copy;
            formOpener = FileAction.Refresh;
            Said = $"Copy {Path.GetFileName(path)} into this project's data folder?";
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        { Said = $"That file can't be copied: {e.Message}"; return false; }
    }

    private bool ConfirmDroppedFile()
    {
        if (droppedFile is not { } source) return false;
        string text;
        try { text = Canonical.ReadText(source); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DocumentFormatException)
        { Said = $"The source file can't be read: {e.Message}"; return false; }
        var name = Path.GetFileName(source);
        if (!Project.CreateFromText(name, text, out var why)) { Said = why; return false; }
        droppedFile = null;
        form = FileForm.None;
        Show(name);
        Said = why;
        Refocus(file: name);
        return true;
    }

    // The open New, Rename or Delete step: a name (and schema), then one choice to do it or cancel. Delete asks once more,
    // naming the file, since it changes the folder.
    private void DrawForm()
    {
        if (form == FileForm.None) return;
        if(ImGui.Internal.IsKeyPressed(ImGuiKey.Escape,0,new ImGuiID(uint.MaxValue)))
        {form=FileForm.None;droppedFile=null;Refocus(button:formOpener);return;}
        ImGui.Separator();
        if (form == FileForm.Copy)
        {
            Ui.TextWrapped($"Copy {Path.GetFileName(droppedFile ?? "")} into this project's data folder?");
            if (Ui.SmallButton("Copy it"u8)) ConfirmDroppedFile();
        }
        else if (form == FileForm.Delete)
        {
            Ui.TextWrapped($"Delete {Active?.Name ?? "nothing"}? Restore puts it back until the project is closed.");
            if (formLinks > 0) Ui.TextWrapped($"{formLinks} link{(formLinks == 1 ? "" : "s")} in other files point into it, and would name nothing.");
            if (Ui.SmallButton("Delete it"u8) && Active is { } shown && DeleteFile(shown.Name)) { form = FileForm.None; Refocus(button: FileAction.Restore); }
        }
        else
        {
            if (form == FileForm.Rename && formLinks > 0)
                Ui.TextWrapped($"{formLinks} link{(formLinks == 1 ? "" : "s")} in other files point into it: the reference map names it by file, so update the map too.");
            ImGui.SetNextItemWidth(-1);
            if (focusForm) { ImGui.SetKeyboardFocusHere(); focusForm = false; }
            var enter = Ui.InputText("##filename"u8, nameText, ImGuiInputTextFlags.EnterReturnsTrue);
            nameText.EvaluateLength();
            var name = nameText.ToString();
            if (form == FileForm.New)
            {
                var schemas = Project.SchemaNames;
                if (!schemas.Contains(newSchema)) newSchema = schemas.FirstOrDefault() ?? "";
                ImGui.SetNextItemWidth(-1);
                if (Ui.BeginCombo("##newschema"u8, newSchema))
                {
                    foreach (var s in schemas) if (Ui.Selectable(s, s == newSchema)) newSchema = s;
                    Ui.EndCombo();
                }
                if ((Ui.SmallButton("Make it"u8) || enter) && NewFile(name, newSchema)) { form = FileForm.None; Refocus(file: name.Trim()); }
            }
            else if ((Ui.SmallButton("Rename it"u8) || enter) && Active is { } shown && RenameFile(shown.Name, name)) { form = FileForm.None; Refocus(file: Active?.Name ?? name.Trim()); }
        }
        if (form == FileForm.None) { ImGui.Separator(); return; }
        ImGui.SameLine();
        if (Ui.SmallButton("Cancel"u8) || ImGui.IsKeyPressed(ImGuiKey.Escape)) { form = FileForm.None; droppedFile = null; Refocus(button: formOpener); }
        ImGui.Separator();
    }

    /// <summary>Local review and acceptance. Remote tools cannot activate this UI capability.</summary>
    public Panel ProposalsPanel => new("project.proposals", "Suggested changes", DrawProposals, OpenAtStart: false, Width: 420, Height: 350, Side: PanelSide.Right);
    private int PendingSuggestions() => Proposals.List()["proposals"]?.AsArray().Count(p => p?["state"]?.GetValue<string>() == "pending") ?? 0;

    private static void DrawProposalText(string text)
    {
        // Render all characters in bounded chunks rather than silently trimming a large document.
        for (var offset = 0; offset < text.Length;)
        {
            var count = Math.Min(1000, text.Length - offset);
            if (offset + count < text.Length && char.IsHighSurrogate(text[offset + count - 1])) count--;
            Ui.TextWrapped(text.Substring(offset, count));
            offset += count;
        }
    }
    private string proposalMessage = "";
    private void DrawProposals()
    {
        Ui.TextWrapped("Review the changes and their checks before accepting. Accept changes updates the unsaved file in one undo step; it does not save."u8);
        if (proposalMessage.Length > 0) Ui.TextWrapped(Shown(proposalMessage));
        var listed = Proposals.List();
        if (listed["error"] is { } error) { Ui.TextWrapped(Shown(error.ToString())); return; }
        foreach (var item in listed["proposals"]!.AsArray())
        {
            var id = item!["id"]!.GetValue<string>();
            var digest = item["digest"]!.GetValue<string>();
            ImGui.PushID(id);
            Ui.TextWrapped($"{item["file"]}: {SuggestionState(item["state"]!.GetValue<string>())}");
            var selected = item["selectedChecks"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
            DrawProposalText("Selected checks: " + (selected.Length == 0 ? "None" : string.Join(", ", selected.Select(CheckLabel))));
            // Exact text is intentionally not truncated by Shown: review must not hide proposed bytes.
            Ui.TextWrapped("Before:"u8); DrawProposalText(item["before"]!.GetValue<string>());
            Ui.TextWrapped("After:"u8); DrawProposalText(item["after"]!.GetValue<string>());
            DrawProposalText(selected.Length == 0 ? "No checks selected. Only the file validation rules were checked." : "These results cover only the selected checks. Other checks may not have run when you saved.");
            foreach (var finding in item["validation"]!.AsArray()) DrawProposalText($"{finding!["severity"]}: {finding["message"]}");
            foreach (var check in item["checks"]!.AsArray())
            {
                DrawProposalText($"{CheckLabel(check!["id"]!.GetValue<string>())}: {check["state"]} {check["summary"]} {check["problem"]}");
                foreach (var finding in check["findings"]!.AsArray()) DrawProposalText($"{finding!["severity"]}: {finding["message"]}");
            }
            if (item["blocked"] is { } blocked) Ui.TextWrapped(Shown(SuggestionProblem(blocked.ToString())));
            if (item["state"]!.GetValue<string>() == "pending")
            {
                Ui.BeginDisabled(item["blocked"] is not null, item["blocked"] is { } reason ? SuggestionProblem(reason.ToString()) : null);
                if (Ui.SmallButton("Accept changes"u8)) proposalMessage = SuggestionResult(Proposals.AcceptLocal(id, digest), "accepted", "Changes added to the unsaved file.");
                Ui.EndDisabled();
                ImGui.SameLine();
                if (Ui.SmallButton("Reject changes"u8)) proposalMessage = SuggestionResult(Proposals.Reject(id, digest), "rejected", "Suggestion rejected.");
            }
            ImGui.Separator(); ImGui.PopID();
        }
    }

    private string CheckLabel(string id) => Checks.Registered.FirstOrDefault(c => c.Id == id)?.Label ?? "Unavailable check";
    private static string SuggestionState(string state) => state switch
    {
        "pending" => "Waiting for review", "accepted" => "Added to the unsaved file", "rejected" => "Rejected", _ => state
    };
    private static string SuggestionProblem(string problem) => problem switch
    {
        "The proposal base session or revision is stale; submit a new proposal." => "This file changed after the suggestion was made. Ask for a new suggestion.",
        "Project inputs changed since proposal checks; submit a new proposal." => "The project changed after these checks. Ask for a new suggestion.",
        "Selected proposal checks are still running." => "The selected checks are still running. Wait for their results.",
        "Proposal id/digest does not match." => "The suggestion no longer matches the one reviewed. Review it again.",
        _ => problem
    };
    private static string SuggestionResult(System.Text.Json.Nodes.JsonObject result, string expected, string success) =>
        result["error"] is { } error ? SuggestionProblem(error.ToString())
            : result["state"]?.GetValue<string>() == expected ? success : "The suggestion was not changed. Review it again.";

    private void DrawChecks()
    {
        foreach (var check in Checks.Registered)
        {
            ImGui.PushID(check.Id);
            var run = Checks.Last(check.Id);
            OptionsForCheck(check.Id)?.Draw();
            if (run is { IsOver: false })
            {
                if (Ui.SmallButton("Cancel"u8)) run.Cancel();
            }
            else
            {
                var allowed = CanRunCheck(check.Id, out var why);
                Ui.BeginDisabled(!allowed, why);
                if (Ui.SmallButton("Run"u8)) RunCheck(check.Id);
                Ui.EndDisabled();
                if (!allowed) ImGui.SetItemTooltip(why);
            }
            var verdict = run is null ? "Not run yet" : CheckVerdict(check.Id);
            var stale = verdict.EndsWith(" (stale)", StringComparison.Ordinal);
            if (!stale) ImGui.SameLine();
            Ui.TextWrapped(stale ? $"{check.Label}: {verdict[..^8]}\nResults are out of date" : $"{check.Label}: {verdict}");
            ImGui.SetItemTooltip(check.Description);
            if(run is {IsOver:true})
            {
                if (run.IsStale(Project, PanelFreshness)) Ui.TextWrapped("Results are out of date because the project changed.\nRun again."u8);
                else if (OptionsStale(run)) Ui.TextWrapped("Results are out of date because the check options changed.\nRun again."u8);
            }
            if (run?.FinishedAt is { } finished)
            {
                // Display-only wall-clock age; staleness and scheduling remain independent.
                var seconds = Math.Max(0, (DateTimeOffset.UtcNow - finished).TotalSeconds);
                Ui.TextDisabled($"Last result recorded {seconds:0} seconds ago.");
            }
            else Ui.TextDisabled(run is null ? "Not run yet." : run.IsOver ? "No result time was recorded." : "Waiting for results.");
            if (run is { IsOver: false, Progress: { } progress }) Ui.TextDisabled(progress.Message);
            if (run is { IsOver: true })
            {
                if (run.Problem is { } problem) Ui.TextWrapped(Shown(problem));
                else if (run.Report is { } report)
                {
                    Ui.TextDisabled(Shown(report.Summary));
                    var shown = FindingsShown(check.Id);
                    for (var i = 0; i < shown; i++)
                    {
                        var f = report.Findings[i];
                        var where = f.Place is { } p ? $"  ({p.File} {p.At ?? p.Item})" : "";
                        if (Ui.Selectable($"{(f.Problem.Severity == Severity.Error ? "Error" : "Warning")}: {Shown(f.Problem.Message)}{Shown(where)}##finding.{i}")) Reveal(f);
                        if (f.Next is { Length: > 0 } next) ImGui.SetItemTooltip(Shown(next));
                    }
                    Ui.TextDisabled($"Showing {shown} of {report.Findings.Count}.");
                    if (shown < report.Findings.Count)
                    {
                        ImGui.SameLine();
                        if (Ui.SmallButton($"Show {Math.Min(FindingsStep, report.Findings.Count - shown)} more")) ShowMoreFindings(check.Id);
                    }
                }
            }
            ImGui.Separator();
            ImGui.PopID();
        }
        Ui.TextWrapped("Some checks may not have run when you saved."u8);
    }

    /// <summary>How many of the last run's findings the Checks panel shows: a long list comes a step at a time, so
    /// thousands of findings can't slow the frame, and the panel always says how many there are.</summary>
    public int FindingsShown(string id)
    {
        if (Checks.Last(id) is not { Report: { } report } run) return 0;
        return Math.Min(findingsShown.TryGetValue(id, out var seen) && ReferenceEquals(seen.Run, run) ? seen.Count : FindingsStep, report.Findings.Count);
    }

    /// <summary>Shows the next step of findings for the check <paramref name="id"/>'s last run.</summary>
    public void ShowMoreFindings(string id)
    {
        if (Checks.Last(id) is { } run) findingsShown[id] = (run, FindingsShown(id) + FindingsStep);
    }

    private const int FindingsStep = 100;

    /// <summary>The most of one message the Checks panel draws. A check's message can be any length, and a frame's text has a limit
    /// (found by A199: a 12 MB message stopped the panel). The rest is said to be there; the whole finding stays in the result, and
    /// an AI's <c>checkResult</c> gives it in full.</summary>
    public const int MessageShown = 2000;

    private static string Shown(string? text) =>
        text is null ? "" : text.Length <= MessageShown ? text : $"{text[..MessageShown]}… ({text.Length - MessageShown:N0} more characters)";
    private readonly Dictionary<string, (CheckRun Run, int Count)> findingsShown = new(StringComparer.Ordinal);

    private void DrawReferences()
    {
        if (Window?.Editor.Primary is not { } item) { Ui.TextWrapped("Select an item to see what refers to it."u8); return; }
        var from = ReferencedBy();
        var name = Window.Editor.Outline.Label(Window.Editor.Session.Current, item) ?? item;
        Ui.TextWrapped(from.Count == 0 ? $"Nothing refers to {name}: removing it breaks no link the map knows."
            : $"{from.Count} link{(from.Count == 1 ? "" : "s")} to {name}; changing its id would break {(from.Count == 1 ? "it" : "them")}.");
        ImGui.Separator();
        for (var i = 0; i < from.Count; i++)
        {
            var r = from[i];
            if (Ui.Selectable($"{r.Label}  ({r.At.File} {r.At.Pointer})##ref.{i}")) { GoTo(r.At); return; }
        }
    }

    // INVENTED (A179 rank 10): keep the action descriptors while button availability is unchanged.
    // Actions still read the live workspace when invoked; only Close captures the callback, as before.
    // One retained list per workspace; no new thread-safety or public API contract.
    private (bool ReadOnly, bool Active, bool Deleted, Func<bool>? Close)? fileActionsFor;
    private IReadOnlyList<(FileAction Id, string Label, Action Run)> fileActions = [];

    private IReadOnlyList<(FileAction Id, string Label, Action Run)> FileActions()
    {
        var readOnly = Project.ReadOnly;
        var active = !readOnly && Active is not null;
        var deleted = !readOnly && Project.Deleted.Count > 0;
        var closeProject = CloseProject;
        if (fileActionsFor is { } known && known.ReadOnly == readOnly && known.Active == active
            && known.Deleted == deleted && ReferenceEquals(known.Close, closeProject)) return fileActions;
        var actions = BuildFileActions(readOnly, active, deleted, closeProject);
        fileActionsFor = (readOnly, active, deleted, closeProject);
        return fileActions = actions;
    }

    // Keep captured locals in a separate method so a cache hit cannot allocate their closure object.
    private IReadOnlyList<(FileAction Id, string Label, Action Run)> BuildFileActions(
        bool readOnly, bool active, bool deleted, Func<bool>? closeProject)
    {
        var actions = new List<(FileAction Id, string Label, Action Run)> { (FileAction.SaveAll, "Save all", () => SaveAll()), (FileAction.Refresh, "Refresh", Refresh) };
        if (!readOnly)
        {
            actions.Add((FileAction.New, "New", () => OpenForm(FileForm.New)));
            if (active)
            {
                actions.Add((FileAction.Rename, "Rename", () => OpenForm(FileForm.Rename)));
                actions.Add((FileAction.Delete, "Delete", () => OpenForm(FileForm.Delete)));
            }
            if (deleted) actions.Add((FileAction.Restore, "Restore", () =>
            {
                var name = Project.Deleted[^1];
                if (RestoreFile(name)) Refocus(file: name);   // the button goes with the last deleted file: focus its row
            }));
        }
        if (closeProject is { } close) actions.Add((FileAction.Close, "Close project", () => close()));
        return actions;
    }

    // One line per file: * for unsaved edits, then the name; a file that can't open or has gone says so, with the
    // reasons on hover. Choosing one shows it.
    private bool FileHasDraft(DataFile file)=>file.Dirty||Documents.TryGetOpenScene(file.Name,out var one)&&one!.Session.Dirty||Documents.TryGetOpenScene2(file.Name,out var two)&&two!.Session.Dirty;

    private void DrawFiles()
    {
        PrepareFileListFocus();
        if(Active is null)
        {
            var firstLevel=Project.Files.FirstOrDefault(file=>Documents.IsScene(file.Name));
            Ui.TextWrapped(firstLevel is null ? "Choose a file below to start editing." : "Open a level to see and move its objects.");
            if(firstLevel is not null && Ui.Button("Open first level"u8))Show(firstLevel.Name);
        }
        if (history.Count > 0 && Ui.SmallButton("Back to previous location##navigation.back"u8)) Back();
        var suggestions = PendingSuggestions();
        if (suggestions > 0 && Window is { } reviewWindow)
        {
            if (Ui.Button($"{suggestions} suggested changes waiting. Review"))
                reviewWindow.Editor.SetOpen("project.proposals", true);
        }
        Ui.TextDisabled($"{Project.Files.Count} files{(Project.ReadOnly ? ", read-only" : "")}{(Project.Dirty.Count > 0 ? $", {Project.Dirty.Count} unsaved" : "")}");
        // The actions wrap onto more rows when the panel is narrow, so none runs past its edge.
        var actions = FileActions();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < actions.Count; i++)
        {
            var width = ImGui.CalcTextSize(actions[i].Label).X + ImGui.GetStyle().FramePadding.X * 2;
            if (i > 0)
            {
                ImGui.SameLine();
                if (ImGui.GetCursorScreenPos().X + width > right) ImGui.NewLine();
            }
            if (Ui.SmallButton(actions[i].Label)) actions[i].Run();
            if (refocus?.Button == actions[i].Id) Refocused();
        }
        DrawForm();
        if (Said.Length > 0) Ui.FieldCaption(Said);
        ImGui.Separator();
        // File status checks touch the filesystem (including every ancestor of recursive paths).
        // Submit only visible rows, as tables and the outline do. The clipper also keeps keyboard
        // navigation; pending focus and popup owners must be submitted even outside the viewport.
        var files = Project.Files;
        var clipper = new ImGuiListClipper();
        clipper.Begin(files.Count, -1);
        for (var index = 0; index < files.Count; index++)
            if (refocus?.File == files[index].Name || pendingFileContext?.File == files[index].Name
                || openFileContext == files[index].Name) clipper.IncludeItemByIndex(index);
        while (clipper.Step())
        for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
        {
            var file = files[index];
            var state = file.State;
            var readOnly = file.ReadOnlyReason;
            var note = state switch
            {
                DataFileState.Refused => "  (can't open)",
                DataFileState.Missing => "  (gone from the folder)",
                DataFileState.Repairing => $"  (repairing: {file.Session!.Problems.Count} left)",
                _ when readOnly is not null && !Project.ReadOnly => "  (read-only)",
                _ => "",
            };
            // A file that doesn't fit its schema can be fixed here, by a Repair button after its name. The name's row leaves
            // room for it: a full-width name pushed the button outside the panel (found by A056).
            var repairable = state == DataFileState.Refused && file.Schema is not null;
            var style = ImGui.GetStyle();
            var room = repairable ? ImGui.CalcTextSize("Repair"u8).X + style.FramePadding.X * 2 + style.ItemSpacing.X : 0;
            if (Ui.Selectable($"{(FileHasDraft(file) ? "* " : "  ")}{file.Name}{note}##file.{file.Name}", file == Active, ImGuiSelectableFlags.None,
                    new float2(MathF.Max(1, ImGui.GetContentRegionAvail().X - room), 0)))
                Show(file.Name);
            var rowFocused = ImGui.IsItemFocused();
            FileContext(file);
            if (refocus?.File == file.Name) Refocused();
            if (repairable)
            {
                ImGui.SameLine();
                if (Ui.SmallButton($"Repair##repair.{file.Name}")) Repair(file.Name);
                // The button stands taller than the clipper's measured row, so a keyboard jump (End) can stop with it
                // hanging past the panel's edge, where it isn't drawn or clickable. Bring the whole line into view.
                if (rowFocused && ImGui.GetItemRectMax().Y > ImGui.GetWindowDrawList().GetClipRectMax().Y) ImGui.SetScrollHereY(1f);
            }
            if (file.Problems.Count > 0) ImGui.SetItemTooltip(string.Join("\n", file.Problems.Select(p => p.Message)));
            else if (readOnly is { } why) ImGui.SetItemTooltip("Read-only: " + why);
        }
        clipper.End();
        if (openFileContext is { } owner && !ImGui.IsPopupOpen("File actions##" + owner)) openFileContext = null;
        // Its target wasn't drawn on the frame after (the file failed to show, or the button went): nowhere to put it back.
        if (refocus is { } r && r.Frame < ImGui.GetFrameCount() - 1) refocus = null;
    }

    private (string File, int Frame)? pendingFileContext;
    private string? openFileContext;

    // Context routes use the same open/form handlers as the Files panel; no filesystem shortcut.
    private void FileContext(DataFile file)
    {
        var io = ImGui.GetIO();
        var requested = ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)
            || ImGui.IsItemFocused() && !io.WantTextInput && !io.KeyCtrl && !io.KeyAlt && !io.KeySuper
                && (ImGui.IsKeyPressed(ImGuiKey.Menu) || io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.F10));
        var popup = "File actions##" + file.Name;
        if (requested && form != FileForm.None)
        {
            Said = "Finish or cancel the open file form before choosing another file action.";
            return;
        }
        if (requested)
        {
            // Refused files keep the previous editor, as Show promises; actions cannot hit that old file.
            Show(file.Name);
            pendingFileContext = (file.Name, ImGui.GetFrameCount());
        }
        // Opening the first file changes from the standalone Files window to its docked panel.
        // Open on that panel's next frame so ImGui keeps the popup's correct owning window.
        if (pendingFileContext is { } pending && pending.File == file.Name && pending.Frame + 1 < ImGui.GetFrameCount() && !ImGui.IsWindowAppearing())
        {
            ImGui.OpenPopup(popup);
            openFileContext = file.Name;
            pendingFileContext = null;
        }
        if (!Ui.BeginPopup(popup)) return;
        var selected = ReferenceEquals(Active, file);
        var unavailable = selected ? null : "This file cannot be opened; the previous file remains shown.";
        if (Ui.MenuItem("Open"u8, ""u8, false, true)) Show(file.Name);
        if(Documents.IsScene(file.Name)&&Ui.MenuItem("Close"u8,"Ctrl+W"u8,false,selected,unavailable))CloseSceneView(file.Name);
        var editReason = unavailable ?? file.ReadOnlyReason;
        if (Ui.MenuItem("Rename"u8, ""u8, false, editReason is null, editReason)) OpenForm(FileForm.Rename);
        if (Ui.MenuItem("Delete"u8, ""u8, false, editReason is null, editReason)) OpenForm(FileForm.Delete);
        Ui.MenuItem("Restore"u8, ""u8, false, false, "This listed file was not deleted here; use Restore for the last deleted file.");
        Ui.MenuItem("Show in Explorer"u8, ""u8, false, false, "This workspace has no Explorer host action.");
        if (Ui.MenuItem("Copy path"u8, ""u8, false, true))
            ImGui.SetClipboardText(Path.GetFullPath(Path.Combine(Project.DataFolder, file.Name)));
        Ui.EndPopup();
    }

    private void Refocus(string? file = null, FileAction? button = null) => refocus = (file, button, ImGui.GetFrameCount());

    // The item just drawn is where the focus goes back to, from the frame after the form went, as the table does after a paste.
    private void Refocused()
    {
        if (refocus is not { } r || r.Frame >= ImGui.GetFrameCount()) return;
        ImGui.SetWindowFocus();
        ImGui.Internal.SetFocusID(ImGui.GetItemID(), ImGui.Internal.GetCurrentWindow());
        ImGui.SetNavCursorVisible(true);
        if(scrollFileFocus&&r.File is not null)ImGui.SetScrollHereY();
        scrollFileFocus=false;
        refocus = null;
    }
}
