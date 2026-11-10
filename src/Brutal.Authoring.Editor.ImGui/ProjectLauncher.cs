using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Where a project's files are: its data folder, its schema folder, and optionally its link map.</summary>
/// <param name="Data">The folder of JSON data files.</param>
/// <param name="Schemas">The folder of their JSON Schemas.</param>
/// <param name="Map">A reference map file (links between items), or null.</param>
public sealed record ProjectPaths(string Data, string Schemas, string? Map = null)
{
    /// <summary>Explicit project root for settings, saved cache and Play; independent of nested data folders.</summary>
    public string? Root {get;init;}

    /// <summary>The original folder for adapter-discovered paths; null for explicitly chosen folders.</summary>
    public string? Folder { get; init; }

    /// <summary>An external adapter remembered for a recent entry; null for older/plain entries.</summary>
    public string? AdapterPath { get; init; }
    /// <summary>The exact public adapter type in that DLL, not whichever type happens to be discovered first.</summary>
    public string? AdapterType { get; init; }

    /// <summary>The usual layout inside one folder: <c>data/</c> and <c>schemas/</c>, and a <c>reference_map.json</c> if
    /// there is one. Null when the folder doesn't hold both.</summary>
    public static ProjectPaths? In(string folder)
    {
        var (data, schemas) = (Path.Combine(folder, "data"), Path.Combine(folder, "schemas"));
        if (!Directory.Exists(data) || !Directory.Exists(schemas)) return null;
        var map = Path.Combine(folder, "reference_map.json");
        return new(Path.GetFullPath(data), Path.GetFullPath(schemas), File.Exists(map) ? Path.GetFullPath(map) : null);
    }
}

/// <summary>
/// The projects opened lately, newest first, kept in a small JSON file in the person's settings folder. A missing or
/// unreadable file is an empty list, never an error; entries whose folders have gone stay listed (marked by the
/// launcher) until removed. Each entry is read on its own, so a bad one is dropped and the good ones around it stay. A
/// change is made to the list as it is on disk at that moment, under a lock, and written whole, so two launchers don't
/// lose each other's entries, and a list that couldn't be read is never written over (found by A203). MIXED: every
/// editor's "recent projects" (Godot's project manager, Blender's recent files).
/// </summary>
public sealed class RecentProjects
{
    private readonly RecentProjectStore store;

    /// <param name="file">Where the list is kept; it's created on the first saved change.</param>
    /// <param name="limit">How many are kept.</param>
    public RecentProjects(string file, int limit = 10) => store = new(file, limit);

    /// <summary>The settings file.</summary>
    public string File => store.File;

    /// <summary>How many are kept.</summary>
    public int Limit => store.Limit;

    /// <summary>The projects, newest first.</summary>
    public IReadOnlyList<ProjectPaths> Items => store.Items;

    /// <summary>The settings folder for this person: <c>%APPDATA%\brutal-authoring</c> on Windows.</summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "brutal-authoring");

    /// <summary>Puts <paramref name="project"/> first (moving it if it's there) and saves the list.</summary>
    public void Add(ProjectPaths project) => store.Add(project);

    /// <summary>Returns optional open time and current display kind without changing the stored list.</summary>
    public RecentProjectMetadata Metadata(ProjectPaths project) => store.Metadata(project);

    /// <summary>Records a successful open with its display kind and the current UTC time.</summary>
    public void Opened(ProjectPaths project, string kind) => store.Opened(project, kind);

    /// <summary>Takes <paramref name="project"/> off the list and saves it. Its files are untouched.</summary>
    public void Remove(ProjectPaths project) => store.Remove(project);
}

/// <summary>
/// The general app's start: open any project with no code, by its folders, from the recent list, or from a folder laid
/// out with <c>data/</c> and <c>schemas/</c>; then the full <see cref="DataWorkspace"/>, and back to the start when it's
/// closed. Every refusal says what's wrong and what to do. A game's adapter is optional: <see cref="Adapter"/> shapes the
/// project (read-only files, its own rules, panels) when one is loaded, and without one any data with schemas opens.
/// MIXED: Godot's project manager and Blender's splash screen, over the data workspace.
/// </summary>
public sealed partial class ProjectLauncher : IDisposable
{
    /// <summary>Stops any game running in the open project (<see cref="DataWorkspace.Dispose"/>), for a host shutting down. The
    /// project stays open and nothing is saved or written.</summary>
    public void Dispose() => Workspace?.Dispose();

    private ImInputString dataText = new(1024, ""u8);
    private ImInputString schemasText = new(1024, ""u8);
    private ImInputString mapText = new(1024, ""u8);
    /// <summary>Optional host-owned dialogs; null retains the existing ImGui browser.</summary>
    public IFileDialogs? FileDialogs { get; set; }
    /// <summary>App-owned asset startup acquisition, absent in hosts that do not support it.</summary>
    public IAssetCatalogProvisioner? AssetCatalogProvisioner { get; set; }
    /// <summary>App-owned linked host, set before opening. Null keeps existing in-process Play only.</summary>
    public ILinkedPlayHost? LinkedPlayHost { get; set; }
    /// <summary>Common saved-files Play factory, for adapter and plain projects alike.</summary>
    public IProjectPlayHost? ProjectPlayHost {get;set;}
    /// <summary>App-owned actual build and smoke bridge.</summary>
    public IGameExportHost? GameExportHost {get;set;}

    private readonly FolderPicker picker = new();
    private readonly Shortcuts shortcuts=new();
    private int picking = -1;
    private bool closeAnyway;

    private float? textScale;   // the person's text size, applied once a frame can see the style, then kept in step

    /// <param name="recent">The recent projects list.</param>
    /// <param name="sample">A sample project to offer a newcomer (the release ships one), or null.</param>
    public ProjectLauncher(RecentProjects recent, ProjectPaths? sample = null, ProjectCreator? creator = null, string? projectsFolder = null)
    {
        Recent = recent;
        Sample = sample;
        projectCreator = creator ?? new ProjectCreator();
        personalProjects = projectsFolder ?? ProjectCreator.DefaultLocation ?? "";
        projectLocation = new ImInputString(2048, personalProjects);
        // Prefill only; opening still requires the person's action. Keep the existing settings location.
        var initial = Recent.Items.FirstOrDefault(p => Directory.Exists(p.Data) && Directory.Exists(p.Schemas)) ?? sample;
        if (initial is not null) FillPaths(initial);
        ReadPreferences();
        KeysPanel.LoadPreferences(shortcuts,Path.Combine(Path.GetDirectoryName(SettingsFile)!,"shortcuts.json"));
    }

    /// <summary>The settings file beside the recent list: the person's text size and app theme.</summary>
    public string SettingsFile => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Recent.File))!, "settings.json");

    // Applies remembered UI preferences on the first frame and merges only changed settings afterward.
    // Keep this call site before either the launcher or an open workspace draws.
    private void KeepTextSize() => KeepPreferences();

    private bool textApplied;

    /// <summary>The recent projects.</summary>
    public RecentProjects Recent { get; }

    /// <summary>The sample project offered on the start screen, if any.</summary>
    public ProjectPaths? Sample { get; }

    /// <summary>The adapter chosen for the next project: null chooses automatically, the first loaded adapter that
    /// recognises it, or none.</summary>
    public IProjectAdapter? Adapter { get; set; }

    /// <summary>Whether no adapter is used even when one recognises the project (plain data with schemas).</summary>
    public bool NoAdapter { get; set; }

    /// <summary>The game adapters loaded (<see cref="Adapters.Load"/>): optional extras, never needed to open a project.</summary>
    public IReadOnlyList<IProjectAdapter> Loaded { get; set; } = [];

    /// <summary>Why an adapter didn't load, shown on the start screen.</summary>
    public IReadOnlyList<string> AdapterProblems { get; set; } = [];

    /// <summary>Called with each project's workspace as it's made, before any file is shown: where a host adds its extra
    /// per-file panels (<see cref="DataWorkspace.FilePanels"/>), as the general app adds the Text panel.</summary>
    public Action<DataWorkspace>? SetUpWorkspace { get; set; }

    /// <summary>The adapter the open project uses, or null.</summary>
    public IProjectAdapter? Using { get; private set; }

    /// <summary>The open project's workspace, or null on the start screen.</summary>
    public DataWorkspace? Workspace { get; private set; }

    /// <summary>The BRUTAL logo shown before the start screen (the licence's 2.2), or null for none: the program turns it
    /// on for its window; tests and headless pictures leave it off.</summary>
    public BrutalSplash? Splash { get; set; }

    /// <summary>The open project's paths, or null.</summary>
    public ProjectPaths? Current { get; private set; }

    /// <summary>What the last action did, or why it didn't.</summary>
    public string Said { get; private set; } = "";

    /// <summary>Opens a project and shows its workspace, adding it to the recent list. Returns false, saying what's
    /// wrong and what to do, when a folder is missing, the schema folder holds no schemas, a schema or the map can't be
    /// read, or a project is already open with unsaved edits.</summary>
    public bool Open(ProjectPaths project) => OpenWith(project, NoAdapter, Adapter, Loaded);

    /// <summary>Reopens a recent entry with its exact remembered adapter. Missing/failed adapters open plain only when standard or explicit paths are available; otherwise opening is refused.</summary>
    public bool OpenRecent(ProjectPaths project)
    {
        // Do not load code merely to discover that dirty drafts prevent changing projects.
        if (!ConfirmBefore(() => OpenRecent(project))) return false;
        if (string.IsNullOrEmpty(project.AdapterPath))
        {
            if (project.Folder is not { } root) return Open(project); // old settings keep their existing behavior
            var provider = Loaded.FirstOrDefault(a => a.GetType().FullName == project.AdapterType);
            if (provider is null && ProjectPaths.In(root) is null)
                return Say(false, $"this project was opened with {project.AdapterType ?? "an adapter"}, which isn't available");
            return OpenFolderWith(root, project.Map, provider is null, provider, [], project);
        }
        IProjectAdapter? remembered = null;
        string? notice = null;
        if (!File.Exists(project.AdapterPath))
            notice = $"The saved game connection at {project.AdapterPath} is missing. The project opened without that game connection (Data only); its location is kept for next time.";
        else
        {
            var (loaded, problems) = Adapters.Load([project.AdapterPath]);
            remembered = loaded.FirstOrDefault(a => a.GetType().FullName == project.AdapterType);
            if (remembered is null)
                notice = $"The saved game connection could not be loaded from {project.AdapterPath}. " +
                    "The project opened without that game connection; its details are kept for next time." +
                    (problems.Count == 0 ? "" : " " + string.Join(" ", problems));
        }
        if (project.Folder is { } original)
        {
            if (remembered is null && ProjectPaths.In(original) is null)
                return Say(false, $"this project was opened with {project.AdapterType ?? project.AdapterPath}, which isn't available");
            return OpenFolderWith(original, project.Map, remembered is null, remembered, [], project, notice);
        }
        // Never substitute a different automatic/global adapter when the remembered one is unavailable.
        return OpenWith(project, remembered is null, remembered, [], project, notice);
    }

    private bool OpenWith(ProjectPaths project, bool noAdapter, IProjectAdapter? selected,
        IReadOnlyList<IProjectAdapter> candidates, ProjectPaths? remembered = null, string? initialNotice = null)
    {
        if (!ConfirmBefore(() => OpenWith(project, noAdapter, selected, candidates, remembered, initialNotice))) return false;
        if (ProjectLoader.Open(project, noAdapter, selected, candidates, out var refusal) is not { } prepared)
            return Say(false, refusal);
        var (data, map, adapter, label, full, notes) = prepared;
        if (initialNotice is not null) notes.Add(initialNotice);
        // Everything is made first, and only then shown, listed and made current: a fault on the way leaves the launcher as it was,
        // never a half-made project (found by A200).
        DataWorkspace workspace;
        DataWorkspace? pendingWorkspace = null;
        int refused;
        // Resolve before adapter redirection: nested opened data must not move the project root.
        var projectRoot=Path.GetFullPath(full.Root??full.Folder??Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full.Data))!);
        try
        {
            // The adapter's per-file setup is given the workspace and file too (IProjectAdapter's default passes them by).
            workspace = pendingWorkspace = new DataWorkspace(data,shortcuts:shortcuts,map:map) { CloseProject = () => Close(), RecentProjectsMenu=DrawRecentMenu, ConfigureFile = adapter is { } a ? a.Configure : null };
            try
            {
                var admitted = new ProjectPaths(data.DataFolder, data.SchemaFolder, full.Map){Root=projectRoot,Folder=full.Folder};
                if (AssetCatalogProvisioner?.Open(admitted, adapter?.AssetsFolder(admitted)) is { } assets)
                    workspace.BindProjectAssets(assets);
            }
            catch (Exception e) { notes.Add("Assets are unavailable: " + e.Message); }
            workspace.BindPlaySettings(projectRoot,
                adapter?.DefaultPlay(new ProjectPaths(data.DataFolder,data.SchemaFolder,full.Map){Root=projectRoot,Folder=full.Folder}),
                Path.GetDirectoryName(SettingsFile)!,()=>FileDialogs);
            if(ProjectPlayHost is {} projectPlay)workspace.BindProjectPlay(projectPlay);
            // Capture actual opened folders and resolved map, not recent-entry guesses or adapter-sidecar inference.
            // Panel availability follows the existing per-file editor: an empty workspace has no file panel yet.
            try
            {
                var adapterFile = adapter is null ? null : AdapterFile(adapter);
                var ai = new AiSetupPanel(AppContext.BaseDirectory,
                    new ProjectPaths(data.DataFolder, data.SchemaFolder, full.Map) { AdapterPath = adapterFile },
                    adapter is not null && adapterFile is null ? AiSetupPanel.BuiltInReason : null);
                workspace.FilePanels.Add((_, _) => ai.AsPanel());
            }
            catch (Exception e) { notes.Add($"AI setup examples are unavailable: {e.Message}"); }
            // The host's own setup is guarded too: a fault in it costs that setup, never the project (found by A188).
            if(adapter is not null&&GameExportHost is {} exportHost&&adapter.Build(workspace) is {} build&&adapter.Export(workspace) is {} export)
            {
                workspace.BindExport(build,export,exportHost);
                workspace.BindExportPreferences(Path.GetDirectoryName(Path.GetFullPath(Recent.File))!);
            }
            try { SetUpWorkspace?.Invoke(workspace); }
            catch (Exception e) { notes.Add($"The editor's own workspace setup failed: {e.Message}. The project is open; some panels or settings may be missing."); }
            if (adapter is not null)
            {
                try { workspace.Checks.Add([.. adapter.Checks]); }
                catch (Exception e) { notes.Add($"{label}'s checks weren't added: {e.Message}"); }   // all or none (found by A073)
            }
            // Typed scenes remain listed until explicitly chosen; their reserved DataFile isn't a refused data open.
            refused = data.Files.Count(file => !workspace.Documents.IsScene(file.Name) && !file.Open());
        }
        catch (Exception e) when (adapter is not null)
        {
            pendingWorkspace?.ReleaseProjectAssets();
            return Say(false, $"{label} failed while the project was opening: {e.Message}. That's a fault in the game connection, not your data: choose Data only to open it without.");
        }
        // The project being replaced stops its game first: nothing may run on from it (B4).
        if (Workspace?.StopAll().Fault is { Length: > 0 } stopFault) notes.Add(stopFault);   // said with the new project's message
        Workspace?.ReleaseProjectAssets();
        Workspace = workspace;
        Current = full;
        Using = adapter;
        closeAnyway = false;
        // Keep explicit/root map provenance, never the loader's inferred adapter sidecar.
        // Use the actual opened folders: a game's adapter may translate its root to a nested data folder.
        // Remember that root only when redirection would otherwise lose it; plain entries keep their existing shape.
        var recent = new ProjectPaths(data.DataFolder, data.SchemaFolder,
            AdapterMap.Resolve(project.Map, project.Data, null,project.Root??project.Folder)){Root=full.Root??(!string.Equals(full.Data,data.DataFolder,StringComparison.OrdinalIgnoreCase)?projectRoot:null)};
        if (remembered?.AdapterPath is not null || remembered?.AdapterType is not null)
            recent = recent with { AdapterPath = remembered.AdapterPath, AdapterType = remembered.AdapterType };
        else if (adapter is not null && ExternalAdapterPath(adapter) is { } external)
            recent = recent with { AdapterPath = external, AdapterType = adapter.GetType().FullName };
        if (project.Folder is { } originalFolder)
            recent = recent with { Data = "", Schemas = "", Map = remembered?.Map, Folder = originalFolder,
                AdapterType = adapter?.GetType().FullName ?? remembered?.AdapterType };
        Recent.Opened(recent, adapter is null ? "Data only" : $"{label} connection");
        // The first file that opens is shown, so the whole editor is there at once rather than a lone file list.
        if (data.Files.FirstOrDefault(f => f.State == DataFileState.Open) is { } first) Workspace.Show(first.Name);
        if (Workspace.AdapterProblem is { } setup) notes.Add(setup);
        var noted = notes.Count == 0 ? "" : " " + string.Join(" ", notes);
        if (noted.Length > 0) Workspace.Note(noted.Trim());   // the workspace is what the person sees next
        return Say(true, $"Opened {Name(full)}{(adapter is null ? "" : $" with {label}")}: {data.Files.Count} files"
            + (refused > 0 ? $", {refused} that can't open (the Files panel says why)." : "; choose another in the Files panel.") + noted);
    }

    private static string? AdapterFile(IProjectAdapter adapter)
    {
        // An adapter chosen from a file has a real Location; one built into the single-file app has "", meaning no file.
#pragma warning disable IL3000
        var location = adapter.GetType().Assembly.Location;
#pragma warning restore IL3000
        return string.IsNullOrEmpty(location) ? null : Path.GetFullPath(location);
    }

    private static string? ExternalAdapterPath(IProjectAdapter adapter)
    {
        if (AdapterFile(adapter) is not { } full) return null;
        var builtIn = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "adapters"));
        var relative = Path.GetRelativePath(builtIn, full);
        return Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full : null;
    }

    // Shared by loading and the forms, with identical diagnostic fallback behavior.
    private static string NameOf(IProjectAdapter adapter, List<string>? notes = null) => ProjectLoader.NameOf(adapter, notes);
    private static bool Recognised(IProjectAdapter adapter, ProjectPaths project, List<string>? notes = null) =>
        ProjectLoader.Recognised(adapter, project, notes);

    /// <summary>A project's name for people: its data folder's name, or the folder holding it when that's just "data".</summary>
    public static string Name(ProjectPaths project)
    {
        string data;
        try { data = Path.GetFullPath(project.Folder ?? project.Data).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return project.Data; }   // shown as written (A203)
        var name = Path.GetFileName(data);
        return string.Equals(name, "data", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(Path.GetDirectoryName(data)) is { Length: > 0 } parent ? parent : name;
    }

    /// <summary>Opens the project laid out in <paramref name="folder"/> (<see cref="ProjectPaths.In"/>). Returns false,
    /// saying what's expected, when it doesn't hold <c>data/</c> and <c>schemas/</c>.</summary>
    public bool OpenFolder(string folder) => OpenFolder(folder, null);

    /// <summary>Opens a standard or adapter-recognised folder, with an explicitly supplied map taking precedence.</summary>
    public bool OpenFolder(string folder, string? explicitMap) =>
        OpenFolderWith(folder, explicitMap, NoAdapter, Adapter, Loaded);

    private bool OpenFolderWith(string folder, string? explicitMap, bool noAdapter, IProjectAdapter? selected,
        IReadOnlyList<IProjectAdapter> candidates, ProjectPaths? remembered = null, string? notice = null)
    {
        if (!ConfirmBefore(() => OpenFolderWith(folder, explicitMap, noAdapter, selected, candidates, remembered, notice))) return false;
        if (FolderProjects.Find(folder, explicitMap, noAdapter, selected, candidates, out var why) is not { } found)
            return Say(false, why);
        var metadata = remembered ?? (found.Paths.Folder is null ? null : new ProjectPaths("", "", explicitMap));
        var paths = remembered?.Folder is { } original ? found.Paths with { Folder = original,Root=found.Paths.Root??remembered.Root } : found.Paths;
        // A provider recent remains folder-based even if a later standard layout wins discovery.
        return OpenWith(paths, noAdapter, found.Adapter, candidates, metadata, notice);
    }

    /// <summary>Closes the project and returns to the start screen. Refused, saying so, while files have unsaved edits,
    /// unless <paramref name="discard"/>.</summary>
    public bool Close(bool discard = false)
    {
        if (Workspace is not { } open) return Say(false, "No project is open.");
        if (open.Documents.HasSceneBindings) return Say(false, "Typed scenes need their own save/discard decision before closing this project.");
        if (!discard && !ConfirmBefore(() => Close())) return false;
        var fault = open.StopAll().Fault;   // a running game stops before its project goes (B4); a refused close, above, left it running
        open.ReleaseProjectAssets();
        Workspace = null;
        Using = null;   // nothing open, so no adapter in use (found by A064)
        Current = null;
        return Say(true, "Closed the project." + (fault.Length > 0 ? " " + fault : ""));
    }

    /// <summary>Draws the start screen, or the open project's workspace.</summary>
    public void Draw()
    {
        var frame = Ui.Snapshot().Frame;
        var owner = Workspace;
        DrawCore();
        // Completion publishes exactly once after this draw; unfinished frames cannot authorize a drop.
        dropDrawnFrame = frame + 1;
        dropDrawnWorkspace = owner;
    }

    private void DrawCore()
    {
        KeepTextSize();
        if (Splash?.Draw() == true) return;
        if (Workspace is { } w)
        {
            w.Draw();
            if(pendingRecentOpen is {} recent){pendingRecentOpen=null;OpenRecent(recent);}
            DrawFolderReplacement();
            if (closeAnyway) DrawCloseAnyway();
            return;
        }
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        if (Ui.Begin("BED###launcher"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings))
            DrawStart();
        Ui.End();
    }

    private void DrawManualProject()
    {
        ImGui.SeparatorText("Open project"u8);
        ImGui.TextWrapped("Choose the data and rules folders. A schema is the rules file that says what each field can hold."u8);
        if (Loaded.Count > 0 || AdapterProblems.Count > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Game connection"u8);
            ImGui.SameLine(ImGui.CalcTextSize("Link map (optional)"u8).X + ImGui.GetStyle().ItemSpacing.X * 3);
            ImGui.SetNextItemWidth(320);
            var shown = NoAdapter ? "Data only" : Adapter is { } picked ? NameOf(picked) : "Automatic";
            if (ImGui.BeginCombo("##adapter"u8, shown))
            {
                if (ImGui.Selectable("Automatic"u8, !NoAdapter && Adapter is null)) (NoAdapter, Adapter) = (false, null);
                if (ImGui.Selectable("Data only"u8, NoAdapter)) (NoAdapter, Adapter) = (true, null);
                foreach (var a in Loaded)
                    if (ImGui.Selectable(NameOf(a), !NoAdapter && Adapter == a)) (NoAdapter, Adapter) = (false, a);
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Optional: a game's own rules and panels. Game connections are programs; load only ones you trust."u8);
        }
        PathRow("Data folder", ref dataText, 0, folder: true);
        PathRow("Rules folder (schema)", ref schemasText, 1, folder: true);
        PathRow("Link map (optional)", ref mapText, 2, folder: false);
        if (ImGui.Button("Open"u8))
        {
            var data = Text(dataText);
            // A project folder holding data/ and schemas/ opens in one step, when it's all that was given.
            if (Text(schemasText).Length == 0 && ProjectPaths.In(data) is { } laidOut) Open(laidOut);
            else Open(new(data, Text(schemasText), Text(mapText) is { Length: > 0 } map ? map : null));
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Tip: a folder holding data and schemas folders can go in Data folder alone."u8);
        if (ImGui.Button("Back"u8)) startPage = StartPage.Home;
        if (picking >= 0 && picker.Draw(picking == 2) is { } chosen)
        {
            if (chosen.Length > 0)
            {
                if (picking == 0) ChooseDataFolder(chosen);
                else if (picking == 1) schemasText = new ImInputString(1024, chosen);
                else mapText = new ImInputString(1024, chosen);
            }
            picking = -1;
        }
    }

    private void FillPaths(ProjectPaths paths)
    {
        dataText = new ImInputString(1024, paths.Data);
        schemasText = new ImInputString(1024, paths.Schemas);
        mapText = new ImInputString(1024, paths.Map ?? "");
    }

    private void ChooseDataFolder(string chosen)
    {
        dataText = new ImInputString(1024, chosen);
        CompleteProjectFolder(chosen);
    }

    private void CompleteProjectFolder(string folder)
    {
        // A root with data/ and schemas/ replaces all three defaults together, including a stale map.
        // Incomplete typed paths remain editable; no project is opened and no setting is written here.
        try
        {
            if (ProjectPaths.In(folder) is { } laidOut) FillPaths(laidOut);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { }
    }

    private void PathRow(string label, ref ImInputString text, int which, bool folder)
    {
        // The label leads its row at one width for all three, then the path, then Browse.
        var style = ImGui.GetStyle();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(Math.Max(ImGui.CalcTextSize("Link map (optional)"u8).X, ImGui.CalcTextSize("Rules folder (schema)"u8).X) + style.ItemSpacing.X * 3);
        ImGui.SetNextItemWidth(-ImGui.CalcTextSize("Browse..."u8).X - style.FramePadding.X * 2 - style.ItemSpacing.X);
        ImGui.InputText($"##path{which}", text);
        if (which == 0 && ImGui.IsItemDeactivatedAfterEdit()) CompleteProjectFolder(Text(text));
        ImGui.SameLine();
        if (ImGui.Button($"Browse...##browse{which}"))
        {
            var start = Text(text) is { Length: > 0 } now ? now : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (folder && FileDialogs is { } dialogs)
            {
                try
                {
                    var chosen = dialogs.PickFolder(label, start);
                    if (chosen is not null)
                    {
                        if (which == 0) ChooseDataFolder(chosen);
                        else text = new ImInputString(1024, chosen);
                    }
                }
                catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    Said = "The folder dialog could not open: " + e.Message;
                }
            }
            else
            {
                picking = which;
                picker.Start(start, chooseFile: !folder);   // the link-map row keeps its existing file browser
            }
        }
    }

    private static string Text(ImInputString field)
    {
        field.EvaluateLength();
        return field.ToString().Trim().Trim('"');   // a pasted path often comes quoted
    }

    private bool Say(bool ok, string said)
    {
        Said = said;
        return ok;
    }
}

/// <summary>
/// A game's adapter for the general app: optional. It opens the game's projects with its own rules (read-only files, a
/// loader as a rule) and adds its panels. A game's adapter implements it; without one, any data with schemas
/// opens plainly.
/// </summary>
public interface IProjectAdapter
{
    /// <summary>Its name, shown when it's loaded.</summary>
    string Name { get; }

    /// <summary>Whether this is one of its game's projects, so the app uses it without being asked (an adapter
    /// knows its own game's data). False leaves the choice to the person.</summary>
    bool Recognises(ProjectPaths project) => false;

    /// <summary>Recognises a nonstandard project folder and returns its paths, relative to that folder or absolute. Null declines.</summary>
    ProjectPaths? FindProject(string folder) => null;

    /// <summary>Optional assets folder for a nested layout; null uses the admitted data folder's sibling assets.</summary>
    string? AssetsFolder(ProjectPaths project) => null;

    /// <summary>Opens the project's data with the game's rules.</summary>
    DataProject Open(ProjectPaths project);

    /// <summary>The game's own checks (its tools, run on a snapshot: piece J), shown in the Checks panel beside the
    /// tools' link check. None by default.</summary>
    IEnumerable<ICheck> Checks => [];
    /// <summary>Optional defaults for project-owned saved-files Play settings; project fields override them individually.</summary>
    PlaySettings? DefaultPlay(ProjectPaths project) => null;
    /// <summary>Optional game-owned build command accepting explicit captured Saved input.</summary>
    GameBuildSpec? Build(DataWorkspace workspace) => null;
    /// <summary>Optional game-owned Saved source/check/origin/output and smoke declarations.</summary>
    GameExportDeclaration? Export(DataWorkspace workspace) => null;


    /// <summary>Adds the game's panels or play modes to each file's editor.</summary>
    void Configure(EditorModel<DataDocument> editor, DataTableView table);

    /// <summary>Names or phrases the game must never use (a trademark it doesn't own, a real person's name). Any text value
    /// containing one, as whole words and ignoring case, accents and punctuation, is refused at its field with the reason,
    /// in the editor, the command line and MCP alike (<see cref="Data.ProtectedNames"/>). None by default.</summary>
    IEnumerable<string> ProtectedNames => [];

    /// <summary>The project's own rights holders (its studio's or people's names). A rights tag whose only words, apart
    /// from "©", years and the like, are one of these doesn't count against an asset declared ours
    /// (<see cref="RightsTags.IsClaim"/>). None by default.</summary>
    IEnumerable<string> OwnNames => [];

    /// <summary>Adds the game's panels or play modes to a file's editor, knowing the workspace and file it's for: what a play mode
    /// needs when it starts from more than the one file (it reads the other files' current drafts from
    /// <see cref="DataWorkspace.Project"/> when Play starts, never from disk behind the editor). The workspace owns the game's
    /// lifetime: it stops it before another file is shown and when the project closes. The general app calls this one; by
    /// default it calls <see cref="Configure(EditorModel{DataDocument}, DataTableView)"/>, so an adapter that needs neither
    /// implements only that. INVENTED.</summary>
    void Configure(DataWorkspace workspace, DataFile file, EditorModel<DataDocument> editor, DataTableView table) => Configure(editor, table);
}

/// <summary>
/// Loads game adapters from their DLLs: every public <see cref="IProjectAdapter"/> with a constructor that takes nothing.
/// Each DLL loads with its own dependencies from its folder, sharing the tools' own assemblies with the app, so a game's
/// adapter can bring its game's libraries. An adapter is a program: it's trusted, so the app loads only the ones beside
/// it (in <c>adapters/</c>) or named on its command line. Problems are returned, never thrown.
/// </summary>
public static class Adapters
{
    /// <summary>Adds <paramref name="adapter"/>'s <see cref="IProjectAdapter.ProtectedNames"/> to the project it opened,
    /// beside any the project already has, and returns the project. Every host that opens a project through an adapter calls
    /// this, so the names are refused alike everywhere.</summary>
    public static DataProject ApplyProtectedNames(IProjectAdapter adapter, DataProject project)
    {
        var names = adapter.ProtectedNames.ToList();
        if (names.Count > 0) project.ProtectedNames = new ProtectedNames([.. project.ProtectedNames?.Names ?? [], .. names]);
        return project;
    }

    /// <summary>The adapters in <paramref name="dlls"/>, and why any didn't load.</summary>
    public static (IReadOnlyList<IProjectAdapter> Loaded, IReadOnlyList<string> Problems) Load(IEnumerable<string> dlls)
    {
        var loaded = new List<IProjectAdapter>();
        var problems = new List<string>();
        foreach (var dll in dlls)
        {
            try
            {
                var path = Path.GetFullPath(dll);
                var name = AssemblyName.GetAssemblyName(path).Name;
                var assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name)
                    ?? new AdapterContext(path).LoadFromAssemblyPath(path);
                var found = assembly.GetExportedTypes()
                    .Where(t => typeof(IProjectAdapter).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) is not null)
                    .ToList();
                foreach (var type in found)
                {
                    try { loaded.Add((IProjectAdapter)Activator.CreateInstance(type)!); }
                    catch (TargetInvocationException e)   // its constructor threw: the others in this DLL still load (found by A073)
                    {
                        problems.Add($"{Path.GetFileName(dll)}: {type.Name} didn't load: {e.InnerException?.Message ?? e.Message}");
                    }
                }
                if (found.Count == 0) problems.Add($"{Path.GetFileName(dll)} holds no game adapter (a public IProjectAdapter with an empty constructor).");
            }
            // A malformed path is a problem to report too (found by A064).
            catch (Exception e) when (e is IOException or BadImageFormatException or ReflectionTypeLoadException or TypeLoadException or TargetInvocationException or MissingMethodException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                problems.Add($"{Path.GetFileName(dll)} didn't load: {(e is TargetInvocationException { InnerException: { } inner } ? inner.Message : e.Message)}");
            }
        }
        return (loaded, problems);
    }

    /// <summary>The adapter DLLs (named <c>*Adapter.dll</c>) in <paramref name="folder"/> and below, or none when it
    /// doesn't exist.</summary>
    public static IEnumerable<string> In(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*Adapter.dll", SearchOption.AllDirectories) : [];

    // The libraries the app itself was built with (its trusted platform list): those always come from the app, so both
    // sides share one IProjectAdapter.
    private static readonly HashSet<string> AppLibraries = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFileNameWithoutExtension).OfType<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // An adapter's other libraries come from its folder, or else from beside the app (a game's own libraries that ship
    // with the release, such as checkers' rules beside play-checkers), loaded here since the app wasn't built with them
    // (found by the release's own check: deferring every file beside the app to the app failed to load them).
    private sealed class AdapterContext(string dll) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(dll))
    {
        private readonly AssemblyDependencyResolver resolver = new(dll);

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is null || AppLibraries.Contains(name.Name)) return null;
            // A single-file release bundles the app's libraries inside BED.exe, so they aren't in the trusted list:
            // whatever the app itself can load is shared, so both sides still see one IProjectAdapter.
            if (AppLoads(name)) return null;
            if (resolver.ResolveAssemblyToPath(name) is { } path && File.Exists(path)) return LoadFromAssemblyPath(path);
            var beside = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            return File.Exists(beside) ? LoadFromAssemblyPath(beside) : null;
        }

        private static bool AppLoads(AssemblyName name)
        {
            try { AssemblyLoadContext.Default.LoadFromAssemblyName(name); return true; }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException) { return false; }
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

/// <summary>A small folder (or file) chooser drawn inside the app, so choosing a path needs no typing and no native
/// dialog: drives, Up, the folders (and files) here, and Choose.</summary>
public sealed class FolderPicker
{
    private string at = "";
    private bool files;

    /// <summary>The folder being shown.</summary>
    public string At => at;

    /// <summary>Starts choosing from <paramref name="from"/> (a file's folder, or the folder itself).</summary>
    public void Start(string from, bool chooseFile)
    {
        at = Directory.Exists(from) ? Path.GetFullPath(from) : Path.GetDirectoryName(Path.GetFullPath(from)) is { } dir && Directory.Exists(dir) ? dir : Environment.CurrentDirectory;
        files = chooseFile;
    }

    /// <summary>Moves into <paramref name="folder"/>, or up with "..". False when it doesn't exist.</summary>
    public bool Go(string folder)
    {
        var next = folder == ".." ? Path.GetDirectoryName(at) : Path.Combine(at, folder);
        if (next is null || !Directory.Exists(next)) return false;
        at = Path.GetFullPath(next);
        return true;
    }

    /// <summary>Draws the chooser. Returns the chosen path, "" when cancelled, or null while still choosing.</summary>
    public string? Draw(bool chooseFile)
    {
        string? result = null;
        ImGui.SetNextWindowSize(new Brutal.Numerics.float2(560, 420), ImGuiCond.FirstUseEver);
        if (ImGui.Begin((chooseFile ? "Choose a file" : "Choose a folder") + "###picker", ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextWrapped(at);
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                if (ImGui.SmallButton(drive.Name)) at = drive.RootDirectory.FullName;
                ImGui.SameLine();
            }
            if (ImGui.SmallButton("Up"u8)) Go("..");
            if (ImGui.BeginChild("##entries"u8, new Brutal.Numerics.float2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.Borders))
            {
                IEnumerable<string> dirs = [], found = [];
                try
                {
                    dirs = Directory.EnumerateDirectories(at).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
                    if (files) found = Directory.EnumerateFiles(at, "*.json").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ImGui.TextDisabled("This folder can't be read."u8); }
                foreach (var d in dirs)
                    if (ImGui.Selectable($"[{d}]")) { Go(d); break; }
                foreach (var f in found)
                    if (ImGui.Selectable(f)) result = Path.Combine(at, f);
            }
            ImGui.EndChild();
            if (!chooseFile && ImGui.Button("Choose this folder"u8)) result = at;
            if (!chooseFile) ImGui.SameLine();
            if (ImGui.Button("Cancel"u8)) result = "";
        }
        ImGui.End();
        return result;
    }
}
