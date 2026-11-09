using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;

namespace FirstTaskSample;

// INVENTED small settings example; BORROWED existing sample adapter/check wiring.
public sealed class FirstTaskAdapter : IProjectAdapter
{
    public string Name => "First task settings";
    public bool Recognises(ProjectPaths project)
    {
        var path = Path.Combine(project.Data, "settings.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("kind"u8, out var kind)
                && kind.ValueKind == JsonValueKind.String && kind.GetString() == "first-task-settings";
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }
    public DataProject Open(ProjectPaths project) => DataProject.Open(project.Data, project.Schemas,
        rules: name => name == "settings.json" ? new FirstTaskRules() : null);
    public IEnumerable<ICheck> Checks => [new FirstTaskCheck()];
    public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
}

public sealed class FirstTaskRules : IValidator<DataDocument>
{
    public IReadOnlyList<Issue> Validate(DataDocument document) => Inspect(document.Root);

    public static IReadOnlyList<Issue> Inspect(JsonElement root)
    {
        var issues = new List<Issue>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("presets"u8, out var rows)
            || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 2)
            return [Issue.Error("Keep exactly the Default and Practice presets.", path: "/presets")];
        var ids = new[] { "default", "practice" };
        var names = new[] { "Default", "Practice" };
        for (var i = 0; i < 2; i++)
        {
            var row = rows[i]; var at = $"/presets/{i}";
            if (row.ValueKind != JsonValueKind.Object) { issues.Add(Issue.Error("A preset must be an object.", path: at)); continue; }
            if (!row.TryGetProperty("id"u8, out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != ids[i]
                || !row.TryGetProperty("name"u8, out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != names[i])
                issues.Add(Issue.Error($"Keep {names[i]}'s name and id unchanged.", path: at));
            if (!row.TryGetProperty("rounds"u8, out var rounds) || rounds.ValueKind != JsonValueKind.Number || !rounds.TryGetInt32(out var r)
                || !row.TryGetProperty("reserve"u8, out var reserve) || reserve.ValueKind != JsonValueKind.Number || !reserve.TryGetInt32(out var s))
            { issues.Add(Issue.Error("Enter whole numbers for rounds and reserve.", path: at)); continue; }
            if (r < 1 || r > 100) issues.Add(Issue.Error("Rounds must be from 1 to 100.", path: at + "/rounds"));
            if (s < 0 || s > 99) issues.Add(Issue.Error("Reserve must be from 0 to 99.", path: at + "/reserve"));
            if (s >= r) issues.Add(Issue.Error($"{names[i]}: rounds must be greater than reserve ({s}); increase rounds or lower reserve.", path: at + "/rounds"));
            if (i == 0 && (r != 60 || s != 10)) issues.Add(Issue.Error("Keep Default unchanged; edit Practice instead.", path: at));
        }
        return issues;
    }
}

public sealed class FirstTaskCheck : ICheck
{
    public const string Id = "sample.first-task.settings";
    public CheckDefinition Definition => new(Id, "Check first task settings",
        "Checks both preset identities, numeric limits and reserve below rounds. No simulation or preview.", TimeSpan.FromSeconds(2));
    public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var text = snapshot.Text("data/settings.json");
        if (text is null) return Task.FromResult(new CheckReport(false, [], "settings.json is missing; open the FirstTask data folder."));
        try
        {
            using var json = JsonDocument.Parse(text);
            var findings = FirstTaskRules.Inspect(json.RootElement).Select(issue => new CheckFinding(issue,
                new CheckPlace("settings.json", At: issue.Path), "Correct this preset and run Check first task settings again.")).ToArray();
            cancel.ThrowIfCancellationRequested();
            return Task.FromResult(new CheckReport(true, findings, findings.Length == 0
                ? $"Both presets checked: identities, numeric limits and reserve below rounds. Practice has {json.RootElement.GetProperty("presets"u8)[1].GetProperty("rounds"u8).GetInt32()} rounds. No simulation was run."
                : "Correct the settings listed below."));
        }
        catch (JsonException)
        {
            return Task.FromResult(new CheckReport(true,
                [new CheckFinding(Issue.Error("settings.json is not valid JSON."), new CheckPlace("settings.json"), "Repair the JSON and run the check again.")],
                "Settings could not be parsed."));
        }
    }
}
