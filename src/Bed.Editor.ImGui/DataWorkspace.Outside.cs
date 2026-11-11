using Brutal.ImGuiApi;
using Bed.Editor;

namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

// Files another program changes while they're open (an AI assistant adding to a level, a text editor): a file with no unsaved edits
// loads the new version by itself; one with edits gets a small box with one button per choice. No closing and reopening the project.
public sealed partial class DataWorkspace
{
    private long outsideCheckedAt;
    private string outsideBoxSaid = "";

    /// <summary>How often open files are looked at for changes made outside BED.</summary>
    public static TimeSpan OutsideCheckEvery { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Loads the saved version of open files another program changed and that have no unsaved edits here, and says so.
    /// Called by every frame (at most every <see cref="OutsideCheckEvery"/>) and before Play.</summary>
    public IReadOnlyList<string> PickUpOutsideChanges()
    {
        outsideCheckedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var loaded = Documents.ReloadChangedOutside();
        if (loaded.Count > 0)
            Said = (loaded.Count == 1 ? $"{loaded[0]} was" : string.Join(", ", loaded) + " were") + " changed outside BED; loaded the new version.";
        return loaded;
    }

    private void WatchOutside()
    {
        if (System.Diagnostics.Stopwatch.GetElapsedTime(outsideCheckedAt) >= OutsideCheckEvery) PickUpOutsideChanges();
    }

    /// <summary>Loads the saved version of <paramref name="name"/>, dropping the unsaved edits to it here.</summary>
    public bool UseSavedVersion(string name)
    {
        if (!Documents.LoadSaved(name, out var why)) { outsideBoxSaid = why; Said = why; return false; }
        outsideBoxSaid = ""; Said = $"Loaded the saved version of {name}; your unsaved changes to it were dropped.";
        return true;
    }

    /// <summary>Keeps the unsaved edits to <paramref name="name"/>; the next save writes over the version saved outside.</summary>
    public bool KeepYourVersion(string name)
    {
        if (!Documents.KeepMine(name, out var why)) { outsideBoxSaid = why; Said = why; return false; }
        outsideBoxSaid = ""; Said = $"Kept your changes to {name}. Save to write them over the other version.";
        return true;
    }

    private void DrawOutsideChoices()
    {
        var waiting = Documents.WaitingOutside.Where(w => w.HasEdits || w.Problem is not null).ToArray();
        if (waiting.Length == 0) return;
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Brutal.Numerics.float2(0.5f, 0.5f));
        if (Ui.Begin("Changed outside BED###workspace.outside"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            foreach (var file in waiting)
            {
                ImGui.PushID(file.Name);
                if (file.HasEdits)
                {
                    Ui.TextWrapped($"{file.Name} was changed by another program, and you have unsaved changes to it here.");
                    if (Ui.Button("Use the saved version"u8)) UseSavedVersion(file.Name);
                    Ui.FieldCaption("Loads the other program's version and drops your unsaved changes."u8);
                    if (Ui.Button("Keep my changes"u8)) KeepYourVersion(file.Name);
                    Ui.FieldCaption("Your next save writes your version over the other one."u8);
                }
                if (file.Problem is { } problem)
                {
                    Ui.TextWrapped(problem);
                    Ui.TextWrapped("BED keeps showing the version it has. Fix the file there, or keep BED's version and save to write it over."u8);
                    if (!file.HasEdits && Ui.Button("Keep BED's version"u8)) KeepYourVersion(file.Name);
                }
                ImGui.Separator();
                ImGui.PopID();
            }
            if (outsideBoxSaid.Length > 0) Ui.TextWrapped(outsideBoxSaid);
        }
        Ui.End();
    }
}
