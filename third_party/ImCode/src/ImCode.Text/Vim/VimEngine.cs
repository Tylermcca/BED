using System.Text;
using ImCode.Text;

namespace ImCode.Vim;

public enum VimMode
{
    Normal,
    Insert,
    Visual,
    VisualLine,
    CommandLine,
}

public enum VimKey
{
    Escape,
    Enter,
    Backspace,
    Delete,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,
}

/// <summary>
/// Vim emulation on top of an <see cref="EditSession"/>. The host feeds typed characters, special keys and
/// Ctrl combinations; in insert mode only Escape is consumed and the host handles regular text input.
/// </summary>
public sealed class VimEngine
{
    enum MotionKind
    {
        Exclusive,
        Inclusive,
        Linewise,
    }

    readonly record struct Input(char Char, VimKey? Key = null, bool Ctrl = false);

    readonly EditSession _s;

    string _count = "";
    char _operator;
    int _operatorCount = 1;
    string _pending = "";

    char _commandLineType;
    char _commandLineOperator;
    readonly StringBuilder _commandLine = new();
    VimMode _modeBeforeCommandLine;

    TextPosition _visualAnchor;
    TextPosition _visualCaret;
    Selection _visualSelection;

    char _lastFindCommand;
    char _lastFindChar;
    string _lastSearch = "";
    bool _lastSearchForward = true;
    bool _lastSearchWholeWord;

    readonly List<Input> _keys = [];
    List<Input> _lastChange = [];
    string _lastInsert = "";
    bool _replaying;
    bool _changed;
    bool _recordInsert;
    TextPosition _insertStart;
    long _commandUndoId;
    long _insertUndoId;

    public VimEngine(EditSession session)
    {
        _s = session;
    }

    public VimMode Mode { get; private set; } = VimMode.Normal;

    /// <summary>Lines scrolled by Ctrl+F/B, half of it by Ctrl+D/U. Set by the host from the view size.</summary>
    public int PageLines { get; set; } = 20;

    /// <summary>Last message of a command, e.g. "3 lines yanked".</summary>
    public string Message { get; private set; } = "";

    /// <summary>Handles ex commands other than line numbers (w, q, wq, ...). Returns a message, or null if unknown.</summary>
    public Func<string, string?>? ExCommandHandler { get; set; }

    /// <summary>Raised by "gf" with the caret position.</summary>
    public event Action<TextPosition>? WordActivated;

    public event Action<VimMode>? ModeChanged;

    TextDocument Doc => _s.Document;
    bool IsVisual => Mode is VimMode.Visual or VimMode.VisualLine;
    bool IsIdle => _count.Length == 0 && _operator == '\0' && _pending.Length == 0;

    /// <summary>Caret for display: in visual mode the moving end of the selection.</summary>
    public TextPosition Caret => IsVisual ? _visualCaret : _s.Caret;

    public bool BlockCaret => Mode != VimMode.Insert;

    /// <summary>Mode indicator, pending keys or command line and the last message.</summary>
    public string Status => Mode switch
    {
        VimMode.Insert => Join("-- INSERT --", Message),
        VimMode.Visual => Join("-- VISUAL --", PendingKeys),
        VimMode.VisualLine => Join("-- VISUAL LINE --", PendingKeys),
        VimMode.CommandLine => $"{_commandLineType}{_commandLine}",
        _ => Join(PendingKeys, Message),
    };

    string PendingKeys => (_operatorCount > 1 && _operator != '\0' ? _operatorCount.ToString() : "") + (_operator == '\0' ? "" : _operator.ToString()) + _count + _pending;

    static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : $"{a}  {b}";

    #region Input

    public void Reset()
    {
        ResetParser();
        if (Mode != VimMode.Normal)
            SetMode(VimMode.Normal);
        _s.SetCaret(_s.Caret);
        ClampNormalCaret();
    }

    /// <summary>Adopts selection and caret changes made outside the engine (mouse, host commands).</summary>
    public void Sync()
    {
        var sel = _s.Selection;
        if (Mode == VimMode.Normal)
        {
            if (!sel.IsEmpty)
                EnterVisualFromSelection(sel);
            else
                ClampNormalCaret();
        }
        else if (IsVisual && sel != _visualSelection)
        {
            if (sel.IsEmpty)
            {
                SetMode(VimMode.Normal);
                ClampNormalCaret();
            }
            else
                EnterVisualFromSelection(sel);
        }
    }

    void EnterVisualFromSelection(Selection sel)
    {
        _visualAnchor = sel.Anchor;
        _visualCaret = sel.Caret > sel.Anchor ? Doc.MoveChars(sel.Caret, -1) : sel.Caret;
        if (Mode != VimMode.VisualLine)
            SetMode(VimMode.Visual);
        UpdateVisualSelection();
    }

    /// <summary>Typed character. Returns false in insert mode, the host inserts it.</summary>
    public bool HandleChar(char c)
    {
        if (Mode == VimMode.Insert)
            return false;
        if (Mode == VimMode.CommandLine)
        {
            _commandLine.Append(c);
            return true;
        }
        Dispatch(new Input(c));
        return true;
    }

    public bool HandleKey(VimKey key)
    {
        switch (Mode)
        {
            case VimMode.Insert:
                if (key != VimKey.Escape)
                    return false;
                LeaveInsertMode();
                return true;
            case VimMode.CommandLine:
                HandleCommandLineKey(key);
                return true;
            default:
                Dispatch(new Input('\0', key));
                return true;
        }
    }

    /// <summary>Ctrl+letter. Handles r, u, d, f, b in normal and visual mode.</summary>
    public bool HandleCtrl(char c)
    {
        if (Mode is VimMode.Insert or VimMode.CommandLine || "rudfb".IndexOf(c) < 0)
            return false;
        Dispatch(new Input(c, null, true));
        return true;
    }

    void Dispatch(Input input)
    {
        bool startOfCommand = IsIdle;
        if (startOfCommand)
        {
            Message = "";
            _changed = false;
            _commandUndoId = _s.History.TopId;
            if (!_replaying)
                _keys.Clear();
        }
        if (!_replaying)
            _keys.Add(input);

        var modeBefore = Mode;
        if (input.Ctrl)
            ProcessCtrl(input.Char);
        else if (input.Key is { } key)
            ProcessKey(key);
        else
            ProcessChar(input.Char);

        if (!IsIdle || Mode == VimMode.CommandLine)
            return;

        if (_changed && !_replaying && modeBefore == VimMode.Normal)
        {
            _lastChange = [.. _keys];
            _lastInsert = "";
        }
        if (Mode == VimMode.Insert)
        {
            _insertUndoId = _commandUndoId;
            _recordInsert = _changed && modeBefore == VimMode.Normal;
            _insertStart = _s.Caret;
        }
        else
        {
            _s.History.SquashSince(_commandUndoId);
            if (Mode == VimMode.Normal)
                ClampNormalCaret();
        }
    }

    void ProcessKey(VimKey key)
    {
        switch (key)
        {
            case VimKey.Escape:
                ResetParser();
                if (IsVisual)
                    ExitVisual(_visualCaret);
                break;
            case VimKey.Enter:
                ProcessChar('+');
                break;
            case VimKey.Backspace:
            case VimKey.Left:
                ProcessChar('h');
                break;
            case VimKey.Right:
                ProcessChar('l');
                break;
            case VimKey.Up:
                ProcessChar('k');
                break;
            case VimKey.Down:
                ProcessChar('j');
                break;
            case VimKey.Home:
                ProcessChar('0');
                break;
            case VimKey.End:
                ProcessChar('$');
                break;
            case VimKey.Delete:
                ProcessChar('x');
                break;
            case VimKey.PageUp:
                ProcessCtrl('b');
                break;
            case VimKey.PageDown:
                ProcessCtrl('f');
                break;
        }
    }

    void ProcessCtrl(char c)
    {
        int n = TakeCount();
        switch (c)
        {
            case 'r':
                for (int i = 0; i < n; i++)
                    _s.Redo();
                AfterUndo();
                break;
            case 'd':
            case 'u':
            case 'f':
            case 'b':
                int lines = (c is 'd' or 'u' ? Math.Max(1, PageLines / 2) : PageLines) * n;
                MoveTo(MoveLines(Caret, c is 'd' or 'f' ? lines : -lines));
                break;
        }
        ResetParser();
    }

    #endregion

    #region Parser

    void ResetParser()
    {
        _count = "";
        _operator = '\0';
        _operatorCount = 1;
        _pending = "";
    }

    int TakeCount()
    {
        int n = _count.Length > 0 ? int.Parse(_count) : 1;
        _count = "";
        return n * _operatorCount;
    }

    bool HasCount => _count.Length > 0;

    void ProcessChar(char c)
    {
        if (_pending.Length > 0)
        {
            ProcessPending(c);
            return;
        }
        if (char.IsDigit(c) && (c != '0' || _count.Length > 0))
        {
            _count += c;
            return;
        }
        if (_operator != '\0')
        {
            if (c is '/' or '?')
            {
                StartCommandLine(c);
                return;
            }
            if (c == _operator)
            {
                int n = TakeCount();
                var first = Caret.Line;
                LinewiseOperator(_operator, first, Math.Min(first + n - 1, Doc.LineCount - 1));
                ResetParser();
                return;
            }
            if (c is 'i' or 'a')
            {
                _pending = c.ToString();
                return;
            }
        }
        if (TryMotion(c))
            return;
        if (IsVisual)
            VisualCommand(c);
        else
            NormalCommand(c);
    }

    void ProcessPending(char c)
    {
        var pending = _pending;
        _pending = "";
        switch (pending)
        {
            case "f":
            case "t":
            case "F":
            case "T":
                _lastFindCommand = pending[0];
                _lastFindChar = c;
                ApplyMotion(FindInLine(pending[0], c, TakeCount()), pending[0] is 'f' or 't' ? MotionKind.Inclusive : MotionKind.Exclusive);
                break;
            case "g":
                switch (c)
                {
                    case 'g':
                        int line = HasCount ? TakeCount() - 1 : 0;
                        ApplyMotion(FirstNonBlank(Math.Clamp(line, 0, Doc.LineCount - 1)), MotionKind.Linewise);
                        break;
                    case 'f':
                        WordActivated?.Invoke(Caret);
                        ResetParser();
                        break;
                    default:
                        ResetParser();
                        break;
                }
                break;
            case "r":
                ReplaceChars(c, TakeCount());
                ResetParser();
                break;
            case "i":
            case "a":
                var range = TextObject(c, pending == "a");
                if (range == null)
                {
                    ResetParser();
                    return;
                }
                if (IsVisual)
                {
                    _visualAnchor = range.Value.Start;
                    _visualCaret = Doc.MoveChars(range.Value.End, -1);
                    UpdateVisualSelection();
                    ResetParser();
                }
                else
                {
                    char op = _operator;
                    ResetParser();
                    CharOperator(op, range.Value);
                }
                break;
            default:
                ResetParser();
                break;
        }
    }

    #endregion

    #region Motions

    bool TryMotion(char c)
    {
        switch (c)
        {
            case 'f':
            case 't':
            case 'F':
            case 'T':
            case 'g':
                _pending = c.ToString();
                return true;
            case 'i':
            case 'a':
                if (!IsVisual)
                    return false;
                _pending = c.ToString();
                return true;
        }

        var pos = Caret;
        int n;
        switch (c)
        {
            case 'h':
                n = TakeCount();
                ApplyMotion(pos with { Col = Math.Max(0, pos.Col - n) }, MotionKind.Exclusive);
                return true;
            case 'l':
            case ' ':
                n = TakeCount();
                int max = _operator != '\0' || IsVisual ? Doc.LineLength(pos.Line) : Math.Max(0, Doc.LineLength(pos.Line) - 1);
                ApplyMotion(pos with { Col = Math.Min(max, pos.Col + n) }, MotionKind.Exclusive);
                return true;
            case 'j':
            case '+':
                n = TakeCount();
                var down = MoveLines(pos, n);
                ApplyMotion(c == '+' ? FirstNonBlank(down.Line) : down, MotionKind.Linewise);
                return true;
            case 'k':
            case '-':
                n = TakeCount();
                var up = MoveLines(pos, -n);
                ApplyMotion(c == '-' ? FirstNonBlank(up.Line) : up, MotionKind.Linewise);
                return true;
            case 'w':
            case 'W':
                n = TakeCount();
                if (_operator == 'c' && Cls(pos, c == 'W') is 1 or 2)
                {
                    for (int i = 0; i < n; i++)
                        pos = i == 0 && IsWordEndAt(pos, c == 'W') ? pos : WordEnd(pos, c == 'W');
                    ApplyMotion(pos, MotionKind.Inclusive);
                    return true;
                }
                for (int i = 0; i < n; i++)
                    pos = WordForward(pos, c == 'W');
                ApplyMotion(pos, MotionKind.Exclusive, wordMotion: true);
                return true;
            case 'b':
            case 'B':
                n = TakeCount();
                for (int i = 0; i < n; i++)
                    pos = WordBackward(pos, c == 'B');
                ApplyMotion(pos, MotionKind.Exclusive);
                return true;
            case 'e':
            case 'E':
                n = TakeCount();
                for (int i = 0; i < n; i++)
                    pos = WordEnd(pos, c == 'E');
                ApplyMotion(pos, MotionKind.Inclusive);
                return true;
            case '0':
                ApplyMotion(pos with { Col = 0 }, MotionKind.Exclusive);
                return true;
            case '^':
                ApplyMotion(FirstNonBlank(pos.Line), MotionKind.Exclusive);
                return true;
            case '$':
                n = TakeCount();
                int l = Math.Min(pos.Line + n - 1, Doc.LineCount - 1);
                ApplyMotion(new TextPosition(l, Math.Max(0, Doc.LineLength(l) - 1)), MotionKind.Inclusive);
                return true;
            case 'G':
                int target = HasCount ? Math.Clamp(TakeCount() - 1, 0, Doc.LineCount - 1) : Doc.LineCount - 1;
                ApplyMotion(FirstNonBlank(target), MotionKind.Linewise);
                return true;
            case '}':
            case '{':
                n = TakeCount();
                for (int i = 0; i < n; i++)
                    pos = Paragraph(pos, c == '}');
                ApplyMotion(pos, MotionKind.Exclusive);
                return true;
            case '%':
                TakeCount();
                ApplyMotion(MatchingBracket(pos), MotionKind.Inclusive);
                return true;
            case ';':
            case ',':
                if (_lastFindCommand == '\0')
                {
                    ResetParser();
                    return true;
                }
                char cmd = _lastFindCommand;
                if (c == ',')
                    cmd = char.IsUpper(cmd) ? char.ToLower(cmd) : char.ToUpper(cmd);
                ApplyMotion(FindInLine(cmd, _lastFindChar, TakeCount(), repeat: true), cmd is 'f' or 't' ? MotionKind.Inclusive : MotionKind.Exclusive);
                return true;
            case 'n':
            case 'N':
                TakeCount();
                ApplyMotion(Search(_lastSearch, c == 'n' ? _lastSearchForward : !_lastSearchForward, _lastSearchWholeWord), MotionKind.Exclusive);
                return true;
            case '*':
            case '#':
                TakeCount();
                var word = Doc.GetWordAt(pos, _s.IsWordChar);
                var term = Doc.GetText(word);
                if (term.Length == 0 || !_s.IsWordChar(term[0]))
                {
                    ResetParser();
                    return true;
                }
                _lastSearch = term;
                _lastSearchForward = c == '*';
                _lastSearchWholeWord = true;
                ApplyMotion(Search(term, _lastSearchForward, true, word.Start), MotionKind.Exclusive);
                return true;
        }
        return false;
    }

    void ApplyMotion(TextPosition? target, MotionKind kind, bool wordMotion = false)
    {
        if (target is not { } to)
        {
            ResetParser();
            return;
        }
        var from = Caret;
        char op = _operator;
        ResetParser();

        if (op != '\0')
        {
            var start = TextPosition.Min(from, to);
            var end = TextPosition.Max(from, to);
            if (kind == MotionKind.Linewise)
            {
                LinewiseOperator(op, start.Line, end.Line);
                return;
            }
            if (kind == MotionKind.Inclusive)
                end = end with { Col = Math.Min(end.Col + 1, Doc.LineLength(end.Line)) };
            else if (wordMotion && end.Line > start.Line)
                end = new TextPosition(end.Line - 1, Doc.LineLength(end.Line - 1));
            CharOperator(op, new TextRange(start, end));
        }
        else
            MoveTo(to);
    }

    void MoveTo(TextPosition pos)
    {
        if (IsVisual)
        {
            _visualCaret = Doc.Clamp(pos);
            UpdateVisualSelection();
        }
        else
            _s.SetCaret(pos);
    }

    TextPosition MoveLines(TextPosition pos, int lines)
    {
        int line = Math.Clamp(pos.Line + lines, 0, Doc.LineCount - 1);
        return Doc.Clamp(new TextPosition(line, pos.Col));
    }

    TextPosition FirstNonBlank(int line) => new(line, Math.Min(TextNavigation.LeadingWhitespace(Doc[line]), Math.Max(0, Doc.LineLength(line) - 1)));

    TextPosition? FindInLine(char cmd, char c, int count, bool repeat = false)
    {
        var line = Doc[Caret.Line];
        int col = Caret.Col;
        bool forward = cmd is 'f' or 't';
        for (int i = 0; i < count; i++)
        {
            int from = forward ? col + 1 : col - 1;
            if (repeat && i == 0 && cmd is 't' or 'T')
                from += forward ? 1 : -1;
            if (from < 0 || from >= line.Length)
                return null;
            int index = forward ? line.IndexOf(c, from) : line.LastIndexOf(c, from);
            if (index < 0)
                return null;
            col = index;
        }
        if (cmd == 't')
            col--;
        else if (cmd == 'T')
            col++;
        return new TextPosition(Caret.Line, col);
    }

    TextPosition? Search(string term, bool forward, bool wholeWord, TextPosition? from = null)
    {
        if (term.Length == 0)
        {
            Message = "No previous search";
            return null;
        }
        var start = from ?? Caret;
        var searchFrom = forward ? Doc.MoveChars(start, 1) : start;
        var match = Doc.Find(term, searchFrom, forward, new SearchOptions(MatchCase: true, WholeWord: wholeWord), _s.IsWordChar);
        if (match == null)
        {
            Message = $"Pattern not found: {term}";
            return null;
        }
        Message = $"{(forward ? '/' : '?')}{term}";
        return match.Value.Start;
    }

    TextPosition Paragraph(TextPosition pos, bool forward)
    {
        int line = pos.Line;
        bool Blank(int l) => string.IsNullOrWhiteSpace(Doc[l]);
        int step = forward ? 1 : -1;
        while (line + step >= 0 && line + step < Doc.LineCount && Blank(line))
            line += step;
        while (line + step >= 0 && line + step < Doc.LineCount)
        {
            line += step;
            if (Blank(line))
                return new TextPosition(line, 0);
        }
        return forward ? Doc.End : TextPosition.Zero;
    }

    const string Brackets = "()[]{}<>";

    TextPosition? MatchingBracket(TextPosition pos)
    {
        var line = Doc[pos.Line];
        int col = pos.Col;
        while (col < line.Length && Brackets.IndexOf(line[col]) < 0)
            col++;
        if (col >= line.Length)
            return null;
        int index = Brackets.IndexOf(line[col]);
        bool forward = index % 2 == 0;
        char open = Brackets[index & ~1];
        char close = Brackets[index | 1];
        return FindBracket(new TextPosition(pos.Line, col), open, close, forward);
    }

    /// <summary>Position of the bracket matching the one at pos, scanning in the given direction.</summary>
    TextPosition? FindBracket(TextPosition pos, char open, char close, bool forward)
    {
        int depth = 0;
        var p = pos;
        while (true)
        {
            char c = Doc.CharAt(p);
            if (c == (forward ? open : close))
                depth++;
            else if (c == (forward ? close : open) && --depth == 0)
                return p;
            if (!(forward ? Next(ref p) : Prev(ref p)))
                return null;
        }
    }

    #endregion

    #region Word classes

    bool Next(ref TextPosition p)
    {
        if (p.Col < Doc.LineLength(p.Line))
            p = p with { Col = p.Col + 1 };
        else if (p.Line < Doc.LineCount - 1)
            p = new TextPosition(p.Line + 1, 0);
        else
            return false;
        return true;
    }

    bool Prev(ref TextPosition p)
    {
        if (p.Col > 0)
            p = p with { Col = p.Col - 1 };
        else if (p.Line > 0)
            p = new TextPosition(p.Line - 1, Doc.LineLength(p.Line - 1));
        else
            return false;
        return true;
    }

    // 0 whitespace or line end, 1 word, 2 punctuation, 3 empty line
    int Cls(TextPosition p, bool bigWord)
    {
        var line = Doc[p.Line];
        if (line.Length == 0)
            return 3;
        if (p.Col >= line.Length)
            return 0;
        char c = line[p.Col];
        if (char.IsWhiteSpace(c))
            return 0;
        return bigWord || _s.IsWordChar(c) ? 1 : 2;
    }

    TextPosition WordForward(TextPosition p, bool big)
    {
        int start = Cls(p, big);
        if (!Next(ref p))
            return p;
        if (start is 1 or 2)
            while (Cls(p, big) == start)
                if (!Next(ref p))
                    return p;
        while (Cls(p, big) == 0)
            if (!Next(ref p))
                return p;
        return p;
    }

    TextPosition WordBackward(TextPosition p, bool big)
    {
        if (!Prev(ref p))
            return p;
        while (Cls(p, big) == 0)
            if (!Prev(ref p))
                return p;
        int cls = Cls(p, big);
        if (cls == 3)
            return p;
        while (true)
        {
            var q = p;
            if (!Prev(ref q) || Cls(q, big) != cls)
                return p;
            p = q;
        }
    }

    TextPosition WordEnd(TextPosition p, bool big)
    {
        if (!Next(ref p))
            return p;
        while (Cls(p, big) is 0 or 3)
            if (!Next(ref p))
                return p;
        int cls = Cls(p, big);
        while (true)
        {
            var q = p;
            if (!Next(ref q) || Cls(q, big) != cls)
                return p;
            p = q;
        }
    }

    bool IsWordEndAt(TextPosition p, bool big)
    {
        int cls = Cls(p, big);
        var q = p;
        return !Next(ref q) || Cls(q, big) != cls;
    }

    TextRange? TextObject(char c, bool around)
    {
        var pos = Caret;
        switch (c)
        {
            case 'w':
            case 'W':
                return WordObject(pos, c == 'W', around);
            case '"':
            case '\'':
            case '`':
                return QuoteObject(pos, c, around);
        }
        int index = Brackets.IndexOf(c);
        if (c == 'b')
            index = 0;
        else if (c == 'B')
            index = 4;
        if (index < 0)
            return null;
        char open = Brackets[index & ~1];
        char close = Brackets[index | 1];
        var start = Doc.CharAt(pos) == open ? pos : FindBracket(pos, open, close, forward: false) ?? FindEnclosing(pos, open, close);
        if (start == null)
            return null;
        var end = FindBracket(start.Value, open, close, forward: true);
        if (end == null)
            return null;
        return around
            ? new TextRange(start.Value, end.Value with { Col = end.Value.Col + 1 })
            : new TextRange(Doc.MoveChars(start.Value, 1), end.Value);
    }

    TextPosition? FindEnclosing(TextPosition pos, char open, char close)
    {
        int depth = 0;
        var p = pos;
        while (Prev(ref p))
        {
            char ch = Doc.CharAt(p);
            if (ch == close)
                depth++;
            else if (ch == open && depth-- == 0)
                return p;
        }
        return null;
    }

    TextRange WordObject(TextPosition pos, bool big, bool around)
    {
        var line = Doc[pos.Line];
        if (line.Length == 0)
            return TextRange.Empty(pos);
        int col = Math.Min(pos.Col, line.Length - 1);
        int cls = Cls(new TextPosition(pos.Line, col), big);
        int start = col, end = col + 1;
        while (start > 0 && Cls(new TextPosition(pos.Line, start - 1), big) == cls)
            start--;
        while (end < line.Length && Cls(new TextPosition(pos.Line, end), big) == cls)
            end++;
        if (around && cls != 0)
        {
            int e = end;
            while (e < line.Length && char.IsWhiteSpace(line[e]))
                e++;
            if (e > end)
                end = e;
            else
                while (start > 0 && char.IsWhiteSpace(line[start - 1]))
                    start--;
        }
        return new TextRange(new TextPosition(pos.Line, start), new TextPosition(pos.Line, end));
    }

    /// <summary>Quotes on the line are paired from the left; uses the pair around the caret or the next one.</summary>
    TextRange? QuoteObject(TextPosition pos, char quote, bool around)
    {
        var line = Doc[pos.Line];
        int left = -1;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != quote)
                continue;
            if (left < 0)
            {
                left = i;
                continue;
            }
            if (pos.Col <= i)
                return around
                    ? new TextRange(new TextPosition(pos.Line, left), new TextPosition(pos.Line, i + 1))
                    : new TextRange(new TextPosition(pos.Line, left + 1), new TextPosition(pos.Line, i));
            left = -1;
        }
        return null;
    }

    #endregion

    #region Operators

    void Yank(string text) => _s.Clipboard.SetText(text);

    static string Plural(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";

    void CharOperator(char op, TextRange range)
    {
        range = Doc.Clamp(range.Normalized);
        var text = Doc.GetText(range);
        switch (op)
        {
            case 'y':
                Yank(text);
                ExitVisual(range.Start);
                break;
            case 'd':
                Yank(text);
                _s.Replace(range, "");
                _changed = true;
                ExitVisual(range.Start);
                break;
            case 'c':
                Yank(text);
                _s.Replace(range, "");
                _changed = true;
                EnterInsert(range.Start);
                break;
            case '>':
            case '<':
                LinewiseOperator(op, range.Start.Line, range.End.Line);
                break;
        }
    }

    void LinewiseOperator(char op, int first, int last)
    {
        int n = last - first + 1;
        var sb = new StringBuilder();
        for (int l = first; l <= last; l++)
            sb.Append(Doc[l]).Append('\n');

        switch (op)
        {
            case 'y':
                Yank(sb.ToString());
                Message = n > 2 ? $"{Plural(n, "line")} yanked" : "";
                ExitVisual(new TextPosition(first, IsVisual ? 0 : Caret.Col));
                break;
            case 'd':
                Yank(sb.ToString());
                if (last < Doc.LineCount - 1)
                    _s.Replace(new TextRange(new TextPosition(first, 0), new TextPosition(last + 1, 0)), "");
                else if (first > 0)
                    _s.Replace(new TextRange(new TextPosition(first - 1, Doc.LineLength(first - 1)), new TextPosition(last, Doc.LineLength(last))), "");
                else
                    _s.Replace(new TextRange(TextPosition.Zero, new TextPosition(last, Doc.LineLength(last))), "");
                _changed = true;
                Message = n > 2 ? $"{Plural(n, "fewer line")}" : "";
                ExitVisual(FirstNonBlank(Math.Min(first, Doc.LineCount - 1)));
                break;
            case 'c':
                Yank(sb.ToString());
                var indent = Doc[first][..TextNavigation.LeadingWhitespace(Doc[first])];
                _s.Replace(new TextRange(new TextPosition(first, 0), new TextPosition(last, Doc.LineLength(last))), indent);
                _changed = true;
                EnterInsert(new TextPosition(first, indent.Length));
                break;
            case '>':
            case '<':
                _s.Selection = new Selection(new TextPosition(first, 0), new TextPosition(last, Doc.LineLength(last)));
                _s.IndentLines(op == '>' ? 1 : -1);
                _changed = true;
                ExitVisual(FirstNonBlank(first));
                break;
        }
    }

    void ReplaceChars(char c, int count)
    {
        var pos = Caret;
        if (pos.Col + count > Doc.LineLength(pos.Line))
            return;
        _s.Replace(new TextRange(pos, pos with { Col = pos.Col + count }), new string(c, count));
        _changed = true;
        _s.SetCaret(pos with { Col = pos.Col + count - 1 });
    }

    void ToggleCase(TextRange range, Func<char, char>? convert = null)
    {
        convert ??= ch => char.IsUpper(ch) ? char.ToLower(ch) : char.ToUpper(ch);
        var text = Doc.GetText(range);
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            chars[i] = convert(chars[i]);
        var replaced = new string(chars);
        if (replaced != text)
        {
            _s.Replace(range, replaced);
            _changed = true;
        }
    }

    void JoinLines(int first, int count)
    {
        int last = Math.Min(first + Math.Max(1, count - 1), Doc.LineCount - 1);
        TextPosition pos = Caret;
        for (int l = first; l < last; l++)
        {
            var current = Doc[first];
            var next = Doc[first + 1];
            int ws = TextNavigation.LeadingWhitespace(next);
            var trimmedCurrent = current.TrimEnd();
            string sep = next.Length == ws || trimmedCurrent.Length == 0 || next[ws] == ')' ? "" : " ";
            var range = new TextRange(new TextPosition(first, trimmedCurrent.Length), new TextPosition(first + 1, ws));
            _s.Replace(range, sep);
            pos = new TextPosition(first, trimmedCurrent.Length);
            _changed = true;
        }
        _s.SetCaret(pos);
    }

    void Paste(bool after, int count)
    {
        var text = TextDocument.NormalizeLineEndings(_s.Clipboard.GetText());
        if (text.Length == 0)
            return;
        var pos = _s.Caret;
        _changed = true;
        if (text.EndsWith('\n'))
        {
            var lines = string.Concat(Enumerable.Repeat(text, count));
            if (after)
            {
                _s.Replace(TextRange.Empty(new TextPosition(pos.Line, Doc.LineLength(pos.Line))), "\n" + lines[..^1]);
                _s.SetCaret(FirstNonBlank(pos.Line + 1));
            }
            else
            {
                _s.Replace(TextRange.Empty(new TextPosition(pos.Line, 0)), lines);
                _s.SetCaret(FirstNonBlank(pos.Line));
            }
            return;
        }
        var insertAt = after ? pos with { Col = Math.Min(pos.Col + 1, Doc.LineLength(pos.Line)) } : pos;
        _s.Replace(TextRange.Empty(insertAt), string.Concat(Enumerable.Repeat(text, count)));
        _s.SetCaret(Doc.MoveChars(_s.Caret, -1));
    }

    #endregion

    #region Commands

    void NormalCommand(char c)
    {
        var pos = _s.Caret;
        var line = Doc[pos.Line];
        int n;
        switch (c)
        {
            case 'd':
            case 'c':
            case 'y':
            case '>':
            case '<':
                _operatorCount = HasCount ? int.Parse(_count) : 1;
                _count = "";
                _operator = c;
                return;
            case 'r':
                _pending = "r";
                return;
            case 'i':
                EnterInsert(pos);
                break;
            case 'a':
                EnterInsert(pos with { Col = Math.Min(pos.Col + 1, line.Length) });
                break;
            case 'I':
                EnterInsert(new TextPosition(pos.Line, TextNavigation.LeadingWhitespace(line)));
                break;
            case 'A':
                EnterInsert(pos with { Col = line.Length });
                break;
            case 'o':
            {
                var indent = line[..TextNavigation.LeadingWhitespace(line)];
                _s.Replace(TextRange.Empty(pos with { Col = line.Length }), "\n" + indent);
                _changed = true;
                EnterInsert(_s.Caret);
                break;
            }
            case 'O':
            {
                var indent = line[..TextNavigation.LeadingWhitespace(line)];
                _s.Replace(TextRange.Empty(pos with { Col = 0 }), indent + "\n");
                _changed = true;
                EnterInsert(new TextPosition(pos.Line, indent.Length));
                break;
            }
            case 'x':
                n = TakeCount();
                if (line.Length > 0)
                    CharOperator('d', new TextRange(pos, pos with { Col = Math.Min(line.Length, pos.Col + n) }));
                break;
            case 'X':
                n = TakeCount();
                if (pos.Col > 0)
                    CharOperator('d', new TextRange(pos with { Col = Math.Max(0, pos.Col - n) }, pos));
                break;
            case 'D':
            case 'C':
            {
                n = TakeCount();
                int last = Math.Min(pos.Line + n - 1, Doc.LineCount - 1);
                CharOperator(char.ToLower(c), new TextRange(pos, new TextPosition(last, Doc.LineLength(last))));
                break;
            }
            case 's':
                n = TakeCount();
                CharOperator('c', new TextRange(pos, pos with { Col = Math.Min(line.Length, pos.Col + n) }));
                break;
            case 'S':
            case 'Y':
                n = TakeCount();
                LinewiseOperator(c == 'S' ? 'c' : 'y', pos.Line, Math.Min(pos.Line + n - 1, Doc.LineCount - 1));
                break;
            case 'p':
            case 'P':
                Paste(c == 'p', TakeCount());
                break;
            case 'u':
                n = TakeCount();
                for (int i = 0; i < n; i++)
                    _s.Undo();
                AfterUndo();
                break;
            case 'J':
                JoinLines(pos.Line, Math.Max(2, TakeCount()));
                break;
            case '~':
                n = TakeCount();
                if (line.Length > 0)
                {
                    var range = new TextRange(pos, pos with { Col = Math.Min(line.Length, pos.Col + n) });
                    ToggleCase(range);
                    _s.SetCaret(range.End);
                }
                break;
            case 'v':
                StartVisual(VimMode.Visual);
                break;
            case 'V':
                StartVisual(VimMode.VisualLine);
                break;
            case '.':
                Repeat(TakeCount());
                return;
            case ':':
            case '/':
            case '?':
                StartCommandLine(c);
                return;
        }
        ResetParser();
    }

    void VisualCommand(char c)
    {
        var (range, linewise) = VisualRange();
        switch (c)
        {
            case 'd':
            case 'x':
            case 'y':
            case 'c':
            case 's':
            case '>':
            case '<':
                char op = c is 'x' ? 'd' : c is 's' ? 'c' : c;
                if (linewise)
                    LinewiseOperator(op, range.Start.Line, range.End.Line);
                else
                    CharOperator(op, range);
                break;
            case 'D':
            case 'X':
            case 'Y':
            case 'C':
            case 'S':
            case 'R':
                LinewiseOperator(c is 'Y' ? 'y' : c is 'C' or 'S' or 'R' ? 'c' : 'd', range.Start.Line, range.End.Line);
                break;
            case 'J':
                JoinLines(range.Start.Line, Math.Max(2, range.End.Line - range.Start.Line + 1));
                ExitVisual(_s.Caret);
                break;
            case '~':
            case 'u':
            case 'U':
                ToggleCase(range, c == 'u' ? char.ToLower : c == 'U' ? char.ToUpper : null);
                ExitVisual(range.Start);
                break;
            case 'p':
            case 'P':
            {
                var text = _s.Clipboard.GetText();
                if (linewise && !text.EndsWith('\n'))
                    text += "\n";
                if (!linewise && text.EndsWith('\n'))
                    text = text[..^1];
                _s.Replace(range, text);
                _changed = true;
                ExitVisual(range.Start);
                break;
            }
            case 'o':
                (_visualAnchor, _visualCaret) = (_visualCaret, _visualAnchor);
                UpdateVisualSelection();
                break;
            case 'v':
                if (Mode == VimMode.Visual)
                    ExitVisual(_visualCaret);
                else
                    StartVisual(VimMode.Visual, keepAnchor: true);
                break;
            case 'V':
                if (Mode == VimMode.VisualLine)
                    ExitVisual(_visualCaret);
                else
                    StartVisual(VimMode.VisualLine, keepAnchor: true);
                break;
            case ':':
            case '/':
            case '?':
                StartCommandLine(c);
                return;
        }
        ResetParser();
    }

    void AfterUndo()
    {
        if (IsVisual)
            SetMode(VimMode.Normal);
        _s.SetCaret(_s.Selection.Start);
    }

    void Repeat(int count)
    {
        if (_lastChange.Count == 0)
            return;
        var keys = _lastChange;
        var insert = _lastInsert;
        var undoId = _commandUndoId;
        _replaying = true;
        try
        {
            for (int i = 0; i < count; i++)
            {
                foreach (var input in keys)
                    Dispatch(input);
                if (Mode == VimMode.Insert)
                {
                    if (insert.Length > 0)
                        _s.InsertText(insert);
                    LeaveInsertMode();
                }
            }
        }
        finally
        {
            _replaying = false;
            _changed = false;
            _commandUndoId = undoId;
        }
        ResetParser();
    }

    #endregion

    #region Modes

    void SetMode(VimMode mode)
    {
        if (mode == Mode)
            return;
        Mode = mode;
        ModeChanged?.Invoke(mode);
    }

    void EnterInsert(TextPosition pos)
    {
        _s.SetCaret(pos);
        _changed = true;
        SetMode(VimMode.Insert);
    }

    void LeaveInsertMode()
    {
        if (_recordInsert && !_replaying)
        {
            var caret = _s.Caret;
            _lastInsert = caret > _insertStart ? Doc.GetText(new TextRange(_insertStart, caret)) : "";
        }
        _recordInsert = false;
        _s.History.SquashSince(_insertUndoId);
        SetMode(VimMode.Normal);
        var pos = _s.Caret;
        _s.SetCaret(pos.Col > 0 ? pos with { Col = pos.Col - 1 } : pos);
    }

    void StartVisual(VimMode mode, bool keepAnchor = false)
    {
        if (!keepAnchor)
        {
            _visualAnchor = _s.Caret;
            _visualCaret = _s.Caret;
        }
        SetMode(mode);
        UpdateVisualSelection();
    }

    void ExitVisual(TextPosition caret)
    {
        if (IsVisual)
            SetMode(VimMode.Normal);
        _s.SetCaret(caret);
    }

    /// <summary>Selected range with exclusive end, and whether visual line mode is active.</summary>
    (TextRange Range, bool Linewise) VisualRange()
    {
        var start = TextPosition.Min(_visualAnchor, _visualCaret);
        var end = TextPosition.Max(_visualAnchor, _visualCaret);
        if (Mode == VimMode.VisualLine)
            return (new TextRange(new TextPosition(start.Line, 0), new TextPosition(end.Line, Doc.LineLength(end.Line))), true);
        var endExclusive = end.Col < Doc.LineLength(end.Line) ? end with { Col = end.Col + 1 } : Doc.MoveChars(end, 1);
        return (new TextRange(start, endExclusive), false);
    }

    void UpdateVisualSelection()
    {
        Selection sel;
        if (Mode == VimMode.VisualLine)
        {
            int a = _visualAnchor.Line, c = _visualCaret.Line;
            int first = Math.Min(a, c), last = Math.Max(a, c);
            var start = new TextPosition(first, 0);
            var end = last + 1 < Doc.LineCount ? new TextPosition(last + 1, 0) : new TextPosition(last, Doc.LineLength(last));
            sel = c >= a ? new Selection(start, end) : new Selection(end, start);
        }
        else
        {
            var (range, _) = VisualRange();
            sel = _visualCaret >= _visualAnchor ? new Selection(range.Start, range.End) : new Selection(range.End, range.Start);
        }
        _s.Selection = sel;
        _visualSelection = _s.Selection;
    }

    void ClampNormalCaret()
    {
        var pos = _s.Caret;
        int len = Doc.LineLength(pos.Line);
        if (len > 0 && pos.Col >= len)
            _s.SetCaret(pos with { Col = len - 1 });
    }

    void StartCommandLine(char type)
    {
        _modeBeforeCommandLine = Mode;
        _commandLineType = type;
        _commandLineOperator = type == ':' ? '\0' : _operator;
        _commandLine.Clear();
        ResetParser();
        SetMode(VimMode.CommandLine);
    }

    void HandleCommandLineKey(VimKey key)
    {
        switch (key)
        {
            case VimKey.Escape:
                SetMode(_modeBeforeCommandLine);
                break;
            case VimKey.Backspace:
                if (_commandLine.Length == 0)
                    SetMode(_modeBeforeCommandLine);
                else
                    _commandLine.Length--;
                break;
            case VimKey.Enter:
                SetMode(_modeBeforeCommandLine);
                ExecuteCommandLine(_commandLineType, _commandLine.ToString());
                break;
        }
    }

    void ExecuteCommandLine(char type, string text)
    {
        _commandUndoId = _s.History.TopId;
        Message = "";
        if (type == ':')
        {
            var cmd = text.Trim();
            if (cmd.Length == 0)
                return;
            if (int.TryParse(cmd, out int line))
            {
                MoveTo(FirstNonBlank(Math.Clamp(line - 1, 0, Doc.LineCount - 1)));
                return;
            }
            Message = ExCommandHandler?.Invoke(cmd) ?? $"Not an editor command: {cmd}";
            return;
        }

        bool forward = type == '/';
        if (text.Length > 0)
            _lastSearch = text;
        _lastSearchForward = forward;
        _lastSearchWholeWord = false;
        _operator = _commandLineOperator;
        ApplyMotion(Search(_lastSearch, forward, false), MotionKind.Exclusive);
        if (Mode == VimMode.Insert)
            _insertUndoId = _commandUndoId;
        else
            _s.History.SquashSince(_commandUndoId);
        if (Mode == VimMode.Normal)
            ClampNormalCaret();
    }

    #endregion
}
