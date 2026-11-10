# Command line

Run these commands from PowerShell in the unzipped BED folder. Quote paths containing spaces. Replace `C:/work/project` with your project folder, which normally contains `data/` and `schemas/`, and optionally `reference_map.json`.

## Open BED or draw a screenshot

```powershell
./BED.exe
./BED.exe "C:/work/project"
./BED.exe --data "C:/work/project/data" --schemas "C:/work/project/schemas"
./BED.exe "C:/work/project" --shot "C:/work/frame.png"
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
| `--settings` | Folder for local settings such as recent projects, useful for an isolated trial. |
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
./BED.exe --probe-play "C:/work/project" --probe-args "--seconds 0" --timeout 30
./BED.exe --probe-export "C:/work/project" --destination "C:/work/export" --timeout 60
```

The game in the first example must understand `--seconds 0` and exit successfully on its own. These commands require exactly one admitted scene document and the project's normal Play or Export support. Configure and approve the normal command in BED first. Probes do not save settings or approvals; extra Play arguments are temporary.

Each probe accepts only its listed flags after the project folder. In particular, --adapter, --settings and --shot cannot be combined with a probe. Put a needed adapter in the adapters folder beside BED. Repeated probe flags, missing values and invalid timeouts refuse.

Success prints JSON: Play includes exit code and captured game output; Export includes the copied file count, byte total and hashes. Probes return 0 on success and 1 on failure, including bad probe arguments. Other BED argument errors return 2; a project-open failure returns 1, and a missing required VC++ library returns 3. Check both stderr and the exit code. An exit-0 screenshot means the screen was drawn; inspect the image for project or file problems.

See [Play](playing.md), [Export](exporting.md) and [troubleshooting](troubleshooting.md) for settings, build approvals and failure messages.

## Read and edit data with bed-cli

This program runs a single data command and exits, or serves MCP over standard input/output. It does not open the normal BED window.

```powershell
./bed-cli.exe --data "C:/work/project/data" --schemas "C:/work/project/schemas" describe
./bed-cli.exe --data "C:/work/project/data" --schemas "C:/work/project/schemas" get settings.json /speed
./bed-cli.exe --data "C:/work/project/data" --schemas "C:/work/project/schemas" set settings.json /speed 25 --preview
./bed-cli.exe --data "C:/work/project/data" --schemas "C:/work/project/schemas" set settings.json /speed 25 --save
```

The last three examples assume a `settings.json` file with a numeric `/speed` field. Use `describe` and `find` to obtain your actual file names and pointers. `<at>` and `<list>` below are JSON Pointers, such as `/items/0/name`; `<json>` is a JSON value passed as one argument. PowerShell single quotes are useful for JSON containing double quotes.

Set, insert and remove without --save end without writing the data file; the response explains how to keep the edit. For those three commands, --preview validates without applying and takes precedence over --save. Create, rename, delete and declare preview without --save; for them, --save controls writing even if --preview is also supplied. There is no separate one-shot save command. **Import writes the asset record when accepted without requiring --save.** Import does not honor --preview: use --read-only to prevent asset-record writes in a plain project. Scripted UI input can also save through the usual editor shortcut.

### bed-cli options

| Option | Meaning |
|---|---|
| `--data` | Data folder, required for normal commands and ai-setup. |
| `--schemas` | Schema folder, required with --data. |
| `--map` | Reference map, needed for link lookups and checks. |
| `--adapter` | One DLL containing exactly one adapter; its rules and checks apply. |
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

There is no CLI help flag: running `bed-cli.exe` with no arguments prints usage to stderr and returns 2. Options take their following argument, except the switches --read-only, --preview, --save, --json and --version. Normal commands require both data and schema folders. Adapter loading failures refuse rather than silently dropping validation.

### bed-cli commands

| Command | What it does |
|---|---|
| `ai-setup [--json]` | Print setup examples without opening the project, loading an adapter or writing configuration. Requires data and schema paths; a supplied adapter file must exist. |
| `ui [--json] [--script <json array>]` | Draw a separate headless instance and print recorded controls. With no data/schema paths it draws the start screen. |
| `scene <file> <action> <args-json> [--preview] [--save]` | Preview or save a validated scene add, move, duplicate, delete, rename or group. |
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
./bed-cli.exe --data "C:/work/project/data" --schemas "C:/work/project/schemas" scene level.json move '{"id":"crate","position":[2,0,3]}'
./bed-cli.exe --data "C:/work/project/data" --schemas "C:/work/project/schemas" scene level.json move '{"id":"crate","position":[2,0,3]}' --save
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
