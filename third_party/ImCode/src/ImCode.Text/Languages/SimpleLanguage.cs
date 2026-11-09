using ImCode.Text;

namespace ImCode.Languages;

/// <summary>Delimiters of a string literal that may span several lines.</summary>
public sealed record MultiLineString(string Start, string End, bool DoubledEndEscapes = false);

/// <summary>
/// Configurable highlighter for C-like languages: keywords, line and block comments, strings,
/// numbers, operators and function calls. Completion offers keywords and words of the document.
/// </summary>
public class SimpleLanguage(string name) : Language
{
    const int StateNormal = 0;
    const int StateBlockComment = 1;
    const int StateMultiLineString = 2;

    public override string Name => name;
    public override IReadOnlyList<string> FileExtensions => Extensions;
    public override string? LineCommentToken => LineComment;

    public IReadOnlyList<string> Extensions { get; init; } = [];
    public Dictionary<string, TokenType> Keywords { get; init; } = [];
    public string? LineComment { get; init; }
    public (string Start, string End)? BlockComment { get; init; }
    public string StringQuotes { get; init; } = "\"'";
    public char EscapeChar { get; init; } = '\\';

    /// <summary>Characters that may precede a string quote and belong to the string, e.g. '$' in C#.</summary>
    public string StringPrefixes { get; init; } = "";

    /// <summary>Checked before single line strings, longer starts first.</summary>
    public IReadOnlyList<MultiLineString> MultiLineStrings { get; init; } = [];

    /// <summary>Lines starting with this prefix (after whitespace) are tokenized as <see cref="TokenType.Macro"/>.</summary>
    public string? PreprocessorPrefix { get; init; }
    public string OperatorChars { get; init; } = "+-*/%=<>!&|^~?:";

    /// <summary>Identifiers followed by '(' are functions.</summary>
    public bool HighlightFunctionCalls { get; init; } = true;

    /// <summary>Identifiers starting with an upper case letter are types.</summary>
    public bool CapitalizedTypes { get; init; }

    public bool CompleteKeywords { get; init; } = true;
    public bool CompleteDocumentWords { get; init; } = true;
    public int MinCompletionWordLength { get; init; } = 3;

    public override int Tokenize(string line, int state, List<Token> tokens)
    {
        int i = 0;
        int n = line.Length;

        if (state == StateBlockComment)
        {
            int end = line.IndexOf(BlockComment!.Value.End, StringComparison.Ordinal);
            if (end < 0)
            {
                Add(tokens, 0, n, TokenType.Comment);
                return StateBlockComment;
            }
            i = end + BlockComment.Value.End.Length;
            Add(tokens, 0, i, TokenType.Comment);
        }
        else if (state >= StateMultiLineString)
        {
            var ml = MultiLineStrings[state - StateMultiLineString];
            int end = FindMultiLineEnd(line, 0, ml);
            if (end < 0)
            {
                Add(tokens, 0, n, TokenType.String);
                return state;
            }
            i = end;
            Add(tokens, 0, i, TokenType.String);
        }
        else if (PreprocessorPrefix != null)
        {
            int ws = TextNavigation.LeadingWhitespace(line);
            if (string.CompareOrdinal(line, ws, PreprocessorPrefix, 0, PreprocessorPrefix.Length) == 0)
            {
                Add(tokens, ws, n - ws, TokenType.Macro);
                return StateNormal;
            }
        }

        while (i < n)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (LineComment != null && Matches(line, i, LineComment))
            {
                Add(tokens, i, n - i, TokenType.Comment);
                return StateNormal;
            }

            if (BlockComment is var (bStart, bEnd) && Matches(line, i, bStart))
            {
                int end = line.IndexOf(bEnd, i + bStart.Length, StringComparison.Ordinal);
                if (end < 0)
                {
                    Add(tokens, i, n - i, TokenType.Comment);
                    return StateBlockComment;
                }
                Add(tokens, i, end + bEnd.Length - i, TokenType.Comment);
                i = end + bEnd.Length;
                continue;
            }

            int mlIndex = MatchMultiLineStart(line, i);
            if (mlIndex >= 0)
            {
                var ml = MultiLineStrings[mlIndex];
                int end = FindMultiLineEnd(line, i + ml.Start.Length, ml);
                if (end < 0)
                {
                    Add(tokens, i, n - i, TokenType.String);
                    return StateMultiLineString + mlIndex;
                }
                Add(tokens, i, end - i, TokenType.String);
                i = end;
                continue;
            }

            int quote = i;
            while (quote < n && StringPrefixes.Contains(line[quote]))
                quote++;
            if (quote < n && StringQuotes.Contains(line[quote]))
            {
                c = line[quote];
                int end = quote + 1;
                while (end < n && line[end] != c)
                    end += line[end] == EscapeChar ? 2 : 1;
                end = Math.Min(end + 1, n);
                Add(tokens, i, end - i, TokenType.String);
                i = end;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(line[i + 1])))
            {
                int end = i + 1;
                while (end < n && (char.IsLetterOrDigit(line[end]) || line[end] == '_' || (line[end] == '.' && end + 1 < n && char.IsDigit(line[end + 1]))))
                    end++;
                Add(tokens, i, end - i, TokenType.Number);
                i = end;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int end = i + 1;
                while (end < n && IsWordChar(line[end]))
                    end++;
                var type = ClassifyWord(line, i, end);
                if (type != TokenType.Default)
                    Add(tokens, i, end - i, type);
                i = end;
                continue;
            }

            if (OperatorChars.Contains(c))
            {
                int end = i + 1;
                while (end < n && OperatorChars.Contains(line[end]))
                    end++;
                Add(tokens, i, end - i, TokenType.Operator);
                i = end;
                continue;
            }

            i++;
        }
        return StateNormal;
    }

    protected virtual TokenType ClassifyWord(string line, int start, int end)
    {
        var word = line.AsSpan(start, end - start);
        if (Keywords.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out var type))
            return type;
        if (HighlightFunctionCalls)
        {
            int j = end;
            while (j < line.Length && line[j] == ' ')
                j++;
            if (j < line.Length && line[j] == '(')
                return TokenType.Function;
        }
        if (CapitalizedTypes && char.IsUpper(word[0]))
            return TokenType.Type;
        return TokenType.Default;
    }

    static bool Matches(string line, int i, string s) => string.CompareOrdinal(line, i, s, 0, s.Length) == 0;

    int MatchMultiLineStart(string line, int i)
    {
        int best = -1;
        for (int k = 0; k < MultiLineStrings.Count; k++)
            if (Matches(line, i, MultiLineStrings[k].Start) && (best < 0 || MultiLineStrings[k].Start.Length > MultiLineStrings[best].Start.Length))
                best = k;
        return best;
    }

    /// <summary>Index after the closing delimiter, or -1 if the string continues on the next line.</summary>
    static int FindMultiLineEnd(string line, int from, MultiLineString ml)
    {
        int i = from;
        while (true)
        {
            int end = line.IndexOf(ml.End, i, StringComparison.Ordinal);
            if (end < 0)
                return -1;
            if (ml.DoubledEndEscapes && Matches(line, end + ml.End.Length, ml.End))
            {
                i = end + 2 * ml.End.Length;
                continue;
            }
            return end + ml.End.Length;
        }
    }

    public override CompletionList? GetCompletions(TextDocument document, TextPosition caret)
    {
        var line = document[caret.Line];
        int start = caret.Col;
        while (start > 0 && IsWordChar(line[start - 1]))
            start--;
        if (start == caret.Col || (caret.Col < line.Length && IsWordChar(line[caret.Col])))
            return null;
        var prefix = line[start..caret.Col];

        var items = new Dictionary<string, CompletionItem>();
        if (CompleteKeywords)
            foreach (var (word, type) in Keywords)
                if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && word != prefix)
                    items[word] = new CompletionItem(word, type);

        if (CompleteDocumentWords)
        {
            for (int l = 0; l < document.LineCount; l++)
            {
                var text = document[l];
                int i = 0;
                while (i < text.Length)
                {
                    if (!IsWordChar(text[i]))
                    {
                        i++;
                        continue;
                    }
                    int s = i;
                    while (i < text.Length && IsWordChar(text[i]))
                        i++;
                    if (i - s < MinCompletionWordLength || char.IsDigit(text[s]) || (l == caret.Line && s == start))
                        continue;
                    var word = text.AsSpan(s, i - s);
                    if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !word.SequenceEqual(prefix))
                    {
                        var w = word.ToString();
                        items.TryAdd(w, new CompletionItem(w, TokenType.Variable));
                    }
                }
            }
        }

        if (items.Count == 0)
            return null;
        var list = items.Values
            .OrderBy(i => i.Label.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new CompletionList(new TextRange(new TextPosition(caret.Line, start), caret), list);
    }

    static void Add(List<Token> tokens, int start, int length, TokenType type)
    {
        if (length > 0)
            tokens.Add(new Token(start, length, type));
    }
}
