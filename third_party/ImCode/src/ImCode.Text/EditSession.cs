namespace ImCode.Text;

/// <summary>
/// Document plus selection, undo history and the editing commands of a text editor.
/// Independent of any UI: the ImGui widget maps input to these commands.
/// </summary>
public sealed class EditSession
{
    readonly List<TextChange> _pending = [];
    int _editDepth;
    bool _editMergeable;
    Selection _editBefore;
    Selection _selection;
    int? _preferredCol;

    /// <summary>
    /// Several sessions (views) may edit the same document. They should then share one <paramref name="history"/>,
    /// each change is recorded once by the session that made it.
    /// </summary>
    public EditSession(TextDocument? document = null, UndoStack? history = null)
    {
        Document = document ?? new TextDocument();
        History = history ?? new UndoStack();
        History.Owner ??= this;
        Document.Changed += OnDocumentChanged;
    }

    public TextDocument Document { get; }
    public UndoStack History { get; }

    /// <summary>Stops tracking the document, call when a view on a shared document is closed.</summary>
    public void Detach()
    {
        Document.Changed -= OnDocumentChanged;
        if (History.Owner == this)
            History.Owner = null;
    }
    public IClipboard Clipboard { get; set; } = new MemoryClipboard();
    public bool ReadOnly { get; set; }
    public int IndentWidth { get; set; } = 4;
    public bool AutoIndent { get; set; } = true;
    public Func<char, bool> IsWordChar { get; set; } = CharClass.IsWordChar;

    /// <summary>Raised whenever the selection or caret changes.</summary>
    public event Action<EditSession>? SelectionChanged;

    /// <summary>Incremented whenever the caret is moved by a command, used to scroll it into view.</summary>
    public int CaretVersion { get; private set; }

    public Selection Selection
    {
        get => _selection;
        set
        {
            var sel = new Selection(Document.Clamp(value.Anchor), Document.Clamp(value.Caret));
            _preferredCol = null;
            CaretVersion++;
            if (sel == _selection)
                return;
            _selection = sel;
            SelectionChanged?.Invoke(this);
        }
    }

    public TextPosition Caret => _selection.Caret;
    public bool HasSelection => !_selection.IsEmpty;
    public string SelectedText => Document.GetText(_selection.Range);
    public string Text
    {
        get => Document.Text;
        set => Document.Text = value;
    }

    /// <summary>Replaces the whole text, resets caret and undo history.</summary>
    public void SetText(string text)
    {
        History.Clear();
        Document.Text = text;
        History.Clear();
        History.MarkSaved();
        Selection = Selection.Collapsed(TextPosition.Zero);
    }

    #region Edit transactions

    public readonly struct EditScope(EditSession session) : IDisposable
    {
        public void Dispose() => session.EndEdit();
    }

    /// <summary>
    /// Groups all document changes until the scope is disposed into one undo step.
    /// Mergeable steps (typing) are combined with the previous step if close in time.
    /// </summary>
    public EditScope BeginEdit(bool mergeable = false)
    {
        if (_editDepth++ == 0)
        {
            History.ActiveEditor = this;
            _editBefore = _selection;
            _editMergeable = mergeable;
            _pending.Clear();
        }
        return new EditScope(this);
    }

    void EndEdit()
    {
        if (--_editDepth > 0)
            return;
        if (History.ActiveEditor == this)
            History.ActiveEditor = null;
        History.Push(_pending, _editBefore, _selection, _editMergeable);
        _pending.Clear();
    }

    void OnDocumentChanged(TextDocument doc, TextChange change)
    {
        var sel = new Selection(change.Transform(_selection.Anchor), change.Transform(_selection.Caret));
        if (sel != _selection)
        {
            _selection = sel;
            SelectionChanged?.Invoke(this);
        }

        if (History.IsApplying)
            return;
        History.Owner ??= this;
        if (_editDepth > 0)
            _pending.Add(change);
        else if (History.ActiveEditor == null && History.Owner == this)
            History.Push([change], _selection, _selection, false);
    }

    #endregion

    #region Caret movement

    public void SetCaret(TextPosition pos, bool extendSelection = false) =>
        Selection = new Selection(extendSelection ? _selection.Anchor : Document.Clamp(pos), pos);

    public void MoveHorizontal(int amount, bool byWord, bool extendSelection)
    {
        if (!extendSelection && HasSelection && !byWord)
        {
            SetCaret(amount < 0 ? _selection.Start : _selection.End);
            return;
        }
        var pos = byWord
            ? Document.Move(Caret, new MoveAction(MoveToken.WordBeginning, amount > 0, Math.Abs(amount)), IsWordChar)
            : Document.MoveChars(Caret, amount);
        SetCaret(pos, extendSelection);
    }

    public void MoveVertical(int lines, bool extendSelection)
    {
        int col = _preferredCol ?? Caret.Col;
        var line = Math.Clamp(Caret.Line + lines, 0, Document.LineCount - 1);
        SetCaret(new TextPosition(line, col), extendSelection);
        _preferredCol = col;
    }

    /// <summary>Toggles between the first non whitespace character and column 0.</summary>
    public void MoveLineStart(bool extendSelection)
    {
        int indent = TextNavigation.LeadingWhitespace(Document[Caret.Line]);
        SetCaret(new TextPosition(Caret.Line, Caret.Col == indent ? 0 : indent), extendSelection);
    }

    public void MoveLineEnd(bool extendSelection) =>
        SetCaret(new TextPosition(Caret.Line, Document.LineLength(Caret.Line)), extendSelection);

    public void MoveDocumentStart(bool extendSelection) => SetCaret(TextPosition.Zero, extendSelection);
    public void MoveDocumentEnd(bool extendSelection) => SetCaret(Document.End, extendSelection);

    public void SelectAll() => Selection = new Selection(TextPosition.Zero, Document.End);

    public void SelectWordAt(TextPosition pos) => Selection = Selection.FromRange(Document.GetWordAt(pos, IsWordChar));

    #endregion

    #region Editing

    public bool Replace(TextRange range, string text, bool mergeable = false)
    {
        if (ReadOnly)
            return false;
        using var _ = BeginEdit(mergeable);
        var end = Document.Replace(range, text);
        Selection = Selection.Collapsed(end);
        return true;
    }

    /// <summary>Inserts text at the caret, replacing the selection.</summary>
    public bool InsertText(string text, bool mergeable = false)
    {
        if (ReadOnly || (text.Length == 0 && !HasSelection))
            return false;
        return Replace(_selection.Range, text, mergeable && !HasSelection);
    }

    /// <summary>Typed characters, merged into one undo step while typing continuously.</summary>
    public bool Type(string text) => InsertText(text, mergeable: true);

    public bool DeleteSelection() => HasSelection && Replace(_selection.Range, "");

    public bool Backspace(bool byWord = false)
    {
        if (ReadOnly)
            return false;
        if (HasSelection)
            return DeleteSelection();
        var start = byWord
            ? Document.FindWordBeginning(Caret, false, IsWordChar)
            : Document.MoveChars(Caret, -1);
        return start != Caret && Replace(new TextRange(start, Caret), "", mergeable: !byWord);
    }

    public bool Delete(bool byWord = false)
    {
        if (ReadOnly)
            return false;
        if (HasSelection)
            return DeleteSelection();
        var end = byWord
            ? Document.FindWordBeginning(Caret, true, IsWordChar)
            : Document.MoveChars(Caret, 1);
        return end != Caret && Replace(new TextRange(Caret, end), "", mergeable: !byWord);
    }

    public bool NewLine()
    {
        var indent = "";
        if (AutoIndent)
        {
            var line = Document[_selection.Start.Line];
            indent = line[..Math.Min(TextNavigation.LeadingWhitespace(line), _selection.Start.Col)];
        }
        return InsertText("\n" + indent);
    }

    /// <summary>Inserts spaces up to the next indent stop.</summary>
    public bool InsertIndent()
    {
        int n = IndentWidth - Caret.Col % IndentWidth;
        return InsertText(new string(' ', n), mergeable: true);
    }

    /// <summary>Indents (levels > 0) or outdents the lines covered by the selection, or the caret line.</summary>
    public bool IndentLines(int levels)
    {
        if (ReadOnly || levels == 0)
            return false;
        var range = _selection.Range;
        int first = range.Start.Line;
        int last = range.End.Line;
        if (last > first && range.End.Col == 0)
            last--;

        var before = _selection;
        int anchorShift = 0, caretShift = 0;
        using (BeginEdit())
        {
            for (int l = first; l <= last; l++)
            {
                var line = Document[l];
                int shift;
                if (levels > 0)
                {
                    shift = levels * IndentWidth;
                    if (line.Length > 0)
                        Document.Insert(new TextPosition(l, 0), new string(' ', shift));
                    else
                        shift = 0;
                }
                else
                {
                    int remove = Math.Min(-levels * IndentWidth, TextNavigation.LeadingWhitespace(line));
                    Document.Delete(new TextRange(new TextPosition(l, 0), new TextPosition(l, remove)));
                    shift = -remove;
                }
                if (l == before.Anchor.Line)
                    anchorShift = shift;
                if (l == before.Caret.Line)
                    caretShift = shift;
            }

            static TextPosition Shift(TextPosition p, int s) => p.Col == 0 ? p : p with { Col = Math.Max(0, p.Col + s) };
            Selection = new Selection(Shift(before.Anchor, anchorShift), Shift(before.Caret, caretShift));
        }
        return true;
    }

    /// <summary>Comments out the selected lines with prefix, or removes it if all of them are commented.</summary>
    public bool ToggleLineComment(string prefix)
    {
        if (ReadOnly || string.IsNullOrEmpty(prefix))
            return false;
        var range = _selection.Range;
        int first = range.Start.Line;
        int last = range.End.Line;
        if (last > first && range.End.Col == 0)
            last--;

        bool allCommented = true;
        int indent = int.MaxValue;
        for (int l = first; l <= last; l++)
        {
            var line = Document[l];
            int ws = TextNavigation.LeadingWhitespace(line);
            if (ws == line.Length)
                continue;
            indent = Math.Min(indent, ws);
            if (string.CompareOrdinal(line, ws, prefix, 0, prefix.Length) != 0)
                allCommented = false;
        }
        if (indent == int.MaxValue)
            return false;

        var before = _selection;
        using (BeginEdit())
        {
            for (int l = first; l <= last; l++)
            {
                var line = Document[l];
                int ws = TextNavigation.LeadingWhitespace(line);
                if (ws == line.Length)
                    continue;
                if (allCommented)
                {
                    int len = prefix.Length + (ws + prefix.Length < line.Length && line[ws + prefix.Length] == ' ' ? 1 : 0);
                    Document.Delete(new TextRange(new TextPosition(l, ws), new TextPosition(l, ws + len)));
                }
                else
                    Document.Insert(new TextPosition(l, indent), prefix + " ");
            }
            var sel = _selection;
            Selection = new Selection(
                before.Anchor.Col == 0 ? before.Anchor : sel.Anchor,
                before.Caret.Col == 0 ? before.Caret : sel.Caret);
        }
        return true;
    }

    #endregion

    #region Clipboard and history

    public void Copy()
    {
        if (HasSelection)
            Clipboard.SetText(SelectedText);
    }

    public void Cut()
    {
        if (ReadOnly || !HasSelection)
            return;
        Copy();
        DeleteSelection();
    }

    public void Paste()
    {
        if (!ReadOnly)
            InsertText(Clipboard.GetText());
    }

    public bool Undo()
    {
        if (ReadOnly || !History.Undo(Document, out var sel))
            return false;
        Selection = sel;
        return true;
    }

    public bool Redo()
    {
        if (ReadOnly || !History.Redo(Document, out var sel))
            return false;
        Selection = sel;
        return true;
    }

    #endregion

    #region Search

    /// <summary>Selects the next/previous match relative to the selection. Returns false if there is none.</summary>
    public bool Find(string term, bool forward = true, SearchOptions options = default)
    {
        var from = forward && HasSelection ? Document.MoveChars(_selection.Start, 1) : forward ? Caret : _selection.Start;
        var match = Document.Find(term, from, forward, options, IsWordChar);
        if (match == null)
            return false;
        Selection = new Selection(match.Value.Start, match.Value.End);
        return true;
    }

    /// <summary>Replaces the selection if it matches term, then selects the next match.</summary>
    public bool ReplaceNext(string term, string replacement, SearchOptions options = default)
    {
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (HasSelection && string.Equals(SelectedText, term, comparison))
            Replace(_selection.Range, replacement);
        return Find(term, true, options);
    }

    public int ReplaceAll(string term, string replacement, SearchOptions options = default)
    {
        if (ReadOnly)
            return 0;
        var matches = Document.FindAll(term, options, IsWordChar).ToList();
        if (matches.Count == 0)
            return 0;
        using (BeginEdit())
        {
            for (int i = matches.Count - 1; i >= 0; i--)
                Document.Replace(matches[i], replacement);
        }
        return matches.Count;
    }

    #endregion
}
