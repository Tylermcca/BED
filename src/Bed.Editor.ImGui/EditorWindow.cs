using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// The editor window for one document (second slice, part 2). It draws the editor model: a toolbar (undo, redo, save),
/// the focus breadcrumb, an outliner, the inspector with override markers and reset, the issues for the selection, a
/// status line, and the command palette (Ctrl+Shift+P). It owns no document state: every edit goes through the model's
/// session, so validation, undo and the journal apply. Key presses come in through <see cref="HandleKey"/>, so a host
/// (or a test) decides where keys come from.
/// </summary>
public sealed partial class EditorWindow<TDoc> where TDoc : class
{
    internal Action? TakeTourAction {get;set;}
    private readonly PreferencesWindow preferences = new();
    internal readonly ProblemReportDialog ProblemReport=new();
    private readonly ImInputString paletteText = new(256, ""u8);
    private readonly InspectorController<TDoc> inspectorController;
    internal Func<IReadOnlyDictionary<string,string>>? OutlineAssetNames;
    private readonly ImInputString outlineSearch=new(512,""u8);
    private bool outlineSearchFocus,outlineHadFocus;
    private (TDoc Doc,long? Version,string? Focus,string Query,IReadOnlyDictionary<string,string> Names,IReadOnlyList<(string Id,int Depth,bool Place)> Rows)? filteredOutline;
    private static readonly IReadOnlyDictionary<string,string> EmptyOutlineNames=new Dictionary<string,string>();
    private ImGuiID outlineSearchId;
    private void ClearOutlineSearch()
    {
        outlineSearch.SetValue("".AsSpan());
        var native=ImGui.Internal.GetInputTextState(outlineSearchId);
        if(!native.IsNull())native.ReloadUserBufAndMoveToEnd();
        filteredOutline=null;outlineSelectionAnchor=null;outlineMoveTo=null;outlineSearchFocus=true;
    }
    private IReadOnlyList<(string Id,int Depth,bool Place)> FilterOutline(IReadOnlyList<(string Id,int Depth,bool Place)> rows)
    {
        var query=Text(outlineSearch).Trim();if(query.Length==0)return rows;
        var document=Editor.Session.Current;var names=OutlineAssetNames?.Invoke()??EmptyOutlineNames;
        var version=Editor.Outline.Version;var sceneOutline=ReferenceEquals(Editor.Outline,SceneEditing.Outline);var cacheable=version is not null||sceneOutline;
        if(cacheable&&filteredOutline is {} known&&ReferenceEquals(known.Doc,document)&&known.Version==version&&known.Focus==Editor.FocusedOn&&known.Query==query&&ReferenceEquals(known.Names,names))return known.Rows;
        var sceneObjects=new Dictionary<string,SceneObject>(StringComparer.Ordinal);
        if(document is Scene scene)foreach(var o in scene.Objects)sceneObjects.TryAdd(o.Id,o);
        var visible=rows.Select(r=>r.Id).ToHashSet(StringComparer.Ordinal);var keep=new HashSet<string>(StringComparer.Ordinal);
        bool Contains(string? text)=>text?.Contains(query,StringComparison.OrdinalIgnoreCase)==true;
        foreach(var row in rows)
        {
            sceneObjects.TryGetValue(row.Id,out var item);var asset=item?.Asset;
            if(!Contains(row.Id)&&!Contains(Label(row.Id))&&!Contains(asset)&&!(asset is not null&&names.TryGetValue(asset,out var name)&&Contains(name))&&!(item?.Properties.Any(p=>Contains(p.Key)||Contains(p.Value.ToString()))??false))continue;
            var current=row.Id;var visited=new HashSet<string>(StringComparer.Ordinal);
            while(current is not null&&visible.Contains(current)&&visited.Add(current))
            {keep.Add(current);current=sceneOutline&&sceneObjects.TryGetValue(current,out var parent)?parent.Parent:Editor.Outline.Parent(document,current);}
        }
        var result=rows.Where(row=>keep.Contains(row.Id)).ToArray();
        filteredOutline=cacheable?(document,version,Editor.FocusedOn,query,names,result):null;return result;
    }
    private readonly SceneDeleteUi? sceneDelete;
    internal Func<(bool Ok,string Message)>? SceneCopy,ScenePaste;
    internal Func<string?>? ScenePasteUnavailable;
    internal Func<IProjectFrameControlSession?>? ProjectFrames;
    internal Func<(bool Ok,string Message)>? SceneHide,SceneLock;
    internal Func<string,string>? OutlineBadges;
    /// <summary>Optional prefab marking per row: drawn as colour, icon or dot, and spoken in the row's accessible name.</summary>
    internal Func<string,OutlineMark?>? OutlineMarks;
    private readonly SceneRenameUi? sceneRename;
    private bool paletteRequested;   // open it on the next frame
    private bool paletteShowing;     // open now (or about to be)
    private bool paletteCloseRequested;
    private string paletteQuery = "";
    private int paletteHighlight;   // visible result selected for execution or its refusal
    private string status = "";
    private long documentAction;
    private string CommandStatusId => documentAction==0?"editor.command.status":$"editor.command.status.{documentAction}";
    private string? toolbarPlay;
    private string paletteRefusal = "";
    private float2 mainRight;   // the editor frame's top-right corner, where panels open beside it
    private float mainWidth = 900;

    /// <summary>A window over <paramref name="editor"/>.</summary>
    /// <param name="editor">The editor model.</param>
    /// <param name="inspector">The inspector, when the document has object types.</param>
    /// <param name="shortcuts">The key bindings.</param>
    /// <param name="commandFor">Turns a palette choice into a document command (for example "Add item" making a new item
    /// at the origin), or null when that command needs arguments from elsewhere.</param>
    public EditorWindow(EditorModel<TDoc> editor, Inspector<TDoc>? inspector = null, Shortcuts? shortcuts = null,
        Func<CommandInfo, EditorModel<TDoc>, ICommand<TDoc>?>? commandFor = null)
    {
        Editor = editor;
        if (!editor.Panels.Any(p => p.Id == BedAbout.Panel.Id)) editor.Add(BedAbout.Panel);
        Inspector = inspector;
        Shortcuts = shortcuts ?? new Shortcuts();
        if(editor is EditorModel<Scene> scene)
        {
            Shortcuts.Default("scene.duplicate","Ctrl+D").Default("scene.remove","Delete")
                .Default("scene.group","Ctrl+G").Default("scene.ungroup","Ctrl+Shift+G").Default("editor.rename_object","F2");
            sceneDelete=new(scene,message=>status=message,()=>ProjectPlaying?.Invoke()==true);
            sceneRename=new(scene,message=>status=message,()=>ProjectPlaying?.Invoke()==true,(id,next,revision,selection)=>
            {
                var focused=scene.FocusedOn;
                var touches=scene.Session.Current.Objects.Where(o=>o.Id==id||o.Parent==id||o.References.Values.Contains(id)).Select(o=>o.Id).ToArray();
                if(!scene.ExecuteAt(revision,[new RenameObject(id,next)],touches))
                    return(false,Explain(scene.Refused.Count>0?scene.Refused:scene.Session.LastIssues));
                if(focused is not null)scene.FocusOn(focused==id?next:focused);
                scene.Select(next);ClearOutlineSearch();return(true,$"Renamed '{id}' to '{next}'; parent and named references follow.");
            });
        }
        KeysPanel.Attach(editor, Shortcuts, () => PaletteEntries(""));
        GettingStartedPanel.Attach(editor);
        CommandFor = commandFor;
        inspectorController = new(editor, inspector, Label, GoTo, Say, Explain, message => status = message);
    }

    /// <summary>The editor model it draws.</summary>
    public EditorModel<TDoc> Editor { get; }

    /// <summary>The inspector, if any.</summary>
    public Inspector<TDoc>? Inspector { get; }

    /// <summary>The key bindings.</summary>
    public Shortcuts Shortcuts { get; }

    /// <summary>Turns a palette choice into a document command.</summary>
    public Func<CommandInfo, EditorModel<TDoc>, ICommand<TDoc>?>? CommandFor { get; }

    /// <summary>The title, with a <c>*</c> when there are unsaved edits.</summary>
    public string Title => (Editor.Session.FilePath is { } p ? Path.GetFileName(p) : "Untitled") + (Editor.Session.Dirty ? " *" : "");

    /// <summary>Optional host navigation from an Objects row to its table. The host revalidates the current row.</summary>
    public Func<string, bool>? RevealInTable { get; init; }

    /// <summary>Optional host copy of the explicitly targeted object row, independent of the table's prior cell selection.</summary>
    public Func<string, bool>? CopyObject { get; init; }
    /// <summary>Optional document-close request owned by the host, including its save decision. Never closes a project.</summary>
    public Func<bool>? CloseDocument { get; init; }
    internal Action<string>? PanelCloseRequested {get;init;}
    /// <summary>The last message shown in the status line.</summary>
    public string Status => status;

    /// <summary>Whether the palette is open (or opening on the next frame).</summary>
    public bool PaletteOpen => paletteShowing;

    /// <summary>Reserved linked mode identity; declared adapter opt-in keeps other modes available.</summary>
    public const string LinkedPlayModeId="bed.linked";
    private sealed class LinkedRegistration {public Func<IPlaySession>? Start;}
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EditorModel<TDoc>,LinkedRegistration> linkedRegistrations=new();
    private LinkedRegistration? linkedRegistration;
    private Func<IPlaySession>? linkedStart;
    private bool linkedPlayBound,linkedPlayPrompt,linkedPlayFocus,linkedPlayAllowSaved;
    private long linkedPlayRevision;
    private string? linkedPlaySaved;
    /// <summary>Whether explicit saved-file admission is waiting for Save / Don't Save / Cancel.</summary>
    public bool LinkedPlaySavePending=>linkedPlayPrompt;
    /// <summary>Registers one declared linked mode for this editor. Null producer data is handled by its workspace,
    /// never converted to a default executable or runtime schema. Returns false on repeated binding.</summary>
    public bool BindLinkedPlay(GameLaunchSpec spec,PlaySnapshotConverter converter,ILinkedPlayHost host,string documentId,Func<string?>? refusal=null)
    {
        ArgumentNullException.ThrowIfNull(spec);ArgumentNullException.ThrowIfNull(converter);ArgumentNullException.ThrowIfNull(host);
        if(linkedPlayBound) return false;
        linkedRegistration=linkedRegistrations.GetValue(Editor,_=>
        {
            var registration=new LinkedRegistration();
            Editor.Add(new PlayMode<TDoc>(LinkedPlayModeId,"Linked game",_=>registration.Start!()));
            return registration;
        });
        linkedStart=()=>
        {
            if(refusal?.Invoke() is { } refused) throw new InvalidOperationException(refused);
            if(Editor.Session.Dirty && !linkedPlayAllowSaved)
                throw new InvalidOperationException("Play runs your saved files. Choose Save, Don't Save or Cancel first.");
            var captured=PlaySnapshot.FromSession(Editor.Session,documentId,PlayInputChoice.Saved,converter);
            if(!captured.Accepted) throw new InvalidOperationException(captured.Error!.Message);
            return host.Start(spec,captured.Snapshot!);
        };
        linkedRegistration.Start=linkedStart;
        linkedPlayBound=true;toolbarPlay=LinkedPlayModeId;return true;
    }
    /// <summary>Resolves only this exact captured play request. Failed/background Save keeps the prompt and drafts;
    /// changed revisions refuse. Don't Save starts the admitted baseline without modifying the draft.</summary>
    public bool AnswerLinkedPlay(SaveChangesChoice choice)
    {
        if(!linkedPlayPrompt) return Say(false,"","No linked Play decision is pending.");
        if(choice==SaveChangesChoice.Cancel) {linkedPlayPrompt=false;return Say(false,"","Play cancelled.");}
        if(Editor.Session.Revision!=linkedPlayRevision)
        {linkedPlayPrompt=false;return Say(false,"","The file changed while choosing Play. Choose Play again.");}
        if(choice==SaveChangesChoice.Save)
        {
            // A previous asynchronous authoritative Save may have completed since the button was pressed.
            if((Editor.Session.Dirty || Editor.Session.ExpectedOnDisk is null)
                && (!Save() || Editor.Session.Dirty || Editor.Session.ExpectedOnDisk is null)) return false;
        }
        else if(choice==SaveChangesChoice.DontSave)
        {
            if(linkedPlaySaved is null) return Say(false,"","There is no saved version yet. Save explicitly before Play.");
            if(Editor.Session.ExpectedOnDisk!=linkedPlaySaved)
            {linkedPlayPrompt=false;return Say(false,"","The saved version changed. Choose Play again.");}
        }
        else return Say(false,"","Choose Save, Don't Save or Cancel.");
        linkedPlayPrompt=false;linkedPlayAllowSaved=choice==SaveChangesChoice.DontSave;
        try {linkedRegistration!.Start=linkedStart;return Say(Editor.Play(LinkedPlayModeId),"Playing saved files.",Explain(Editor.Refused));}
        finally {linkedPlayAllowSaved=false;}
    }
    private void LinkedPlaySavePrompt()
    {
        if(!linkedPlayPrompt) return;
        ImGui.SetNextWindowSize(new float2(560,0),ImGuiCond.FirstUseEver);
        if(Ui.Begin("Play saved files###linked-play-save"u8,ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.AlwaysAutoResize))
        {
            Ui.TextWrapped("Play runs your saved files"u8);
            Ui.TextWrapped("Save your changes before Play?"u8);
            if(linkedPlayFocus) {ImGui.SetKeyboardFocusHere();linkedPlayFocus=false;}
            if(Ui.Button("Save"u8)) AnswerLinkedPlay(SaveChangesChoice.Save);
            ImGui.SameLine();
            Ui.BeginDisabled(linkedPlaySaved is null,"This file hasn't been saved yet.");
            if(Ui.Button("Don't Save"u8)) AnswerLinkedPlay(SaveChangesChoice.DontSave);
            Ui.EndDisabled();ImGui.SameLine();
            if(Ui.Button("Cancel"u8)) AnswerLinkedPlay(SaveChangesChoice.Cancel);
        }
        Ui.End();
    }

    // ---- actions (keys, buttons and the palette all come here) ----

    /// <summary>Runs whatever <paramref name="pressed"/> is bound to. Returns false when it's bound to nothing.</summary>
    public bool HandleKey(Chord pressed)
    {
        if(sceneRename is {HasPending:true}&&pressed==Escape){sceneRename.Cancel();return true;}
        if(sceneDelete is {HasPending:true}&&pressed==Escape){sceneDelete.Cancel();return true;}
        if(linkedPlayPrompt && pressed==Escape) {AnswerLinkedPlay(SaveChangesChoice.Cancel);return true;}
        if(linkedPlayPrompt && pressed==Chord.Parse("Enter")) {AnswerLinkedPlay(SaveChangesChoice.Save);return true;}
        if (saveAsOpen && pressed == Escape)
        {
            saveAsOpen = false;
            return Say(true, "Not saved.", "");
        }
        if (paletteShowing && pressed == Escape)   // Escape belongs to the open palette first (A008, U08)
        {
            paletteShowing = paletteRequested = false;
            paletteCloseRequested = true;
            return Say(true, "Closed the command palette.", "");
        }
        if (Shortcuts.Action(pressed) is { } id) return InvokeUiAction(id);
        var bound = Shortcuts.InEffect().Where(kv => kv.Value == pressed).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
        if (bound.Count > 1)   // a clash does nothing, and says so (A008, U09)
            return Say(false, "", $"{pressed} is bound to {string.Join(" and ", bound)} (a clash), so it did nothing. Change one of them in the shortcuts.");
        return false;
    }

    private static readonly Chord Escape = Chord.Parse("Escape");

    /// <summary>Sets ImGui up the way the usability profile asks: keyboard navigation on, so every action
    /// is reachable without a pointer; the keyboard cursor always visible; Escape left to the editor (U08). A host calls
    /// this once after creating its ImGui context.</summary>
    public static void Configure(ImGuiIOPtr io)
    {
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;   // docking for DrawDocked
        io.ConfigNavCursorVisibleAlways = true;      // the keyboard cursor is always visible (U06)
        io.ConfigNavEscapeClearFocusItem = false;    // Escape is routed to HandleKey (palette, then focus), not swallowed (U06, U08)
    }

    /// <summary>Goes to the object an issue names: selects it, stepping out of focus first when it's outside.
    /// Returns false, with a status line saying why, when the issue names no object or the object is gone.</summary>
    public bool GoTo(Issue issue)
    {
        if (issue.ObjectId is not { } id) return Say(false, "", "That problem isn't about one object.");
        if (Editor.Outline.Label(Editor.Session.Current, id) is null) return Say(false, "", $"'{id}' isn't in the file any more.");
        if (Editor.HolderOf(id) is not { } holder) return Say(false, "", $"Nothing here holds '{id}'.");
        id = holder;   // a file's own plain field is held by the file's entry (A141)
        while (!Editor.InFocus(id) && Editor.FocusOut()) { }
        Editor.Select(id);
        return Say(true, $"Showing {Label(id)}{(issue.Path is { } path ? $" ({path})" : "")}.", "");
    }

    /// <summary>The smallest text size, as a scale of the normal size.</summary>
    public const float MinTextScale = 0.75f;

    /// <summary>The largest text size: twice normal (WCAG 2.2, 1.4.4: text resized to 200% without losing content).</summary>
    public const float MaxTextScale = 2f;

    /// <summary>How much Larger and Smaller text change it.</summary>
    public const float TextStep = 0.25f;

    /// <summary>The text size now, as a scale of the normal size: ImGui's own font scale, shared by every window (a
    /// text-size setting anyone can reach by keyboard, through the palette).</summary>
    public static float TextScale => ImGui.GetCurrentContext().IsNull() ? 1 : ImGui.GetStyle().FontScaleMain;   // no window yet: normal

    /// <summary>Sets the text size, kept between <see cref="MinTextScale"/> and <see cref="MaxTextScale"/>. False when
    /// it doesn't change.</summary>
    public static bool SetTextScale(float scale)
    {
        var next = Math.Clamp(MathF.Round(scale / TextStep) * TextStep, MinTextScale, MaxTextScale);
        if (next == TextScale || ImGui.GetCurrentContext().IsNull()) return false;
        ImGui.GetStyle().FontScaleMain = next;
        return true;
    }

    private bool InvokeUiAction(string id)
    {
        var action = UiActions().FirstOrDefault(a => a.Id == id);
        return action is null ? Run(id) : action.Invoke();
    }

    /// <summary>Runs an editor action (<c>editor.*</c>) or a document command by id. Returns false when it can't.</summary>
    public bool Run(string id)
    {
        if(id is "editor.pause_simulation" or "editor.step_simulation" or "editor.resume_simulation")
        {
            var frameCommand=id=="editor.pause_simulation"?ProjectFrameCommand.Pause:id=="editor.step_simulation"?ProjectFrameCommand.Step:ProjectFrameCommand.Resume;
            var control=ProjectFrames?.Invoke();
            return ProjectFrameActions.Refusal(control?.FrameControl,frameCommand) is {} refusal?Say(false,"",refusal):control!.RequestFrameControl(frameCommand);
        }
        if(id=="editor.build_play" && ProjectBuildAndPlayAction is {} buildProject)
            return ProjectPlayUnavailable?.Invoke(true) is {} why?Say(false,"",why):buildProject();
        if(ProjectPlayAction is {} projectPlay && id.StartsWith(PlayAction,StringComparison.Ordinal))return projectPlay();
        if(ProjectStopAction is {} stopProject && id=="editor.stop")return Say(stopProject(),"Stopping Play.","Nothing is playing.");
        if (Editor.Playing is { } playing && id is not ("editor.stop" or "editor.palette") && !id.StartsWith(PanelAction, StringComparison.Ordinal))
            return Say(false, "", $"'{playing.Title}' is playing; stop it first.");
        if(id is "scene.copy" or "scene.paste")
        {
            if(!inspectorController.TryCommitPending(out var why))return Say(false,"",why);
            if(id=="scene.paste"&&ProjectPlaying?.Invoke()==true)return Say(false,"","Stop Play before pasting objects.");
            var action=id=="scene.copy"?SceneCopy:ScenePaste;
            if(action is null)return Say(false,"","Object copy and paste needs an open project.");
            var result=action();return Say(result.Ok,result.Message,result.Message);
        }
        switch (id)
        {
            case "editor.hide_objects":
            case "editor.lock_objects":
                var viewChoice=(id=="editor.hide_objects"?SceneHide:SceneLock)?.Invoke()??(false,"Scene view choices are unavailable here.");
                status=viewChoice.Item2;return viewChoice.Item1;
            case "editor.rename_object":
                if(!inspectorController.TryCommitPending(out var pendingWhy))return Say(false,"",pendingWhy);
                return sceneRename?.Request()??Say(false,"","This file has no scene objects to rename.");
            case "editor.stop":
                if(Editor.Game is LinkedPlayPreview linked && !linked.HasCompleted)
                    return Say(linked.RequestStop(),"Stopping linked Play.","Linked Play is finishing; waiting for owned resources.");
                return Say(Editor.Stop(), "Stopped; back to editing.", "Nothing is playing.");
            case not null when id.StartsWith(PlayAction, StringComparison.Ordinal):
                var mode = Editor.PlayModes.FirstOrDefault(m => PlayAction + m.Id == id);
                if(mode?.Id==LinkedPlayModeId && linkedPlayBound) linkedRegistration!.Start=linkedStart;
                if(mode is not null && mode.Id==LinkedPlayModeId && linkedPlayBound
                    && (Editor.Session.Dirty || Editor.Session.ExpectedOnDisk is null))
                {
                    linkedPlayPrompt=true;linkedPlayRevision=Editor.Session.Revision;linkedPlaySaved=Editor.Session.ExpectedOnDisk;linkedPlayFocus=true;
                    return Say(true,"Play runs your saved files. Choose Save, Don't Save or Cancel.","");
                }
                return mode is null ? Say(false, "", $"There's no play mode '{id[PlayAction.Length..]}'.")
                    : Say(Editor.Play(mode.Id), $"Playing {mode.Title}.", Explain(Editor.Refused));
            case "editor.text_larger": return Say(SetTextScale(TextScale + TextStep), $"Text at {TextScale:P0}.", "The text is as large as it goes.");
            case "editor.text_smaller": return Say(SetTextScale(TextScale - TextStep), $"Text at {TextScale:P0}.", "The text is as small as it goes.");
            case "editor.text_reset": return Say(SetTextScale(1), "Text at its normal size.", "The text is already its normal size.");
            case "editor.undo": return HistoryAction(false);
            case "editor.redo": return HistoryAction(true);
            case "editor.reset_layout": resetLayout = true; return Say(true, "Layout reset.", "");
            case "editor.close": return CloseDocument?.Invoke() ?? Say(false,"","This editor does not provide a close-file action.");
            case "editor.save": return Save();
            case "editor.file_save_as": if(SaveAsUnavailable?.Invoke() is {} saveAsWhy)return Say(false,"",saveAsWhy);OpenSaveAs(); return Say(true, "Choose where to save it.", "");
            case "editor.palette": paletteRequested = paletteShowing = true; return true;
            case "editor.focus_out": return Say(Editor.FocusOut(), "Stepped out.", "Already showing everything.");
            case not null when id.StartsWith(PanelAction, StringComparison.Ordinal) && Editor.Panels.FirstOrDefault(p => PanelAction + p.Id == id) is { } panel:
                var open = !Editor.IsOpen(panel.Id);
                Editor.SetOpen(panel.Id, open);
                return Say(true, $"{(open ? "Showing" : "Hid")} {panel.Title}.", "");
            case "editor.focus_in":
                return Editor.Primary is { } p ? Say(Editor.FocusOn(p), $"Focused on {Label(p)}.", "Can't focus there.") : Say(false, "", "Select something to focus on.");
            case not null when id.StartsWith(PanelCommandAction, StringComparison.Ordinal):
                foreach (var host in Editor.Panels)
                    foreach (var c in host.Commands ?? [])
                        if (PanelCommandAction + host.Id + "/" + c.Id == id)
                        {
                            if (c.Unavailable?.Invoke() is { } why) return Say(false, "", $"{c.Label}: {why}.");
                            Editor.SetOpen(host.Id, true);
                            return Say(c.Run(), c.Label.TrimEnd('.'), $"{c.Label.TrimEnd('.')}: it didn't work; {host.Title} says why.");
                        }
                return Say(false, "", $"There's no panel action '{id[PanelCommandAction.Length..]}'.");
        }
        if(Editor is EditorModel<Scene> groupEditor&&id is "scene.group" or "scene.ungroup")
        {
            if(ProjectPlaying?.Invoke()==true)return Say(false,"","Stop Play before grouping or ungrouping objects.");
            if(!inspectorController.TryCommitPending(out var why))return Say(false,"",why);
            var result=id=="scene.group"?SceneGroupUi.Group(groupEditor):SceneGroupUi.Ungroup(groupEditor);
            return Say(result.Ok,result.Message,result.Message);
        }
        var info = Editor.Session.Catalog.Commands.FirstOrDefault(c => c.Id == id);
        if (info is null) return Say(false, "", $"There's no command '{id}'.");
        if ((CommandFor?.Invoke(info, Editor) ?? Editor.Make(info.Id)) is not { } command) return Say(false, "", $"{info.Label} needs details: use the inspector.");
        if(sceneDelete is not null&&command is RemoveObject remove)return sceneDelete.Request(remove);
        if(Editor is EditorModel<Scene> sceneEditor&&command is DuplicateGroup duplicate)
        {
            if(ProjectPlaying?.Invoke()==true)return Say(false,"","Stop Play before duplicating objects.");
            var duplicated=SceneDuplicateUi.Run(sceneEditor,duplicate,out var why);
            return Say(duplicated,why,why);
        }
        var ok = Editor.Execute(command);
        return Say(ok, $"{info.Label}: done.", Explain(Editor.Refused.Count > 0 ? Editor.Refused : Editor.Session.LastIssues));
    }

    /// <summary>Why saving isn't allowed right now, or null when it is: a project opened read-only, say. Checked by
    /// The default Save route and Save as check this before anything is written.</summary>
    public Func<string?>? SaveRefusal { get; set; }

    /// <summary>Optional authoritative Save route, including its own refusal and conflict checks.
    /// When set, replaces the default Save path and SaveRefusal for Save only; Save as is unchanged.
    /// A result is final: no second session write follows either success or refusal.</summary>
    public Func<(bool Success, string Message)>? SaveAction { get; set; }
    /// <summary>Optional workspace-owned export command, shown only for declared adapters.</summary>
    public Action? ExportBuildAction {get;set;}
    /// <summary>Opens the project's Gum screens in the person's own Gum tool; null hides File > Edit screens in Gum.</summary>
    public Action? OpenGumAction {get;set;}
    /// <summary>Opens the Gum screen panel, a live picture of one of the project's Gum screens; null hides File > Show Gum screens.</summary>
    public Action? ShowGumScreenAction {get;set;}
    internal Action? RecentProjectsMenu {get;set;}
    /// <summary>Workspace-owned project Play settings, independent of adapter availability.</summary>
    public Action? PlaySettingsAction {get;set;}
    /// <summary>Workspace-owned saved-files Play request. Null retains the legacy host route.</summary>
    public Func<bool>? ProjectPlayAction {get;set;}
    /// <summary>Workspace-owned stop request.</summary>
    public Func<bool>? ProjectStopAction {get;set;}
    /// <summary>Whether the workspace still owns a running or stopping process.</summary>
    public Func<bool>? ProjectPlaying {get;set;}
    internal Func<bool>? ProjectBuildAndPlayAction {get;set;}
    internal Func<bool,string?>? ProjectPlayUnavailable {get;set;}


    private bool Save()
    {
        if (SaveAction is { } save)
        {
            var result = save();
            return DocumentAction(result.Success, result.Message, result.Message);
        }
        if (SaveRefusal?.Invoke() is { } refused) return Say(false, "", refused);
        if (Editor.Session.FilePath is null)
        {
            OpenSaveAs();
            return Say(false, "", "This hasn't been saved to a file yet; choose where to save it.");
        }
        // Never write over a change someone else made to the file since it was opened or last saved (found by A031).
        if (Editor.Session.CheckDisk() == DiskState.Missing)   // never silently put back a file deleted outside (found by A049)
            return Say(false, "", $"{Path.GetFileName(Editor.Session.FilePath)} was deleted outside the editor; nothing was saved. Use Save as to keep your version.");
        if (Editor.Session.CheckDisk() == DiskState.ChangedOnDisk)
            return Say(false, "", $"{Path.GetFileName(Editor.Session.FilePath)} was changed outside the editor since you opened it; nothing was saved. Use Save as to keep your version under another name, or close without saving to keep theirs.");
        try
        {
            Editor.Session.Save();
            return DocumentAction(true, $"Saved {Path.GetFileName(Editor.Session.FilePath)}.", "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Says which file, and what to do: a message that names neither leaves a person stuck (found by A102).
            return Say(false, "", $"Couldn't save {Path.GetFileName(Editor.Session.FilePath)}: {e.Message} If another program has it open, close that and save again. Your edits are still here.");
        }
    }

    // An owning workspace can keep Save as within its admission and per-file policy.
    internal Func<string, (bool Saved, string Message)>? ManagedSaveAs { get; init; }
    internal Func<string?>? SaveAsUnavailable {get;init;}

    /// <summary>Saves to <paramref name="path"/> (relative paths are from the current folder), which becomes the
    /// document's file. Returns false, saying why, when it can't (a game is playing, the path is empty or unwritable);
    /// the edits stay either way.</summary>
    public bool SaveAs(string path)
    {
        if (Editor.Playing is { } playing) return Say(false, "", $"'{playing.Title}' is playing; stop it first.");   // found by Codex in A022
        if (SaveRefusal?.Invoke() is { } refused) return Say(false, "", refused);
        if (string.IsNullOrWhiteSpace(path)) return Say(false, "", "Type a file name to save to.");
        try
        {
            var full = Path.GetFullPath(path.Trim());
            if (ManagedSaveAs is { } managed)
            {
                var result = managed(full);
                if (result.Saved) saveAsOpen = false;
                return Say(result.Saved, result.Saved ? result.Message : "", result.Saved ? "" : result.Message);
            }
            Editor.Session.Save(full);
            saveAsOpen = false;
            return Say(true, $"Saved to {Path.GetFileName(full)}.", "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Say(false, "", $"Couldn't save to {path.Trim()}: {e.Message} Your edits are still here.");
        }
    }

    /// <summary>Whether the "Save as" row is showing.</summary>
    public bool SaveAsOpen => saveAsOpen;

    private void OpenSaveAs()
    {
        saveAsOpen = true;
        saveAsText = new ImInputString(1024, Editor.Session.FilePath ?? "untitled.json");
        saveAsFocus = true;
    }

    private bool saveAsOpen, saveAsFocus;
    private ImInputString saveAsText = new(1024, ""u8);

    // The "Save as" row under the toolbar: a path, Save and Cancel. Enter saves; Escape cancels.
    private void SaveAsRow()
    {
        if (!saveAsOpen) return;
        Ui.Text("Save as:"u8);
        if (saveAsFocus)
        {
            ImGui.SetKeyboardFocusHere();
            saveAsFocus = false;
        }
        ImGui.SetNextItemWidth(-1);
        Ui.NameNextItem("##saveas"u8, "Save as path");
        var entered = Ui.InputText("##saveas"u8, saveAsText, ImGuiInputTextFlags.EnterReturnsTrue);
        if (AccessKeys.Button("Save##saveas", 'S') || entered) SaveAs(Text(saveAsText));
        ImGui.SameLine();
        if (AccessKeys.Button("Cancel##saveas", 'C')) saveAsOpen = false;
    }

    // What a text field holds now. ImGui edits the bytes natively; the managed length is re-read first so the text is
    // never stale (found by Codex in A019).
    private static string Text(ImInputString field)
    {
        field.EvaluateLength();
        return field.ToString();
    }

    private bool Say(bool ok, string done, string failed)
    {
        status = ok ? done : failed;
        ProblemReportLog.Record(ok?ReportEvent.OperationAccepted:ReportEvent.OperationRefused);
        return ok;
    }

    // A new semantic identity per completed action repeats an identical result once, never once per frame.
    private bool DocumentAction(bool ok,string done,string failed)
    {if(ok)documentAction++;return Say(ok,done,failed);}

    private bool HistoryAction(bool redo)
    {
        var session=Editor.Session;
        // The bounded journal can start with older snapshots whose command names are no longer available.
        // Preserve those positions as unknown rather than guessing a newer command's name.
        var basis=session.JournalBase.History();
        var undo=new LinkedList<string?>(Enumerable.Repeat<string?>(null,basis.UndoCount));
        var forward=new Stack<string?>();var baseDocument=session.JournalBase.Document;
        while(basis.TryRedo(baseDocument,out var later)){forward.Push(null);baseDocument=later;}
        var labels=session.Catalog.Commands.ToDictionary(c=>c.Id,c=>c.Label,StringComparer.Ordinal);
        string? LabelOf(JournalEntry entry)
        {
            if(entry.Command is {} id)return labels.GetValueOrDefault(id);
            var children=entry.Batch??[];
            if(children.Count==1)return LabelOf(children[0]);
            return children.Count>1?$"{children.Count} changes":null;
        }
        foreach(var entry in session.Journal)
            switch(entry.Op)
            {
                case "do": case "batch":
                    undo.AddLast(LabelOf(entry));if(undo.Count>session.History.Capacity)undo.RemoveFirst();forward.Clear();break;
                case "undo":
                    if(undo.Last is {} previous){forward.Push(previous.Value);undo.RemoveLast();}break;
                case "redo":
                    if(forward.TryPop(out var next)){undo.AddLast(next);if(undo.Count>session.History.Capacity)undo.RemoveFirst();}break;
            }
        var label=redo?(forward.TryPeek(out var nextLabel)?nextLabel:null):undo.Last?.Value;
        var changed=redo?session.Redo():session.Undo();
        return DocumentAction(changed,label is null?(redo?"Redid a change.":"Undid a change."):$"{(redo?"Redid":"Undid")}: {label.TrimEnd('.')}.",redo?"Nothing to redo.":"Nothing to undo.");
    }

    private string Explain(IReadOnlyList<Issue> issues) => issues.Count == 0 ? "That didn't work."
        : string.Join(" ", issues.Select(i => i.ObjectId is { } id ? $"{Label(id)} ({id}): {i.Message}" : i.Message));   // what, which, remedy (U18)

    private string Label(string id)
    {
        var doc = Editor.Session.Current;
        if (Editor.Outline.Version is not { } version) return Editor.Outline.Label(doc, id) ?? id;
        // Kept per document and outline version (found by A179: a schema-backed label walks the schema each time it's asked).
        if (labelsFor is not { } known || !ReferenceEquals(known.Doc, doc) || known.Version != version)
        {
            labels.Clear();
            labelsFor = (doc, version);
        }
        if (!labels.TryGetValue(id, out var label)) labels[id] = label = Editor.Outline.Label(doc, id) ?? id;
        return label;
    }

    private (TDoc Doc, long Version)? labelsFor;
    private readonly Dictionary<string, string> labels = new(StringComparer.Ordinal);

    /// <summary>The local UI definitions over the existing Run handlers. Query each frame for current dynamic entries.</summary>
    public IReadOnlyList<UiAction> UiActions()
    {
        var actions = new List<UiAction>();
        void Add(string id, Func<UiActionContext, UiActionPresentation> present) =>
            actions.Add(new UiAction(id, present, () => Shortcuts.For(id), () => Run(id)));
        static UiActionPresentation Palette(UiActionContext c, string label, string? reason = null) => new(c, label, true, reason is null, reason);
        static UiActionPresentation Toolbar(UiActionContext c, string label, bool enabled = true) => new(c, label, true, enabled, null);
        static UiActionPresentation Hidden(UiActionContext c, string label) => new(c, label, false, false, null);

        if(SceneHide is not null)
        {
            var viewReason=Editor.Playing is not null||ProjectPlaying?.Invoke()==true?"Stop Play before changing view choices.":Editor.Selection.Count==0?"Select objects first.":null;
            Add("editor.hide_objects",c=>c==UiActionContext.Toolbar?Hidden(c,"Hide/show objects"):Palette(c,"Hide/show objects",viewReason));
            Add("editor.lock_objects",c=>c==UiActionContext.Toolbar?Hidden(c,"Lock/unlock objects"):Palette(c,"Lock/unlock objects",viewReason));
        }
        if(SceneCopy is not null)
        {
            Add("scene.copy",c=>c==UiActionContext.Toolbar?Hidden(c,"Copy objects"):Palette(c,"Copy objects",Editor.Selection.Count==0?"Select objects to copy first.":null));
            Add("scene.paste",c=>c==UiActionContext.Toolbar?Hidden(c,"Paste objects"):Palette(c,"Paste objects",ProjectPlaying?.Invoke()==true||Editor.Playing is not null?"Stop Play before pasting objects.":ScenePasteUnavailable?.Invoke()));
        }
        if(Editor is EditorModel<Scene>)
        {
            var groupPlaying=Editor.Playing is not null||ProjectPlaying?.Invoke()==true;
            Add("scene.group",c=>c==UiActionContext.Toolbar?Hidden(c,"Group objects"):Palette(c,"Group objects",groupPlaying?"Stop Play before grouping objects.":Editor.Selection.Count==0?"Select objects to group first.":null));
            Add("scene.ungroup",c=>c==UiActionContext.Toolbar?Hidden(c,"Ungroup parent"):Palette(c,"Ungroup parent",groupPlaying?"Stop Play before ungrouping objects.":Editor.Selection.Count!=1?"Select one parent to ungroup.":null));
        }
        if(sceneRename is not null)Add("editor.rename_object",c=>c==UiActionContext.Toolbar?Hidden(c,"Rename object")
            :Palette(c,"Rename object",Editor.Playing is not null||ProjectPlaying?.Invoke()==true?"Stop Play before renaming an object.":Editor.Selection.Count!=1?"Select one object to rename.":null));
        Add("editor.undo", c => c == UiActionContext.Toolbar
            ? Toolbar(c, "Undo", Editor.Session.History.CanUndo && Editor.Playing is null)
            : Palette(c, "Undo", Editor.Session.History.CanUndo ? null : "nothing to undo"));
        Add("editor.redo", c => c == UiActionContext.Toolbar
            ? Toolbar(c, "Redo", Editor.Session.History.CanRedo && Editor.Playing is null)
            : Palette(c, "Redo", Editor.Session.History.CanRedo ? null : "nothing to redo"));
        Add("editor.reset_layout", c => c == UiActionContext.Toolbar ? Hidden(c, "Reset layout") : Palette(c, "Reset layout"));
        Add("editor.save", c => c == UiActionContext.Toolbar
            ? Toolbar(c, "Save", Editor.Session.FilePath is not null && Editor.Session.Dirty && Editor.Playing is null)
            : Palette(c, "Save", Editor.Session.FilePath is null ? "no file yet" : null));
        Add("editor.close", c => c == UiActionContext.Toolbar ? Hidden(c, "Close")
            : Palette(c, "Close", CloseDocument is null ? "This editor does not provide a close-file action." : null));
        Add("editor.file_save_as", c => c == UiActionContext.Toolbar
            ? Toolbar(c, "Save as...", Editor.Playing is null&&SaveAsUnavailable?.Invoke() is null) : Palette(c, "Save as...",SaveAsUnavailable?.Invoke()));
        Add("editor.focus_in", c => c == UiActionContext.Toolbar ? Hidden(c, "Focus on selection")
            : Palette(c, "Focus on selection", Editor.Primary is null ? "select something first" : null));
        Add("editor.focus_out", c => c == UiActionContext.Toolbar ? Hidden(c, "Step out of focus")
            : Palette(c, "Step out of focus", Editor.Focus.Count == 0 ? "not focused" : null));
        Add("editor.text_larger", c => c == UiActionContext.Toolbar ? Hidden(c, "Larger text")
            : Palette(c, "Larger text", TextScale >= MaxTextScale ? "it's as large as it goes" : null));
        Add("editor.text_smaller", c => c == UiActionContext.Toolbar ? Hidden(c, "Smaller text")
            : Palette(c, "Smaller text", TextScale <= MinTextScale ? "it's as small as it goes" : null));
        Add("editor.text_reset", c => c == UiActionContext.Toolbar ? Hidden(c, "Normal text size")
            : Palette(c, "Normal text size", TextScale == 1 ? "it's already normal" : null));
        Add("editor.palette", c => c == UiActionContext.Toolbar ? Toolbar(c, "Find a command...") : Hidden(c, "Find a command..."));

        // Match the old palette's one playing-state choice per query. The toolbar keeps its existing branch/order.
        var playing = Editor.Playing;
        if(ProjectPlayAction is not null)
        {
            var running=ProjectPlaying?.Invoke()==true;
            var reason=running?"Stop the running program first.":ProjectPlayUnavailable?.Invoke(false);
            Add(PlayAction+"project",c=>Palette(c,"Play",reason));
            foreach(var (id,label,command) in new[]{("editor.pause_simulation","Pause simulation",ProjectFrameCommand.Pause),("editor.step_simulation","Step one frame",ProjectFrameCommand.Step),("editor.resume_simulation","Resume simulation",ProjectFrameCommand.Resume)})
                Add(id,c=>c==UiActionContext.Toolbar?Hidden(c,label):Palette(c,label,ProjectFrameActions.Refusal(ProjectFrames?.Invoke()?.FrameControl,command)));
            Add("editor.stop",c=>Palette(c,"Stop",running?null:"Nothing is playing."));
            Add("editor.build_play",c=>Palette(c,"Build and Play",running?"Stop the running program first.":
                ProjectBuildAndPlayAction is null?"Set a build command in Tools > Play settings":ProjectPlayUnavailable?.Invoke(true)));
        }
        else
        {
            Add("editor.stop", c => playing is null ? Hidden(c, "Stop") : c == UiActionContext.Toolbar
                ? Toolbar(c, $"Stop {playing.Title}") : Palette(c, $"Stop playing {playing.Title}"));
            foreach (var mode in Editor.PlayModes)
                Add(PlayAction + mode.Id, c => playing is null ? Palette(c, $"Play {mode.Title}") : Hidden(c, $"Play {mode.Title}"));
        }
        foreach (var panel in Editor.Panels)
            Add(PanelAction + panel.Id, c => c == UiActionContext.Toolbar || panel.ShowWhen?.Invoke() == false ? Hidden(c, panel.Title)
                : Palette(c, (Editor.IsOpen(panel.Id) ? "Hide " : "Show ") + panel.Title));
        foreach (var panel in Editor.Panels)
            foreach (var command in panel.Commands ?? [])
                Add(PanelCommandAction + panel.Id + "/" + command.Id, c => c == UiActionContext.Toolbar || panel.ShowWhen?.Invoke() == false ? Hidden(c, command.Label)
                    : Palette(c, command.Label, Editor.Playing is null ? command.Unavailable?.Invoke() : "a game is playing"));
        return actions;
    }

    /// <summary>The palette's entries for <paramref name="text"/>: the editor's own actions, then the document's commands.</summary>
    public IReadOnlyList<(string Id, string Label, string? Shortcut, string? Unavailable)> PaletteEntries(string text)
    {
        var t = text.Trim();
        var actions = UiActions().Select(a => (Action: a, View: a.Present(UiActionContext.Palette)))
            .Where(e => e.View.Visible || e.Action.Id.StartsWith(PanelAction, StringComparison.Ordinal) || e.Action.Id.StartsWith(PanelCommandAction, StringComparison.Ordinal))
            .Where(e => t.Length == 0 || e.View.Label.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Action.Id.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.Action.Id, e.View.Label, Shortcut: e.Action.Shortcut?.ToString(), Unavailable: e.View.Reason));
        // Document command search/making stays on the existing backend path, without eager toolbar/key preflight.
        var commands = Editor.Search(t, c => (CommandFor?.Invoke(c, Editor) ?? Editor.Make(c.Id)) is null ? "needs details: use the inspector" : null)
            .Select(p => (p.Command.Id, p.Command.Label, Shortcut: Shortcuts.For(p.Command.Id)?.ToString(), p.Unavailable));
        return actions.Concat(commands).ToList();
    }

    /// <summary>The palette action that starts a play mode is this prefix plus the mode's id.</summary>
    public const string PlayAction = "editor.play.";

    /// <summary>The palette action that shows or hides a panel is this prefix plus the panel's id.</summary>
    public const string PanelAction = "editor.panel.";

    /// <summary>The palette action that runs a panel's command is this prefix, the panel's id, a slash and the command's id.</summary>
    public const string PanelCommandAction = "editor.panel_do.";

    // ---- drawing ----

    /// <summary>Draws the window. Call between <c>ImGui.NewFrame</c> and <c>ImGui.Render</c>.</summary>
    public void Draw()
    {
        EditorTheme.Refresh();
        Keys();
        ImGui.SetNextWindowSize(new float2(900, 600), ImGuiCond.FirstUseEver);
        var open=true;
        var showing=CloseDocument is null?Ui.Begin($"{Title}###editor"):Ui.Begin($"{Title}###editor",ref open);
        if (showing)
        {
            mainRight = ImGui.GetWindowPos() + new float2(ImGui.GetWindowSize().X, 0);
            mainWidth = ImGui.GetWindowSize().X;
            Toolbar();
            SaveAsRow();
            Breadcrumb();
            ImGui.Separator();
            if (Ui.BeginChild("outliner"u8, new float2(260, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.Borders)) Outliner();else outlineHadFocus=false;
            Ui.EndChild();
            ImGui.SameLine();
            if (Ui.BeginChild("inspector"u8, new float2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.Borders)) inspectorController.Draw();
            Ui.EndChild();
            var message = BedAbout.Heading + " | " + (status.Length > 0 ? status : "Ctrl+Shift+P finds any command.");
            if (status.Length > 0) Ui.StatusDisabled(message, status, CommandStatusId);
            else Ui.TextDisabled(message);
        }
        Ui.End();
        if(!open)Run("editor.close");
        if (Editor.Playing is null) Panels();   // authoring panels step aside while a game plays, and come back on Stop
        Game();
        Palette();
        LinkedPlaySavePrompt();
        sceneDelete?.Draw();
        preferences.Draw();
        ProblemReport.Draw();
        NotePopups();
    }

    // The running game in its own frame, beside the editor. A game that throws is stopped and reported.
    private void Game()
    {
        if (Editor.Playing is not { } mode || Editor.Game is not { } game) return;
        var linked=game as LinkedPlayPreview;
        if(linked is not null)
        {
            try
            {
                linked.Update();
                if(linked.HasCompleted)
                {
                    var final=linked.Status;
                    Editor.Stop();Say(true,final,final);return;
                }
            }
            catch(Exception error)
            {
                Editor.Stop();Say(false,"",$"{mode.Title} failed and was stopped: {error.Message}");return;
            }
        }
        if (centre is { } dock) ImGui.SetNextWindowDockID(dock, ImGuiCond.Always);   // the game takes the centre, as a tab
        else ImGui.SetNextWindowPos(mainRight + new float2(10, 0), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(460, 560), ImGuiCond.FirstUseEver);
        var open = true;
        if (Ui.Begin($"{mode.Title} (playing)###play", ref open))
        {
            try { if(linked is not null) linked.DrawAfterUpdate();else game.Draw(); }
            catch (Exception e)
            {
                open = false;
                Say(false, "", $"{mode.Title} failed and was stopped: {e.Message}");
            }
        }
        Ui.End();
        if (!open) Editor.Stop();
    }

    // Each open panel in its own frame. A panel that throws is closed and reported; the editor carries on.
    private void Panels()
    {
        var below = 0f;
        foreach (var panel in Editor.Panels.Where(p => Editor.IsOpen(p.Id) && p.ShowWhen?.Invoke() != false).ToList())
        {
            var open = true;
            if (centre is { } dock && panel.Side == PanelSide.Centre) ImGui.SetNextWindowDockID(dock, ImGuiCond.FirstUseEver);   // a tab in the centre
            else
            {
                var vp=ImGui.GetMainViewport();var limit=vp.WorkPos+vp.WorkSize;
                var width=MathF.Min(panel.Width,vp.WorkSize.X-16);var height=MathF.Min(panel.Height,vp.WorkSize.Y-16);
                var at=mainRight+new float2(10,below);
                ImGui.SetNextWindowPos(new float2(Math.Clamp(at.X,vp.WorkPos.X,MathF.Max(vp.WorkPos.X,limit.X-width)),Math.Clamp(at.Y,vp.WorkPos.Y,MathF.Max(vp.WorkPos.Y,limit.Y-height))),ImGuiCond.FirstUseEver);
            }   // beside the editor, stacked
            ImGui.SetNextWindowSize(new float2(panel.Width, panel.Height), ImGuiCond.FirstUseEver);
            below += panel.Height + 10;
            var fresh = centre is not null && panel.Side != PanelSide.Centre && ImGui.Internal.FindWindowByName($"{panel.Title}###panel.{panel.Id}").IsNull();
            if (Ui.Begin($"{panel.Title}###panel.{panel.Id}", ref open))
                try { panel.Draw(); }
                catch (Exception e)
                {
                    open = false;
                    ProblemReportLog.Record(ReportEvent.PanelFailed);
                    Say(false, "", $"The {panel.Title} panel failed and was closed: {e.Message}");
                }
            Ui.End();
            // In the docked layout a panel's first showing must land in a dock. A layout built for another document (or
            // restored from an older layout file) never placed it, so it would float over the inspector and the centre.
            // A saved layout that floats it on purpose is kept (Reset layout docks everything again).
            if (fresh && ImGui.Internal.FindWindowByName($"{panel.Title}###panel.{panel.Id}") is var shown && !shown.IsNull() && shown.DockId == default
                && ImGui.Internal.FindWindowSettingsByWindow(shown).IsNull())
                strays.Add(panel);
            if (!open){if(PanelCloseRequested is {} requested)requested(panel.Id);else Editor.SetOpen(panel.Id,false);}
        }
    }

    // ---- keys from ImGui ----

    // Every key a shortcut can name (Shortcuts' letters, digits, F1-F12 and named keys), with ImGui's key for it.
    private static readonly (ImGuiKey Key, string Name)[] KeyNames =
    [
        .. Enumerable.Range(0, 26).Select(i => ((ImGuiKey)((int)ImGuiKey.A + i), ((char)('A' + i)).ToString())),
        .. Enumerable.Range(0, 10).Select(i => ((ImGuiKey)((int)ImGuiKey._0 + i), i.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        .. Enumerable.Range(0, 12).Select(i => ((ImGuiKey)((int)ImGuiKey.F1 + i), $"F{i + 1}")),
        (ImGuiKey.Space, "Space"), (ImGuiKey.Escape, "Escape"), (ImGuiKey.Delete, "Delete"), (ImGuiKey.Enter, "Enter"),
        (ImGuiKey.Tab, "Tab"), (ImGuiKey.Backspace, "Backspace"), (ImGuiKey.Home, "Home"), (ImGuiKey.End, "End"),
        (ImGuiKey.PageUp, "PageUp"), (ImGuiKey.PageDown, "PageDown"), (ImGuiKey.UpArrow, "Up"), (ImGuiKey.DownArrow, "Down"),
        (ImGuiKey.LeftArrow, "Left"), (ImGuiKey.RightArrow, "Right"), (ImGuiKey.Insert, "Insert"),
    ];

    // Feeds the shortcuts from ImGui's keyboard, so a real window's keys reach HandleKey (found by the host's build: no
    // key ever arrived). While a text field has the keyboard, keys belong to it: typing "F" never focuses, Ctrl+Z
    // undoes the typing, not the document.
    private void Keys()
    {
        if(ProblemReport.HasKeyboardFocus)return;
        if(KeysPanel.Capture(Editor,Shortcuts,KeyNames))return;
        var io = ImGui.GetIO();
        if (io.KeyCtrl && !io.KeyShift && !io.KeyAlt && !io.KeySuper && ImGui.IsKeyPressed(ImGuiKey.Comma, false))
        { preferences.Open(); return; }
        if (preferences.HasKeyboardFocus) return;
        if(CloseDocument is not null&&Shortcuts.For("editor.close") is not null&&io.KeyCtrl&&!io.KeyAlt&&!io.KeyShift&&!io.KeySuper&&ImGui.IsKeyPressed(ImGuiKey.F4,false)&&Shortcuts.Action(Chord.Parse("Ctrl+F4")) is null)
        {Run("editor.close");return;}
        if(sceneRename is {HasPending:true}&&ImGui.IsKeyPressed(ImGuiKey.Escape,false)){HandleKey(Escape);return;}
        if(outlineHadFocus&&Text(outlineSearch).Length>0&&!popupWasOpen&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))
        {ClearOutlineSearch();return;}
        if(outlineHadFocus&&io.KeyCtrl&&!io.KeyAlt&&!io.KeySuper&&ImGui.IsKeyPressed(ImGuiKey.F,false))return;
        // Escape still reaches the palette and the Save-as row while their fields have the keyboard: it's their way out.
        if ((paletteShowing || saveAsOpen) && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            HandleKey(Chord.Parse("Escape"));
            return;
        }
        if (io.WantTextInput)
        {
            // Function-key Play controls remain available while editing a field; ordinary typing stays local.
            if(ProjectPlayAction is not null)
                foreach(var (key,name) in KeyNames)
                {
                    if(name.Length<2||name[0]!='F'||!ImGui.IsKeyPressed(key,false))continue;
                    var chord=Chord.Parse((io.KeyCtrl?"Ctrl+":"")+(io.KeyShift?"Shift+":"")+(io.KeyAlt?"Alt+":"")+name);
                    if(Shortcuts.Action(chord) is "editor.play.project" or "editor.stop" or "editor.build_play" or "editor.pause_simulation" or "editor.step_simulation" or "editor.resume_simulation")HandleKey(chord);
                }
            return;
        }
        // A popup that's open (a dropdown) takes Escape for itself; it doesn't also step out of focus (found by A031).
        // ImGui closes it on Escape at the start of the frame, so ask whether one was open at the end of the last one.
        if ((popupWasOpen || ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)) && ImGui.IsKeyPressed(ImGuiKey.Escape, false)) return;
        var mods = (io.KeyCtrl ? "Ctrl+" : "") + (io.KeyShift ? "Shift+" : "") + (io.KeyAlt ? "Alt+" : "");
        foreach (var (key, name) in KeyNames)
            if (ImGui.IsKeyPressed(key, false)) HandleKey(Chord.Parse(mods + name));
    }

    // ---- the docked layout ----

    private bool resetLayout;
    private ImGuiID? centre;   // the central dock node, once the docked layout is built

    /// <summary>
    /// Draws the editor filling the screen, docked: the toolbar, focus breadcrumb and status along the top; the outliner
    /// docked left and the inspector right; panels (a viewport, say) and the running game as tabs in the centre. People
    /// can drag tabs and splits to rearrange it. BORROWED: the layout of Unity, Godot and Blender's editors, built with
    /// ImGui's docking. Needs <see cref="Configure"/> (which turns docking on). Call it instead of <see cref="Draw"/>,
    /// between <c>ImGui.NewFrame</c> and <c>ImGui.Render</c>.
    /// </summary>
    public void DrawDocked()
    {
        EditorTheme.Refresh();
        Keys();
        var vp = ImGui.GetMainViewport();
        mainRight = vp.Pos + new float2(vp.Size.X, 0);
        mainWidth = vp.Size.X;
        if (Ui.BeginMainMenuBar())
        {
            var compact = CompactCommandStrip(vp.WorkSize.X);
            if (compact)
            {
                var menusInOverflow = MenuStripWidth() + StripItemWidth("More") + 2 * ImGui.GetStyle().WindowPadding.X > vp.WorkSize.X;
                if (!menusInOverflow) { Menus(); ImGui.Separator(); }
                var io=ImGui.GetIO();
                var alt=io.KeyAlt&&!io.WantTextInput&&!io.KeyCtrl&&!io.KeySuper;
                (ImGuiKey Key,string Label)[] menuKeys=[(ImGuiKey.F,"File"),(ImGuiKey.E,"Edit"),(ImGuiKey.V,"View"),(ImGuiKey.T,"Tools"),(ImGuiKey.H,"Help")];
                if(alt&&(ImGui.IsKeyPressed(ImGuiKey.M,false)||menusInOverflow&&menuKeys.Any(pair=>ImGui.IsKeyPressed(pair.Key,false))))ImGui.OpenPopup("More"u8);
                var moreOpen = Ui.BeginMenu("More"u8);
                Ui.AccessKeyLast("M");
                if (moreOpen)
                {
                    if (menusInOverflow)
                    {
                        if(alt)foreach(var pair in menuKeys)if(ImGui.IsKeyPressed(pair.Key,false))ImGui.OpenPopup(pair.Label);
                        Menus(); ImGui.Separator();
                    }
                    OverflowToolbar(); Ui.EndMenu();
                }
            }
            else
            {
                Menus(); ImGui.Separator(); Toolbar(false); ImGui.SameLine();
                Ui.TextDisabled("|"u8); ImGui.SameLine(); Breadcrumb(); ImGui.SameLine();
            }
            var message = status.Length > 0 ? status : "Ctrl+Shift+P: commands";
            var trailing = BedAbout.Heading + " | " + message;
            var style = ImGui.GetStyle();
            // Menus come first; optional status text yields to the remaining controls.
            var textWidth = MathF.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Help"u8).X
                - 2 * style.FramePadding.X - 2 * style.ItemSpacing.X);
            if (ImGui.CalcTextSize(trailing).X > textWidth) trailing = message;
            if (ImGui.CalcTextSize(trailing).X > textWidth)
            {
                const string ellipsis = "...";
                if (ImGui.CalcTextSize(ellipsis).X > textWidth) trailing = "";
                else
                {
                    // Keep whole text elements, including surrogate pairs and combining marks.
                    var starts = System.Globalization.StringInfo.ParseCombiningCharacters(trailing);
                    var low = 0;
                    var high = starts.Length;
                    while (low < high)
                    {
                        var count = (low + high + 1) / 2;
                        var end = count == starts.Length ? trailing.Length : starts[count];
                        if (ImGui.CalcTextSize(trailing[..end] + ellipsis).X <= textWidth) low = count;
                        else high = count - 1;
                    }
                    var length = low == starts.Length ? trailing.Length : starts[low];
                    trailing = trailing[..length] + ellipsis;
                }
            }
            if (trailing.Length > 0)
            {
                if (status.Length > 0) Ui.StatusDisabled(trailing, status, CommandStatusId);
                else Ui.TextDisabled(trailing);
            }
            Ui.EndMainMenuBar();
        }
        DrawPlaybackToolbar();
        // Keep this private ImGui seed stable so existing personal layout.ini files still dock correctly.
        var space = ImGui.GetID("brutal.authoring.dock"u8);
        if (resetLayout) { BuildLayout(space, vp.WorkSize); resetLayout = false; }
        else if (centre is null)
        {
            var restored = RestoredCentre(ImGui.Internal.DockBuilderGetNode(space));
            if (restored is { } id) centre = id;
            else BuildLayout(space, vp.WorkSize);
        }
        DockStrays(space);
        ImGui.DockSpaceOverViewport(space, vp, ImGuiDockNodeFlags.None, default);
        if (RestoredCentre(ImGui.Internal.DockBuilderGetNode(space)) is { } central && ImGui.Internal.DockBuilderGetNode(central) is var centralNode && !centralNode.IsNull())
            DockedCentre.Record(centralNode.Pos, centralNode.Size);   // floating help boxes keep inside it, clear of the docks
        if (saveAsOpen)   // in the docked layout, Save as is a small window under the toolbar
        {
            ImGui.SetNextWindowPos(new float2(vp.Pos.X + vp.Size.X / 2, vp.WorkPos.Y + 8), ImGuiCond.Always, new float2(0.5f, 0));
            ImGui.SetNextWindowSize(new float2(MathF.Min(640, vp.Size.X - 40), 0), ImGuiCond.Always);
            if (Ui.Begin("Save as###saveas"u8, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar)) SaveAsRow();
            Ui.End();
        }
        if(sceneRename is {FocusWindow:true})ImGui.SetNextWindowFocus();
        if (Ui.Begin("Outliner###outliner"u8)) Outliner();else outlineHadFocus=false;
        Ui.End();
        if (inspectorFocusRequested) { ImGui.SetNextWindowFocus(); inspectorFocusRequested = false; }
        if (Ui.Begin("Inspector###inspector"u8)) inspectorController.Draw();
        Ui.End();
        if (Editor.Playing is null) Panels();
        Game();
        Palette();
        LinkedPlaySavePrompt();
        sceneDelete?.Draw();
        preferences.Draw();
        ProblemReport.Draw();
        NotePopups();
    }

    // The first layout: outliner left (a fifth), inspector right (just over a quarter of what's left), the rest central.
    // The root's cached CentralNode pointer is not populated until DockSpace updates it.
    // Loaded node flags are already available before submission, when builder changes must happen.
    private static ImGuiID? RestoredCentre(Brutal.ImGuiApi.Internal.ImGuiDockNodePtr node, int depth = 0)
    {
        if (node.IsNull() || depth > 64) return null;
        if (Brutal.ImGuiApi.Internal.ImGuiDockNodeExtensions.IsCentralNode(node)) return node.ID;
        return RestoredCentre(node.ChildNodes[0], depth + 1) ?? RestoredCentre(node.ChildNodes[1], depth + 1);
    }
    private readonly List<Panel> strays = [];
    private static ImGuiID DockOf(string window)
    {
        var found = ImGui.Internal.FindWindowByName(window);
        return found.IsNull() ? default : found.DockId;
    }
    // Docks panels that first showed floating where the built layout would have put them: a bottom browser panel beside
    // Files (Files keeps the left part), a right panel as a tab with the inspector or above it, a left one above the outliner.
    private void DockStrays(ImGuiID space)
    {
        if (strays.Count == 0) return;
        foreach (var panel in strays)
        {
            var name = $"{panel.Title}###panel.{panel.Id}";
            ImGuiID target = default;
            if (BottomPanel(panel))
            {
                foreach (var other in Editor.Panels)
                    if (other.Id != panel.Id && other.Id != "project.files" && BottomPanel(other) && DockOf($"{other.Title}###panel.{other.Id}") is var id && id != default) { target = id; break; }
                if (target == default && DockOf("Play console###project.play.console") is var console && console != default) target = console;
                if (target == default && DockOf("Files###panel.project.files") is var files && files != default)
                    ImGui.Internal.DockBuilderSplitNode(files, ImGuiDir.Right, .72f, out target, out _);
            }
            else if (panel.Side == PanelSide.Right && DockOf("Inspector###inspector") is var inspector && inspector != default)
            {
                if (RightTabPanel(panel)) target = inspector;
                else ImGui.Internal.DockBuilderSplitNode(inspector, ImGuiDir.Up, .4f, out target, out _);
            }
            else if (panel.Side == PanelSide.Left && DockOf("Outliner###outliner") is var outliner && outliner != default)
                ImGui.Internal.DockBuilderSplitNode(outliner, ImGuiDir.Up, .4f, out target, out _);
            if (target == default && DockOf("Inspector###inspector") is var fallback && fallback != default) target = fallback;
            if (target != default) ImGui.Internal.DockBuilderDockWindow(name, target);
        }
        strays.Clear();
        ImGui.Internal.DockBuilderFinish(space);
    }
    private static bool RightTabPanel(Panel panel)=>panel.Id is "scene2.physics" or "project.references" or "project.proposals";
    private static bool BottomPanel(Panel panel)=>panel.Id is "project.files" or "project.checks" or "scene2.assets" or "scene2.prefabs" || panel.Title=="Assets";
    private void BuildLayout(ImGuiID space, float2 size)
    {
        ImGui.Internal.DockBuilderRemoveNode(space);
        ImGui.Internal.DockBuilderAddNode(space, (ImGuiDockNodeFlags)(1 << 10));   // ImGuiDockNodeFlags_DockSpace
        ImGui.Internal.DockBuilderSetNodeSize(space, size);
        ImGui.Internal.DockBuilderSplitNode(space,ImGuiDir.Down,size.Y<ImGui.GetFontSize()*26?.22f:Math.Clamp(.28f+(ImGui.GetFontSize()/16f-1)*.08f,.28f,.4f),out var bottom,out var content);
        ImGui.Internal.DockBuilderSplitNode(content, ImGuiDir.Left, 0.2f, out var left, out var rest);
        ImGui.Internal.DockBuilderSplitNode(rest, ImGuiDir.Right, 0.3f, out var right, out var middle);
        var filesDock=bottom;var browserDock=bottom;
        if(Editor.Panels.Any(p=>p.Id=="scene2.prefabs"&&p.ShowWhen?.Invoke()!=false))
            ImGui.Internal.DockBuilderSplitNode(bottom,ImGuiDir.Left,.28f,out filesDock,out browserDock);
        ImGuiID leftTop = default, rightTop = default;
        // A panel that asks for a side gets the top part of that column, above the outliner or the inspector.
        if (Editor.Panels.Any(p => p.Side == PanelSide.Left && !BottomPanel(p) && p.ShowWhen?.Invoke() != false)) ImGui.Internal.DockBuilderSplitNode(left, ImGuiDir.Up, 0.4f, out leftTop, out left);
        if (Editor.Panels.Any(p => p.Side == PanelSide.Right && !RightTabPanel(p) && !BottomPanel(p) && p.ShowWhen?.Invoke() != false)) ImGui.Internal.DockBuilderSplitNode(right, ImGuiDir.Up, Math.Clamp(.4f+(ImGui.GetFontSize()/16f-1)*.2f,.4f,.65f), out rightTop, out right);
        ImGui.Internal.DockBuilderDockWindow("Outliner###outliner"u8, left);
        ImGui.Internal.DockBuilderDockWindow("Inspector###inspector"u8, right);
        foreach (var panel in Editor.Panels.Where(p => p.ShowWhen?.Invoke() != false))
            ImGui.Internal.DockBuilderDockWindow($"{panel.Title}###panel.{panel.Id}", BottomPanel(panel)?(panel.Id=="project.files"?filesDock:browserDock):panel.Side switch { PanelSide.Left => leftTop, PanelSide.Right => RightTabPanel(panel)?right:rightTop, _ => middle });
        ImGui.Internal.DockBuilderDockWindow("Play console###project.play.console"u8,browserDock);
        ImGui.Internal.DockBuilderFinish(space);
        centre = middle;
    }

    // Menu routes use the same live descriptors and handlers as the palette. Missing host routes
    // are visibly unavailable: a menu must never manufacture a project operation or a check policy.
    private void Menus()
    {
        var scopes = new Stack<HashSet<char>>(); scopes.Push(new());
        var io = ImGui.GetIO(); var consumed = false;
        bool Chord(char letter) => !consumed && io.KeyAlt && !io.WantTextInput && !io.KeyCtrl && !io.KeySuper
            && ImGui.IsKeyPressed((ImGuiKey)((int)ImGuiKey.A + letter - 'A'), false);
        AccessKey? Assign(string label)
        {
            for (var i = 0; i < label.Length; i++)
            {
                var letter = char.ToUpperInvariant(label[i]);
                if (letter is >= 'A' and <= 'Z' && scopes.Peek().Add(letter)) return new(label, label, letter, i);
            }
            return null;
        }
        void Mark(AccessKey? key, float2 origin, ImDrawListPtr list)
        {
            if (key is null) return;
            Ui.AccessKeyLast(key.Letter.ToString());
            if (io.KeyAlt && !io.WantTextInput && !io.KeyCtrl && !io.KeySuper)
            {
                var line = AccessKeys.Underline(key, origin);
                list.AddLine(new float2(line.X, line.Y), new float2(line.X + line.Width, line.Y), ImGui.GetColorU32(ImGuiCol.Text), 1);
            }
        }
        bool Begin(string label, bool enabled = true)
        {
            var key = Assign(label); var list = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var top = scopes.Count == 1;
            origin += top ? new float2(ImGui.GetStyle().ItemSpacing.X / 2, ImGui.GetStyle().FramePadding.Y) : float2.Zero;
            if (enabled && key is not null && Chord(key.Letter) && (top || ImGui.IsWindowFocused()) &&
                (!top || !ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)))
            { ImGui.OpenPopup(label); consumed = true; }
            var opened = Ui.BeginMenu(label, enabled);
            Mark(key, origin, list);
            if (opened) scopes.Push(new());
            return opened;
        }
        void End() { Ui.EndMenu(); scopes.Pop(); }
        bool Choice(string label, string shortcut, bool selected, bool enabled, string? reason = null)
        {
            var key = Assign(label); var origin = ImGui.GetCursorScreenPos(); var list = ImGui.GetWindowDrawList();
            var clicked = Ui.MenuItem(label, shortcut, selected, enabled, reason);
            if (enabled && shortcut.Length > 0) Tooltips.Show($"{label} ({shortcut})");
            Mark(key, origin, list);
            if (enabled && key is not null && Chord(key.Letter) && ImGui.IsWindowFocused())
            { consumed = true; ImGui.CloseCurrentPopup(); return true; }
            return clicked;
        }
        void Item(string label, string id, string missing = "This editor does not provide this action.")
        {
            var action = UiActions().FirstOrDefault(a => a.Id == id);
            var view = action?.Present(id == "editor.palette" ? UiActionContext.Toolbar : UiActionContext.Palette);
            if (view is { Visible: false }) return;
            var reason = action is null ? missing : view?.Reason;
            var enabled = action is not null && view is { Enabled: true };
            var selected = id.StartsWith(PanelAction, StringComparison.Ordinal) && Editor.IsOpen(id[PanelAction.Length..]);
            if (Choice(label, action?.Shortcut?.ToString() ?? "", selected, enabled, reason)) action!.Invoke();
            if (reason is not null) Tooltips.Show(reason);
        }
        void Missing(string label, string why) => Item(label, "", why);
        void Panel(string label, string id) => Item(label, PanelAction + id, "This panel is not available for this file.");
        if (Begin("File", true))
        {
            Item("New file...", PanelCommandAction + "project.files/new_file");
            Item("New project...", PanelCommandAction + "project.files/new_project", "This editor has no project host.");
            Item("Open project...", PanelCommandAction + "project.files/open_project", "This editor has no project host.");
            Item("Open file...", PanelCommandAction + "project.files/open_file", "This editor has no project file list.");
            if(RecentProjectsMenu is {} recent&&Begin("Recent projects")){recent();End();}
            else if(RecentProjectsMenu is null)Missing("Recent projects", "This host has no recent-project list.");
            ImGui.Separator();
            Item("Save", "editor.save");
            Item("Save as...", "editor.file_save_as");
            Item("Save all", PanelCommandAction + "project.files/save_all");
            if(ExportBuildAction is {} export&&Choice("Export build...","",false,true))export();
            if(OpenGumAction is {} gum&&Choice("Edit screens in Gum","",false,true))gum();
            if(ShowGumScreenAction is {} gumScreen&&Choice("Show Gum screens","",false,true))gumScreen();
            ImGui.Separator();
            Item("Close", "editor.close");
            Item("Close project", PanelCommandAction + "project.files/close_project");
            Missing("Exit", "Use Close project to save or discard unsaved changes, then close the window.");
            End();
        }
        if (Begin("Edit", true))
        {
            Item("Undo", "editor.undo");
            Item("Redo", "editor.redo");
            ImGui.Separator();
            if(Editor is EditorModel<Scene>)
            {
                var reason=ProjectPlaying?.Invoke()==true?"Stop Play before duplicating objects.":Editor.Playing is not null?"Stop Play before duplicating objects.":Editor.Selection.Count==0?"Select objects to duplicate first.":null;
                if(Choice("Duplicate",Shortcuts.For("scene.duplicate")?.ToString()??"",false,reason is null,reason))Run("scene.duplicate");
            }
            if(Editor is EditorModel<Scene>){Item("Group", "scene.group");Item("Ungroup", "scene.ungroup");}
            Missing("Cut", "Use Copy, then Delete after checking the copied selection.");
            Item("Copy", SceneCopy is not null?"scene.copy":PanelCommandAction + "editor.table/copy");
            Item("Paste", ScenePaste is not null?"scene.paste":PanelCommandAction + "editor.table/paste");
            if(sceneDelete is not null)
            {
                var reason=ProjectPlaying?.Invoke()==true||Editor.Playing is not null?"Stop Play before deleting objects.":Editor.Selection.Count!=1?"Select one object to delete.":null;
                if(Choice("Delete",Shortcuts.For("scene.remove")?.ToString()??"",false,reason is null,reason))Run("scene.remove");
            }
            else Missing("Delete", "Use the selected object's available delete action.");
            if(sceneRename is not null)Item("Rename object", "editor.rename_object");
            if(SceneHide is not null){Item("Hide/show objects","editor.hide_objects");Item("Lock/unlock objects","editor.lock_objects");}
            Missing("Select all", "Use selection controls in the current panel.");
            Missing("Find...", "Use the find field in Properties.");
            Item("Fill column...", PanelCommandAction + "editor.table/fill");
            if (Begin("Preferences", true))
            {
                if (Choice("Open preferences...", "Ctrl+,", false, true)) preferences.Open();
                Panel("Keyboard shortcuts", KeysPanel.Id);
                Item("Larger text", "editor.text_larger");
                Item("Smaller text", "editor.text_smaller");
                Item("Normal text size", "editor.text_reset");
                if (Begin("UI scale", true))
                {
                    foreach (var percent in new[] { 100, 150, 200 })
                        if (Choice($"{percent}%", "", TextScale == percent / 100f, true)) SetTextScale(percent / 100f);
                    End();
                }
                if (Begin("Theme", true))
                {
                    if (Choice("Use Windows setting", "", EditorTheme.Preference == EditorThemePreference.System, true))
                        EditorTheme.Preference = EditorThemePreference.System;
                    if (Choice("Light", "", EditorTheme.Preference == EditorThemePreference.Light, true))
                        EditorTheme.Preference = EditorThemePreference.Light;
                    if (Choice("Dark", "", EditorTheme.Preference == EditorThemePreference.Dark, true))
                        EditorTheme.Preference = EditorThemePreference.Dark;
                    if (Choice("High contrast", "", EditorTheme.Preference == EditorThemePreference.HighContrast, true))
                        EditorTheme.Preference = EditorThemePreference.HighContrast;
                    End();
                }
                End();
            }
            End();
        }
        if (Begin("View", true))
        {
            Panel("Files", "project.files");
            Panel("Checks and output", "project.checks");
            Panel("Table", "editor.table");
            Panel("Text", "editor.text");
            if(Editor.Panels.Any(p=>p.Id=="scene2.physics"))Panel("Physics","scene2.physics");
            Item("Compare with saved", PanelCommandAction + "editor.compare/editor.compare.saved");
            Item("Command palette...", "editor.palette");
            Item("Refresh files", PanelCommandAction + "project.files/refresh");
            Item("Reset layout", "editor.reset_layout");
            Item("Larger text", "editor.text_larger");
            Item("Smaller text", "editor.text_smaller");
            End();
        }
        if (Editor.Panels.Any(p => p.Id == "scene.assets") && Begin("Scene", true))
        {
            Item("Add object.", PanelCommandAction + "scene.assets/add");
            End();
        }
        if (Begin("Tools", true))
        {
            if(PlaySettingsAction is {} settings&&Choice("Play settings...","",false,true))settings();
            Panel("Checks", "project.checks");
            Panel("Suggested changes", "project.proposals");
            var checks = UiActions().Where(a => a.Id.StartsWith(PanelCommandAction + "project.checks/run.", StringComparison.Ordinal)).ToArray();
            if (Begin("Run check", checks.Length > 0))
            {
                foreach (var action in checks) Item(action.Present(UiActionContext.Palette).Label, action.Id);
                End();
            }
            Missing("Run all checks", "Run individual checks from Checks; no quick-check batch is registered.");
            if(ProjectPlayAction is not null)
            {
                Item("Play",PlayAction+"project");Item("Build and Play","editor.build_play");Item("Stop","editor.stop");
            }
            else if (Editor.Playing is not null) Item("Stop", "editor.stop");
            else if (Editor.PlayModes.Count == 0) Missing("Play", "This project has no play mode");
            else if (Begin("Play", true))
            {
                foreach (var mode in Editor.PlayModes) Item(mode.Title, PlayAction + mode.Id);
                End();
            }
            Missing("Project information...", "The status bar shows the project folder and active file. Use Tools > Play settings for run commands.");
            End();
        }
        if (Begin("Help", true))
        {
            if(TakeTourAction is {} takeTour&&Ui.MenuItem("Take the tour"u8))takeTour();
            Panel("Getting started", GettingStartedPanel.Id);
            Panel("Keyboard shortcuts", KeysPanel.Id);
            Panel("Use with an AI assistant", AiSetupPanel.Id);
            Panel("About BED", "bed.about");
            if(Ui.MenuItem("Report a problem"u8))ProblemReport.Open();
            End();
        }
    }

    private static float StripItemWidth(string label)
    {
        var style=ImGui.GetStyle();
        return ImGui.CalcTextSize(label).X + 2 * MathF.Max(style.FramePadding.X,style.ItemSpacing.X) + style.ItemSpacing.X;
    }
    private static float MenuStripWidth() => new[]{"File","Edit","View","Tools","Help"}.Sum(StripItemWidth);

    private bool CompactCommandStrip(float width)
    {
        var style=ImGui.GetStyle();
        // Measure only controls that this editor will actually draw, not every possible Play path at once.
        var needed=MenuStripWidth()+new[]{"Save","Undo","Redo","All objects >"}.Sum(StripItemWidth)+4*style.ItemSpacing.X;
        if(Editor.Panels.Any(p=>p.Id=="project.checks"))needed+=StripItemWidth("Checks")+style.ItemSpacing.X;
        if(ProjectPlayAction is not null)
        {
            needed+=StripItemWidth(ProjectPlaying?.Invoke()==true?"Stop":"Play");
            if(ProjectBuildAndPlayAction is not null)needed+=StripItemWidth("Build and Play");
        }
        else if(Editor.Playing is not null)needed+=StripItemWidth("Stop");
        else if(Editor.PlayModes.Count>0)needed+=140+style.ItemSpacing.X+StripItemWidth("Play");
        foreach(var place in Editor.Focus)needed+=StripItemWidth(Label(place))+ImGui.CalcTextSize(">"u8).X+style.ItemSpacing.X;
        return width<needed+ImGui.CalcTextSize("Ctrl+Shift+P"u8).X+style.ItemSpacing.X*3;
    }

    private void OverflowToolbar()
    {
        void Item(string id,string? label=null,bool panel=false)
        {
            var action=UiActions().First(a=>a.Id==id);
            var view=action.Present(panel?UiActionContext.Palette:UiActionContext.Toolbar);
            if(Ui.MenuItem(label??view.Label,action.Shortcut?.ToString()??"",false,view.Enabled,view.Reason))action.Invoke();
            Tooltips.Show(!view.Enabled?view.Reason??"Unavailable":(label??view.Label)+(action.Shortcut is null?"":" ("+action.Shortcut+")"));
        }
        Item("editor.save");Item("editor.undo");Item("editor.redo");
        if(Editor.Panels.Any(p=>p.Id=="project.checks"))Item(PanelAction+"project.checks","Checks",panel:true);
        if(ProjectPlayAction is not null)
        {
            var playing=ProjectPlaying?.Invoke()==true;
            Item(playing?"editor.stop":PlayAction+"project",playing?"Stop":"Play");
            if(ProjectBuildAndPlayAction is not null)Item("editor.build_play","Build and Play");
        }
        else if(Editor.Playing is not null)Item("editor.stop","Stop");
        else if(Editor.PlayModes.Count>0)
        {
            var selected=Editor.PlayModes.FirstOrDefault(mode=>mode.Id==toolbarPlay)??Editor.PlayModes[0];toolbarPlay=selected.Id;
            if(Ui.BeginMenu("Play mode"u8))
            {
                foreach(var mode in Editor.PlayModes)
                    if(Ui.MenuItem(mode.Title,""u8,mode.Id==toolbarPlay,true))toolbarPlay=mode.Id;
                Ui.EndMenu();
            }
            Item(PlayAction+toolbarPlay,"Play");
        }
        if(Ui.MenuItem("All objects >"u8,""u8,false,true))while(Editor.FocusOut()){}
    }

    private void Toolbar(bool includePlayback=true)
    {
        void Button(string id, string? label = null)
        {
            var action = UiActions().First(a => a.Id == id);
            var view = action.Present(UiActionContext.Toolbar);
            Ui.BeginDisabled(!view.Enabled, view.Reason);
            if (Ui.Button(label ?? view.Label)) action.Invoke();
            Tooltips.Show(!view.Enabled ? view.Reason ?? "Unavailable" : view.Label + (action.Shortcut is null ? "" : " (" + action.Shortcut + ")"));
            Ui.EndDisabled();
        }
        Button("editor.save");
        ImGui.SameLine();
        Button("editor.undo");
        ImGui.SameLine();
        Button("editor.redo");
        if (Editor.Panels.Any(p => p.Id == "project.checks"))
        {
            ImGui.SameLine();
            Ui.TextDisabled("|"u8);
            ImGui.SameLine();
            // Opening Checks is supported; running all quick checks needs the host's batch contract.
            if (Ui.Button("Checks"u8))
                UiActions().First(a => a.Id == PanelAction + "project.checks").Invoke();
        }
        if(includePlayback&&ProjectPlayAction is not null)
        {
            ImGui.SameLine();
            Button(ProjectPlaying?.Invoke()==true?"editor.stop":PlayAction+"project",ProjectPlaying?.Invoke()==true?"Stop":"Play");
            if(ProjectBuildAndPlayAction is not null){ImGui.SameLine();Button("editor.build_play","Build and Play");}
        }
        else if (includePlayback&&Editor.Playing is not null)
        {
            ImGui.SameLine();
            Button("editor.stop", "Stop");
        }
        else if (includePlayback&&Editor.PlayModes.Count > 0)
        {
            var selected = Editor.PlayModes.FirstOrDefault(m => m.Id == toolbarPlay) ?? Editor.PlayModes[0];
            toolbarPlay = selected.Id;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            if (Ui.BeginCombo("##playmode"u8, selected.Title))
            {
                foreach (var mode in Editor.PlayModes)
                    if (Ui.Selectable(mode.Title, mode.Id == toolbarPlay)) toolbarPlay = mode.Id;
                Ui.EndCombo();
            }
            ImGui.SameLine();
            Button(PlayAction + toolbarPlay, "Play");
        }
    }

    private void Breadcrumb()
    {
        if (Ui.SmallButton("All objects >"u8)) { while (Editor.FocusOut()) { } }
        foreach (var place in Editor.Focus)
        {
            ImGui.SameLine();
            Ui.TextDisabled(">"u8);
            ImGui.SameLine();
            if (Ui.SmallButton($"{Label(place)}##crumb{place}")) Editor.FocusOn(place);
        }
    }

    private string? outlineSelectionAnchor, outlineMoveTo, outlineFocusedRow;
    private object? outlineNavigationRows;
    private (TDoc Doc,long? Version,string? Focus)? outlineSelectionContext;
    private string[] outlineExpectedSelection=[];
    private int outlineMoveFrame=-1;

    private static int OutlineIndex(IReadOnlyList<(string Id,int Depth,bool Place)> rows,string? id)
    {for(var i=0;i<rows.Count;i++)if(rows[i].Id==id)return i;return -1;}
    private void RememberOutlineSelection(string anchor)
    {
        outlineSelectionAnchor=anchor;outlineExpectedSelection=Editor.Selection.ToArray();
        outlineSelectionContext=(Editor.Session.Current,Editor.Outline.Version,Editor.FocusedOn);
    }
    private void ExtendOutlineSelection(IReadOnlyList<(string Id,int Depth,bool Place)> rows,int from,int to)
    {
        if(outlineSelectionContext is not {} context || !ReferenceEquals(context.Doc,Editor.Session.Current)
            ||context.Version!=Editor.Outline.Version||context.Focus!=Editor.FocusedOn||!outlineExpectedSelection.SequenceEqual(Editor.Selection))
            outlineSelectionAnchor=Editor.Primary??rows[from].Id;
        var anchor=OutlineIndex(rows,outlineSelectionAnchor);if(anchor<0)anchor=from;
        var direction=to>=anchor?1:-1;
        Editor.Select(Enumerable.Range(0,Math.Abs(to-anchor)+1).Select(i=>rows[anchor+i*direction].Id).ToArray());
        RememberOutlineSelection(rows[anchor].Id); // The last selected id remains the keyboard cursor in either direction.
    }
    private void OutlineSelectionKeys(IReadOnlyList<(string Id,int Depth,bool Place)> rows,int row,bool activated)
    {
        var id=rows[row].Id;var io=ImGui.GetIO();
        if(ImGui.IsItemFocused()&&!io.WantTextInput&&!io.KeyAlt&&!io.KeySuper)
        {
            if(io.KeyCtrl&&!io.KeyShift&&ImGui.IsKeyPressed(ImGuiKey.Space,false))
            {Editor.Toggle(id);RememberOutlineSelection(id);return;} // Ignore Selectable's simultaneous Space activation.
            var step=io.KeyShift&&!io.KeyCtrl?(ImGui.IsKeyPressed(ImGuiKey.DownArrow)?1:ImGui.IsKeyPressed(ImGuiKey.UpArrow)?-1:0):0;
            if(step!=0&&outlineMoveFrame!=ImGui.GetFrameCount())
            {
                var next=Math.Clamp(row+step,0,rows.Count-1);ImGui.Internal.NavMoveRequestCancel();
                if(next!=row){ExtendOutlineSelection(rows,row,next);outlineMoveTo=rows[next].Id;}
                outlineMoveFrame=ImGui.GetFrameCount();return;
            }
        }
        if(!activated)return;
        if(io.KeyCtrl)Editor.Toggle(id);else Editor.Select(id);
        RememberOutlineSelection(id);
        if(rows[row].Place&&ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))Run("editor.focus_in");
    }

    // Only visible rows and a pending keyboard target are labelled; no whole-outline drawing for range navigation.
    private void Outliner()
    {
        // Scene range navigation must not add a row or alter the established data-outline focus order.
        var scene=Editor.Session.Current is Scene;
        var wrapOutline=scene||Editor.Session.Current is Scene2;
        outlineHadFocus=scene&&ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if(scene)
        {
            var io=ImGui.GetIO();
            if(outlineHadFocus&&io.KeyCtrl&&!io.KeyAlt&&!io.KeySuper&&ImGui.IsKeyPressed(ImGuiKey.F,false))outlineSearchFocus=true;
            if(outlineSearchFocus){ImGui.SetKeyboardFocusHere();outlineSearchFocus=false;}
            Ui.TextWrapped("Search objects"u8);
            ImGui.SetNextItemWidth(-1);
            Ui.NameNextItem("##outline.search"u8,"Search objects");
            outlineSearchId=ImGui.GetID("##outline.search"u8);
            Ui.InputText("##outline.search"u8,outlineSearch,ImGuiInputTextFlags.EscapeClearsAll);
            const string help="Ctrl+F searches names, assets and properties. Escape clears. Shift+Up/Down extends selection. Ctrl+Space toggles the focused object.";
            var field=Ui.Tree.Count-1;Ui.Tree.Update(field,Ui.Tree.At(field) with{Tooltip=help});Tooltips.Show(help);
        }
        // Keep this scene count region above the clipped list, with one identity and count-only text.
        if(scene)
        {
            var selected=Editor.Selection.Count;
            Ui.StatusWrapped(selected==1?"1 object selected":$"{selected} objects selected","outliner.selection.count");
        }
        if(scene&&ImGui.GetContentRegionAvail().Y>=ImGui.GetFontSize()*12)Ui.TextWrapped("Shift+Up/Down extends selection. Ctrl+Space toggles the focused object."u8);
        var rows=scene?FilterOutline(OutlineRows()):OutlineRows();
        if(scene&&rows.Count==0&&Text(outlineSearch).Trim().Length>0)Ui.TextWrapped("No objects match this search."u8);
        if(scene&&ImGui.IsWindowFocused()&&!ReferenceEquals(outlineNavigationRows,rows))
        {
            // A removed/hidden keyboard row leaves a native NavId that Tab cannot find again.
            // Repair only this already-focused window, never take focus from an Inspector or popup.
            if(outlineFocusedRow is {} previous&&OutlineIndex(rows,previous)<0)
            {var primary=OutlineIndex(rows,Editor.Primary);outlineMoveTo=rows.Count==0?null:rows[primary>=0?primary:0].Id;outlineFocusedRow=null;}
            outlineNavigationRows=rows;
        }
        var rowHeight=wrapOutline?OutlineRowHeight(rows):0;
        var clipper=new ImGuiListClipper();clipper.Begin(rows.Count,wrapOutline?rowHeight+ImGui.GetStyle().ItemSpacing.Y:-1);
        if(sceneRename?.PendingId is {} renameId)
        {var index=OutlineIndex(rows,renameId);if(index>=0)clipper.IncludeItemByIndex(index);}
        if(scene&&outlineMoveTo is {} target)
        {var index=OutlineIndex(rows,target);if(index>=0)clipper.IncludeItemByIndex(index);else outlineMoveTo=null;}
        while(clipper.Step())
            for(var r=clipper.DisplayStart;r<clipper.DisplayEnd;r++)
            {
                var (id,depth,place)=rows[r];if(depth>0)ImGui.Indent(depth*14);
                if(sceneRename?.DrawRow(id)==true){if(depth>0)ImGui.Unindent(depth*14);continue;}
                var (shown,spoken,mark)=OutlineText(id,place);var rowLabel=shown+"##obj"+id;
                if(mark is not null)Ui.NameNextItem(rowLabel,spoken);
                var textAt=ImGui.GetCursorScreenPos();textAt.Y+=ImGui.Internal.GetCurrentWindow().DC.CurrLineTextBaseOffset;
                if(mark is {} colour)ImGui.PushStyleColor(ImGuiCol.Text,colour.TextColour);
                bool activated;
                try{activated=wrapOutline?Ui.SelectableWrapped(rowLabel,Editor.Selection.Contains(id),ImGuiSelectableFlags.AllowDoubleClick,rowHeight):Ui.Selectable(rowLabel,Editor.Selection.Contains(id),ImGuiSelectableFlags.AllowDoubleClick);}
                finally{if(mark is not null)ImGui.PopStyleColor();}
                if(mark is {} m)
                {
                    var node=Ui.Tree.Count-1;Ui.Tree.Update(node,Ui.Tree.At(node) with{Tooltip=m.Tooltip});Tooltips.Show(m.Tooltip);
                    m.Draw(textAt,textAt.X+(place?ImGui.CalcTextSize("> "u8).X:0),textAt.X+ImGui.CalcTextSize(shown).X);
                }
                if(scene&&(outlineMoveTo==id||sceneRename?.TakeFocusRow(id)==true))
                {
                    ImGui.Internal.SetFocusID(ImGui.GetItemID(),ImGui.Internal.GetCurrentWindow());ImGui.SetNavCursorVisible(true);
                    ImGui.SetScrollHereY();outlineMoveTo=null;
                }
                if(scene)
                {
                    if(ImGui.IsItemFocused())outlineFocusedRow=id;
                    OutlineSelectionKeys(rows,r,activated);
                }
                else if(activated)
                {
                    if(ImGui.GetIO().KeyCtrl)Editor.Toggle(id);else Editor.Select(id);
                    if(place&&ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))Run("editor.focus_in");
                }
                if(ObjectContextMenu.Draw(this,id) is {} message)status=message;
                if(depth>0)ImGui.Unindent(depth*14);
            }
        clipper.End();
    }

    // The outline as a tree, in the order it's drawn: each object under its parent, with its depth and whether it holds any.
    // Kept per document, outline version and focus; an outline with no version is worked out each frame.
    private IReadOnlyList<(string Id,int Depth,bool Place)>? readableOutline;
    private float readableOutlineWidth,readableOutlineFont,readableOutlineHeight;
    private float OutlineRowHeight(IReadOnlyList<(string Id,int Depth,bool Place)> rows)
    {
        var width=ImGui.GetContentRegionAvail().X;var font=ImGui.GetFontSize();
        if(ReferenceEquals(readableOutline,rows)&&readableOutlineWidth==width&&readableOutlineFont==font)return readableOutlineHeight;
        readableOutline=rows;readableOutlineWidth=width;readableOutlineFont=font;readableOutlineHeight=24*ImGui.GetStyle().FontScaleDpi;
        foreach(var (id,depth,place) in rows)readableOutlineHeight=MathF.Max(readableOutlineHeight,Ui.WrappedSelectableHeight(OutlineText(id,place).Shown,width-depth*14));
        return readableOutlineHeight;
    }
    // Visible row text, the accessible name and any prefab marking. A marked row's name is spoken with plain words
    // ("crate_1, copy of crate"); the drawn text stays the name alone, with room left for the copy icon.
    private (string Shown,string Spoken,OutlineMark? Mark) OutlineText(string id,bool place)
    {
        var prefix=place?"> ":"";var name=Label(id)+(OutlineBadges?.Invoke(id)??"");var mark=OutlineMarks?.Invoke(id);
        if(mark is not {} m)return(prefix+name,prefix+name,null);
        return(prefix+(m.Kind==OutlineMarkKind.PrefabCopy?OutlineMark.IconRoom():"")+name,prefix+name+", "+m.Spoken,m);
    }
    private IReadOnlyList<(string Id, int Depth, bool Place)> OutlineRows()
    {
        var doc = Editor.Session.Current;
        var version = Editor.Outline.Version;
        var focus = Editor.FocusedOn;
        if (version is { } v && outlineFor is { } known && ReferenceEquals(known.Doc, doc) && known.Version == v && known.Focus == focus) return known.Rows;
        var rows=OutlineOrder.Rows(Editor);
        outlineFor = version is { } kept ? (doc, kept, focus, rows) : null;
        return rows;
    }

    private (TDoc Doc, long Version, string? Focus, IReadOnlyList<(string Id, int Depth, bool Place)> Rows)? outlineFor;

    /// <summary>What finds places in a document, for the box above the inspector. None by default, so no box.</summary>
    public Func<TDoc, string, (IReadOnlyList<FoundPlace> Places, bool More)>? Searcher
    {
        get => inspectorController.Searcher;
        init => inspectorController.Searcher = value;
    }

    /// <summary>What the last search found. Empty once the document has changed, since an index may mean something else then.</summary>
    public IReadOnlyList<FoundPlace> Found => inspectorController.Found;

    /// <summary>Searches the document for <paramref name="text"/>. Changes nothing in it.</summary>
    public int Find(string text) => inspectorController.Find(text);

    /// <summary>Goes to a place in the document: selects what holds it, opens what's folded, turns to its page, shows it if it
    /// isn't set, and scrolls to it. A view action: the document, its undo and its unsaved state don't change. False, saying why
    /// in <see cref="Status"/>, when no row shows that place.</summary>
    public bool Reveal(string pointer) => inspectorController.Reveal(pointer);

    private bool inspectorFocusRequested;
    internal void RequestInspectorFocus() => inspectorFocusRequested = true;

    /// <summary>Where the inspector last drew a part of a row, in screen coordinates, or null when it didn't. For a host,
    /// a script or a test to reach a control by what it is rather than by a pixel position that moves with the layout.</summary>
    public float2? InspectorPlace(string rowId, InspectorPart part = InspectorPart.Field) =>
        inspectorController.InspectorPlace(rowId, part);

    /// <summary>How many text boxes the inspector is holding on to. A box out of sight comes back with the value it should show.</summary>
    public int FieldsHeld => inspectorController.FieldsHeld;

    // A box big enough for the text already there, with room to type more: a fixed size couldn't draw a long spell text at all (found by A123).
    internal static ImInputString TextBox(string text) => new(Math.Max(1024, System.Text.Encoding.UTF8.GetByteCount(text) + 1024), text);

    /// <summary>The most text, in bytes, a field's box holds; past it the field shows its size and points to the Text panel, which
    /// edits text of any length (found by A206: a 10 MB cell stopped the frame, past the frame text's limit).</summary>
    public const int MostInBox = 256 * 1024;

    // A field too long for a box, said instead of drawn: how long, and where to edit it.
    internal static bool TooLong(string text, out string said)
    {
        var bytes = text.Length > MostInBox / 4 ? System.Text.Encoding.UTF8.GetByteCount(text) : 0;
        said = bytes > MostInBox ? $"({bytes / 1024:N0} KB of text: edit it in the Text panel)" : "";
        return bytes > MostInBox;
    }

    private bool popupWasOpen;

    private void NotePopups() => popupWasOpen = ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

    /// <summary>The first sentence of <paramref name="text"/>: up to the first ". " (or "; ", or ": " when a list
    /// follows), or all of it when it's one sentence.</summary>
    public static string FirstSentence(string text)
    {
        foreach (var stop in new[] { ". ", "; ", ": " })
            if (text.IndexOf(stop, StringComparison.Ordinal) is var i and > 0) return text[..(i + (stop == ". " ? 1 : 0))];
        return text;
    }

    private void Palette()
    {
        var opening = paletteRequested;
        if (paletteRequested)
        {
            ImGui.SetNextWindowPos(new float2(mainRight.X - mainWidth / 2, mainRight.Y + 40), ImGuiCond.Always, new float2(0.5f, 0));   // centred, near the top
            ImGui.SetNextWindowSize(new float2(MathF.Min(520, mainWidth - 40), 0), ImGuiCond.Always);
            ImGui.OpenPopup("palette"u8);
            paletteRequested = false;
            paletteHighlight = 0; paletteRefusal = "";
        }
        if (!Ui.BeginPopup("palette"u8))
        {
            paletteShowing = false;   // closed from outside (a click elsewhere)
            return;
        }
        if (paletteCloseRequested)
        {
            ImGui.CloseCurrentPopup();
            paletteCloseRequested = paletteShowing = false;
            Ui.EndPopup();
            return;
        }
        ImGui.SetKeyboardFocusHere();
        Ui.NameNextItem("##find"u8, "Find command");
        Ui.InputText("##find"u8, paletteText);
        // The search box keeps the keyboard, so the results are chosen from it (found by A031): Down/Up or Tab/Shift+Tab
        // move over every visible result. Enter executes an available command or explains the selected refusal.
        var query = Text(paletteText);
        // Inventory retains registered panel IDs; discovery and keyboard selection honor presentation visibility.
        var hidden = UiActions().Where(a => !a.Present(UiActionContext.Palette).Visible).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var entries = PaletteEntries(query).Where(e => !hidden.Contains(e.Id)).ToList();
        var selectionChanged = opening || query != paletteQuery;
        if (query != paletteQuery) { paletteQuery = query; paletteHighlight = 0; }
        var shift = ImGui.GetIO().KeyShift;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true) || (ImGui.IsKeyPressed(ImGuiKey.Tab, true) && !shift)) { paletteHighlight++; selectionChanged = true; }
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true) || (ImGui.IsKeyPressed(ImGuiKey.Tab, true) && shift)) { paletteHighlight--; selectionChanged = true; }
        if (entries.Count > 0) paletteHighlight = (paletteHighlight % entries.Count + entries.Count) % entries.Count;
        var highlighted = entries.Count > 0 ? entries[paletteHighlight].Id : null;
        if (selectionChanged) paletteRefusal = "";
        if (highlighted is not null && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)))
        {
            if (entries[paletteHighlight].Unavailable is { } why) status = paletteRefusal = why;
            else
            {
                InvokeUiAction(highlighted);
                ImGui.CloseCurrentPopup();
                paletteShowing = false;
                Ui.EndPopup();
                return;
            }
        }
        foreach (var (id, label, shortcut, unavailable) in entries)
        {
            Ui.BeginDisabled(unavailable is not null, unavailable);
            var text = shortcut is null ? label : $"{label}    {shortcut}";
            if (Ui.Selectable($"{text}##pal{id}", id == highlighted))
            {
                InvokeUiAction(id);
                ImGui.CloseCurrentPopup();
                paletteShowing = false;
            }
            if (id == highlighted && selectionChanged) ImGui.SetScrollHereY();
            if (unavailable is not null) Ui.FieldCaption($"({unavailable})");
            Ui.EndDisabled();
        }
        if (entries.Count == 0) Ui.TextWrapped("No matching commands."u8);
        if (paletteRefusal.Length > 0)
        {
            ImGui.PushTextWrapPos(0); Ui.Status(paletteRefusal, "palette.command.status"); ImGui.PopTextWrapPos();
        }
        Ui.EndPopup();
    }
}

/// <summary>A control on an inspector row.</summary>
public enum InspectorPart
{
    /// <summary>The value's field.</summary>
    Field,

    /// <summary>The list's Add button.</summary>
    Add,

    /// <summary>The list item's Remove button.</summary>
    Remove,

    /// <summary>A long list's button for the page before.</summary>
    Earlier,

    /// <summary>A long list's button for the page after.</summary>
    Later,

    /// <summary>A long list's button for its first page.</summary>
    First,

    /// <summary>A long list's button for its last page.</summary>
    Last,

    /// <summary>A long list's box: type an item's number and press Enter to go to its page.</summary>
    GoTo,

    /// <summary>The box naming a new entry, in an object of named entries.</summary>
    NewName,

    /// <summary>A long object's button showing or hiding the fields it doesn't have set.</summary>
    Unset,

    /// <summary>The box above the inspector that searches the file. Reached with an empty row id.</summary>
    Find,
}
