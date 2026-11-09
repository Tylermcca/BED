# Reading a recorded screen

`bed-cli ui --json` draws the start screen in a local headless host. Add `--data <folder> --schemas <folder>` to open a project; optional `--map` and `--adapter` use the normal project loader. Without `--json`, output is the recorder's indented text.

The JSON is the existing UI snapshot shape: `frame` and `nodes`, with role, label, bounds, state, parent, actions and identity classification. Only completed frames are copied. Without a script, this command does not connect to a live window, generate screenshots, use the system clipboard or save edits. Its temporary recent-project settings are isolated from the person's settings and removed afterward.

Coverage is incomplete until the UI controls are instrumented. An empty `nodes` array does not mean the screen is empty; the command reports that limitation on stderr. Current tests prove the headless snapshot shape and source preservation, not whole-screen semantic coverage.

No remote input route is implemented. `--save`, `--read-only` and unsupported arguments are rejected rather than silently ignored; local scripted save shortcuts use normal editor behavior as described below.


## Hosted MCP observation

`McpServer` accepts an optional `Func<JsonObject>? uiSnapshot` provider. `ui_snapshot`
accepts no arguments and cannot send input. Without a provider it returns an explicit
unavailable error; a one-shot CLI MCP server does not pretend to have a live window.
The host must supply an owned, safely published completed-frame copy. Do not call a
thread-local UI recorder on an MCP thread or draw a frame in the callback. The response
is copied and serialized without modifying the supplied snapshot. Incomplete recorder
coverage remains incomplete; this does not imply full screen-reader coverage.

## Local scripts

`bed-cli ui --script '[{"click":"Name"},{"type":"Invented"},{"key":"Tab"},{"select":0}]' --json`

The JSON array has at most 32 steps, each with exactly one property. `click` uses an
exact unique actionable recorded label. `type` requires a focused editable recorded
text field. `key` accepts named keys such as Ctrl+S, Tab and Escape with Ctrl/Shift/Alt
modifiers. `select` uses a zero-based row in one recorded list; multiple lists refuse.
Missing, ambiguous, disabled or out-of-viewport targets refuse and stop remaining steps.
Unsupported keys and malformed steps are rejected before opening the headless host.

Only this command's own headless instance receives input, never a person's live window.
Actions use normal editor handlers: a save shortcut can write the explicitly opened
project. This is not a preview, transaction or rollback; completed steps remain completed
when a later step refuses. Use disposable project copies when experimenting.

The initial snapshot and each completed step are printed. With `--json` and a script,
each snapshot is one JSON line; without a script, the existing single JSON object remains.
Tree coverage limits still apply: an unrecorded control cannot be targeted by label.
No live-window bridge, remote click or system clipboard access is added.
