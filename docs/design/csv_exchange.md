# CSV exchange: reviewed updates, not a spreadsheet engine

**Design proposal, not implemented.** The spreadsheet stays the authoring source; BED reviews explicit exports into its JSON draft. No automatic synchronization or workbook rewriting. MIXED: ordinary CSV quoting, schema-driven table edits and a new identity-bound exchange envelope. Decisions below need approval before implementation.

## Contract proposed for the first slice

- One file, one top-level object list, existing rows only. Choose a unique, required string key explicitly; never infer identity from display labels or sorted positions. Export all rows in document order, independent of the visible filter. Refuse absent/duplicate keys. Preserve document order on return; reject changed/unknown/duplicate keys. CSV row reordering changes no identity. Omitted rows remain untouched, never deleted.
- Export UTF-8 comma-separated CSV with a header, CRLF records, doubled quote escaping and quoted commas/newlines. Accept LF records and one initial UTF-8 BOM. A proposed companion manifest records format version, file/list/key, field types, schema/reference-map fingerprints and base document hash. Missing/mismatched manifest refuses; no guessed mapping. Check base again at Apply; concurrent edits require a fresh export and explicit reconciliation.
- Headers are exact top-level field names, not labels. Reordered headers are allowed; unknown/duplicate headers refuse. Missing key column refuses; other missing columns leave values unchanged. Nested objects/arrays are excluded, not flattened. Refuse a requested nested column rather than silently drop it; preserve excluded data.
- Strings retain whitespace and leading zeros; integers use exact signed 64-bit decimal, numbers finite invariant decimal accepted without lossy conversion, booleans exactly true/false. Enums use schema values, never translated labels. Refuse unsupported numeric precision and ambiguous/mixed types. References use stored keys and declared reference maps; changed unresolved/ambiguous references refuse. Without a required map, refuse reference editing, not pretend validation occurred.
- Explicit cell tokens distinguish absence: proposed `\missing` means leave unchanged, `\null` means JSON null only if allowed, `\empty` means empty string. Double an initial backslash for literal strings. An unmarked blank refuses rather than silently clear/default. No field deletion in this slice. Export absent fields with `\missing`; no-change reimport preserves them.
- Import spreadsheet columns as text to avoid date/id/precision coercion; BED cannot recover values a spreadsheet already changed. No formulas, macros, external links or workbook parsing. Refuse formula-triggering string cells (leading =, +, -, @ or control whitespace) on export/import; never silently prepend an apostrophe. This deliberately limits round-trip coverage; supported values, not arbitrary JSON, round-trip.

## One reviewed change

Parse without modifying anything. Review row key, field, before/after and changed/unchanged/refused counts. Every refusal names CSV record/column, key, raw value, expected rule and remedy; cross-field failures identify the candidate document path. Any refusal blocks the whole import. Cancel and a no-change import add no history. Apply rechecks base, edit permissions and full candidate validation, then makes one undoable draft change; never auto-save. Undo reverses the entire import, not selected rows. Report selected checks separately; save coverage remains unknown.

**Existing seams:** DataTableModel.PlanPaste is positional TSV, not CSV or key matching (source: lines 142–192). DataTableView.Apply (221–234) uses one SetValues command and current validation. A332's bulk recipe separates preview, Apply, checks and Save. A new parser/key resolver/stale-plan guard is required; no ready API is asserted. Reuse A343's preview only after its final contract lands; it is not a dependency here.

## Three manual examples (expected outcomes, not executions)

1. **FirstTask**, settings.json /presets: `id,rounds` then `practice,80`. Existing Practice is 60; proposed import changes one cell, Default stays 60. `practice,5` passes integer bounds but must refuse at Apply: reserve is 10. Adapter rules, FirstTaskAdapter.cs:56–57, also protect Default.
2. **StarterProject**, wares.json /wares: `id,price,kind` then `bread,4,food`. Price 3→4, valid enum unchanged. `bread,4,Food` refuses the whole import; allowed values are food/material/tool. Omitted name/fragile columns remain unchanged. Sources: data/wares.json and schemas/wares.schema.json.
3. **StarterProject**, recipes.json /recipes: `id,makes` then `bake,flour`. Stored bread→flour resolves in wares.json. `bake,missing` refuses; no guessing by display name. Unmentioned from/minutes stay unchanged. Sources: data/recipes.json, schemas/recipes.schema.json and reference_map.json.

## Proof required before shipping

Test unchanged export/reimport; Unicode, quotes/newlines/backslashes and all three tokens; exact integers/precision refusal; enum/reference cases above; reordered rows/columns; omitted rows/columns; duplicate/unknown keys/headers; nested and formula refusals. Assert zero draft/history/disk changes on every refusal/cancel/stale base, full cross-field validation at Apply, one Undo/Redo for success and no auto-save. Test real review input separately. Never support row creation/deletion, key renaming, multi-file transactions, formula evaluation or silent coercion in this first slice. No code, parser, workbook or runtime test was run for this design.
