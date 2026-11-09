using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;

namespace Brutal.Authoring.Agent.Cli;

/// <summary>Local headless observation of the editor's recorded, completed UI frame; never a live-window connection.</summary>
public static class UiSnapshotCommand
{
    /// <summary>Draws the start screen or an explicit project in an isolated headless host and prints the recorder snapshot.</summary>
    public static int Run(string? data, string? schemas, string? map, string? adapterPath, bool json, TextWriter output, TextWriter errors, string? script = null)
    {
        if ((data is null) != (schemas is null) || data is null && (map is not null || adapterPath is not null))
        {
            errors.WriteLine("ui needs both --data and --schemas, or neither for the start screen.");
            return 2;
        }
        try { if (script is not null) UiScript.Validate(script); }
        catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        { errors.WriteLine("Invalid ui script: " + e.Message); return 2; }
        var settings = Path.Combine(Path.GetTempPath(), "bed-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(settings, "recent.json")),
                ProjectPaths.In(Path.Combine(AppContext.BaseDirectory, "samples", "recipes")));
            if (adapterPath is not null)
            {
                var (loaded, problems) = Adapters.Load([adapterPath]);
                if (problems.Count > 0 || loaded.Count != 1)
                {
                    errors.WriteLine(problems.Count > 0 ? string.Join(" ", problems) : "ui needs exactly one adapter in the supplied DLL.");
                    return 2;
                }
                launcher.Adapter = loaded[0];
            }
            if (data is not null && !launcher.Open(new ProjectPaths(data, schemas!, map)))
            {
                errors.WriteLine(launcher.Said);
                return 2;
            }
            using var host = new HeadlessHost(1280, 800) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            host.Frames(launcher.Draw, 4);
            void Emit(UiSnapshot snapshot) => output.WriteLine(json
                ? script is null ? snapshot.Json : System.Text.Json.Nodes.JsonNode.Parse(snapshot.Json)!.ToJsonString()
                : snapshot.Text);
            Emit(Ui.Snapshot());
            if (script is not null) UiScript.Run(script, host, launcher.Draw, Emit);
            errors.WriteLine("UI snapshots include only recorded controls; an empty or partial tree does not prove an empty screen.");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDocumentException)
        {
            errors.WriteLine("Cannot read the headless UI: " + e.Message);
            return 2;
        }
        finally
        {
            var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(settings);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("bed-ui-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected temporary settings boundary.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
