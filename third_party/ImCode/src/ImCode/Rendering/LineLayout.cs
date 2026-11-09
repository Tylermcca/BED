using System.Text;
using ImCode.Text;

namespace ImCode.Rendering;

/// <summary>
/// Render data of one line: UTF-8 bytes with tabs expanded to spaces and the mapping
/// between character columns and visual (monospace cell) columns.
/// </summary>
public sealed class LineLayout
{
    // null if the line is plain ASCII without tabs: visual column == char column == byte offset
    readonly int[]? _visualCols;
    readonly int[]? _byteOffsets;

    public LineLayout(string line, int tabSize)
    {
        Source = line;
        bool simple = true;
        foreach (var c in line)
            if (c == '\t' || c > 127)
            {
                simple = false;
                break;
            }

        if (simple)
        {
            Utf8 = Encoding.UTF8.GetBytes(line);
            VisualLength = line.Length;
            return;
        }

        _visualCols = new int[line.Length + 1];
        _byteOffsets = new int[line.Length + 1];
        var bytes = new List<byte>(line.Length + 8);
        Span<byte> buf = stackalloc byte[4];
        int vcol = 0;
        for (int i = 0; i < line.Length; i++)
        {
            _visualCols[i] = vcol;
            _byteOffsets[i] = bytes.Count;
            var c = line[i];
            if (c == '\t')
            {
                int n = tabSize - vcol % tabSize;
                for (int k = 0; k < n; k++)
                    bytes.Add((byte)' ');
                vcol += n;
            }
            else if (char.IsHighSurrogate(c) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
            {
                var rune = new Rune(c, line[i + 1]);
                bytes.AddRange(buf[..rune.EncodeToUtf8(buf)]);
                vcol++;
                i++;
                _visualCols[i] = vcol;
                _byteOffsets[i] = bytes.Count;
            }
            else
            {
                var rune = Rune.IsValid(c) ? new Rune(c) : Rune.ReplacementChar;
                bytes.AddRange(buf[..rune.EncodeToUtf8(buf)]);
                vcol++;
            }
        }
        _visualCols[line.Length] = vcol;
        _byteOffsets[line.Length] = bytes.Count;
        Utf8 = [.. bytes];
        VisualLength = vcol;
    }

    public string Source { get; }
    public byte[] Utf8 { get; }
    public int VisualLength { get; }

    public int VisualColumn(int col)
    {
        col = Math.Clamp(col, 0, Source.Length);
        return _visualCols?[col] ?? col;
    }

    public int ByteOffset(int col)
    {
        col = Math.Clamp(col, 0, Source.Length);
        return _byteOffsets?[col] ?? col;
    }

    /// <summary>Character column whose left edge is closest to the visual position (in cells, fractional).</summary>
    public int ColumnFromVisual(float visual)
    {
        if (_visualCols == null)
            return Math.Clamp((int)MathF.Round(visual), 0, Source.Length);
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i <= Source.Length; i++)
        {
            if (i > 0 && i < Source.Length && char.IsHighSurrogate(Source[i - 1]) && char.IsLowSurrogate(Source[i]))
                continue;
            float d = MathF.Abs(_visualCols[i] - visual);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
            else if (_visualCols[i] > visual)
                break;
        }
        return best;
    }
}

/// <summary>Per line layout cache kept in sync with a document.</summary>
public sealed class LineLayoutCache
{
    readonly TextDocument _document;
    readonly List<LineLayout?> _lines = [];
    int _tabSize;
    int _maxVisualLength = -1;

    public LineLayoutCache(TextDocument document, int tabSize)
    {
        _document = document;
        _tabSize = tabSize;
        _lines.AddRange(new LineLayout?[document.LineCount]);
        document.Changed += OnChanged;
    }

    public int TabSize
    {
        get => _tabSize;
        set
        {
            if (value == _tabSize)
                return;
            _tabSize = value;
            Invalidate();
        }
    }

    public LineLayout this[int line] => _lines[line] ??= new LineLayout(_document[line], _tabSize);

    public int MaxVisualLength
    {
        get
        {
            if (_maxVisualLength < 0)
            {
                _maxVisualLength = 0;
                for (int i = 0; i < _lines.Count; i++)
                {
                    var line = _document[i];
                    int len = line.Contains('\t') || _lines[i] != null ? this[i].VisualLength : line.Length;
                    _maxVisualLength = Math.Max(_maxVisualLength, len);
                }
            }
            return _maxVisualLength;
        }
    }

    public void Invalidate()
    {
        for (int i = 0; i < _lines.Count; i++)
            _lines[i] = null;
        _maxVisualLength = -1;
    }

    public void Detach() => _document.Changed -= OnChanged;

    void OnChanged(TextDocument doc, TextChange change)
    {
        int start = change.Start.Line;
        _lines.RemoveRange(start, change.OldLineCount);
        _lines.InsertRange(start, new LineLayout?[change.NewLineCount]);
        _maxVisualLength = -1;
    }
}
