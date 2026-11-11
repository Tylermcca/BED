namespace Bed.Editor;

/// <summary>Pure generation admission for one asset in one project admission. Executes no importer or publication.</summary>
/// <typeparam name="T">An immutable result owned by the caller; this tracker neither mutates nor disposes it.</typeparam>
public sealed class AssetImportQueue<T> where T : class
{
    private readonly object gate = new();
    private long next;
    private Ticket? desired;
    private Ticket? active;
    private Result? lastGood;
    private string? failure;
    private bool closed;

    /// <summary>An opaque queue-owned request. Revision is supplied by the host, not computed from a live path.</summary>
    public sealed class Ticket
    {
        internal Ticket(long generation, string revision) { Generation = generation; Revision = revision; }
        /// <summary>Monotonic within this admission only.</summary>
        public long Generation { get; }
        /// <summary>Host-supplied complete immutable input revision identity.</summary>
        public string Revision { get; }
        private int canceled;
        /// <summary>Cooperative cancellation request, not evidence the importer has stopped.</summary>
        public bool CancellationRequested => Volatile.Read(ref canceled) != 0;
        internal void Cancel() => Interlocked.Exchange(ref canceled, 1);
    }

    /// <summary>A retained successful result with its actual request identity, never relabelled as newer content.</summary>
    public sealed record Result(long Generation, string Revision, T Value);
    /// <summary>Whether a worker still owns the single execution slot, including canceled workers not yet completed.</summary>
    public bool InFlight { get { lock (gate) return active is not null; } }
    /// <summary>Last successful current result, retained across failure/cancellation/stale completion.</summary>
    public Result? LastGood { get { lock (gate) return lastGood; } }
    /// <summary>Whether last-good belongs to the latest noncanceled request in this open admission.</summary>
    public bool LastGoodIsCurrent { get { lock (gate) return !closed && desired is { CancellationRequested: false } && lastGood?.Generation == desired.Generation; } }
    /// <summary>The latest current request's failure; stale workers cannot replace it.</summary>
    public string? Failure { get { lock (gate) return failure; } }

    /// <summary>Requests a new generation; superseded work is canceled cooperatively, never assumed terminated.</summary>
    public Ticket Request(string revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            desired?.Cancel();
            desired = new Ticket(checked(++next), revision);
            failure = null;
            return desired;
        }
    }

    /// <summary>Admits the latest pending request only when no previous worker is still in flight.</summary>
    public bool TryStart(out Ticket? ticket)
    {
        lock (gate)
        {
            ticket = null;
            if (closed || active is not null || desired is null || desired.CancellationRequested
                || lastGood?.Generation == desired.Generation || failure is not null) return false;
            active = ticket = desired;
            return true;
        }
    }

    /// <summary>Requests cancellation without freeing the active slot or discarding last-good content.</summary>
    public void Cancel()
    {
        lock (gate) { desired?.Cancel(); active?.Cancel(); }
    }

    /// <summary>Completes actual worker termination. Only the exact current uncanceled ticket can replace last-good.</summary>
    public bool Complete(Ticket ticket, T value, out string why)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (!Finish(ticket, out why)) return false;
            lastGood = new(ticket.Generation, ticket.Revision, value);
            failure = null;
            return true;
        }
    }

    /// <summary>Records failure only for the current request; retained content keeps its original revision.</summary>
    public bool Fail(Ticket ticket, string reason, out string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (gate)
        {
            if (!Finish(ticket, out why)) return false;
            failure = reason;
            return true;
        }
    }

    private bool Finish(Ticket ticket, out string why)
    {
        if (!ReferenceEquals(active, ticket) || active is null)
        { why = "This request does not own the in-flight slot."; return false; }
        active = null;
        if (closed || ticket.CancellationRequested || !ReferenceEquals(desired, ticket))
        { why = "This completion is canceled, superseded, or belongs to a closed admission."; return false; }
        why = "";
        return true;
    }

    /// <summary>Closes this admission. Late completion may release its slot but can never publish a result.</summary>
    public void Close()
    {
        lock (gate) { closed = true; desired?.Cancel(); active?.Cancel(); }
    }
}
