using Bed.Editor.ImGui;
using System.Text;
using System.Text.Json;
using Bed.Data;
using Brutal.ImGuiApi;
using ImCode;
using ImCode.Input;
using ImCode.Languages;
using ImCode.Text;

namespace Bed.Editor.Text;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// The Text panel: a data file's JSON as text, beside the outline and inspector, for the edits a form makes slow (moving a
/// block, pasting a list). It shows the document as it is now, unsaved edits included, as the file would be written,
/// highlighted (<see cref="JsonLanguage"/>), in ImCode's editor widget.
/// <list type="bullet">
///   <item>Typing makes a draft, kept in the panel: nothing changes the document until <see cref="Apply"/> (the Apply button
///   or Ctrl+Enter).</item>
///   <item>Apply parses the text. Text that isn't JSON is refused, saying the line and column, with a squiggle there. JSON is
///   applied as one command (<see cref="SetValue"/> on the whole file), through the editor like any other edit: the schema
///   judges it, a refusal leaves the document as it was and says why, and one Undo steps back. A read-only file shows its
///   text but can't apply it.</item>
///   <item>When the document changes elsewhere (the inspector, an undo), the text follows it if there's no draft. With a
///   draft, the panel says the document changed and offers Revert; it never overwrites the draft by itself.</item>
/// </list>
/// The text is written out once per document, not per frame: documents are immutable, so the same document has the same
/// text (nothing is redone on a frame where nothing changed). The text lives in ImCode's UI-free <see cref="EditSession"/>, so a script or test can read,
/// edit and apply it with no ImGui frame; the widget is made at the first draw. MIXED: Unity's and Godot's "edit as text"
/// views, BORROWED; the draft-then-apply step and the one-command apply, INVENTED, over goal 4's single edit path.
/// </summary>
public sealed class JsonTextPane
{
    private readonly EditorModel<DataDocument> editor;
    private readonly Func<string?>? readOnly;
    private readonly EditSession text = new() { IndentWidth = 2 };
    private ImCodeEditor? code;
    private DataDocument? shownFor;
    private string shownText = "";
    private int shownVersion;
    private bool applyRequested;
    private bool squiggleShown = true;

    /// <summary>The panel's id, as <see cref="AsPanel"/> registers it.</summary>
    public const string PanelId = "editor.text";

    /// <param name="editor">The file's editor: the pane reads its document and edits only through it.</param>
    /// <param name="readOnly">Why the file can't be changed here, or null when it can: a read-only file shows its text but
    /// can't apply it.</param>
    public JsonTextPane(EditorModel<DataDocument> editor, Func<string?>? readOnly = null)
    {
        this.editor = editor;
        this.readOnly = readOnly;
    }

    private const string ApplyCommand = "brutal.text.apply";

    /// <summary>The panel, closed at start, as a tab in the centre, with Apply and Revert in the command palette.</summary>
    public Panel AsPanel() => new(PanelId, "Text", Draw, OpenAtStart: false, Width: 560, Height: 480, Side: PanelSide.Centre,
        Commands:
        [
            new("apply", "Apply the Text panel's JSON", Apply, () => readOnly?.Invoke() is { } why ? why : !HasDraft ? "the text has no changes" : null),
            new("revert", "Revert the Text panel to the document", () => { Revert(); return true; }, () => HasDraft ? null : "the text has no changes"),
        ]);

    /// <summary>The text in the panel: the document's JSON, or the person's draft.</summary>
    public string Text { get { Sync(); return text.Text; } }

    /// <summary>True when the text differs from the document it was shown for: the person's unapplied draft.</summary>
    public bool HasDraft => text.Document.Version != shownVersion && text.Text != shownText;

    /// <summary>True when there's a draft and the document changed since it began (an edit elsewhere, an undo). The draft
    /// stays; <see cref="Revert"/> shows the document as it is now.</summary>
    public bool Stale { get { Sync(); return HasDraft && !ReferenceEquals(shownFor, editor.Session.Current); } }

    /// <summary>True when the file can't be changed here, so the text can be read but not edited or applied.</summary>
    public bool ReadOnly { get { Sync(); return text.ReadOnly; } }

    /// <summary>What the last Apply or Revert did, or why it didn't.</summary>
    public string Said { get; private set; } = "";

    /// <summary>Where the last refused text stopped being JSON, 1-based, or null.</summary>
    public (int Line, int Column)? ErrorAt { get; private set; }

    /// <summary>The squiggle under the last refused text, as the widget shows it, or null.</summary>
    public Diagnostic? Squiggle { get; private set; }

    /// <summary>How many times the panel has written a document out as text: once per document shown, never per frame.</summary>
    public int Rendered { get; private set; }

    /// <summary>The code editor widget, once the panel has been drawn, for a host to set its font or theme.</summary>
    public ImCodeEditor? Code => code;

    /// <summary>Replaces the text, as typing or pasting would: a draft until applied. False for a read-only file.</summary>
    public bool Edit(string replacement)
    {
        Sync();
        return text.Replace(text.Document.FullRange, replacement);
    }

    /// <summary>Throws the draft away and shows the document as it is now.</summary>
    public void Revert()
    {
        Load(editor.Session.Current);
        Said = "Reverted: showing the document as it is now.";
    }

    /// <summary>Applies the text to the document as one undoable command. Returns false, leaving the document as it was and
    /// saying why in <see cref="Said"/>, when the file is read-only, the document changed under the draft, the text isn't
    /// JSON (with <see cref="ErrorAt"/>), or the editor or schema refuses it.</summary>
    public bool Apply()
    {
        Sync();
        Mark(null, null);
        if (readOnly?.Invoke() is { } why) return Refuse($"Not applied: this file is read-only ({why}).");
        if (!HasDraft) { Said = "Nothing to apply: the text is the document as it is."; return true; }
        if (Stale) return Refuse("Not applied: the document changed since this draft began (an edit elsewhere, or an undo). Revert to see it as it is now; copy your draft first to keep it.");
        JsonElement value;
        try
        {
            using var parsed = JsonDocument.Parse(text.Text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            value = parsed.RootElement.Clone();
        }
        catch (JsonException e)
        {
            var line = (int)Math.Clamp(e.LineNumber ?? 0, 0, text.Document.LineCount - 1);
            var bytes = Encoding.UTF8.GetBytes(text.Document[line]);
            var column = Encoding.UTF8.GetCharCount(bytes, 0, (int)Math.Clamp(e.BytePositionInLine ?? 0, 0, bytes.Length));
            var reason = e.Message.Split(" LineNumber:")[0].Split(" Path:")[0].TrimEnd('.', ' ');
            var length = text.Document.LineLength(line);
            Mark((line + 1, column + 1), new Diagnostic(new TextRange(new TextPosition(line, Math.Min(column, length)), new TextPosition(line, length)), DiagnosticSeverity.Error, reason));
            return Refuse($"Not applied: the text isn't valid JSON at line {line + 1}, column {column + 1}: {reason}. Nothing changed; fix it and apply again, or Revert.");
        }
        // Valid JSON can still hold a string that isn't text (a lone surrogate escape): the data format names its place, so the
        // refusal points at the value, not at the edit's own payload (found by A202). Only text with a \u escape can.
        if (text.Text.Contains("\\u", StringComparison.Ordinal))
        {
            try { new DataFormat().Read(text.Text); }
            catch (DocumentFormatException e)
            {
                var reason = e.Message.Split(": ", 2)[^1];
                return Refuse($"Not applied: {e.Path} {reason}. Nothing changed; fix it and apply again, or Revert.");
            }
        }
        var current = editor.Session.Current;
        if (JsonSchemas.Same(value, current.Root)) { Load(current); Said = "Nothing to apply: the text means the same as the document."; return true; }
        // Every object in the file is touched, so a focus on one place refuses a change to the whole file.
        if (!editor.Execute(new SetValue("", value), [.. editor.Outline.ObjectIds(current)]))
        {
            var issues = editor.Refused.Count > 0 ? editor.Refused : editor.Session.LastIssues;
            var errors = issues.Where(i => i.Severity == Severity.Error).ToList();
            var shown = errors.Take(3).Select(i => (i.Path is { } p ? p + ": " : "") + i.Message).ToList();
            return Refuse("Not applied: " + (shown.Count == 0 ? "the edit was refused." : string.Join("; ", shown) + (errors.Count > 3 ? $"; and {errors.Count - 3} more" : "") + ".")
                + " The document is unchanged; fix the text and apply again, or Revert.");
        }
        Load(editor.Session.Current);
        Said = "Applied as one step; Undo steps back.";
        return true;
    }

    private bool Refuse(string why)
    {
        Said = why;
        return false;
    }

    private void Mark((int Line, int Column)? at, Diagnostic? squiggle)
    {
        ErrorAt = at;
        Squiggle = squiggle;
        squiggleShown = false;
    }

    // Shows the document's text when it's a new document and there's no draft to keep. Asked every frame: when nothing
    // changed, it's one reference comparison.
    private void Sync()
    {
        text.ReadOnly = readOnly?.Invoke() is not null;
        var current = editor.Session.Current;
        if (ReferenceEquals(current, shownFor) || (shownFor is not null && HasDraft)) return;
        Load(current);
    }

    private void Load(DataDocument document)
    {
        var caret = text.Caret;
        var readOnlyNow = text.ReadOnly;
        text.ReadOnly = false;   // the shown document is always written in; only the person's typing is locked
        shownText = TextDocument.NormalizeLineEndings(editor.Session.Format.Write(document));
        Rendered++;
        shownFor = document;
        text.SetText(shownText);
        text.ReadOnly = readOnlyNow;
        shownVersion = text.Document.Version;
        text.Selection = Selection.Collapsed(caret);
        Mark(null, null);
    }

    // The widget, made at the first draw: it asks ImGui for the time whenever its text changes, so it can't exist
    // before there's an ImGui frame.
    private ImCodeEditor Widget()
    {
        if (code is not null) return code;
        code = new ImCodeEditor(text) { Language = JsonLanguage.Instance };
        code.Options.AutoCompletion = false;
        code.Options.TabSize = 2;
        code.RegisterCommand(ApplyCommand, _ => applyRequested = true);
        code.KeyMap.Bind(new KeyChord(ImGuiKey.Enter, Ctrl: true), ApplyCommand);
        code.KeyMap.Bind(new KeyChord(ImGuiKey.KeypadEnter, Ctrl: true), ApplyCommand);
        return code;
    }

    /// <summary>Draws the panel: Apply and Revert, what happened, and the text.</summary>
    public void Draw()
    {
        Sync();
        var widget = Widget();
        if (!squiggleShown)
        {
            widget.Diagnostics.Clear();
            if (Squiggle is { } s) widget.Diagnostics.Add(s);
            squiggleShown = true;
        }
        var locked = readOnly?.Invoke();
        Ui.BeginDisabled(locked is not null || !HasDraft, locked ?? "the text has no changes");
        if (Ui.SmallButton("Apply"u8)) Apply();
        Ui.EndDisabled();
        ImGui.SetItemTooltip("Apply the text to the document as one step (Ctrl+Enter)."u8);
        ImGui.SameLine();
        Ui.BeginDisabled(!HasDraft, "the text has no changes");
        if (Ui.SmallButton("Revert"u8)) Revert();
        Ui.EndDisabled();
        ImGui.SameLine();
        Ui.TextWrapped(locked is not null ? $"Read-only: {locked}." : HasDraft ? "Draft: not applied yet." : "The document as it is now.");
        if (Stale) Ui.TextWrapped("The document changed since this draft began (an edit elsewhere, or an undo). Revert to see it as it is now."u8);
        if (Said.Length > 0) Ui.TextWrapped(Said);
        // Reached by keyboard (the palette, or Ctrl+Tab), the panel has no other focusable item until there's a draft, and the code
        // editor isn't an ImGui nav item: so when this window has focus and nothing in it does, the text takes it (A210).
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !widget.HasFocus && !ImGui.IsAnyItemActive()
            && !ImGui.IsAnyItemFocused())
            widget.Focus();
        widget.Draw("##json"u8);
        if (applyRequested)
        {
            applyRequested = false;
            Apply();
        }
    }
}
