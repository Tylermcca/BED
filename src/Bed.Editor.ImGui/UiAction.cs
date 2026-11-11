using Bed.Editor;

namespace Bed.Editor.ImGui;

/// <summary>The existing surface asking how to present an editor action. Keys use the palette's labels.</summary>
public enum UiActionContext
{
    /// <summary>The existing toolbar.</summary>
    Toolbar,
    /// <summary>The command palette and its Keys-panel projection.</summary>
    Palette,
}

/// <summary>One surface's current label, visibility and availability. A toolbar can be disabled without displaying a reason.</summary>
/// <param name="Context">The surface queried.</param>
/// <param name="Label">The existing label for that surface.</param>
/// <param name="Visible">Whether that surface normally lists the action.</param>
/// <param name="Enabled">Whether that surface offers the action now.</param>
/// <param name="Reason">The existing displayed refusal, or null when the surface displays none.</param>
public readonly record struct UiActionPresentation(UiActionContext Context, string Label, bool Visible, bool Enabled, string? Reason);

/// <summary>A local UI definition over an existing handler. Presentation never gates invocation or replaces its refusals.</summary>
/// <param name="id">The unchanged action id.</param>
/// <param name="presentation">Reads current per-surface metadata without executing the handler.</param>
/// <param name="shortcut">Reads the current binding, including overrides and removal.</param>
/// <param name="invoke">Delegates to the existing handler, which owns validation, status and refusal.</param>
public sealed class UiAction(string id, Func<UiActionContext, UiActionPresentation> presentation, Func<Chord?> shortcut, Func<bool> invoke)
{
    /// <summary>The existing stable UI id; unrelated CLI/MCP names are not changed.</summary>
    public string Id { get; } = id;
    /// <summary>The current binding, read live rather than captured when the definition was made.</summary>
    public Chord? Shortcut => shortcut();
    /// <summary>Reads this surface's current presentation. It does not invoke or preflight the handler.</summary>
    public UiActionPresentation Present(UiActionContext context) => presentation(context);
    /// <summary>Calls the existing handler exactly once, even when a surface currently displays the action disabled.</summary>
    public bool Invoke() => invoke();
}
