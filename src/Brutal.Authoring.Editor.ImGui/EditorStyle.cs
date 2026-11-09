namespace Brutal.Authoring.Editor.ImGui;

/// <summary>The editor's light or dark theme. Both the window host and headless host use this entry point.</summary>
public static class EditorStyle
{
    /// <summary>Applies the chosen app theme without changing the current text size.</summary>
    public static void Apply() => EditorTheme.ApplyCurrent();
}
