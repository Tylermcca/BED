namespace ImCode.Languages;

/// <summary>Line based Markdown highlighting: headings, lists, quotes, fenced code and inline spans.</summary>
public sealed class MarkdownLanguage : Language
{
    const int StateFence = 1;

    public override string Name => "Markdown";
    public override IReadOnlyList<string> FileExtensions => [".md", ".markdown"];

    public override int Tokenize(string line, int state, List<Token> tokens)
    {
        var trimmed = line.AsSpan().TrimStart();
        int indent = line.Length - trimmed.Length;

        if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
        {
            tokens.Add(new Token(0, line.Length, TokenType.MarkupCode));
            return state == StateFence ? 0 : StateFence;
        }
        if (state == StateFence)
        {
            if (line.Length > 0)
                tokens.Add(new Token(0, line.Length, TokenType.MarkupCode));
            return StateFence;
        }
        if (trimmed.IsEmpty)
            return 0;

        if (trimmed[0] == '#')
        {
            tokens.Add(new Token(indent, trimmed.Length, TokenType.MarkupHeading));
            return 0;
        }
        if (trimmed[0] == '>')
        {
            tokens.Add(new Token(indent, trimmed.Length, TokenType.MarkupQuote));
            return 0;
        }

        int i = indent;
        int marker = ListMarkerLength(trimmed);
        if (marker > 0)
        {
            tokens.Add(new Token(i, marker, TokenType.MarkupList));
            i += marker;
            var rest = line.AsSpan(i);
            if (rest.StartsWith("[ ] ") || rest.StartsWith("[x] ") || rest.StartsWith("[X] "))
            {
                tokens.Add(new Token(i, 3, TokenType.MarkupList));
                i += 3;
            }
        }
        TokenizeInline(line, i, tokens);
        return 0;
    }

    static int ListMarkerLength(ReadOnlySpan<char> s)
    {
        if (s.Length >= 2 && (s[0] == '-' || s[0] == '*' || s[0] == '+') && s[1] == ' ')
            return 2;
        int d = 0;
        while (d < s.Length && char.IsDigit(s[d]))
            d++;
        if (d > 0 && d + 1 < s.Length && (s[d] == '.' || s[d] == ')') && s[d + 1] == ' ')
            return d + 2;
        return 0;
    }

    static void TokenizeInline(string line, int i, List<Token> tokens)
    {
        while (i < line.Length)
        {
            char c = line[i];
            int end = -1;
            TokenType type = TokenType.Default;
            if (c == '`')
            {
                end = line.IndexOf('`', i + 1);
                type = TokenType.MarkupCode;
            }
            else if ((c == '*' || c == '_') && i + 1 < line.Length && line[i + 1] == c)
            {
                end = line.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal);
                if (end >= 0)
                    end++;
                type = TokenType.MarkupBold;
            }
            else if ((c == '*' || c == '_') && i + 1 < line.Length && !char.IsWhiteSpace(line[i + 1]) && (i == 0 || !char.IsLetterOrDigit(line[i - 1])))
            {
                end = line.IndexOf(c, i + 1);
                type = TokenType.MarkupItalic;
            }
            else if (c == '[')
            {
                int close = line.IndexOf("](", i + 1, StringComparison.Ordinal);
                if (close >= 0)
                    end = line.IndexOf(')', close + 2);
                type = TokenType.MarkupLink;
            }

            if (end < 0)
            {
                i++;
                continue;
            }
            tokens.Add(new Token(i, end + 1 - i, type));
            i = end + 1;
        }
    }

    public override double MatchScore(string text)
    {
        int lines = 0;
        double score = 0;
        foreach (var raw in text.AsSpan().EnumerateLines())
        {
            var line = raw.TrimStart();
            if (line.IsEmpty)
                continue;
            lines++;
            if (line.StartsWith("#") && line.TrimStart('#').StartsWith(" "))
                score += 1;
            else if (line.StartsWith("- [") || line.StartsWith("* [") || line.StartsWith("```"))
                score += 1;
            else if (ListMarkerLength(line) > 0)
                score += 0.7;
        }
        return lines == 0 ? 0 : score / lines;
    }
}
