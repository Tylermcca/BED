# The first slice: what it promises

This is the contract of the shared core's first slice (worksheet_r2, "The first slice"). It's written so someone who has never seen the code can test it. The public API is in `src/Bed`, and the neutral sample is in `samples/Toy`.

## Documents and commands

- **Documents are immutable.** Holding one can never change a session.
- **Commands are the only way to edit** (D11). A command is a plain record whose properties are its arguments. Commands are registered by hand in a `CommandCatalog<TDoc>`, each with:
  - a stable id: lowercase and dotted, like `toy.move`;
  - an argument version, starting at 1;
  - a label people can search;
  - optionally, marked internal, which hides it from search.
- **An edit is applied in four steps** (`DocumentSession<TDoc>.Execute` or `ExecuteBatch`):
  1. The command builds a candidate document, or refuses.
  2. The validator checks the whole candidate.
  3. A clean candidate is committed: it becomes `Current`, `Revision` goes up by 1, one undo step is pushed, redo is cleared, and one journal entry is recorded.
  4. A refused command or candidate changes nothing at all: not the document, the revision, the undo or redo stacks, or the journal. `LastIssues` says why.
- **A batch is all or nothing,** and it counts as one undo step and one journal entry.
- **Undo and redo** step through committed edits. Each also counts as a revision and adds a journal entry. History keeps at most its capacity of undo steps; older ones are forgotten, not undone.
- **Generated values are recorded.** When a command settles something left open (the sample's `AddItem` with no id picks `item_N`), the journal records the settled form, so a replay reproduces it.

## Issues

An `Issue` carries:
- a severity (Info, Warning or Error);
- a message saying what went wrong and what to do;
- the object id, when there is one;
- the field or JSON path, when there is one.

It prints as `error [<object> <path>]: <message>`. Only errors refuse an edit.

## Files

- **Canonical text:** UTF-8 with no byte-order mark, LF line endings, a final newline, and fixed key order. Non-ASCII text stays readable. Numbers use their shortest round-trip form.
- **Save, load, save is byte-identical.**
- **Strict reading.** Unknown properties, duplicate properties, missing properties, wrong types, non-finite numbers, and an unsupported `kind` or `version` are all refused with a `DocumentFormatException` naming the JSON path (for example `$.items[0].y`). An invalid document never becomes a session (`InvalidDocumentException`).
- **Saving is atomic.** The new text goes to a temporary file beside the target, is flushed, then replaces it. If saving fails, the old file is untouched, no temporary file is left behind, and the session stays dirty.
- **`Dirty`** is true exactly when the current document's canonical text differs from what was last loaded or saved. Undoing back to the saved state makes it clean again.
- **`CheckDisk()`** reports:
  - `Unchanged`: the file holds exactly what the session last loaded or saved (its own saves never count as outside edits);
  - `ChangedOnDisk`: something else changed it, even with the same length;
  - `Missing`: the file is gone;
  - `NoFile`: the session has no file.
- **`Revert()`** reloads the file, dropping history and journal. A file that can't be read or isn't valid changes nothing, and `LastIssues` says why.

## Journals

A journal file is `{"kind": "journal", "version": 1, "entries": [...]}`. Each entry is one of:
- `{"op": "do", "command": <id>, "version": <n>, "args": {...}}`
- `{"op": "undo"}`
- `{"op": "redo"}`
- `{"op": "batch", "batch": [<do entries>]}`

Rules:
- **Strict reading.** An unknown op, unknown command id, a version mismatch, unknown or badly typed arguments, or a batch holding anything but `do` entries are all refused with a path.
- **A replay stops at the first failing entry.** An entry that can't be read (unknown op or command id, version mismatch, unknown or badly typed arguments) is bad input: a `DocumentFormatException` whose path names the entry (for example `$.entries[2].args`). An entry whose edit is refused raises a `ReplayException` naming the entry's index.
- **Nullable arguments may be left out.** A left-out nullable argument reads as null (the sample's `toy.add` with no `id` then picks `item_N`, and the journal records the id it picked). Every other argument is required.
- **Replays are repeatable.** The same journal replayed twice onto the same base document gives byte-identical documents and byte-identical journals.

## The `toy` command line

| Command | Does |
|---|---|
| `toy new <board.json>` | Makes an empty board. Refuses to overwrite |
| `toy check <board.json>` | Reads and validates |
| `toy apply <board.json> <journal.json>` | Applies the journal and saves. Nothing is written if any step is refused |
| `toy replay <base.json> <journal.json> <out.json>` | Replays onto a base and writes the result |
| `toy commands [text]` | Lists commands, or searches them by label or id |

For tests, `Program.Run(args, out, err, afterApply)` runs `afterApply` during `apply` after the journal is applied and before the disk check, so a test can change the file there and get exit code 3.

Exit codes: **0** done; **1** refused (nothing written); **2** bad input (unreadable or malformed file, bad journal, wrong arguments, or `new` onto an existing file); **3** the file changed on disk during `apply`.

## The sample board

- **Item ids** start with a lowercase letter and use only lowercase letters, digits and underscores. They must be unique.
- **Labels** are 1 to 80 characters and not blank.
- **Positions** are finite.
- **Commands:**
  - `toy.add` (id optional, label, x, y);
  - `toy.move` (id, x, y);
  - `toy.rename` (id, label);
  - `toy.remove` (id).
