# Edit project data

Use the Files panel to choose a data file, the table to compare rows, and the
Inspector to edit the selected item. The project's adapter and schemas decide
which files and fields BED can edit. Read any opening or read-only message before
changing a file; generated files may belong to another tool.

## Quick steps

1. Open your project, then choose a data file in **Files**.
2. Type in **Filter** to find a row, and select it.
3. Edit a cell, or a field in **Inspector**, and press Enter. If BED refuses the
   value, the reason appears next to the field.
4. Press **Ctrl+Z** to undo or **Ctrl+Y** to redo.
5. Press **Ctrl+S** to save the file, or **Save all** in **Files** to save every
   changed file.

## Open a file and change a value

1. Choose **Open project...** in the launcher and choose your project.
2. In **Files**, choose a data file. If it contains several lists, use **List**
   above the table to choose one.
3. Type in **Filter** to narrow the table by a case-insensitive substring in row
   labels or shown values. Click a column heading to sort the view. Filtering and
   sorting do not rearrange the saved list.
4. Select a row. Edit a cell directly, or use **Inspector** for its fields and
   nested values. The schema determines the available controls and allowed values.
   Where a field offers **reset**, use it to restore that field's default.
5. Finish the field edit and read any refusal message. An invalid edit is refused;
   changing a schema is a separate task.
6. Leave the text field, then use **Ctrl+Z** to undo a document edit or **Ctrl+Y**
   to redo it. While typing, the text field handles its own undo.
7. Press **Ctrl+S** to save the current file. Resolve any schema, read-only or
   outside-file-change message before retrying. Each open file has its own edits
   and undo history.

For keyboard navigation and rebinding, see the [keyboard guide](keyboard.md).
The [editor integration tutorial](editing_data.md) explains how to build these
controls into an editor for your own format.

## Follow a link and see who uses an item

A mapped reference cell offers a **->** button with the **Follow reference**
tooltip. Activate it to open the target file and select its containing item.
Use **Back** above the table to return. A missing or ambiguous target needs its
reported problem resolved before BED can follow it.

With an item selected, open **Used by** from the panel controls. It lists incoming
references known to the project's map, including references to nested parts of
the item and current unsaved source edits. Each entry names its source file and
JSON pointer. Activate an entry to jump to the source item; BED keeps the nested
pointer so you can locate the referring field.

You can use **Ctrl+Tab** to reach the panel, then **Tab** and **Enter** to activate
an entry. An empty result means that the map knows of no incoming references.
It does not prove that scripts or unmapped text never use that ID.

## Change one field across rows

Choose **Fill column** and enter **Fill value**. Then choose the scope:

- **Fill shown rows** previews the rows admitted by the current filter.
- **Fill selected rows** appears when at least two rows are selected. Use
  **Ctrl+click** or **Ctrl+Space** on a focused row to toggle it, or **Shift+click**
  to select a range. Selected rows keep their identity through filtering and
  sorting. Hidden selected rows remain targets; the selection message counts them.

Read the preview's changed, unchanged and refused counts. For a selected fill,
confirm its target count, including hidden rows. Use **Earlier refused rows** or
**More refused rows** when a refusal list has several pages. Correct the input or
selection and preview again if needed. Changing the document or selection can
invalidate a selected preview.

Choose **Apply** to run the whole-document checks and commit the batch, or
**Cancel** to discard the preview. A refused batch changes no rows. One document
undo restores a successful fill. Save the file when you want to keep the result.
The [bulk editing recipe](bulk_edit_recipe.md) gives a longer worked example.

## Find and replace whole values across files

1. Leave any active text field and press **Ctrl+H** to open **Find and replace**.
2. In **Find value** and **Replace with**, enter JSON scalar values: quoted text,
   a number, `true`, `false` or `null`. For example, find `"draft"` and replace it
   with `"ready"`. This matches whole values; it does not replace part of a string.
   Object and array values are not accepted. Field names and the root `$schema`
   declaration stay unchanged.
3. Choose **Preview matches**. Review each file, JSON pointer and old/new value.
   Editable matches start chosen; uncheck any you want to keep. Read-only matches
   show their reason and cannot be chosen. Scene documents and unreadable files
   are reported as unsearched. More than 5,000 matches requires a more specific
   value instead of applying a truncated result.
4. Choose **Replace chosen**. Stop Play first. BED checks every chosen file's
   proposed data against its schema before applying any replacement. Changed
   documents or saved files require a fresh preview. Changing either query value
   also clears the preview and choices.
5. Read the result count. A later host-policy refusal can stop the operation after
   earlier files were changed; the message reports how many changed. There is
   **one undo step per file**, so visit each changed file to undo its replacement.
   The operation changes drafts: save each file whose changes you want to keep.

Use **Close find and replace** or **Escape** in the panel to close it. Closing the
panel does not undo edits already applied. See [troubleshooting](troubleshooting.md)
for save and project problems, or [checks and lifetime](check_threads.md) if you
are writing an adapter's checks.

## Readable tables and saving several files

Tables size their columns to fit their headings and contents when a file opens.
You can still resize, reorder and sort columns; BED remembers your choice for each
file in your personal settings. Very wide data scrolls sideways.

A refused edit shows its reason next to the field until you correct the value.

**Save all** saves every changed file, including levels, in one go. It waits for
any background checks to finish, and if one file is refused, nothing is saved and
every draft is kept. When you close a project with several changed files, BED lists
each one with its own **Save** and **Don't Save** buttons, plus **Save all** and
**Cancel**.
