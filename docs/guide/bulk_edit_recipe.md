# Change a field across many rows, check it, and keep only part of the change

This recipe uses existing Table controls. You do not need to build an editor host or write a script. Open your project and the intended data file first. If Table is hidden, use the command palette (Ctrl+Shift+P) to find Show Table.

## Before you start
Use a disposable example if you are learning. Imagine 40 items with an editable integer `quantity`, all initially 10, and a displayed cohort field: 20 contain `cohort-alpha`, 20 contain `cohort-beta`. These are invented names and values, not fields guaranteed in your project. Your schema must allow the values you enter.

The example changes every quantity to 20, checks the result, then keeps that change only for alpha. Start with a valid document and no intervening edits. Repair mode has different acceptance rules for existing errors; this recipe does not use it.

## 1. Fill the shown rows
1. In Table, choose the intended list. Clear any old Filter. Check that all 40 intended rows are shown, and verify their identities.
2. In the fill row, choose `quantity` and enter `20`.
3. Choose **Fill shown rows**. Read the preview before going further. It should target 40 cells. The changed count can be smaller when some values are already 20.
4. Choose **Apply**, or **Cancel** to leave the document untouched. If Apply refuses the edit, read the problem and correct the value or scope before trying again.

Fill affects every currently shown row, not just a selected rectangle. Filter is a case-insensitive substring search across labels and shown values, not a numeric-range query. Verify identities as well as the count. Editing a value used by the filter can make rows disappear from the view afterward.

A successful scalar preview means the value can be converted and the cells are editable. It does **not** mean the entire candidate document passed validation. Apply performs that validation before committing the edit. An invalid ordinary edit is refused rather than leaving a broken batch in the document.

## 2. Check the result
Inspect the displayed values and intended rows. Then open **Checks** or use the registered Run check commands in the palette. Run the checks relevant to your project, wait for completion and inspect their findings. Checks include the editor's link check and any checks registered by the adapter; they do not promise coverage of every possible project rule.

Manual checks include unsaved edits. A later edit can make their results stale, so run the relevant checks again after changing the document. Do not treat an earlier clean result as proof of the latest draft.

When ready, Ctrl+S saves. Save can be deferred or refused by validation or file-state guards. A project may also have an optional save-check hook. **Manual checks and save hooks are separate:** saving successfully does not establish that every registered manual check ran, and a project without a save hook has no additional hook coverage. Read any refusal rather than assuming the save completed.

## 3. Keep the change on only half the rows
**One Apply is one undo step. You cannot half-undo that single batch.** If this fill is still the latest document edit:

1. Leave active text editing, then press Ctrl+Z once for document Undo. This restores all 40 previous values. Inside a text editor, Ctrl+Z may undo text instead.
2. Filter to `cohort-alpha`. Verify that the intended 20 rows, and only those rows, are shown. Substring matches can include unintended rows.
3. Choose `quantity` and enter `20` again if needed. Choose **Fill shown rows** and inspect the 20-cell preview.
4. Choose **Apply**. Alpha now has the change; beta keeps its original values. Rerun the relevant checks, then save when appropriate.

This is **Undo-all, then reapply the intended subset**, not selective history undo. Do not repeatedly press Undo expecting it to peel cells off a batch. If other edits have happened since the fill, stop and inspect the history situation rather than undoing unrelated work. After a new edit, do not rely on the old whole-batch redo still being available.

For this example only, filtering beta and filling 10 would be a new compensating edit. It is unsafe when original values differed or you do not know them. Undo-all restores the actual originals without guessing.

If you know beforehand that two groups must be separately undoable, Apply each group separately. That creates two history steps at the cost of extra setup and review. It does not split an existing batch.

## Paste is another existing option
**Paste at cell** starts at the chosen cell and follows the currently shown row and column order. Check the anchor, filter and sort order before applying a rectangular clipboard grid. Overflow or ragged grids are refused. Do not use a pasted column as a restoration source unless you have the exact original values in the correct order.

## What this recipe establishes
This is source-backed guidance, not a recorded newcomer run. Bulk fill can require fewer counted actions than entering every cell separately, but no elapsed-time saving or observed usability result is claimed. Actual UI navigation, runtime checks and newcomer testing remain unverified for this recipe.

For detailed editor contracts and keyboard guidance, see [Editing data](editing_data.md).
