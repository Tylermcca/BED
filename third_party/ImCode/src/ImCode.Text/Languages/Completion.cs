using ImCode.Text;

namespace ImCode.Languages;

public sealed record CompletionItem(string Label, TokenType Kind = TokenType.Default, string? Detail = null, string? InsertText = null)
{
    public string Text => InsertText ?? Label;
}

/// <summary>Completion candidates, accepting one replaces <see cref="Range"/> by its text.</summary>
public sealed record CompletionList(TextRange Range, IReadOnlyList<CompletionItem> Items);
