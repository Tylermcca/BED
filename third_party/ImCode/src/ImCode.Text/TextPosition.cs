namespace ImCode.Text;

/// <summary>Zero based line and column (UTF-16 code unit index into the line).</summary>
public readonly record struct TextPosition(int Line, int Col) : IComparable<TextPosition>
{
    public static readonly TextPosition Zero = new(0, 0);

    public int CompareTo(TextPosition other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Col.CompareTo(other.Col);

    public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextPosition a, TextPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextPosition a, TextPosition b) => a.CompareTo(b) >= 0;

    public static TextPosition Min(TextPosition a, TextPosition b) => a <= b ? a : b;
    public static TextPosition Max(TextPosition a, TextPosition b) => a >= b ? a : b;

    public override string ToString() => $"({Line}, {Col})";
}

/// <summary>Half open range [Start, End). Not necessarily normalized, see <see cref="Normalized"/>.</summary>
public readonly record struct TextRange(TextPosition Start, TextPosition End)
{
    public bool IsEmpty => Start == End;
    public bool IsMultiLine => Start.Line != End.Line;
    public TextRange Normalized => Start <= End ? this : new TextRange(End, Start);

    public static TextRange Empty(TextPosition pos) => new(pos, pos);

    public bool Contains(TextPosition pos)
    {
        var r = Normalized;
        return r.Start <= pos && pos < r.End;
    }

    public override string ToString() => $"[{Start} - {End}]";
}

/// <summary>Selection with a fixed anchor and a moving caret. Empty if both are equal.</summary>
public readonly record struct Selection(TextPosition Anchor, TextPosition Caret)
{
    public bool IsEmpty => Anchor == Caret;
    public TextRange Range => new TextRange(Anchor, Caret).Normalized;
    public TextPosition Start => TextPosition.Min(Anchor, Caret);
    public TextPosition End => TextPosition.Max(Anchor, Caret);

    public static Selection Collapsed(TextPosition pos) => new(pos, pos);
    public static Selection FromRange(TextRange range) => new(range.Start, range.End);

    public override string ToString() => $"[{Anchor} -> {Caret}]";
}
