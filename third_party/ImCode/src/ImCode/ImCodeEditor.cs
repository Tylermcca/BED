using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using ImCode.Input;
using ImCode.Languages;
using ImCode.Rendering;
using ImCode.Text;
using ImCode.Vim;

namespace ImCode;

/// <summary>
/// Code editing text area without any surrounding UI. Call <see cref="Draw"/> once per frame
/// inside an ImGui window. Text, selection and undo live in <see cref="Session"/>.
/// </summary>
public sealed partial class ImCodeEditor : IDisposable
{
    enum DragMode
    {
        None,
        Char,
        Word,
        Line,
    }

    readonly Dictionary<string, Action<ImCodeEditor>> _commands = [];
    readonly LineLayoutCache _layout;
    readonly SyntaxHighlighter _highlighter;
    readonly StringBuilder _typed = new();

    int _lastCaretVersion = -1;
    int _lastDocumentVersion = -1;
    double _caretTime;
    bool _scrollToCaret;
    bool _focusRequested;
    int _scrollLines;

    DragMode _dragMode;
    TextRange _dragAnchor;

    float2 _textOrigin;
    float _textTop;
    float _gutterWidth;
    float _textPadding;

    public ImCodeEditor(EditSession? session = null)
    {
        Session = session ?? new EditSession();
        Session.Clipboard = new ImGuiClipboard();
        _layout = new LineLayoutCache(Session.Document, Options.TabSize);
        _highlighter = new SyntaxHighlighter(Session.Document);
        Session.Document.Changed += OnDocumentChanged;
        Vim = new VimEngine(Session);
        Vim.WordActivated += pos => WordActivated?.Invoke(this, pos);
        RegisterBuiltinCommands();
    }

    public ImCodeEditor(string text) : this()
    {
        Session.SetText(text);
    }

    public EditSession Session { get; }
    public TextDocument Document => Session.Document;
    public EditorOptions Options { get; set; } = new();
    public ColorTheme Theme { get; set; } = ColorTheme.Dark();
    public KeyMap KeyMap { get; set; } = KeyMap.CreateDefault();

    public string Text
    {
        get => Session.Text;
        set => Session.SetText(value);
    }

    /// <summary>Highlighting, hover, completion and diagnostics provider.</summary>
    public Language Language
    {
        get => _highlighter.Language;
        set
        {
            _highlighter.Language = value;
            Session.IsWordChar = value.IsWordChar;
            Diagnostics.Clear();
            _diagnosticsVersion = -1;
            CloseCompletion();
        }
    }

    public SyntaxHighlighter Highlighter => _highlighter;

    /// <summary>Vim emulation, active if <see cref="EditorOptions.VimMode"/> is set.</summary>
    public VimEngine Vim { get; }

    /// <summary>Vim mode and pending command for a status bar, null if vim mode is off.</summary>
    public string? VimStatus => Options.VimMode ? Vim.Status : null;

    /// <summary>
    /// Diagnostics shown as underlines and in the hover tooltip. Ranges follow edits.
    /// Replaced automatically if the language provides diagnostics.
    /// </summary>
    public List<Diagnostic> Diagnostics { get; } = [];

    public bool ReadOnly
    {
        get => Session.ReadOnly;
        set => Session.ReadOnly = value;
    }

    public bool HasFocus { get; private set; }
    public float CharWidth { get; private set; }
    public float LineHeight { get; private set; }
    public int FirstVisibleLine { get; private set; }
    public int VisibleLineCount { get; private set; } = 1;

    /// <summary>Single line ranges drawn with <see cref="ColorTheme.SearchMatch"/>, sorted by position (e.g. search results).</summary>
    public IReadOnlyList<TextRange> Highlights { get; set; } = [];

    /// <summary>Stops listening to the document. The session is not detached, see <see cref="EditSession.Detach"/>.</summary>
    public void Dispose()
    {
        _layout.Detach();
        _highlighter.Detach();
        Document.Changed -= OnDocumentChanged;
    }

    /// <summary>Raised on Ctrl+Click with the clicked text position (go to definition, open documentation, ...).</summary>
    public event Action<ImCodeEditor, TextPosition>? WordActivated;

    #region Commands

    public IEnumerable<string> CommandIds => _commands.Keys;

    /// <summary>Adds or replaces a command that can be bound in <see cref="KeyMap"/>.</summary>
    public void RegisterCommand(string id, Action<ImCodeEditor> action) => _commands[id] = action;

    public bool ExecuteCommand(string id)
    {
        if (!_commands.TryGetValue(id, out var action))
            return false;
        action(this);
        return true;
    }

    void RegisterBuiltinCommands()
    {
        void Caret(string id, Action<ImCodeEditor, bool> move)
        {
            _commands[id] = w => move(w, false);
            _commands[id + EditorCommands.SelectSuffix] = w => move(w, true);
        }

        Caret(EditorCommands.CaretLeft, (w, sel) => w.Session.MoveHorizontal(-1, false, sel));
        Caret(EditorCommands.CaretRight, (w, sel) => w.Session.MoveHorizontal(1, false, sel));
        Caret(EditorCommands.CaretWordLeft, (w, sel) => w.Session.MoveHorizontal(-1, true, sel));
        Caret(EditorCommands.CaretWordRight, (w, sel) => w.Session.MoveHorizontal(1, true, sel));
        Caret(EditorCommands.CaretUp, (w, sel) => w.Session.MoveVertical(-1, sel));
        Caret(EditorCommands.CaretDown, (w, sel) => w.Session.MoveVertical(1, sel));
        Caret(EditorCommands.CaretPageUp, (w, sel) => w.Session.MoveVertical(-w.PageLines, sel));
        Caret(EditorCommands.CaretPageDown, (w, sel) => w.Session.MoveVertical(w.PageLines, sel));
        Caret(EditorCommands.CaretLineStart, (w, sel) => w.Session.MoveLineStart(sel));
        Caret(EditorCommands.CaretLineEnd, (w, sel) => w.Session.MoveLineEnd(sel));
        Caret(EditorCommands.CaretDocumentStart, (w, sel) => w.Session.MoveDocumentStart(sel));
        Caret(EditorCommands.CaretDocumentEnd, (w, sel) => w.Session.MoveDocumentEnd(sel));

        _commands[EditorCommands.ScrollUp] = w => w._scrollLines--;
        _commands[EditorCommands.ScrollDown] = w => w._scrollLines++;
        _commands[EditorCommands.ZoomIn] = w => w.SetZoom(w.Options.Zoom * 1.1f);
        _commands[EditorCommands.ZoomOut] = w => w.SetZoom(w.Options.Zoom / 1.1f);
        _commands[EditorCommands.ZoomReset] = w => w.SetZoom(1f);

        _commands[EditorCommands.Backspace] = w => w.Session.Backspace();
        _commands[EditorCommands.BackspaceWord] = w => w.Session.Backspace(byWord: true);
        _commands[EditorCommands.Delete] = w => w.Session.Delete();
        _commands[EditorCommands.DeleteWord] = w => w.Session.Delete(byWord: true);
        _commands[EditorCommands.NewLine] = w => w.Session.NewLine();
        _commands[EditorCommands.Indent] = w =>
        {
            if (w.Session.Selection.Range.IsMultiLine)
                w.Session.IndentLines(1);
            else
                w.Session.InsertIndent();
        };
        _commands[EditorCommands.Outdent] = w => w.Session.IndentLines(-1);
        _commands[EditorCommands.Undo] = w => w.Session.Undo();
        _commands[EditorCommands.Redo] = w => w.Session.Redo();
        _commands[EditorCommands.Cut] = w => w.Session.Cut();
        _commands[EditorCommands.Copy] = w => w.Session.Copy();
        _commands[EditorCommands.Paste] = w => w.Session.Paste();
        _commands[EditorCommands.SelectAll] = w => w.Session.SelectAll();
        _commands[EditorCommands.ToggleComment] = w =>
        {
            if (w.Language.LineCommentToken is { } prefix)
                w.Session.ToggleLineComment(prefix);
        };
        _commands[EditorCommands.TriggerCompletion] = w => w.UpdateCompletion(open: true);
    }

    int PageLines => Math.Max(1, VisibleLineCount - 1);

    void SetZoom(float zoom) => Options.Zoom = Math.Clamp(zoom, Options.MinZoom, Options.MaxZoom);

    #endregion

    /// <summary>Gives keyboard focus to the editor in the next <see cref="Draw"/> call.</summary>
    public void Focus() => _focusRequested = true;

    public void ScrollToCaret() => _scrollToCaret = true;

    /// <summary>Screen position of the top left corner of a text position, valid after the last Draw.</summary>
    public float2 ScreenPosition(TextPosition pos)
    {
        pos = Document.Clamp(pos);
        return new float2(_textOrigin.X + _layout[pos.Line].VisualColumn(pos.Col) * CharWidth, _textTop + pos.Line * LineHeight);
    }

    /// <summary>Text position closest to a screen position, valid after the last Draw.</summary>
    public TextPosition TextPositionAt(float2 screen)
    {
        int line = (int)MathF.Floor((screen.Y - _textTop) / LineHeight);
        if (line < 0)
            return TextPosition.Zero;
        if (line >= Document.LineCount)
            return Document.End;
        var col = _layout[line].ColumnFromVisual((screen.X - _textOrigin.X) / CharWidth);
        return new TextPosition(line, col);
    }

    /// <summary>
    /// Draws the editor and handles its input. A size component of 0 fills the available space,
    /// a negative one leaves that much space. Returns true if the text changed.
    /// </summary>
    public bool Draw(ImString id, float2 size = default)
    {
        int startVersion = Document.Version;
        _layout.TabSize = Options.TabSize;

        var font = Options.Font ?? ImGui.GetFont();
        float fontSize = Options.FontSize > 0 ? Options.FontSize : ImGui.GetStyle().FontSizeBase;
        ImGui.PushFont(font, fontSize * Options.Zoom);

        CharWidth = ImGui.GetFontBaked().GetCharAdvance('M');
        float spacing = MathF.Round(Options.LineSpacing * Options.Zoom);
        LineHeight = MathF.Round(ImGui.GetFontSize()) + spacing;
        _textPadding = MathF.Round(CharWidth * 0.5f);

        int digits = Math.Max(3, (int)Math.Floor(Math.Log10(Document.LineCount)) + 1);
        _gutterWidth = Options.ShowLineNumbers ? MathF.Round((digits + 2) * CharWidth) : 0;

        var avail = ImGui.GetContentRegionAvail();
        if (size.X <= 0)
            size.X = MathF.Max(avail.X + size.X, CharWidth * 4);
        if (size.Y <= 0)
            size.Y = MathF.Max(avail.Y + size.Y, LineHeight * 2);

        var contentSize = new float2(
            _gutterWidth + _textPadding + (_layout.MaxVisualLength + 2) * CharWidth,
            (Document.LineCount + 1) * LineHeight);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Background);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0, 0));
        ImGui.SetNextWindowContentSize(contentSize);
        bool visible = ImGui.BeginChild(id, size, ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoNavInputs);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        if (visible)
            DrawContents(spacing);
        else
            HasFocus = false;

        ImGui.EndChild();
        if (visible)
            DrawPopups();
        ImGui.PopFont();
        return Document.Version != startVersion;
    }

    void DrawContents(float spacing)
    {
        var io = ImGui.GetIO();
        var style = ImGui.GetStyle();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        float scrollX = ImGui.GetScrollX();
        float scrollY = ImGui.GetScrollY();
        float viewW = winSize.X - (ImGui.GetScrollMaxY() > 0 ? style.ScrollbarSize : 0);
        float viewH = winSize.Y - (ImGui.GetScrollMaxX() > 0 ? style.ScrollbarSize : 0);

        _textTop = winPos.Y - scrollY;
        _textOrigin = new float2(winPos.X + _gutterWidth + _textPadding - scrollX, _textTop);
        FirstVisibleLine = Math.Clamp((int)(scrollY / LineHeight), 0, Document.LineCount - 1);
        VisibleLineCount = Math.Max(1, (int)(viewH / LineHeight));

        // the item swallows clicks so the parent window does not move and holds the mouse wheel when zooming
        ImGui.SetCursorScreenPos(winPos);
        ImGui.InvisibleButton("##text"u8, new float2(MathF.Max(viewW, 1), MathF.Max(viewH, 1)));
        bool hovered = ImGui.IsItemHovered();
        var itemId = ImGui.GetItemID();

        if (_focusRequested)
        {
            ImGui.SetWindowFocus();
            _focusRequested = false;
        }
        HasFocus = ImGui.IsWindowFocused();

        if (hovered)
        {
            if (ImGui.GetMousePos().X >= winPos.X + _gutterWidth)
                ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);
            if (io.KeyCtrl && Options.MouseWheelZoom)
            {
                ImGui.SetItemKeyOwner(ImGuiKey.MouseWheelY);
                if (io.MouseWheel != 0)
                    SetZoom(Options.Zoom * MathF.Pow(1.1f, io.MouseWheel));
            }
        }

        if (HasFocus)
        {
            // keeps keyboard navigation from moving the focus to the parent window on Escape
            ImGui.Internal.SetKeyOwner(ImGuiKey.Escape, itemId);
            io.WantCaptureKeyboard = true;
            io.WantTextInput = true;
            ImGui.SetNextFrameWantCaptureKeyboard(true);
            HandleKeyboard(io);
        }
        HandleMouse(io, hovered, winPos.X + _gutterWidth);
        RefreshCompletion();
        UpdateDiagnostics();

        if (Session.CaretVersion != _lastCaretVersion)
        {
            _lastCaretVersion = Session.CaretVersion;
            _scrollToCaret = true;
            _caretTime = ImGui.GetTime();
            _hoverSuppressed = true;
        }
        if (Document.Version != _lastDocumentVersion)
        {
            _lastDocumentVersion = Document.Version;
            _caretTime = ImGui.GetTime();
            _hoverSuppressed = true;
        }

        UpdateHover(hovered);

        if (_scrollLines != 0)
        {
            ImGui.SetScrollY(scrollY + _scrollLines * LineHeight);
            _scrollLines = 0;
        }
        if (_scrollToCaret)
        {
            ScrollCaretIntoView(scrollX, scrollY, viewW - _gutterWidth, viewH);
            _scrollToCaret = false;
        }

        DrawText(winPos, viewW, viewH, scrollX, spacing);
        DrawGutter(winPos, viewH, spacing);
    }

    void HandleKeyboard(ImGuiIOPtr io)
    {
        bool vim = Options.VimMode;
        if (vim != _vimActive)
        {
            _vimActive = vim;
            Vim.Reset();
        }
        if (vim)
        {
            Vim.PageLines = VisibleLineCount;
            Vim.Sync();
        }
        bool vimCommandMode = vim && Vim.Mode != VimMode.Insert;

        if (HandleCompletionKeys())
        {
        }
        else if (vim && Vim.Mode == VimMode.Insert && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            CloseCompletion();
            Vim.HandleKey(VimKey.Escape);
        }
        else if (vimCommandMode)
            HandleVimKeys(io);
        else
        {
            var command = KeyMap.GetPressedCommand();
            if (command != null)
                ExecuteCommand(command);
        }

        if ((io.KeyCtrl && !io.KeyAlt) || io.KeySuper)
            return;
        ref var queue = ref io.InputQueueCharacters;
        if (queue.Count == 0)
            return;
        _typed.Clear();
        for (int i = 0; i < queue.Count; i++)
        {
            uint c = queue[i];
            if (c < 32 || c == 127)
                continue;
            if (c <= 0xFFFF)
                _typed.Append((char)c);
            else if (c <= 0x10FFFF)
                _typed.Append(char.ConvertFromUtf32((int)c));
        }
        int typedStart = 0;
        if (vimCommandMode)
        {
            while (typedStart < _typed.Length && Vim.HandleChar(_typed[typedStart]))
                typedStart++;
            _typed.Remove(0, typedStart);
        }
        if (_typed.Length > 0 && Session.Type(_typed.ToString()))
        {
            if (Options.AutoCompletion && _completion == null && Session.IsWordChar(_typed[^1]))
                UpdateCompletion(open: true);
        }
    }

    static readonly (ImGuiKey Key, VimKey VimKey)[] VimKeys =
    [
        (ImGuiKey.Escape, VimKey.Escape), (ImGuiKey.Enter, VimKey.Enter), (ImGuiKey.KeypadEnter, VimKey.Enter),
        (ImGuiKey.Backspace, VimKey.Backspace), (ImGuiKey.Delete, VimKey.Delete),
        (ImGuiKey.LeftArrow, VimKey.Left), (ImGuiKey.RightArrow, VimKey.Right), (ImGuiKey.UpArrow, VimKey.Up),
        (ImGuiKey.DownArrow, VimKey.Down), (ImGuiKey.Home, VimKey.Home), (ImGuiKey.End, VimKey.End),
        (ImGuiKey.PageUp, VimKey.PageUp), (ImGuiKey.PageDown, VimKey.PageDown),
    ];

    bool _vimActive;

    /// <summary>Keys in vim normal, visual and command line mode. Ctrl/Alt chords not used by vim go to the key map.</summary>
    void HandleVimKeys(ImGuiIOPtr io)
    {
        if (!io.KeyCtrl && !io.KeyAlt)
        {
            foreach (var (key, vimKey) in VimKeys)
                if (ImGui.IsKeyPressed(key))
                    Vim.HandleKey(vimKey);
            return;
        }
        if (io.KeyCtrl && !io.KeyAlt && !io.KeyShift)
        {
            foreach (char c in "rudfb")
                if (ImGui.IsKeyPressed(ImGuiKey.A + (c - 'a')) && Vim.HandleCtrl(c))
                    return;
        }
        var command = KeyMap.GetPressedCommand();
        if (command != null)
            ExecuteCommand(command);
    }

    void HandleMouse(ImGuiIOPtr io, bool hovered, float textAreaLeft)
    {
        var mouse = ImGui.GetMousePos();
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            CloseCompletion();
            var pos = TextPositionAt(mouse);
            int clicks = ImGui.GetMouseClickedCount(ImGuiMouseButton.Left);
            if (mouse.X < textAreaLeft || clicks >= 3)
            {
                _dragMode = DragMode.Line;
                _dragAnchor = new TextRange(pos, pos);
                SelectLines(pos.Line, pos.Line);
            }
            else if (clicks == 2)
            {
                _dragMode = DragMode.Word;
                _dragAnchor = Document.GetWordAt(pos, Session.IsWordChar);
                Session.Selection = Selection.FromRange(_dragAnchor);
            }
            else if (io.KeyCtrl && WordActivated != null)
            {
                _dragMode = DragMode.None;
                Session.SetCaret(pos);
                WordActivated(this, pos);
            }
            else
            {
                _dragMode = DragMode.Char;
                Session.SetCaret(pos, io.KeyShift);
            }
            return;
        }

        if (_dragMode == DragMode.None)
            return;
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _dragMode = DragMode.None;
            return;
        }
        if (io.MouseDelta.X == 0 && io.MouseDelta.Y == 0 && !IsOutsideView(mouse))
            return;

        var cur = TextPositionAt(mouse);
        switch (_dragMode)
        {
            case DragMode.Char:
                Session.SetCaret(cur, extendSelection: true);
                break;
            case DragMode.Word:
                var word = Document.GetWordAt(cur, Session.IsWordChar);
                Session.Selection = cur < _dragAnchor.Start
                    ? new Selection(_dragAnchor.End, word.Start)
                    : new Selection(_dragAnchor.Start, TextPosition.Max(word.End, _dragAnchor.End));
                break;
            case DragMode.Line:
                SelectLines(_dragAnchor.Start.Line, cur.Line);
                break;
        }
    }

    bool IsOutsideView(float2 mouse)
    {
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        return mouse.X < min.X + _gutterWidth || mouse.Y < min.Y || mouse.X > max.X || mouse.Y > max.Y;
    }

    void SelectLines(int anchorLine, int caretLine)
    {
        int first = Math.Min(anchorLine, caretLine);
        int last = Math.Max(anchorLine, caretLine);
        var start = new TextPosition(first, 0);
        var end = last + 1 < Document.LineCount ? new TextPosition(last + 1, 0) : new TextPosition(last, Document.LineLength(last));
        Session.Selection = caretLine < anchorLine ? new Selection(end, start) : new Selection(start, end);
    }

    void ScrollCaretIntoView(float scrollX, float scrollY, float textViewW, float viewH)
    {
        var caret = Session.Caret;
        float x = _textPadding + _layout[caret.Line].VisualColumn(caret.Col) * CharWidth;
        float y = caret.Line * LineHeight;
        float margin = CharWidth * 2;

        if (y < scrollY)
            ImGui.SetScrollY(y);
        else if (y + LineHeight > scrollY + viewH)
            ImGui.SetScrollY(y + LineHeight - viewH);

        if (x - margin < scrollX)
            ImGui.SetScrollX(x + margin <= textViewW ? 0 : x - margin);
        else if (x + margin > scrollX + textViewW)
            ImGui.SetScrollX(x + margin - textViewW);
    }

    void DrawText(float2 winPos, float viewW, float viewH, float scrollX, float spacing)
    {
        var dl = ImGui.GetWindowDrawList();
        var clipMin = new float2(winPos.X + _gutterWidth, winPos.Y);
        var clipMax = new float2(winPos.X + viewW, winPos.Y + viewH);
        dl.PushClipRect(clipMin, clipMax, true);

        var selection = Session.Selection;
        var sel = selection.Range;
        var caret = Session.Caret;
        float textY = MathF.Floor(spacing / 2);
        int firstCol = Math.Max(0, (int)((scrollX - _textPadding) / CharWidth) - 1);
        int visibleCols = (int)((viewW - _gutterWidth) / CharWidth) + 3;
        int last = Math.Min(Document.LineCount - 1, FirstVisibleLine + VisibleLineCount + 1);

        int highlight = FirstHighlight(FirstVisibleLine);
        for (int l = FirstVisibleLine; l <= last; l++)
        {
            float y = _textTop + l * LineHeight;
            var layout = _layout[l];

            if (Options.HighlightCurrentLine && l == caret.Line && selection.IsEmpty)
                dl.AddRectFilled(new float2(clipMin.X, y), new float2(clipMax.X, y + LineHeight), Theme.CurrentLine);

            for (; highlight < Highlights.Count && Highlights[highlight].Start.Line <= l; highlight++)
            {
                var h = Highlights[highlight];
                if (h.Start.Line != l || h.End.Line != l)
                    continue;
                float hx0 = layout.VisualColumn(h.Start.Col) * CharWidth;
                float hx1 = layout.VisualColumn(h.End.Col) * CharWidth;
                dl.AddRectFilled(new float2(_textOrigin.X + hx0, y), new float2(_textOrigin.X + hx1, y + LineHeight), Theme.SearchMatch);
            }

            if (!selection.IsEmpty && l >= sel.Start.Line && l <= sel.End.Line)
            {
                float x0 = l == sel.Start.Line ? layout.VisualColumn(sel.Start.Col) * CharWidth : 0;
                float x1 = l == sel.End.Line
                    ? layout.VisualColumn(sel.End.Col) * CharWidth
                    : layout.VisualLength * CharWidth + _textPadding;
                if (x1 > x0)
                    dl.AddRectFilled(new float2(_textOrigin.X + x0, y), new float2(_textOrigin.X + x1, y + LineHeight), Theme.Selection);
            }

            if (layout.Utf8.Length > 0 && layout.VisualLength > firstCol)
            {
                int c0 = 0, c1 = layout.Source.Length;
                if (firstCol > 0 || layout.VisualLength > visibleCols)
                {
                    c0 = Math.Max(0, layout.ColumnFromVisual(firstCol) - 1);
                    c1 = Math.Min(layout.Source.Length, layout.ColumnFromVisual(firstCol + visibleCols) + 1);
                }
                DrawLineText(dl, layout, _highlighter.GetTokens(l), c0, c1, y + textY);
            }

            if (Diagnostics.Count > 0)
                DrawDiagnostics(dl, l, layout, y + LineHeight - MathF.Max(1, spacing / 2));
        }

        if (HasFocus && Options.VimMode && Vim.BlockCaret)
        {
            var pos = Vim.Caret;
            var layout = _layout[pos.Line];
            var p = ScreenPosition(pos);
            int width = Math.Max(1, layout.VisualColumn(pos.Col + 1) - layout.VisualColumn(pos.Col));
            dl.AddRectFilled(p, new float2(p.X + width * CharWidth, p.Y + LineHeight), Theme.BlockCaret);
        }
        else if (HasFocus && CaretVisible())
        {
            var p = ScreenPosition(caret);
            dl.AddRectFilled(new float2(p.X - 1, p.Y), new float2(p.X + 1, p.Y + LineHeight), Theme.Caret);
        }

        dl.PopClipRect();
    }

    int FirstHighlight(int line)
    {
        int lo = 0, hi = Highlights.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Highlights[mid].Start.Line < line)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    void DrawLineText(ImDrawListPtr dl, LineLayout layout, ReadOnlySpan<Token> tokens, int c0, int c1, float y)
    {
        int pos = c0;
        foreach (var t in tokens)
        {
            if (t.End <= c0)
                continue;
            if (t.Start >= c1)
                break;
            if (t.Start > pos)
                DrawSegment(dl, layout, pos, t.Start, y, Theme.Text);
            int start = Math.Max(t.Start, pos);
            int end = Math.Min(t.End, c1);
            DrawSegment(dl, layout, start, end, y, Theme[t.Type]);
            pos = end;
        }
        if (pos < c1)
            DrawSegment(dl, layout, pos, c1, y, Theme.Text);
    }

    void DrawSegment(ImDrawListPtr dl, LineLayout layout, int c0, int c1, float y, ImColor8 color)
    {
        int b0 = layout.ByteOffset(c0);
        int b1 = layout.ByteOffset(c1);
        if (b1 <= b0)
            return;
        var pos = new float2(_textOrigin.X + layout.VisualColumn(c0) * CharWidth, y);
        dl.AddText(pos, color, layout.Utf8.AsSpan(b0, b1 - b0));
    }

    void DrawDiagnostics(ImDrawListPtr dl, int line, LineLayout layout, float y)
    {
        foreach (var d in Diagnostics)
        {
            var r = d.Range.Normalized;
            if (line < r.Start.Line || line > r.End.Line)
                continue;
            int v0 = line == r.Start.Line ? layout.VisualColumn(r.Start.Col) : 0;
            int v1 = line == r.End.Line ? layout.VisualColumn(r.End.Col) : layout.VisualLength;
            if (v1 <= v0)
                v1 = v0 + 1;
            float x0 = _textOrigin.X + v0 * CharWidth;
            float x1 = _textOrigin.X + v1 * CharWidth;
            DrawSquiggle(dl, x0, x1, y, Theme[d.Severity]);
        }
    }

    void DrawSquiggle(ImDrawListPtr dl, float x0, float x1, float y, ImColor8 color)
    {
        float step = MathF.Max(2, MathF.Round(CharWidth / 3));
        float amp = MathF.Max(1, step / 2);
        bool up = true;
        for (float x = x0; x < x1; x += step)
        {
            float nx = MathF.Min(x + step, x1);
            dl.AddLine(new float2(x, up ? y : y - amp), new float2(nx, up ? y - amp : y), color, 1f);
            up = !up;
        }
    }

    bool CaretVisible()
    {
        float period = Options.CaretBlinkPeriod;
        if (period <= 0)
            return true;
        double t = ImGui.GetTime() - _caretTime;
        return t % period < period * 0.5;
    }

    void DrawGutter(float2 winPos, float viewH, float spacing)
    {
        if (_gutterWidth <= 0)
            return;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, new float2(winPos.X + _gutterWidth, winPos.Y + viewH), Theme.GutterBackground);

        Span<byte> buf = stackalloc byte[16];
        int caretLine = Session.Caret.Line;
        int last = Math.Min(Document.LineCount - 1, FirstVisibleLine + VisibleLineCount + 1);
        float textY = MathF.Floor(spacing / 2);
        for (int l = FirstVisibleLine; l <= last; l++)
        {
            (l + 1).TryFormat(buf, out int n);
            float x = winPos.X + _gutterWidth - CharWidth * (n + 1);
            var color = l == caretLine ? Theme.ActiveLineNumber : Theme.LineNumber;
            dl.AddText(new float2(x, _textTop + l * LineHeight + textY), color, buf[..n]);
        }
    }
}
