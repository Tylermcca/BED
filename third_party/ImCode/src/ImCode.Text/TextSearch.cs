namespace ImCode.Text;

public readonly record struct SearchOptions(bool MatchCase = false, bool WholeWord = false, bool Wrap = true);

/// <summary>Single line plain text search.</summary>
public static class TextSearch
{
    /// <summary>
    /// Forward: first match starting at or after from. Backward: last match starting before from.
    /// </summary>
    public static TextRange? Find(this TextDocument doc, string term, TextPosition from, bool forward, SearchOptions options, Func<char, bool>? isWordChar = null)
    {
        if (string.IsNullOrEmpty(term) || term.Contains('\n'))
            return null;
        isWordChar ??= CharClass.IsWordChar;
        from = doc.Clamp(from);
        var result = forward ? FindForward(doc, term, from, doc.End, options, isWordChar) : FindBackward(doc, term, from, TextPosition.Zero, options, isWordChar);
        if (result == null && options.Wrap)
            result = forward ? FindForward(doc, term, TextPosition.Zero, from, options, isWordChar) : FindBackward(doc, term, doc.End, from, options, isWordChar);
        return result;
    }

    public static IEnumerable<TextRange> FindAll(this TextDocument doc, string term, SearchOptions options, Func<char, bool>? isWordChar = null)
    {
        if (string.IsNullOrEmpty(term) || term.Contains('\n'))
            yield break;
        isWordChar ??= CharClass.IsWordChar;
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int l = 0; l < doc.LineCount; l++)
        {
            var line = doc[l];
            int i = 0;
            while ((i = line.IndexOf(term, i, comparison)) >= 0)
            {
                if (!options.WholeWord || IsWholeWord(line, i, term.Length, isWordChar))
                {
                    yield return new TextRange(new TextPosition(l, i), new TextPosition(l, i + term.Length));
                    i += term.Length;
                }
                else
                    i++;
            }
        }
    }

    static TextRange? FindForward(TextDocument doc, string term, TextPosition from, TextPosition limit, SearchOptions options, Func<char, bool> isWordChar)
    {
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int l = from.Line; l <= limit.Line; l++)
        {
            var line = doc[l];
            int i = l == from.Line ? from.Col : 0;
            while (i <= line.Length && (i = line.IndexOf(term, i, comparison)) >= 0)
            {
                var start = new TextPosition(l, i);
                if (start >= limit && limit != doc.End)
                    return null;
                if (!options.WholeWord || IsWholeWord(line, i, term.Length, isWordChar))
                    return new TextRange(start, new TextPosition(l, i + term.Length));
                i++;
            }
        }
        return null;
    }

    static TextRange? FindBackward(TextDocument doc, string term, TextPosition from, TextPosition limit, SearchOptions options, Func<char, bool> isWordChar)
    {
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int l = from.Line; l >= limit.Line; l--)
        {
            var line = doc[l];
            int i = l == from.Line ? from.Col - 1 : line.Length - 1;
            while (i >= 0 && (i = line.LastIndexOf(term, Math.Min(i + term.Length - 1, line.Length - 1), comparison)) >= 0)
            {
                if (l == from.Line && i >= from.Col)
                {
                    i--;
                    continue;
                }
                var start = new TextPosition(l, i);
                if (start < limit)
                    return null;
                if (!options.WholeWord || IsWholeWord(line, i, term.Length, isWordChar))
                    return new TextRange(start, new TextPosition(l, i + term.Length));
                i--;
            }
        }
        return null;
    }

    static bool IsWholeWord(string line, int start, int length, Func<char, bool> isWordChar) =>
        (start == 0 || !isWordChar(line[start - 1])) && (start + length >= line.Length || !isWordChar(line[start + length]));
}
