using Bed.Editor.ImGui;

namespace Bed.Agent.Cli;

// Same automatic choice as ProjectLoader: the first loaded adapter that recognises explicit project paths.
// An explicitly supplied DLL must never silently fall back to plain data when none of its adapters matches.
internal static class CliAdapterSelection
{
    internal static IProjectAdapter? Load(string path, ProjectPaths project, TextWriter errors)
    {
        var (loaded, problems) = Adapters.Load([path]);
        if (problems.Count > 0)
        {
            errors.WriteLine($"the adapter {path} didn't load: {string.Join(" ", problems)} Fix it, or choose a working adapter DLL.");
            return null;
        }
        if (loaded.Count == 0)
        {
            errors.WriteLine($"{path} has no adapter: no public class implementing IProjectAdapter with a public constructor that takes nothing.");
            return null;
        }
        // Keep the established explicit single-adapter route, including adapters without automatic recognition.
        if (loaded.Count == 1) return loaded[0];
        foreach (var adapter in loaded)
        {
            bool recognised;
            try { recognised = adapter.Recognises(project); }
            catch (Exception error)
            {
                errors.WriteLine($"{adapter.GetType().Name} couldn't tell whether this is its project ({error.Message}), so it wasn't used.");
                continue;
            }
            if (!recognised) continue;
            errors.WriteLine($"Using adapter {adapter.GetType().Name} for this project.");
            return adapter;
        }
        errors.WriteLine($"None of the {loaded.Count} adapters in {path} recognises this project. Check --data and --schemas, or choose the DLL for this project; nothing was opened or saved.");
        return null;
    }
}
