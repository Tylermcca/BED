# One asset catalog and handoff contract

Status: design contract on main cba9bd1. No new interface, schema, importer, cache or runtime package is implemented by this document. Examples are synthetic. Existing source behavior is distinguished from proposed behavior below.

## Foundation and scope

**BORROWED:** stable sidecar identities and rebuildable derived results follow the Unity/Godot/Stride research in [editor architecture](../research/editor_architecture.md#5-assets-identity-and-import). **MIXED:** retain BED's existing records and scene commands, with one project-owned catalog shared by every consumer. **INVENTED:** the generation, publication and lease rules below are the proposed BED handoff contract.

Current source anchors:

- `src/Bed/Assets.cs:47-60`: AssetRecord holds GUID, source hash, importer/version, settings, provenance and AI-use policy. Its sidecar format version is 1. These are not a catalog generation.
- `Assets.cs:217-270`: AssetIndex scans sidecars, reports problems, indexes GUIDs and refuses duplicate GUID resolution. A changed source produces a warning but can still resolve to a path. A missing source is not indexed. Therefore a non-null Resolve result does not prove current imported content or permission to export.
- `src/Bed.Editor/AssetBrowser.cs:49-67`: Refresh builds an index, then reads sidecars again to construct entries. This is currently a browser model, not an atomic shared snapshot. Its entries include problematic sidecars that have no resolvable asset.
- `src/Bed/Scene.cs:13-25,91-110`: a scene asset is an optional string, documented as path or GUID. Null means an empty object. No automatic reference migration is present.
- `Scene.cs:226-233,238,294-301`: missing assets retain placeholders; AddObject and SetAsset are commands. Catalog publication must not silently mutate the scene or its undo history.

This contract adds no second asset database. Sidecars and source files remain authoritative. The catalog is a disposable projection plus rebuildable derived cache. It must not become an independently writable copy of provenance, settings or identities. No asset contents, historical import-security work or real project data were inspected for this design.

## Reference and identity

**MIXED.** New browser placement emits the existing 32-lowercase-hex GUID string into the existing scene asset field. No `asset://` prefix, version-2 scene format or sidecar rewrite is introduced. A move with the sidecar preserves identity; a copy that duplicates the GUID is ambiguous and resolves neither copy. A display name or path is never a replacement identity for an unresolved GUID.

For legacy scene strings, an explicit resolver binding chooses the existing project-relative path policy. It preserves the literal on read/save and never guesses a new GUID from a basename. A GUID-shaped string is treated as an identity, not retried as a path when missing. Any project whose legacy path collides with that representation needs an explicit migration decision before adopting this binding; do not silently reinterpret it. Null scene assets bypass resolution as empty objects.

Proposed in-memory reference: `(project identity, GUID)` or `(project identity, legacy path, legacy binding)`. Project identity is an admission-scoped token, not a new persisted field. References from two projects must never share a cache entry simply because their GUIDs match.

## Revision, generation and published snapshot

**INVENTED.** Separate three concepts:

1. **Catalog generation:** a monotonic counter within one project admission. It identifies one complete immutable published snapshot. A new admission gets a new identity token even if the counter restarts. No counter is written into a sidecar.
2. **Content revision:** a deterministic fingerprint of source content, normalized settings under a declared canonicalization version, importer identity/version, target/profile and the transitive dependency revisions. Paths/mtime are discovery hints, not proof of unchanged bytes. Unknown dependencies mean an incomplete revision, not a cache hit.
3. **Policy revision:** provenance, AI-use and relevant approval/policy inputs. Policy changes invalidate authorization decisions even when geometry/texture content is identical. Whole-record changes also advance the catalog generation so every consumer sees the updated explanation.

A proposed resolution contains reference, admission token, generation, content/policy revisions, state, diagnostics and an optional immutable derived-content lease. These are conceptual fields, not newly available C# APIs. Existing AssetRecord and Issue data remain the source of display information; never parse Issue message text to infer state.

A single project catalog service owns refresh/import completion and serializes publication. Browser entries and resolver results come from the same candidate snapshot, including diagnostic-only entries for missing or ambiguous sources. Replace the current double scan only in a separately assigned implementation; do not hide it behind an assertion that today's Refresh is atomic.

Build candidate results off to the side. Record every source, sidecar and declared dependency read; check that inputs still match before publishing. A changed input invalidates the candidate and triggers a bounded retry or a visible stale state, never a mixed partial publication. Filesystem notifications are hints, with an explicit refresh path for missed events. IO/parse failure publishes explicit diagnostics without calling the failed read a successful generation of content. Do not spin indefinitely during continuous external edits.

Cross-file atomicity is not provided by the filesystem. Export must pin verified bytes/derived artifacts for its operation, rather than reopen mutable source paths after validation and call that the same snapshot. This pinning and its disposal belong to future implementation, not an existing AssetIndex guarantee.

## States and last-good rules

**INVENTED**, preserving existing missing-placeholder behavior:

| State | Meaning | Consumer response |
|---|---|---|
| Valid | Unique identity, complete current content revision and successful required derivation | Use a lease from this snapshot; still check operation-specific policy. |
| Missing | No matching sidecar/source or unresolved legacy reference | Keep scene object/reference and show placeholder plus reason. Never bind another asset by name. |
| Ambiguous | Multiple records claim the same GUID | Resolve no content; show all conflicting locations. |
| Stale | Inputs/importer/settings/dependencies changed or publication is pending | Current desired revision is not ready. A retained last-good lease may be displayed only with an explicit stale badge. |
| Failed | Current derivation or metadata read failed | Preserve failure diagnostics. Last-good content remains separately identified, never relabeled Valid. |
| Not imported | Source has no identity record | Browser discovery item only, not a stable scene placement reference. |

State and permission are independent. Valid does not mean licensed for redistribution or allowed for AI. Missing provenance is not a missing mesh. Retain existing recorded policy and diagnostic detail; this document does not revise import acceptance rules or authorize reading restricted assets.

Last-good is a cache lease, not an overwrite of the desired revision. On failure the scene remains unchanged; preview may show last-good with its revision and warning, while new Play/export admission refuses stale/failed required content. A running Play session continues its pinned snapshot until an explicit supported reload succeeds; it must not receive half a new generation. If no last-good exists, use a placeholder. Resource eviction waits for all leases, including GPU work, to retire; the catalog does not own a graphics device or destroy a texture still used by a frame.

## One cache, several consumers

**MIXED.** One logical cache authority per admitted project serves all consumers. Preview thumbnails and backend-specific resources may be distinct derived entries, keyed by the same content revision plus representation/profile/device generation. That is not another identity database. Device loss invalidates device resources, not asset identities or authoritative sidecars. Bound memory and disk use; cached results are disposable and never the sole copy of user edits.

| Consumer | Required handoff | Must not do |
|---|---|---|
| Browser | Entries, diagnostic-only entries and badges from one snapshot; selection by identity | Rescan independently or turn a path rename into a new GUID. |
| Scene placement | Drag payload with admission token, GUID and observed generation/revision; re-resolve at drop, validate typed AddObject/SetAsset command | Mutate SceneObject directly, accept a payload from another project, or bypass undo. |
| Preview | Immutable content lease with explicit current/stale state; renderer owns its resource lifetime | Resolve a live path every frame or show last-good as current. |
| Play | Explicit manifest of required references/revisions and pinned content; refuse unresolved required assets before starting | Load editor assemblies solely to resolve references or silently swap generations mid-run. |
| Export | Same manifest closure, pinned artifacts, current policy decision and complete required dependencies | Ship stale cache entries, treat AI-use as redistribution permission, or silently omit failed required content. |

A drag from generation 12 dropped at generation 13 is re-resolved. If the identity and relevant content revision still match, unrelated catalog changes need not block the command. If content changed, refuse that stale placement with a refresh/retry explanation; don't silently substitute it. Existing placements remain valid references and become placeholders/stale previews as appropriate, without unsolicited scene commands.

Play's eventual pure reader receives the manifest/resolver contract, not AssetBrowser or editor state. Export uses the same references and content revisions but applies its own explicit distribution policy. Optional dependencies must be declared optional with a defined fallback; otherwise absence is a failure. The eventual dependency walker detects cycles and reports them deterministically, never recursively loops or omits edges to force a cache hit.

## Concrete contract examples

**INVENTED**, acceptance examples rather than executed tests:

- **Valid:** synthetic GUID `11111111111111111111111111111111` at `models/cube.mesh`, content revision R1, generation 7. Browser, scene and preview resolve that GUID in admission P to the same R1 lease. A path move with its sidecar publishes generation 8 and the same content revision; the scene string is unchanged.
- **Missing:** remove the source while retaining its sidecar. Generation 9 lists the problem, resolver returns Missing, scene object `cube` remains with its GUID. Export refuses this required reference. Restoring the exact source can make a later generation Valid without changing scene identity.
- **Stale:** settings change while R1 is displayed. Desired revision R2 is pending. Preview labels its R1 lease stale; new Play/export refuse until R2 and its dependencies succeed. A failed R2 import does not overwrite R1 or claim R2 passed.
- **Dependency:** material M depends on texture T1. A texture change yields T2 and invalidates M's revision even if M's own bytes are unchanged. An unrelated asset U does not invalidate M. A late worker completing M(T1) cannot publish it as M(T2).
- **Policy:** the recorded AI-use flag changes from allowed to forbidden with identical source bytes. Content may stay R1; policy revision and catalog generation change. Cached preview does not grant permission to send the bytes to an assistant.
- **Duplicate:** two sidecars carry the example GUID. Both remain visible with diagnostics; no lease resolves, regardless of directory enumeration order.
- **Project close:** a pending import from admission P completes after reopening as Q. Its completion is discarded, even if GUIDs and local generation numbers happen to match. Existing leased resources retire safely; no callback writes into Q.

## Future ownership and proof

**INVENTED** implementation split, not permission for concurrent edits:

1. Core owner: proposed `src/Bed/AssetCatalog.cs` holds immutable snapshot/reference/resolution contracts using existing AssetRecord/Issue concepts. No editor dependency or schema migration.
2. Asset owner: proposed `src/Bed.Editor/AssetGeneration.cs` owns input observation, revision/dependency calculation, candidate publication and leases; coordinate any necessary Assets.cs projection seam before changing it. No separate sidecar database.
3. Browser owner: `AssetBrowser.cs` becomes a projection of that snapshot instead of a second scan. Keep existing search/problem behavior and diagnostics.
4. Scene owner: existing scene command boundaries consume the resolved drag handoff; typed document host owns scene/session admission. No DataWorkspace behavior is changed here.
5. Preview/Play/export owners: consume leases/manifests and declare representation/dependency requirements. Their device/process lifetimes remain outside the catalog. Runtime package extraction is a separate task.

Before implementation lands, synthetic tests should prove valid/missing/stale/duplicate states; path move and legacy preservation; settings/importer/transitive dependency invalidation; policy-only invalidation; stable ordering; failed refresh and last-good separation; changed inputs during import; late completion after close/reopen; cross-project drag refusal; re-resolution at drop; pinned export bytes; and lease disposal after consumer release. Preserve existing asset and scene tests. No real assets, importer execution, build/export or performance measurement was performed for this document. Interface types and tests are intentionally deferred until the implementation owner selects the narrow public seam; an unimplemented interface would falsely suggest these snapshot guarantees already exist.
