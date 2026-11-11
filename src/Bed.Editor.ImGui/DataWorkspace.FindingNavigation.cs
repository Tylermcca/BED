using System.Globalization;
using System.Text.Json;
using Bed.Data;

namespace Bed.Editor.ImGui;

public sealed partial class DataWorkspace
{
    // MIXED: existing checked finding freshness + workspace Back + Inspector reveal. No second document owner.
    private bool RevealFindingPlace(CheckPlace? place)
    {
        if (place is null) { Said = "This finding has no file or JSON Pointer to open."; return false; }
        static bool IsPointer(string? p) => p is not null && (p.Length == 0 || p[0] == '/');
        var at = IsPointer(place.At) ? place.At : IsPointer(place.Item) ? place.Item : null;
        if (at is not null)
            for (var n = 0; n < at.Length; n++)
                if (at[n] == '~' && (++n == at.Length || at[n] is not ('0' or '1')))
                { Said = $"The finding's JSON Pointer '{at}' has an invalid escape; use ~0 for ~ or ~1 for /."; return false; }
        if (Documents.IsScene(place.File)) return RevealSceneFinding(place.File, at);
        if (at is null || at.Length == 0)
        {
            var from = NavigationHere();
            if (!Show(place.File)) return false;
            if (from is not null) history.Push(from);
            Said += at is null && (place.At is not null || place.Item is not null)
                ? " The finding names no JSON Pointer; showing the file instead." : " This finding applies to the whole file.";
            return true;
        }
        if (!GoTo(new ReferenceTarget(place.File, at))) return false;
        var window = Window!;
        if (window.Reveal(at))
        {
            window.RequestInspectorFocus();
            Said = $"At {at} in {place.File}. Back returns." + shownFaults;
        }
        else Said += " The exact field could not be shown: " + window.Status;
        return true; // The owning item/file is still useful when a finding identifies a missing field.
    }

    private bool RevealSceneFinding(string file, string? at)
    {
        if (!Documents.OpenScene(file, out var entry, out var why)) { Said = why; return false; }
        string? id = null;
        var exact = true;
        if (at is { Length: > 0 })
        {
            var parts = Pointer.Split(at);
            if (parts.Count < 2 || parts[0] != "objects" ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index < 0 || index >= entry!.Editor.Session.Current.Objects.Length)
            { Said = $"The finding's JSON Pointer '{at}' does not locate an object in {file}."; return false; }
            var obj = entry.Editor.Session.Current.Objects[index];
            id = obj.Id;
            exact = new DataDocument(JsonSerializer.SerializeToElement(obj)).At(Pointer.Join(parts.Skip(2))) is not null;
        }
        var from = NavigationHere();
        if (!Show(file)) return false;
        if (from is not null) history.Push(from);
        if (id is not null)
        {
            var ok = SceneWindow!.GoTo(Issue.Error("Check finding", id));
            Said = ok ? (exact ? $"At {at} in {file}; selected {id} in Objects. Back returns."
                : $"The field {at} is absent in {file}; selected its owner {id} in Objects. Back returns.") + shownFaults : SceneWindow.Status;
            return ok;
        }
        Said += " This finding applies to the whole file.";
        return true;
    }
}
