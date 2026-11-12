using Bed.Editor.Gum;

namespace Bed.Editor.ImGui;

public sealed partial class DataWorkspace
{
    private IReadOnlyList<string> gumProjects = [];
    private string? gumRoot;
    private GumScreenPanel? gumScreen;

    /// <summary>Offers File > Edit screens in Gum and File > Show Gum screen when <paramref name="projectRoot"/>
    /// holds a Gum project.</summary>
    internal void BindGum(string projectRoot)
    {
        ReleaseGum();
        gumRoot = projectRoot;
        gumProjects = GumCatalog.FindProjects(projectRoot);
    }

    // The one Gum project, or null when there is none or more than one (BED doesn't guess which).
    private string? OnlyGumProject => gumProjects.Count == 1 ? gumProjects[0] : null;

    /// <summary>The Gum screen panel, once made.</summary>
    internal GumScreenPanel? GumScreen => gumScreen;

    /// <summary>Opens the project's Gum project in the person's own Gum tool, as File > Edit screens in Gum does.
    /// When the folder holds more than one Gum project, BED doesn't guess which: it names them and opens none.</summary>
    internal bool OpenGum()
    {
        if (gumProjects.Count == 0) { Said = "This project has no Gum screens."; return false; }
        if (gumProjects.Count > 1)
        {
            var names = gumProjects.Select(p => gumRoot is null ? Path.GetFileName(p) : GumCatalog.Relative(gumRoot, p)).ToList();
            Said = $"This project has {names.Count} Gum projects: {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}. "
                + "BED doesn't pick one for you. Open Gum on its own and choose the one you want from its File menu.";
            return false;
        }
        var gum = FindGum();
        if (gum is null) { Said = GumTool.NotFound; return false; }
        var (started, message) = GumTool.Start(GumTool.Plan(gum, GumCatalog.Read(gumProjects[0]), null));
        Said = message;
        return started;
    }

    /// <summary>Where BED looks for Gum when it hasn't been shown; tests replace it so they don't start a real Gum.</summary>
    internal Func<IEnumerable<string>> GumPlaces { get; set; } = GumTool.UsualPlaces;

    // Gum.exe: the one remembered, else one found in the usual places, else the one the person picks. Remembered from then
    // on (Preferences shows it and can change it). Null when there's no Gum and the person didn't pick one.
    private string? FindGum()
    {
        var gum = GumTool.Find(Locations.Gum, GumPlaces());
        if (gum is null && reportDialogs?.Invoke() is { } dialogs)
        {
            var picked = dialogs.OpenFile("Where is Gum? Pick Gum.exe", [new FileDialogFilter("Gum", "Gum.exe")], null);
            gum = GumTool.Find(picked, []);
        }
        if (gum is not null) Locations.Gum = gum;
        return gum;
    }

    private void PrepareGumUi()
    {
        PictureTexture.Tick();
        Action? open = gumProjects.Count == 0 ? null : () => OpenGum();
        if (Window is { } data) { data.OpenGumAction = open; data.ShowGumScreenAction = ShowGumScreen(data.Editor); }
        if (SceneWindow is { } scene) { scene.OpenGumAction = open; scene.ShowGumScreenAction = ShowGumScreen(scene.Editor); }
        if (Scene2Window is { } scene2) { scene2.OpenGumAction = open; scene2.ShowGumScreenAction = ShowGumScreen(scene2.Editor); }
    }

    // Every editor shown gets the one Gum screen panel (one picture for the project); the File menu opens it.
    private Action? ShowGumScreen<T>(EditorModel<T> editor) where T : class
    {
        if (OnlyGumProject is not { } file) return null;
        gumScreen ??= new GumScreenPanel(file, () => OpenGum());
        var panel = gumScreen.AsPanel();
        if (editor.Panels.All(p => p.Id != panel.Id)) editor.Add(panel);
        return () => editor.SetOpen(panel.Id, true);
    }

    // Lets go of the Gum screen picture (Gum's drawing service, its file watcher, the canvas and the ImGui texture). The
    // editors keep the panel entry; drawing it again makes a new picture.
    private void ReleaseGum()
    {
        var panel = gumScreen;
        gumScreen = null;
        panel?.Dispose();
    }
}
