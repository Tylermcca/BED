using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;

namespace RoutePuzzleSample.Authoring;

// BORROWED wiring: StarterAdapter and CheckersAdapter; INVENTED puzzle and messages.
public sealed class RouteAdapter : IProjectAdapter
{
    public string Name => "Route puzzle (sample)";
    public bool Recognises(ProjectPaths project)
    {
        var path = Path.Combine(project.Data, "routes.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return IsCatalog(json.RootElement);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }
    private static bool IsCatalog(JsonElement root) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
        && kind.GetString() == RouteCatalog.Kind;
    public DataProject Open(ProjectPaths project) => DataProject.Open(project.Data, project.Schemas,
        rules: name => name == "routes.json" ? new RouteRules() : null);
    public IEnumerable<ICheck> Checks => [new RouteCheck()];
    public void Configure(EditorModel<DataDocument> editor, DataTableView table)
    {
        if (!IsCatalog(editor.Session.Current.Root)) return;
        editor.Add(new PlayMode<DataDocument>("sample.route.play", "Try route",
            draft => new RoutePlay(RouteCatalog.Read(draft.Root.GetRawText()))));
    }
}

public sealed class RouteRules : IValidator<DataDocument>
{
    public IReadOnlyList<Issue> Validate(DataDocument document)
    {
        try { RouteCatalog.Read(document.Root.GetRawText()); return []; }
        catch (RouteProblem e) { return [Issue.Error(e.Message, path: e.At)]; }
    }
}

/// <summary>Reads a frozen snapshot, including unsaved edits, never a live path.</summary>
public sealed class RouteCheck : ICheck
{
    public CheckDefinition Definition => new("sample.route.check", "Check route puzzle",
        "Check the catalog with the native game loader, including a route within the energy budget.",
        TimeSpan.FromSeconds(2));
    public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var text = snapshot.Text("data/routes.json");
        CheckReport result;
        if (text is null)
            result = new(false, [], "No routes.json in this snapshot; open the RoutePuzzle sample folder.");
        else
        {
            try
            {
                var catalog = RouteCatalog.Read(text);
                result = new(true, [], $"Goal reachable: minimum cost {catalog.MinimumCost()}, energy {catalog.Energy}.");
            }
            catch (RouteProblem e)
            {
                result = new(true, [new CheckFinding(Issue.Error(e.Message, path: e.At),
                    new CheckPlace("routes.json", At: e.At), "Correct this field and run Check route puzzle again.")],
                    "The native game loader refuses this catalog.");
            }
        }
        cancel.ThrowIfCancellationRequested();
        progress.Report(new(1, result.Summary));
        return Task.FromResult(result);
    }
}

/// <summary>Workspace-owned in-editor play; no process, file writes, clock or network.</summary>
public sealed class RoutePlay : IPlaySession
{
    private readonly RouteCatalog catalog;
    public RoutePlay(RouteCatalog catalog) { this.catalog = catalog; Game = new(catalog); }
    public RouteGame Game { get; }
    public bool Stopped { get; private set; }
    public string Status => Stopped ? "Stopped." : Game.Status;
    public bool Choose(string destination) => !Stopped && Game.Choose(destination);
    public void Draw()
    {
        if (Stopped) return;
        ImGui.TextWrapped(catalog.Title);
        ImGui.TextWrapped(Status);
        if (Game.Won || Game.Lost) return;
        foreach (var exit in catalog.Place(Game.Current).Exits)
        {
            if (exit.Cost > Game.Energy) continue;
            if (ImGui.Button($"{catalog.Place(exit.To).Label} (cost {exit.Cost})##{exit.To}"))
            { Choose(exit.To); break; } // one decision per frame, then redraw the new place
        }
    }
    public void Dispose() => Stopped = true;
}
