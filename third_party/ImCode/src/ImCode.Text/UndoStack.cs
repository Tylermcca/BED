using System.Diagnostics;

namespace ImCode.Text;

/// <summary>
/// Operation based undo history. Each entry holds the changes of one edit command
/// and the selection before and after it.
/// </summary>
public sealed class UndoStack
{
    sealed class Entry
    {
        public required List<TextChange> Changes;
        public required Selection Before;
        public required Selection After;
        public required double Time;
        public required bool Mergeable;
        public long Id;
    }

    readonly List<Entry> _undo = [];
    readonly List<Entry> _redo = [];
    long _nextId = 1;
    long _savedId;

    public int Capacity { get; set; } = 1000;

    /// <summary>Mergeable entries closer in time than this (seconds) are combined into one undo step.</summary>
    public double MergeWindow { get; set; } = 1.0;

    public Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    // sessions sharing this history: the one in an edit scope records, external changes go to the owner
    internal object? Owner;
    internal object? ActiveEditor;

    /// <summary>True while undo or redo modifies the document.</summary>
    public bool IsApplying { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>Id of the newest undo entry, 0 if there is none. See <see cref="SquashSince"/>.</summary>
    public long TopId => _undo.Count > 0 ? _undo[^1].Id : 0;

    /// <summary>
    /// Combines all entries pushed after the entry with the given id (0: all entries) into one undo step.
    /// Does nothing if that entry is no longer on the stack.
    /// </summary>
    public void SquashSince(long id)
    {
        int first = id == 0 ? 0 : _undo.FindIndex(e => e.Id == id) + 1;
        if (first == 0 && id != 0)
            return;
        if (_undo.Count - first < 2)
            return;
        var target = _undo[first];
        for (int i = first + 1; i < _undo.Count; i++)
        {
            target.Changes.AddRange(_undo[i].Changes);
            target.After = _undo[i].After;
            target.Time = _undo[i].Time;
        }
        target.Mergeable = false;
        if (_undo.Skip(first + 1).Any(e => e.Id == _savedId))
            _savedId = -1;
        _undo.RemoveRange(first + 1, _undo.Count - first - 1);
    }

    /// <summary>True if the document is in the state of the last <see cref="MarkSaved"/> call.</summary>
    public bool IsAtSavePoint => TopId == _savedId;

    public void MarkSaved() => _savedId = TopId;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savedId = 0;
    }

    public void Push(IReadOnlyList<TextChange> changes, Selection before, Selection after, bool mergeable)
    {
        if (changes.Count == 0)
            return;
        _redo.Clear();
        var now = Clock();

        if (mergeable && _undo.Count > 0)
        {
            var last = _undo[^1];
            if (last.Mergeable && last.Id != _savedId && last.After == before && now - last.Time < MergeWindow)
            {
                last.Changes.AddRange(changes);
                last.After = after;
                last.Time = now;
                return;
            }
        }

        _undo.Add(new Entry { Changes = [.. changes], Before = before, After = after, Time = now, Mergeable = mergeable, Id = _nextId++ });
        if (_undo.Count > Capacity)
            _undo.RemoveAt(0);
    }

    public bool Undo(TextDocument document, out Selection selection)
    {
        selection = default;
        if (_undo.Count == 0)
            return false;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(document, entry.Changes.AsEnumerable().Reverse().Select(c => c.Inverse()));
        _redo.Add(entry);
        selection = entry.Before;
        return true;
    }

    public bool Redo(TextDocument document, out Selection selection)
    {
        selection = default;
        if (_redo.Count == 0)
            return false;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(document, entry.Changes);
        entry.Mergeable = false;
        _undo.Add(entry);
        selection = entry.After;
        return true;
    }

    void Apply(TextDocument document, IEnumerable<TextChange> changes)
    {
        IsApplying = true;
        try
        {
            foreach (var c in changes)
                document.Replace(new TextRange(c.Start, c.OldEnd), c.NewText);
        }
        finally
        {
            IsApplying = false;
        }
    }
}
