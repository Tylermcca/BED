using System.Text.Json;
using System.Text.Json.Nodes;

namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Optional recent-open time and the display kind at the time it is read. A missing time is unknown.</summary>
public sealed record RecentProjectMetadata(DateTimeOffset? LastOpenedUtc, string Kind);

// INVENTED (code-health rank 13): persistence ownership behind RecentProjects.
// Existing read/merge/lock/write/fallback order is retained; no new validation or concurrency policy.
internal sealed class RecentProjectStore
{
    private sealed record Entry(ProjectPaths Project, DateTimeOffset? LastOpenedUtc = null, string? Kind = null);
    private List<Entry> items = [];

    /// <param name="file">Where the list is kept; it's created when first saved.</param>
    /// <param name="limit">How many are kept.</param>
    public RecentProjectStore(string file, int limit = 10)
    {
        File = file;
        Limit = limit;
        if (ReadDisk() is { } found) items = found;   // a list that can't be read starts empty, and isn't written over
    }

    /// <summary>The settings file.</summary>
    public string File { get; }

    /// <summary>How many are kept.</summary>
    public int Limit { get; }

    /// <summary>The projects, newest first.</summary>
    public IReadOnlyList<ProjectPaths> Items => items.Select(e => e.Project).ToArray();

    public RecentProjectMetadata Metadata(ProjectPaths project)
    {
        var entry = items.FirstOrDefault(e => Same(e.Project, project));
        var present = project.Folder is { } folder ? Directory.Exists(folder)
            : Directory.Exists(project.Data) && Directory.Exists(project.Schemas);
        return new(entry?.LastOpenedUtc, present ? entry?.Kind ?? "Unknown" : "Folder not found");
    }

    // Only the successful-open caller supplies this event. Plain Add never invents a timestamp.
    public void Opened(ProjectPaths project, string kind)
    {
        if (!Usable(project)) return;
        var opened = new Entry(project, DateTimeOffset.UtcNow, kind);
        Change(list =>
        {
            list.RemoveAll(e => Same(e.Project, project));
            list.Insert(0, opened);
        });
    }

    /// <summary>Puts <paramref name="project"/> first (moving it if it's there) and saves the list.</summary>
    public void Add(ProjectPaths project)
    {
        if (!Usable(project)) return;
        Change(list =>
        {
            var previous = list.FirstOrDefault(e => Same(e.Project, project));
            list.RemoveAll(e => Same(e.Project, project));
            list.Insert(0, new(project, previous?.LastOpenedUtc, previous?.Kind));
        });
    }

    /// <summary>Takes <paramref name="project"/> off the list and saves it. Its files are untouched.</summary>
    public void Remove(ProjectPaths project) => Change(list => list.RemoveAll(e => Same(e.Project, project)));

    private static bool Same(ProjectPaths a, ProjectPaths b) =>
        a.Folder is not null || b.Folder is not null
            ? string.Equals(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a.Data, b.Data, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Schemas, b.Schemas, StringComparison.OrdinalIgnoreCase);

    // An entry the launcher can show and open: both folders named, and each a path this system can hold (a NUL in one
    // stopped the start screen; found by A203).
    private static bool Usable(ProjectPaths? p)
    {
        if (p is null || (p.Folder is null && p is not { Data.Length: > 0, Schemas.Length: > 0 })) return false;
        try
        {
            if (p.Folder is { } folder) Path.GetFullPath(folder);
            else { Path.GetFullPath(p.Data); Path.GetFullPath(p.Schemas); }
            if (p.Map is { Length: > 0 } map) Path.GetFullPath(map);
            if (p.AdapterPath is { Length: > 0 } adapter) Path.GetFullPath(adapter);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    // The list on disk: missing is empty; unreadable (locked, not JSON, not a list) is null. Each entry is read on its own.
    private List<Entry>? ReadDisk()
    {
        string text;
        try
        {
            if (!System.IO.File.Exists(File)) return [];
            text = System.IO.File.ReadAllText(File);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<Entry>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                ProjectPaths? p = null;
                try { p = entry.Deserialize<ProjectPaths>(); }
                catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException) { }
                if (Usable(p) && !list.Exists(q => Same(q.Project, p!)))
                {
                    DateTimeOffset? opened = null;
                    if (entry.TryGetProperty("LastOpenedUtc"u8, out var time) && time.ValueKind == JsonValueKind.String
                        && time.GetString() is { } stamp
                        && (stamp.EndsWith('Z') || (stamp.Length >= 6 && (stamp[^6] == '+' || stamp[^6] == '-')))
                        && time.TryGetDateTimeOffset(out var parsed)) opened = parsed.ToUniversalTime();
                    var kind = entry.TryGetProperty("Kind"u8, out var name) && name.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(name.GetString()) ? name.GetString() : null;
                    list.Add(new(p!, opened, kind));
                }
                if (list.Count == Limit) break;
            }
            return list;
        }
        catch (JsonException) { return null; }
    }

    // Applies a change to the list as it is on disk now, under a lock another launcher also takes, and writes the result whole
    // (a temp file, then a replace). When the list on disk can't be read, it's left alone and the change is kept here only.
    private void Change(Action<List<Entry>> change)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(File))!);
            using var held = Lock();
            if (held is null || ReadDisk() is not { } disk)
            {
                change(items);
                Trim(items);
                return;
            }
            change(disk);
            Trim(disk);
            items = disk;
            var temp = File + ".tmp";
            System.IO.File.WriteAllText(temp, Serialize(items));
            System.IO.File.Move(temp, File, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            change(items);   // not remembering is never fatal
            Trim(items);
        }
    }

    // Keep the legacy flat array/path fields, adding optional metadata without changing ProjectPaths equality.
    private static string Serialize(List<Entry> list)
    {
        var array = new JsonArray();
        foreach (var entry in list)
        {
            var value = JsonSerializer.SerializeToNode(entry.Project)!.AsObject();
            if (entry.LastOpenedUtc is { } opened) value["LastOpenedUtc"] = JsonValue.Create(opened);
            if (entry.Kind is { } kind) value["Kind"] = kind;
            array.Add(value);
        }
        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private void Trim(List<Entry> list)
    {
        if (list.Count > Limit) list.RemoveRange(Limit, list.Count - Limit);
    }

    // A lock file beside the list, held only while one change is made; another launcher waits up to two seconds for it.
    private FileStream? Lock()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            try { return new FileStream(File + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(20); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
    }
}
