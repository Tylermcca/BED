using Brutal.ImGuiApi;
using ImCode.Languages;

namespace ImCode;

public sealed class ColorTheme
{
    readonly ImColor8[] _tokenColors = new ImColor8[Enum.GetValues<TokenType>().Length];

    public ImColor8 Background { get; set; }
    public ImColor8 Text { get; set; }
    public ImColor8 CurrentLine { get; set; }
    public ImColor8 Selection { get; set; }
    public ImColor8 SearchMatch { get; set; }
    public ImColor8 Caret { get; set; }
    public ImColor8 BlockCaret { get; set; }
    public ImColor8 GutterBackground { get; set; }
    public ImColor8 LineNumber { get; set; }
    public ImColor8 ActiveLineNumber { get; set; }
    public ImColor8 Error { get; set; }
    public ImColor8 Warning { get; set; }
    public ImColor8 Info { get; set; }
    public ImColor8 PopupBackground { get; set; }
    public ImColor8 PopupSelection { get; set; }
    public ImColor8 PopupDetail { get; set; }

    /// <summary>Color of a token type, <see cref="Text"/> if not set.</summary>
    public ImColor8 this[TokenType type]
    {
        get
        {
            var c = _tokenColors[(int)type];
            return c.AsUint() == 0 ? Text : c;
        }
        set => _tokenColors[(int)type] = value;
    }

    public ImColor8 this[DiagnosticSeverity severity] => severity switch
    {
        DiagnosticSeverity.Error => Error,
        DiagnosticSeverity.Warning => Warning,
        _ => Info,
    };

    /// <summary>Color from 0xRRGGBB.</summary>
    public static ImColor8 Rgb(uint rgb, byte alpha = 255) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, alpha);

    void SetTokens(params (TokenType Type, uint Rgb)[] colors)
    {
        foreach (var (type, rgb) in colors)
            this[type] = Rgb(rgb);
    }

    public static ColorTheme Dark()
    {
        var theme = new ColorTheme
        {
            Background = Rgb(0x1E1E1E),
            Text = Rgb(0xD4D4D4),
            CurrentLine = Rgb(0xFFFFFF, 12),
            Selection = Rgb(0x264F78),
            SearchMatch = Rgb(0x623315, 200),
            Caret = Rgb(0xF0F0F0),
            BlockCaret = Rgb(0xF0F0F0, 110),
            GutterBackground = Rgb(0x1E1E1E),
            LineNumber = Rgb(0x6E7681),
            ActiveLineNumber = Rgb(0xC6C6C6),
            Error = Rgb(0xF14C4C),
            Warning = Rgb(0xCCA700),
            Info = Rgb(0x3794FF),
            PopupBackground = Rgb(0x252526),
            PopupSelection = Rgb(0x04395E),
            PopupDetail = Rgb(0x8B8B8B),
        };
        theme.SetTokens(
            (TokenType.Namespace, 0x4EC9B0), (TokenType.Type, 0x4EC9B0), (TokenType.Class, 0x4EC9B0),
            (TokenType.Enum, 0xB8D7A3), (TokenType.Interface, 0xB8D7A3), (TokenType.Struct, 0x86C691),
            (TokenType.TypeParameter, 0xB8D7A3), (TokenType.Parameter, 0x9CDCFE), (TokenType.Variable, 0x9CDCFE),
            (TokenType.Property, 0x9CDCFE), (TokenType.EnumMember, 0x4FC1FF), (TokenType.Event, 0x9CDCFE),
            (TokenType.Function, 0xDCDCAA), (TokenType.Method, 0xDCDCAA), (TokenType.Macro, 0x9B9B9B),
            (TokenType.Keyword, 0x569CD6), (TokenType.Modifier, 0x569CD6), (TokenType.Comment, 0x6A9955),
            (TokenType.String, 0xCE9178), (TokenType.Number, 0xB5CEA8), (TokenType.Regexp, 0xD16969),
            (TokenType.Operator, 0xD4D4D4), (TokenType.Decorator, 0xC586C0), (TokenType.Control, 0xC586C0),
            (TokenType.Punctuation, 0xD4D4D4), (TokenType.Constant, 0x569CD6),
            (TokenType.MarkupHeading, 0x569CD6), (TokenType.MarkupQuote, 0x6A9955), (TokenType.MarkupList, 0x6796E6),
            (TokenType.MarkupCode, 0xCE9178), (TokenType.MarkupBold, 0xDCDCAA), (TokenType.MarkupItalic, 0xC586C0),
            (TokenType.MarkupLink, 0x4FC1FF));
        return theme;
    }

    public static ColorTheme Light()
    {
        var theme = new ColorTheme
        {
            Background = Rgb(0xFFFFFF),
            Text = Rgb(0x1E1E1E),
            CurrentLine = Rgb(0x000000, 12),
            Selection = Rgb(0xADD6FF),
            SearchMatch = Rgb(0xF8C9AB, 200),
            Caret = Rgb(0x000000),
            BlockCaret = Rgb(0x000000, 80),
            GutterBackground = Rgb(0xFFFFFF),
            LineNumber = Rgb(0x8C8C8C),
            ActiveLineNumber = Rgb(0x0B216F),
            Error = Rgb(0xE51400),
            Warning = Rgb(0xBF8803),
            Info = Rgb(0x1A85FF),
            PopupBackground = Rgb(0xF3F3F3),
            PopupSelection = Rgb(0xC4D9F1),
            PopupDetail = Rgb(0x717171),
        };
        theme.SetTokens(
            (TokenType.Namespace, 0x267F99), (TokenType.Type, 0x267F99), (TokenType.Class, 0x267F99),
            (TokenType.Enum, 0x267F99), (TokenType.Interface, 0x267F99), (TokenType.Struct, 0x267F99),
            (TokenType.TypeParameter, 0x267F99), (TokenType.Parameter, 0x001080), (TokenType.Variable, 0x001080),
            (TokenType.Property, 0x001080), (TokenType.EnumMember, 0x0070C1), (TokenType.Event, 0x001080),
            (TokenType.Function, 0x795E26), (TokenType.Method, 0x795E26), (TokenType.Macro, 0x808080),
            (TokenType.Keyword, 0x0000FF), (TokenType.Modifier, 0x0000FF), (TokenType.Comment, 0x008000),
            (TokenType.String, 0xA31515), (TokenType.Number, 0x098658), (TokenType.Regexp, 0x811F3F),
            (TokenType.Operator, 0x1E1E1E), (TokenType.Decorator, 0xAF00DB), (TokenType.Control, 0xAF00DB),
            (TokenType.Punctuation, 0x1E1E1E), (TokenType.Constant, 0x0000FF),
            (TokenType.MarkupHeading, 0x800000), (TokenType.MarkupQuote, 0x008000), (TokenType.MarkupList, 0x0451A5),
            (TokenType.MarkupCode, 0xA31515), (TokenType.MarkupBold, 0x000080), (TokenType.MarkupItalic, 0x800080),
            (TokenType.MarkupLink, 0x0070C1));
        return theme;
    }
}
