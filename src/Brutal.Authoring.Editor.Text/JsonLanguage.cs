using ImCode.Languages;

namespace Brutal.Authoring.Editor.Text;

/// <summary>
/// JSON highlighting for the Text panel's code editor (ImCode's custom-language seam): keys, string values, numbers,
/// <c>true</c>, <c>false</c>, <c>null</c> and punctuation. A JSON string can't span lines, so every line is read on its
/// own and no state carries between lines. It only colours; whether the text is valid JSON is decided on Apply.
/// BORROWED: the token kinds of VS Code's JSON grammar; INVENTED: this small tokenizer.
/// </summary>
public sealed class JsonLanguage : Language
{
    /// <summary>One shared instance: it holds no state.</summary>
    public static readonly JsonLanguage Instance = new();

    /// <inheritdoc />
    public override string Name => "JSON";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [".json"];

    /// <inheritdoc />
    public override int Tokenize(string line, int state, List<Token> tokens)
    {
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            var start = i;
            if (c == '"')
            {
                i++;
                while (i < line.Length && line[i] != '"') i += line[i] == '\\' ? 2 : 1;
                i = Math.Min(i + 1, line.Length);
                var next = i;
                while (next < line.Length && char.IsWhiteSpace(line[next])) next++;
                tokens.Add(new Token(start, i - start, next < line.Length && line[next] == ':' ? TokenType.Property : TokenType.String));
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                i++;
                while (i < line.Length && (char.IsAsciiDigit(line[i]) || line[i] is '.' or 'e' or 'E' or '+' or '-')) i++;
                tokens.Add(new Token(start, i - start, TokenType.Number));
            }
            else if (char.IsAsciiLetter(c))
            {
                while (i < line.Length && char.IsAsciiLetterOrDigit(line[i])) i++;
                var word = line.AsSpan(start, i - start);
                if (word is "true" or "false" or "null") tokens.Add(new Token(start, i - start, TokenType.Constant));
            }
            else
            {
                if (c is '{' or '}' or '[' or ']' or ',' or ':') tokens.Add(new Token(start, 1, TokenType.Punctuation));
                i++;
            }
        }
        return 0;
    }
}
