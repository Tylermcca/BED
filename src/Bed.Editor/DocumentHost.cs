using System.Runtime.CompilerServices;
using Bed.Data;

namespace Bed.Editor;

/// <summary>One scene session for an explicitly bound project file. All consumers share its editor and history.</summary>
public sealed class SceneDocumentEntry
{
    internal SceneDocumentEntry(DocumentSession<Scene> session) => Editor = new(session, SceneEditing.Outline);
    /// <summary>The authoritative editor, with the existing scene command catalog and validator.</summary>
    public EditorModel<Scene> Editor { get; }
    /// <summary>The authoritative scene session, saved through this host and its typed project policy.</summary>
    public DocumentSession<Scene> Session => Editor.Session;
}

/// <summary>UI-thread-owned typed routing for one DataProject. It never infers document kind from extensions or contents.</summary>
public sealed partial class DocumentHost
{
    private static readonly ConditionalWeakTable<DataProject, DocumentHost> hosts = new();
    private readonly DataProject project;
    private readonly Dictionary<string, string> bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SceneDocumentEntry> scenes = new(StringComparer.Ordinal);
    private readonly HashSet<string> opening = new(StringComparer.Ordinal);
    private DocumentHost(DataProject project) {this.project=project;project.TypedSaveAll=SaveMixedGroup;}

    /// <summary>Gets the single host associated with this project, without keeping otherwise unreachable projects alive.</summary>
    public static DocumentHost For(DataProject project) => hosts.GetValue(project, p => new(p));

    /// <summary>Binds an admitted name to an explicit scene format kind before any data open. Failed scene reads retain this route.</summary>
    public bool BindScene(string admittedName, string expectedKind, out string why)
    {
        if (bindings.TryGetValue(admittedName, out var old))
        { why = old == expectedKind ? "" : $"{admittedName} is already bound to {old}."; return old == expectedKind; }
        if (!project.ReserveTypedDocument(admittedName, expectedKind, out why)) return false;
        bindings.Add(admittedName, expectedKind);
        return true;
    }

    private bool linkedPlayDeclared;

    /// <summary>Records the adapter's admitted linked declaration for this project. The UI host calls this only
    /// after binding its linked host and game-owned converter. This starts nothing and leaves scene save policy unchanged.</summary>
    public void DeclareLinkedPlay(GameLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        linkedPlayDeclared = true;
    }
    private AssetCatalog? assets;

    /// <summary>Injects this admission's catalog once. The producer owns acquisition, refresh and disposal;
    /// this host never scans paths or infers freshness. Rebinding to a different catalog is refused.</summary>
    public bool BindAssetCatalog(AssetCatalog catalog, out string why)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (assets is not null && !ReferenceEquals(assets, catalog))
        { why = "This document host already has an asset catalog."; return false; }
        assets = catalog; why = ""; return true;
    }

    /// <summary>Captures a registered selection and the currently open scene for delayed placement.</summary>
    public bool CaptureAssetPlacement(string name, AssetCatalog.Selection selection, out SceneAssetPlacement? placement, out string why, string? parent = null)
    {
        placement = null;
        if (assets is null) { why = "No assets are available for this project."; return false; }
        if (!scenes.TryGetValue(name, out var entry)) { why = "Open the level first."; return false; }
        if (parent is not null && entry.Session.Current.Find(parent) is null) { why = "The selected parent no longer exists."; return false; }
        if (!assets.TryResolve(selection, out _, out why)) return false;
        placement = new(this, name, entry, entry.Session.Revision, selection, parent);
        return true;
    }

    /// <summary>Revalidates a captured selection, then adds one object at the root or captured parent through the existing command/undo path.
    /// Resolution is the acceptance point for pinned catalog content, not proof of live filesystem freshness.</summary>
    public bool PlaceAsset(SceneAssetPlacement placement, string id, System.Numerics.Vector3 position, out string why)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!ReferenceEquals(placement.Owner, this) || !scenes.TryGetValue(placement.Name, out var entry)
            || !ReferenceEquals(entry, placement.Entry))
        { why = "The target scene admission changed; start placement again."; return false; }
        if (checking) { why = "A scene save check is in progress."; return false; }
        if (project.ReadOnlyReason(placement.Name) is { } readOnly) { why = readOnly; return false; }
        if (placement.Parent is null && entry.Editor.FocusedOn is not null) { why = "Step out of focus before placing a root object."; return false; }
        if (entry.Session.Revision != placement.Revision) { why = "The scene changed; start placement again."; return false; }
        if (assets is null) { why = "No assets are available for this project."; return false; }
        if (!assets.TryResolve(placement.Selection, out var lease, out why)) return false;
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        for (var parent = placement.Parent; parent is not null;)
        {
            if (parent == id || !ancestors.Add(parent)) { why = "Placement would create a parent cycle."; return false; }
            if (entry.Session.Current.Find(parent) is not { } ancestor) { why = "The selected parent no longer exists."; return false; }
            parent = ancestor.Parent;
        }
        var command = new AddObject(SceneObject.At(id, placement.Selection.AssetGuid,
            placement.Parent is null ? position : System.Numerics.Vector3.Zero) with { Parent = placement.Parent });
        var ok = entry.Editor.ExecuteAt(placement.Revision, [command], placement.Parent is null ? [] : [placement.Parent]);
        // Keep the accepted immutable bytes alive through the command, even if the producer refreshes afterwards.
        GC.KeepAlive(lease);
        why = ok ? $"Placed {id}." : "Placement was refused by the scene command or current editor state.";
        if (ok) entry.Editor.Select(id);
        return ok;
    }

    /// <summary>A scene-specific save decision over the actual typed draft. Null means no extra scene rule;
    /// a legacy DataProject.SaveCheck still requires an explicit scene policy rather than receiving a null data session.</summary>
    public Func<SceneDocumentEntry, string?>? SceneSaveCheck { get; set; }
    private bool checking;

    /// <summary>Gets a currently open entry without opening a file or changing its lifetime.</summary>
    public bool TryGetOpenScene(string name, out SceneDocumentEntry? entry) => scenes.TryGetValue(name, out entry);

    /// <summary>Saves one current scene through validation, typed checks and expected-disk conflict protection.
    /// Refusals keep the draft and history. This is not a mixed-document SaveAll transaction.</summary>
    public bool SaveScene(string name, out string why)
    {
        if (checking || project.Judging) { why = "A save check is already in progress."; return false; }
        if (!scenes.TryGetValue(name, out var entry)) { why = $"{name} has no open scene session."; return false; }
        if (entry.Editor.Playing is not null) { why = "Stop Play before saving."; return false; }
        if (project.ReadOnlyReason(name) is { } readOnly) { why = readOnly; return false; }
        if (project.SaveCheck is not null && SceneSaveCheck is null)
        { why = "This project has a data save check; an explicit scene save policy is required."; return false; }
        var session = entry.Session;
        var revision = session.Revision;
        var policy = SceneSaveCheck;
        checking = true;
        try
        {
            var issues = session.Validator.Validate(session.Current);
            if (issues.HasErrors()) { why = string.Join(" ", issues.Where(i => i.Severity == Severity.Error).Select(i => i.Message)); return false; }
            if (policy?.Invoke(entry) is { } refused) { why = refused; return false; }
            if (!scenes.TryGetValue(name, out var current) || !ReferenceEquals(current, entry)
                || session.Revision != revision || !ReferenceEquals(policy, SceneSaveCheck))
            { why = "The scene or its policy changed while being checked; save again for a fresh decision."; return false; }
            if (project.Judging) { why = "A data save check started while the scene was checked; nothing was saved."; return false; }
            if (project.ReadOnlyReason(name) is { } nowReadOnly) { why = nowReadOnly; return false; }
            if (project.SaveCheck is not null && SceneSaveCheck is null)
            { why = "An explicit scene save policy is required."; return false; }
            if (session.CheckDisk() != DiskState.Unchanged)
            { why = "The level was changed or removed outside BED; nothing was saved. Your unsaved changes are kept; choose which version to keep."; return false; }
            if (!session.Dirty) { why = "Nothing to save."; return true; }
            var text = session.TextToSave;
            var result = FileGroup.Write([new GroupFile(session.FilePath!, session.ExpectedOnDisk, text)]);
            if (result.States[0] == GroupFileState.Written) session.MarkSaved(text);
            if (result.Failed is not null || result.States[0] != GroupFileState.Written)
            { why = result.Why ?? "The scene could not be saved; check the file before trying again."; return false; }
            why = $"Saved {name}.";
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { why = $"The scene save failed: {e.Message}. The draft is retained."; return false; }
        finally { checking = false; }
    }

    /// <summary>Closes one scene only after an explicit decision. Cancel and failed Save retain its exact entry.
    /// The binding remains reserved; reopening creates a fresh session rather than reviving cached views.</summary>
    public bool CloseScene(string name, SceneCloseDecision decision, out string why)
    {
        if (checking || project.Judging) { why = "A save check is in progress; close is refused."; return false; }
        if (!scenes.TryGetValue(name, out var entry)) { why = $"{name} has no open scene session."; return false; }
        if (decision == SceneCloseDecision.Cancel) { why = "Close cancelled."; return false; }
        if (decision is not (SceneCloseDecision.Save or SceneCloseDecision.Discard)) { why = "Choose Save, Discard or Cancel."; return false; }
        if (decision == SceneCloseDecision.Save && !SaveScene(name, out why)) return false;
        try { entry.Editor.Stop(); }
        catch (Exception e) { why = $"Play could not stop cleanly: {e.Message}. The scene remains open."; return false; }
        scenes.Remove(name);
        why = $"Closed {name}.";
        return true;
    }

    /// <summary>Whether any scene route is bound, including unopened or refused files.</summary>
    public bool HasSceneBindings => bindings.Count != 0;

    /// <summary>Whether this name has an explicit scene route, including one whose last parse failed.</summary>
    public bool IsScene(string name) => bindings.ContainsKey(name);

    /// <summary>Opens or reuses a scene. Failure publishes no entry and leaves all existing drafts untouched.</summary>
    public bool OpenScene(string name, out SceneDocumentEntry? entry, out string why)
    {
        entry = null;
        if(scene2.TryGetValue(name,out _)){why=$"{name} already has a scene-v2 authority; use OpenScene2.";return false;}
        if (!bindings.TryGetValue(name, out var kind)) { why = $"{name} has no explicit scene binding."; return false; }
        if (scenes.TryGetValue(name, out entry)) { why = ""; return true; }
        if (project.Find(name) is not { } file) { why = $"There's no file called {name} in this project."; return false; }
        if (!opening.Add(name)) { why = $"{name} is already opening."; return false; }
        try
        {
            // Open either returns a complete validated session or throws before publication; it owns no disposable resource.
            var session = DocumentSession<Scene>.Open(file.Path, new SceneFormat(kind), new SceneValidator(), SceneKit.Catalog());
            entry = new SceneDocumentEntry(session);
            entry.Editor.PlayRefusal = () => linkedPlayDeclared ? null : "Use project Play from Tools > Play or the toolbar. Configure the program in Tools > Play settings.";
            scenes.Add(name, entry);
            why = "";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DocumentFormatException or InvalidDocumentException)
        { why = $"{name} can't be opened as {kind}: {e.Message}"; return false; }
        finally { opening.Remove(name); }
    }
}

/// <summary>An explicit local close decision, never inferred from switching documents.</summary>
public enum SceneCloseDecision
{
    /// <summary>Save through the scene policy before closing; refusal keeps the entry.</summary>
    Save,
    /// <summary>Explicitly discard the in-memory draft, leaving disk untouched.</summary>
    Discard,
    /// <summary>Keep the entry, draft and history.</summary>
    Cancel,
}

/// <summary>Opaque delayed placement target. Dropping it without committing cancels without changing the scene.</summary>
public sealed class SceneAssetPlacement
{
    internal SceneAssetPlacement(DocumentHost owner, string name, SceneDocumentEntry entry, long revision, AssetCatalog.Selection selection, string? parent)
    { Owner=owner; Name=name; Entry=entry; Revision=revision; Selection=selection; Parent=parent; }
    internal DocumentHost Owner { get; }
    internal string Name { get; }
    /// <summary>The admitted target name, for active-view routing only.</summary>
    public string DocumentName => Name;
    internal SceneDocumentEntry Entry { get; }
    internal long Revision { get; }
    internal AssetCatalog.Selection Selection { get; }
    internal string? Parent { get; }
}
