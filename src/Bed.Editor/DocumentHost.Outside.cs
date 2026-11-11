using Bed.Data;
namespace Bed.Editor;

/// <summary>An open file whose saved copy another program changed (an AI assistant, a text editor, a script), and that BED
/// couldn't simply load: it has unsaved edits in BED, its level is in Play, or the new version can't be read.</summary>
/// <param name="Name">The file's name in the project.</param>
/// <param name="HasEdits">True when BED has unsaved edits to it, so the person chooses which version to keep.</param>
/// <param name="Problem">Why the new version couldn't be loaded, or null.</param>
public sealed record ChangedOutside(string Name, bool HasEdits, string? Problem);

// INVENTED from VS Code's and Rider's rule for files another program changes: a file with no unsaved edits quietly loads the new
// version; one with edits asks once, with one button per choice. Nobody has to close the project to see the new version.
public sealed partial class DocumentHost
{
    private readonly Dictionary<string, (DateTime Written, long Length)> stamps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChangedOutside> waiting = new(StringComparer.Ordinal);

    /// <summary>Loads the saved version of every open file another program changed that has no unsaved edits in BED; returns the
    /// names it loaded. A file whose date and size haven't moved since the last call isn't read again, so this is cheap to call often.
    /// Files it couldn't load are in <see cref="WaitingOutside"/>. A level in Play is left until Play stops.</summary>
    public IReadOnlyList<string> ReloadChangedOutside()
    {
        var loaded = new List<string>();
        foreach (var file in project.Files)
        {
            if (!IsOpen(file.Name)) { stamps.Remove(file.Name); waiting.Remove(file.Name); continue; }
            var playing = Playing(file.Name);
            if (!Touched(file) && !(playing is false && waiting.TryGetValue(file.Name, out var held) && !held.HasEdits && held.Problem is null)) continue;
            if (Reload(file, onlyIfClean: true)) loaded.Add(file.Name);
        }
        return loaded;
    }

    /// <summary>Open files changed outside that BED couldn't load by itself, by name.</summary>
    public IReadOnlyList<ChangedOutside> WaitingOutside => waiting.Values.OrderBy(w => w.Name, StringComparer.Ordinal).ToArray();

    /// <summary>Replaces <paramref name="name"/>'s open copy with its saved version, dropping BED's unsaved edits to it. False, with
    /// <paramref name="why"/>, when it isn't open, is in Play, or the saved version can't be read or isn't valid (then nothing changes).</summary>
    public bool LoadSaved(string name, out string why)
    {
        why = "";
        if (project.Find(name) is not { } file || !IsOpen(name)) { why = $"{name} isn't open."; return false; }
        if (Playing(name)) { why = $"Stop Play before loading the saved version of {name}."; return false; }
        if (Reload(file, onlyIfClean: false)) return true;
        why = waiting.TryGetValue(name, out var w) && w.Problem is { } problem ? problem : $"{name} has no saved version to load.";
        return false;
    }

    /// <summary>Keeps BED's edits to <paramref name="name"/> over the version another program saved: the next save writes over it.</summary>
    public bool KeepMine(string name, out string why)
    {
        why = "";
        var ok = scenes.TryGetValue(name, out var one) ? one.Session.KeepMine()
            : scene2.TryGetValue(name, out var two) ? two.Session.KeepMine()
            : project.Find(name)?.Session is { } data && data.KeepMine();
        if (!ok) { why = $"{name}'s saved version can't be read, so there's nothing to keep your version over."; return false; }
        waiting.Remove(name); project.Find(name)?.ClearProblems();
        return true;
    }

    // True when the file's date or size moved since last looked at (and records them).
    private bool Touched(DataFile file)
    {
        DateTime written; long length;
        try { var info = new FileInfo(file.Path); if (!info.Exists) return false; (written, length) = (info.LastWriteTimeUtc, info.Length); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        if (stamps.TryGetValue(file.Name, out var seen) && seen == (written, length)) return false;
        stamps[file.Name] = (written, length);
        return true;
    }

    // Loads the saved version when it changed outside (or always, when not onlyIfClean). True when it loaded.
    private bool Reload(DataFile file, bool onlyIfClean)
    {
        var name = file.Name;
        if (scenes.TryGetValue(name, out var one)) return One(one.Session, one.Editor.Playing is not null);
        if (scene2.TryGetValue(name, out var two)) return One(two.Session, two.Editor.Playing is not null);
        return file.Session is { } data && One(data, false);

        bool One<T>(DocumentSession<T> session, bool playing) where T : class
        {
            var state = session.CheckDisk();
            if (state is DiskState.Missing or DiskState.NoFile || (onlyIfClean && state != DiskState.ChangedOnDisk)) { waiting.Remove(name); return false; }
            if (playing || (onlyIfClean && session.Dirty)) { waiting[name] = new(name, session.Dirty, null); return false; }
            if (session.Revert()) { waiting.Remove(name); file.ClearProblems(); return true; }
            waiting[name] = new(name, session.Dirty, $"{name}'s saved version can't be loaded: " + string.Join(" ", session.LastIssues.Select(i => i.Message)));
            return false;
        }
    }

    private bool IsOpen(string name) => scenes.ContainsKey(name) || scene2.ContainsKey(name) || project.Find(name)?.Session is not null;
    private bool Playing(string name) => (scenes.TryGetValue(name, out var one) && one.Editor.Playing is not null)
        || (scene2.TryGetValue(name, out var two) && two.Editor.Playing is not null);
}
