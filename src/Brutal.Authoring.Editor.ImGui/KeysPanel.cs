using System.Runtime.CompilerServices;
using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Read-only live shortcuts and explicitly fixed controls. Existing panel navigation only; no AT bridge.</summary>
public static class KeysPanel
{
    /// <summary>The Keys panel's id.</summary>
    public const string Id = "bed.keys";
    /// <summary>The command that opens the Keys panel.</summary>
    public const string ActionId = "editor.panel." + Id;
    private sealed class Binding
    {
        public required Shortcuts Shortcuts;
        public required Func<IReadOnlyList<(string Id, string Label, string? Shortcut, string? Unavailable)>> Entries;
    }
    private static readonly ConditionalWeakTable<object, Binding> bindings = new();

    /// <summary>Refreshes an editor's live sources without registering duplicate panels.</summary>
    public static void Attach<T>(EditorModel<T> editor, Shortcuts shortcuts,
        Func<IReadOnlyList<(string Id, string Label, string? Shortcut, string? Unavailable)>> entries) where T : class
    {
        var binding = bindings.GetValue(editor, _ => new Binding { Shortcuts = shortcuts, Entries = entries });
        binding.Shortcuts = shortcuts; binding.Entries = entries;
        if (!editor.Panels.Any(p => p.Id == Id))
            editor.Add(new Panel(Id, "Keys", () => Draw(binding), OpenAtStart: false, Width: 560, Height: 420));
    }

    /// <summary>Every live binding, with palette label when present and the action id otherwise.</summary>
    public static IReadOnlyList<(string Id, string Label, string Chord)> Rows(Shortcuts shortcuts,
        IReadOnlyList<(string Id, string Label, string? Shortcut, string? Unavailable)> entries) =>
        shortcuts.InEffect().OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            (pair.Key, entries.FirstOrDefault(e => e.Id == pair.Key).Label ?? pair.Key, pair.Value.ToString())).ToArray();

    /// <summary>Keys handled directly by tables and canvases, listed for reference; they can't be rebound.</summary>
    public static IReadOnlyList<string> FixedKeys { get; } = Array.AsReadOnly(new[]
    {
        "Table: Ctrl+C copies selected cells (not rebindable).",
        "Table: Up/Down/Left/Right moves the cell selection; Shift extends it (not rebindable).",
        "Canvas: Escape cancels a move (not rebindable).",
        "Canvas: 0 or keypad 0 resets zoom and pan (not rebindable).",
    });

    private static void Draw(Binding binding)
    {
        Ui.TextWrapped("Your shortcuts"u8);
        Ui.TextWrapped("Anywhere in the editor"u8);
        foreach (var row in Rows(binding.Shortcuts, binding.Entries()))
            Ui.Selectable($"{row.Chord}  {row.Label}##key-{row.Id}", false);
        ImGui.Separator();
        Ui.TextWrapped("Fixed keys"u8);
        foreach (var text in FixedKeys) Ui.Selectable(text, false);
    }
}
