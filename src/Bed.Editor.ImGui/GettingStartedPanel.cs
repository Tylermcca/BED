using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>The short, read-only guide. Hosts attach/show it once an editor exists.</summary>
public static class GettingStartedPanel
{
    /// <summary>Stable panel identity for future menu and launcher wiring.</summary>
    public const string Id = "bed.getting-started";
    /// <summary>The existing generic panel action, not a new command implementation.</summary>
    public const string ActionId = "editor.panel." + Id;
    /// <summary>The panel's visible title.</summary>
    public const string Title = "Getting started";
    /// <summary>The guide heading.</summary>
    public const string Heading = "Getting started with BED";
    /// <summary>The button that closes only this guide.</summary>
    public const string CloseLabel = "Start editing";
    /// <summary>The guide's single immutable text source, in reading order.</summary>
    public static IReadOnlyList<string> Steps { get; } = Array.AsReadOnly(new[]
    {
        "A project is a folder. BED shows its files on the left; click one to open it.",
        "Edit a row: pick a row in the table, change its values in Properties, and Ctrl+Z undoes.",
        "Save and check: Ctrl+S saves, and Tools > Run all checks finds problems before your game does.",
    });

    /// <summary>Registers one initially closed guide in an editor, without changing keys or menus.</summary>
    public static void Attach<T>(EditorModel<T> editor) where T : class
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!editor.Panels.Any(panel => panel.Id == Id))
            editor.Add(new Panel(Id, Title, () => Draw(editor),
                OpenAtStart: false, Width: 720, Height: 560));
    }

    /// <summary>Shows the guide idempotently, attaching it first if needed. Requires an existing editor.</summary>
    public static void Show<T>(EditorModel<T> editor) where T : class
    {
        Attach(editor);
        editor.SetOpen(Id, true);
    }

    private static void Draw<T>(EditorModel<T> editor) where T : class
    {
        Ui.TextWrapped(Heading);
        ImGui.Separator();
        for (var i = 0; i < Steps.Count; i++)
        {
            Ui.TextWrapped($"{i + 1}. {Steps[i]}");
            ImGui.Spacing();
        }
        if (Ui.Button(CloseLabel)) editor.SetOpen(Id, false);
    }
}
