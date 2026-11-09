namespace ImCode.Input;

/// <summary>Ids of the built-in commands of <see cref="ImCodeEditor"/>.</summary>
public static class EditorCommands
{
    /// <summary>Appended to caret commands to extend the selection.</summary>
    public const string SelectSuffix = ".select";

    public const string CaretLeft = "caret.left";
    public const string CaretRight = "caret.right";
    public const string CaretWordLeft = "caret.wordLeft";
    public const string CaretWordRight = "caret.wordRight";
    public const string CaretUp = "caret.up";
    public const string CaretDown = "caret.down";
    public const string CaretPageUp = "caret.pageUp";
    public const string CaretPageDown = "caret.pageDown";
    public const string CaretLineStart = "caret.lineStart";
    public const string CaretLineEnd = "caret.lineEnd";
    public const string CaretDocumentStart = "caret.documentStart";
    public const string CaretDocumentEnd = "caret.documentEnd";

    public const string ScrollUp = "view.scrollUp";
    public const string ScrollDown = "view.scrollDown";
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string ZoomReset = "view.zoomReset";

    public const string Backspace = "edit.backspace";
    public const string BackspaceWord = "edit.backspaceWord";
    public const string Delete = "edit.delete";
    public const string DeleteWord = "edit.deleteWord";
    public const string NewLine = "edit.newLine";
    public const string Indent = "edit.indent";
    public const string Outdent = "edit.outdent";
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string Paste = "edit.paste";
    public const string SelectAll = "edit.selectAll";
    public const string ToggleComment = "edit.toggleComment";
    public const string TriggerCompletion = "edit.triggerCompletion";

    static readonly Dictionary<string, string> Titles = new()
    {
        [CaretLeft] = "Move left",
        [CaretRight] = "Move right",
        [CaretWordLeft] = "Move word left",
        [CaretWordRight] = "Move word right",
        [CaretUp] = "Move up",
        [CaretDown] = "Move down",
        [CaretPageUp] = "Page up",
        [CaretPageDown] = "Page down",
        [CaretLineStart] = "Line start",
        [CaretLineEnd] = "Line end",
        [CaretDocumentStart] = "Document start",
        [CaretDocumentEnd] = "Document end",
        [ScrollUp] = "Scroll up",
        [ScrollDown] = "Scroll down",
        [ZoomIn] = "Zoom in",
        [ZoomOut] = "Zoom out",
        [ZoomReset] = "Reset zoom",
        [Backspace] = "Delete left",
        [BackspaceWord] = "Delete word left",
        [Delete] = "Delete right",
        [DeleteWord] = "Delete word right",
        [NewLine] = "New line",
        [Indent] = "Indent",
        [Outdent] = "Outdent",
        [Undo] = "Undo",
        [Redo] = "Redo",
        [Cut] = "Cut",
        [Copy] = "Copy",
        [Paste] = "Paste",
        [SelectAll] = "Select all",
        [ToggleComment] = "Toggle line comment",
        [TriggerCompletion] = "Show completions",
    };

    /// <summary>Human readable title of a command id, "(select)" appended for selection variants.</summary>
    public static string Title(string id)
    {
        if (id.EndsWith(SelectSuffix) && Titles.TryGetValue(id[..^SelectSuffix.Length], out var baseTitle))
            return baseTitle + " (select)";
        return Titles.TryGetValue(id, out var title) ? title : id;
    }
}
