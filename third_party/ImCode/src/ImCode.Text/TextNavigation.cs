namespace ImCode.Text;

public enum MoveToken
{
    Char,
    Line,
    WordBeginning,
    WordEnd,
}

public readonly record struct MoveAction(MoveToken Token = MoveToken.Char, bool Forward = true, int Amount = 1)
{
    public int SignedAmount => Forward ? Amount : -Amount;
}

public static class CharClass
{
    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}

/// <summary>Position arithmetic on a document. Character moves wrap across line ends.</summary>
public static class TextNavigation
{
    /// <summary>Wraps a column that is out of the line to the previous/next line and clamps to the document.</summary>
    public static TextPosition Wrap(this TextDocument doc, TextPosition pos)
    {
        var (line, col) = (pos.Line, pos.Col);
        if (col < 0 && line > 0)
        {
            line--;
            col = doc.LineLength(line);
        }
        else if (line >= 0 && line < doc.LineCount && col > doc.LineLength(line) && line < doc.LineCount - 1)
        {
            line++;
            col = 0;
        }
        return doc.Clamp(new TextPosition(line, col));
    }

    public static TextPosition MoveChars(this TextDocument doc, TextPosition pos, int amount)
    {
        int dir = Math.Sign(amount);
        for (int i = Math.Abs(amount); i > 0; i--)
        {
            var next = doc.Wrap(new TextPosition(pos.Line, pos.Col + dir));
            if (next == pos)
                break;
            pos = next;
        }
        return pos;
    }

    public static TextPosition MoveLines(this TextDocument doc, TextPosition pos, int amount) =>
        doc.Clamp(new TextPosition(Math.Clamp(pos.Line + amount, 0, doc.LineCount - 1), pos.Col));

    public static bool IsWordBeginning(this TextDocument doc, TextPosition pos, Func<char, bool> isWordChar)
    {
        if (pos.Col == 0)
            return true;
        return !isWordChar(doc.CharAt(new TextPosition(pos.Line, pos.Col - 1))) && isWordChar(doc.CharAt(pos));
    }

    public static bool IsWordEnd(this TextDocument doc, TextPosition pos, Func<char, bool> isWordChar)
    {
        if (pos.Col == 0)
            return false;
        if (pos.Col >= doc.LineLength(pos.Line))
            return true;
        return isWordChar(doc.CharAt(new TextPosition(pos.Line, pos.Col - 1))) && !isWordChar(doc.CharAt(pos));
    }

    public static TextPosition FindWordBeginning(this TextDocument doc, TextPosition pos, bool forward, Func<char, bool> isWordChar) =>
        FindBoundary(doc, pos, forward, p => doc.IsWordBeginning(p, isWordChar));

    public static TextPosition FindWordEnd(this TextDocument doc, TextPosition pos, bool forward, Func<char, bool> isWordChar) =>
        FindBoundary(doc, pos, forward, p => doc.IsWordEnd(p, isWordChar));

    static TextPosition FindBoundary(TextDocument doc, TextPosition pos, bool forward, Func<TextPosition, bool> isBoundary)
    {
        int dir = forward ? 1 : -1;
        while (true)
        {
            var next = doc.Wrap(new TextPosition(pos.Line, pos.Col + dir));
            if (next == pos)
                return pos;
            pos = next;
            if (isBoundary(pos))
                return pos;
        }
    }

    /// <summary>Moves until a whitespace character or the end/start of the line.</summary>
    public static TextPosition FindWhitespace(this TextDocument doc, TextPosition pos, bool forward = true)
    {
        var line = doc[pos.Line];
        int dir = forward ? 1 : -1;
        while (pos.Col < line.Length && pos.Col >= 0 && !char.IsWhiteSpace(line[pos.Col]))
        {
            pos = pos with { Col = pos.Col + dir };
            if (pos.Col < 0)
                return doc.Wrap(pos);
        }
        return pos;
    }

    /// <summary>Moves over whitespace within the line, wrapping to the next/previous line at its end.</summary>
    public static TextPosition FindNonWhitespace(this TextDocument doc, TextPosition pos, bool forward = true)
    {
        if (!char.IsWhiteSpace(doc.CharAt(pos)))
            return pos;
        var line = doc[pos.Line];
        int dir = forward ? 1 : -1;
        while (pos.Col < line.Length && pos.Col >= 0 && char.IsWhiteSpace(doc.CharAt(pos)))
            pos = pos with { Col = pos.Col + dir };
        return doc.Wrap(pos);
    }

    public static TextPosition FindNextWord(this TextDocument doc, TextPosition pos, bool forward = true)
    {
        if (char.IsWhiteSpace(doc.CharAt(pos)))
            return doc.FindNonWhitespace(pos, forward);
        return doc.FindNonWhitespace(doc.FindWhitespace(pos, forward), forward);
    }

    public static TextPosition Move(this TextDocument doc, TextPosition pos, MoveAction action, Func<char, bool> isWordChar)
    {
        switch (action.Token)
        {
            case MoveToken.Char:
                return doc.MoveChars(pos, action.SignedAmount);
            case MoveToken.Line:
                return doc.MoveLines(pos, action.SignedAmount);
            case MoveToken.WordBeginning:
                for (int i = 0; i < action.Amount; i++)
                    pos = doc.FindWordBeginning(pos, action.Forward, isWordChar);
                return pos;
            case MoveToken.WordEnd:
                for (int i = 0; i < action.Amount; i++)
                    pos = doc.FindWordEnd(pos, action.Forward, isWordChar);
                return pos;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>Range of the word, whitespace run or punctuation run at pos (double click selection).</summary>
    public static TextRange GetWordAt(this TextDocument doc, TextPosition pos, Func<char, bool> isWordChar)
    {
        pos = doc.Clamp(pos);
        var line = doc[pos.Line];
        if (line.Length == 0)
            return TextRange.Empty(pos);
        int col = Math.Min(pos.Col, line.Length - 1);
        if (pos.Col == line.Length && col > 0 && !isWordChar(line[col]) && isWordChar(line[col - 1]))
            col--;

        Func<char, bool> same = isWordChar(line[col]) ? isWordChar
            : char.IsWhiteSpace(line[col]) ? char.IsWhiteSpace
            : c => !isWordChar(c) && !char.IsWhiteSpace(c);

        int start = col, end = col + 1;
        while (start > 0 && same(line[start - 1]))
            start--;
        while (end < line.Length && same(line[end]))
            end++;
        return new TextRange(new TextPosition(pos.Line, start), new TextPosition(pos.Line, end));
    }

    public static int LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return i;
    }
}
