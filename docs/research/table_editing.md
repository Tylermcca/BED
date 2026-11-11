# Table editing for data: reference evidence and a first-view proposal

Research snapshot: 2026-10-31, A029 revision 1. This is a documentation study, not an editor trial or benchmark. The proposed view is a hybrid: typed record editing from engine inspectors, range operations from spreadsheets, nested-data navigation from structured databases, and this library's validated command session.

**Scope and provenance.** BORROWED denotes a named reference behavior; MIXED combines named sources with the existing library; INVENTED denotes a proposed local policy or acceptance target. No implementation, package, third-party fixture or private game content accompanies this paper. Public documentation is the evidence. The only local inputs are the readiness/architecture/goals/usability documents, current data-editing contracts and installed ImGui XML.

**Read the evidence boundaries.** “Unknown” below means the cited source set does not establish that behavior, not that the product lacks it. We did not derive a thousand-row performance promise from a screenshot, a row limit, paging, or caching. Epic pages currently identify Unreal 5.8; Unity findings use 6000.0, plus versioned Odin notes; Godot links are its moving stable documentation. CastleDB findings concern the legacy standalone editor/manual, whose README points to HIDE. Depot findings combine its current README, 2021 wiki and explicitly named 1.6.0 release notes. Older documentation is not asserted to describe every current release.

**Licence correction.** The card calls CastleDB MIT. The official repository identifies ISC and its LICENSE contains ISC permission text. Treat CastleDB as ISC for this research, subject to any future dependency review. Nothing is copied into the library. [CA01] [CA02]

## How each does it

### Unreal Data Tables

| Aspect | Documented behavior or limit |
|---|---|
| Rows / columns | A Data Table row is a row-struct instance, keyed by Name; columns map to struct properties. The editor tutorial shows column headings plus a selected-row Row Editor. [UE01] [UE02] |
| Nested data | The tutorial's nested Item Text struct is edited in the row/property workflow. Array-in-cell behavior and arbitrary nested-table navigation are unknown here. [UE02] |
| Row references | FDataTableRowHandle carries a table pointer and RowName. Reference identity is table plus key, not the current visual row number. [UE03] |
| Multi-cell / fill / paste | CSV import/reimport provides external bulk authoring. Native rectangular paste and fill-down semantics are unknown in these docs. [UE01] [UE02] |
| Undo | The reviewed row-editor and utility docs do not establish gesture-level undo coverage or bulk-edit grouping. General engine transactions are not proof of a particular table interaction. [UE02] [UE05] |
| Cell validation | FTableRowBase supplies IsDataValid; the validation system reports through Message Log/CI. Per-cell error badges are unknown. [UE04] [UE06] |
| Sort / filter | Native view-sort/filter behavior and its effect on stored order are unknown. An editing utility such as MoveRow is not evidence of a view sort. [UE05] |
| Frozen columns | No pin/freeze contract established in this source set. [UE02] [UE05] |
| Thousands of rows | Utilities cache editable row/column copies. No latency/row-count benchmark established. [UE05] |

**BORROWED lesson:** retain typed columns and a row-detail route. **MIXED lesson:** combine these with spreadsheet ranges rather than assuming Unreal supplies the exact grid interaction we need.

### Unity ScriptableObject inspectors, with Odin TableList as a separate add-on

| Aspect | Documented behavior or limit |
|---|---|
| Rows / columns | A stock ScriptableObject is an asset inspected through properties, not automatically a database sheet. Odin TableList explicitly presents lists/arrays as tables and members as columns. These are distinct surfaces. [UN01] [UN04] [UN05] |
| Nested data | Unity 6000.0 serialization supports serializable class/struct fields but requires wrappers or callbacks for nested container types. Odin's table page does not promise arbitrary recursive grid flattening. [UN03] [UN04] |
| Row references | ScriptableObjects can be assigned as asset references in Inspector fields. That is object/asset identity, not a built-in table-row string-id relationship. [UN01] |
| Multi-cell / fill / paste | SerializedObject/SerializedProperty supports multi-object property editing. This is not evidence of rectangular clipboard paste or series fill in TableList; those remain unknown. [UN02] [UN04] |
| Undo | Unity recommends the serialized-property path because it handles multi-object edits and undo. Add-on-specific rectangle transaction boundaries are unknown. [UN02] [UN04] |
| Cell validation | The examined TableList documentation establishes drawing, not a general cell-local schema validation/rejection contract. Such a contract is unknown here. [UN04] |
| Sort / filter | No stock ScriptableObject or TableList sort/filter contract established from this source set. [UN01] [UN04] |
| Frozen columns | Odin's version 2.0.20 notes document fixed column headers in scroll views. That does not establish a pinned first identity column. [UN05] |
| Thousands of rows | Odin documents paging and scroll-view options; neither supplies measured thousand-row responsiveness. No benchmark established. [UN04] [UN05] |

**MIXED lesson:** reuse property descriptions and typed widgets in a comparison surface. Preserve the session edit boundary instead of treating a table renderer as validation infrastructure.

### Godot resource editors

| Aspect | Documented behavior or limit |
|---|---|
| Rows / columns | The Inspector lists properties of one selected object/resource, grouped by class. This source describes a property editor rather than a multi-record spreadsheet. [GO01] |
| Nested data | Resources can contain resources; Inspector expands sub-resources. Exported arrays are supported; the current docs explicitly exclude fully nested typed arrays such as Array[Array[float]]. [GO01] [GO02] [GO04] |
| Row references | Resources are shared data containers loaded by reference; fields can hold other resources. No generic sheet-row-id resolver is established by this model. [GO02] |
| Multi-cell / fill / paste | Inspector documents property copy/paste and whole-property copying; exported resource arrays can accept multiple dragged files. Rectangular grid fill/paste remains unknown. [GO01] [GO04] |
| Undo | EditorUndoRedoManager groups actions into scene histories or global history for external resources; custom tools must use its action path. [GO03] |
| Cell validation | Export annotations supply typed controls/range hints and tooltip descriptions. The reviewed docs establish no spreadsheet cell-error system or cross-row validation contract. [GO04] |
| Sort / filter | Inspector search filters displayed properties; this is not row sorting. Row-sort semantics are unknown. [GO01] |
| Frozen columns | Favorited properties can stay at the top of Inspector. A frozen table column is not established. [GO01] |
| Thousands of rows | No measured multi-resource grid workload established. Shared resource loading alone proves nothing about table redraw latency. [GO01] [GO02] |

**BORROWED lesson:** keep nested detail and descriptions close to a scalar field. **MIXED lesson:** put those behind a spreadsheet-style comparison view without flattening every child into a permanently huge grid.

### Excel

| Aspect | Documented behavior or limit |
|---|---|
| Rows / columns | Worksheets expose rectangular cells; ranges can be sorted, filtered and copied. Columns need not have one enforced type unless rules are supplied. [EX02] [EX03] [EX06] |
| Nested data | The cited range tools do not establish JSON child-list semantics. Mapping nested records into separate sheets would be an adapter/design choice, not a native guarantee from these pages. [EX02] [EX03] |
| Row references | Cell/formula references are part of the spreadsheet model. A domain row-id foreign-key resolver is not established by these range operations. [EX02] |
| Multi-cell / fill / paste | Range copy includes values/formulas and formatting by default; fill uses a handle and seed cells for repetition or series. This is broader than a plain JSON-value paste. [EX01] [EX02] |
| Undo | Specifications list 100 undo levels; this is not a guarantee that every macro/import action participates. [EX07] |
| Cell validation | Data Validation can restrict numbers/list choices and show input prompts/error alerts. Do not equate this entry UI with whole-document semantic validation. [EX06] |
| Sort / filter | Numeric/text/multi-column sort and additive filters are documented. Filters hide rows; they do not mean those records cease to exist. [EX03] [EX04] |
| Frozen columns | Freeze Panes retains rows above and columns to the left of the selected cell. [EX05] |
| Thousands of rows | Published capacity is 1,048,576 rows by 16,384 columns per worksheet. Capacity is not an interactive latency result for a particular document. [EX07] |

**BORROWED lesson:** comparisons need visible row identity, numeric sorting, range selection and fill. **INVENTED restriction:** our first view pastes typed values only; it does not implement formulas, formatting or automatic date conversion.

### Google Sheets

| Aspect | Documented behavior or limit |
|---|---|
| Rows / columns | Sheet ranges support cell selection, row/column selection and range operations. [GS02] [GS03] |
| Nested data | The examined sheet docs do not establish JSON child lists as first-class cells. A nested editor would be an additional mapping. [GS02] [GS03] |
| Row references | These docs describe spreadsheet ranges and formula references, not a game-data foreign-key system. [GS03] |
| Multi-cell / fill / paste | Autofill continues number/date series or repeats values. Keyboard routes include fill range/down/right, paste and paste-values-only. [GS01] [GS02] |
| Undo | Undo and redo shortcuts are documented. Exact transaction grouping for every external import is not established. [GS02] |
| Cell validation | Dropdowns can use a range or explicit values; invalid input can be rejected or shown as a warning. This is a configurable distinction. [GS05] |
| Sort / filter | Alphabetical/numeric sorting and filters are documented; filter views can apply to the viewer's own view. [GS03] |
| Frozen columns | Rows and columns can be frozen to stay visible during scrolling. [GS04] |
| Thousands of rows | No task-specific thousand-row latency evidence from these help pages. Feature availability is not a benchmark. [GS01] [GS03] |

**MIXED lesson:** use view-only filters with an explicit visible/total count, and keyboard range operations through the same command batches as pointer operations.

### CastleDB

| Aspect | Documented behavior or limit |
|---|---|
| Rows / columns | Sheets have data models; model and rows live in JSON. Column types guide input. [CA01] |
| Nested data | List columns create hidden child sheets whose data belongs to each parent row; expand to edit. [CA03] |
| Row references | References retain identifiers while display fields provide readable labels. F4 follows a target; F3 finds incoming references. [CA03] |
| Multi-cell / fill / paste | Shift selects ranges; cut/copy/paste works across rows/columns with documented conversion. Series/fill semantics are unknown. [CA03] |
| Undo | Ctrl+Z/Y documented; exact action grouping unknown. [CA03] |
| Cell validation | Models validate/guide input; exact cell-error presentation unknown. [CA01] [CA03] |
| Sort / filter | Ctrl+F quick filter documented; sorting unknown. [CA03] |
| Frozen columns | Unverified. [CA03] |
| Thousands of rows | No measured row-count/latency result established. [CA01] [CA03] |

**MIXED lesson:** combine readable reference navigation and nested sheets with spreadsheet range edits and our stricter atomic validation boundary. The legacy editor is evidence of interactions, not a dependency recommendation.

### Depot

| Aspect | Documented behavior or limit |
|---|---|
| Rows / columns | A VS Code custom editor presents typed JSON sheets, lines and columns. [DE01] [DE02] |
| Nested data | Lists are nested sheets; Properties holds an object; Grid holds typed positions. [DE02] |
| Row references | Lines/sheets use GUID references; readable IDs provide display names. [DE02] [DE04] |
| Multi-cell / fill / paste | Version 1.6.0 recursively duplicates lines/list content with new GUIDs. Rectangle paste/fill is not established. [DE03] |
| Undo | Not established by reviewed docs; VS Code hosting is insufficient evidence. [DE01] [DE02] [DE03] |
| Cell validation | Duplicate-ID warnings and missing-target errors are documented. The 2021 column page says numeric min/max are unenforced. Another comparison page claims ID uniqueness enforcement: keep that inconsistency visible, not a hard-rejection promise. [DE01] [DE02] [DE04] |
| Sort / filter | Not established in this source set. [DE01] [DE02] [DE03] |
| Frozen columns | Unverified. [DE01] [DE02] |
| Thousands of rows | 1.6.0 notes optimizations after a roughly 30,000-line JSON file became slow. Lines of JSON are not table rows; no transferable latency benchmark. [DE03] |

**MIXED lesson:** readable names plus explicit target identity, and nested editing. Do not import Depot's persistence schema or claim its documented constraints are enforced.

## What tuning needs

These are general operations derived from [the readiness table's SD bundle](../plan/readiness.md), S06-S12 and S24. This paper copies no source records, stats or private workbook content. The readiness percentages are a historical audit; a table alone does not satisfy a native adapter, registries, units, runtime export or project-opening workflow.

| Designer operation | Readiness demand | Table consequence |
|---|---|---|
| Compare many items' numbers | S06 price bounds, S07 capacity/requirements, S10 handling, S11 response/power/heat | Put related scalar columns side by side, retain units from an adapter, pin identity, compare numeric values rather than formatted strings. |
| Edit a column across rows | S09 grades/compatibility; S10-S12 repeated tuning/policy values | Select explicit rows/cells; set/fill one typed value or paste a rectangle; show affected count and commit one atomic batch. |
| Find outliers | S06-S12 scalar tuning and S24 catalogue checks | Numeric sort, value/range filtering and issue-only filtering; indicate hidden rows and let a diagnostic reveal its target. No automatic numerical balance advice. |
| Follow an id reference | S08 production inputs/outputs, S09 compatibility/rules, S24 cross-file references | Show stored id and readable label where a resolver exists; navigate to one known target, distinguish missing from unloaded/ambiguous. |
| Edit nested collections | S08 recipe inputs/outputs and S09 rule lists | Summary cell with child count and explicit detail route; avoid stuffing child lists into one string cell. |
| Review/save a safe change | S24 validation and SD source-preserving save | Show candidate errors, retain old document after refusal, undo a batch in one step, save through the existing session. |

These priorities extend the [architecture research](editor_architecture.md), [goals 4, 11 and 12](../goals.md) and [usability profile](../spec/usability_profile.md). They do not define an integration approval or new schema.

## Proposal

### Existing seams and ImGui availability

The current [DataEditing model](../../src/Bed.Editor/DataEditing.cs) exposes schema-derived InspectorRows and EditCommands for object pointers. It uses top-level section/list items, readable item labels, property title/key labels, description/default, and typed scalar controls; object/list values are summaries. It does not supply arbitrary foreign-key meaning for a string. A JSON Schema $ref references a schema, not a data row.

[DataDocument/DataKit](../../src/Bed.Data/DataDocument.cs) already has SetValue, InsertItem, RemoveItem and MoveItem. [DocumentSession.ExecuteBatch](../../src/Bed/DocumentSession.cs) applies commands against a candidate, validates the final candidate and records one batch undo step; any refusal leaves the live state/history/journal unchanged. This permits a coupled bound change whose intermediate values would be invalid but whose final document is valid. No new setter/interface is needed.

**Checked locally:** Ahwoo.Brutal.ImGui 2026.10.2's `lib/net10.0/Brutal.ImGui.xml` exposes BeginTable/EndTable, TableSetupColumn, TableHeadersRow, TableNextRow, TableSetColumnIndex, TableSetupScrollFreeze, TableGetSortSpecs, Sortable/Reorderable/ScrollX/ScrollY flags and ImGuiListClipper Begin/Step. Its documented native source is [v1.92.2-docking-brutal](https://github.com/RocketWerkz/imgui/blob/v1.92.2-docking-brutal/imgui.h). XML verifies available bindings, not a running table.

The installed XML says TableSetupScrollFreeze locks rows/columns during scrolling. TableGetSortSpecs returns sort descriptions; the caller sorts and clears SpecsDirty. Its pointer must not survive the frame/subsequent BeginTable. Tables do not implement document commands, filtering, rectangle selection, clipboard parsing or validated undo for us. TableSetColumnIndex reports visibility; the clipper provides DisplayStart/DisplayEnd. Those are rendering mechanisms, not proof that whole-file edits are fast.

### First slice, in priority order

All rows below are design recommendations; this card did not implement or certify them.

| Priority | Feature and provenance | Boundary / acceptance |
|---|---|---|
| P0.1 | **MIXED — Unreal typed rows + Odin members-as-columns + existing DataEditing.** One selected top-level list of objects becomes a table; scalar properties become schema-described columns. Pin a readable identity column and header (**BORROWED — Excel/Sheets freeze**). Keep inspector detail. | Reuse labels/help/defaults and choices; show absent values distinctly from null/empty string. For heterogeneous item schemas, resolve each cell at its actual pointer, rather than treating the first row's schema as universal. Unsupported/control-ambiguous cells are summaries/read-only with a reason. |
| P0.2 | **MIXED — Unity serialized-property edit discipline + current immutable session.** Enter edits one cell; accept creates SetValue (or commands already returned by the data binding), cancel discards its text buffer. | No mutation on selection or every keystroke. Keep exact numeric type/precision; refuse conversion errors visibly. Leave missing-property removal distinct from a JSON null value. Schema validation governs commit. |
| P0.3 | **MIXED — Excel/Sheets comparisons + view state from the current editor.** Numeric/text ascending/descending sort, text search, numeric range and issues-only filter; show visible/total counts. | Sort a view index, never call MoveItem for a header click. Missing/null/type-mismatch ordering is explicit and deterministic. Editing must target the underlying item/property, not display index. A filter is not a document edit. |
| P0.4 | **MIXED — CastleDB/Sheets ranges + ExecuteBatch.** Explicit rectangular selection, set selected cells, repeat a value down a selected column, and plain-value TSV paste with a preview. | Enumerate exact target pointers at the current revision, parse/type-check all cells, show count/changed cells and errors, then one ExecuteBatch. Invalid shape/type/limit or validator error applies nothing. One undo restores all values, including mixed originals. No new per-cell undo spam. |
| P0.5 | **MIXED — CastleDB follow-reference + Unreal table/key handles + existing link/adapter seams.** Follow a known id to its detail view, with a return route. | Only where a supplied resolver defines identity. Unknown string fields remain strings; do not infer by name or invent a universal x-ref schema. Duplicate/unloaded/missing targets show distinct reasons. Cross-file navigation is read-only; multi-document atomic edits wait. |
| P0.6 | **MIXED — Godot/Unreal details + CastleDB nested sheets.** Object/list summary opens a focused detail route with parent breadcrumb. | No flattening every array into columns. Structural edits use InsertItem/RemoveItem/MoveItem and invalidate stale cell selections; no drag-only requirement. |
| P0.7 | **MIXED — spreadsheet keyboard routes + this project's usability contract.** Arrow/Tab/Shift+Tab navigation, Enter/F2 edit, Escape cancel, keyboard range extension, copy/paste/fill commands discoverable in menus/palette. | Existing user shortcut overrides win. Text undo stays inside the text field; document undo runs only when it owns the action. A child popup consumes cancellation before global focus-out. Every exposed pointer action has a keyboard route. |
| P0.8 | **INVENTED — explicit performance acceptance before increasing advertised size.** Fixed-height rows, visible-cell rendering, clipper, cached row map/schema descriptions/diagnostic lookup keyed by document revision. | Generated 1,000- and 10,000-row self-authored cases, 20 scalar fields plus nested summaries. Measure first-open, idle-frame p50/p95, filter/sort, one edit, paste, full validation, save/reload and undo/replay; report allocations and machine/build. Proposed 10,000-row idle p95 under 16.7 ms is a target to test, not a measured claim. |

**INVENTED paste policy for the first slice:** accept an unambiguous rectangular TSV value grid only, with invariant numeric dots and explicit booleans/string enums. Preserve string IDs including leading zeros. Reject ambiguous locale numbers, irregular row widths, embedded tabs/newlines requiring an unimplemented quoting dialect, oversized payloads and incompatible destinations with a useful message. The preview identifies whether targets are the current visible order; freeze that exact target set until commit. Do not spill into filtered-out rows or silently grow arrays. Never execute clipboard formulas. Empty text is not automatically “delete property”; offer explicit reset/remove actions separately. A no-op batch should be omitted.

**INVENTED stale-target policy:** capture the document revision and underlying item pointers when editing/pasting starts. If a structural change, external reload or another document edit invalidates that mapping, cancel/rebuild with a clear explanation before submitting. Display sorting/filtering never changes stored JSON list order. A data id is not necessarily permanent or unique; do not upgrade it to GUID identity without an existing adapter contract.

### Later, after the first workflow works

- **BORROWED — Excel/Sheets:** drag fill handles, inferred number/date series, arithmetic column transforms, rich clipboard dialects, formulas and computed columns. These need explicit type/unit/candidate previews; they are not needed to repeat one literal value.
- **MIXED — CastleDB/Depot + adapter contracts:** reference pickers with large registries, backlinks, safe rename and cross-file refactoring. Existing resolver rules must settle scope and atomicity first.
- **MIXED — Godot nested resources + CastleDB children:** full nested table workspaces and arbitrary-depth trees. The first detail route must already be safe.
- **INVENTED:** asynchronous indexing/validation and larger-file editing only after profiling. Do not bypass the validator, truncate diagnostics or silently save partially checked data for speed.
- **BORROWED — spreadsheet analysis:** conditional formatting, aggregates, charts and statistical outlier suggestions. First ship explicit numeric sorting/filtering with units and errors.
- **INVENTED:** persisted column presets and multi-column sort after stable identity, revision invalidation and keyboard tests. A first view can start with one sort key and deterministic ties.

## Risks

| Risk | Consequence and proposed control |
|---|---|
| Whole-file work hidden behind clipping | Rendering only visible rows does not reduce command, validation, journal or save cost. DataDocument.With currently constructs a mutable copy for a command; a many-cell batch can repeatedly copy large JSON. Profile separately from draw time. Keep safe size limits and refusals; a faster mutation API would require a separately approved contract. |
| Positional pointers after sort/delete | The wrong row can receive an edit. Use view-index-to-source mapping plus revision checks; retain immutable command targets only while valid. Test duplicate labels/ids and concurrent structural edits. |
| Nested/heterogeneous data | A giant flattened table loses type/context; first-row schema reuse can write the wrong type. Use per-cell schema resolution, summary cells and explicit details. No guessing missing types or refs. |
| Precision, locale and clipboard | Automatic dates, scientific notation and binary floating conversions can corrupt identifiers or numbers. Keep typed parsing, original JSON lexemes where unchanged, invariant input policy and preview. Never render-and-parse rounded display text as the edit oracle. |
| Hidden rows and batch scope | Fill/paste can surprise authors. Display exact target count/selection, show active filters, preview and freeze target mapping; one validation/batch/undo. |
| Issues outside the current view | A refused batch can concern another field/row. Preserve all issue paths and provide reveal/clear-filter navigation rather than only colouring the edited cell. The final validator decides cross-field consistency. |
| Keyboard-only operation | ImGui tables/navigation flags do not create spreadsheet range semantics. Implement and test traversal, edit/cancel, extend, paste/fill, sorting/filtering, nested return and issue navigation with real input through HeadlessHost. Check focus after clipping, scrolling and popup close. |
| Accessibility and backend limits | Follow the existing usability profile's scaling, contrast, visible focus, key ownership, long/non-ASCII labels and locale/IME requirements. Colour cannot be the sole error cue; show text/path. Headless input does not establish screen-reader, IME or real OS clipboard support. No new GUI/OS tests ran for this card. |

**INVENTED verification cases before claiming the first view works:** same batch through UI and headless commands yields identical JSON/journal; second invalid cell refuses the whole paste; a jointly valid two-bound edit commits as one batch; undo/replay preserve source style; sort/filter never dirty or reorder the file; a source-row insertion cannot redirect an active edit; null/absent/empty string and Unicode labels stay distinct; numeric 2 sorts before 10; hidden rows are untouched; keyboard cancellation cannot trigger global focus-out. These extend the existing contracts; they are proposed checks, not passed tests.

## Self-check

Fixed population: the 34 external citations in the registry below, in its printed order. Draw: Python random.Random(290031).sample(range(34), 20), sorted; seed named before the re-open pass. This exceeds the card's ten-citation request and meets the standing twenty-item rule.

All 20 selected primary pages were re-opened via find/open, the excerpt re-read and its associated claim judged. HTML inline markup was read as rendered text; quotes are short and each source contributes fewer than 25 quoted words in this paper. Two quote-boundary fixes were made before hand-in: EX01 omits an image-separated period and EX03 omits a punctuation guess. The same selected claims still pass; no claim/sample was removed or replaced.

| Citation | Re-opened excerpt | Judgment |
|---|---|---|
| [UE01] | “a single row maps 1:1 directly to the structure inherited from” | PASS — typed struct rows. |
| [UE02] | “In the Row Editor panel, enter the same values” | PASS — selected-row editing. |
| [UE05] | “parses out editable copies of rows and columns” | PASS — cached editing representation, no latency claim. |
| [UN01] | “you can drag or pick instances of them into fields in the Inspector” | PASS — asset references. |
| [UN04] | “Renders lists and arrays in the inspector as tables.” | PASS — table addon. |
| [GO02] | “Resources are data containers.” | PASS — nested/shared resource model. |
| [GO04] | “Nested typed arrays such as” | PASS — nested typed arrays caveat. |
| [EX01] | “Drag the fill handle” | PASS — fill handle. |
| [EX02] | “including formulas and their resulting values, comments, cell formats, and hidden cells” | PASS — copy scope. |
| [EX03] | “Filters are additive” | PASS — composable filters. |
| [EX04] | “numbers (smallest to largest or largest to smallest)” | PASS — numeric sorting. |
| [EX07] | “Undo levels” | PASS — 100 undo levels; limits are not timings. |
| [GS02] | “Paste values only” | PASS — range fill, paste, undo shortcuts. |
| [GS03] | “only applies to your view of the spreadsheet.” | PASS — filter views. |
| [GS04] | “you can freeze rows or columns.” | PASS — freeze. |
| [CA01] | “each sheet has a data model” | PASS — typed sheets; legacy editor. |
| [CA03] | “Use Ctrl+Z and Ctrl+Y to Undo/Redo your changes” | PASS — undo; primary manual fallback. |
| [DE01] | “edit JSON data like a spreadsheet” | PASS — typed JSON sheets. |
| [DE02] | “not yet enforced by Depot” | PASS — min/max caveat. |
| [DE04] | “Sheets, Lines, and Columns all have their own GUIDs.” | PASS — identity. |

All 20 passed against the scoped claims. Mandatory additional checks: official CastleDB LICENSE is ISC (not the card's MIT); Depot's “30,000” is JSON lines, not table rows; installed XML confirms the required ImGui members; local ExecuteBatch validates the final candidate and groups undo. Unknown feature/performance cells remain unknown. No source alone supplies the proposed system.

### Citation registry

Links resolve to the publisher/maintainer, not a search page. Research observations and quotes above are limited to these public documentation pages; no implementation was copied.

- **UE01** — [Epic data-driven tables](https://dev.epicgames.com/documentation/en-us/unreal-engine/data-driven-gameplay-elements-in-unreal-engine).
- **UE02** — [Epic row editor tutorial](https://dev.epicgames.com/documentation/en-us/unreal-engine/coder-05-manage-item-and-data-in-an-unreal-engine-game).
- **UE03** — [Epic row handle](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/FDataTableRowHandle).
- **UE04** — [Epic data validation](https://dev.epicgames.com/documentation/en-us/unreal-engine/data-validation-in-unreal-engine).
- **UE05** — [Epic table editing utilities](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Editor/UnrealEd/FDataTableEditorUtils).
- **UE06** — [Epic row validation hook](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/FTableRowBase).
- **UN01** — [Unity 6000.0 ScriptableObject](https://docs.unity.com/en-us/engine/6000.0/manual/scripting/get-started/fundamental-unity-types/class-scriptable-object).
- **UN02** — [Unity 6000.0 Editor](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/editor).
- **UN03** — [Unity 6000.0 serialization](https://docs.unity.com/en-us/engine/6000.0/manual/programming-environment/code-reload-serialization/script-serialization/rules).
- **UN04** — [Odin TableList](https://odininspector.com/attributes/table-list-attribute).
- **UN05** — [Odin 2.0.20 patch notes](https://odininspector.com/patch-notes/2-0-20-0).
- **GO01** — [Godot stable Inspector Dock](https://docs.godotengine.org/en/stable/tutorials/editor/inspector_dock.html).
- **GO02** — [Godot stable Resources](https://docs.godotengine.org/en/stable/tutorials/scripting/resources.html).
- **GO03** — [Godot stable EditorUndoRedoManager](https://docs.godotengine.org/en/stable/classes/class_editorundoredomanager.html).
- **GO04** — [Godot stable exports](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/gdscript_exports.html).
- **EX01** — [Excel fill](https://support.microsoft.com/en-us/excel/get-started/fill-data-automatically-in-worksheet-cells).
- **EX02** — [Excel copy](https://support.microsoft.com/en-gb/office/move-or-copy-cells-rows-and-columns-3ebbcafd-8566-42d8-8023-a2ec62746cfc).
- **EX03** — [Excel filter](https://support.microsoft.com/en-us/excel/get-started/filter-data-in-a-range-or-table-in-excel).
- **EX04** — [Excel sort](https://support.microsoft.com/en-us/excel/sort-data-in-a-range-or-table-in-excel).
- **EX05** — [Excel freeze](https://support.microsoft.com/en-us/excel/get-started/freeze-panes-to-lock-rows-and-columns).
- **EX06** — [Excel validation](https://support.microsoft.com/en-us/excel/get-started/apply-data-validation-to-cells).
- **EX07** — [Excel specifications](https://support.microsoft.com/en-us/excel/excel-specifications-and-limits).
- **GS01** — [Sheets autofill](https://support.google.com/docs/answer/75509?hl=en).
- **GS02** — [Sheets shortcuts](https://support.google.com/docs/answer/181110?hl=en).
- **GS03** — [Sheets sort/filter](https://support.google.com/docs/answer/3540681?hl=en).
- **GS04** — [Sheets freeze](https://support.google.com/docs/answer/9060449?hl=en).
- **GS05** — [Sheets dropdown validation](https://support.google.com/docs/answer/186103?hl=en).
- **CA01** — [CastleDB README](https://github.com/ncannasse/castle).
- **CA02** — [CastleDB licence](https://github.com/ncannasse/castle/blob/master/LICENSE).
- **CA03** — [CastleDB first-party manual HTML](https://github.com/ncannasse/castle/blob/master/www/index.html).
- **DE01** — [Depot README](https://github.com/afterschoolstudio/Depot).
- **DE02** — [Depot columns](https://github.com/afterschoolstudio/Depot/wiki/Depot-Column-Information).
- **DE03** — [Depot changelog](https://github.com/afterschoolstudio/Depot/blob/master/CHANGELOG.md).
- **DE04** — [Depot comparison](https://github.com/afterschoolstudio/Depot/wiki/Depot-vs.-CastleDB).

[UE01]: https://dev.epicgames.com/documentation/en-us/unreal-engine/data-driven-gameplay-elements-in-unreal-engine
[UE02]: https://dev.epicgames.com/documentation/en-us/unreal-engine/coder-05-manage-item-and-data-in-an-unreal-engine-game
[UE03]: https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/FDataTableRowHandle
[UE04]: https://dev.epicgames.com/documentation/en-us/unreal-engine/data-validation-in-unreal-engine
[UE05]: https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Editor/UnrealEd/FDataTableEditorUtils
[UE06]: https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/FTableRowBase
[UN01]: https://docs.unity.com/en-us/engine/6000.0/manual/scripting/get-started/fundamental-unity-types/class-scriptable-object
[UN02]: https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/editor
[UN03]: https://docs.unity.com/en-us/engine/6000.0/manual/programming-environment/code-reload-serialization/script-serialization/rules
[UN04]: https://odininspector.com/attributes/table-list-attribute
[UN05]: https://odininspector.com/patch-notes/2-0-20-0
[GO01]: https://docs.godotengine.org/en/stable/tutorials/editor/inspector_dock.html
[GO02]: https://docs.godotengine.org/en/stable/tutorials/scripting/resources.html
[GO03]: https://docs.godotengine.org/en/stable/classes/class_editorundoredomanager.html
[GO04]: https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/gdscript_exports.html
[EX01]: https://support.microsoft.com/en-us/excel/get-started/fill-data-automatically-in-worksheet-cells
[EX02]: https://support.microsoft.com/en-gb/office/move-or-copy-cells-rows-and-columns-3ebbcafd-8566-42d8-8023-a2ec62746cfc
[EX03]: https://support.microsoft.com/en-us/excel/get-started/filter-data-in-a-range-or-table-in-excel
[EX04]: https://support.microsoft.com/en-us/excel/sort-data-in-a-range-or-table-in-excel
[EX05]: https://support.microsoft.com/en-us/excel/get-started/freeze-panes-to-lock-rows-and-columns
[EX06]: https://support.microsoft.com/en-us/excel/get-started/apply-data-validation-to-cells
[EX07]: https://support.microsoft.com/en-us/excel/excel-specifications-and-limits
[GS01]: https://support.google.com/docs/answer/75509?hl=en
[GS02]: https://support.google.com/docs/answer/181110?hl=en
[GS03]: https://support.google.com/docs/answer/3540681?hl=en
[GS04]: https://support.google.com/docs/answer/9060449?hl=en
[GS05]: https://support.google.com/docs/answer/186103?hl=en
[CA01]: https://github.com/ncannasse/castle
[CA02]: https://github.com/ncannasse/castle/blob/master/LICENSE
[CA03]: https://github.com/ncannasse/castle/blob/master/www/index.html
[DE01]: https://github.com/afterschoolstudio/Depot
[DE02]: https://github.com/afterschoolstudio/Depot/wiki/Depot-Column-Information
[DE03]: https://github.com/afterschoolstudio/Depot/blob/master/CHANGELOG.md
[DE04]: https://github.com/afterschoolstudio/Depot/wiki/Depot-vs.-CastleDB
