using System.Text;

namespace ImCode.Text;

/// <summary>
/// Line based text buffer. Line separators are normalized to '\n' and not stored.
/// All modifications go through <see cref="Replace"/>, which raises <see cref="Changed"/>.
/// </summary>
public sealed class TextDocument
{
    readonly List<string> _lines = [""];
    string? _text;
    int _maxLineLength = -1;

    public event Action<TextDocument, TextChange>? Changed;

    public TextDocument(string text = "")
    {
        if (text.Length > 0)
            Replace(FullRange, text);
        Version = 0;
    }

    /// <summary>Incremented on every change.</summary>
    public int Version { get; private set; }

    public int LineCount => _lines.Count;
    public IReadOnlyList<string> Lines => _lines;
    public string this[int line] => _lines[line];
    public int LineLength(int line) => _lines[line].Length;

    public TextPosition Start => TextPosition.Zero;
    public TextPosition End => new(_lines.Count - 1, _lines[^1].Length);
    public TextRange FullRange => new(Start, End);

    public int MaxLineLength
    {
        get
        {
            if (_maxLineLength < 0)
                foreach (var line in _lines)
                    _maxLineLength = Math.Max(_maxLineLength, line.Length);
            return _maxLineLength;
        }
    }

    public string Text
    {
        get => _text ??= string.Join('\n', _lines);
        set => Replace(FullRange, value);
    }

    /// <summary>Character at pos, '\n' at the end of a line.</summary>
    public char CharAt(TextPosition pos)
    {
        var line = _lines[pos.Line];
        return pos.Col >= line.Length ? '\n' : line[pos.Col];
    }

    public TextPosition Clamp(TextPosition pos)
    {
        if (pos.Line < 0)
            return Start;
        if (pos.Line >= _lines.Count)
            return End;
        return new TextPosition(pos.Line, Math.Clamp(pos.Col, 0, _lines[pos.Line].Length));
    }

    public TextRange Clamp(TextRange range) => new(Clamp(range.Start), Clamp(range.End));

    public string GetText(TextRange range)
    {
        range = Clamp(range.Normalized);
        var (start, end) = (range.Start, range.End);
        if (start.Line == end.Line)
            return _lines[start.Line].Substring(start.Col, end.Col - start.Col);

        var sb = new StringBuilder();
        sb.Append(_lines[start.Line], start.Col, _lines[start.Line].Length - start.Col);
        for (int i = start.Line + 1; i < end.Line; i++)
            sb.Append('\n').Append(_lines[i]);
        sb.Append('\n').Append(_lines[end.Line], 0, end.Col);
        return sb.ToString();
    }

    public static string NormalizeLineEndings(string text) =>
        text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;

    /// <summary>Replaces the text in range and returns the end position of the inserted text.</summary>
    public TextPosition Replace(TextRange range, string text)
    {
        range = Clamp(range.Normalized);
        text = NormalizeLineEndings(text);
        var (start, end) = (range.Start, range.End);
        if (range.IsEmpty && text.Length == 0)
            return start;

        var oldText = GetText(range);
        var prefix = _lines[start.Line][..start.Col];
        var suffix = _lines[end.Line][end.Col..];
        var parts = text.Split('\n');

        TextPosition newEnd;
        if (parts.Length == 1)
            newEnd = new TextPosition(start.Line, start.Col + parts[0].Length);
        else
            newEnd = new TextPosition(start.Line + parts.Length - 1, parts[^1].Length);

        parts[0] = prefix + parts[0];
        parts[^1] += suffix;

        if (parts.Length == end.Line - start.Line + 1)
        {
            for (int i = 0; i < parts.Length; i++)
                _lines[start.Line + i] = parts[i];
        }
        else
        {
            _lines.RemoveRange(start.Line, end.Line - start.Line + 1);
            _lines.InsertRange(start.Line, parts);
        }

        _text = null;
        _maxLineLength = -1;
        Version++;
        Changed?.Invoke(this, new TextChange(start, end, newEnd, oldText, text));
        return newEnd;
    }

    public TextPosition Insert(TextPosition pos, string text) => Replace(TextRange.Empty(pos), text);
    public void Delete(TextRange range) => Replace(range, "");
}
