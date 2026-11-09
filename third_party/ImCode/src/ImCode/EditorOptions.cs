using Brutal.ImGuiApi;

namespace ImCode;

public sealed class EditorOptions
{
    /// <summary>Monospace font. Null uses the current ImGui font.</summary>
    public ImFontPtr? Font { get; set; }

    /// <summary>Unscaled font size. 0 uses the current font size.</summary>
    public float FontSize { get; set; }

    public float Zoom { get; set; } = 1f;
    public float MinZoom { get; set; } = 0.5f;
    public float MaxZoom { get; set; } = 4f;
    public bool MouseWheelZoom { get; set; } = true;

    /// <summary>Extra vertical space between lines in pixels (unscaled).</summary>
    public float LineSpacing { get; set; } = 2f;

    /// <summary>Vim key bindings, see <see cref="ImCodeEditor.Vim"/>.</summary>
    public bool VimMode { get; set; }

    public int TabSize { get; set; } = 4;
    public bool ShowLineNumbers { get; set; } = true;
    public bool HighlightCurrentLine { get; set; } = true;

    /// <summary>Caret blink period in seconds, 0 disables blinking.</summary>
    public float CaretBlinkPeriod { get; set; } = 1f;

    /// <summary>Open the completion popup automatically while typing a word.</summary>
    public bool AutoCompletion { get; set; } = true;
    public int MaxCompletionItems { get; set; } = 10;

    /// <summary>Seconds the mouse has to rest before the hover tooltip shows.</summary>
    public float HoverDelay { get; set; } = 0.5f;

    /// <summary>Seconds after the last change before the language diagnostics are recomputed.</summary>
    public float DiagnosticsDelay { get; set; } = 0.3f;
}
