namespace Brutal.Authoring.Editor.ImGui;

// Pure path selection shared with source-linked synthetic tests. No map content or adapter loading here.
internal static class AdapterMap
{
    internal static string? Resolve(string? explicitMap, string data, string? adapterFile, string? originalFolder = null)
    {
        // A supplied path is authoritative even if missing or malformed: the normal loader reports it.
        if (!string.IsNullOrEmpty(explicitMap)) return explicitMap;
        var root = originalFolder ?? Directory.GetParent(Path.GetFullPath(data))?.FullName;
        if (root is not null)
        {
            var local = Path.Combine(root, "reference_map.json");
            if (File.Exists(local)) return local;
        }
        if (!string.IsNullOrEmpty(adapterFile))
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(adapterFile));
            if (folder is not null)
            {
                var beside = Path.Combine(folder, "reference_map.json");
                if (File.Exists(beside)) return beside;
            }
        }
        return null;
    }
}
