using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;

namespace StarterAdapter;

/// <summary>
/// The sample project's game adapter: a worked example of what a game adds to the general app. It recognises the sample
/// by its link map's game name, and adds a rule no schema can state: a recipe can't use up the ware it makes. It also
/// lists a name the game must never use (<see cref="ProtectedNames"/>). Without
/// it the sample still opens; with it, that edit is refused with the game's own message.
/// </summary>
public sealed class StarterAdapter : IProjectAdapter
{
    /// <inheritdoc />
    public string Name => "Starter (sample game)";

    /// <inheritdoc />
    public bool Recognises(ProjectPaths project)
    {
        if (project.Map is not { } map || !File.Exists(map)) return false;
        using var doc = JsonDocument.Parse(File.ReadAllText(map));
        return doc.RootElement.TryGetProperty("game"u8, out var game) && game.GetString() == "Starter";
    }

    /// <inheritdoc />
    public DataProject Open(ProjectPaths project) =>
        DataProject.Open(project.Data, project.Schemas, rules: file => file == "recipes.json" ? new NoSelfRecipe() : null);

    /// <inheritdoc />
    public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }

    /// <summary>A name this sample game must never use (INVENTED for the example): a ware called "Acme Wonder Flour", or
    /// "acme wonder-flour", is refused at its field, in the editor, the command line and MCP alike.</summary>
    public IEnumerable<string> ProtectedNames => ["Acme Wonder Flour"];

    private sealed class NoSelfRecipe : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument document)
        {
            var issues = new List<Issue>();
            if (document.Root.TryGetProperty("recipes"u8, out var recipes) && recipes.ValueKind == JsonValueKind.Array)
                for (var i = 0; i < recipes.GetArrayLength(); i++)
                {
                    var r = recipes[i];
                    if (r.TryGetProperty("makes"u8, out var makes) && r.TryGetProperty("from"u8, out var from) && makes.ValueKind == JsonValueKind.String && makes.GetString() == from.GetString())
                        issues.Add(Issue.Error($"a recipe can't use up the ware it makes ({makes.GetString()}); change makes or from", $"/recipes/{i}", $"/recipes/{i}/from"));
                }
            return issues;
        }
    }
}
