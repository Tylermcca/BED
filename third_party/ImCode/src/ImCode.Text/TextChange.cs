namespace ImCode.Text;

/// <summary>
/// Replacement of the text in [Start, OldEnd) by NewText, which ends at NewEnd.
/// On line level, lines Start.Line..OldEnd.Line were replaced by lines Start.Line..NewEnd.Line.
/// </summary>
public sealed record TextChange(TextPosition Start, TextPosition OldEnd, TextPosition NewEnd, string OldText, string NewText)
{
    public int OldLineCount => OldEnd.Line - Start.Line + 1;
    public int NewLineCount => NewEnd.Line - Start.Line + 1;
    public int LineDelta => NewEnd.Line - OldEnd.Line;

    public TextChange Inverse() => new(Start, NewEnd, OldEnd, NewText, OldText);

    /// <summary>Maps a position before the change to the corresponding position after it.</summary>
    public TextPosition Transform(TextPosition pos)
    {
        if (pos <= Start && !(pos == Start && OldEnd == Start))
            return pos;
        if (pos < OldEnd)
            return NewEnd;
        if (pos.Line == OldEnd.Line)
            return new TextPosition(NewEnd.Line, NewEnd.Col + pos.Col - OldEnd.Col);
        return new TextPosition(pos.Line + LineDelta, pos.Col);
    }
}
