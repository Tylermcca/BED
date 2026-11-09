using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;

namespace Brutal.Authoring.Launcher;

// Read-only snapshots. File choice and key choices are per-panel/session, never persisted.
internal sealed class CompareFilePanel(DataFile file, EditorModel<DataDocument> editor, JsonSchemas schemas, Func<string, bool>? navigate = null)
{
    private readonly FolderPicker picker = new();
    private readonly Dictionary<string, string?> keys = new(StringComparer.Ordinal);
    private bool picking;
    private string? path;
    private Task<Loaded>? pending;
    private Loaded? loaded;
    private int page, listPage, problemPage;
    private int changeIndex = -1;
    internal string NavigationStatus { get; private set; } = "";
    internal sealed record Loaded(DataDocument? Before, DataDocument Current, StructuralCompare.Result? Result, string? Error);

    internal Task<Loaded>? Pending => pending;

    internal Panel Panel() => new("editor.compare", "Compare", Draw, OpenAtStart: false, Width: 650, Height: 500,
        Commands: [new("editor.compare.saved", "Compare with saved", CompareWithSaved,
            () => editor.Session.FilePath is null ? "This document has no saved file." : null),
            new("next", "Next change", () => Move(1), NavigationReason),
            new("previous", "Previous change", () => Move(-1), NavigationReason)]);

    private bool CompareWithSaved()
    {
        if (editor.Session.FilePath is not { } saved) return false;
        path = saved;
        Start(readDisk: true);
        editor.SetOpen("editor.compare", true);
        return true;
    }

    private void FinishPending()
    {
        if (pending is not { IsCompleted: true }) return;
        loaded = pending.GetAwaiter().GetResult();
        pending = null;
    }

    internal string? NavigationReason()
    {
        FinishPending();
        if (pending is not null) return "A compare is still running; wait for it to finish.";
        if (loaded is null) return "Compare a file first.";
        if (!ReferenceEquals(loaded.Current, editor.Session.Current)) return "The file changed since this compare; compare again.";
        if (loaded.Error is { } error) return error;
        if (loaded.Result is not { Changes.Count: > 0 }) return "This compare has no changes.";
        if (navigate is null) return "Navigation is unavailable in this host.";
        return null;
    }

    internal bool Move(int direction)
    {
        if (NavigationReason() is { } why) { NavigationStatus = why; return false; }
        var result = loaded!.Result!;
        var next = changeIndex < 0 ? (direction > 0 ? 0 : result.Changes.Count - 1)
            : Math.Clamp(changeIndex + direction, 0, result.Changes.Count - 1);
        var change = result.Changes[next];
        var target = CompareNavigation.Target(loaded.Before!, loaded.Current, result, change);
        changeIndex = next;
        page = next / 30;
        if (target is null)
        {
            NavigationStatus = "Removed: its parent list no longer has an unambiguous current location.";
            return false;
        }
        if (!navigate!(target)) { NavigationStatus = "The change could not be shown in the current file."; return false; }
        NavigationStatus = $"Change {next + 1} of {result.Changes.Count}: {change.Kind}" +
            (change.AfterAt is null ? " (removed; showing its parent list)." : ".");
        return true;
    }

    private void Start(bool readDisk)
    {
        if (path is null) return;
        var chosen = path;
        var current = editor.Session.Current;
        var old = readDisk ? null : loaded?.Before;
        var choices = new Dictionary<string, string?>(keys, StringComparer.Ordinal);
        page = listPage = problemPage = 0;
        changeIndex = -1;
        NavigationStatus = "";
        // No editor or ImGui calls on the worker; only immutable snapshots and a read-only file read.
        pending = Task.Run(() =>
        {
            try
            {
                var before = old ?? new DataFormat().Read(File.ReadAllText(chosen));
                return new Loaded(before, current, StructuralCompare.Compare(before, current, choices), null);
            }
            catch (Exception e) { return new Loaded(null, current, null, $"Could not compare {chosen}: {e.Message}"); }
        });
    }

    private void Draw()
    {
        Ui.TextWrapped("Read-only: disk copy (before) versus current draft (after). No merge or save."u8);
        if (Ui.Button("Choose another copy..."u8)) { picker.Start(path ?? file.Path, chooseFile: true); picking = true; }
        if (picking && picker.Draw(chooseFile: true) is { } picked)
        {
            picking = false;
            if (picked.Length > 0) { path = picked; Start(readDisk: true); }
        }
        if (path is null) return;
        Ui.TextWrapped(path);
        FinishPending();
        if (pending is not null) { Ui.TextWrapped("Comparing snapshots..."u8); return; }
        if (Ui.Button("Read copy and compare again"u8)) { Start(readDisk: true); return; }
        if (loaded is null) return;
        if (!ReferenceEquals(loaded.Current, editor.Session.Current))
            Ui.TextWrapped("The draft changed. These results are stale; compare again. The disk copy is also a snapshot, not watched."u8);
        else Ui.TextWrapped("The disk copy is a snapshot, not watched. Compare again to read changes on disk."u8);
        if (loaded.Error is { } error) { Ui.TextWrapped(error); return; }
        var result = loaded.Result!;
        var total = CompareNavigation.Total(result);
        Ui.TextWrapped($"File: {total.Added} added, {total.Removed} removed, {total.Changed} changed.");
        foreach (var counts in CompareNavigation.ByList(loaded.Before!, loaded.Current, result))
            Ui.TextWrapped($"{counts.List}: {counts.Added} added, {counts.Removed} removed, {counts.Changed} changed.");
        if (NavigationStatus.Length > 0) Ui.TextWrapped(NavigationStatus);
        Ui.TextWrapped($"{(result.Complete ? "Complete" : "Incomplete")}: {result.Changes.Count} differences, {result.Problems.Count} problems.");
        foreach (var problem in result.Problems.Skip(problemPage * 20).Take(20)) Ui.TextWrapped(problem);
        if (problemPage > 0 && Ui.Button("Previous problems"u8)) problemPage--;
        if ((problemPage + 1) * 20 < result.Problems.Count && Ui.Button("More problems"u8)) problemPage++;
        ImGui.Separator();
        Ui.TextWrapped("List identity (remembered only in this session):"u8);
        foreach (var list in result.Lists.Skip(listPage * 20).Take(20))
        {
            Ui.TextWrapped($"{list.Pointer}: {list.Mode}");
            if (!Ui.BeginCombo("Key##" + list.Pointer, list.Key ?? "Position (no key)")) continue;
            string? chosen = null; var changed = false;
            if (Ui.Selectable("Position (no key)"u8, list.Key is null)) { chosen = null; changed = true; }
            foreach (var field in list.Fields)
                if (Ui.Selectable(field, list.Key == field)) { chosen = field; changed = true; }
            Ui.EndCombo();
            if (changed) { keys[list.Pointer] = chosen; Start(readDisk: false); return; }
        }
        if (listPage > 0 && Ui.Button("Previous lists"u8)) listPage--;
        if ((listPage + 1) * 20 < result.Lists.Count && Ui.Button("More lists"u8)) listPage++;
        ImGui.Separator();
        foreach (var change in result.Changes.Skip(page * 30).Take(30))
        {
            Ui.TextWrapped($"{change.Kind}: {Describe(loaded.Before!, change.BeforeAt)} -> {Describe(loaded.Current, change.AfterAt)}");
            Value("Before", change.Before); Value("After", change.After);
            ImGui.Separator();
        }
        if (page > 0 && Ui.Button("Previous differences"u8)) page--;
        if ((page + 1) * 30 < result.Changes.Count && Ui.Button("More differences"u8)) page++;
    }

    private string Describe(DataDocument doc, string? at)
    {
        if (at is null) return "(absent)";
        var item = at;
        while (item.Length > 0 && doc.At(item)?.ValueKind != JsonValueKind.Object)
            item = item[..item.LastIndexOf('/')];
        try
        {
            var label = editor.Outline.Label(doc, item) ?? item;
            var title = file.Schema is { } schema ? schemas.At(doc.Root, schema, at)
                .Where(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("title"u8, out var t) && t.ValueKind == JsonValueKind.String)
                .Select(s => s.GetProperty("title"u8).GetString()).FirstOrDefault() : null;
            return $"{label}: {title ?? at} ({at})";
        }
        catch (Exception e) { return $"{at} (label unavailable: {e.Message})"; }
    }

    private static void Value(string label, string? text)
    {
        Ui.TextWrapped(label);
        text ??= "(absent)";
        // Never truncate a large value silently or submit it as one giant native string.
        for (var i = 0; i < text.Length;)
        {
            var length = Math.Min(512, text.Length - i);
            if (i + length < text.Length && char.IsHighSurrogate(text[i + length - 1])) length--;
            Ui.TextWrapped(text.Substring(i, length)); i += length;
        }
    }
}
