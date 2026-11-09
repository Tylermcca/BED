namespace ImCode.Languages;

/// <summary>Token categories, the first block follows the LSP semantic token types.</summary>
public enum TokenType : byte
{
    Default,
    Namespace,
    Type,
    Class,
    Enum,
    Interface,
    Struct,
    TypeParameter,
    Parameter,
    Variable,
    Property,
    EnumMember,
    Event,
    Function,
    Method,
    Macro,
    Keyword,
    Modifier,
    Comment,
    String,
    Number,
    Regexp,
    Operator,
    Decorator,
    Control,
    Punctuation,
    Constant,

    MarkupHeading,
    MarkupQuote,
    MarkupList,
    MarkupCode,
    MarkupBold,
    MarkupItalic,
    MarkupLink,
}

/// <summary>Token of a line, Start and Length in characters of that line.</summary>
public readonly record struct Token(int Start, int Length, TokenType Type)
{
    public int End => Start + Length;
}
