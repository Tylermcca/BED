namespace Brutal.Authoring.Editor;

/// <summary>Coalesces notification hints into explicit refresh requests; subscribes to nothing and performs no IO.</summary>
public sealed class AssetWatchReconciler
{
    /// <summary>Why the host should reconcile its catalog rather than trust an event as authoritative state.</summary>
    [Flags]
    public enum Reason
    {
        /// <summary>No pending work.</summary>
        None = 0,
        /// <summary>The user explicitly requested refresh.</summary>
        Manual = 1,
        /// <summary>A source or sidecar may have changed.</summary>
        Changed = 2,
        /// <summary>A source or sidecar may have appeared.</summary>
        Created = 4,
        /// <summary>A source or sidecar may have disappeared.</summary>
        Deleted = 8,
        /// <summary>A path changed; this is not evidence that its GUID changed.</summary>
        Renamed = 16,
        /// <summary>Notifications were lost; a complete census is required.</summary>
        Overflow = 32,
    }

    /// <summary>One bounded request, not a catalog generation or a successful scan receipt.</summary>
    public sealed record Plan(Reason Reasons, int HintCount, bool CountSaturated)
    {
        /// <summary>Existing AssetBrowser.Refresh has a full-scan seam, not a safe incremental identity API.</summary>
        public bool RequiresFullReconciliation => true;
    }

    private const Reason All = Reason.Manual | Reason.Changed | Reason.Created | Reason.Deleted | Reason.Renamed | Reason.Overflow;
    private readonly object gate = new();
    private readonly int countLimit;
    private Reason reasons;
    private int count;
    private bool saturated;
    private bool closed;

    /// <summary>Creates a constant-memory accumulator. The count is diagnostic only and saturates at the supplied limit.</summary>
    public AssetWatchReconciler(int countLimit = 1024)
    {
        if (countLimit < 1) throw new ArgumentOutOfRangeException(nameof(countLimit));
        this.countLimit = countLimit;
    }

    /// <summary>Queues the manual refresh path without starting work.</summary>
    public bool RequestRefresh() => Notify(Reason.Manual);

    /// <summary>Records event kinds only. No paths, source bytes, GUID assumptions or unbounded event list are retained.</summary>
    public bool Notify(Reason reason)
    {
        if (reason == Reason.None || (reason & ~All) != 0) throw new ArgumentOutOfRangeException(nameof(reason));
        lock (gate)
        {
            if (closed) return false;
            reasons |= reason;
            if (count < countLimit) count++; else saturated = true;
            return true;
        }
    }

    /// <summary>Explicitly drains one pending batch. Host controls scheduling/execution; idle calls never request a scan.</summary>
    public bool TryTake(out Plan? plan)
    {
        lock (gate)
        {
            plan = null;
            if (closed || reasons == Reason.None) return false;
            plan = new(reasons, count, saturated);
            reasons = Reason.None; count = 0; saturated = false;
            return true;
        }
    }

    /// <summary>Closes this admission and discards its hints. Reopening uses a new instance; old callbacks are ignored.</summary>
    public void Close()
    {
        lock (gate) { closed = true; reasons = Reason.None; count = 0; saturated = false; }
    }
}
