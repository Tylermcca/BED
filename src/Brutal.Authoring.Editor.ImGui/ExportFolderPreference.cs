using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Brutal.Authoring.Editor.ImGui;

// Optional UI history, separate from project settings, build approval and copy authority.
internal sealed class ExportFolderPreference
{
    private readonly string file;
    internal ExportFolderPreference(string project, string preferencesFolder)
    {
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project));
        if (OperatingSystem.IsWindows()) key = key.ToUpperInvariant();
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        file = Path.Combine(Path.GetFullPath(preferencesFolder), "export-folders", id + ".json");
    }
    internal string? Read()
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024) return null;
            var folder = JsonSerializer.Deserialize<string>(File.ReadAllText(file));
            return Valid(folder) ? folder : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    internal void Remember(string folder)
    {
        try
        {
            if (!Valid(folder)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(Path.GetFullPath(folder)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { } // A successful copy stays successful.
    }
    private static bool Valid(string? folder) => !string.IsNullOrWhiteSpace(folder) && !folder.Contains('\0')
        && Path.IsPathFullyQualified(folder) && Encoding.UTF8.GetByteCount(folder) < 4096;
}
