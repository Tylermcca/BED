using Brutal.Authoring.Editor.ImGui;

namespace Brutal.Authoring.Editor.App;

/// <summary>Formats the native title from current project facts and suppresses unchanged host updates.</summary>
public sealed class WindowTitle
{
    private string current;

    /// <summary>Starts with the title already used when the host window was created.</summary>
    public WindowTitle(string initial) => current = initial;

    /// <summary>Formats a start screen, a project, or its active file; only the active file's edits add a star.</summary>
    public static string Format(string? project, string? file = null, bool dirty = false) =>
        project is null ? "BED" : file is null ? $"{project} - BED" : $"{file}{(dirty ? "*" : "")} - {project} - BED";

    /// <summary>Reads the launcher's existing current project and active file without changing either.</summary>
    public static string For(ProjectLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        if (launcher.Current is not { } project) return Format(null);
        var active = launcher.Workspace?.Active;
        return Format(ProjectLauncher.Name(project), active?.Name, active?.Dirty ?? false);
    }

    /// <summary>Publishes only changed text. A failed host update is not recorded as successful.</summary>
    public bool Update(string title, Action<string> setTitle)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(setTitle);
        if (string.Equals(current, title, StringComparison.Ordinal)) return false;
        setTitle(title);
        current = title;
        return true;
    }
}
