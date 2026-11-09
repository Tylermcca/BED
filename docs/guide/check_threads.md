# Check threads and lifetime

This is the generic check contract, not a promise that every project object is thread-safe. It describes `ICheck`, `Checks` and `CheckRun` in [Checks.cs](../../src/Brutal.Authoring.Data/Checks.cs).

## Keep project and registry entry points on their owner thread

Create and use a project's `Checks` registry on the same owner thread as its `DataProject` and editing sessions. Register checks with `Add`, query the registry with `Registered`/`Last`, start them with `Start`, and query `LingeringRuns` there. The registry has mutable dictionaries and lists; `LingeringRuns` also removes finished entries. No new general concurrent-registry support is provided.

Call `IsStale`, `Clean` and `Verdict` on that owner thread too. They inspect the live project's revisions/files. The `IsStale(project, maxAge)` overload retains a mutable freshness cache. Safe background status publication does not make these entry points safe during concurrent edits or concurrent cached freshness queries. They may do filesystem work.

`Start` pins the project signature and arranges snapshot capture, then schedules `ICheck.RunAsync` on a worker. Capture pins immutable draft documents on the caller; the worker serializes them and reads the saved-file paths. Pass only the supplied snapshot into the check; do not reach back into the live project, editor or UI. See `CheckSnapshot.CaptureLater` and `SignatureLater` for the boundary. The actual disk-read boundary is not a filesystem transaction: changes outside the editor can still make the result stale.

## Worker progress and status readers

An `ICheck` can report through its supplied `IProgress<CheckProgress>` from its worker. Reports are handled synchronously by the run's reporter, not posted to a UI synchronization context. A null report, NaN, infinity or a fraction outside zero through one is ignored; a null fraction means the amount cannot be measured. Late progress after terminal completion is ignored.

`State`, `Progress`, `Report` and `Problem` read an immutable status reference through `Volatile.Read`. Writers retain the existing writer gate and publish through `Volatile.Write`. Findings are copied and validated before the completed status/report is published. A status reader does not acquire the writer gate or wait for findings enumeration. Reentrant progress during enumeration is preserved.

Each getter is a separate read. Several getter calls during a running transition can observe successive publications; they are not an atomic multi-property observation. Terminal publications never change. Reading `State` as `Completed` and then reading `Report` sees the finished report. A failure/cancel/timeout similarly publishes its existing diagnostic with its terminal state. This does not add a public snapshot API or make a check's own mutable objects safe to share.

`Cancel` uses the existing gate, moves a nonterminal run to `Cancelling`, then signals its token. Token callbacks can run synchronously; do not block the owner thread on a callback that itself needs that thread. Cancellation is cooperative, not thread termination. No callback-dispatch behavior changes in this patch.

## Logical completion is not physical worker completion

`Ended` completes when the run has its terminal status: completed, failed, cancelled or timed out. `Worker` completes only when the check's own work returns. A token-ignoring check can remain active after `Ended`; `Lingering` exposes that distinction, and the registry's existing `MaxLingering` limit bounds replacement admission. Do not dispose resources still used by such a worker just because `Ended` completed.

The existing worker continuation observes late faults and retains the linked token/timer until the physical work ends. Late work cannot replace a terminal status. Keep honoring cancellation inside the check and own any children/resources it starts; a logical timeout is not evidence that they were killed.

## Focused proof and limits

[CheckRunPublicationTests.cs](../../tests/Brutal.Authoring.Tests/CheckRunPublicationTests.cs) uses invented temporary JSON, public seams and controlled completion signals. It covers valid/invalid progress, result copying/normalization, exact malformed-result/fault diagnostics, late progress, cancellation with a lingering worker, reentrant progress and nonblocking status reads during controlled findings enumeration. Fixtures release and await their own work before deleting their own synthetic directory. Redirect TEMP/TMP into the caller's isolated test area when running there.

These focused seam tests are not proof of exhaustive interleavings or runtime memory-order behavior, and they do not replace compilation, serial UI integration or full native gates. No torn-read defect was reproduced by the source review, and no allocation, latency or throughput gain is claimed. The private immutable status allocates on accepted progress, cancellation and terminal transitions; measure if a workload makes that material.

Provenance: MIXED. The need for coherent publication and thread/lifetime documentation is rank 15 of [code_health.md](../plan/code_health.md). The private record publication and deterministic fixture details are INVENTED for A307, using the existing `Volatile`/writer-gate and public check contracts. No game-specific behavior or new concurrency API is introduced.
