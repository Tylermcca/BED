namespace Bed.Editor.ImGui;

// Source-only folder selection. No adapter loading, settings mutation or workspace lifetime.
internal sealed record FolderProject(ProjectPaths Paths, IProjectAdapter? Adapter);

internal static class FolderProjects
{
    internal static FolderProject? Find(string folder, string? explicitMap, bool noAdapter,
        IProjectAdapter? selected, IReadOnlyList<IProjectAdapter> candidates, out string why)
    {
        why = "";
        try
        {
            folder = Path.GetFullPath(folder);
            if ((noAdapter || selected is null) && ProjectPaths.In(folder) is { } standard)
                return new(standard with { Map = explicitMap ?? standard.Map }, noAdapter ? null : selected);
            if (!noAdapter)
            {
                IEnumerable<IProjectAdapter> providers = selected is null ? candidates : new[] { selected! };
                foreach (var provider in providers)
                {
                    ProjectPaths? paths;
                    try { paths = provider.FindProject(folder); }
                    catch (Exception e) { why = $"{ProjectLoader.NameOf(provider)} couldn't inspect the folder: {e.Message}"; return null; }
                    if (paths is null)
                    {
                        // Only a declined selected provider falls back; invalid answers still refuse below.
                        if (selected is not null && ProjectPaths.In(folder) is { } fallback)
                            return new(fallback with { Map = explicitMap ?? fallback.Map }, selected);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(paths.Data) || string.IsNullOrWhiteSpace(paths.Schemas))
                    { why = $"{ProjectLoader.NameOf(provider)} returned empty data or schema paths."; return null; }
                    string Full(string path) => Path.GetFullPath(path, folder);
                    var data = Full(paths.Data);
                    var schemas = Full(paths.Schemas);
                    if (!Directory.Exists(data) || !Directory.Exists(schemas))
                    { why = $"{ProjectLoader.NameOf(provider)} returned a data or schema folder that doesn't exist."; return null; }
                    string? root = null;
                    if (paths.Root is not null)
                    {
                        if (string.IsNullOrWhiteSpace(paths.Root))
                        { why = $"{ProjectLoader.NameOf(provider)} returned an empty project root."; return null; }
                        root = Full(paths.Root);
                        if (!Directory.Exists(root))
                        { why = $"{ProjectLoader.NameOf(provider)} returned a project root that doesn't exist."; return null; }
                        var relative = Path.GetRelativePath(root, data);
                        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        { why = $"{ProjectLoader.NameOf(provider)} returned a project root that doesn't contain its data folder."; return null; }
                    }
                    var map = AdapterMap.Resolve(explicitMap, data, null, root ?? folder)
                        ?? (paths.Map is { Length: > 0 } supplied ? Full(supplied) : null);
                    return new(new ProjectPaths(data, schemas, map) { Folder = folder, Root = root }, provider);
                }
            }
            why = $"{folder} doesn't hold a data folder and a schemas folder, and no adapter recognises it. Choose the two folders separately.";
            return null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        { why = $"The project paths can't be used: {e.Message}"; return null; }
    }
}
