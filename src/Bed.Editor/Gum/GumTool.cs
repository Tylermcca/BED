using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bed.Editor.Gum;

/// <summary>Starts the person's own Gum tool on a project or one of its screens. BORROWED: Gum's own command line, which
/// takes a project file, or a screen file saved directly in the project's Screens folder and selects that screen.</summary>
public static class GumTool
{
    /// <summary>What to start: the program and its one argument, plus a note when Gum can't select the screen itself.
    /// <see cref="Problem"/> is set instead when nothing can be started.</summary>
    public sealed record Launch(string? Program, string? Argument, string Note, string? Problem, string? Project = null)
    {
        internal string? ScreenName { get; init; }
    }

    /// <summary>Works out how to open <paramref name="screen"/> (or the whole project when null) in the Gum tool at
    /// <paramref name="gumExe"/>. Nothing is started.</summary>
    public static Launch Plan(string? gumExe, GumProject project, GumScreen? screen)
    {
        if (string.IsNullOrWhiteSpace(gumExe))
            return new(null, null, "", NotFound);
        if (!File.Exists(gumExe))
            return new(null, null, "", $"Gum isn't at {gumExe} any more. Press Edit in Gum again and show BED where Gum.exe is now.");
        if (!File.Exists(project.ProjectFile))
            return new(null, null, "", $"The Gum project file {Path.GetFileName(project.ProjectFile)} isn't in its folder any more, so there is nothing to open.");
        if (screen is null) return new(gumExe, Path.GetFullPath(project.ProjectFile), "Gum is opening your screens. Save there and they update here and in your game.", null, Path.GetFullPath(project.ProjectFile));
        if (screen.Missing || !File.Exists(screen.File))
            return new(gumExe, Path.GetFullPath(project.ProjectFile), $"The screen {screen.Name} hasn't been saved as its own file, so Gum is opening the whole project instead.", null, Path.GetFullPath(project.ProjectFile)) { ScreenName = screen.Name };
        // Gum finds the project beside the Screens folder only for a screen saved directly in it.
        var screens = Path.Combine(project.Folder, "Screens");
        var direct = string.Equals(Path.GetDirectoryName(Path.GetFullPath(screen.File)), screens, StringComparison.OrdinalIgnoreCase);
        return direct
            ? new(gumExe, Path.GetFullPath(screen.File), $"Gum is opening the screen {screen.Name}.", null, Path.GetFullPath(project.ProjectFile)) { ScreenName = screen.Name }
            : new(gumExe, Path.GetFullPath(project.ProjectFile), $"Gum is opening your project. Gum can't go straight to a screen in a subfolder, so pick {screen.Name} from the screen list in Gum.", null, Path.GetFullPath(project.ProjectFile)) { ScreenName = screen.Name };
    }

    /// <summary>Starts what <see cref="Plan"/> worked out, without a shell, so the path is passed exactly as it is.
    /// Returns the note to show, or the problem. When Gum already has the project open, that Gum is brought to the front instead of opening a second one.</summary>
    public static (bool Started, string Message) Start(Launch launch, Func<ProcessStartInfo, bool>? start = null, Func<string, bool>? showOpen = null)
    {
        if (launch.Problem is { } problem) return (false, problem);
        if (launch.Project is { } project && (showOpen ?? ShowOpen)(project))
            return (true, "Gum already has these screens open, so BED brought it to the front."
                + (launch.ScreenName is { } name ? $" Pick {name} from the screen list there." : ""));
        var info = new ProcessStartInfo(launch.Program!) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(launch.Argument!)! };
        info.ArgumentList.Add(launch.Argument!);
        try { return (start ?? (i => Process.Start(i) is not null))(info) ? (true, launch.Note) : (false, "Gum didn't start. Try opening Gum on its own to check it works."); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { return (false, "Windows couldn't start Gum. Try opening Gum on its own to check it works. You can change which Gum.exe BED uses in Options."); }
    }

    /// <summary>Where Gum usually is, in the order BED looks: Gum.exe on the PATH, a Gum folder in the person's Programs,
    /// Program Files, Downloads, Desktop or Documents folders, and a Gum folder one level into any drive (C:\Tools\Gum,
    /// C:\Gum ...). Folders that can't be read are passed over.</summary>
    public static IEnumerable<string> UsualPlaces()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(dir, "Gum.exe");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] parents =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ];
        foreach (var drive in Safe(() => DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.RootDirectory.FullName).ToArray()))
        {
            yield return Path.Combine(drive, "Gum", "Gum.exe");
            foreach (var folder in Safe(() => Directory.GetDirectories(drive))) yield return Path.Combine(folder, "Gum", "Gum.exe");
        }
        foreach (var parent in parents.Where(p => p.Length > 0))
            foreach (var folder in Safe(() => Directory.GetDirectories(parent, "*Gum*")))
            {
                yield return Path.Combine(folder, "Gum.exe");
                yield return Path.Combine(folder, "Gum", "Gum.exe");
            }
    }

    /// <summary>Gum.exe: <paramref name="remembered"/> when it is still there, otherwise the first of
    /// <paramref name="places"/> that exists, or null when Gum isn't found.</summary>
    public static string? Find(string? remembered, IEnumerable<string> places)
    {
        if (!string.IsNullOrWhiteSpace(remembered) && File.Exists(remembered)) return remembered;
        foreach (var place in places)
            if (string.Equals(Path.GetFileName(place), "Gum.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(place)) return Path.GetFullPath(place);
        return null;
    }

    /// <summary>Brings to the front a Gum that has <paramref name="projectFile"/> open (by the window title Gum shows). False when no Gum has it open, or not on Windows.</summary>
    public static bool ShowOpen(string projectFile)
    {
        if (!OperatingSystem.IsWindows()) return false;
        foreach (var gum in Process.GetProcessesByName("Gum"))
            using (gum)
            {
                try
                {
                    var window = gum.MainWindowHandle;
                    if (window == 0 || !SameFile(gum.MainWindowTitle, projectFile)) continue;
                    ShowWindow(window, IsIconic(window) ? 9 : 5);   // restore when minimised, else show
                    SetForegroundWindow(window);
                    return true;
                }
                catch (InvalidOperationException) { } // it closed meanwhile
            }
        return false;
    }

    /// <summary>Whether a Gum window title names <paramref name="projectFile"/>. Gum's source titles the window with the
    /// project's full path; Gum 2026.11.02 shows only its name ("CheckersUi"), so either is taken, with or without the
    /// file ending and a trailing "*" for unsaved changes.</summary>
    public static bool SameFile(string title, string projectFile)
    {
        var shown = title.Trim().TrimEnd('*').Trim();
        if (shown.Length == 0) return false;
        if (string.Equals(shown, Path.GetFileName(projectFile), StringComparison.OrdinalIgnoreCase)
            || string.Equals(shown, Path.GetFileNameWithoutExtension(projectFile), StringComparison.OrdinalIgnoreCase)) return true;
        try { return Path.IsPathFullyQualified(shown) && string.Equals(Path.GetFullPath(shown), Path.GetFullPath(projectFile), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool ShowWindow(nint window, int show);
    [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool IsIconic(nint window);

    /// <summary>Said when Gum isn't on the computer and the person didn't pick it.</summary>
    public const string NotFound = "Gum isn't on this computer yet. It's free: download it from https://docs.flatredball.com/gum, then press Edit in Gum again and show BED where Gum.exe is.";

    private static string[] Safe(Func<string[]> list)
    {
        try { return list(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
    }
}
