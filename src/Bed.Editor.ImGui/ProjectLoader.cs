using System.Text.Json;
using Bed.Data;

namespace Bed.Editor.ImGui;

// Preparation only: no launcher state, forms, windows, recent settings or running-game lifetime.
// BORROWED verbatim loading/refusal logic from ProjectLauncher.Open; result boundary is this extraction.
internal sealed record LoadedProject(DataProject Data, ReferenceMap? Map, IProjectAdapter? Adapter,
    string Label, ProjectPaths Full, List<string> Notes);

internal static class ProjectLoader
{
    internal static LoadedProject? Open(ProjectPaths project, bool noAdapter, IProjectAdapter? selected,
        IReadOnlyList<IProjectAdapter> adapters, out string refusal)
    {
        using var preparation=HeadlessStartupTiming.Preparation("project_load_ms");
        refusal = "";
        if(project.Root is {} root)
        {
            try{if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root))return Refuse("Choose an existing project root.",out refusal);project=project with{Root=Path.GetFullPath(root)};}
            catch(Exception error)when(error is ArgumentException or NotSupportedException or PathTooLongException){return Refuse("The project root is invalid: "+error.Message,out refusal);}
        }
        if (project.Data.Trim().Length == 0 || !Directory.Exists(project.Data))
            return Refuse(project.Data.Trim().Length == 0 ? "Choose the data folder: the folder of your game's JSON files." : $"There's no data folder at {project.Data}. Check the path, or choose it with Browse.", out refusal);
        if (project.Schemas.Trim().Length == 0 || !Directory.Exists(project.Schemas))
            return Refuse(project.Schemas.Trim().Length == 0 ? "Choose the schema folder: the folder of JSON Schemas that describe the data." : $"There's no schema folder at {project.Schemas}. Check the path, or choose it with Browse.", out refusal);
        if (!Directory.EnumerateFiles(project.Schemas, "*.json").Any())
            return Refuse($"{project.Schemas} holds no schemas (.json files). Is it the right folder? A schema is named after its data file: wares.json is checked by wares.schema.json.", out refusal);
        // Explicit and root maps are checked before calling any adapter, preserving refusal order.
        project = project with { Map = AdapterMap.Resolve(project.Map, project.Data, null, project.Root??project.Folder) };
        ReferenceMap? map = null;
        if (project.Map is { Length: > 0 } mapFile)
        {
            if (!System.IO.File.Exists(mapFile)) return Refuse($"There's no link map at {mapFile}. Leave it empty if the project has none.", out refusal);
            try { map = ReferenceMap.Read(System.IO.File.ReadAllText(mapFile)); }
            catch (Exception e) when (e is JsonException or InvalidDocumentException or IOException) { return Refuse($"The link map {mapFile} can't be read: {e.Message}", out refusal); }
        }
        var notes = new List<string>();   // what went wrong in the adapter without stopping the project opening
        var adapter = noAdapter ? null : selected ?? adapters.FirstOrDefault(a => Recognised(a, project, notes));
        var label = adapter is null ? "" : NameOf(adapter, notes);
        if (project.Map is null && adapter is not null)
        {
            // Only the chosen adapter's actual assembly may provide the final fallback.
            // An adapter chosen from a file has a real Location; one built into the single-file app has "", which the code below treats as "no file".
#pragma warning disable IL3000
            project = project with { Map = AdapterMap.Resolve(null, project.Data, adapter.GetType().Assembly.Location, project.Root??project.Folder) };
#pragma warning restore IL3000
            if (project.Map is { Length: > 0 } fallback)
            {
                try { map = ReferenceMap.Read(System.IO.File.ReadAllText(fallback)); }
                catch (Exception e) when (e is JsonException or InvalidDocumentException or IOException)
                { return Refuse($"The link map {fallback} can't be read: {e.Message}", out refusal); }
            }
        }
        DataProject data;
        try
        {
            if (adapter is null) data = DataProject.Open(project.Data, project.Schemas);
            // An adapter that gives back nothing is refused, never quietly swapped for plain data under its name (found by A200).
            else if (adapter.Open(project) is { } opened) data = Adapters.ApplyProtectedNames(adapter, opened);
            else return Refuse($"{label} gave back no project from Open. That's a fault in the adapter, not your data: choose None (plain data) to open it without.", out refusal);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidDocumentException)
        {
            return Refuse($"The project can't be opened: {e.Message}", out refusal);
        }
        // An adapter is someone else's code, and any fault in it is that code's (found by A073). Nothing has changed yet.
        catch (Exception e) when (adapter is not null)
        {
            return Refuse($"{label} couldn't open the project: {e.Message}. That's a fault in the adapter, not your data: choose None (plain data) to open it without.", out refusal);
        }
        var full = project with { Data = Path.GetFullPath(project.Data), Schemas = Path.GetFullPath(project.Schemas), Map = project.Map is { Length: > 0 } m ? Path.GetFullPath(m) : null,Root=project.Root };
        return new(data, map, adapter, label, full, notes);
    }

    private static LoadedProject? Refuse(string message, out string refusal)
    {
        refusal = message;
        return null;
    }

    // An adapter's name is only for showing, so one that can't be read is its type's name (found by A073).
    internal static string NameOf(IProjectAdapter adapter, List<string>? notes = null)
    {
        try { return adapter.Name; }
        catch (Exception e)
        {
            notes?.Add($"The adapter {adapter.GetType().Name} couldn't give its name: {e.Message}.");
            return adapter.GetType().Name;
        }
    }

    // An adapter's own check can fail; that's a "no", never a crash on the start screen, and the person is told (A200).
    internal static bool Recognised(IProjectAdapter adapter, ProjectPaths project, List<string>? notes = null)
    {
        try { return adapter.Recognises(project); }
        catch (Exception e)
        {
            notes?.Add($"{NameOf(adapter)} couldn't tell whether this is its project ({e.Message}), so it wasn't used.");
            return false;
        }
    }

}
