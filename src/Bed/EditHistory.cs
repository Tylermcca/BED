namespace Bed;

/// <summary>
/// Undo and redo for one document, as snapshots of immutable documents (BORROWED from an existing game editor). The undo side
/// keeps at most <see cref="Capacity"/> steps and forgets the oldest past that; a new edit clears redo.
/// </summary>
public sealed class EditHistory<TDoc> where TDoc : class
{
    private readonly LinkedList<TDoc> undo = new();
    private readonly Stack<TDoc> redo = new();

    /// <summary>Keeps up to <paramref name="capacity"/> undo steps.</summary>
    public EditHistory(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>The most undo steps kept.</summary>
    public int Capacity { get; }

    /// <summary>Whether there is a step to undo.</summary>
    public bool CanUndo => undo.Count > 0;

    /// <summary>Whether there is a step to redo.</summary>
    public bool CanRedo => redo.Count > 0;

    /// <summary>How many steps can be undone.</summary>
    public int UndoCount => undo.Count;

    /// <summary>Records that the document was <paramref name="before"/> until a new edit. Clears redo.</summary>
    public void Push(TDoc before)
    {
        undo.AddLast(before);
        if (undo.Count > Capacity) undo.RemoveFirst();
        redo.Clear();
    }

    /// <summary>Steps back from <paramref name="current"/>; false when there's nothing to undo.</summary>
    public bool TryUndo(TDoc current, out TDoc previous)
    {
        if (undo.Last is not { } last) { previous = current; return false; }
        undo.RemoveLast();
        redo.Push(current);
        previous = last.Value;
        return true;
    }

    /// <summary>Steps forward from <paramref name="current"/>; false when there's nothing to redo.</summary>
    public bool TryRedo(TDoc current, out TDoc next)
    {
        if (!redo.TryPop(out var n)) { next = current; return false; }
        undo.AddLast(current);
        if (undo.Count > Capacity) undo.RemoveFirst();
        next = n;
        return true;
    }

    /// <summary>An independent copy: the same steps (the documents themselves are immutable, so they're shared, not copied).</summary>
    public EditHistory<TDoc> Copy()
    {
        var copy = new EditHistory<TDoc>(Capacity);
        foreach (var d in undo) copy.undo.AddLast(d);
        foreach (var d in redo.Reverse()) copy.redo.Push(d);
        return copy;
    }

    /// <summary>Forgets every step (after a load or a revert).</summary>
    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
