using ImCode.Text;

namespace ImCode.Languages;

/// <summary>
/// Language support for the editor: tokenization for highlighting plus optional hover,
/// completion and diagnostics. Implementations must be cheap, they are called on the UI thread.
/// </summary>
public abstract class Language
{
    public abstract string Name { get; }

    /// <summary>File extensions including the dot, lower case.</summary>
    public virtual IReadOnlyList<string> FileExtensions => [];

    public virtual Func<char, bool> IsWordChar => CharClass.IsWordChar;

    /// <summary>Prefix used by the toggle comment command, null if the language has no line comments.</summary>
    public virtual string? LineCommentToken => null;

    /// <summary>
    /// Appends the tokens of a line (sorted, non overlapping) and returns the state at its end.
    /// <paramref name="state"/> is the state at the end of the previous line, 0 for the first line.
    /// </summary>
    public abstract int Tokenize(string line, int state, List<Token> tokens);

    /// <summary>Plain text shown when hovering pos, or null.</summary>
    public virtual string? GetHover(TextDocument document, TextPosition pos) => null;

    /// <summary>Completion candidates at the caret, or null.</summary>
    public virtual CompletionList? GetCompletions(TextDocument document, TextPosition caret) => null;

    /// <summary>Diagnostics of the whole document, or null if the language does not produce any.</summary>
    public virtual IReadOnlyList<Diagnostic>? GetDiagnostics(TextDocument document) => null;

    /// <summary>Likelihood in [0, 1] that the text is written in this language, used by detection.</summary>
    public virtual double MatchScore(string text) => 0;

    public override string ToString() => Name;
}

public sealed class PlainTextLanguage : Language
{
    public static readonly PlainTextLanguage Instance = new();

    public override string Name => "Plain Text";
    public override IReadOnlyList<string> FileExtensions => [".txt"];
    public override int Tokenize(string line, int state, List<Token> tokens) => 0;
}
