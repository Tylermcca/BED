using Bed.Editor.Gum;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

// Preferences > Locations: where BED finds things on this computer (Gum, the usual folder for new projects, a folder of
// extra adapters). Kept with the person's other preferences, never in a project. Empty means BED's normal place.
internal static class Locations
{
    /// <summary>Gum.exe, or empty for "BED finds Gum itself".</summary>
    internal static string Gum { get; set; } = "";
    /// <summary>The usual folder for new projects, or empty for Documents\BED Projects.</summary>
    internal static string Projects { get; set; } = "";
    /// <summary>A folder of extra adapters BED loads as well as its own, or empty for none.</summary>
    internal static string Adapters { get; set; } = "";
    /// <summary>The ordinary Windows file pickers, when the app has them.</summary>
    internal static Func<IFileDialogs?> Dialogs { get; set; } = () => null;
    /// <summary>Where BED looks for Gum when asked to find it; tests replace it.</summary>
    internal static Func<IEnumerable<string>> GumPlaces { get; set; } = GumTool.UsualPlaces;

    private static readonly Row gum = new("Gum", "Gum.exe"), projects = new("New projects go in", null), adapters = new("Extra adapters", null);

    internal static void Draw()
    {
        // Folded away until opened, so Preferences fits its window: most people never change these.
        if (!Ui.TreeNodeEx("Locations"u8, ImGuiTreeNodeFlags.CollapsingHeader)) return;
        Ui.TextWrapped("Where BED finds things on this computer: Gum, the folder new projects go in, and extra adapters. Leave a line empty to use BED's normal place."u8);
        Gum = gum.Draw(Gum, "BED finds Gum by itself the first time you press Edit in Gum, or asks you where it is.", () =>
        {
            var found = GumTool.Find(null, GumPlaces());
            if (found is null) gum.Note = "BED couldn't find Gum on this computer. It's free: download it from https://docs.flatredball.com/gum.";
            return found;
        });
        if (Gum.Length > 0 && !File.Exists(Gum)) Ui.TextWrapped("There's no Gum.exe there. Check the folder and the spelling, or press Find it for me."u8);
        Projects = projects.Draw(Projects, $"Normally {ProjectCreator.DefaultLocation ?? "your Documents folder"}.", null);
        if (Projects.Length > 0 && !Directory.Exists(Projects)) Ui.TextWrapped("That folder isn't there yet. BED makes it when you create a project there."u8);
        Adapters = adapters.Draw(Adapters, "Adapters teach BED about a game. BED always uses the ones that come with it; put more in a folder of your own and choose it here. BED loads them the next time it starts.", null);
        if (Adapters.Length > 0 && !Directory.Exists(Adapters)) Ui.TextWrapped("There's no folder there. Check the spelling."u8);
    }

    // One location: its box, Browse..., an optional Find it for me, and Back to normal. Returns the new value.
    private sealed class Row(string label, string? file)
    {
        private ImInputString text = new(1024, ""u8);
        private string shown = "";
        public string Note = "";   // what the last button found, shown under this line

        public string Draw(string value, string normal, Func<string?>? find)
        {
            if (shown != value) { text = new ImInputString(1024, value); shown = value; }
            ImGui.PushID(label);
            // A box about as wide as a usual location, its name beside it, and the buttons on the same line when they fit.
            var style = ImGui.GetStyle();
            var captions = new List<string>(3);
            if (Dialogs() is not null) captions.Add("Browse...");
            if (find is not null) captions.Add("Find it for me");
            captions.Add("Back to normal");
            var buttons = captions.Sum(c => ImGui.CalcTextSize(c).X + style.FramePadding.X * 2 + style.ItemSpacing.X);
            var name = ImGui.CalcTextSize(label).X + style.ItemInnerSpacing.X + style.ItemSpacing.X;
            var room = ImGui.GetContentRegionAvail().X;
            var box = MathF.Max(ImGui.GetFontSize() * 6, MathF.Min(ImGui.GetFontSize() * 24, room - name));
            ImGui.SetNextItemWidth(box);
            if (Ui.InputText($"{label}###box", text)) { text.EvaluateLength(); value = shown = text.ToString().Trim().Trim('"'); Note = ""; }
            if (box + name + buttons <= room) ImGui.SameLine();
            if (Dialogs() is { } dialogs)
            {
                if (Ui.Button("Browse..."u8))
                {
                    var picked = file is null ? dialogs.PickFolder(label, value.Length > 0 ? value : null)
                        : dialogs.OpenFile($"Where is {label}? Pick {file}", [new FileDialogFilter(label, file)], null);
                    if (!string.IsNullOrWhiteSpace(picked)) { value = picked; Note = ""; }
                }
                ImGui.SameLine();
            }
            if (find is not null)
            {
                if (Ui.Button("Find it for me"u8)) { Note = ""; if (find() is { } found) { value = found; Note = $"Found {label} at {found}."; } }
                ImGui.SameLine();
            }
            if (Ui.Button("Back to normal"u8)) { value = ""; Note = ""; }
            if (value.Length == 0) Ui.TextWrapped(normal);
            if (Note.Length > 0) Ui.TextWrapped(Note);
            ImGui.PopID();
            return value;
        }
    }
}
