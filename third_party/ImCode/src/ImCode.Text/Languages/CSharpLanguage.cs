namespace ImCode.Languages;

public static class CSharpLanguage
{
    static readonly string[] ControlKeywords =
    [
        "if", "else", "switch", "case", "default", "for", "foreach", "while", "do", "break", "continue",
        "return", "goto", "yield", "try", "catch", "finally", "throw", "await", "when",
    ];

    static readonly string[] Keywords =
    [
        "class", "struct", "interface", "enum", "record", "namespace", "using", "var", "dynamic", "nameof",
        "checked", "unchecked", "lock", "base", "this", "new", "typeof", "sizeof", "stackalloc", "ref", "out",
        "in", "is", "as", "operator", "delegate", "event", "params", "get", "set", "init", "value", "where",
        "with", "and", "or", "not", "global", "fixed", "implicit", "explicit", "required", "scoped", "file",
    ];

    static readonly string[] Modifiers =
    [
        "public", "private", "protected", "internal", "static", "readonly", "const", "virtual", "override",
        "abstract", "sealed", "partial", "async", "unsafe", "extern", "volatile", "new",
    ];

    static readonly string[] BuiltinTypes =
    [
        "void", "object", "string", "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "char", "nint", "nuint",
    ];

    static readonly string[] Constants = ["true", "false", "null"];

    public static SimpleLanguage Create()
    {
        var keywords = new Dictionary<string, TokenType>();
        foreach (var k in Keywords)
            keywords[k] = TokenType.Keyword;
        foreach (var k in Modifiers)
            keywords[k] = TokenType.Modifier;
        foreach (var k in BuiltinTypes)
            keywords[k] = TokenType.Keyword;
        foreach (var k in ControlKeywords)
            keywords[k] = TokenType.Control;
        foreach (var k in Constants)
            keywords[k] = TokenType.Constant;

        return new CSharpLanguageImpl
        {
            Extensions = [".cs", ".csx"],
            Keywords = keywords,
            LineComment = "//",
            BlockComment = ("/*", "*/"),
            MultiLineStrings =
            [
                new("\"\"\"", "\"\"\""),
                new("$\"\"\"", "\"\"\""),
                new("@\"", "\"", DoubledEndEscapes: true),
                new("$@\"", "\"", DoubledEndEscapes: true),
                new("@$\"", "\"", DoubledEndEscapes: true),
            ],
            StringPrefixes = "$",
            PreprocessorPrefix = "#",
            CapitalizedTypes = true,
        };
    }

    sealed class CSharpLanguageImpl() : SimpleLanguage("C#")
    {
        public override double MatchScore(string text)
        {
            int lines = 0, hits = 0;
            foreach (var raw in text.AsSpan().EnumerateLines())
            {
                var line = raw.Trim();
                if (line.IsEmpty)
                    continue;
                lines++;
                if (line.StartsWith("using ") && line.EndsWith(";") || line.StartsWith("namespace ")
                    || line.StartsWith("public ") || line.StartsWith("private ") || line.StartsWith("internal ")
                    || line.EndsWith(";") || line is "{" or "}" || line.StartsWith("//"))
                    hits++;
            }
            return lines == 0 ? 0 : (double)hits / lines;
        }
    }
}
