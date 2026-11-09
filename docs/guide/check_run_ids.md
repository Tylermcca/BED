# Check run ids

A completed ordinary check response includes `run`, an opaque id. Keep it with `id`, then send `run`, `first` and `limit` to page that same result. For example, an agent may call `RunCheck("lantern.audit", first: 50, limit: 50, runId: savedRun)`; MCP uses the `run` argument.

The history holds eight terminal ordinary runs across all checks in one Checks instance. It evicts the oldest FinishedAt completion; equal timestamps use stable completion-registration order. It is memory-only. Hosted sessions sharing the same Checks instance share this history. Isolated proposal runs are excluded. Latest panel results and lingering-worker lifetime bookkeeping are separate and unchanged; an evicted latest result may still be shown by the panel or legacy no-id route, but explicit lookup refuses it.

Unknown, evicted, wrong-check and currently stale ids are refused without running another check. Staleness uses the current exact input signature, so undo can make retained results current again. `run` cannot be combined with options. Cancellation, timeout and failure can have ids but keep their original non-clean verdict; a failed capture may also be stale and therefore unavailable to explicit paging. Ids are not proof of freshness or successful validation.

Without `run`, legacy behavior remains: `first` uses the latest result, and no latest result starts a check. A new check without `first` starts another run. Use an explicit id when pages must stay together. Successful responses add the id without removing old fields.

The one-shot CLI returns an id in its initial result but cannot reuse it in another process. `--run` refuses before opening a project: "run ids last only within one session; use MCP or the agent". Use the existing persistent MCP stream or an AgentSession for continued paging. There is no disk cache, new listener, background service or permission change.

Source prepared only: compiler, CLI, MCP and concurrency tests are not executed by the worker.
