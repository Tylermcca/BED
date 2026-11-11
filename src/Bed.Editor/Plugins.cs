namespace Bed.Editor;

/// <summary>
/// A plug-in for editors of one document type (FUT-002, the third rung of goal 3): a manifest, and one method that
/// says what it adds. It registers through a <see cref="PluginRegistrar{TDoc}"/>, the same seams the editor's own parts
/// use, and never touches the editor directly. Code loaded in-process is trusted: the host checks names and
/// versions, not intent.
/// </summary>
public interface IEditorPlugin<TDoc> where TDoc : class
{
    /// <summary>Who it is and what it needs. Read before <see cref="Register"/> runs.</summary>
    PluginManifest Manifest { get; }

    /// <summary>Says what the plug-in adds. Nothing takes effect until this returns and every addition checks out.</summary>
    void Register(PluginRegistrar<TDoc> add);
}

/// <summary>
/// What a plug-in adds, gathered before any of it takes effect: commands, inspector object types, workspaces, panels and
/// default shortcuts. Every id must start with the plug-in's id and a dot (<c>lights.add</c>), so plug-ins never collide with
/// the editor, the game or each other.
/// </summary>
public sealed class PluginRegistrar<TDoc> where TDoc : class
{
    internal readonly List<(CommandInfo Info, Action<CommandCatalog<TDoc>> Register)> Commands = [];
    internal readonly List<IObjectType<TDoc>> Types = [];
    internal readonly List<Workspace> Workspaces = [];
    internal readonly List<(string Id, string Chord)> Shortcuts = [];
    internal readonly List<Panel> Panels = [];
    internal readonly List<PlayMode<TDoc>> PlayModes = [];
    internal readonly List<(string Id, Func<EditorModel<TDoc>, ICommand<TDoc>?> Make)> Makers = [];

    internal PluginRegistrar(PluginManifest manifest, EditorModel<TDoc> editor)
    {
        Manifest = manifest;
        Editor = editor;
    }

    /// <summary>The plug-in being registered.</summary>
    public PluginManifest Manifest { get; }

    /// <summary>The editor it's added to, for panels and tools to read (selection, focus, the document) and to edit
    /// through (<see cref="EditorModel{TDoc}.Execute"/>). Registering itself changes nothing.</summary>
    public EditorModel<TDoc> Editor { get; }

    /// <summary>Adds a command, as <see cref="CommandCatalog{TDoc}.Register{TCommand}"/> does.</summary>
    public PluginRegistrar<TDoc> Command<TCommand>(string id, int version, string label, bool isInternal = false) where TCommand : ICommand<TDoc>
    {
        Commands.Add((new CommandInfo(id, version, label, typeof(TCommand), isInternal), c => c.Register<TCommand>(id, version, label, isInternal)));
        return this;
    }

    /// <summary>Adds an inspector object type.</summary>
    public PluginRegistrar<TDoc> ObjectType(IObjectType<TDoc> type)
    {
        Types.Add(type);
        return this;
    }

    /// <summary>Adds a workspace. It may lead with any command in the editor, the plug-in's own included.</summary>
    public PluginRegistrar<TDoc> Workspace(Workspace workspace)
    {
        Workspaces.Add(workspace);
        return this;
    }

    /// <summary>Says how the palette makes one of the plug-in's own commands from the editor's state
    /// (<see cref="EditorModel{TDoc}.Maker"/>).</summary>
    public PluginRegistrar<TDoc> Maker(string commandId, Func<EditorModel<TDoc>, ICommand<TDoc>?> make)
    {
        Makers.Add((commandId, make));
        return this;
    }

    /// <summary>Adds a play mode (goal 14): the game's own code, run from the document by the Play button.</summary>
    public PluginRegistrar<TDoc> PlayMode(PlayMode<TDoc> mode)
    {
        PlayModes.Add(mode);
        return this;
    }

    /// <summary>Adds a panel to the editor window.</summary>
    public PluginRegistrar<TDoc> Panel(Panel panel)
    {
        Panels.Add(panel);
        return this;
    }

    /// <summary>Gives one of the plug-in's own commands a default shortcut. The person's own choice still wins.</summary>
    public PluginRegistrar<TDoc> Shortcut(string commandId, string chord)
    {
        Shortcuts.Add((commandId, chord));
        return this;
    }
}

/// <summary>A plug-in the host was given: enabled, or refused with the reasons.</summary>
/// <param name="Manifest">Its manifest.</param>
/// <param name="Enabled">True when everything it adds took effect.</param>
/// <param name="Issues">Why it was refused (empty when enabled).</param>
public sealed record PluginStatus(PluginManifest Manifest, bool Enabled, IReadOnlyList<Issue> Issues)
{
    /// <summary>Things worth knowing that don't stop the plug-in: no licence given, a licence the checker doesn't know,
    /// or licences that can't be shipped together with another enabled plug-in's or the project's.</summary>
    public IReadOnlyList<Issue> Warnings { get; init; } = [];
}

/// <summary>
/// Adds plug-ins to one editor. Each is all or nothing: its manifest is checked against the library's API version first,
/// then <see cref="IEditorPlugin{TDoc}.Register"/> runs into a staging area, then every addition is checked (ids owned,
/// well formed, unused; workspaces naming real commands; shortcuts that parse). Only when all pass does anything take
/// effect. A plug-in that throws or fails a check is disabled and reported, and the editor carries on (FUT-002). The host
/// loads no assemblies and scans nothing: the game or editor hands it plug-in objects (explicit registration).
/// </summary>
public sealed class PluginHost<TDoc> where TDoc : class
{
    private readonly List<PluginStatus> plugins = [];

    /// <summary>A host adding to <paramref name="editor"/>, <paramref name="inspector"/> and <paramref name="shortcuts"/>,
    /// at library API <paramref name="api"/> (default <see cref="AuthoringInfo.ApiVersion"/>).</summary>
    public PluginHost(EditorModel<TDoc> editor, Inspector<TDoc> inspector, Shortcuts shortcuts, ApiVersion? api = null)
        : this(editor, inspector, shortcuts, api, projectLicense: null) { }

    /// <summary>As the other constructor, with the project's own licence (an SPDX expression, or <c>proprietary</c> /
    /// <c>LicenseRef-...</c> for closed source), so plug-ins whose licences can't ship with it are warned about.</summary>
    public PluginHost(EditorModel<TDoc> editor, Inspector<TDoc> inspector, Shortcuts shortcuts, ApiVersion? api, string? projectLicense)
    {
        ProjectLicense = projectLicense;
        Editor = editor;
        Inspector = inspector;
        Shortcuts = shortcuts;
        Api = api ?? AuthoringInfo.ApiVersion;
    }

    /// <summary>The editor plug-ins add to.</summary>
    public EditorModel<TDoc> Editor { get; }

    /// <summary>The inspector plug-ins add object types to.</summary>
    public Inspector<TDoc> Inspector { get; }

    /// <summary>The shortcuts plug-ins add defaults to.</summary>
    public Shortcuts Shortcuts { get; }

    /// <summary>The project's own licence, if it was given.</summary>
    public string? ProjectLicense { get; }

    /// <summary>The library API version plug-ins are checked against.</summary>
    public ApiVersion Api { get; }

    /// <summary>Every plug-in given to the host, in order, enabled or not.</summary>
    public IReadOnlyList<PluginStatus> Plugins => plugins;

    /// <summary>Adds <paramref name="plugin"/>. Returns false, with the reasons in <see cref="Plugins"/>, when it's refused;
    /// then nothing it asked for has taken effect.</summary>
    public bool Add(IEditorPlugin<TDoc> plugin)
    {
        var m = plugin.Manifest;
        var issues = new List<Issue>();
        if (plugins.Any(p => p.Enabled && p.Manifest.Id == m.Id))
            issues.Add(Issue.Error($"a plug-in with the id '{m.Id}' is already enabled", m.Id));
        issues.AddRange(m.Check(Api));
        if (issues.Count > 0) return Done(m, issues);

        var add = new PluginRegistrar<TDoc>(m, Editor);
        try { plugin.Register(add); }
        catch (Exception e) { return Done(m, [Issue.Error($"'{m.Name}' failed while registering: {e.Message}", m.Id)]); }

        void Own(string kind, string id)
        {
            if (!m.Owns(id)) issues.Add(Issue.Error($"{kind} '{id}' must be named '{m.Id}.<name>'", id));
        }
        var catalog = Editor.Session.Catalog;
        var scratch = new CommandCatalog<TDoc>();
        foreach (var (info, register) in add.Commands)
        {
            Own("command", info.Id);
            if (catalog.Commands.Any(c => c.Id == info.Id)) issues.Add(Issue.Error($"command '{info.Id}' is already in the editor", info.Id));
            else if (catalog.Commands.FirstOrDefault(c => c.Type == info.Type) is { } same)
                issues.Add(Issue.Error($"{info.Type.Name} is already in the editor as '{same.Id}'", info.Id));
            else
                try { register(scratch); }
                catch (ArgumentException e) { issues.Add(Issue.Error(e.Message, info.Id)); }
        }
        foreach (var t in add.Types)
        {
            Own("object type", t.Id);
            if (Inspector.Types.Any(x => x.Id == t.Id) || add.Types.Count(x => x.Id == t.Id) > 1)
                issues.Add(Issue.Error($"object type '{t.Id}' is already registered", t.Id));
        }
        var commandIds = catalog.Commands.Select(c => c.Id).Concat(add.Commands.Select(c => c.Info.Id)).ToHashSet(StringComparer.Ordinal);
        foreach (var w in add.Workspaces)
        {
            Own("workspace", w.Id);
            if (Editor.Workspaces.Any(x => x.Id == w.Id) || add.Workspaces.Count(x => x.Id == w.Id) > 1)
                issues.Add(Issue.Error($"workspace '{w.Id}' is already added", w.Id));
            foreach (var c in w.Commands.Where(c => !commandIds.Contains(c)))
                issues.Add(Issue.Error($"workspace '{w.Id}' leads with '{c}', which isn't a command in the editor", w.Id));
        }
        foreach (var p in add.Panels)
        {
            Own("panel", p.Id);
            if (Editor.Panels.Any(x => x.Id == p.Id) || add.Panels.Count(x => x.Id == p.Id) > 1)
                issues.Add(Issue.Error($"panel '{p.Id}' is already added", p.Id));
        }
        foreach (var p in add.PlayModes)
        {
            Own("play mode", p.Id);
            if (Editor.PlayModes.Any(x => x.Id == p.Id) || add.PlayModes.Count(x => x.Id == p.Id) > 1)
                issues.Add(Issue.Error($"play mode '{p.Id}' is already added", p.Id));
        }
        foreach (var (id, _) in add.Makers)
            if (!m.Owns(id) || !commandIds.Contains(id) || Editor.HasMaker(id) || add.Makers.Count(x => x.Id == id) > 1)
                issues.Add(Issue.Error($"a palette maker may only go to one of '{m.Name}''s own commands, once, not '{id}'", id));
        foreach (var (id, chord) in add.Shortcuts)
        {
            if (!m.Owns(id) || !commandIds.Contains(id))
                issues.Add(Issue.Error($"a shortcut may only go to one of '{m.Name}''s own commands, not '{id}'", id));
            try { Chord.Parse(chord); }
            catch (FormatException e) { issues.Add(Issue.Error($"shortcut for '{id}': {e.Message}", id)); }
        }
        if (issues.Count > 0) return Done(m, issues);

        foreach (var (_, register) in add.Commands) register(catalog);
        foreach (var t in add.Types) Inspector.Add(t);
        foreach (var w in add.Workspaces) Editor.Add(w);
        foreach (var p in add.Panels) Editor.Add(p);
        foreach (var p in add.PlayModes) Editor.Add(p);
        foreach (var (id, make) in add.Makers) Editor.Maker(id, make);
        foreach (var (id, chord) in add.Shortcuts) Shortcuts.Default(id, chord);
        return Done(m, []);
    }

    private bool Done(PluginManifest m, IReadOnlyList<Issue> issues)
    {
        plugins.Add(new PluginStatus(m, issues.Count == 0, issues) { Warnings = issues.Count == 0 ? LicenceWarnings(m) : [] });
        return issues.Count == 0;
    }

    // Licence notes for a newly enabled plug-in: a warning, never a refusal, and never legal advice.
    private List<Issue> LicenceWarnings(PluginManifest m)
    {
        const string Check = "check the licences before you distribute them together";
        var warnings = new List<Issue>();
        if (m.License is not { } mine)
        {
            warnings.Add(Issue.Warning($"'{m.Name}' gives no licence; check its terms before you share or ship it", m.Id));
            return warnings;
        }
        foreach (var unknown in Licences.Unknown(mine))
            warnings.Add(Issue.Warning($"'{m.Name}' uses the licence '{unknown}', which this editor can't check; {Check}", m.Id));
        var choices = Licences.Choices(mine);
        if (ProjectLicense is { } project && Licences.Clash(choices, Licences.Choices(project)) is { } why)
            warnings.Add(Issue.Warning($"'{m.Name}' ({mine}) and this project ({project}): {why}; {Check}", m.Id));
        foreach (var other in plugins.Where(p => p.Enabled && p.Manifest.License is not null))
            if (Licences.Clash(choices, Licences.Choices(other.Manifest.License!)) is { } clash)
                warnings.Add(Issue.Warning($"'{m.Name}' ({mine}) and '{other.Manifest.Name}' ({other.Manifest.License}): {clash}; {Check}", m.Id));
        return warnings;
    }
}
