using ImCode.Text;

namespace ImCode.Languages;

/// <summary>
/// Lazily tokenizes the lines of a document. After a change, lines are re-tokenized on demand
/// until the line state matches the previously computed one again.
/// </summary>
public sealed class SyntaxHighlighter
{
    struct LineEntry
    {
        public Token[]? Tokens;
        public int StartState;
        public int EndState;
    }

    readonly TextDocument _document;
    readonly List<LineEntry> _lines = [];
    readonly List<Token> _buffer = [];
    Language _language;
    int _firstStale;

    public SyntaxHighlighter(TextDocument document, Language? language = null)
    {
        _document = document;
        _language = language ?? PlainTextLanguage.Instance;
        _lines.AddRange(new LineEntry[document.LineCount]);
        document.Changed += OnChanged;
    }

    public Language Language
    {
        get => _language;
        set
        {
            if (value == _language)
                return;
            _language = value;
            Invalidate();
        }
    }

    public void Invalidate()
    {
        for (int i = 0; i < _lines.Count; i++)
            _lines[i] = default;
        _firstStale = 0;
    }

    public void Detach() => _document.Changed -= OnChanged;

    public ReadOnlySpan<Token> GetTokens(int line)
    {
        for (int i = _firstStale; i <= line; i++)
        {
            int startState = i == 0 ? 0 : _lines[i - 1].EndState;
            var entry = _lines[i];
            if (entry.Tokens != null && entry.StartState == startState)
                continue;
            _buffer.Clear();
            int endState = _language.Tokenize(_document[i], startState, _buffer);
            _lines[i] = new LineEntry { Tokens = [.. _buffer], StartState = startState, EndState = endState };
        }
        _firstStale = Math.Max(_firstStale, line + 1);
        return _lines[line].Tokens;
    }

    /// <summary>Token containing pos, or null.</summary>
    public Token? GetTokenAt(TextPosition pos)
    {
        if (pos.Line < 0 || pos.Line >= _document.LineCount)
            return null;
        foreach (var t in GetTokens(pos.Line))
            if (pos.Col >= t.Start && pos.Col < t.End)
                return t;
        return null;
    }

    void OnChanged(TextDocument doc, TextChange change)
    {
        int start = change.Start.Line;
        _lines.RemoveRange(start, change.OldLineCount);
        _lines.InsertRange(start, new LineEntry[change.NewLineCount]);
        _firstStale = Math.Min(_firstStale, start);
    }
}
