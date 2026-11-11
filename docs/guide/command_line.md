# Command line

Run these commands from PowerShell in the unzipped BED folder. Quote paths containing spaces. Replace `./project` with your project folder, which normally contains `data/` and `schemas/`, and optionally `reference_map.json`.

## Open BED or draw a screenshot

```powershell
./BED.exe
./BED.exe "./project"
./BED.exe --data "./project/data" --schemas "./project/schemas"
./BED.exe "./project" --shot "./frame.png"
./BED.exe --version
./BED.exe --help
```

With no project, BED opens the start screen. Choose either a project folder or the explicit data/schema pair. An adapter can recognize other project layouts. The map can accompany either route. Normal opening creates a window; screenshots and probes run headlessly.

### BED options

| Option | Meaning |
|---|---|
| `--data` | Data folder; requires --schemas and cannot accompany a positional project folder. |
| `--schemas` | Schema folder paired with --data. |
| `--map` | Reference-map file; overrides the discovered map even with a project folder. |
| `--adapter` | Adapter DLL to load; repeat for multiple DLLs. Adapters beside BED in its adapters folder load automatically. |
| `--settings` | Folder for local settings such as recent projects and the window layout, useful for an isolated trial. |
| `--shot` | Output PNG filename, not the project path. Draws the screen without opening a window. It can also draw the start screen. |
| `--version` | Print the build version and exit; must be used alone. |
| `-h` | Print usage and exit. |
| `--help` | Print usage and exit. |
| `/?` | Print usage and exit. |
| `--probe-play` | Project folder to open and run through saved-files Play, headlessly. Must be the first argument. |
| `--probe-args` | One quoted string of extra game arguments, appended after configured arguments for this Play probe only. |
| `--timeout` | Whole seconds from 1 through 300; default 30 for Play, 60 for Export. Probe-only. |
| `--probe-export` | Project folder to build, verify and copy headlessly. Must be the first argument. |
| `--destination` | Required Export-probe output folder; choose a new or empty folder. |

### Probe Play and Export

```powershell
./BED.exe --probe-play "./project" --probe-args "--seconds 0" --timeout 30
./BED.exe --probe-export "./project" --destination "./export" --timeout 60
```

The game in the first example must understand `--seconds 0` and exit successfully on its own. These commands require exactly one admitted scene document and the project's normal Play or Export support. Configure and approve the normal command in BED first. Probes do not save settings or approvals; extra Play arguments are temporary.

Each probe accepts only its listed flags after the project folder. In particular, --adapter, --settings and --shot cannot be combined with a probe. Put a needed adapter in the adapters folder beside BED. Repeated probe flags, missing values and invalid timeouts refuse.

Success prints JSON: Play includes exit code and captured game output; Export includes the copied file count, byte total and hashes. Probes return 0 on success and 1 on failure, including bad probe arguments. Other BED argument errors return 2; a project-open failure returns 1, and a missing required VC++ library returns 3. Check both stderr and the exit code. An exit-0 screenshot means the screen was drawn; inspect the image for project or file problems.

See [Play](playing.md), [Export](exporting.md) and [troubleshooting](troubleshooting.md) for settings, build approvals and failure messages.

## Read and edit data with bed-cli

This program runs a single data command and exits, or serves MCP over standard input/output. It does not open the normal BED window.

```powershell
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" describe
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" get settings.json /speed
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" set settings.json /speed 25 --preview
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" set settings.json /speed 25 --save
```

The last three examples assume a `settings.json` file with a numeric `/speed` field. Use `describe` and `find` to obtain your actual file names and pointers. `<at>` and `<list>` below are JSON Pointers, such as `/items/0/name`; `<json>` is a JSON value passed as one argument. PowerShell single quotes are useful for JSON containing double quotes.

Set, insert and remove without --save end without writing the data file; the response explains how to keep the edit. For those three commands, --preview validates without applying and takes precedence over --save. Create, rename, delete and declare preview without --save; for them, --save controls writing even if --preview is also supplied. There is no separate one-shot save command. **Import writes the asset record when accepted without requiring --save.** Import does not honor --preview: use --read-only to prevent asset-record writes in a plain project. Scripted UI input can also save through the usual editor shortcut.

### bed-cli options

| Option | Meaning |
|---|---|
| `--help` | Print usage to stdout and exit 0; must be used alone. |
| `--data` | Data folder, required for normal commands and ai-setup. |
| `--schemas` | Schema folder, required with --data. |
| `--map` | Reference map, needed for link lookups and checks. |
| `--adapter` | One DLL; its adapter rules and checks apply. If it contains several adapters, the first that recognises the supplied project is selected, as in BED. If none recognises it, the command refuses. |
| `--assets` | Asset folder for import/declare and the content-rights check. |
| `--own-name` | An own rights-holder name; repeat to supply more names. |
| `--read-only` | Open plain data read-only; combining it with an adapter is currently refused. |
| `--preview` | Validate an edit without applying or saving it. |
| `--save` | Save an accepted edit through the normal checked save path. |
| `--limit` | Positive whole-number maximum for find (default 50) or check results (all by default). |
| `--first` | Zero-based first finding for check; must be a whole number at least 0. |
| `--options` | JSON object of choices for a check, as listed by checks. |
| `--licence` | Licence name for an asset declaration. |
| `--source` | Asset source, required for licensed and publicDomain origins. |
| `--author` | Optional author for import. |
| `--ai` | Import's AI-use declaration: allowed, forbidden or unknown; omitted means unknown (treated as forbidden). |
| `--script` | JSON array of up to 32 local ui input steps; only valid with ui. |
| `--json` | Machine-readable output for ui or ai-setup; normal data replies already use JSON. |
| `--version` | Print the build version and exit; must be used alone. |
| `--run` | Refused by one-shot CLI: run IDs last only within a persistent MCP session. |

`bed-cli.exe --help` prints usage to stdout and returns 0 without opening a project. Combining --help with any other argument is refused with exit 2 before project I/O. There are no -h or /? aliases. Running `bed-cli.exe` with no arguments still prints usage to stderr and returns 2. Options take their following argument, except the switches --read-only, --preview, --save, --json, --help and --version. Normal commands require both data and schema folders. Adapter loading failures refuse rather than silently dropping validation.

`bed-cli.exe report`, used alone, prints a problem report with personal paths removed and returns 0. It needs no data or schema folders, opens no project, writes no file and opens no browser. See [Reporting a problem](reporting_a_problem.md).

### bed-cli commands

| Command | What it does |
|---|---|
| `ai-setup [--json]` | Print setup examples without opening the project, loading an adapter or writing configuration. Requires data and schema paths; a supplied adapter file must exist. |
| `ui [--json] [--script <json array>]` | Draw a separate headless instance and print recorded controls. With no data/schema paths it draws the start screen. |
| `scene <file> <action> <args-json> [--preview] [--save]` | Preview or save typed scene edits; version two also supports prefab and sprite actions. |
| `describe` | List files, lists, problems and available calls. |
| `find <file> <list> [text] [--limit N]` | Find items by label or field text. |
| `get <file> <at>` | Read a value, label and the target of an ID. |
| `problems [file]` | Report schema problems for one file or the project. |
| `refs <file> <at>` | List references to an item; needs a reference map. |
| `repair <file>` | Open a schema-invalid file for repair. |
| `set <file> <at> <json> [--preview] [--save]` | Replace a value. |
| `insert <file> <list> <index> <json> [--preview] [--save]` | Insert a list item at a zero-based index. |
| `remove <file> <at> [--preview] [--save]` | Remove a value or item. |
| `create <file> <schema> [--save]` | Create a file from a schema; previews by default. |
| `rename <file> <new name> [--save]` | Rename a file and report incoming links; previews by default. |
| `delete <file> [--save]` | Delete a file and report affected links; previews by default. |
| `checks` | List check IDs and their supported choices. |
| `check <id> [--limit N] [--first N]` | Run a check and wait; use --options for its JSON choices. |
| `import <file> <origin> [--licence L] [--source S] [--author A] [--ai allowed or forbidden]` | Import an asset with its origin; needs assets. Accepted import can write immediately. |
| `declare <file> <origin> [--licence L] [--source S] [--save]` | Change an imported asset's origin; previews by default. |
| `mcp` | Serve tools over stdio until the client closes the session. |

For asset origins, use `ours`, `licensed` or `publicDomain`; see [content rights](content_rights.md). `ai-setup` accepts only data/schema paths, optional map/adapter and --json. `ui` accepts those same options plus --script, with paths optional as a pair. Both refuse ordinary editing and asset flags.

Normal replies are JSON with a `next` hint. Exit 0 means success; 1 means refusal, failed save/check or schema problems; 2 means bad arguments or an unreadable project/map. A one-shot edit ends with the process. MCP keeps drafts and run IDs within its session and has its own tool list; see [AI setup](using_bed_with_ai.md) and [check run IDs](check_run_ids.md).

For UI snapshots and the supported click/type/key/select script steps, read [recorded screens](ui_snapshot.md). UI scripts act only on their own headless instance, but a save action can write the explicitly opened project and earlier actions remain if a later step refuses.

### Scene commands

Use an admitted scene file from the data folder. A scene command computes a candidate without writing by default. Add `--save` to keep it through the same typed validation and disk-conflict checks as the editor. `--preview` takes precedence over `--save`. The reply includes the candidate scene, its journal, one undo step and the resulting selection. An invalid command or failed save writes nothing.

```powershell
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" scene level.json move '{"id":"crate","position":[2,0,3]}'
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" scene level.json move '{"id":"crate","position":[2,0,3]}' --save
```

| Action | JSON arguments | Result |
|---|---|---|
| `add` | `{"object":{"id":"crate","position":[0,0,0]}}` | Append an object using the existing version-one scene command arguments. |
| `move` | `{"id":"crate","position":[2,0,3]}` | Replace its local position in metres. |
| `duplicate` | `{"ids":["crate","child"]}` | Allocate unused IDs, keep internal parents/references together, place the copies one metre along world X and select them. |
| `delete` | `{"id":"crate"}` | Remove an unreferenced object. Incoming parents or named references refuse and name the referring objects; this command never assumes Delete anyway. |
| `rename` | `{"id":"crate","newId":"cargo_crate"}` | Rename in place; every parent and named reference follows. Empty, invalid and already-used IDs refuse. |
| `group` | `{"ids":["crate","other_crate"]}` | Add an unused group ID at the mean selected world position, reparent selected roots while preserving world poses and select the new parent. |

Duplicate and group share their non-drawing planner with the editor, including collision-free IDs and one atomic undo step. Their `ids` argument must be a nonempty array of unique existing IDs, with no extra argument fields. The other actions use the scene catalog's strict v1 arguments. The file must have the expected `scene` kind and version 1. Read-only and protected files refuse; adapter save policies remain in force. A referenced delete can be prepared by editing the references explicitly; the CLI does not show the editor's confirmation dialog. These are one-shot CLI commands, separate from MCP's data tools.

With a multi-adapter DLL, both data commands and `ui` report the selected adapter type on stderr. A recognition exception is reported and that adapter is skipped, as on the start screen. Selection never falls back to plain data when an explicit multi-adapter DLL has no match. A DLL containing one adapter retains the explicit single-adapter behavior, including adapters that do not implement automatic recognition. All edits still use the selected adapter's admitted files, rules, checks and checked save path.

### Version-two scene commands

The same `scene` command chooses the explicitly bound file's exact version. V1 actions and replies keep their existing meaning. A v2 preview uses a short-lived typed draft and one journal step, with no disk write; `--preview` still wins over `--save`. `--save` commits the checked host plan and saves through `Scene2SaveCheck` and conflict checks. Unsupported versions and unsafe, read-only, stale or malformed inputs refuse plainly.

V2 accepts the core payloads in [the API overview](api_overview.md). `add`, `move`, `rotate`, `scale`, `rename` and `delete` alias compatible object actions; `duplicate` with `ids` duplicates ordinary objects, while the instance payload duplicates an entire linked instance. `group` accepts `ids` and an optional unused `id`. Sprite metadata and accepted prefab content survive compatible edits.

| Action | Host JSON arguments | Result |
|---|---|---|
| `instantiate` | `source` GUID, `placement`, optional `key`, `baseId`, `nodeIds`, `bindings` | Accept one admitted saved definition. Missing instance/node/base keys are allocated once and recorded. |
| `update` | `instance`, optional `baseId`, `newNodeIds` | Look up that instance's source GUID, preview conflicts, retain overrides and accept the saved source. New node IDs are allocated once if omitted. |
| `set` | `id`, atomic `field`, optional `value` | Set an explicit local value; omit value to unset a permitted field. |
| `revert` | `instance`, `node`, `field` | Remove an explicit override. |
| `break_link` | `instance` | Preserve effective IDs, order and content as ordinary objects. |
| `add_sprite` | `object` with complete sprite metadata | Add an ordinary planar sprite. |
| `paste` | `fragment` in the existing scene-v2 shape, same space, copied objects/bases/complete instances and exact copied objectOrder | Append a preallocated fragment atomically; collisions, absent external targets and invalid graphs refuse. No source discovery or system clipboard format. |
| `set_physics` | Optional complete `physics` block | Set world gravity/step atomically; omit to remove, refused while bodies remain. |
| `edit_bodies` | `edits` array of `{id,field,value?,revert?}` | Atomic motion/mass or multi-object body changes. Only approved body addresses, 4096 IDs and five nonoverlapping fields per ID; `revert:true` returns an inherited field to its accepted value. |
| `set_sprite` | `id`, `sprite` with size/pivot/order/filter | Replace sprite fields through their authoring owners. |

```powershell
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" scene level.json set '{"id":"door","field":"/position","value":[3,2,0]}' --preview
./bed-cli.exe --data "./project/data" --schemas "./project/schemas" scene level.json update '{"instance":"22222222222222222222222222222222"}' --save
```

Source lookup uses only the project's admitted saved JSON files (including subfolders only when the adapter admits them). It accepts GUIDs, never caller-supplied source paths or inline definitions. Duplicate GUIDs, changed source bytes between preview and commit, unsaved source drafts and exhausted scan budgets refuse. Commands journal their accepted snapshot, so later source removal does not change replay or an existing accepted scene. These host/CLI routes do not add a native scene-v2 panel or runtime/build reader.
